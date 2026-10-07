using System.Diagnostics;

namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private readonly List<VersionRule> _rules = VersionChecker.Rules();
    private readonly Dictionary<string, VersionRule> _bindings = PortableData.Load<Dictionary<string, VersionRule>>("sources");
    private readonly Dictionary<string, VersionResult> _versions = PortableData.Load<Dictionary<string, VersionResult>>("versions");
    private CancellationTokenSource? _versionCancellation;
    private GlassButton _check = null!;
    private ContextMenuStrip _context = null!;
    private bool _layoutColumns;
    private int _lastNameWidth;
    private void WindowsThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (Theme.Selected != "Windows" || IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { if (!IsDisposed) { Theme.Select("Windows"); Theme.Apply(this); } });
    }
    private VersionRule? RuleFor(AppEntry app) => _bindings.GetValueOrDefault(PortableData.Identity(app)) ?? VersionChecker.Resolve(app, _rules);
    private VersionResult? ResultFor(AppEntry app)
    {
        var value = _versions.GetValueOrDefault(PortableData.Identity(app));
        return value?.Installed == app.Version && value.RuleStamp == (RuleFor(app)?.Stamp ?? "") ? value : null;
    }
    private static string StatusName(VersionState state) => L.T("Status" + state);
    private async Task CheckVersions(IEnumerable<AppEntry> entries)
    {
        if (_versionCancellation != null) { _versionCancellation.Cancel(); return; }
        var apps = entries.ToArray(); if (apps.Length == 0) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _versionCancellation = cancellation;
        var completed = 0;
        _tips.SetToolTip(_check, L.T("CancelCheck"));
        UpdateActionState();
        using var slots = new SemaphoreSlim(3);
        try
        {
            var tasks = apps.Select(async app =>
            {
                await slots.WaitAsync(cancellation.Token);
                try
                {
                    var key = PortableData.Identity(app); var rule = RuleFor(app); var previous = ResultFor(app);
                    // Regex/JSON parsing must not run on the UI synchronization context.
                    var result = await Task.Run(() => new VersionChecker().Check(app, rule, cancellation.Token, previous));
                    _versions[key] = result;
                    _status = L.F("CheckProgress", ++completed, apps.Length); Invalidate(); _grid.Invalidate();
                }
                finally { slots.Release(); }
            }).ToArray();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _versionCancellation = null;
            if (!_lifetime.IsCancellationRequested)
            {
                _status = L.F(cancellation.IsCancellationRequested ? "CheckCancelled" : "CheckFinished", completed, apps.Length);
                try { PortableData.Save("versions", _versions); } catch (Exception ex) { ShowDataError(ex); }
                _tips.SetToolTip(_check, UiText.CheckVersions); UpdateActionState(); ApplyView(); Invalidate();
            }
        }
    }
    private void ShowVersion(AppEntry app)
    {
        var result = ResultFor(app); var rule = RuleFor(app);
        var content = StatusName(result?.State ?? VersionState.Unchecked) + "\n\n" + L.T("InstalledVersion") + ": " + app.Version
            + "\n" + L.T("FoundVersion") + ": " + (result?.Latest is { Length: > 0 } version ? version : UiText.Empty)
            + "\n" + L.T("Source") + ": " + (result?.Source ?? rule?.Url ?? UiText.Empty)
            + "\n" + L.T("CheckedAt") + ": " + (result?.CheckedAt?.ToLocalTime().ToString("g") ?? UiText.Empty)
            + "\n" + L.T("AttemptedAt") + ": " + (result?.AttemptedAt.ToLocalTime().ToString("g") ?? UiText.Empty)
            + "\n" + L.T("Channel") + ": " + (rule?.Channel ?? UiText.Empty)
            + "\n" + L.T("Branch") + ": " + (rule?.Branch is { Length: > 0 } branch ? branch : L.T("Unspecified"))
            + "\n\n" + L.T(result?.Reason ?? "NoCheck") + "\n\n" + L.T(result?.Compatibility ?? rule?.Compatibility ?? "CompatibilityUnknown");
        using var dialog = new DetailWindow(app.Name, content, rule?.Url); dialog.ShowDialog(this);
    }
    private void EditSource(AppEntry app)
    {
        using var dialog = new SourceWindow(app, RuleFor(app));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var key = PortableData.Identity(app);
        var updated = new Dictionary<string, VersionRule>(_bindings);
        if (dialog.Rule == null) updated.Remove(key); else updated[key] = dialog.Rule;
        try
        {
            PortableData.Save("sources", updated);
            _bindings.Clear(); foreach (var pair in updated) _bindings.Add(pair.Key, pair.Value);
            _versions.Remove(key); _grid.Invalidate();
        }
        catch (Exception ex) { ShowDataError(ex); }
    }
    private void ShowDataError(Exception ex) => Dialogs.Show(this, L.T("DataSaveError") + "\n" + ex.Message, UiText.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void SavePreferences()
    {
        try { PortableData.Save("settings", Program.Settings); }
        catch (Exception ex) { ShowDataError(ex); }
    }
    private void SetTheme(string theme)
    {
        Theme.Select(theme); Program.Settings.Theme = theme; SavePreferences(); Theme.Apply(this); RefreshMenus();
    }
    private void SetLanguage(string language)
    {
        L.Select(language); Program.Settings.Language = language; SavePreferences();
        _search.PlaceholderText = UiText.Search; _search.AccessibleName = UiText.Search;
        for (var i = 0; i < 6; i++) _grid.Columns[i].HeaderText = UiText.Columns[i];
        SizeColumns();
        string[] tips = [UiText.Delete, UiText.RefreshHint, UiText.CheckVersions, UiText.Theme, UiText.Language, UiText.Programs, UiText.StoreHint];
        for (var i = 0; i < _actions.Count; i++) { _tips.SetToolTip(_actions[i], tips[i]); _actions[i].AccessibleName = tips[i]; }
        string[] captions = [UiText.Minimize, UiText.Maximize, UiText.Close];
        for (var i = 0; i < 3; i++) { _tips.SetToolTip(_caption[i], captions[i]); _caption[i].AccessibleName = captions[i]; }
        _status = UiText.Ready; RefreshMenus(); ApplyView(); Invalidate(true);
    }
    private void RefreshMenus()
    {
        _operationMenus.Clear(); _menu.Items.Clear(); BuildMenu();
        _context?.Dispose(); _context = new ContextMenuStrip { Renderer = new GlassRenderer(), ShowItemToolTips = true };
        AddActions(_context.Items); _context.Opening += (_, _) => UpdateActionState(); _grid.ContextMenuStrip = _context;
        UpdateActionState();
    }
    private void AddActions(ToolStripItemCollection items)
    {
        foreach (var action in new[] { 0, 1, 2, 3, -1, 4, 5, 6, 7, -1, 9, 8 })
            items.Add(action < 0 ? new ToolStripSeparator() : ActionItem(action));
    }
    private void PickAppearance(Control anchor, bool language)
    {
        var popup = new ContextMenuStrip { Renderer = new GlassRenderer() };
        if (language) foreach (var pair in L.Languages) popup.Items.Add(new ToolStripMenuItem(pair.Value, null, (_, _) => SetLanguage(pair.Key)) { Checked = pair.Key == L.Language });
        else foreach (var theme in Theme.Names) popup.Items.Add(new ToolStripMenuItem(theme == "Windows" ? L.T("AsWindows") : theme, null, (_, _) => SetTheme(theme)) { Checked = theme == Theme.Selected });
        popup.Closed += (_, _) => BeginInvoke(popup.Dispose); popup.Show(anchor, new Point(0, anchor.Height));
    }
    private void SizeColumns()
    {
        _layoutColumns = true;
        try
        {
            int[] defaults = [0, 80, 98, 86, 68, 65];
            int[] minimum = [150, 65, 86, 65, 68, 55];
            var widths = Program.Settings.ColumnWidths;
            for (var i = 1; i < 6; i++)
            {
                // Leave room for translated headings and the sort glyph at the current DPI.
                _grid.Columns[i].MinimumWidth = Math.Max(S(minimum[i]), TextRenderer.MeasureText(_grid.Columns[i].HeaderText, Font).Width + S(22));
                _grid.Columns[i].Width = Math.Max(_grid.Columns[i].MinimumWidth, S(widths.Length == 6 ? Math.Clamp(widths[i], minimum[i], 250) : defaults[i]));
            }
            var available = _grid.ClientSize.Width;
            var overflow = _grid.Columns.Cast<DataGridViewColumn>().Skip(1).Sum(c => c.Width) + S(150) - available;
            for (var i = 5; i >= 1 && overflow > 0; i--)
            {
                var reduction = Math.Min(overflow, _grid.Columns[i].Width - _grid.Columns[i].MinimumWidth);
                _grid.Columns[i].Width -= reduction; overflow -= reduction;
            }
            _grid.Columns[0].Width = Math.Max(S(150), available - _grid.Columns.Cast<DataGridViewColumn>().Skip(1).Sum(c => c.Width));
            _lastNameWidth = _grid.Columns[0].Width;
        }
        finally { _layoutColumns = false; }
    }
    private void ColumnResized(object? sender, DataGridViewColumnEventArgs e)
    {
        if (_layoutColumns || !_shown) return;
        _layoutColumns = true;
        if (e.Column.Index == 0) _grid.Columns[1].Width = Math.Max(_grid.Columns[1].MinimumWidth, _grid.Columns[1].Width - (e.Column.Width - _lastNameWidth));
        Program.Settings.ColumnWidths = _grid.Columns.Cast<DataGridViewColumn>().Select(c => (int)Math.Round(c.Width * 96d / DeviceDpi)).ToArray();
        _layoutColumns = false; SizeColumns();
        // Persist at form close rather than writing on every pixel of a drag.
    }
}

