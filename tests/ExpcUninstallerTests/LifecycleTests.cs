using System.Reflection;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class LifecycleTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) throw failure;
    }
    [TestMethod] public void CloseEndsSessionAndLocksVault()
    {
        Sta(() =>
        {
            var before = PortableData.DirectoryPath;
            PortableData.DirectoryPath = Path.Combine(Path.GetTempPath(), "EXPC-lifecycle-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var vault = new SecretVault(Path.Combine(PortableData.DirectoryPath, "fixture.vault"));
                vault.Create(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
                using var form = new MainWindow(vault);
                var close = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
                typeof(MainWindow).GetMethod("OnFormClosing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [close]);
                Assert.IsFalse(close.Cancel); Assert.IsFalse(vault.Unlocked);
            }
            finally { PortableData.DirectoryPath = before; }
        });
    }
    [TestMethod]
    [Ignore("Requires isolated GUI: active-operation warning must be observed; never run on the user desktop")]
    public void ActiveOperationBlocksClosureWithWarning()
    {
        Sta(() =>
        {
            var before = PortableData.DirectoryPath; PortableData.DirectoryPath = Path.Combine(Path.GetTempPath(), "EXPC-shutdown-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var vault = new SecretVault(Path.Combine(PortableData.DirectoryPath, "fixture.vault")); vault.Create(Guid.NewGuid().ToString("N"));
                using var form = new MainWindow(vault);
                typeof(MainWindow).GetField("_operationBusy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, true);
                var close = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.WindowsShutDown, false);
                typeof(MainWindow).GetMethod("OnFormClosing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [close]);
                Assert.IsTrue(close.Cancel); Assert.IsTrue(vault.Unlocked);
            }
            finally { PortableData.DirectoryPath = before; }
        });
    }
    [TestMethod] public void MenuAndEightIconsFitAllLanguagesAndThemesAtLogicalMinimum()
    {
        Sta(() =>
        {
            var language = L.Language; var theme = Theme.Selected;
            try
            {
                foreach (var locale in L.Languages.Keys)
                {
                    L.Select(locale);
                    using var form = new MainWindow(); form.ClientSize = form.MinimumSize;
                    var menu = form.Controls.OfType<System.Windows.Forms.MenuStrip>().Single();
                    var buttons = form.Controls.OfType<GlassButton>().Where(b => !b.CaptionButton).ToArray();
                    Assert.AreEqual(8, buttons.Length); Assert.IsTrue(menu.Right < buttons.Min(b => b.Left));
                    Assert.IsTrue(buttons.All(b => b.Right < form.ClientSize.Width && b.Top == buttons[0].Top));
                    foreach (var selected in Theme.Names) { Theme.Select(selected); Theme.Apply(form); Assert.IsTrue(menu.Right < buttons.Min(b => b.Left)); }
                }
            }
            finally { L.Select(language); Theme.Select(theme); }
        });
    }
}
