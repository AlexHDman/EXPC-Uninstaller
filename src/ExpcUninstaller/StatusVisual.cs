using System.Drawing.Drawing2D;
namespace ExpcUninstaller;
internal static class StatusVisual
{
    internal static Color ColorFor(VersionState state) => state switch
    {
        VersionState.Unchecked or VersionState.Ignored => Color.FromArgb(143, 151, 160),
        VersionState.Update => Color.FromArgb(65, 159, 244),
        VersionState.Current => Color.FromArgb(62, 190, 117),
        VersionState.Error => Color.FromArgb(237, 87, 91),
        _ => Color.FromArgb(238, 190, 62)
    };
    internal static void Draw(Graphics g, Rectangle rect, VersionState state)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var saved = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias;
        var points = Enumerable.Range(0, 10).Select(i => { var angle = -Math.PI / 2 + i * Math.PI / 5; var radius = (i % 2 == 0 ? .49 : .23) * Math.Min(rect.Width, rect.Height); return new PointF(rect.X + rect.Width / 2f + (float)(Math.Cos(angle) * radius), rect.Y + rect.Height / 2f + (float)(Math.Sin(angle) * radius)); }).ToArray();
        var color = ColorFor(state); using var brush = new LinearGradientBrush(rect, Theme.Blend(color, Color.White, .35f), color, LinearGradientMode.Vertical);
        g.FillPolygon(brush, points); g.Restore(saved);
    }
}
