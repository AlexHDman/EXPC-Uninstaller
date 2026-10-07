using System.Reflection;
using System.Text.Json;
using ExpcUninstaller;

internal static class ManagerAcceptance
{
    public static int Run(string managerName)
    {
        int exit = 1; Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
            L.Select("ru"); Theme.Select("Glass Amber");
            using var window = new MainWindow();
            window.Shown += async (_, _) =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    var manager = new PackageManagers(); await manager.Discover(timeout.Token);
                    var catalog = FastCatalog.ReadRegistry();
                    if (managerName == "winget")
                    {
                        var app = catalog.Entries.Single(a => a.Metadata?.KeyName.Equals("7-Zip", StringComparison.OrdinalIgnoreCase) == true);
                        var rule = VersionChecker.Resolve(app, VersionChecker.Rules());
                        var result = manager.Evaluate(app, rule, false);
                        File.WriteAllText(@"C:\EXPC-Results\winget-reviewed.json", JsonSerializer.Serialize(new { app.Name, app.Version, result, manager.Availability }));
                        if (!result.CanInstall || result.Package?.Id != "7zip.7zip") throw new InvalidOperationException("Exact 7-Zip update was not eligible");
                        var versions = (Dictionary<string, VersionResult>)typeof(MainWindow).GetField("_versions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                        versions[PortableData.Identity(app)] = result;
                        typeof(MainWindow).GetMethod("ApplyView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [true]); window.Update();
                        // Explicit, fixture-only test command invokes the same application action after review.
                        var task = (Task<string>)typeof(MainWindow).GetMethod("ApplyUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [app, result, new Progress<string>()])!;
                        var outcome = await task;
                        var after = FastCatalog.ReadRegistry().Entries.Single(a => a.Metadata?.KeyName.Equals("7-Zip", StringComparison.OrdinalIgnoreCase) == true);
                        var pass = ReleaseVersion.Compare(after.Version, result.Latest) == 0 && outcome.StartsWith(L.T("UpdateVerified"));
                        File.WriteAllText(@"C:\EXPC-Results\winget-update.json", JsonSerializer.Serialize(new { pass, Before = app.Version, Requested = result.Latest, After = after.Version, outcome,
                            RingModel = result.CanInstall, Launch = "Explicit isolated test command -> MainWindow.ApplyUpdate; physical button not exercised" }));
                        exit = pass ? 0 : 1;
                    }
                    else
                    {
                        var package = manager.Packages.Single(p => p.Manager == "Scoop" && p.Id == "7zip");
                        // Scoop's official 7-Zip manifest is portable and creates no ARP entry. Do not invent one.
                        var shellEntry = catalog.Entries.Any(a => a.Metadata?.Location.StartsWith(PackageManagers.ScoopRoot, StringComparison.OrdinalIgnoreCase) == true);
                        if (!package.Applicable || package.Pinned || ReleaseVersion.Compare(package.Installed, package.Latest) >= 0) throw new InvalidOperationException("Scoop fixture not applicable");
                        File.WriteAllText(@"C:\EXPC-Results\scoop-reviewed.json", JsonSerializer.Serialize(new { package, RegistryEntryPresent = shellEntry, manager.Availability }));
                        var command = await ManagerCommand.Run(PackageManagers.PowerShell, ["-NoProfile", "-NonInteractive", "-File", PackageManagers.ScoopPath, "update", "7zip"], CancellationToken.None, false);
                        File.WriteAllText(@"C:\EXPC-Results\scoop-update-output.txt", command.Output + command.Error);
                        var refreshed = new PackageManagers(); await refreshed.Discover(timeout.Token);
                        var after = refreshed.Packages.Single(p => p.Manager == "Scoop" && p.Id == "7zip");
                        var pass = command.ExitCode == 0 && ReleaseVersion.Compare(after.Installed, package.Latest) == 0;
                        File.WriteAllText(@"C:\EXPC-Results\scoop-update.json", JsonSerializer.Serialize(new { BackendPass = pass, Before = package.Installed, Requested = package.Latest,
                            After = after.Installed, command.ExitCode, RegistryEntryPresent = shellEntry, FullShellPipeline = "NOT TESTED: official portable package has no uninstall registration; no artificial registration added" }));
                        exit = pass ? 0 : 1;
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { typeof(MainWindow).GetField("_exitRequested", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true); window.Close(); }
            };
            Application.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) File.WriteAllText(@"C:\EXPC-Results\" + managerName + "-error.txt", failure.ToString());
        return exit;
    }
}
