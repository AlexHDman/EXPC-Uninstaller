using System.Globalization;
using System.Net.Http;
using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class VersionTests
{
    private static AppEntry App(string version = "1.2.0") => new("id", "Sample", version, null, null, null, "Vendor");
    private static VersionRule Rule => new() { Id = "test", Url = "https://example.com/versions", Pattern = @"Version: (?<version>\d+(?:\.\d+)+)", Branch = "1" };
    [TestMethod]
    [DataRow("1.9", "1.10", -1)]
    [DataRow("5.01.0000", "5.1.0.0", 0)]
    [DataRow("v2.0.15000", "2.0.15700", -1)]
    [DataRow("999999999999.1", "2.0", 1)]
    public void VersionsCompareNumerically(string a, string b, int sign) => Assert.AreEqual(sign, Math.Sign(ReleaseVersion.Compare(a, b)!.Value));
    [TestMethod]
    [DataRow("1.2-beta")]
    [DataRow("1.2 / 2.0")]
    [DataRow("2026年")]
    [DataRow("")]
    public void UnrecognizedVersionsNeverBecomeCurrent(string installed) => Assert.IsNull(ReleaseVersion.Compare(installed, "1.2"));
    [TestMethod]
    public void BranchTransitionRequiresManualReview()
    {
        var value = VersionChecker.Evaluate(App(), Rule, "2.0", DateTimeOffset.UtcNow);
        Assert.AreEqual(VersionState.Manual, value.State);
    }
    [TestMethod]
    public void NewerInstalledVersionIsNotCalledCurrent() => Assert.AreEqual(VersionState.Unknown,
        VersionChecker.Evaluate(App("1.5"), Rule, "1.2", DateTimeOffset.UtcNow).State);
    [TestMethod]
    public void MultipleDifferentMatchesAreRejected()
    {
        Assert.Throws<FormatException>(() => VersionChecker.Extract("Version: 1.2 Version: 1.3", Rule.Pattern));
        Assert.AreEqual("1.2", VersionChecker.Extract("Version: 1.2 Version: 1.2", Rule.Pattern));
    }
    [TestMethod]
    public async Task NetworkFailureRetainsEvidenceTimeButNotSuccessState()
    {
        var before = DateTimeOffset.UtcNow.AddDays(-2);
        var old = VersionChecker.Evaluate(App(), Rule, "1.3", before);
        var value = await new VersionChecker((_, _) => throw new HttpRequestException("offline")).Check(App(), Rule, CancellationToken.None, old);
        Assert.AreEqual(VersionState.Error, value.State); Assert.AreEqual(before, value.CheckedAt);
        Assert.AreEqual("1.3", value.Latest); Assert.IsTrue(value.AttemptedAt > before);
    }
    [TestMethod]
    public async Task OldEvidenceCannotMoveToAnotherVersionOrRule()
    {
        var old = VersionChecker.Evaluate(App("1.0"), Rule, "1.3", DateTimeOffset.UtcNow);
        var value = await new VersionChecker((_, _) => throw new HttpRequestException()).Check(App(), Rule, CancellationToken.None, old);
        Assert.IsNull(value.CheckedAt); Assert.AreEqual("", value.Latest);
    }
    [TestMethod]
    public async Task CancelIsDistinctFromNetworkError()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var value = await new VersionChecker().Check(App(), Rule, cancel.Token);
        Assert.AreEqual(VersionState.Cancelled, value.State); Assert.IsNull(value.CheckedAt);
    }
    [TestMethod]
    public async Task MissingRuleIsUnknownWithoutNetwork()
    {
        var value = await new VersionChecker((_, _) => throw new Exception("must not fetch")).Check(App(), null, CancellationToken.None);
        Assert.AreEqual(VersionState.Unknown, value.State); Assert.IsNull(value.CheckedAt);
    }
    [TestMethod]
    public async Task GithubStableTagIsParsedAndPrereleaseRejected()
    {
        var rule = Rule with { Provider = "github", Url = "https://github.com/owner/repo", Pattern = @"^v(?<version>\d+\.\d+)$" };
        var good = await new VersionChecker((_, _) => Task.FromResult("{\"tag_name\":\"v1.3\",\"draft\":false,\"prerelease\":false}"))
            .Check(App(), rule, CancellationToken.None);
        Assert.AreEqual(VersionState.Update, good.State);
        var bad = await new VersionChecker((_, _) => Task.FromResult("{\"tag_name\":\"v1.3\",\"draft\":false,\"prerelease\":true}"))
            .Check(App(), rule, CancellationToken.None);
        Assert.AreEqual(VersionState.Manual, bad.State);
    }
    [TestMethod]
    public async Task NoReleaseIsNotEvidenceOfEndOfSupport()
    {
        var value = await new VersionChecker((_, _) => Task.FromResult("No releases yet")).Check(App(), Rule, CancellationToken.None);
        Assert.AreNotEqual(VersionState.EndOfSupport, value.State);
    }
    [TestMethod]
    public void SimilarNamesAndWrongPublishersDoNotMatch()
    {
        var rules = VersionChecker.Rules();
        var app = new AppEntry("id", "Драйверы Рутокен", "5.0.1.0", null, null, null, "Компания \"Актив\"");
        Assert.AreEqual("rutoken-drivers", VersionChecker.Resolve(app, rules)?.Id);
        Assert.IsNull(VersionChecker.Resolve(app with { Name = "Драйверы Рутокен Repair Tool" }, rules));
        Assert.IsNull(VersionChecker.Resolve(app with { Publisher = "Unknown" }, rules));
        Assert.IsNull(VersionChecker.Resolve(app, rules.Concat(rules)));
    }
    [TestMethod]
    public void RequiredCryptoProductsHaveSeparateRules()
    {
        var rules = VersionChecker.Rules();
        foreach (var (name, publisher, id) in new[] {
            ("КриптоПро CSP", "Crypto-Pro LLC", "cryptopro-csp"),
            ("КриптоПро ЭЦП Browser plug-in", "Crypto-Pro LLC", "cryptopro-browser-plugin"),
            ("Драйверы Рутокен", "Компания \"Актив\"", "rutoken-drivers"),
            ("Рутокен Плагин", "Aktiv Company", "rutoken-plugin") })
            Assert.AreEqual(id, VersionChecker.Resolve(App() with { Name = name, Publisher = publisher }, rules)?.Id, name);
    }
    [TestMethod]
    [DataRow("http://example.com")]
    [DataRow("https://127.0.0.1/a")]
    [DataRow("https://user:secret@example.com")]
    [DataRow("https://host.local")]
    [DataRow("file:///c:/test")]
    public void NonPublicOrCredentialUrlsRejected(string url) => Assert.Throws<FormatException>(() => VersionChecker.PublicUrl(url));
    [TestMethod]
    public void LocalizationHasSameKeysAndPreservesPlaceholders()
    {
        var english = L.Catalog("en"); Assert.IsTrue(english.Count > 150);
        foreach (var language in L.Languages.Keys)
        {
            var catalog = L.Catalog(language);
            CollectionAssert.AreEquivalent(english.Keys.ToArray(), catalog.Keys.ToArray());
            foreach (var pair in english)
            {
                Assert.IsFalse(string.IsNullOrEmpty(catalog[pair.Key]), language + "/" + pair.Key);
                for (var i = 0; i < 3; i++) Assert.AreEqual(pair.Value.Contains("{" + i + "}"), catalog[pair.Key].Contains("{" + i + "}"), language + "/" + pair.Key);
            }
        }
    }
    [TestMethod]
    public void LanguageFallbackAndProductIdentityAreConservative()
    {
        Assert.AreEqual("en", PortableData.LanguageFor("", CultureInfo.GetCultureInfo("ja-JP")));
        Assert.AreEqual("de", PortableData.LanguageFor("de", CultureInfo.GetCultureInfo("ru-RU")));
        Assert.AreNotEqual(PortableData.Identity(App()), PortableData.Identity(App() with { Publisher = "Other" }));
        Assert.AreEqual(PortableData.Identity(App()), PortableData.Identity(App("1.3")));
    }
    [TestMethod]
    public void PortablePreferencesRoundTripAndBackup()
    {
        var previous = PortableData.DirectoryPath;
        var path = Path.Combine(Path.GetTempPath(), "EXPC-preferences-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            PortableData.DirectoryPath = path;
            PortableData.Save("settings", new Preferences { Language = "it", Theme = "Glass Ruby", ColumnWidths = [350, 76, 98, 86, 52, 65] });
            PortableData.Save("settings", new Preferences { Language = "de" });
            Assert.AreEqual("de", PortableData.Load<Preferences>("settings").Language);
            Assert.IsTrue(File.Exists(Path.Combine(path, "settings.json.bak")));
            File.WriteAllText(Path.Combine(path, "settings.json"), "{\"Language\":null,\"ColumnWidths\":null}");
            Assert.AreEqual("", PortableData.Load<Preferences>("settings").Language);
            Assert.IsNotNull(PortableData.ReadError);
        }
        finally { PortableData.DirectoryPath = previous; /* Leave this unique temporary fixture for inspection. */ }
    }
    [TestMethod]
    public void LightThemeRecolorsExistingRowsAndKeepsSelectionReadable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Theme.Select("Glass Teal");
                using var grid = new System.Windows.Forms.DataGridView { AllowUserToAddRows = false };
                grid.Columns.Add("name", "Name"); grid.Rows.Add("Sample"); grid.Rows.Add("Other");
                Theme.Apply(grid); Theme.Select("Windows Light"); Theme.Apply(grid);
                Assert.AreEqual(GlassTeal.Background, grid.Rows[0].InheritedStyle.BackColor);
                Assert.AreEqual(GlassTeal.Stripe, grid.Rows[1].InheritedStyle.BackColor);
                Assert.AreEqual(GlassTeal.Text, grid.Rows[0].InheritedStyle.ForeColor);
                foreach (var theme in Theme.Names) { Theme.Select(theme); Theme.Apply(grid); }
            }
            catch (Exception ex) { failure = ex; }
            finally { Theme.Select("Glass Teal"); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10))); if (failure != null) throw failure;
    }
}
