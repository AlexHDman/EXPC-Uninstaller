using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class CatalogTests
{
    private static RegistryMetadata Metadata => new("id", "Sample", "1.0", "Publisher", @"C:\Sample",
        "uninstall.exe", "", "", "20261006", 1024, "0", "0");

    [TestMethod]
    public void ExactRegistryCopiesAcrossViewsCollapse()
    {
        var items = FastCatalog.Deduplicate([("HKLM64", Metadata), ("HKLM32", Metadata), ("HKCU32", Metadata)]);
        Assert.HasCount(1, items);
        Assert.AreEqual(1048576L, items[0].SizeBytes);
        Assert.AreEqual(new DateTime(2026, 10, 6), items[0].Installed);
    }

    [TestMethod]
    public void SameNameWithDifferentCommandsOrMetadataIsPreserved()
    {
        var items = FastCatalog.Deduplicate([("a", Metadata), ("b", Metadata with { Uninstall = "another.exe" }),
            ("c", Metadata with { Version = "2.0" }), ("d", Metadata with { NoRemove = "1" }),
            ("e", Metadata with { KeyName = "different-record" })]);
        Assert.HasCount(5, items);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("20261301")]
    [DataRow("20260230")]
    [DataRow("2026-10-06")]
    public void InvalidDatesStayUnknown(string date) => Assert.IsNull(FastCatalog.ParseDate(date));

    [TestMethod]
    public void MissingSizeIsDistinctFromZeroAndDwordDoesNotOverflow()
    {
        Assert.IsNull(FastCatalog.ParseSizeKb(null));
        Assert.AreEqual(0L, FastCatalog.ParseSizeKb(0));
        Assert.AreEqual(4294967295L, FastCatalog.ParseSizeKb(-1));
        Assert.IsNull(FastCatalog.ToBytes(long.MaxValue));
        Assert.AreEqual(UiText.Empty, UiText.Size(null));
        Assert.AreNotEqual(UiText.Empty, UiText.Size(0));
    }

    [TestMethod]
    public void SearchAndSortUseActualValuesRatherThanFormattedText()
    {
        AppEntry Row(string id, string name, string version, long size) => new(id, name, version, size, null, null, "");
        var rows = new[] { Row("a", "Alpha", "2.0", 200), Row("b", "alpha tool", "10.0", 1000), Row("c", "Beta", "1.0", 30) };
        CollectionAssert.AreEqual(new[] { "a", "b" }, CatalogView.Select(rows, " ALPHA ", 3, false).Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, CatalogView.Select(rows, "", 1, true).Select(x => x.Id).ToArray());
        Assert.IsEmpty(CatalogView.Select(rows, "not installed", 0, false));
    }

    [TestMethod]
    public void IconsNeverResolveNetworkOrRelativePaths()
    {
        Assert.IsNull(LazyIcons.Load(@"\\server\share\app.exe"));
        Assert.IsNull(LazyIcons.Load("app.exe"));
        Assert.IsNull(LazyIcons.Load("https://example.invalid/icon.png"));
    }

    [TestMethod]
    public void MixedVersionFormatsHaveAConsistentSortOrder()
    {
        var versions = new[] { "2.0", "10.0", "1beta", "", "1.2.3.4", "2026 R1" };
        var rows = versions.Select(v => new AppEntry(v, v, v, null, null, null, "")).ToArray();
        var expected = CatalogView.Select(rows, "", 3, false).Select(x => x.Version).ToArray();
        CollectionAssert.AreEqual(expected, CatalogView.Select(rows.Reverse(), "", 3, false).Select(x => x.Version).ToArray());
        CollectionAssert.AreEqual(expected.Reverse().ToArray(), CatalogView.Select(rows, "", 3, true).Select(x => x.Version).ToArray());
    }
}
