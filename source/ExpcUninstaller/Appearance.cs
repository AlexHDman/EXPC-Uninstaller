using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ExpcUninstaller;

internal static class UiText
{
    public const string Title = "EXPC Uninstaller";
    public const string Design = "EXPC-002";
    public const string Empty = "—";
    public const string Store = "Microsoft Store";
    public static string Ready => L.T("Ready");
    public static string Loading => L.T("Loading");
    public static string Search => L.T("Search");
    public static string Unknown => L.T("StatusUnchecked");
    public static string Disabled => L.T("Unavailable");
    public static string Scroll => L.T("Scroll");
    public static string Press => L.T("Press");
    public static string Delete => L.T("Uninstall");
    public static string Refresh => L.T("Refresh");
    public static string CheckVersions => L.T("CheckVersions");
    public static string Theme => L.T("Theme");
    public static string Language => L.T("Language");
    public static string ThemeHint => L.T("Theme");
    public static string LanguageHint => L.T("Language");
    public static string Programs => L.T("Programs");
    public static string StoreHint => L.T("StoreHint");
    public static string Minimize => L.T("Minimize");
    public static string Maximize => L.T("Maximize");
    public static string Close => L.T("Close");
    public static string Exit => L.T("Exit");
    public static string Attribution => L.T("Attribution");
    public static string License => L.T("About");
    public static string ReadFailed => L.T("ReadFailed");
    public static string RefreshFailed => L.T("RefreshFailed");
    public static string ReadError => L.T("ReadFailed");
    public static string RefreshHint => Refresh + " (F5)";
    public static string ReadErrors(int count) => L.F("ReadErrors", count);
    public static string[] ContextActions => [Delete, L.T("Force"), L.T("FindJunk"), L.T("RemoveEntry"), L.T("Folder"), L.T("Registry"), L.T("Website"), L.T("Google"), L.T("SourceEditor"), L.T("CheckSelected")];
    public static string[] Columns => new[] { "ProgramColumn", "SizeColumn", "InstalledColumn", "VersionColumn", "StatusColumn", "NewColumn" }.Select(L.T).ToArray();
    public static string[] Menus => new[] { "FileMenu", "ActionsMenu", "ViewMenu", "HelpMenu" }.Select(L.T).ToArray();
    public static string Count(int shown, int total) => shown == total ? L.F("Count", total) : L.F("Found", shown, total);
    public static string Size(long? bytes) => bytes switch
    {
        null => Empty,
        >= 1073741824 => $"{bytes.Value / 1073741824d:0.##} GB",
        >= 1048576 => $"{bytes.Value / 1048576d:0.##} MB",
        _ => $"{bytes.Value / 1024d:0.##} KB"
    };
}

internal static class GlassTeal
{
    public static Color Background => Theme.Light ? Color.FromArgb(246, 247, 248) : Color.FromArgb(27, 32, 36);
    public static Color Surface => Theme.Blend(Background, Theme.Accent, Theme.Light ? .09f : .12f);
    public static Color Stripe => Theme.Light ? Color.FromArgb(234, 237, 239) : Color.FromArgb(32, 38, 42);
    public static Color Border => Theme.Light ? Color.FromArgb(191, 202, 207) : Color.FromArgb(48, 65, 72);
    public static Color Text => Theme.Light ? Color.FromArgb(28, 36, 42) : Color.FromArgb(230, 241, 245);
    public static Color Muted => Theme.Light ? Color.FromArgb(84, 101, 112) : Color.FromArgb(136, 164, 176);
    public static Color Accent => Theme.Accent;
    public static Color Selection => Theme.Blend(Background, Theme.Accent, .24f);
    public static Color Caption => Theme.Blend(Background, Theme.Accent, .14f);
    public static readonly Color StatusTop = Color.FromArgb(84, 110, 125);
    public static readonly Color StatusBottom = Color.FromArgb(40, 60, 73);
    public static readonly Color ButtonSelected = Color.FromArgb(25, 103, 118);
    public static readonly Color ButtonHover = Color.FromArgb(48, 86, 100);
    public static readonly Color ButtonTop = Color.FromArgb(39, 62, 74);
    public static readonly Color ButtonBottom = Color.FromArgb(20, 36, 45);
    public static readonly Color Shine = Color.FromArgb(75, 159, 188, 195);

    public static void Fill(Graphics g, Rectangle r, Color top, Color bottom)
    {
        if (r.Width < 1 || r.Height < 1) return;
        using var brush = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical);
        g.FillRectangle(brush, r);
    }
}

internal enum Glyph { Delete, Refresh, Update, Theme, Language, Programs, Store, Minimize, Maximize, Close }

internal sealed class GlassButton : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Glyph Glyph { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ActionEnabled { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CaptionButton { get; init; }
    private bool _hover;

