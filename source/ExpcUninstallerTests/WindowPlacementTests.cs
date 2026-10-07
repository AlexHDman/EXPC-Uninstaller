using ExpcUninstaller;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpcUninstallerTests;

[TestClass]
public sealed class WindowPlacementTests
{
    [TestMethod] public void FirstStartUsesFullWorkHeightAndLogical720Width()
    {
        var work = new Rectangle(0, 0, 2560, 1392);
        Assert.AreEqual(new Rectangle(920, 0, 720, 1392), WindowPlacement.Fit(work, 96, new Size(640, 440), null));
        Assert.AreEqual(1080, WindowPlacement.Fit(work, 144, new Size(960, 660), null).Width);
    }
    [TestMethod] public void RestorePreservesManualSizeAndScalesForMonitorDpi()
    {
        var saved = new SavedWindow(2700, 100, 700, 900, 96, "secondary", 2560, 0);
        Assert.AreEqual(new Rectangle(210, 42, 1050, 1350), WindowPlacement.Fit(new Rectangle(0, 0, 2560, 1392), 144, new Size(960, 660), saved));
    }
    [TestMethod] public void DisconnectedSmallMonitorAndCorruptCoordinatesStayInsideWorkArea()
    {
        var area = new Rectangle(-800, 40, 800, 560);
        foreach (var saved in new[] { new SavedWindow(7000, -5000, 1800, 2000, 144, "gone", 2560, 0), new SavedWindow(int.MaxValue, int.MinValue, int.MaxValue, int.MaxValue, 0, "bad", 0, 0) })
        {
            var restored = WindowPlacement.Fit(area, 96, new Size(960, 660), saved);
            Assert.IsTrue(area.Contains(restored)); Assert.AreEqual(area.Size, restored.Size);
        }
    }
    [TestMethod] public void SavedPlacementRoundTripsWithoutChangingOtherPreferences()
    {
        var previous = PortableData.DirectoryPath;
        PortableData.DirectoryPath = Path.Combine(Path.GetTempPath(), "EXPC-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saved = new SavedWindow(920, 80, 720, 900, 96, "display", 0, 0);
            PortableData.Save("settings", new Preferences { Theme = "Glass Amber", Language = "de", Window = saved });
            var restored = PortableData.Load<Preferences>("settings");
            Assert.AreEqual(saved, restored.Window); Assert.AreEqual("de", restored.Language); Assert.AreEqual("Glass Amber", restored.Theme);
            Assert.AreEqual(new Rectangle(920, 80, 720, 900), WindowPlacement.Fit(new Rectangle(0, 0, 2560, 1400), 96, new Size(640, 440), restored.Window));
        }
        finally { PortableData.DirectoryPath = previous; }
    }
}
