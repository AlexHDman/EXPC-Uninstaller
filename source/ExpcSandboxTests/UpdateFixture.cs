using System.Diagnostics;
using System.Text.Json;
using ExpcUninstaller;

internal static class UpdateFixture
{
    public static async Task<int> Commands()
    {
        bool cancelled = false;
        using (var token = new CancellationTokenSource(250))
        {
            try { await ManagerCommand.Run(Environment.ProcessPath!, ["command-fixture", "readonly"], token.Token); }
            catch (OperationCanceledException) { cancelled = true; }
        }
        var watch = Stopwatch.StartNew();
        using var installation = new CancellationTokenSource(50);
        var result = await ManagerCommand.Run(Environment.ProcessPath!, ["command-fixture", "installer"], installation.Token, false);
        var activeInstallerNotKilled = result.ExitCode == 0 && watch.ElapsedMilliseconds >= 900;
        File.WriteAllText(@"C:\EXPC-Results\command-fixtures.json", JsonSerializer.Serialize(new { cancelled, activeInstallerNotKilled }));
        return cancelled && activeInstallerNotKilled ? 0 : 1;
    }
    public static async Task<int> Run(string mode)
    {
        // Same outer Sandbox + sentinel gate as uninstall tests. Only this exact disposable package is allowed.
        const string id = "expc.sandbox.update.fixture";
        var app = FastCatalog.ReadRegistry().Entries.Single(x => x.Metadata?.KeyName == "EXPC-Update-Fixture");
        if (app.Publisher != "EXPC Sandbox Tests") throw new InvalidOperationException();
        var rule = new VersionRule { Provider = "chocolatey", WingetId = id, ConfirmManagedIdentity = true,
            ConfirmedCompatibilityVersion = mode == "success" ? "2.0.0" : "3.0.0", ConfirmedCompatibilitySystem = PackageManagers.CompatibilitySystem };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var manager = new PackageManagers(); await manager.Discover(timeout.Token);
        var result = manager.Evaluate(app, rule, true);
        var expected = mode == "success" ? "2.0.0" : "3.0.0";
        if (!result.CanInstall || result.Package?.Id != id || result.Latest != expected)
            throw new Exception("Fixture mapping failed: " + JsonSerializer.Serialize(new { result, manager.Availability, manager.Packages }));
        var watch = Stopwatch.StartNew();
        var command = await ManagerCommand.Run(PackageManagers.ChocoPath, PackageManagers.ChocolateyUpgradeArguments(result.Package, true), CancellationToken.None, false);
        var after = FastCatalog.ReadRegistry().Entries.Single(x => x.Metadata?.KeyName == "EXPC-Update-Fixture");
        var verified = ReleaseVersion.Compare(after.Version, expected) == 0;
        var outcome = MainWindow.UpdateOutcome(command.ExitCode, verified);
        var pass = mode == "success" ? verified && command.ExitCode == 0 : !verified && command.ExitCode != 0 && after.Version == "2.0.0";
        File.WriteAllText(@"C:\EXPC-Results\update-" + mode + ".json", JsonSerializer.Serialize(new { pass, mode, app.Version, result.Latest, InstalledAfter = after.Version,
            command.ExitCode, outcome, watch.ElapsedMilliseconds, result.Source, manager.Availability }, new JsonSerializerOptions { WriteIndented = true }));
        return pass ? 0 : 1;
    }
}
