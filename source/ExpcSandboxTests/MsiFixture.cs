using System.Diagnostics;
using Microsoft.Win32;
using ExpcUninstaller;

internal static class MsiFixture
{
    public static async Task<OperationResult> Run()
    {
        var file = Path.Combine(@"C:\EXPC-Phase03-Fixtures", "fixture-" + Guid.NewGuid().ToString("N") + ".msi");
        var code = "{446281CD-369C-4759-81E2-67A1D68A0193}";
        var installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer") ?? throw new Exception("Windows Installer COM unavailable");
        dynamic installer = Activator.CreateInstance(installerType)!;
        dynamic database = installer.OpenDatabase(file, 3);
        void Sql(string sql)
        {
            dynamic view = database.OpenView(sql.Replace('[', (char)96).Replace(']', (char)96)); view.Execute(); view.Close(); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(view);
        }
        Sql("CREATE TABLE [Property] ([Property] CHAR(72) NOT NULL, [Value] CHAR(0) LOCALIZABLE PRIMARY KEY [Property])");
        void Property(string name, string value) => Sql("INSERT INTO [Property] ([Property], [Value]) VALUES ('" + name + "', '" + value + "')");
        Property("ProductCode", code); Property("ProductName", "EXPC MSI Fixture"); Property("ProductVersion", "1.0.0");
        Property("Manufacturer", "EXPC Sandbox Tests"); Property("ProductLanguage", "1033"); Property("INSTALLLEVEL", "1");
        Property("UpgradeCode", "{6DE74E1B-E001-41D3-87A1-D810C16E0394}"); Property("ARPNOMODIFY", "1"); Property("ARPNOREPAIR", "1");
        Sql("CREATE TABLE [Directory] ([Directory] CHAR(72) NOT NULL, [Directory_Parent] CHAR(72), [DefaultDir] CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY [Directory])");
        Sql("INSERT INTO [Directory] ([Directory], [DefaultDir]) VALUES ('TARGETDIR', 'SourceDir')");
        Sql("CREATE TABLE [Component] ([Component] CHAR(72) NOT NULL, [ComponentId] CHAR(38), [Directory_] CHAR(72) NOT NULL, [Attributes] SHORT NOT NULL, [Condition] CHAR(255), [KeyPath] CHAR(72) PRIMARY KEY [Component])");
        Sql("INSERT INTO [Component] ([Component], [ComponentId], [Directory_], [Attributes], [KeyPath]) VALUES ('FixtureComponent', '{5238F91B-10D6-4B77-A412-F918DFE43B7B}', 'TARGETDIR', 4, 'FixtureRegistry')");
        Sql("CREATE TABLE [Feature] ([Feature] CHAR(38) NOT NULL, [Feature_Parent] CHAR(38), [Title] CHAR(64) LOCALIZABLE, [Description] CHAR(255) LOCALIZABLE, [Display] SHORT, [Level] SHORT NOT NULL, [Directory_] CHAR(72), [Attributes] SHORT NOT NULL PRIMARY KEY [Feature])");
        Sql("INSERT INTO [Feature] ([Feature], [Title], [Display], [Level], [Attributes]) VALUES ('MainFeature', 'Test fixture', 1, 1, 0)");
        Sql("CREATE TABLE [FeatureComponents] ([Feature_] CHAR(38) NOT NULL, [Component_] CHAR(72) NOT NULL PRIMARY KEY [Feature_], [Component_])");
        Sql("INSERT INTO [FeatureComponents] ([Feature_], [Component_]) VALUES ('MainFeature', 'FixtureComponent')");
        Sql("CREATE TABLE [Registry] ([Registry] CHAR(72) NOT NULL, [Root] SHORT NOT NULL, [Key] CHAR(255) NOT NULL LOCALIZABLE, [Name] CHAR(255) LOCALIZABLE, [Value] CHAR(0) LOCALIZABLE, [Component_] CHAR(72) NOT NULL PRIMARY KEY [Registry])");
        Sql("INSERT INTO [Registry] ([Registry], [Root], [Key], [Name], [Value], [Component_]) VALUES ('FixtureRegistry', 1, 'Software\\EXPC Sandbox Tests\\MsiFixture', 'Installed', 'yes', 'FixtureComponent')");
        Sql("CREATE TABLE [Media] ([DiskId] SHORT NOT NULL, [LastSequence] SHORT NOT NULL, [DiskPrompt] CHAR(64) LOCALIZABLE, [Cabinet] CHAR(255), [VolumeLabel] CHAR(32), [Source] CHAR(72) PRIMARY KEY [DiskId])");
        Sql("INSERT INTO [Media] ([DiskId], [LastSequence]) VALUES (1, 0)");
        Sql("CREATE TABLE [InstallExecuteSequence] ([Action] CHAR(72) NOT NULL, [Condition] CHAR(255), [Sequence] SHORT PRIMARY KEY [Action])");
        foreach (var (action, sequence) in new (string, int)[] { ("CostInitialize", 800), ("FileCost", 900), ("CostFinalize", 1000), ("InstallValidate", 1400), ("InstallInitialize", 1500), ("ProcessComponents", 1600), ("UnpublishFeatures", 1800), ("RemoveRegistryValues", 2600), ("RemoveFiles", 3500), ("WriteRegistryValues", 5000), ("RegisterUser", 6000), ("RegisterProduct", 6100), ("PublishFeatures", 6300), ("PublishProduct", 6400), ("InstallFinalize", 6600) })
            Sql("INSERT INTO [InstallExecuteSequence] ([Action], [Sequence]) VALUES ('" + action + "', " + sequence + ")");
        dynamic summary = database.SummaryInformation(20);
        summary.Property[1] = 1252; summary.Property[2] = "Installation Database"; summary.Property[7] = "Intel;1033";
        summary.Property[9] = Guid.NewGuid().ToString("B"); summary.Property[14] = 200; summary.Property[15] = 0; summary.Persist();
        database.Commit();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(summary);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(database);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(installer);
        using var install = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
        { UseShellExecute = false, ArgumentList = { "/i", file, "/qn", "/norestart", "/l*v", @"C:\EXPC-Results\msi-install.log" } })!;
        await install.WaitForExitAsync();
        if (install.ExitCode != 0) throw new Exception("MSI fixture install: " + install.ExitCode);
        var app = FastCatalog.ReadRegistry().Entries.Single(x => x.Name == "EXPC MSI Fixture");
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\EXPC Sandbox Tests\MsiFixture"))
            if (key?.GetValue("Installed") as string != "yes") throw new Exception("MSI component not installed");
        // Keep exact MSI product code but suppress fixture UI to permit unattended isolated testing.
        using (var key = Registration.Parse(app.Sources[0]).Open(true)) key!.SetValue("UninstallString", app.Metadata!.Uninstall + " /qn");
        app = FastCatalog.ReadRegistry().Entries.Single(x => x.Name == "EXPC MSI Fixture");
        var result = await ProductActions.Uninstall(app, CancellationToken.None);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\EXPC Sandbox Tests\MsiFixture"))
            if (key?.GetValue("Installed") != null) throw new Exception("MSI component survived uninstall");
        return result;
    }
}
