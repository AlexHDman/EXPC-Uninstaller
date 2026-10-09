namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private void ShowUpdates()
    {
        if (_operationBusy || _versionCancellation != null) return;
        (AppEntry App, VersionResult Result)[] Entries() => (_catalogs[0]?.Entries ?? []).Select(a => (App: a, Result: ResultFor(a)))
            .Where(x => x.Result != null).Select(x => (x.App, x.Result!)).ToArray();
        using var dialog = new UpdatesWindow(Entries(), ApplyUpdate, _ignoredUpdates,
            async (app, owner) => { if (EditSource(app, owner)) await CheckVersions([app]); },
            apps => CheckVersions(apps), Entries);
        dialog.ShowDialog(this);
    }
    private async Task<string> ApplyUpdate(AppEntry app, VersionResult reviewed, IProgress<string> progress)
    {
        if (!UpdatePolicy.Ready(app, reviewed) || reviewed.Package == null) return L.T("ManagerIdentityReview");
        _operationBusy = true; UpdateActionState();
        try
        {
            progress.Report(L.T("RevalidatingUpdate"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var current = new PackageManagers(); await current.Discover(timeout.Token);
            var live = await Task.Run(FastCatalog.ReadRegistry);
            var appNow = live.Entries.SingleOrDefault(a => PortableData.Identity(a) == PortableData.Identity(app));
            if (appNow == null || appNow.Version != app.Version) return L.T("UpdatePlanChanged");
            var candidate = current.Evaluate(appNow, RuleFor(appNow), _bindings.ContainsKey(PortableData.Identity(appNow)));
            var selected = reviewed.Package;
            if (!candidate.CanInstall || candidate.Package?.Id != selected.Id || candidate.Package.Manager != selected.Manager
                || candidate.Package.Latest != selected.Latest || candidate.Package.Scope != selected.Scope) return L.T("UpdatePlanChanged");
            progress.Report(L.T("InstallingUpdate"));
            // Cancellation stops the queue, never kills an installer tree after it has begun.
            var result = await InstallPackage(candidate.Package, progress);
            progress.Report(L.T("VerifyingUpdate"));
            using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var after = new PackageManagers(); await after.Discover(verificationTimeout.Token);
            var packages = after.Packages.Where(p => p.Id == selected.Id && p.Manager == selected.Manager && p.Scope == selected.Scope).ToArray();
            var registrations = await Task.Run(FastCatalog.ReadRegistry);
            var verified = packages.Length == 1 && ReleaseVersion.Compare(packages[0].Installed, selected.Latest) == 0
                && registrations.Entries.Any(a => selected.Manager == "WinGet" ? PackageManagers.Matches(a, packages[0])
                    : a.Name == app.Name && a.Publisher == app.Publisher && ReleaseVersion.Compare(a.Version, selected.Latest) == 0);
            var outcome = UpdateOutcome(result.ExitCode, verified);
            OperationLog.Write("package-upgrade", selected.Manager + "/" + selected.Id, outcome + " / " + result.ExitCode);
            _versions.Remove(PortableData.Identity(app));
            await LoadCatalog(true);
            return L.T(outcome) + " (" + result.ExitCode + ")";
        }
        catch (Exception ex)
        { return L.T("UpdateNotVerified") + " (" + ex.GetType().Name + ")"; }
        finally { _operationBusy = false; UpdateActionState(); }
    }
    internal static string UpdateOutcome(int exitCode, bool versionVerified)
    {
        var reboot = exitCode is 3010 or 1641 or unchecked((int)0x8A150109) or unchecked((int)0x8A15010A) or unchecked((int)0x8A15010B);
        return !versionVerified ? reboot ? "UpdateNotVerifiedReboot" : "UpdateNotVerified"
            : reboot ? "UpdateVerifiedReboot" : exitCode == 0 ? "UpdateVerified" : "UpdateVerifiedWithError";
    }
    private async Task<CommandResult> InstallPackage(ManagedPackage package, IProgress<string> progress)
    {
        if (!package.IdentityConfirmed || !package.Applicable || package.Pinned || !PackageManagers.SafeId(package.Id)
            || ReleaseVersion.Parse(package.Latest) == null) throw new InvalidOperationException(L.T("ManagerIdentityReview"));
        if (package.Manager == "WinGet") return await ManagerCommand.Run(PackageManagers.WingetPath, PackageManagers.WingetUpgradeArguments(package), CancellationToken.None, false, progress);
        if (package.Manager == "Scoop" && package.Scope == "User" && PackageManagers.SafeId(package.Source))
            return await ManagerCommand.Run(PackageManagers.PowerShell, ["-NoProfile", "-NonInteractive", "-File", PackageManagers.ScoopPath, "update", package.Id], CancellationToken.None, false, progress);
        if (package.Manager != "Chocolatey") throw new InvalidOperationException();
        if (!Confirm(L.T("ChocolateyConsent") + "\n" + package.Id + " " + package.Latest + "\n" + package.Source)) return new(1223, "", "");
        var args = PackageManagers.ChocolateyUpgradeArguments(package, true);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            return await ManagerCommand.Run(PackageManagers.ChocoPath, args, CancellationToken.None, false, progress);
        var start = new System.Diagnostics.ProcessStartInfo(PackageManagers.ChocoPath) { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException();
        await process.WaitForExitAsync(); return new(process.ExitCode, "", "");
    }
}

internal sealed class UpdatesWindow : Form
{
    public UpdatesWindow((AppEntry App, VersionResult Result)[] entries, Func<AppEntry, VersionResult, IProgress<string>, Task<string>> apply,
        IgnoredUpdates? ignored = null, Func<AppEntry, IWin32Window, Task>? edit = null,
        Func<AppEntry[], Task>? recheck = null, Func<(AppEntry App, VersionResult Result)[]>? reload = null)
    {
        ignored ??= new();
        Text = L.T("Updates"); ClientSize = new Size(900, 520); MinimumSize = new Size(700, 400);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9);
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            EditMode = DataGridViewEditMode.EditProgrammatically, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        foreach (var key in new[] { "ProgramColumn", "VersionColumn", "SourceVersion", "Source", "StatusColumn" })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T(key), ReadOnly = true });
        var log = new TextBox { Dock = DockStyle.Bottom, Height = 85, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
        var note = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(6) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button Add(string key) { var b = new ThemedButton { Text = L.T(key), AutoSize = true }; buttons.Controls.Add(b); return b; }
        var close = Add("Close"); var install = Add("InstallSelected"); var ignore = Add("IgnoreSelected"); var restore = Add("RestoreUpdates");
        var check = Add("RecheckUpdates"); var source = Add("SourceEditor"); var download = Add("DownloadPage");
        var showIgnored = new CheckBox { Text = L.T("ShowIgnored"), AutoSize = true }; buttons.Controls.Add(showIgnored);
        Controls.Add(grid); Controls.Add(log); Controls.Add(note); Controls.Add(buttons);
        bool busy = false, filling = false, cancel = false;
        var selected = new HashSet<string>();
        (AppEntry App, VersionResult Result) Entry(DataGridViewRow row) => entries[(int)row.Tag!];
        string Id(DataGridViewRow row) => row.Cells[0].Tag as string ?? UpdatePolicy.Identity(Entry(row).App);
        bool Ready(DataGridViewRow row) => !ignored.Contains(Entry(row).App) && UpdatePolicy.Ready(Entry(row).App, Entry(row).Result);
        DataGridViewRow[] Checked() => grid.Rows.Cast<DataGridViewRow>().Where(r => selected.Contains(Id(r))).ToArray();
        void Selection()
        {
            var rows = Checked(); var ready = rows.Count(Ready);
            install.Enabled = !busy && ready > 0;
            ignore.Enabled = restore.Enabled = !busy && rows.Length > 0;
            source.Enabled = !busy && edit != null && grid.CurrentRow != null;
            check.Enabled = !busy && recheck != null && rows.Length > 0;
            download.Enabled = !busy && grid.CurrentRow is { } r && Uri.TryCreate(Entry(r).Result.Source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
            note.Text = L.F("UpdateSelectionCount", ready, rows.Length - ready);
        }
        void Details()
        {
            if (!busy && grid.CurrentRow is { } row)
            {
                var e = Entry(row);
                log.Text = e.App.Name + "\r\n" + L.T(UpdatePolicy.Reason(e.App, e.Result)) + "\r\n" + L.T(e.Result.Compatibility) + "\r\n" + e.Result.Source;
            }
            Selection();
        }
        void Fill()
        {
            filling = true;
            var sort = grid.SortedColumn?.Index; var direction = grid.SortOrder;
            var currentId = grid.CurrentRow is { } old ? Id(old) : null;
            grid.Rows.Clear();
            for (var i = 0; i < entries.Length; i++)
            {
                var e = entries[i]; if (ignored.Contains(e.App) && !showIgnored.Checked) continue;
                var reason = L.T(UpdatePolicy.Reason(e.App, e.Result));
                var index = grid.Rows.Add(selected.Contains(UpdatePolicy.Identity(e.App)), e.App.Name, e.Result.Installed,
                    e.Result.Latest.Length == 0 ? UiText.Empty : e.Result.Latest, e.Result.Source, "");
                var row = grid.Rows[index]; row.Tag = i; row.Cells[0].Tag = UpdatePolicy.Identity(e.App);
                row.Cells[5].ToolTipText = L.T("Status" + e.Result.State) + "\n" + reason + "\n" + L.T(e.Result.Compatibility) + "\n" + e.Result.Source;
                if (currentId == Id(row)) grid.CurrentCell = row.Cells[1];
            }
            if (sort is > 0) grid.Sort(grid.Columns[sort.Value], direction == SortOrder.Descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending);
            filling = false; Details();
        }
        void Toggle(DataGridViewRow row) { if (!busy) row.Cells[0].Value = !selected.Contains(Id(row)); }
        grid.CellMouseClick += (_, e) => { if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == 0) Toggle(grid.Rows[e.RowIndex]); };
        grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { e.Handled = e.SuppressKeyPress = true; if (grid.CurrentRow is { } r) Toggle(r); } };
        grid.CellValueChanged += (_, e) =>
        {
            if (filling || e.ColumnIndex != 0 || e.RowIndex < 0) return;
            var row = grid.Rows[e.RowIndex]; if (row.Cells[0].Value is true) selected.Add(Id(row)); else selected.Remove(Id(row)); Selection();
        };
        grid.CurrentCellChanged += (_, _) => { if (!filling) Details(); };
        grid.CellPainting += (_, e) =>
        {
            if (e.ColumnIndex != 5 || e.RowIndex < 0) return;
            e.PaintBackground(e.ClipBounds, true); var r = e.CellBounds; var size = 16 * DeviceDpi / 96;
            StatusVisual.Draw(e.Graphics!, new Rectangle(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size), Entry(grid.Rows[e.RowIndex]).Result.State); e.Handled = true;
        };
        async Task Work(Func<Task> action)
        {
            busy = true; grid.Enabled = buttons.Enabled = false; string? error = null;
            try { await action(); }
            catch (Exception ex) { error = ex.Message; }
            finally { busy = false; grid.Enabled = buttons.Enabled = true; if (reload != null) entries = reload(); Fill(); if (error != null) log.Text = error; }
        }
        source.Click += async (_, _) => { if (grid.CurrentRow is { } row && edit != null) { var app = Entry(row).App; await Work(() => edit(app, this)); } };
        check.Click += async (_, _) => { if (recheck != null) { var apps = Checked().Select(r => Entry(r).App).ToArray(); await Work(() => recheck(apps)); } };
        void Ignore(bool value)
        {
            try { foreach (var row in Checked()) ignored.Set(Entry(row).App, value); if (reload != null) entries = reload(); Fill(); }
            catch (Exception ex) { log.Text = ex.Message; }
        }
        ignore.Click += (_, _) => Ignore(true); restore.Click += (_, _) => Ignore(false);
        showIgnored.CheckedChanged += (_, _) => Fill();
        download.Click += (_, _) => { try { if (grid.CurrentRow is { } row) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VersionChecker.PublicUrl(Entry(row).Result.Source).AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { log.Text = ex.Message; } };
        close.Click += (_, _) => { if (busy) { cancel = true; log.Text = L.T("CancelUpdateQueue"); } else Close(); };
        install.Click += async (_, _) =>
        {
            var chosen = Checked(); var ready = chosen.Where(Ready).ToArray(); if (ready.Length == 0) return;
            var summary = L.F("UpdateSelectionCount", ready.Length, chosen.Length - ready.Length) + "\n" + string.Join("\n", chosen.Select(r => Entry(r).App.Name + ": " + L.T(UpdatePolicy.Reason(Entry(r).App, Entry(r).Result))));
            if (Dialogs.Show(this, summary, L.T("ConfirmUpdates"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            busy = true; cancel = false; grid.Enabled = showIgnored.Enabled = false; Selection(); close.Text = L.T("StopQueue");
            try
            {
                foreach (var row in ready)
                {
                    if (cancel) break;
                    var e = Entry(row);
                    var outcome = await apply(e.App, e.Result, new Progress<string>(text => log.Text = text));
                    log.Text = outcome;
                    row.Cells[5].ToolTipText = outcome; selected.Remove(Id(row)); row.Cells[0].Value = false;
                    // The reviewed plan is consumed. Recheck is required before another installation.
                    var verified = new[] { "UpdateVerified", "UpdateVerifiedReboot", "UpdateVerifiedWithError" }.Any(key => outcome.StartsWith(L.T(key) + " (", StringComparison.Ordinal));
                    entries[(int)row.Tag!] = (e.App, e.Result with { State = verified ? VersionState.Unchecked : VersionState.Error, Package = null, Reason = "UpdatePlanChanged" });
                }
            }
            finally { busy = false; grid.Enabled = showIgnored.Enabled = true; close.Text = UiText.Close; Selection(); }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; cancel = true; } };
        Fill(); Theme.Apply(this);
    }
}
