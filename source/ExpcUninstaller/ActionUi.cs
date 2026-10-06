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
            item.ToolTipText = item.Enabled ? "" : "Недоступно для выбранной записи или операция выполняется";
        }
        if (_delete == null) return;
        _delete.ActionEnabled = CanRun(0, selected); _delete.TabStop = _delete.ActionEnabled; _delete.Invalidate();
    }
    private bool Confirm(string text) => MessageBox.Show(this, text, UiText.Title, MessageBoxButtons.YesNo,
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
            if (action == 4)
            {
                var path = app.Store ? app.StoreLocation! : CleanupBoundary.Normalize(app.Metadata!.Location);
                if (!Directory.Exists(path)) throw new IOException("Папка отсутствует.");
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true, ArgumentList = { path } });
                return;
            }
            if (action == 5)
            {
                var registration = Registration.Parse(app.Sources[0]);
                using var key = registration.Open();
                if (key == null) throw new IOException("Запись отсутствует. Обновите список.");
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
            if (action == 0 && !Confirm("Удалить «" + app.Name + "»?\n" + (app.Store ? "Только для текущего пользователя." : "Будет запущен штатный деинсталлятор."))) return;
            if (action == 1 && !Confirm("Принудительное удаление «" + app.Name + "»: найти связанные остатки для ручного выбора?\nШтатный деинсталлятор запускаться не будет. Полное удаление не гарантируется.")) return;
            if (action == 3 && !Confirm("Удалить запись «" + app.Name + "» из списка?\nФайлы программы останутся. Перед изменением будет сохранена резервная копия реестра.")) return;
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
                MessageBox.Show(this, result.Message, app.Name, MessageBoxButtons.OK, result.Removed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                if (result.Removed && !app.Store && !result.Reboot && Confirm("«" + app.Name + "»: найти оставшиеся файлы и ключи реестра?")) await ShowJunk(app);
            }
            else if (action is 1 or 2) await ShowJunk(app);
            else if (action == 3)
            {
                await Task.Run(() => RegistryBackup.RemoveRecord(app));
                await RefreshAfter(app);
                MessageBox.Show(this, "Удалена только запись. Файлы программы не удалялись.", app.Name);
            }
        }
        catch (Exception ex)
        {
            try { OperationLog.Write("operation-error", app.Id, ex.ToString()); } catch { }
            MessageBox.Show(this, ex.Message, "Операция не завершена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (action <= 3) await RefreshAfter(app);
        }
        finally { _operationBusy = false; UpdateActionState(); }
    }
    private async Task ShowJunk(AppEntry app)
    {
        _status = "Адресный поиск остатков…"; Invalidate();
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
        if (_operationBusy) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }
}

internal sealed class OperationWindow : Form
{
    private bool _done;
    public OperationWindow(string name, CancellationTokenSource cancellation)
    {
        Text = "Удаление — " + name; ClientSize = new Size(470, 110); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        BackColor = GlassTeal.Background; ForeColor = GlassTeal.Text;
        var text = new Label { Text = "Ожидание деинсталлятора и проверка регистрации.\nДля отмены удаления используйте окно деинсталлятора.", AutoSize = false, Dock = DockStyle.Top, Height = 65, Padding = new Padding(12) };
        var stop = new Button { Text = "Прекратить ожидание", Dock = DockStyle.Bottom, Height = 32, FlatStyle = FlatStyle.Flat };
        stop.Click += (_, _) => { cancellation.Cancel(); stop.Enabled = false; };
        Controls.Add(text); Controls.Add(stop);
        FormClosing += (_, e) => { if (!_done) { e.Cancel = true; cancellation.Cancel(); } };
    }
    public void Complete() { _done = true; Close(); }
}

internal sealed class JunkWindow : Form
{
    private readonly DataGridView _items = new();
    private readonly Button _remove = new() { Text = "Удалить выбранное", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button _close = new() { Text = "Закрыть", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private bool _busy;
    public JunkWindow(AppEntry app, List<JunkItem> items)
    {
        Text = "Остатки — " + app.Name; StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(850, 400); MinimumSize = new Size(600, 300);
        BackColor = GlassTeal.Background; ForeColor = GlassTeal.Text; Font = new Font("Segoe UI", 9);
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(10),
            Text = items.Count == 0 ? "Подтверждённые адресные остатки не найдены. Это не доказательство полного отсутствия остатков."
                : app.Name + " — " + items.Count + " объекта\nВыберите вручную. Файлы удаляются без корзины; ключи реестра сохраняются в резервную копию."
        };
        _items.Dock = DockStyle.Fill; _items.BackgroundColor = GlassTeal.Background; _items.BorderStyle = BorderStyle.None;
        _items.AllowUserToAddRows = _items.AllowUserToDeleteRows = false; _items.RowHeadersVisible = false;
        _items.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; _items.MultiSelect = false;
        _items.DefaultCellStyle.BackColor = GlassTeal.Surface; _items.DefaultCellStyle.ForeColor = GlassTeal.Text;
        _items.DefaultCellStyle.SelectionBackColor = GlassTeal.Selection; _items.DefaultCellStyle.SelectionForeColor = GlassTeal.Text;
        _items.EnableHeadersVisualStyles = false; _items.ColumnHeadersDefaultCellStyle.BackColor = GlassTeal.Caption; _items.ColumnHeadersDefaultCellStyle.ForeColor = GlassTeal.Text;
        _items.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        foreach (var title in new[] { "Объект", "Основание" }) _items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, ReadOnly = true });
        foreach (var item in items)
        {
            var row = _items.Rows[_items.Rows.Add(false, item.Kind == "folder" ? item.Path : item.Hive + " / " + item.View + " / " + item.Path, item.Evidence)]; row.Tag = item;
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
            if (MessageBox.Show(this, "Удалить выбранные объекты («" + app.Name + "»): " + selected.Count + "?\nУдаление файлов необратимо.", Text,
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
                MessageBox.Show(this, errors.Count == 0 ? "Выбранные объекты удалены. Результат записан в local-data." : string.Join("\n", errors), Text);
            }
            finally { _busy = false; _items.Enabled = _close.Enabled = true; _remove.Enabled = _items.Rows.Count > 0; }
        };
    }
}