internal sealed class DetailWindow : Form
{
    public DetailWindow(string title, string content, string? source)
    {
        Text = title; ClientSize = new Size(570, 390); MinimumSize = new Size(460, 280); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9);
        var text = new TextBox { Text = content.ReplaceLineEndings(Environment.NewLine), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
        var close = new Button { Text = UiText.Close, Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.Cancel };
        Controls.Add(text); Controls.Add(close); CancelButton = close;
        Shown += (_, _) => { text.SelectionLength = 0; close.Focus(); };
        if (!string.IsNullOrEmpty(source))
        {
            var open = new Button { Text = L.T("OpenSource"), Dock = DockStyle.Bottom, Height = 34 };
            open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(VersionChecker.PublicUrl(source).AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { Dialogs.Show(this, ex.Message); } };
            Controls.Add(open);
        }
        Theme.Apply(this);
    }
}

internal sealed class SourceWindow : Form
{
    public VersionRule? Rule { get; private set; }
    public SourceWindow(AppEntry app, VersionRule? existing)
    {
        Text = L.T("SourceEditor") + " — " + app.Name; ClientSize = new Size(600, 420); MinimumSize = new Size(520, 400);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterParent;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        string[] providers = ["page", "github", "winget", "manual"];
        provider.Items.AddRange(providers.Select(p => L.T("Provider_" + p)).ToArray());
        provider.SelectedIndex = Math.Max(0, Array.IndexOf(providers, existing?.Provider ?? "page"));
        TextBox Field(string value) => new() { Text = value, Dock = DockStyle.Fill };
        var url = Field(existing?.Url ?? app.Metadata?.Website ?? "");
        var pattern = Field(existing?.Pattern ?? @"(?<version>\d+\.\d+(?:\.\d+)*)");
        var branch = Field(existing?.Branch ?? ""); var winget = Field(existing?.WingetId ?? "");
        var note = new Label { AutoSize = true, MaximumSize = new Size(540, 0), Text = L.T("SourceHelp"), Dock = DockStyle.Fill };
        var result = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, Height = 78, ScrollBars = ScrollBars.Vertical };
        void Row(string label, Control control) { var row = panel.RowCount++; panel.Controls.Add(new Label { Text = L.T(label), AutoSize = true, Padding = new Padding(0, 5, 0, 5) }, 0, row); panel.Controls.Add(control, 1, row); }
        Row("Provider", provider); Row("SourceUrl", url); Row("Extraction", pattern); Row("Branch", branch); Row("WingetId", winget);
        Row("Note", note); Row("TrialResult", result);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var cancel = new Button { Text = L.T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = L.T("Save"), AutoSize = true }; var test = new Button { Text = L.T("TestSource"), AutoSize = true };
        var reset = new Button { Text = L.T("UseCatalog"), AutoSize = true };
        buttons.Controls.AddRange([cancel, save, test, reset]); Controls.Add(panel); Controls.Add(buttons); CancelButton = cancel;
        // The token is owned until FormClosed, including in-flight trial requests.
        var trial = new CancellationTokenSource(); FormClosed += (_, _) => { trial.Cancel(); };
        Disposed += (_, _) => trial.Dispose();
        VersionRule Read()
        {
            var address = url.Text.Trim(); var kind = providers[provider.SelectedIndex];
            if (address.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) && kind == "page") kind = "github";
            VersionChecker.PublicUrl(address);
            var known = VersionChecker.Rules().SingleOrDefault(r => r.Url.Equals(address, StringComparison.OrdinalIgnoreCase) && r.Matches(app));
            if (known != null && pattern.Text == @"(?<version>\d+\.\d+(?:\.\d+)*)" && kind == "page") return known;
            if (kind is "page" or "github")
            {
                var expression = new System.Text.RegularExpressions.Regex(pattern.Text, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(200));
                if (pattern.Text.Length > 1500 || !expression.GetGroupNames().Contains("version")) throw new FormatException(L.T("ExtractionRequired"));
            }
            return new VersionRule { Id = "custom", Provider = kind, Url = address, Pattern = pattern.Text, Branch = branch.Text.Trim(), WingetId = winget.Text.Trim(),
                Channel = "user-selected", Compatibility = existing?.Compatibility ?? "CompatibilityUnknown", ManualReason = existing?.ManualReason is { Length: > 0 } reason ? reason : "ManualSource" };
        }
        save.Click += (_, _) => { try { Rule = Read(); DialogResult = DialogResult.OK; } catch (Exception ex) { result.Text = ex.Message; } };
        reset.Click += (_, _) => { Rule = null; DialogResult = DialogResult.OK; };
        test.Click += async (_, _) =>
        {
            test.Enabled = save.Enabled = false;
            try
            {
                var rule = Read(); var value = await Task.Run(() => new VersionChecker().Check(app, rule, trial.Token));
                if (!IsDisposed) result.Text = L.T("Status" + value.State) + " · " + (value.Latest.Length > 0 ? value.Latest : UiText.Empty) + "\r\n" + L.T(value.Reason);
            }
            catch (Exception ex) { if (!IsDisposed) result.Text = ex.Message; }
            finally { if (!IsDisposed) test.Enabled = save.Enabled = true; }
        };
        Theme.Apply(this);
    }
}
