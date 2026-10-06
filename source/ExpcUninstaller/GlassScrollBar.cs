namespace ExpcUninstaller;

// Compact scrollbar for the virtual grid. Does not enumerate or modify catalog entries.
internal sealed class GlassScrollBar : Control
{
    private int _total;
    private int _page = 1;
    private int _dragOffset = -1;
    public int Value { get; private set; }
    public event EventHandler? ValueChanged;
    private int Maximum => Math.Max(0, _total - _page);
    private int ThumbHeight => Math.Min(Height, Math.Max(24 * DeviceDpi / 96, Height * _page / Math.Max(1, _total)));
    private Rectangle Thumb => new(3, Maximum == 0 ? 0 : (int)((long)Value * (Height - ThumbHeight) / Maximum),
        Math.Max(2, Width - 6), ThumbHeight);

    public GlassScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
        BackColor = GlassTeal.Background;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = UiText.Scroll;
    }
    public void SetRange(int total, int page, int value)
    {
        _total = total; _page = page; Value = Math.Clamp(value, 0, Maximum); Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Maximum == 0) return;
        using var brush = new SolidBrush(GlassTeal.Muted);
        e.Graphics.FillRectangle(brush, Thumb);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Maximum == 0) return;
        if (Thumb.Contains(e.Location)) { _dragOffset = e.Y - Thumb.Top; Capture = true; }
        else Change(Value + (e.Y < Thumb.Top ? -_page : _page));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragOffset >= 0 && Height > ThumbHeight)
            Change((int)((long)Math.Clamp(e.Y - _dragOffset, 0, Height - ThumbHeight) * Maximum / (Height - ThumbHeight)));
    }
    protected override void OnMouseUp(MouseEventArgs e) { _dragOffset = -1; Capture = false; base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragOffset = -1; base.OnMouseCaptureChanged(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Change(Value - Math.Sign(e.Delta) * 3); base.OnMouseWheel(e); }
    private void Change(int value)
    {
        Value = Math.Clamp(value, 0, Maximum); Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
