using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

// Shared circular busy indicator for startup and Excel import. It only paints;
// callers own when work starts and ends.
internal sealed class BusySpinner : Control
{
    private const int RayCount = 12;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 80 };
    private int frame;

    public BusySpinner(int size = 48)
    {
        Size = new Size(size, size);
        BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        animation.Tick += (_, _) => Advance();
    }

    public bool IsSpinning => animation.Enabled;

    public void Start()
    {
        Visible = true;
        animation.Start();
    }

    public void Stop()
    {
        animation.Stop();
    }

    internal void Advance()
    {
        frame = (frame + 1) % RayCount;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = Math.Min(ClientSize.Width, ClientSize.Height);
        var centerX = ClientSize.Width / 2F;
        var centerY = ClientSize.Height / 2F;
        var inner = diameter * 13F / 48F;
        var outer = diameter * 20F / 48F;
        for (var index = 0; index < RayCount; index++)
        {
            var angle = (float)(index * Math.PI * 2 / RayCount);
            var opacity = 65 + ((index + frame) % RayCount) * 16;
            using var pen = new Pen(Color.FromArgb(opacity, 37, 99, 235), Math.Max(2F, diameter * 3.5F / 48F))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            var dx = (float)Math.Cos(angle);
            var dy = (float)Math.Sin(angle);
            graphics.DrawLine(pen, centerX + dx * inner, centerY + dy * inner,
                centerX + dx * outer, centerY + dy * outer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}
