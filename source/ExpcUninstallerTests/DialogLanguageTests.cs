using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class DialogLanguageTests
{
    [TestMethod]
    [DataRow("ru")]
    [DataRow("en")]
    public void OwnDialogsConstructWithTranslatedControlsWithoutShowingWindows(string language)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "EXPC-language-" + Guid.NewGuid().ToString("N") + ".vault");
            try
            {
                L.Select(language);
                var catalog = L.Catalog(language);
                using var vault = new SecretVault(path);
                using var create = new VaultPasswordWindow(vault, false);
                vault.Create("Synthetic-test-password-123!");
                using var unlock = new VaultPasswordWindow(vault, false);
                using var change = new VaultPasswordWindow(vault, true);
                using var accounts = new AccountWindow(vault);
                using var cancel = new CancellationTokenSource();
                var app = new AppEntry("fixture", "Example", "1.0", null, null, null, "fixture");
                using var source = new SourceWindow(app, null);
                using var details = new DetailWindow(L.T("Updates"), "fixture", null);
                using var operation = new OperationWindow(app, 1, () => Task.CompletedTask);
                using var junk = new OperationWindow(app, 2, () => Task.CompletedTask);
                IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
                foreach (var form in new Form[] { create, unlock, change, accounts, source, details, operation, junk })
                {
                    Assert.IsFalse(form.Visible, "Background test must not show any window");
                    foreach (var control in Descendants(form))
                        Assert.IsFalse(catalog.TryGetValue(control.Text, out var translated) && translated != control.Text && !catalog.Values.Contains(control.Text),
                            $"Untranslated key in {form.GetType().Name}: {control.Text}");
                }
                Assert.AreEqual(L.T("CreateVault"), create.Text);
                Assert.AreEqual(L.T("UnlockVault"), unlock.Text);
                Assert.AreEqual(L.T("Accounts"), accounts.Text);
            }
            catch (Exception ex) { failure = ex; }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) throw failure;
    }
}
