namespace ExpcUninstaller;

internal sealed partial class MainWindow
{
    private void ShowUpdates()
    {
        if (_operationBusy || _versionCancellation != null) return;
        var entries = (_catalogs[0]?.Entries ?? []).Select(a => (App: a, Result: ResultFor(a)))
            .Where(x => x.Result?.State is VersionState.Update or VersionState.Manual && ReleaseVersion.Compare(x.Result.Installed, x.Result.Latest) < 0).ToArray();
        using var dialog = new UpdatesWindow(entries.Select(x => (x.App, x.Result!)).ToArray(), ApplyUpdate);
        dialog.ShowDialog(this);
    }
    private async Task<string> ApplyUpdate(AppEntry app, VersionResult reviewed, IProgress<string> progress)
    {
        if (!reviewed.CanInstall || reviewed.Package == null) return L.T("ManagerIdentityReview");
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
    public UpdatesWindow((AppEntry App, VersionResult Result)[] entries, Func<AppEntry, VersionResult, IProgress<string>, Task<string>> apply)
    {
        Text = L.T("Updates"); ClientSize = new Size(860, 460); MinimumSize = new Size(650, 360); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9);
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Width = 32, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        foreach (var key in new[] { "ProgramColumn", "VersionColumn", "NewColumn", "Source", "UpdateResult" }) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T(key), ReadOnly = true });
        foreach (var entry in entries)
        {
            var i = grid.Rows.Add(false, entry.App.Name, entry.Result.Installed, entry.Result.Latest, entry.Result.Source,
                L.T(entry.Result.CanInstall ? "ReadyForUpdate" : entry.Result.Reason));
            grid.Rows[i].Cells[0].ReadOnly = !entry.Result.CanInstall;
        }
        var log = new TextBox { Dock = DockStyle.Bottom, Height = 80, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
        var note = new Label { Text = L.T("UpdateConsent"), Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        var bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 7, Style = ProgressBarStyle.Marquee, Visible = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var close = new Button { Text = UiText.Close, AutoSize = true }; var install = new Button { Text = L.T("InstallSelected"), AutoSize = true };
        buttons.Controls.AddRange([close, install]); Controls.Add(grid); Controls.Add(log); Controls.Add(note); Controls.Add(bar); Controls.Add(buttons);
        bool busy = false, cancel = false;
        close.Click += (_, _) => { if (busy) { cancel = true; close.Enabled = false; log.Text = L.T("CancelUpdateQueue"); } else Close(); };
        install.Click += async (_, _) =>
        {
            grid.EndEdit(); var selected = Enumerable.Range(0, entries.Length).Where(i => entries[i].Result.CanInstall && grid.Rows[i].Cells[0].Value is true)
                .DistinctBy(i => (entries[i].Result.Package!.Manager, entries[i].Result.Package!.Id, entries[i].Result.Package!.Scope)).ToArray();
            if (selected.Length == 0) return;
            if (Dialogs.Show(this, L.T("ConfirmUpdates") + "\n" + string.Join("\n", selected.Select(i => entries[i].App.Name + " → " + entries[i].Result.Latest)), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            busy = true; cancel = false; grid.Enabled = install.Enabled = false; bar.Visible = true; close.Text = L.T("StopQueue");
            try
            {
                foreach (var index in selected)
                {
                    if (cancel) break;
                    grid.Rows[index].Cells[5].Value = L.T("InstallingUpdate");
                    var progress = new Progress<string>(text => { log.AppendText(text.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine); if (log.TextLength > 20_000) log.Text = log.Text[^12_000..]; });
                    grid.Rows[index].Cells[5].Value = await apply(entries[index].App, entries[index].Result, progress);
                    grid.Rows[index].Cells[0].Value = false; grid.Rows[index].Cells[0].ReadOnly = true;
                }
            }
            finally { busy = false; grid.Enabled = close.Enabled = true; install.Enabled = false; bar.Visible = false; close.Text = UiText.Close; }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; cancel = true; log.Text = L.T("CancelUpdateQueue"); } };
        Theme.Apply(this);
    }
}