    public GlassButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new GlassAccessible(this);
    private sealed class GlassAccessible(GlassButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.ActionEnabled ? 0 : AccessibleStates.Unavailable);
        public override string? DefaultAction => owner.ActionEnabled ? UiText.Press : UiText.Disabled;
        public override void DoDefaultAction() { if (owner.ActionEnabled) owner.OnClick(EventArgs.Empty); }
    }
    protected override void OnClick(EventArgs e) { if (ActionEnabled) base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Parent paints the continuous panel; icons have no backing plate or frame.
        if (_hover && ActionEnabled)
        {
            using var hover = new SolidBrush(Color.FromArgb(22, GlassTeal.Accent));
            g.FillEllipse(hover, 2, 2, Width - 4, Height - 4);
        }
        var size = Math.Min(Width, Height) * .60f;
        var old = g.Save();
        g.TranslateTransform((Width - size) / 2, (Height - size) / 2);
        g.ScaleTransform(size / 24, size / 24);
        Color Ink(Color color) => Color.FromArgb(ActionEnabled ? 255 : 120, color);
        using var pen = new Pen(Ink(GlassTeal.Text), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var teal = new Pen(Ink(Color.FromArgb(74, 194, 183)), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var blue = new SolidBrush(Ink(Color.FromArgb(69, 151, 222)));
        using var coral = new SolidBrush(Ink(Color.FromArgb(218, 120, 105)));
        using var gold = new SolidBrush(Ink(Color.FromArgb(228, 179, 83)));
        void Logo(float x, float y, float side)
        {
            var half = (side - 1.5f) / 2;
            Color[] colors = [Color.FromArgb(242, 80, 34), Color.FromArgb(127, 186, 0),
                Color.FromArgb(0, 164, 239), Color.FromArgb(255, 185, 0)];
            for (var i = 0; i < 4; i++)
            {
                using var tile = new SolidBrush(Ink(colors[i]));
                g.FillRectangle(tile, x + i % 2 * (half + 1.5f), y + i / 2 * (half + 1.5f), half, half);
            }
        }
        switch (Glyph)
        {
            case Glyph.Delete:
                g.FillPolygon(coral, new Point[] { new(5, 7), new(19, 7), new(17, 22), new(7, 22) });
                using (var lid = new Pen(Ink(Color.FromArgb(239, 153, 132)), 2))
                { g.DrawLine(lid, 4, 5, 20, 5); g.DrawLines(lid, new Point[] { new(9, 4), new(9, 2), new(15, 2), new(15, 4) }); }
                g.DrawLine(pen, 10, 10, 10, 18); g.DrawLine(pen, 14, 10, 14, 18); break;
            case Glyph.Refresh:
                g.DrawArc(teal, 4, 4, 16, 16, 205, 135); g.DrawArc(teal, 4, 4, 16, 16, 25, 135);
                g.DrawLines(teal, new Point[] { new(17, 7), new(22, 7), new(22, 2) });
                g.DrawLines(teal, new Point[] { new(2, 22), new(2, 17), new(7, 17) }); break;
            case Glyph.Update:
                g.FillPolygon(blue, new Point[] { new(12, 2), new(22, 12), new(16, 12), new(16, 22), new(8, 22), new(8, 12), new(2, 12) }); break;
            case Glyph.Theme:
                g.FillEllipse(gold, 2, 2, 20, 20);
                g.FillEllipse(coral, 6, 6, 4, 4); g.FillEllipse(blue, 14, 5, 4, 4);
                using (var spot = new SolidBrush(Ink(Color.FromArgb(65, 154, 137)))) g.FillEllipse(spot, 16, 12, 4, 4);
                using (var hole = new SolidBrush(Ink(GlassTeal.Background))) g.FillEllipse(hole, 6, 14, 3, 3); break;
            case Glyph.Language:
                g.FillEllipse(blue, 2, 2, 20, 20); g.DrawEllipse(pen, 7, 2, 10, 20);
                g.DrawLine(pen, 3, 8, 21, 8); g.DrawLine(pen, 3, 16, 21, 16); break;
            case Glyph.Programs: Logo(2, 2, 20); break;
            case Glyph.Store:
                using (var handle = new Pen(Ink(Color.FromArgb(111, 182, 240)), 2))
                    g.DrawArc(handle, 8, 1, 8, 11, 180, 180);
                g.FillPolygon(blue, new Point[] { new(4, 7), new(20, 7), new(22, 22), new(2, 22) });
                Logo(7, 10, 10); break;
            case Glyph.Minimize: g.DrawLine(pen, 5, 13, 19, 13); break;
            case Glyph.Maximize: g.DrawRectangle(pen, 5, 5, 14, 14); break;
            case Glyph.Close: g.DrawLine(pen, 5, 5, 19, 19); g.DrawLine(pen, 19, 5, 5, 19); break;
        }
        g.Restore(old);
        if (Selected || (Focused && ActionEnabled))
        {
            using var marker = new Pen(GlassTeal.Accent, Math.Max(2, DeviceDpi / 96f * 2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(marker, Width * .35f, Height - 2, Width * .65f, Height - 2);
        }
    }
}

internal sealed class GlassRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => GlassTeal.Fill(e.Graphics, e.AffectedBounds, GlassTeal.Surface, GlassTeal.Background);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected) { using var b = new SolidBrush(GlassTeal.Selection); e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size)); }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? GlassTeal.Text : GlassTeal.Muted;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
}
