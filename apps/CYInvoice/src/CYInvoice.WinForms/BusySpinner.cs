using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

// A single circular busy indicator shared by startup and Excel import.
// WinForms has no native circular marquee control, so this retains the already-used
// 12-ray design without creating a second animation or window-message path.
internal sealed class BusySpinner : Control
{
    private const int RayCount = 12;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 80 };
    private int frame;

    internal bool IsAnimating => animation.Enabled;
    internal int Frame => frame;

    internal BusySpinner()
    {
        Size = new Size(48, 48);
        BackColor = Color.White;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        animation.Tick += (_, _) => Advance();
    }

    internal void StartAnimation()
    {
        if (IsDisposed) return;
        animation.Start();
        Invalidate();
    }

    internal void StopAnimation()
    {
        animation.Stop();
    }

    internal void Advance()
    {
        frame = (frame + 1) % RayCount;
        if (!IsDisposed) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var diameter = Math.Min(ClientSize.Width, ClientSize.Height);
        if (diameter < 8) return;
        var centerX = ClientSize.Width / 2F;
        var centerY = ClientSize.Height / 2F;
        var inner = diameter * (13F / 48F);
        var outer = diameter * (20F / 48F);
        for (var index = 0; index < RayCount; index++)
        {
            var angle = (float)(index * Math.PI * 2 / RayCount);
            var opacity = 65 + ((index + frame) % RayCount) * 16;
            using var pen = new Pen(Color.FromArgb(opacity, 37, 99, 235),
                Math.Max(1.6F, diameter * 3.5F / 48F))
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

    // Test actual pixels and a timer-driven frame, not merely Timer.Enabled.
    // The window is synthetic and never reads settings, Excel or invoice records.
    internal static void VerifyAnimationSmoke()
    {
        using var host = new Form { ClientSize = new Size(90, 90) };
        using var spinner = new BusySpinner { Location = new Point(16, 16) };
        host.Controls.Add(spinner);
        host.Show();
        Application.DoEvents();
        spinner.StartAnimation();
        if (!spinner.IsAnimating)
            throw new InvalidOperationException("轉圈動畫計時器沒有啟動");

        var firstFrame = spinner.Frame;
        var until = Environment.TickCount64 + 1200;
        while (spinner.Frame == firstFrame && Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(12);
        }
        if (spinner.Frame == firstFrame)
            throw new InvalidOperationException("轉圈動畫未在 UI 訊息迴圈中前進");

        using var before = new Bitmap(spinner.Width, spinner.Height);
        using var after = new Bitmap(spinner.Width, spinner.Height);
        spinner.DrawToBitmap(before, new Rectangle(Point.Empty, spinner.Size));
        spinner.Advance();
        spinner.Update();
        spinner.DrawToBitmap(after, new Rectangle(Point.Empty, spinner.Size));
        var painted = false;
        var changed = false;
        for (var y = 0; y < spinner.Height; y++)
        for (var x = 0; x < spinner.Width; x++)
        {
            var prior = before.GetPixel(x, y);
            var next = after.GetPixel(x, y);
            if (prior.ToArgb() != spinner.BackColor.ToArgb()) painted = true;
            if (prior.ToArgb() != next.ToArgb()) changed = true;
        }
        spinner.StopAnimation();
        if (spinner.IsAnimating || !painted || !changed)
            throw new InvalidOperationException("轉圈動畫沒有可見的旋轉畫面或結束時仍在運轉");
        host.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animation.Dispose();
        base.Dispose(disposing);
    }
}
