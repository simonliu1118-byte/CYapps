using System.Drawing.Drawing2D;

namespace CYInvoiceVisualShell;

internal sealed class RoundedThemeButtonV4 : Button
{
    private CyTheme theme = CyTheme.Blue;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private bool hovered;
    private bool pressed;

    internal CyButtonRole Role { get; set; } = CyButtonRole.Primary;
    internal int CornerRadius { get; set; } = 7;

    internal RoundedThemeButtonV4(string text, CyButtonRole role = CyButtonRole.Primary)
    {
        Text = text;
        Role = role;
        AutoSize = false;
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

    internal void ApplyTheme(CyTheme newTheme, ThemePalette newPalette, Font font)
    {
        theme = newTheme;
        palette = newPalette;
        Font = font;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) pressed = true;
        Invalidate();
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        // Same key idea as the proven CYInvoice PrimaryActionButton:
        // never paint the anti-aliased edge directly on the control boundary.
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = RoundedRectangle(bounds, CornerRadius);

        var (fill, border, text) = ResolveColors();
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, Focused && Enabled ? 1.5f : 1f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            bounds,
            text,
            TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }

    private (Color Fill, Color Border, Color Text) ResolveColors()
    {
        if (!Enabled)
            return (SystemColors.ControlDark, SystemColors.ControlDarkDark, SystemColors.GrayText);

        if (Role == CyButtonRole.Danger)
        {
            var fill = pressed
                ? Color.FromArgb(143, 38, 38)
                : hovered ? VisualTokens.DangerHover : VisualTokens.Danger;
            return (fill, pressed ? Color.FromArgb(120, 30, 30) : VisualTokens.DangerHover, Color.White);
        }

        // Coral intentionally uses a pale pink surface so it remains clearly separate from Danger red.
        if (theme == CyTheme.Coral)
        {
            var fill = pressed
                ? Blend(palette.Soft, palette.Accent, 0.30f)
                : hovered ? Blend(palette.Soft, palette.Accent, 0.16f) : palette.Soft;
            return (fill, palette.Accent, palette.Pressed);
        }

        var normal = palette.Accent;
        var hover = palette.Hover;
        var down = palette.Pressed;
        var current = pressed ? down : hovered ? hover : normal;
        return (current, down, Color.White);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
