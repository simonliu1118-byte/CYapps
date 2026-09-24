using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

internal static class CyDrawing
{
    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = Math.Max(1f, radius * 2f);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CyPrimaryButton : Button
{
    private bool _hovered;
    private bool _pressed;

    internal float CornerRadius { get; set; } = 2f;

    public CyPrimaryButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) _pressed = true; Invalidate(); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        // Canonical WinForms V7 geometry: 1 px horizontal safety inset,
        // ~1.5 px vertical inset and a fixed ~2 px corner radius.
        var bounds = new RectangleF(1f, 1.5f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 4f));
        using var path = CyDrawing.RoundedRectangle(bounds, CornerRadius);

        var fill = !Enabled ? CyVisualTheme.AccentSoft
            : _pressed ? CyVisualTheme.AccentPressed
            : _hovered ? CyVisualTheme.AccentHover
            : CyVisualTheme.Accent;
        var border = Focused && Enabled ? CyVisualTheme.AccentFocus
            : Enabled ? CyVisualTheme.AccentPressed
            : CyVisualTheme.Border;
        var text = Enabled ? Color.White : CyVisualTheme.TextDisabled;

        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, Focused && Enabled ? 1.5f : 1f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(bounds), text,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ModeToggle : CheckBox
{
    public ModeToggle()
    {
        Text = "進階模式";
        AccessibleName = "進階模式";
        AccessibleDescription = "關閉為標準模式，開啟為進階模式";
        AccessibleRole = AccessibleRole.CheckButton;
        AutoCheck = true;
        AutoSize = false;
        Size = new Size(118, 34);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        CheckedChanged += (_, _) => Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) =>
        pevent.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        var leftText = new Rectangle(0, 0, 34, Height);
        var rightText = new Rectangle(84, 0, 34, Height);
        var track = new RectangleF(42f, 10f, 34f, 14f);
        var trackRadius = track.Height / 2f;

        var standardColor = Checked ? CyVisualTheme.TextSecondary : CyVisualTheme.TextPrimary;
        var advancedColor = Checked ? CyVisualTheme.TextPrimary : CyVisualTheme.TextSecondary;
        var standardFont = new Font(Font, Checked ? FontStyle.Regular : FontStyle.Bold);
        var advancedFont = new Font(Font, Checked ? FontStyle.Bold : FontStyle.Regular);
        try
        {
            TextRenderer.DrawText(g, "標準", standardFont, leftText, standardColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "進階", advancedFont, rightText, advancedColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        finally
        {
            standardFont.Dispose();
            advancedFont.Dispose();
        }

        using var trackPath = CyDrawing.RoundedRectangle(track, trackRadius);
        using var trackBrush = new SolidBrush(Checked ? CyVisualTheme.Accent : CyVisualTheme.Border);
        g.FillPath(trackBrush, trackPath);

        var thumbSize = 10f;
        var thumbX = Checked ? track.Right - thumbSize - 2f : track.Left + 2f;
        var thumb = new RectangleF(thumbX, track.Top + 2f, thumbSize, thumbSize);
        using var thumbBrush = new SolidBrush(Color.White);
        g.FillEllipse(thumbBrush, thumb);

        if (Focused)
        {
            var focusRect = RectangleF.Inflate(track, 2f, 2f);
            using var focusPath = CyDrawing.RoundedRectangle(focusRect, trackRadius + 2f);
            using var focusPen = new Pen(CyVisualTheme.AccentFocus, 1.5f);
            g.DrawPath(focusPen, focusPath);
        }
    }
}
