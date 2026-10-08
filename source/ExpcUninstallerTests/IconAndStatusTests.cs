using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class IconAndStatusTests
{
    [TestMethod]
    [DataRow("\"C:\\App With Space\\app.dll\",-5", "C:\\App With Space\\app.dll", -5)]
    [DataRow("@C:\\App\\app.exe,0", "C:\\App\\app.exe", 0)]
    [DataRow("\"C:\\App\\app.exe,3\"", "C:\\App\\app.exe", 3)]
    [DataRow("C:\\App, Inc\\app.exe", "C:\\App, Inc\\app.exe", 0)]
    public void DisplayIconQuotesAndResourceIndexes(string raw, string path, int index)
    { Assert.AreEqual((path, index), LazyIcons.Parse(raw)); }

    [TestMethod] public void NativeIconsAndStandaloneIcoAreOwnedAndFileIsReleased()
    {
        var exe = typeof(MainWindow).Assembly.Location.Replace(".dll", ".exe");
        using var image = LazyIcons.Load(exe); Assert.IsNotNull(image); Assert.IsTrue(image.Width >= 24);
        var path = Path.Combine(Path.GetTempPath(), "EXPC-icon-" + Guid.NewGuid() + ".ico");
        using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Expc.Icon.ico")!)
        using (var file = File.Create(path)) stream.CopyTo(file);
        using var icon = LazyIcons.Load('"' + path + "\",0"); Assert.IsNotNull(icon);
        File.Delete(path); Assert.IsFalse(File.Exists(path));
    }
    [TestMethod] public void StoreAssetsAreBoundedToDeclaredPackageDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "EXPC-store-icon-" + Guid.NewGuid()); Directory.CreateDirectory(Path.Combine(root, "Assets"));
        File.WriteAllText(Path.Combine(root, "AppxManifest.xml"), "<Package><VisualElements Square44x44Logo='Assets/Logo.png' Square150x150Logo='../foreign.png'/></Package>");
        File.WriteAllText(Path.Combine(root, "Assets", "Logo.targetsize-48_altform-unplated.png"), "fixture");
        var paths = LazyIcons.StoreCandidates(root).ToArray();
        Assert.IsTrue(paths.Any(x => x.EndsWith("Logo.targetsize-48_altform-unplated.png")));
        Assert.IsTrue(paths.All(x => x.StartsWith(root + Path.DirectorySeparatorChar)));
        Assert.IsNull(LazyIcons.Load(paths.Last())); // Corrupt bitmap fails safely.
    }
    [TestMethod] public void ManualUpdateAndUnknownAreNotUnverifiedSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        var web = new VersionResult(VersionState.Update, "1.0", "2.0", "https://example.com", now, now, "ComparedBranch", "CompatibilityUnknown", "");
        Assert.AreEqual(VersionState.Update, MainWindow.DisplayState(web)); Assert.IsFalse(web.CanInstall);
        Assert.AreEqual(VersionState.Unchecked, MainWindow.DisplayState(null));
        Assert.AreEqual(VersionState.Unknown, MainWindow.DisplayState(web with { State = VersionState.Unknown }));
        foreach (var state in new[] { VersionState.Current, VersionState.Error, VersionState.Authentication, VersionState.Ambiguous, VersionState.Cancelled })
        { Assert.AreEqual(state, MainWindow.DisplayState(web with { State = state })); Assert.IsFalse((web with { State = state }).CanInstall); }
    }
}
