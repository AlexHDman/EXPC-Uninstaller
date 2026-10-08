using System.Diagnostics;
using System.Reflection;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class OperationWindowTests
{
    private static AppEntry App => new("operation-fixture", "EXPC operation fixture", "1", null, null, null, "EXPC test");
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30))); if (error != null) throw error;
    }
    private static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult();
    }
    private static string FixtureDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EXPC-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    [TestMethod]
    public async Task StandardRouteDoesNotOpenAnythingUntilConfirmedAndLaunchesOnce()
    {
        var pending = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launched = 0; var scanned = 0; var shown = 0;
        var runner = new StandardRemoval(); var root = FixtureDirectory();
        try
        {
            var services = new OperationServices { Uninstall = (_, _) => { Interlocked.Increment(ref launched); return pending.Task; },
                Find = _ => { scanned++; return [new("folder", root, "Operation66")]; } };
            var run = runner.Run(App, services, _ => { }, () => Task.CompletedTask, _ => shown++);
            await runner.Run(App, services, _ => { }, () => Task.CompletedTask, _ => shown++);
            Assert.AreEqual(0, scanned); Assert.AreEqual(0, shown);
            pending.SetResult(ProductActions.Completion(0, true, true)); await run;
            Assert.AreEqual(1, launched); Assert.AreEqual(1, scanned); Assert.AreEqual(1, shown);
        }
        finally { Directory.Delete(root, false); }
    }
    [TestMethod]
    [DataRow(1602, true, true)] [DataRow(1603, true, true)] [DataRow(0, false, true)] [DataRow(0, true, false)] [DataRow(3010, true, true)]
    public async Task CancelErrorUnknownAndRebootCannotScan(int exit, bool absent, bool trace)
    {
        await new StandardRemoval().Run(App, new() { Uninstall = (_, _) => Task.FromResult(ProductActions.Completion(exit, absent, trace)),
            Find = _ => throw new AssertFailedException("No cleanup for unconfirmed removal") }, _ => { }, () => Task.CompletedTask,
            _ => throw new AssertFailedException("No EXPC window"));
    }
    [TestMethod]
    public async Task EmptySearchAndLaunchErrorOnlyReportStatus()
    {
        var status = "";
        await new StandardRemoval().Run(App, new() { Uninstall = (_, _) => Task.FromResult(ProductActions.Completion(0, true, true)), Find = _ => [] },
            s => status = s, () => Task.CompletedTask, _ => throw new AssertFailedException("Empty leftovers must not open a window"));
        Assert.AreEqual(L.T("Operation20"), status);
        await new StandardRemoval().Run(App, new() { Uninstall = (_, _) => throw new IOException("fixture launch error") },
            s => status = s, () => Task.CompletedTask, _ => throw new AssertFailedException("Launch error must not open a window"));
        StringAssert.Contains(status, "fixture launch error");
    }
    [TestMethod]
    public void UncheckedProfileSurvivesSelectedParentInRealTemporaryTree()
    {
        var root = FixtureDirectory(); var profile = Path.Combine(root, "Profiles"); Directory.CreateDirectory(profile);
        var history = Path.Combine(profile, "history.db"); var program = Path.Combine(root, "program.bin");
        File.WriteAllText(history, "synthetic user history"); File.WriteAllText(program, "synthetic program");
        try
        {
            var entries = CleanupPlan.Expand([new("folder", root, "Operation66")]);
            var chosen = entries.Where(e => e.Path == root || e.Path == program).ToHashSet();
            var plan = CleanupPlan.Build(entries, chosen);
            Assert.IsFalse(plan.Any(e => e.Path == root)); Assert.AreEqual(1, plan.Count);
            foreach (var e in plan) CleanupPlan.DeleteFileEntry(e);
            Assert.IsTrue(File.Exists(history)); Assert.IsFalse(File.Exists(program));
            // Even a stale or bypassed plan cannot recursively remove the unchecked profile.
            Assert.Throws<IOException>(() => CleanupPlan.DeleteFileEntry(entries.Single(e => e.Path == root)));
            Assert.IsTrue(File.Exists(history));
        }
        finally { File.Delete(history); if(File.Exists(program)) File.Delete(program); Directory.Delete(profile, false); Directory.Delete(root, false); }
    }
    [TestMethod]
    public void RegistryUncheckedChildBlocksSelectedAncestor()
    {
        var root = new JunkItem("registry", "SOFTWARE\\Synthetic", "Operation67");
        var parent = new CleanupEntry(root, root.Path, false, "CleanupRegistry");
        var profile = new CleanupEntry(root, root.Path + "\\Profile", false, "CleanupUserData");
        Assert.AreEqual(0, CleanupPlan.Build([parent, profile], new HashSet<CleanupEntry> { parent }).Count);
        Assert.AreEqual("CleanupUserData", CleanupPlan.Category(root.Path, true));
    }
    [TestMethod]
    public void ParentShowsPartialWhenProtectedProfileIsInAnotherSection() => Sta(() =>
    {
        var root = new JunkItem("folder", "C:\\synthetic", "Operation66");
        var parent = new CleanupEntry(root, root.Path, true, "CleanupFiles");
        var profile = new CleanupEntry(root, root.Path + "\\Profiles", true, "CleanupUserData");
        using var window = new OperationWindow(App, 2, () => Task.CompletedTask, initial: [parent, profile]);
        window.Toggle(window.Tree.Nodes[0]);
        Assert.AreEqual(2, window.Tree.Nodes[0].Nodes[0].StateImageIndex);
        Assert.AreEqual(2, window.Tree.Nodes[0].StateImageIndex);
        Assert.AreEqual(0, window.Tree.Nodes[2].StateImageIndex);
    });
    [TestMethod]
    public void ForceAndRecordStillRequireConfirmation() => Sta(() =>
    {
        var calls = 0;
        using var record = new OperationWindow(App, 3, () => Task.CompletedTask, services: new() { RemoveRecord = _ => { calls++; return Task.CompletedTask; } });
        Assert.AreEqual(0, calls); Pump(record.Advance()); Pump(record.Advance()); Assert.AreEqual(1, calls);
        using var forced = new OperationWindow(App, 1, () => Task.CompletedTask, services: new() { Find = _ => { calls++; return []; } });
        Assert.AreEqual(1, calls); Pump(forced.Advance()); Assert.AreEqual(2, calls);
        Assert.AreEqual(0, Application.OpenForms.Count);
    });
    [TestMethod] [DataRow("ru")] [DataRow("en")]
    public void SectionsPartialSelectionAndLogControlsWorkWithoutShowingForm(string language) => Sta(() =>
    {
        L.Select(language);
        var root = new JunkItem("folder", "C:\\synthetic", "Operation66");
        var a = new CleanupEntry(root, root.Path + "\\a", false, "CleanupFiles");
        var b = new CleanupEntry(root, root.Path + "\\b", false, "CleanupFiles");
        var profile = new CleanupEntry(root, root.Path + "\\Profiles", true, "CleanupUserData");
        var deleted = new List<CleanupEntry>();
        using var window = new OperationWindow(App, 2, () => Task.CompletedTask, services: new() { DeleteEntry = (_, e) => { deleted.Add(e); return Task.CompletedTask; } }, initial: [a,b,profile]);
        Assert.AreEqual(3, window.Tree.Nodes.Count);
        Assert.IsTrue(window.Tree.Nodes.Cast<TreeNode>().All(n => n.StateImageIndex == 0));
        window.Toggle(window.Tree.Nodes[0]); Assert.AreEqual(1, window.Tree.Nodes[0].StateImageIndex);
        window.Toggle(window.Tree.Nodes[0].Nodes[0]); Assert.AreEqual(2, window.Tree.Nodes[0].StateImageIndex);
        Assert.AreEqual(0, window.Tree.Nodes[2].StateImageIndex);
        Pump(window.Advance()); Assert.AreEqual(OperationStage.ConfirmCleanup, window.Stage); Assert.AreEqual(0, deleted.Count);
        Pump(window.Advance()); Assert.AreEqual(1, deleted.Count); Assert.IsFalse(deleted.Contains(profile));
        var log = window.Controls.OfType<TextBox>().Single(); Assert.IsTrue(log.TextLength > 0);
        IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
        var clear = All(window).OfType<Button>().Single(x => x.Text == L.T("CleanupClearLog"));
        var diagnostic = Path.Combine(OperationLog.DirectoryPath, "operations.jsonl"); var length = new FileInfo(diagnostic).Length;
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(clear, [EventArgs.Empty]);
        Assert.AreEqual(0, log.TextLength); Assert.AreEqual(length, new FileInfo(diagnostic).Length);
        Assert.AreEqual(0, Application.OpenForms.Count); Assert.IsFalse(window.Visible);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OperationWindow(App, 0, () => Task.CompletedTask));
    });
    [TestMethod] [DataRow(96)] [DataRow(144)]
    public void PlacementStaysOnOwnersMonitor(int dpi)
    {
        var work = new Rectangle(2560, 0, 1280, 1000); var owner = new Rectangle(3500, 50, 720, 900);
        var result = OperationWindow.Placement(owner, work, dpi); Assert.IsTrue(work.Contains(result));
    }
    [TestMethod] [DataRow("success",0)] [DataRow("cancel",1602)] [DataRow("error",1603)] [DataRow("parent",0)]
    public async Task HiddenProcessUsesStandardRoute(string mode, int exit)
    {
        var fixture = Environment.GetEnvironmentVariable("EXPC_OPERATION_FIXTURE");
        if (string.IsNullOrEmpty(fixture)) { Assert.Inconclusive("Set EXPC_OPERATION_FIXTURE"); return; }
        var root = FixtureDirectory(); var shown = 0; var scanned = 0; var launched = 0; var done = false;
        try
        {
            await new StandardRemoval().Run(App, new()
            {
                Uninstall = async (_, token) =>
                {
                    launched++; var command = UninstallCommand.Parse("\"" + fixture + "\" --operation-fixture " + mode);
                    var info = command.ToProcessStartInfo(); info.UseShellExecute = false; info.CreateNoWindow = true;
                    using var trace = new ProcessStartEvents(); using var process = Process.Start(info)!;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await ProcessFamily.Wait(process, trace, timeout.Token); Assert.AreEqual(exit, process.ExitCode);
                    done = true; return ProductActions.Completion(exit, true, true); // registration fact is synthetic
                },
                Find = _ => { Assert.IsTrue(done); scanned++; return [new("folder",root,"Operation66")]; }
            }, _ => { }, () => Task.CompletedTask, _ => { Assert.IsTrue(done); shown++; });
            Assert.AreEqual(1, launched); Assert.AreEqual(exit == 0 ? 1 : 0, shown); Assert.AreEqual(shown, scanned);
        }
        finally { Directory.Delete(root, false); }
    }
}
