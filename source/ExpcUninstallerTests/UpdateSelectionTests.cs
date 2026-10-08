using System.Reflection;
using System.ComponentModel;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class UpdateSelectionTests
{
    [TestMethod]
    [DataRow("ru")]
    [DataRow("en")]
    public void MouseSpaceSortResizeAndBlockedRowsKeepSelectionSafe(string language)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                L.Select(language);
                var now = DateTimeOffset.UtcNow;
                var p = new ManagedPackage { Manager = "WinGet", Id = "Example.Ready", Installed = "1.0", Latest = "2.0", Applicable = true, IdentityConfirmed = true };
                var ready = new VersionResult(VersionState.Update, "1.0", "2.0", "fixture", now, now, "ManagerReady", "ManagerCompatible", "") { Package = p };
                Assert.AreEqual("ManagerPinned", (ready with { Package = p with { Pinned = true } }).SelectionReason);
                Assert.AreEqual("CompatibilityUnknown", (ready with { Package = p with { Applicable = false } }).SelectionReason);
                Assert.AreEqual("UpdatePlanChanged", (ready with { Package = p with { Latest = "3.0" } }).SelectionReason);
                var app = new AppEntry("fixture", "Z Ready", "1.0", null, null, null, "fixture");
                using var form = new UpdatesWindow([(app, ready), (app with { Id = "fixture-blocked", Name = "A Blocked" }, ready with { Package = p with { Pinned = true }, Reason = "ManagerPinned" })],
                    (_, _, _) => throw new AssertFailedException("Selection must never start installation"));
                var grid = form.Controls.OfType<DataGridView>().Single();
                var install = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == L.T("InstallSelected"));
                // Mouse click at the edge of the checkbox cell: no CellContentClick required.
                void Click(int row) => typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid,
                    [new DataGridViewCellMouseEventArgs(0, row, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))]);
                Assert.IsFalse(install.Enabled);
                Click(0); Assert.IsTrue(grid.Rows[0].Cells[0].Value is true); Assert.IsTrue(install.Enabled);
                Click(0); Assert.IsFalse(grid.Rows[0].Cells[0].Value is true); Assert.IsFalse(install.Enabled);
                Click(0); Assert.IsTrue(grid.Rows[0].Cells[0].Value is true); Assert.IsTrue(install.Enabled);
                Click(1); Assert.IsTrue(grid.Rows[1].Cells[0].Value is true);
                grid.Sort(grid.Columns[1], ListSortDirection.Ascending);
                Assert.AreEqual(1, (int)grid.Rows[0].Tag!); Assert.IsTrue(grid.Rows[0].Cells[0].Value is true);
                Assert.IsTrue(grid.Rows[1].Cells[0].Value is true);
                form.Size = new Size(1050, 620); grid.Refresh(); Assert.IsTrue(grid.Rows[1].Cells[0].Value is true);
                grid.CurrentCell = grid.Rows[1].Cells[1];
                typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [new KeyEventArgs(Keys.Space)]);
                Assert.IsFalse(grid.Rows[1].Cells[0].Value is true); Assert.IsFalse(install.Enabled);
                Assert.IsTrue(grid.Rows[0].Cells[5].ToolTipText.Contains(L.T("ManagerPinned")));
                Assert.IsTrue(form.Controls.OfType<TextBox>().Single().Text.Contains("Z Ready"));
                using var multi = new UpdatesWindow([(app, ready), (app with { Id = "fixture-second", Name = "Second" }, ready)],
                    (_, _, _) => throw new AssertFailedException("Selection must never start installation"));
                var multiGrid = multi.Controls.OfType<DataGridView>().Single();
                var multiInstall = multi.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == L.T("InstallSelected"));
                void MultiClick(int row) => typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(multiGrid,
                    [new DataGridViewCellMouseEventArgs(0, row, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))]);
                MultiClick(0); MultiClick(1);
                Assert.IsTrue(multiGrid.Rows.Cast<DataGridViewRow>().All(r => r.Cells[0].Value is true));
                MultiClick(0); Assert.IsTrue(multiInstall.Enabled);
                MultiClick(1); Assert.IsFalse(multiInstall.Enabled);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) throw failure;
    }
}
