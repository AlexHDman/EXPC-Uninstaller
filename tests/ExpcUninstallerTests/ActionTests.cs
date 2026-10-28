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

    [TestMethod]
    public void ObservedVersionDirectoryAbsentIsCheckedWithoutDeletionAuthority()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"version-fixture-"+Guid.NewGuid().ToString("N"));
        var version=Path.Combine(root,"8.5.1.1302");Directory.CreateDirectory(version);
        var icon=Path.Combine(version,"client.exe");File.WriteAllText(icon,"fixture");
        var app=App(version) with {Name="Platform 8",Version="8.5.1.1302",Sources=[]};
        app=app with {Metadata=app.Metadata! with {KeyName=Guid.NewGuid().ToString("B"),WindowsInstaller="1",Icon=icon,Uninstall="msiexec.exe /x "+Guid.NewGuid().ToString("B")}};
        var observed=BcuTargetedJunkAdapter.CaptureProbePaths(app,[]);Assert.AreEqual(1,observed.Length);
        Assert.AreEqual(0,BcuTargetedJunkAdapter.CaptureInstallPaths(app,[]).Length);
        var other=App(version) with {Sources=["other"]};
        Assert.AreEqual(0,BcuTargetedJunkAdapter.CaptureProbePaths(app,[other]).Length);
        app=app with {ProbeOnlyInstallPaths=observed};
        var remaining=BcuTargetedJunkAdapter.Scan(app,default,new([],0),registryAbsent:(_,_,_)=>true);
        Assert.IsNotNull(remaining.Warning);Assert.AreEqual(0,remaining.Items.Count);
        var denied=BcuTargetedJunkAdapter.Scan(app,default,new([],0),_=>throw new UnauthorizedAccessException("fixture denied"));
        Assert.IsNotNull(denied.Warning);
        File.Delete(icon);Directory.Delete(version);
        var absent=BcuTargetedJunkAdapter.Scan(app,default,new([],0),registryAbsent:(_,_,_)=>true);
        Assert.IsNull(absent.Warning);Assert.AreEqual(0,absent.Items.Count);Assert.IsTrue(absent.CheckedScopes>0);
        var unknown=BcuTargetedJunkAdapter.Scan(app with {ProbeOnlyInstallPaths=[]},default,new([],0));Assert.IsNotNull(unknown.Warning);
        Directory.Delete(root);
    }
    [TestMethod]
    public void KnownRemainingFileIsReportedWithinSafeProductRoot()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"known-"+Guid.NewGuid().ToString("N"),"Product");Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"known.txt"),"fixture");
        var found=BcuTargetedJunkAdapter.Scan(App(root) with {Sources=[]},default,new([],0));
        Assert.IsTrue(found.Items.Any(x=>x.Path==root));
    }

    [TestMethod]
    public async Task StandardRemovalPreservesObservationAcrossDisappearingRegistrationAndDirectory()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"route-"+Guid.NewGuid().ToString("N"),"8.5.1.1302");Directory.CreateDirectory(root);
        var icon=Path.Combine(root,"client.exe");File.WriteAllText(icon,"fixture");
        var app=App(root) with {Name="Platform 8",Version="8.5.1.1302",Sources=[]};
        app=app with {Metadata=app.Metadata! with {KeyName=Guid.NewGuid().ToString("B"),WindowsInstaller="1",Icon=icon,Uninstall="msiexec.exe /x "+Guid.NewGuid().ToString("B")}};
        bool checkedResult=false;
        await new StandardRemoval().Run(app,new(){Catalog=()=>new([],0),Uninstall=(snapshot,_)=>{
            Assert.AreEqual(1,snapshot.ProbeOnlyInstallPaths.Length);File.Delete(icon);Directory.Delete(root);
            return Task.FromResult(ProductActions.Completion(0,true,true));
        }},_=>{},()=>Task.CompletedTask,_=>Assert.Fail("Must use injected scanner"),_=>true,search:(snapshot,warning)=>{
            Assert.AreEqual(app.Metadata,snapshot.Metadata);
            var scan=BcuTargetedJunkAdapter.Scan(snapshot,default,new([],0),registryAbsent:(_,_,_)=>true);
            Assert.IsNull(scan.Warning);Assert.AreEqual(0,scan.Items.Count);Assert.IsTrue(scan.CheckedScopes>0);checkedResult=true;return Task.CompletedTask;
        });
        Assert.IsTrue(checkedResult);
    }
}
