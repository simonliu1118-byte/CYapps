using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

/// <summary>
/// CY's own region capture (user, 2026-10-11): the whole desktop is frozen into a dimmed
/// full-screen overlay, the user drags a rectangle, and releasing the mouse returns that part.
/// Esc (or a right click) cancels. Unlike Windows' snipping overlay, the result and a cancel
/// come straight back to CY, so the screenshot window can chain captures and stop on Esc.
/// </summary>
internal sealed class RegionCaptureForm : Form
{
    private readonly Bitmap _desktop;
    private readonly Rectangle _virtualScreen;
    private Point? _start;
    private Rectangle _selection;

    private RegionCaptureForm(Bitmap desktop, Rectangle virtualScreen)
    {
        _desktop = desktop;
        _virtualScreen = virtualScreen;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = virtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
    }

    /// <summary>The selected part of the screen, or null when cancelled.</summary>
    public static Bitmap? Capture(IWin32Window? owner)
    {
        var screen = SystemInformation.VirtualScreen;
        using var desktop = ScreenCapture.Capture(screen);
        using var form = new RegionCaptureForm(desktop, screen);
        if (form.ShowDialog(owner) != DialogResult.OK || form._selection.Width < 8 || form._selection.Height < 8) return null;
        return desktop.Clone(form._selection, desktop.PixelFormat);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; return; }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
        if (e.Button == MouseButtons.Left) _start = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start is { } s)
        {
            _selection = Rectangle.FromLTRB(Math.Min(s.X, e.X), Math.Min(s.Y, e.Y), Math.Max(s.X, e.X), Math.Max(s.Y, e.Y));
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _start is not null)
        {
            _start = null;
            _selection.Intersect(new Rectangle(Point.Empty, _virtualScreen.Size));
            DialogResult = _selection.Width >= 8 && _selection.Height >= 8 ? DialogResult.OK : DialogResult.None;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_desktop, 0, 0);
        using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
        using (var region = new Region(ClientRectangle))
        {
            if (_selection.Width > 0 && _selection.Height > 0) region.Exclude(_selection);
            g.FillRegion(dim, region);
        }
        if (_selection.Width > 0 && _selection.Height > 0)
        {
            using var pen = new Pen(CyVisualTheme.Accent, 2f) { DashStyle = DashStyle.Solid };
            g.DrawRectangle(pen, _selection);
        }

        const string hint = "拖曳框選蝦皮訂單的一段　放開即加入　Esc 結束截圖";
        using var font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
        var size = TextRenderer.MeasureText(g, hint, font);
        var primary = Screen.PrimaryScreen?.Bounds ?? _virtualScreen;
        var box = new Rectangle(primary.Left - _virtualScreen.Left + (primary.Width - size.Width) / 2 - 16,
            primary.Top - _virtualScreen.Top + 24, size.Width + 32, size.Height + 16);
        using (var back = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            g.FillRectangle(back, box);
        TextRenderer.DrawText(g, hint, font, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Small round red "×" for removing a screenshot card.</summary>
internal sealed class RemoveBadge : Control
{
    private bool _hovered;

    public RemoveBadge()
    {
        Size = new Size(22, 22);
        Cursor = Cursors.Hand;
        AccessibleName = "移除這張截圖";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var circle = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
        using (var fill = new SolidBrush(_hovered ? Color.FromArgb(185, 28, 28) : CyVisualTheme.Danger))
            g.FillEllipse(fill, circle);
        using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var inset = Width * 0.32f;
        g.DrawLine(pen, inset, inset, Width - inset - 1, Height - inset - 1);
        g.DrawLine(pen, Width - inset - 1, inset, inset, Height - inset - 1);
    }
}
