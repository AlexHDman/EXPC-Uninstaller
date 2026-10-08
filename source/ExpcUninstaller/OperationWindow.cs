namespace ExpcUninstaller;

// The same production dialog can be exercised without showing it or touching installed products.
internal sealed class OperationServices
{
    public Func<AppEntry, CancellationToken, Task<OperationResult>> Uninstall { get; init; } = ProductActions.Uninstall;
    public Func<AppEntry, List<JunkItem>> Find { get; init; } = BcuTargetedJunkAdapter.Find;
    public Func<AppEntry, JunkItem, Task> Delete { get; init; } = BcuTargetedJunkAdapter.Delete;
    public Func<AppEntry, Task> RemoveRecord { get; init; } = RegistryBackup.RemoveRecord;
}

internal enum OperationStage { Confirm, Running, Result, Leftovers, ConfirmCleanup, Finished }

internal sealed class OperationWindow : Form
{
    private readonly AppEntry _app;
    private readonly int _action;
    private readonly OperationServices _services;
    private readonly Func<Task> _refresh;
    private readonly CancellationTokenSource _observation = new();
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 90, Padding = new Padding(8), AutoEllipsis = true };
    private readonly TextBox _journal = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false };
    private readonly DataGridView _items = new() { Dock = DockStyle.Fill, Visible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.None, EnableHeadersVisualStyles = false, EditMode = DataGridViewEditMode.EditProgrammatically };
    private readonly Button _primary = new() { AutoSize = true };
    private readonly Button _secondary = new() { AutoSize = true };
    private readonly Button _details = new() { Dock = DockStyle.Bottom, Height = 30, Text = L.T("OperationDetails") };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Image? _image;
    private List<JunkItem> _approved = [];
    private bool _canFind;
    private bool _waiting;
    internal OperationStage Stage { get; private set; } = OperationStage.Confirm;
    internal bool Running => Stage == OperationStage.Running;
    internal string Status => _status.Text;

    public OperationWindow(AppEntry app, int action, Func<Task> refresh, Image? image = null, OperationServices? services = null)
    {
        _app = app; _action = action; _refresh = refresh; _services = services ?? new();
        Text = UiText.Title; ClientSize = new Size(660, 360); MinimumSize = new Size(540, 320);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = true; MaximizeBox = false; TopMost = false;
        _image = image == null ? null : (Image)image.Clone();
        var header = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8) };
        header.Controls.Add(new Label { Text = app.Name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
        header.Controls.Add(new PictureBox { Image = _image, Dock = DockStyle.Left, Width = 40, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(4) });
        _items.Columns.Add(new DataGridViewCheckBoxColumn { Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        foreach (var key in new[] { "Operation22", "Operation23" }) _items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T(key), ReadOnly = true });
        void Toggle(int index)
        {
            if (Stage != OperationStage.Leftovers || index < 0) return;
            var cell = _items.Rows[index].Cells[0]; cell.Value = cell.Value is not true; UpdateButtons();
        }
        _items.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Left && e.ColumnIndex == 0) Toggle(e.RowIndex); };
        _items.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { e.Handled = e.SuppressKeyPress = true; Toggle(_items.CurrentRow?.Index ?? -1); } };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([_secondary, _primary]);
        Controls.Add(_items); Controls.Add(_journal); Controls.Add(_details); Controls.Add(_progress); Controls.Add(_status); Controls.Add(header); Controls.Add(buttons);
        _details.Click += (_, _) => _journal.Visible = !_journal.Visible;
        _primary.Click += async (_, _) => await Advance();
        _secondary.Click += (_, _) => Secondary();
        FormClosing += (_, e) => { if (Running) e.Cancel = true; };
        SetStatus(action switch { 0 => L.T(app.Store ? "Operation4" : "Operation5"), 1 => L.T("Operation6") + app.Name + L.T("Operation7"),
            2 => L.T("OperationFindPrompt"), 3 => L.T("Operation8") + app.Name + L.T("Operation9"), _ => throw new ArgumentOutOfRangeException(nameof(action)) });
        Theme.Apply(this); UpdateButtons();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        if (_journal.TextLength > 40000) _journal.Text = _journal.Text[^30000..];
    }
    private void UpdateButtons()
    {
        _progress.Visible = Running;
        _items.Enabled = Stage == OperationStage.Leftovers;
        _primary.Visible = Stage is OperationStage.Confirm or OperationStage.Leftovers or OperationStage.ConfirmCleanup || Stage == OperationStage.Result && _canFind;
        _primary.Enabled = !Running && (Stage != OperationStage.Leftovers || Selected().Count > 0);
        _primary.Text = Stage switch { OperationStage.Confirm => L.T(_action is 1 or 2 ? "OperationFind" : "OperationDelete"),
            OperationStage.Result => L.T("OperationFind"), OperationStage.ConfirmCleanup => L.T("OperationConfirmCleanup"), _ => L.T("Operation17") };
        _secondary.Text = Running && _waiting ? L.T("OperationStopObserving") : Stage is OperationStage.Confirm or OperationStage.ConfirmCleanup ? L.T("Cancel") : UiText.Close;
        _secondary.Enabled = !Running || _waiting && !_observation.IsCancellationRequested;
    }
    private List<JunkItem> Selected() => _items.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).Select(r => (JunkItem)r.Tag!).ToList();
    internal void Secondary()
    {
        if (Running)
        {
            if (!_waiting) return;
            _observation.Cancel(); SetStatus(L.T("OperationStopRequested")); UpdateButtons(); return;
        }
        if (Stage == OperationStage.ConfirmCleanup) { _approved.Clear(); Stage = OperationStage.Leftovers; SetStatus(L.T("OperationChoose")); UpdateButtons(); return; }
        Close();
    }
    internal async Task Advance()
    {
        if (Running || Stage == OperationStage.Finished || Stage == OperationStage.Result && !_canFind) return;
        if (Stage == OperationStage.Leftovers)
        {
            _approved = Selected(); if (_approved.Count == 0) return;
            Stage = OperationStage.ConfirmCleanup;
            SetStatus(L.T("Operation24") + _app.Name + "»: " + _approved.Count + L.T("Operation25")); UpdateButtons(); return;
        }
        var previous = Stage;
        Stage = OperationStage.Running; _waiting = previous == OperationStage.Confirm && _action == 0;
        SetStatus(L.T(_waiting ? "OperationWaiting" : previous == OperationStage.ConfirmCleanup || _action == 3 ? "OperationRemoving" : "Operation13")); UpdateButtons();
        try
        {
            if (previous == OperationStage.ConfirmCleanup)
            {
                var failures = new List<string>();
                foreach (var item in _approved.OrderBy(x => x.Kind == "folder" ? 1 : 0))
                {
                    try
                    {
                        await Task.Run(() => _services.Delete(_app, item));
                        var row = _items.Rows.Cast<DataGridViewRow>().First(r => Equals(r.Tag, item)); _items.Rows.Remove(row);
                        SetStatus(L.T("Operation26") + " " + item.Path);
                    }
                    catch (Exception ex) { failures.Add(item.Path + ": " + ex.Message); LogError(ex); }
                }
                _approved.Clear(); Stage = OperationStage.Leftovers;
                SetStatus(failures.Count == 0 ? L.T("Operation26") : string.Join(Environment.NewLine, failures));
            }
            else if (previous == OperationStage.Result || _action is 1 or 2)
            {
                var items = await Task.Run(() => _services.Find(_app));
                _items.Rows.Clear();
                foreach (var item in items)
                {
                    var index = _items.Rows.Add(false, item.Kind == "folder" ? item.Path : item.Hive + " / " + item.View + " / " + item.Path, L.T(item.Evidence));
                    _items.Rows[index].Tag = item;
                    foreach (DataGridViewCell cell in _items.Rows[index].Cells) cell.ToolTipText = cell.Value?.ToString();
                }
                _items.Visible = true; Stage = OperationStage.Leftovers;
                SetStatus(items.Count == 0 ? L.T("Operation20") : L.T("OperationChoose"));
            }
            else if (_action == 3)
            {
                await Task.Run(() => _services.RemoveRecord(_app)); Stage = OperationStage.Finished; SetStatus(L.T("Operation11"));
            }
            else
            {
                var result = await Task.Run(() => _services.Uninstall(_app, _observation.Token));
                // Stopping observation can race completion; never offer cleanup on that path.
                _canFind = result.Removed && !result.Reboot && !_app.Store && !_observation.IsCancellationRequested;
                Stage = OperationStage.Result;
                SetStatus(_observation.IsCancellationRequested ? L.T("Operation56") : result.Message);
            }
        }
        catch (Exception ex)
        {
            _canFind = false; Stage = OperationStage.Finished;
            SetStatus(ex is OperationCanceledException ? L.T("Operation56") : L.T("Operation12") + ": " + ex.Message); LogError(ex);
        }
        finally
        {
            // Keep the operation guard until refresh completes, without activating or replacing this dialog.
            var completedStage = Stage; Stage = OperationStage.Running; _waiting = false; UpdateButtons();
            try { await _refresh(); }
            catch (Exception ex) { SetStatus(Status + Environment.NewLine + L.T("OperationRefreshError") + ": " + ex.Message); LogError(ex); }
            Stage = completedStage; UpdateButtons();
        }
    }
    private void LogError(Exception ex)
    { try { OperationLog.Write("operation-error", _app.Id, ex.ToString()); } catch { } }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _observation.Dispose(); _image?.Dispose(); }
        base.Dispose(disposing);
    }
}
