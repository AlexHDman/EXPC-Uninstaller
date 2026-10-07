using Microsoft.Win32;

namespace ExpcUninstaller;

internal static class Theme
{
    public static readonly string[] Names = ["Windows", "Windows Light", "Graphite", "Glass Teal", "Glass Blue", "Glass Amber", "Glass Ruby"];
    public static string Selected { get; private set; } = "Glass Teal";
    public static bool Light { get; private set; }
    public static Color Accent { get; private set; } = Color.FromArgb(58, 220, 222);
    public static void Select(string name)
    {
        Selected = Names.Contains(name) ? name : "Glass Teal";
        var windowsLight = true;
        if (Selected == "Windows")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            windowsLight = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        Light = Selected == "Windows Light" || Selected == "Windows" && windowsLight;
        Accent = Selected switch
        {
            "Glass Blue" => Color.FromArgb(98, 159, 219), "Glass Amber" => Color.FromArgb(210, 166, 84),
            "Glass Ruby" => Color.FromArgb(204, 107, 129), "Graphite" => Color.FromArgb(154, 168, 178),
            _ => Color.FromArgb(68, 174, 175)
        };
    }
    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    public static void Apply(Control control)
    {
        control.BackColor = GlassTeal.Background; control.ForeColor = GlassTeal.Text;
        foreach (Control child in control.Controls) Apply(child);
        if (control is GlassButton) control.BackColor = Color.Transparent;
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = GlassTeal.Background;
            grid.DefaultCellStyle.BackColor = GlassTeal.Background; grid.DefaultCellStyle.ForeColor = GlassTeal.Text;
            grid.DefaultCellStyle.SelectionBackColor = GlassTeal.Selection; grid.DefaultCellStyle.SelectionForeColor = GlassTeal.Text;
            grid.RowsDefaultCellStyle.BackColor = GlassTeal.Background; grid.RowsDefaultCellStyle.ForeColor = GlassTeal.Text;
            grid.RowsDefaultCellStyle.SelectionBackColor = GlassTeal.Selection; grid.RowsDefaultCellStyle.SelectionForeColor = GlassTeal.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = GlassTeal.Stripe;
            grid.ColumnHeadersDefaultCellStyle.BackColor = GlassTeal.Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = GlassTeal.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GlassTeal.Surface;
        }
        control.Invalidate(true);
    }
}
