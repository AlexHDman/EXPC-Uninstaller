using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using ExpcUninstaller;

internal static class Program
{
    private static readonly string TestRoot = @"C:\EXPC-Phase03-Fixtures";
    private static readonly List<object> Results = [];
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        // This binary is never distributed. Refuse all fixture writes outside Windows Sandbox.
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(@"C:\EXPC-Test") || !File.Exists(@"C:\EXPC-Test\ALLOW-SANDBOX-TESTS")) return 99;
        if (args.Length >= 2 && args[0] == "fixture")
        {
            var mode = args[1];
            if (mode == "nochange") return 0;
            if (mode == "cancel") return 1602;
            if (mode == "error") return 1603;
            if (mode == "parent")
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "fixture", "child" } });
                await Task.Delay(600); return 0;
            }
            if (mode == "child") await Task.Delay(3000);
            Registry.CurrentUser.DeleteSubKeyTree(Registration.Parent + @"\EXPC-Phase03-Test", false);
            File.WriteAllText(TestRoot + @"\child-complete.txt", "complete");
            return mode == "reboot" ? 3010 : 0;
        }
        Directory.CreateDirectory(TestRoot);
        try
        {
            if (args.Contains("msi-only"))
            {
                var result = await MsiFixture.Run();
                File.WriteAllText(@"C:\EXPC-Results\msi-result.json", JsonSerializer.Serialize(result));
                return result.Removed ? 0 : 1;
            }
            foreach (var mode in new[] { "cancel", "error", "parent", "reboot", "nochange" })
            {
                var app = Install(mode);
                var timer = Stopwatch.StartNew();
                var result = await ProductActions.Uninstall(app, CancellationToken.None);
                Check(mode, mode switch { "cancel" => result.State == "cancelled" && ProductIdentity.IsRegistered(app), "error" => result.State == "error" && ProductIdentity.IsRegistered(app), "nochange" => result.State == "unconfirmed" && ProductIdentity.IsRegistered(app), "parent" => result.Removed && timer.ElapsedMilliseconds >= 3000 && File.Exists(TestRoot + @"\child-complete.txt"), _ => result.Removed && result.Reboot }, result);
            }
            var cancellationApp = Install("parent");
            using (var cancellation = new CancellationTokenSource(200))
            {
                var cancelled = await ProductActions.Uninstall(cancellationApp, cancellation.Token);
                Check("stop-waiting", cancelled.State == "cancelled", cancelled);
            }
            await Task.Delay(4000);
            Check("stop-does-not-kill", !ProductIdentity.IsRegistered(cancellationApp), "external child completed after wait cancellation");
            var record = Install("error");
            await RegistryBackup.RemoveRecord(record);
            Check("record-only", !ProductIdentity.IsRegistered(record) && File.Exists(record.Metadata!.Location + @"\keep.txt"), "registration absent, files retained");
            Check("registry-backups", Directory.EnumerateFiles(Path.Combine(OperationLog.DirectoryPath, "registry-backups"), "*.reg").Any(f => File.ReadAllText(f).Contains("EXPC-Phase03-Test")), "nonempty .reg export contains exact registration");
            var forced = Install("error");
            Registry.CurrentUser.DeleteSubKeyTree(Registration.Parent + @"\EXPC-Phase03-Test", false);
            var leftovers = BcuTargetedJunkAdapter.Find(forced);
            Check("targeted-search", leftovers.Any(x => x.Kind == "folder") && leftovers.Any(x => x.Kind == "registry"), leftovers);
            foreach (var item in leftovers.OrderBy(x => x.Kind == "folder" ? 1 : 0)) await BcuTargetedJunkAdapter.Delete(forced, item);
            Check("forced-cleanup", !Directory.Exists(forced.Metadata!.Location) && File.Exists(TestRoot + @"\EXPC Test Vendor\Other Product\foreign.txt"), "own folder removed, sibling retained");
            var shared = Install("error");
            using (var other = Registry.CurrentUser.CreateSubKey(Registration.Parent + @"\EXPC-Phase03-Other"))
            {
                other.SetValue("DisplayName", "EXPC Test Product Plus"); other.SetValue("Publisher", "EXPC Test Vendor");
                other.SetValue("InstallLocation", shared.Metadata!.Location + @"\Other Product");
            }
            Check("overlap-protection", BcuTargetedJunkAdapter.Find(shared).Count == 0, "registered nested product blocks cleanup");
            Registry.CurrentUser.DeleteSubKeyTree(Registration.Parent + @"\EXPC-Phase03-Other", false);
            var common = shared with { Metadata = shared.Metadata! with { Location = TestRoot + @"\EXPC Test Vendor" } };
            Check("publisher-root-protection", !CleanupBoundary.SafeDirectory(common, []), "shared publisher directory refused");
            using (var key = Registry.CurrentUser.OpenSubKey(Registration.Parent + @"\EXPC-Phase03-Test", true)) key!.SetValue("DisplayVersion", "changed");
            bool rejected = false; try { ProductIdentity.RequireUnchanged(shared); } catch (InvalidOperationException) { rejected = true; }
            Check("identity-change", rejected, "changed registration refused");
            var msiResult = await MsiFixture.Run();
            Check("msi-install-uninstall", msiResult.Removed, msiResult);
            var preview = Install("error");
            Directory.CreateSymbolicLink(preview.Metadata!.Location + @"\foreign-link", TestRoot + @"\EXPC Test Vendor\Other Product");
            var linkRejected = false; try { BcuTargetedJunkAdapter.Find(preview); } catch (IOException) { linkRejected = true; }
            Check("junction-protection", linkRejected && File.Exists(TestRoot + @"\EXPC Test Vendor\Other Product\foreign.txt"), "reparse point prevents scan and deletion");
            Directory.Delete(preview.Metadata.Location + @"\foreign-link");
            using (var unrelated = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\EXPC Test Vendor\EXPC Test Product Plus")) unrelated.SetValue("InstallLocation", preview.Metadata.Location);
            Check("similar-registry-name", !BcuTargetedJunkAdapter.Find(preview).Any(x => x.Path.EndsWith("Product Plus")), "similar name not a candidate");
            File.WriteAllText(@"C:\EXPC-Results\preview-product.json", JsonSerializer.Serialize(preview));
            File.WriteAllText(@"C:\EXPC-Results\results.json", JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\EXPC-Results\failure.txt", ex.ToString());
            File.WriteAllText(@"C:\EXPC-Results\results.json", JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
    private static void Check(string name, bool pass, object evidence)
    {
        Results.Add(new { name, pass, evidence });
        if (!pass) throw new Exception("FAILED: " + name);
    }
    private static AppEntry Install(string mode)
    {
        var location = TestRoot + @"\EXPC Test Vendor\EXPC Test Product";
        Directory.CreateDirectory(location);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(location, Path.GetFileName(file)), true);
        File.WriteAllText(location + @"\keep.txt", "owned test data");
        Directory.CreateDirectory(TestRoot + @"\EXPC Test Vendor\Other Product");
        File.WriteAllText(TestRoot + @"\EXPC Test Vendor\Other Product\foreign.txt", "must remain");
        using (var key = Registry.CurrentUser.CreateSubKey(Registration.Parent + @"\EXPC-Phase03-Test"))
        {
            key.SetValue("DisplayName", "EXPC Test Product"); key.SetValue("DisplayVersion", "1.0");
            key.SetValue("Publisher", "EXPC Test Vendor"); key.SetValue("InstallLocation", location);
            key.SetValue("UninstallString", "\"" + location + @"\ExpcSandboxTests.exe" + "\" fixture " + mode);
        }
        using (var settings = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\EXPC Test Vendor\EXPC Test Product")) settings.SetValue("InstallLocation", location);
        return FastCatalog.ReadRegistry().Entries.Single(x => x.Name == "EXPC Test Product");
    }
}
