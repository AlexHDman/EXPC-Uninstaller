using System.Reflection;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
namespace ExpcUninstallerTests;
[TestClass]
public sealed class CompletionCandidateTests
{
    private static AppEntry App => new("fixture-scope-x64", "Fixture", "1.0", null, null, null, "Fixture Vendor") { Sources = ["CurrentUser/Registry64/fixture"], Metadata = new("fixture", "Fixture", "1.0", "Fixture Vendor", @"C:\Fixture", "", "", "", "", null, "", "") };
    private static VersionResult Manual => new(VersionState.Update, "1.0", "2.0", "https://example.com/download", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ComparedBranch", "CompatibilityUnknown", "");
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(25))); if (error != null) throw error;
    }
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);
    [TestMethod] public void IgnoreSurvivesVersionChangeButNotDifferentInstallations()
    {
        var previous = PortableData.DirectoryPath; var dir = Path.Combine(Path.GetTempPath(), "EXPC-ignore-" + Guid.NewGuid());
        try
        {
            PortableData.DirectoryPath = dir; var store = new IgnoredUpdates(); store.Set(App, true);
            var loaded = new IgnoredUpdates(); Assert.IsTrue(loaded.Contains(App with { Name = "Fixture 3.0", Version = "3.0" }));
            Assert.IsFalse(loaded.Contains(App with { Sources = ["LocalMachine/Registry32/fixture"] }));
            Assert.IsFalse(loaded.Contains(App with { Metadata = App.Metadata! with { Location = @"C:\Other" } }));
            Assert.IsFalse(loaded.Contains(App with { Id = "another-component" }));
            loaded.Set(App, false); Assert.IsFalse(new IgnoredUpdates().Contains(App));
        }
        finally { PortableData.DirectoryPath = previous; if (Directory.Exists(dir)) { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); } }
    }
    [TestMethod] public void ManualSelectionIgnoreRestoreAndEditedSourceRecheckStayInSameWindow() => Sta(() =>
    {
        var old = PortableData.DirectoryPath; var dir = Path.Combine(Path.GetTempPath(), "EXPC-dialog-" + Guid.NewGuid());
        try
        {
            PortableData.DirectoryPath = dir; var ignored = new IgnoredUpdates(); var current = Manual; int edited = 0, checkedCount = 0;
            using var form = new UpdatesWindow([(App, current)], (_, _, _) => throw new AssertFailedException("No installation allowed"), ignored,
                (_, _) => { edited++; current = Manual with { Latest = "3.0", State = VersionState.Unknown, Reason = "AmbiguousVersion" }; return Task.CompletedTask; },
                _ => { checkedCount++; current = Manual with { State = VersionState.Current, Latest = "1.0" }; return Task.CompletedTask; },
                () => [(App, current)]);
            var grid = form.Controls.OfType<DataGridView>().Single(); var panel = form.Controls.OfType<FlowLayoutPanel>().Single();
            Button Button(string key) => panel.Controls.OfType<Button>().Single(b => b.Text == L.T(key));
            void Toggle() => typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [new DataGridViewCellMouseEventArgs(0, 0, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))]);
            grid.CurrentCell = grid.Rows[0].Cells[1]; Toggle();
            Assert.IsTrue(grid.Rows[0].Cells[0].Value is true); Assert.IsFalse(Button("InstallSelected").Enabled);
            Click(Button("IgnoreSelected")); Assert.AreEqual(0, grid.Rows.Count); Assert.IsTrue(new IgnoredUpdates().Contains(App));
            panel.Controls.OfType<CheckBox>().Single().Checked = true; Assert.AreEqual(1, grid.Rows.Count);
            Click(Button("RestoreUpdates")); Assert.IsFalse(new IgnoredUpdates().Contains(App));
            grid.CurrentCell = grid.Rows[0].Cells[1]; Click(Button("SourceEditor"));
            Assert.AreEqual(1, edited); Assert.AreEqual("3.0", grid.Rows[0].Cells[3].Value); Assert.IsTrue(grid.Rows[0].Cells[0].Value is true);
            Click(Button("RecheckUpdates")); Assert.AreEqual(1, checkedCount); StringAssert.Contains(grid.Rows[0].Cells[5].ToolTipText, L.T("StatusCurrent"));
            Assert.IsFalse(form.Visible); Assert.IsFalse(form.IsDisposed);
        }
        finally { PortableData.DirectoryPath = old; if (Directory.Exists(dir)) { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); } }
    });
    [TestMethod] public async Task UnknownOffersCapturedManualRouteWithoutScanningOrRelabellingSuccess()
    {
        AppEntry? pending = null; int scans = 0;
        await new StandardRemoval().Run(App, new() { Uninstall = (_, _) => Task.FromResult(ProductActions.Completion(0, true, false)), Find = _ => { scans++; return []; } }, _ => { }, () => Task.CompletedTask, _ => Assert.Fail(), _ => true, a => pending = a);
        Assert.IsNotNull(pending); Assert.AreEqual(App.Metadata, pending.Metadata); Assert.AreEqual(0, scans);
        foreach (var exit in new[] { 1602, 1603 })
            await new StandardRemoval().Run(App, new() { Uninstall = (_, _) => Task.FromResult(ProductActions.Completion(exit, true, false)) }, _ => { }, () => Task.CompletedTask, _ => Assert.Fail(), _ => true, _ => Assert.Fail("Cancellation/error must not be success or automatic cleanup"));
    }
    [TestMethod] public void MsiFallbackNeedsIndependentProductStateAndRegistrationAbsence()
    {
        const string code = "{D6B3D38A-13AA-46F9-883B-085E380BAFB7}";
        Assert.IsTrue(ProductActions.MsiFallback(@"C:\Windows\System32\msiexec.exe", code, true, 0, -1));
        Assert.IsFalse(ProductActions.MsiFallback(@"C:\Example\uninstall.exe", code, true, 0, -1));
        Assert.IsFalse(ProductActions.MsiFallback("msiexec.exe", code, false, 0, -1));
        Assert.IsFalse(ProductActions.MsiFallback("msiexec.exe", code, true, 1602, -1));
        Assert.IsFalse(ProductActions.MsiFallback("msiexec.exe", code, true, 0, 5));
    }
    [TestMethod] public void ExcludedRegistryValueProtectsSelectedParentAndNewValuesBlockDeletion()
    {
        var root = new JunkItem("registry", @"SOFTWARE\Fixture\Product", "fixture");
        var key = new CleanupEntry(root, root.Path, false, "CleanupUserData");
        var profile = new CleanupEntry(root, root.Path, false, "CleanupUserData", "Profile", "fixture");
        var another = new CleanupEntry(root, root.Path, false, "CleanupUserData", "Cache", "fixture");
        var plan = CleanupPlan.Build([key, profile, another], new HashSet<CleanupEntry> { key, another });
        CollectionAssert.AreEqual(new[] { another }, plan.ToArray());
        Sta(() => { using var form = new OperationWindow(App, 2, () => Task.CompletedTask, initial: [key, profile, another]); Assert.IsFalse(form.Visible); var section = form.Tree.Nodes.Cast<TreeNode>().Single(n => n.Text == L.T("CleanupUserData")); form.Toggle(section); Assert.AreEqual(1, section.StateImageIndex); form.Toggle(section.Nodes[0].Nodes[0]); Assert.AreEqual(2, section.StateImageIndex); });
    }
    [TestMethod] public void DriverAndWebsiteNeverBecomeAutomaticInstallByColorOrSelection()
    {
        var package = new ManagedPackage { Id = "Fixture.Driver", Manager = "WinGet", Installed = "1.0", Latest = "2.0", IdentityConfirmed = true, Applicable = true };
        Assert.IsTrue((Manual with { Package = package }).CanInstall);
        Assert.IsFalse(UpdatePolicy.Ready(App with { Name = "Rutoken Drivers" }, Manual with { Package = package }));
        Assert.IsFalse(UpdatePolicy.Ready(App, Manual)); Assert.AreEqual("SourceOnlyUpdate", Manual.SelectionReason);
        Assert.AreEqual(VersionState.Update, MainWindow.DisplayState(Manual));
        foreach (var theme in Theme.Names) { Theme.Select(theme); Assert.AreEqual(Color.FromArgb(65, 159, 244), StatusVisual.ColorFor(VersionState.Update)); }
        Theme.Select("Glass Amber");
    }
    [TestMethod] public void SharedPublisherDataIsNotAProductCandidateAndLightPaletteIsPreserved()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.IsFalse(BcuTargetedJunkAdapter.SafeLinkedData(App, Path.Combine(roaming, App.Publisher)));
        Assert.IsFalse(BcuTargetedJunkAdapter.SafeLinkedData(App, roaming));
        Assert.IsTrue(BcuTargetedJunkAdapter.SafeLinkedData(App, Path.Combine(roaming, App.Name)));
        Theme.Select("Windows Light");
        Assert.AreEqual(Color.FromArgb(246,247,248), GlassTeal.Background);
        Assert.AreEqual(Theme.Blend(GlassTeal.Background, Color.FromArgb(68,174,175), .24f), GlassTeal.Selection);
        Theme.Select("Glass Amber");
    }
}
