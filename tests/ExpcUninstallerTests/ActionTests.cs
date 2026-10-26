using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class ActionTests
{
    private static AppEntry App(string root = @"C:\EXPC-Fixture\Vendor\Product", string publisher = "Vendor") => new("id", "Product", "1", null, null, null, publisher)
    {
        Sources = ["CurrentUser/Registry64/Fixture"],
        Metadata = new("Fixture", "Product", "1", publisher, root, "\"" + root + @"\uninstall.exe" + "\" /remove", "", "", "", null, "0", "0")
    };
    [TestMethod]
    public void QuotedExeKeepsArgumentsExactly()
    {
        var parsed = UninstallCommand.Parse("\"C:\\Program Files\\Acme Tool\\remove.exe\" /remove /log \"C:\\a b\\file.txt\"", _ => true);
        Assert.AreEqual(@"C:\Program Files\Acme Tool\remove.exe", parsed.FileName);
        Assert.AreEqual("/remove /log \"C:\\a b\\file.txt\"", parsed.Arguments);
    }
    [TestMethod]
    public void UnquotedSpacePathIsNeverHandedToShellAsCombinedCommand()
    {
        var parsed = UninstallCommand.Parse(@"C:\Program Files\Acme Tool\remove.exe /S", _ => true);
        Assert.AreEqual(@"C:\Program Files\Acme Tool\remove.exe", parsed.FileName); Assert.AreEqual("/S", parsed.Arguments);
    }
    [TestMethod]
    public void MsiMaintenanceBecomesUninstallAndCannotForceReboot()
    {
        var parsed = UninstallCommand.Parse(@"MsiExec.exe /I{11111111-2222-3333-4444-555555555555} /forcerestart REBOOT=Force UI=full");
        Assert.StartsWith("/x {11111111-2222-3333-4444-555555555555}", parsed.Arguments);
        Assert.DoesNotContain("/forcerestart", parsed.Arguments); Assert.DoesNotContain("REBOOT=Force", parsed.Arguments);
        Assert.Contains("REBOOT=ReallySuppress", parsed.Arguments); Assert.Contains("UI=full", parsed.Arguments);
    }
    [TestMethod]
    [DataRow("remove.exe /S")]
    [DataRow(@"\\server\share\remove.exe")]
    [DataRow(@"C:\Windows\System32\cmd.exe /c del x")]
    [DataRow("\"C:\\broken.exe")]
    [DataRow(@"msiexec.exe /i other.msi")]
    public void AmbiguousOrInterpreterCommandsAreRejected(string command) => Assert.Throws<FormatException>(() => UninstallCommand.Parse(command, _ => true));
    [TestMethod]
    public void AmbiguousExecutablePrefixIsRejectedInsteadOfExecutingWrongFile()
    {
        Assert.Throws<FormatException>(() => UninstallCommand.Parse(@"C:\Folder.exe Subdir\uninstall.exe /remove", _ => true));
        var parsed = UninstallCommand.Parse(@"C:\Folder.exe Subdir\uninstall.exe /remove", p => p.EndsWith("uninstall.exe"));
        Assert.AreEqual(@"C:\Folder.exe Subdir\uninstall.exe", parsed.FileName);
    }
    [TestMethod]
    public void RegistrationAddressCannotEscapeUninstallParent()
    {
        var r = Registration.Parse("LocalMachine/Registry32/Exact Product");
        Assert.AreEqual(RegistryView.Registry32, r.View);
        Assert.Throws<InvalidOperationException>(() => Registration.Parse(@"CurrentUser/Registry64/X\..\Other"));
        Assert.Throws<InvalidOperationException>(() => Registration.Parse("Users/Registry64/X"));
    }
    [TestMethod]
    public void FolderBoundaryDistinguishesSiblingPrefixAndNestedProduct()
    {
        var app = App();
        Assert.IsTrue(CleanupBoundary.SafeDirectory(app, []));
        var sibling = App(@"C:\EXPC-Fixture\Vendor\Product Plus") with { Sources = ["CurrentUser/Registry64/Other"] };
        Assert.IsTrue(CleanupBoundary.SafeDirectory(app, [sibling]));
        var nested = App(@"C:\EXPC-Fixture\Vendor\Product\Other") with { Sources = ["CurrentUser/Registry64/Other"] };
        Assert.IsFalse(CleanupBoundary.SafeDirectory(app, [nested]));
        Assert.IsFalse(CleanupBoundary.Overlap(@"C:\x\Product", @"C:\x\Product Plus"));
    }
    [TestMethod]
    public void ManufacturerAndSystemRootsAreNeverCleanupTargets()
    {
        Assert.IsFalse(CleanupBoundary.SafeDirectory(App(@"C:\EXPC-Fixture\Vendor"), []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(App(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(App(@"C:\Program Files"), []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(App(@"C:\EXPC-Fixture\Common Files\Product"), []));
    }
    [TestMethod]
    public void NameAloneAndExternalUninstallerCannotEstablishOwnership()
    {
        var app = App();
        Assert.IsFalse(CleanupBoundary.SafeDirectory(app with { Metadata = app.Metadata! with { Uninstall = @"C:\different\uninstall.exe" } }, []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(app with { Metadata = app.Metadata! with { Location = "" } }, []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(app with { Store = true }, []));
        Assert.IsFalse(CleanupBoundary.SafeDirectory(app with { Protected = true }, []));
    }
    [TestMethod]
    public void WindowsPathAliasesAreRejected()
    {
        Assert.IsFalse(CleanupBoundary.LocalPath(@"C:\Windows.\Product"));
        Assert.IsFalse(CleanupBoundary.LocalPath(@"C:\Program Files \Product"));
        Assert.IsFalse(CleanupBoundary.LocalPath(@"C:\x\..\Product"));
        Assert.IsFalse(CleanupBoundary.LocalPath(@"C:\x\file:stream"));
        Assert.IsTrue(CleanupBoundary.LocalPath(@"C:\Products\Sample\"));
    }
    [TestMethod]
    public void DedupPreservesEveryRegistrationForRevalidation()
    {
        var metadata = App().Metadata!;
        var rows = FastCatalog.Deduplicate([("CurrentUser/Registry64/Fixture", metadata), ("CurrentUser/Registry32/Fixture", metadata)]);
        Assert.HasCount(1, rows); Assert.HasCount(2, rows[0].Sources); Assert.AreEqual(metadata, rows[0].Metadata);
    }
}
