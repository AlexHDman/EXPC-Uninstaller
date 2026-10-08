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
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Background dialog test timeout");
        if (error != null) throw error;
    }
    private static void Pump(Task task)
    {
        var time = Stopwatch.StartNew();
        while (!task.IsCompleted && time.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.IsTrue(task.IsCompleted, "Background operation timeout"); task.GetAwaiter().GetResult();
    }
    [TestMethod]
    [DataRow("ru")]
    [DataRow("en")]
    public void OneDialogOwnsConfirmationResultCleanupAndInlineErrors(string language) => Sta(() =>
    {
        L.Select(language);
        var calls = 0; var scans = 0; var deletions = 0;
        var pending = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new JunkItem("registry", "SOFTWARE\\EXPC-fixture-only", "Operation67");
        using var window = new OperationWindow(App, 0, () => Task.CompletedTask, services: new()
        {
            Uninstall = (_, _) => { Interlocked.Increment(ref calls); return pending.Task; },
            Find = _ => { scans++; return [item]; },
            Delete = (_, _) => { deletions++; return Task.CompletedTask; }
        });
        Assert.AreEqual(OperationStage.Confirm, window.Stage); Assert.AreEqual(0, calls);
        Assert.IsFalse(window.Controls.OfType<TextBox>().Single().Visible);
        var run = window.Advance(); Pump(window.Advance()); // Duplicate start while pending.
        Assert.AreEqual(OperationStage.Running, window.Stage);
        pending.SetResult(ProductActions.Completion(0, true, true)); Pump(run);
        Assert.AreEqual(1, calls); Assert.AreEqual(0, scans); Assert.AreEqual(OperationStage.Result, window.Stage);
        Pump(window.Advance()); Assert.AreEqual(1, scans); Assert.AreEqual(OperationStage.Leftovers, window.Stage);
        var grid = window.Controls.OfType<DataGridView>().Single();
        Assert.IsFalse(grid.Rows[0].Cells[0].Value is true);
        typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(grid,
            [new DataGridViewCellMouseEventArgs(0, 0, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))]);
        Pump(window.Advance()); Assert.AreEqual(OperationStage.ConfirmCleanup, window.Stage); Assert.AreEqual(0, deletions);
        window.Secondary(); Assert.AreEqual(OperationStage.Leftovers, window.Stage); Assert.AreEqual(0, deletions);
        Pump(window.Advance()); Pump(window.Advance());
        Assert.AreEqual(1, deletions); Assert.AreEqual(0, grid.Rows.Count); Assert.IsFalse(window.Visible);
        Assert.AreEqual(0, Application.OpenForms.Count);
    });

    [TestMethod]
    public void CancelBeforeStartStopObservationAndFailureNeverStartCleanup() => Sta(() =>
    {
        var calls = 0;
        using (var cancelled = new OperationWindow(App, 0, () => Task.CompletedTask,
            services: new() { Uninstall = (_, _) => { calls++; throw new Exception("must not run"); } }))
        { cancelled.Secondary(); Assert.AreEqual(0, calls); }
        var pending = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        using var stopped = new OperationWindow(App, 0, () => Task.CompletedTask, services: new()
        {
            Uninstall = (_, token) => { observed = token; return pending.Task; },
            Find = _ => throw new AssertFailedException("Cleanup after stop")
        });
        var run = stopped.Advance(); stopped.Secondary();
        // Even a racing successful result must not enable cleanup after stop.
        pending.SetResult(ProductActions.Completion(0, true, true)); Pump(run);
        Assert.IsTrue(observed.IsCancellationRequested); Assert.AreEqual(L.T("Operation56"), stopped.Status);
        Pump(stopped.Advance()); Assert.AreEqual(OperationStage.Result, stopped.Stage);
        using var failure = new OperationWindow(App, 0, () => Task.CompletedTask,
            services: new() { Uninstall = (_, _) => throw new IOException("fixture error") });
        Pump(failure.Advance()); Assert.AreEqual(OperationStage.Finished, failure.Stage); StringAssert.Contains(failure.Status, "fixture error");
        Assert.AreEqual(0, Application.OpenForms.Count);
    });

    [TestMethod]
    public void RecordOnlyForcedScanAndCleanupErrorStayInline() => Sta(() =>
    {
        var removedRecords = 0;
        using var record = new OperationWindow(App, 3, () => Task.CompletedTask,
            services: new() { RemoveRecord = _ => { removedRecords++; return Task.CompletedTask; } });
        Assert.AreEqual(0, removedRecords); Pump(record.Advance()); Pump(record.Advance());
        Assert.AreEqual(1, removedRecords); Assert.AreEqual(L.T("Operation11"), record.Status);
        using var forced = new OperationWindow(App, 1, () => Task.CompletedTask, services: new()
        {
            Uninstall = (_, _) => throw new AssertFailedException("Forced scan must not launch uninstaller"),
            Find = _ => [new("folder", "C:\\synthetic-only", "Operation67")],
            Delete = (_, _) => throw new IOException("fixture boundary changed")
        });
        Pump(forced.Advance());
        var grid = forced.Controls.OfType<DataGridView>().Single(); grid.Rows[0].Cells[0].Value = true;
        Pump(forced.Advance()); Pump(forced.Advance());
        Assert.AreEqual(1, grid.Rows.Count); StringAssert.Contains(forced.Status, "fixture boundary changed");
        Assert.AreEqual(OperationStage.Leftovers, forced.Stage); Assert.AreEqual(0, Application.OpenForms.Count);
        Assert.IsFalse(ProductActions.Completion(0, false, true).Removed);
        Assert.IsFalse(ProductActions.Completion(0, true, false).Removed);
        Assert.IsTrue(ProductActions.Completion(3010, true, true).Reboot);
    });

    [TestMethod]
    public void StoppingObservationLeavesFixtureProcessAlive() => Sta(() =>
    {
        var fixture = Environment.GetEnvironmentVariable("EXPC_OPERATION_FIXTURE");
        if (string.IsNullOrEmpty(fixture)) { Assert.Inconclusive("Set EXPC_OPERATION_FIXTURE"); return; }
        using var process = Process.Start(new ProcessStartInfo(fixture)
        { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--operation-fixture", "slow" } })!;
        using var window = new OperationWindow(App, 0, () => Task.CompletedTask, services: new()
        {
            Uninstall = async (_, token) =>
            {
                await process.WaitForExitAsync(token);
                return ProductActions.Completion(process.ExitCode, true, true);
            },
            Find = _ => throw new AssertFailedException("Stopped observation must not scan")
        });
        var task = window.Advance(); window.Secondary(); Pump(task);
        Assert.IsFalse(process.HasExited, "Stop waiting must not terminate the fixture");
        Assert.AreEqual(L.T("Operation56"), window.Status); Pump(window.Advance());
        Assert.IsTrue(process.WaitForExit(5000), "Fixture must finish naturally");
    });

    [TestMethod]
    [DataRow("success", 0)]
    [DataRow("cancel", 1602)]
    [DataRow("error", 1603)]
    [DataRow("reboot", 3010)]
    [DataRow("parent", 0)]
    public void HiddenFixtureProcessDrivesTheSameDialog(string mode, int expectedExit) => Sta(() =>
    {
        var fixture = Environment.GetEnvironmentVariable("EXPC_OPERATION_FIXTURE");
        if (string.IsNullOrEmpty(fixture)) { Assert.Inconclusive("Build tools/Expc.OperationFixture and set EXPC_OPERATION_FIXTURE to its EXE"); return; }
        Assert.IsTrue(File.Exists(fixture));
        var scans = 0;
        using var window = new OperationWindow(App, 0, () => Task.CompletedTask, services: new()
        {
            Uninstall = async (_, token) =>
            {
                var command = UninstallCommand.Parse("\"" + fixture + "\" --operation-fixture " + mode);
                var start = command.ToProcessStartInfo(); start.UseShellExecute = false; start.CreateNoWindow = true;
                using var trace = new ProcessStartEvents();
                using var process = Process.Start(start)!;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await ProcessFamily.Wait(process, trace, timeout.Token);
                Assert.AreEqual(expectedExit, process.ExitCode);
                // Only registration/trace facts are synthetic; never register or remove a host application.
                return ProductActions.Completion(process.ExitCode, true, true);
            },
            Find = _ => { scans++; return []; }
        });
        Pump(window.Advance()); Assert.AreEqual(OperationStage.Result, window.Stage);
        Assert.AreEqual(ProductActions.Completion(expectedExit, true, true).Message, window.Status);
        Pump(window.Advance()); Assert.AreEqual(expectedExit == 0 ? 1 : 0, scans);
        Assert.IsFalse(window.Visible); Assert.AreEqual(0, Application.OpenForms.Count);
    });
}
