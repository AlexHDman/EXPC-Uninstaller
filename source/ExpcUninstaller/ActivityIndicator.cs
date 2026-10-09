using System.Drawing.Drawing2D;
namespace ExpcUninstaller;
internal enum OperationActivity { Idle, Busy, Success, Error }
internal sealed class ActivityIndicator : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 };
    private float _angle;
    internal OperationActivity Activity { get; private set; }
    internal void Set(OperationActivity activity) { Activity = activity; _timer.Enabled = activity == OperationActivity.Busy; Invalidate(); }
    internal ActivityIndicator() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); _timer.Tick += (_, _) => { _angle = (_angle + 18) % 360; Invalidate(); }; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var r = Rectangle.Inflate(ClientRectangle, -4, -4); if (r.Width < 1 || r.Height < 1) return;
        using var ring = new Pen(Theme.Blend(GlassTeal.Background, GlassTeal.Accent, .3f), Math.Max(1, DeviceDpi / 64f)); e.Graphics.DrawEllipse(ring, r);
        if (Activity == OperationActivity.Busy) { using var pen = new Pen(GlassTeal.Accent, ring.Width + 1) { StartCap = LineCap.Round, EndCap = LineCap.Round }; e.Graphics.DrawArc(pen, r, _angle, 230); }
        if (Activity == OperationActivity.Success) { using var pen = new Pen(Color.FromArgb(64,190,115), ring.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round }; e.Graphics.DrawLines(pen, new[] { new PointF(r.Left+r.Width*.22f,r.Top+r.Height*.52f),new PointF(r.Left+r.Width*.43f,r.Top+r.Height*.72f),new PointF(r.Left+r.Width*.8f,r.Top+r.Height*.28f) }); }
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
