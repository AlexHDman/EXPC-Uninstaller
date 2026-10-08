using System.Reflection;
using System.ComponentModel;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class UpdateSelectionTests
{
    [TestMethod] public void MouseSpaceSortResizeAndBlockedRowsKeepSelectionSafe()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var p = new ManagedPackage { Manager = "WinGet", Id = "Example.Ready", Installed = "1.0", Latest = "2.0", Applicable = true, IdentityConfirmed = true };
                var ready = new VersionResult(VersionState.Update, "1.0", "2.0", "fixture", now, now, "ManagerReady", "ManagerCompatible", "") { Package = p };
                var app = new AppEntry("fixture", "Z Ready", "1.0", null, null, null, "fixture");
                using var form = new UpdatesWindow([(app, ready), (app with { Name = "A Blocked" }, ready with { Package = p with { Pinned = true }, Reason = "ManagerPinned" })],
                    (_, _, _) => throw new AssertFailedException("Selection must never start installation"));
                var grid = form.Controls.OfType<DataGridView>().Single();
                var install = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == L.T("InstallSelected"));
                void Click(int row) => typeof(DataGridView).GetMethod("OnCellContentClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [new DataGridViewCellEventArgs(0, row)]);
                Assert.IsFalse(install.Enabled);
                Click(0); Assert.IsTrue(grid.Rows[0].Cells[0].Value is true); Assert.IsTrue(install.Enabled);
                Click(1); Assert.IsFalse(grid.Rows[1].Cells[0].Value is true);
                grid.Sort(grid.Columns[1], ListSortDirection.Ascending);
                Assert.AreEqual(1, (int)grid.Rows[0].Tag!); Assert.IsFalse(grid.Rows[0].Cells[0].Value is true);
                Assert.IsTrue(grid.Rows[1].Cells[0].Value is true);
                form.Size = new Size(1050, 620); grid.Refresh(); Assert.IsTrue(grid.Rows[1].Cells[0].Value is true);
                grid.CurrentCell = grid.Rows[1].Cells[1];
                typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(grid, [new KeyEventArgs(Keys.Space)]);
                Assert.IsFalse(grid.Rows[1].Cells[0].Value is true); Assert.IsFalse(install.Enabled);
                Assert.IsTrue(grid.Rows[0].Cells[5].ToolTipText.Contains(L.T("ManagerPinned")));
                Assert.IsTrue(form.Controls.OfType<TextBox>().Single().Text.Contains("Z Ready"));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) throw failure;
    }
}
