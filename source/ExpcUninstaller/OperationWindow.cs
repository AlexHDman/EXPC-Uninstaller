using System.Text;

namespace ExpcUninstaller;

internal sealed class OperationServices
{
    public Func<AppEntry, CancellationToken, Task<OperationResult>> Uninstall { get; init; } = ProductActions.Uninstall;
    public Func<AppEntry, List<JunkItem>> Find { get; init; } = app => BcuTargetedJunkAdapter.Find(app);
    public Func<AppEntry, CancellationToken, TargetedScan>? Scan { get; init; }
    public Func<AppEntry, CancellationToken, List<JunkItem>>? FindCancellable { get; init; }
    public Func<AppEntry, Task> RemoveRecord { get; init; } = RegistryBackup.RemoveRecord;
    public Func<AppEntry, CleanupEntry, Task> DeleteEntry { get; init; } = CleanupPlan.Delete;
}
internal sealed class LeftoverTree : TreeView { internal void RecreateNativeHandle() => RecreateHandle(); }
internal enum OperationStage { Confirm, Running, Leftovers, ConfirmCleanup, Finished }

// This form is never used to start or wait for a normal uninstall.
internal sealed class OperationWindow : Form
{
    private readonly AppEntry _app;
    private readonly int _action;
    private readonly OperationServices _services;
    private readonly Func<Task> _refresh;
    private CancellationTokenSource? _work;
    private readonly ActivityIndicator _activity = new() { Dock = DockStyle.Right, Width = 30 };
    internal Action<OperationActivity, string>? Progress;
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 78, Padding = new Padding(8), AutoEllipsis = true };
    private readonly TextBox _journal = new() { Dock = DockStyle.Bottom, Height = 125, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Visible = false };
    private readonly LeftoverTree _tree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowNodeToolTips = true, HideSelection = false };
    private readonly Button _primary = new ThemedButton() { AutoSize = true };
    private readonly Button _secondary = new ThemedButton() { AutoSize = true };
    private readonly HashSet<CleanupEntry> _selected = [];
    private List<CleanupEntry> _entries = [];
    private List<CleanupEntry> _approved = [];
    private readonly Image? _image;
    private ImageList? _states;
    private List<Bitmap> _stateBitmaps = [];
    internal OperationStage Stage { get; private set; } = OperationStage.Confirm;
    internal bool Running => Stage == OperationStage.Running;
    internal string Status => _status.Text;
    internal LeftoverTree Tree => _tree;

    public OperationWindow(AppEntry app, int action, Func<Task> refresh, Image? image = null, OperationServices? services = null, List<CleanupEntry>? initial = null, CleanupCheck? check = null)
    {
        if (action is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(action), "Normal uninstall must not open OperationWindow");
        _app = app; _action = action; _refresh = refresh; _services = services ?? new() { Scan = (app, token) => BcuTargetedJunkAdapter.Scan(app, token) };
        Text = L.T("Operation19") + app.Name; ClientSize = new Size(720, 490); MinimumSize = new Size(540, 340);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10.5f); StartPosition = FormStartPosition.Manual;
        TopMost = false; MaximizeBox = false;
        try { _image = image == null ? null : new Bitmap(image); }
        catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException or System.Runtime.InteropServices.ExternalException) { _image = null; }
        var header = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8) };
        header.Controls.Add(_activity);
        header.Controls.Add(new Label { Text = app.Name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
        header.Controls.Add(new PictureBox { Image = _image, Dock = DockStyle.Left, Width = 40, SizeMode = PictureBoxSizeMode.Zoom, Padding = new Padding(4) });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([_secondary, _primary]);
        var journalBar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var details = new ThemedButton { Text = L.T("OperationDetails"), Dock = DockStyle.Left, AutoSize = true, Width = 195 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        void JournalAction(string key, Action action)
        {
            var b = new ThemedButton { Text = L.T(key), AutoSize = true };
            b.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } };
            actions.Controls.Add(b);
        }
        JournalAction("CleanupClearLog", () => _journal.Clear());
        JournalAction("CleanupCopyLog", () => { if (_journal.TextLength > 0) Clipboard.SetText(_journal.Text); });
        JournalAction("CleanupSaveLog", () =>
        {
            using var save = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "EXPC-operation.txt" };
            if (save.ShowDialog(this) == DialogResult.OK) File.WriteAllText(save.FileName, _journal.Text, Encoding.UTF8);
        });
        journalBar.Controls.Add(actions); journalBar.Controls.Add(details);
        Controls.Add(_tree); Controls.Add(_journal); Controls.Add(journalBar); Controls.Add(_status); Controls.Add(header); Controls.Add(buttons);
        details.Click += (_, _) =>
        {
            _journal.Visible = !_journal.Visible;
            if (_journal.Visible && Height < 465 * DeviceDpi / 96)
            {
                var work = Screen.FromControl(this).WorkingArea;
                Height = Math.Min(work.Height, 465 * DeviceDpi / 96);
                Top = Math.Clamp(Top, work.Top, work.Bottom - Height);
            }
        };
        _primary.Click += async (_, _) => await Advance();
        _secondary.Click += (_, _) => Secondary();
        _tree.NodeMouseClick += (_, e) => { if (e.Node != null && e.Button == MouseButtons.Left && _tree.HitTest(e.Location).Location.HasFlag(TreeViewHitTestLocations.StateImage)) Toggle(e.Node); };
        _tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None && _tree.SelectedNode is { } node) { e.Handled = e.SuppressKeyPress = true; Toggle(node); } };
        FormClosing += (_, e) => { if (Running) { _work?.Cancel(); e.Cancel = true; } };
        Theme.Apply(this); BuildStateImages();
        _tree.LineColor = GlassTeal.Border; _tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        _tree.DrawNode += (_, e) =>
        {
            if (e.Node == null) return;
            using var background = new SolidBrush((e.State & TreeNodeStates.Selected) != 0 ? GlassTeal.Selection : GlassTeal.Background);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, _tree.Font, e.Bounds, GlassTeal.Text, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        };
        if (check?.Error != null) { Stage = OperationStage.Finished; SetStatus(check.Error); }
        else if (initial != null) Populate(initial);
        else SetStatus(action == 1 ? L.T("Operation6") + app.Name + L.T("Operation7") : action == 3 ? L.T("Operation8") + app.Name + L.T("Operation9") : L.T("OperationFindPrompt"));
        if (check?.Notice != null) SetStatus(Status + "\n" + check.Notice);
        if (initial is { Count: 0 }) ClientSize = new Size(720, 300);
        UpdateButtons();
    }
    internal static Rectangle Placement(Rectangle owner, Rectangle work, int dpi, bool compact = false)
    {
        var width = Math.Min(work.Width, (int)(740L * dpi / 96)); var height = Math.Min(work.Height, (int)((compact ? 340L : 530L) * dpi / 96));
        return new(Math.Clamp(owner.Left + (owner.Width - width) / 2, work.Left, work.Right - width),
            Math.Clamp(owner.Top + (owner.Height - height) / 2, work.Top, work.Bottom - height), width, height);
    }
    internal void PositionNear(Form owner)
    {
        var work = Screen.FromControl(owner).WorkingArea;
        Location = owner.Location; // move to the owner's monitor before applying its physical DPI
        MinimumSize = Size.Empty;
        Bounds = Placement(owner.Bounds, work, owner.DeviceDpi, _entries.Count == 0 && Stage != OperationStage.Confirm);
        MinimumSize = new Size(Math.Min(work.Width, 540 * owner.DeviceDpi / 96), Math.Min(work.Height, 340 * owner.DeviceDpi / 96));
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); BuildStateImages(); }
    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); if (_states != null && !_tree.IsDisposed) BuildStateImages(); }
    private void BuildStateImages() => RebuildStateImages(DeviceDpi);
    internal void RebuildStateImages(int dpi)
    {
        var states = new ImageList { ImageSize = new Size(Math.Max(16, 18 * dpi / 96), Math.Max(16, 18 * dpi / 96)), ColorDepth = ColorDepth.Depth32Bit };
        var bitmaps = new List<Bitmap>();
        for (var i = 0; i < 3; i++)
        {
            var image = new Bitmap(states.ImageSize.Width, states.ImageSize.Height);
            using var g = Graphics.FromImage(image); g.Clear(GlassTeal.Background);
            var box = new Rectangle(1, 1, image.Width - 2, image.Height - 2);
            ControlPaint.DrawCheckBox(g, box, i == 0 ? ButtonState.Normal : ButtonState.Checked);
            if (i == 2) { using var b = new SolidBrush(GlassTeal.Surface); g.FillRectangle(b, Rectangle.Inflate(box, -3, -3)); using var pen = new Pen(GlassTeal.Heading, 2); g.DrawLine(pen, 4, image.Height / 2, image.Width - 4, image.Height / 2); }
            states.Images.Add(image); bitmaps.Add(image);
        }
        // StateImageList rebuilds native nodes. Suppress custom drawing until node handles are remapped.
        _tree.BeginUpdate();
        try { _tree.StateImageList = states; }
        finally { _tree.EndUpdate(); }
        _states?.Dispose();
        foreach (var old in _stateBitmaps) old.Dispose();
        _states = states; _stateBitmaps = bitmaps;
    }
    private static IEnumerable<TreeNode> Nodes(TreeNode node) => new[] { node }.Concat(node.Nodes.Cast<TreeNode>().SelectMany(Nodes));
    private void Populate(List<CleanupEntry> entries)
    {
        _entries = entries; _selected.Clear(); _tree.Nodes.Clear();
        foreach (var key in entries.Count == 0 ? Array.Empty<string>() : new[] { "CleanupFiles", "CleanupRegistry", "CleanupUserData" })
        {
            var section = _tree.Nodes.Add(L.T(key));
            var byPath = new Dictionary<(JunkItem, string), TreeNode>();
            foreach (var entry in entries.Where(x => x.Section == key).OrderBy(x => x.Path.Length))
            {
                var parent = section; var path = entry.Path;
                if (entry.ValueName != null && byPath.TryGetValue((entry.Root, path.ToUpperInvariant()), out var keyNode)) parent = keyNode;
                while (entry.ValueName == null && path.LastIndexOf('\\') is var slash && slash >= 0)
                { path = path[..slash]; if (byPath.TryGetValue((entry.Root, path.ToUpperInvariant()), out var found)) { parent = found; break; } }
                var node = parent.Nodes.Add(entry.DisplayPath); node.Tag = entry; node.ToolTipText = entry.DisplayPath + "\n" + L.T(entry.Root.Evidence);
                if (entry.ValueName == null) byPath[(entry.Root, entry.Path.ToUpperInvariant())] = node;
            }
            section.Expand();
        }
        Stage = OperationStage.Leftovers; SetStatus(entries.Count == 0 ? L.T("CleanupEmpty") : L.T("CleanupSelectionHelp")); UpdateStates();
    }
    internal void Toggle(TreeNode node)
    {
        if (Stage != OperationStage.Leftovers) return;
        var entries = Nodes(node).Select(n => n.Tag).OfType<CleanupEntry>().ToList();
        var select = entries.Any(x => !_selected.Contains(x));
        foreach (var entry in entries) { if (select) _selected.Add(entry); else _selected.Remove(entry); }
        UpdateStates(); UpdateButtons();
    }
    private void UpdateStates()
    {
        foreach (var node in _tree.Nodes.Cast<TreeNode>().SelectMany(Nodes))
        {
            var values = Nodes(node).Select(n => n.Tag).OfType<CleanupEntry>().ToList(); var count = values.Count(_selected.Contains);
            node.StateImageIndex = count == 0 ? 0 : count == values.Count ? 1 : 2;
        }
        // A folder in Program files may contain protected entries shown in User data.
        var parents = _tree.Nodes.Cast<TreeNode>().SelectMany(Nodes).Where(n => n.Tag is CleanupEntry { ValueName: null })
            .ToDictionary(n => ((CleanupEntry)n.Tag!).Root + "|" + ((CleanupEntry)n.Tag!).Path.ToUpperInvariant());
        foreach (var entry in _entries.Where(e => !_selected.Contains(e)))
        {
            var path = entry.Path;
            while (path.LastIndexOf('\\') is var slash && slash >= 0)
            {
                path = path[..slash];
                if (parents.TryGetValue(entry.Root + "|" + path.ToUpperInvariant(), out var parent) && parent.StateImageIndex != 0)
                    for (var node = parent; node != null; node = node.Parent) node.StateImageIndex = 2;
            }
        }
    }
    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        _status.Text = text; Progress?.Invoke(_activity.Activity, text); _journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        try { OperationLog.Write("cleanup-ui", _app.Id, text); } catch { }
    }
    private void UpdateButtons()
    {
        _tree.Enabled = Stage == OperationStage.Leftovers;
        _primary.Enabled = !Running && (Stage != OperationStage.Leftovers || CleanupPlan.Build(_entries, _selected).Count > 0);
        _primary.Visible = Stage != OperationStage.Finished && !(Stage == OperationStage.Leftovers && _entries.Count == 0);
        _primary.Text = L.T(Stage == OperationStage.Confirm ? _action == 3 ? "OperationDelete" : "OperationFind" : Stage == OperationStage.ConfirmCleanup ? "OperationConfirmCleanup" : "Operation17");
        _secondary.Enabled = true;
        _activity.Set(Running ? OperationActivity.Busy : _activity.Activity == OperationActivity.Busy ? OperationActivity.Idle : _activity.Activity);
        _secondary.Text = Running ? L.T("StopOperation") : Stage is OperationStage.Confirm or OperationStage.ConfirmCleanup ? L.T("Cancel") : UiText.Close;
    }
    internal void Secondary()
    {
        if (Running) { _work?.Cancel(); return; }
        if (Stage == OperationStage.ConfirmCleanup) { Stage = OperationStage.Leftovers; _approved.Clear(); SetStatus(L.T("CleanupSelectionHelp")); UpdateButtons(); }
        else Close();
    }
    private bool _canShowSuccess = true;
    private string? _scopeWarning;
    internal OperationActivity Activity => _activity.Activity;
    internal async Task Search(string? warning = null, bool removalConfirmed = true)
    {
        if (Running) return;
        _canShowSuccess = removalConfirmed; _scopeWarning = null;
        using var work = new CancellationTokenSource(); _work = work;
        Stage = OperationStage.Running; _activity.Set(OperationActivity.Busy); UpdateButtons(); SetStatus(L.T("Operation13"));
        try
        {
            var result = await Task.Run(() =>
            {
                var scan = _services.Scan?.Invoke(_app, work.Token)
                    ?? new TargetedScan(_services.FindCancellable?.Invoke(_app, work.Token) ?? _services.Find(_app), null, 1);
                var entries = new List<CleanupEntry>(); var issues = new List<string>();
                if (warning != null) issues.Add(warning); if (scan.Warning != null) issues.Add(scan.Warning);
                foreach (var root in scan.Items)
                {
                    work.Token.ThrowIfCancellationRequested();
                    try { entries.AddRange(CleanupPlan.Expand([root], work.Token)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { issues.Add(root.Path + ": " + ex.Message); }
                }
                return (entries, warning: issues.Count == 0 ? null : string.Join("\n", issues), scan.CheckedScopes);
            });
            work.Token.ThrowIfCancellationRequested(); Populate(result.entries);
            if (result.warning != null)
            {
                _scopeWarning = result.warning; _canShowSuccess = false;
                _activity.Set(OperationActivity.Error);
                if (result.CheckedScopes == 0 && result.entries.Count == 0) Stage = OperationStage.Finished;
                SetStatus(L.T(result.CheckedScopes == 0 && result.entries.Count == 0 ? "CleanupFailed" : "CleanupPartial") + ": " + result.warning);
            }
            else { _activity.Set(_canShowSuccess && result.entries.Count == 0 ? OperationActivity.Success : OperationActivity.Idle); SetStatus(Status); }
        }
        catch (OperationCanceledException) { Stage = OperationStage.Finished; _activity.Set(OperationActivity.Idle); SetStatus(L.T("SearchCancelled")); }
        catch (Exception ex) { Stage = OperationStage.Finished; _activity.Set(OperationActivity.Error); SetStatus(L.T("CleanupFailed") + ": " + ex.Message); }
        finally { _work = null; UpdateButtons(); Progress?.Invoke(_activity.Activity, Status); }
    }
    internal async Task Advance()
    {
        if (Running || Stage == OperationStage.Finished) return;
        if (Stage == OperationStage.Leftovers)
        {
            _approved = CleanupPlan.Build(_entries, _selected); if (_approved.Count == 0) return;
            Stage = OperationStage.ConfirmCleanup; SetStatus(L.T("Operation24") + _app.Name + "»): " + _approved.Count + L.T("Operation25")); UpdateButtons(); return;
        }
        if (Stage == OperationStage.Confirm && _action != 3) { await Search(); return; }
        using var work = new CancellationTokenSource(); _work = work; int completedCount = 0;
        var previous = Stage; Stage = OperationStage.Running; _activity.Set(OperationActivity.Busy); UpdateButtons();
        try
        {
            if (previous == OperationStage.ConfirmCleanup)
            {
                var failures = new List<string>();
                foreach (var entry in _approved)
                {
                    if (work.IsCancellationRequested) break;
                    try { await Task.Run(() => _services.DeleteEntry(_app, entry)); completedCount++; _entries.RemoveAll(x => x == entry || x == (entry with { ApprovedAnchor = null }));
                        if (entry.ApprovedAnchor != null) _entries.RemoveAll(x => x.Root == entry.Root && x.Path == entry.Path && x.ValueName == "InstallLocation"); SetStatus(L.T("Operation26") + " " + entry.DisplayPath); }
                    catch (Exception ex) { failures.Add(entry.DisplayPath + ": " + ex.Message); SetStatus(failures[^1]); }
                }
                Populate(_entries.ToList());
                _activity.Set(work.IsCancellationRequested ? OperationActivity.Idle : failures.Count == 0 ? (_canShowSuccess ? OperationActivity.Success : OperationActivity.Idle) : OperationActivity.Error);
                SetStatus(work.IsCancellationRequested ? L.F("PartialOperation", completedCount, _approved.Count - completedCount) : failures.Count == 0 ? L.T("Operation26") : string.Join(Environment.NewLine, failures)); _approved.Clear();
            }
            else if (_action == 3) { await Task.Run(() => _services.RemoveRecord(_app)); Stage = OperationStage.Finished; SetStatus(L.T("Operation11")); }
            else { Populate(await Task.Run(() => CleanupPlan.Expand(_services.Find(_app)))); }
        }
        catch (Exception ex) { Stage = OperationStage.Finished; _activity.Set(OperationActivity.Error); SetStatus(L.T("Operation12") + ": " + ex.Message); }
        finally
        {
            var completed = Stage; Stage = OperationStage.Running;
            try { await _refresh(); } catch (Exception ex) { SetStatus(L.T("OperationRefreshError") + ": " + ex.Message); }
            Stage = completed; _work = null;
            if (_scopeWarning != null) SetStatus(Status + "\n" + L.T("CleanupPartial") + ": " + _scopeWarning);
            UpdateButtons(); Progress?.Invoke(_activity.Activity, Status);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _work?.Cancel();
        // Destroy TreeView's native handle before its state-image list and source bitmaps.
        base.Dispose(disposing);
        if (disposing) { _states?.Dispose(); foreach (var bitmap in _stateBitmaps) bitmap.Dispose(); _image?.Dispose(); }
    }
}
