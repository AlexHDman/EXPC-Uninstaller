using System.Diagnostics;
using Microsoft.Win32;

namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private readonly GlassButton _delete;
    private readonly List<(int Action, ToolStripMenuItem Item)> _operationMenus = [];
    private bool _operationBusy;
    private AppEntry? SelectedEntry => _grid.CurrentRow?.Index is int index && index >= 0 && index < _visible.Count && _grid.SelectedRows.Count > 0 ? _visible[index] : null;
    private ToolStripMenuItem ActionItem(int action)
    {
        var item = new ToolStripMenuItem(UiText.ContextActions[action]);
        item.Image = MenuGlyph(action switch { 0 => Glyph.Delete, 1 => Glyph.ForceDelete, 2 => Glyph.Junk, 3 => Glyph.RemoveEntry,
            4 => Glyph.Folder, 5 => Glyph.Registry, 6 => Glyph.Website, 7 => Glyph.Search, 8 => Glyph.Source, _ => Glyph.Update });
        item.ShortcutKeys = action switch { 0 => Keys.Control | Keys.U, 1 => Keys.Control | Keys.Shift | Keys.U, 2 => Keys.Control | Keys.J, 4 => Keys.Control | Keys.O, 5 => Keys.Control | Keys.R, 9 => Keys.Control | Keys.F5, _ => Keys.None };
        item.Click += async (_, _) => await RunSelected(action);
        _operationMenus.Add((action, item));
        return item;
    }
    private bool CanRun(int action, AppEntry? entry)
    {
        if (_operationBusy || entry == null) return false;
        var win32 = !entry.Store && entry.Metadata != null && entry.Sources.Length > 0;
        return action switch
        {
            0 => !entry.Protected && (entry.Store || win32 && !string.IsNullOrWhiteSpace(entry.Metadata!.Uninstall)),
            1 or 2 or 3 => win32 && !entry.Protected,
            4 => CleanupBoundary.LocalPath(entry.Store ? entry.StoreLocation : entry.Metadata?.Location),
            5 => win32,
            6 => Website(entry.Metadata?.Website) != null,
            7 => true,
            8 => _versionCancellation == null,
            9 => _versionCancellation == null,
            _ => false
        };
    }
    private static Uri? Website(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    private void UpdateActionState()
    {
        var selected = SelectedEntry;
        foreach (var (action, item) in _operationMenus)
        {
            item.Enabled = CanRun(action, selected);
            item.ToolTipText = item.Enabled ? "" : L.T("Operation0");
        }
        if (_delete == null) return;
        _delete.ActionEnabled = CanRun(0, selected); _delete.TabStop = _delete.ActionEnabled; _delete.Invalidate();
    }
    private bool Confirm(string text) => Dialogs.Show(this, text, UiText.Title, MessageBoxButtons.YesNo,
        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    private async Task RefreshAfter(AppEntry app)
    {
        _catalogs[app.Store ? 1 : 0] = null;
        if (_tab == (app.Store ? 1 : 0)) await LoadCatalog(true);
    }
    private async Task RunSelected(int action)
    {
        var app = SelectedEntry;
        if (!CanRun(action, app) || app == null) return;
        try
        {
            if (action == 8) { EditSource(app); return; }
            if (action == 9) { await CheckVersions([app]); return; }
            if (action == 4)
            {
                var path = app.Store ? app.StoreLocation! : CleanupBoundary.Normalize(app.Metadata!.Location);
                if (!Directory.Exists(path)) throw new IOException(L.T("Operation1"));
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true, ArgumentList = { path } });
                return;
            }
            if (action == 5)
            {
                var registration = Registration.Parse(app.Sources[0]);
                using var key = registration.Open();
                if (key == null) throw new IOException(L.T("Operation2"));
                var path = registration.FullPath;
                if (registration.View == RegistryView.Registry32 && registration.Hive == RegistryHive.LocalMachine)
                    path = path.Replace(@"\SOFTWARE\", @"\SOFTWARE\WOW6432Node\");
                // Do not kill a user's existing Registry Editor (the older BCU helper does).
                using var settings = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
                settings.SetValue("LastKey", path);
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "regedit.exe")) { UseShellExecute = true });
                return;
            }
            if (action is 6 or 7)
            {
                var url = action == 6 ? Website(app.Metadata?.Website)!.AbsoluteUri
                    : "https://www.google.com/search?q=" + Uri.EscapeDataString(app.Name + " " + app.Publisher);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return;
            }
            if (action == 0 && !Confirm(L.T("Operation3") + app.Name + "»?\n" + (app.Store ? L.T("Operation4") : L.T("Operation5")))) return;
            if (action == 1 && !Confirm(L.T("Operation6") + app.Name + L.T("Operation7"))) return;
            if (action == 3 && !Confirm(L.T("Operation8") + app.Name + L.T("Operation9"))) return;
            _operationBusy = true; UpdateActionState();
            if (action == 0)
            {
                using var cancel = new CancellationTokenSource();
                using var progress = new OperationWindow(app.Name, cancel);
                progress.Show(this);
                OperationResult result;
                try { result = await Task.Run(() => ProductActions.Uninstall(app, cancel.Token)); }
                finally { progress.Complete(); }
                await RefreshAfter(app);
                Dialogs.Show(this, result.Message, app.Name, MessageBoxButtons.OK, result.Removed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                if (result.Removed && !app.Store && !result.Reboot && Confirm("«" + app.Name + L.T("Operation10"))) await ShowJunk(app);
            }
            else if (action is 1 or 2) await ShowJunk(app);
            else if (action == 3)
            {
                await Task.Run(() => RegistryBackup.RemoveRecord(app));
                await RefreshAfter(app);
                Dialogs.Show(this, L.T("Operation11"), app.Name);
            }
        }
        catch (Exception ex)
        {
            try { OperationLog.Write("operation-error", app.Id, ex.ToString()); } catch { }
            Dialogs.Show(this, ex.Message, L.T("Operation12"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (action <= 3) await RefreshAfter(app);
        }
        finally { _operationBusy = false; UpdateActionState(); }
    }
    private async Task ShowJunk(AppEntry app)
    {
        _status = L.T("Operation13"); Invalidate();
        try
        {
            var items = await Task.Run(() => BcuTargetedJunkAdapter.Find(app));
            using var dialog = new JunkWindow(app, items);
            dialog.ShowDialog(this);
        }
        finally { _status = UiText.Ready; Invalidate(); await RefreshAfter(app); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exitRequested)
        { e.Cancel = true; SavePreferences(); Hide(); return; }
        _vault.Lock();
        if (_operationBusy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
        _versionCancellation?.Cancel();
        SavePreferences();
        base.OnFormClosing(e);
    }
}

internal sealed class OperationWindow : Form
{
    private bool _done;
    public OperationWindow(string name, CancellationTokenSource cancellation)
    {
        Text = L.T("Operation14") + name; ClientSize = new Size(470, 110); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        BackColor = GlassTeal.Background; ForeColor = GlassTeal.Text;
        var text = new Label { Text = L.T("Operation15"), AutoSize = false, Dock = DockStyle.Top, Height = 65, Padding = new Padding(12) };
        var stop = new Button { Text = L.T("Operation16"), Dock = DockStyle.Bottom, Height = 32, FlatStyle = FlatStyle.Flat };
        stop.Click += (_, _) => { cancellation.Cancel(); stop.Enabled = false; };
        Controls.Add(text); Controls.Add(stop);
        FormClosing += (_, e) => { if (!_done) { e.Cancel = true; cancellation.Cancel(); } };
    }
    public void Complete() { _done = true; Close(); }
}

internal sealed class JunkWindow : Form
{
    private readonly DataGridView _items = new();
    private readonly Button _remove = new() { Text = L.T("Operation17"), AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button _close = new() { Text = L.T("Operation18"), AutoSize = true, FlatStyle = FlatStyle.Flat };
    private bool _busy;
    public JunkWindow(AppEntry app, List<JunkItem> items)
    {
        Text = L.T("Operation19") + app.Name; StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(850, 400); MinimumSize = new Size(600, 300);
        BackColor = GlassTeal.Background; ForeColor = GlassTeal.Text; Font = new Font("Segoe UI", 9);
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(10),
            Text = items.Count == 0 ? L.T("Operation20")
                : app.Name + " — " + items.Count + L.T("Operation21")
        };
        _items.Dock = DockStyle.Fill; _items.BackgroundColor = GlassTeal.Background; _items.BorderStyle = BorderStyle.None;
        _items.AllowUserToAddRows = _items.AllowUserToDeleteRows = false; _items.RowHeadersVisible = false;
        _items.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; _items.MultiSelect = false;
        _items.DefaultCellStyle.BackColor = GlassTeal.Surface; _items.DefaultCellStyle.ForeColor = GlassTeal.Text;
        _items.DefaultCellStyle.SelectionBackColor = GlassTeal.Selection; _items.DefaultCellStyle.SelectionForeColor = GlassTeal.Text;
        _items.EnableHeadersVisualStyles = false; _items.ColumnHeadersDefaultCellStyle.BackColor = GlassTeal.Caption; _items.ColumnHeadersDefaultCellStyle.ForeColor = GlassTeal.Text;
        _items.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        foreach (var title in new[] { L.T("Operation22"), L.T("Operation23") }) _items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, ReadOnly = true });
        foreach (var item in items)
        {
            var row = _items.Rows[_items.Rows.Add(false, item.Kind == "folder" ? item.Path : item.Hive + " / " + item.View + " / " + item.Path, L.T(item.Evidence))]; row.Tag = item;
        }
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        bar.Controls.Add(_close); bar.Controls.Add(_remove);
        Controls.Add(_items); Controls.Add(heading); Controls.Add(bar);
        _remove.Enabled = items.Count > 0;
        _close.Click += (_, _) => Close();
        FormClosing += (_, e) => e.Cancel = _busy;
        _remove.Click += async (_, _) =>
        {
            _items.EndEdit();
            var selected = _items.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value is true).ToList();
            if (selected.Count == 0) return;
            if (Dialogs.Show(this, L.T("Operation24") + app.Name + "»): " + selected.Count + L.T("Operation25"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _busy = true; _items.Enabled = _remove.Enabled = _close.Enabled = false;
            var errors = new List<string>();
            try
            {
                foreach (var row in selected.OrderBy(r => ((JunkItem)r.Tag!).Kind == "folder" ? 1 : 0))
                {
                    var item = (JunkItem)row.Tag!;
                    try { await Task.Run(() => BcuTargetedJunkAdapter.Delete(app, item)); _items.Rows.Remove(row); }
                    catch (Exception ex)
                    {
                        errors.Add(item.Path + ": " + ex.Message);
                        try { OperationLog.Write("cleanup-error", app.Id, item.Path + ": " + ex); } catch { }
                    }
                }
                Dialogs.Show(this, errors.Count == 0 ? L.T("Operation26") : string.Join("\n", errors), Text);
            }
            finally { _busy = false; _items.Enabled = _close.Enabled = true; _remove.Enabled = _items.Rows.Count > 0; }
        };
    }
}
