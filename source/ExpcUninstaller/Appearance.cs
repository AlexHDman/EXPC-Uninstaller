using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ExpcUninstaller;

internal static class UiText
{
    public const string Title = "EXPC Uninstaller";
    public const string Design = "EXPC-002";
    public const string Empty = "—";
    public const string Ready = "Готово";
    public const string Loading = "Чтение списка…";
    public const string Search = "Поиск программ";
    public const string Unknown = "Не проверено. Проверка версий пока не подключена.";
    public const string Disabled = "Будет подключено на следующем этапе";
    public const string Scroll = "Прокрутка списка";
    public const string Press = "Нажать";
    public const string Delete = "Удалить";
    public const string Refresh = "Обновить список";
    public const string RefreshHint = "Обновить список (F5)";
    public const string CheckVersions = "Проверить версии";
    public const string Theme = "Тема: Glass Teal";
    public const string Language = "Язык: русский";
    public const string ThemeHint = "Тема: Glass Teal. Выбор темы пока недоступен";
    public const string LanguageHint = "Язык: русский. Выбор языка пока недоступен";
    public const string Programs = "Программы";
    public const string Store = "Microsoft Store";
    public const string StoreHint = "Microsoft Store — текущий пользователь";
    public const string Minimize = "Свернуть";
    public const string Maximize = "Развернуть / восстановить";
    public const string Close = "Закрыть";
    public const string Exit = "Выход";
    public const string Attribution = "EXPC-002 · на основе BCU / Marcin Szeniak";
    public const string License = "Лицензия и авторство: Licence.txt / NOTICE";
    public const string ReadFailed = "Не удалось прочитать список";
    public const string RefreshFailed = "Сбой обновления; прежний список";
    public const string ReadError = "Ошибка чтения: ";
    public static string ReadErrors(int count) => $"Ошибки чтения: {count}";
    public static readonly string[] FutureActions = [Delete, "Принудительное удаление", "Поиск остатков", "Удалить запись", CheckVersions];
    public static readonly string[] ContextActions = [Delete, "Принудительное удаление", "Поиск остатков", "Удалить запись",
        "Папка программы", "Запись реестра", "Официальный сайт", "Google", "Источник обновлений"];
    public static readonly string[] Columns = ["Программа", "Размер", "Установлено", "Версия", "Статус", "Новая"];
    public static readonly string[] Menus = ["Файл", "Действия", "Вид", "Справка"];
    public static string Count(int shown, int total) => shown == total ? $"Программ: {total}" : $"Найдено: {shown} из {total}";
    public static string Size(long? bytes) => bytes switch
    {
        null => Empty,
        >= 1073741824 => $"{bytes.Value / 1073741824d:0.##} ГБ",
        >= 1048576 => $"{bytes.Value / 1048576d:0.##} МБ",
        _ => $"{bytes.Value / 1024d:0.##} КБ"
    };
}

internal static class GlassTeal
{
    public static readonly Color Background = Color.FromArgb(17, 29, 35);
    public static readonly Color Surface = Color.FromArgb(23, 40, 49);
    public static readonly Color Stripe = Color.FromArgb(27, 44, 53);
    public static readonly Color Border = Color.FromArgb(48, 76, 87);
    public static readonly Color Text = Color.FromArgb(230, 241, 245);
    public static readonly Color Muted = Color.FromArgb(136, 164, 176);
    public static readonly Color Accent = Color.FromArgb(58, 220, 222);
    public static readonly Color Selection = Color.FromArgb(23, 75, 88);
    public static readonly Color Caption = Color.FromArgb(29, 56, 68);
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
                 ControlStyles.Selectable, true);
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
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        GlassTeal.Fill(g, ClientRectangle, GlassTeal.Surface, GlassTeal.Background);
        if (!CaptionButton || _hover)
        {
            var top = Selected ? GlassTeal.ButtonSelected : _hover && ActionEnabled ? GlassTeal.ButtonHover : GlassTeal.ButtonTop;
            GlassTeal.Fill(g, r, top, GlassTeal.ButtonBottom);
            using var border = new Pen(Selected ? GlassTeal.Accent : GlassTeal.Border);
            g.DrawRectangle(border, r);
            using var shine = new Pen(GlassTeal.Shine);
            g.DrawLine(shine, 3, 3, Width - 4, 3);
        }
        var size = Math.Min(Width, Height) * .55f;
        var old = g.Save();
        g.TranslateTransform((Width - size) / 2, (Height - size) / 2);
        g.ScaleTransform(size / 24, size / 24);
        using var pen = new Pen(ActionEnabled ? GlassTeal.Text : GlassTeal.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var accent = new SolidBrush(ActionEnabled ? GlassTeal.Accent : GlassTeal.Muted);
        switch (Glyph)
        {
            case Glyph.Delete:
                g.DrawRectangle(pen, 5, 6, 14, 16); g.DrawLine(pen, 3, 5, 21, 5); g.DrawRectangle(pen, 9, 2, 6, 3);
                g.DrawLine(pen, 10, 9, 10, 18); g.DrawLine(pen, 14, 9, 14, 18); break;
            case Glyph.Refresh:
                g.DrawArc(pen, 3, 3, 18, 18, 200, 145); g.DrawArc(pen, 3, 3, 18, 18, 20, 145);
                g.DrawLines(pen, new Point[] { new(17, 7), new(22, 7), new(22, 2) });
                g.DrawLines(pen, new Point[] { new(2, 22), new(2, 17), new(7, 17) }); break;
            case Glyph.Update:
                g.FillPolygon(accent, new Point[] { new(12, 2), new(22, 12), new(16, 12), new(16, 22), new(8, 22), new(8, 12), new(2, 12) }); break;
            case Glyph.Theme:
                g.DrawEllipse(pen, 2, 2, 20, 19); g.FillEllipse(accent, 7, 6, 3, 3); g.FillEllipse(accent, 14, 5, 3, 3);
                g.FillEllipse(accent, 17, 11, 3, 3); g.FillEllipse(accent, 5, 12, 3, 3); break;
            case Glyph.Language:
                g.DrawEllipse(pen, 2, 2, 20, 20); g.DrawEllipse(pen, 7, 2, 10, 20); g.DrawLine(pen, 3, 8, 21, 8); g.DrawLine(pen, 3, 16, 21, 16); break;
            case Glyph.Programs:
                foreach (var x in new[] { 3, 14 }) foreach (var y in new[] { 3, 14 }) g.FillRectangle(accent, x, y, 7, 7); break;
            case Glyph.Store:
                g.DrawRectangle(pen, 4, 7, 16, 15); g.DrawArc(pen, 8, 1, 8, 12, 180, 180); break;
            case Glyph.Minimize: g.DrawLine(pen, 5, 13, 19, 13); break;
            case Glyph.Maximize: g.DrawRectangle(pen, 5, 5, 14, 14); break;
            case Glyph.Close: g.DrawLine(pen, 5, 5, 19, 19); g.DrawLine(pen, 19, 5, 5, 19); break;
        }
        g.Restore(old);
        if (Focused && ActionEnabled) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8));
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
