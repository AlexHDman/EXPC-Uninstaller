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
            "Glass Blue" => Color.FromArgb(74, 156, 247), "Glass Amber" => Color.FromArgb(255, 191, 48),
            "Glass Ruby" => Color.FromArgb(239, 106, 132), "Graphite" => Color.FromArgb(154, 168, 178),
            "Windows" => Color.FromArgb(102, 162, 223), _ => Color.FromArgb(35, 198, 153)
        };
        if (Light) Accent = Color.FromArgb(68, 174, 175);
    }
    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string theme, string? list);
    private static void NativeTheme(object? sender, EventArgs e)
    {
        if (sender is not Control control || !control.IsHandleCreated) return;
        if (control is Form)
        {
            int dark = Light ? 0 : 1, caption = Light ? -1 : ColorTranslator.ToWin32(GlassTeal.Caption), text = Light ? -1 : ColorTranslator.ToWin32(GlassTeal.Text);
            DwmSetWindowAttribute(control.Handle, 20, ref dark, 4); DwmSetWindowAttribute(control.Handle, 35, ref caption, 4); DwmSetWindowAttribute(control.Handle, 36, ref text, 4);
        }
        if (control is TextBoxBase or TreeView or ListView) SetWindowTheme(control.Handle, Light ? "Explorer" : "DarkMode_Explorer", null);
    }
    public static void Apply(Control control)
    {
        control.BackColor = GlassTeal.Background; control.ForeColor = GlassTeal.Text;
        foreach (Control child in control.Controls) Apply(child);
        if (control is GlassButton) control.BackColor = Color.Transparent;
        if (!Light)
        {
            if (control is Label or Button or ToolStrip) control.ForeColor = GlassTeal.Heading;
            if (control is TextBoxBase or ComboBox or ToolStrip or Button) control.BackColor = GlassTeal.Surface;
            if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = GlassTeal.Border;
                button.FlatAppearance.MouseOverBackColor = GlassTeal.Selection;
            }
        }
        else if (control is Button button) button.FlatStyle = FlatStyle.Standard;
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = GlassTeal.Background;
            grid.DefaultCellStyle.BackColor = GlassTeal.Background; grid.DefaultCellStyle.ForeColor = GlassTeal.Text;
            grid.DefaultCellStyle.SelectionBackColor = GlassTeal.Selection; grid.DefaultCellStyle.SelectionForeColor = GlassTeal.Text;
            grid.RowsDefaultCellStyle.BackColor = GlassTeal.Background; grid.RowsDefaultCellStyle.ForeColor = GlassTeal.Text;
            grid.RowsDefaultCellStyle.SelectionBackColor = GlassTeal.Selection; grid.RowsDefaultCellStyle.SelectionForeColor = GlassTeal.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = GlassTeal.Stripe;
            grid.ColumnHeadersDefaultCellStyle.BackColor = GlassTeal.Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = GlassTeal.Heading;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GlassTeal.Surface;
        }
        control.HandleCreated -= NativeTheme; control.HandleCreated += NativeTheme;
        if (control.IsHandleCreated) NativeTheme(control, EventArgs.Empty);
        control.Invalidate(true);
    }
}
