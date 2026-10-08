using System.Diagnostics;
using Microsoft.Win32;

namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private readonly GlassButton _delete;
    private readonly List<(int Action, ToolStripMenuItem Item)> _operationMenus = [];
    private bool _operationBusy;
    private OperationWindow? _operationDialog;
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
        if (action <= 3 && _operationDialog is { IsDisposed: false } existing)
        { if (!existing.Running) existing.Activate(); return; }
        if (!CanRun(action, app) || app == null) return;
        if (action <= 3) { OpenOperation(app, action); return; }
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
        }
        catch (Exception ex)
        {
            try { OperationLog.Write("operation-error", app.Id, ex.ToString()); } catch { }
            Dialogs.Show(this, ex.Message, L.T("Operation12"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (action <= 3) await RefreshAfter(app);
        }
        finally { _operationBusy = false; UpdateActionState(); }
    }
    private void OpenOperation(AppEntry app, int action)
    {
        if (_operationDialog is { IsDisposed: false }) return;
        _icons.TryGetValue(app.Id, out var icon);
        var dialog = new OperationWindow(app, action, () => RefreshAfter(app), icon ?? _fallbackIcon);
        _operationBusy = true;
        _operationDialog = dialog;
        dialog.FormClosed += (_, _) =>
        {
            _operationDialog = null; _operationBusy = false; UpdateActionState(); dialog.Dispose();
        };
        UpdateActionState();
        dialog.Show(this);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SavePlacement();
        if (e.CloseReason == CloseReason.UserClosing && !_exitRequested)
        { e.Cancel = true; SavePreferences(); Hide(); return; }
        _vault.Lock();
        if (_operationBusy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
        _versionCancellation?.Cancel();
        SavePreferences();
        base.OnFormClosing(e);
    }
}
