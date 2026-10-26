namespace ExpcUninstaller;

internal sealed record SavedWindow(int X, int Y, int Width, int Height, int Dpi, string Monitor, int WorkX, int WorkY);

internal static class WindowPlacement
{
    internal static Rectangle Fit(Rectangle area, int dpi, Size minimum, SavedWindow? saved)
    {
        var factor = dpi / (double)(saved?.Dpi is >= 48 and <= 768 ? saved.Dpi : 96);
        var width = saved is { Width: > 0, Height: > 0 } ? saved.Width * factor : 720d * dpi / 96;
        var height = saved is { Width: > 0, Height: > 0 } ? saved.Height * factor : area.Height;
        int Bound(double value, int min, int max) => (int)Math.Clamp(value, Math.Min(min, max), max);
        var w = Bound(width, minimum.Width, area.Width); var h = Bound(height, minimum.Height, area.Height);
        var x = saved == null ? area.Left + (area.Width - w) / 2d : area.Left + ((double)saved.X - saved.WorkX) * factor;
        var y = saved == null ? area.Top : area.Top + ((double)saved.Y - saved.WorkY) * factor;
        return new Rectangle((int)Math.Clamp(x, area.Left, (double)area.Right - w), (int)Math.Clamp(y, area.Top, (double)area.Bottom - h), w, h);
    }
}

internal sealed partial class MainWindow
{
    private void RestorePlacement()
    {
        var saved = Program.Settings.Window;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == saved?.Monitor) ?? Screen.PrimaryScreen ?? Screen.FromControl(this);
        // Move first: PerMonitorV2 then supplies this monitor's actual DeviceDpi.
        Location = screen.WorkingArea.Location;
        LayoutShell();
        Bounds = WindowPlacement.Fit(screen.WorkingArea, DeviceDpi, MinimumSize, saved);
    }
    private void SavePlacement()
    {
        if (!_shown || WindowState != FormWindowState.Normal) return;
        var screen = Screen.FromControl(this); var area = screen.WorkingArea;
        Program.Settings.Window = new(Left, Top, Width, Height, DeviceDpi, screen.DeviceName, area.Left, area.Top);
        SavePreferences();
    }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); SavePlacement(); }
}
