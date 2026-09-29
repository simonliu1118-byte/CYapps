using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

/// <summary>
/// CY desktop visual-guide Danger button: native Button behavior with owner-painted surface only.
/// Geometry follows the approved WinForms V7 reference (small fixed radius and symmetric paint inset).
/// </summary>
internal sealed class ThemedDangerButton : NoFocusCueButton
{
    private static readonly Color Danger = Color.FromArgb(180, 55, 55);      // #B43737
    private static readonly Color DangerHover = Color.FromArgb(158, 45, 45);
    private static readonly Color DangerPressed = Color.FromArgb(143, 38, 38);
    private static readonly Color DangerBorder = Color.FromArgb(143, 38, 38);
    private bool hovered;
    private bool pressed;

    internal ThemedDangerButton(string text)
    {
        Text = text;
        Width = UiControls.StandardButtonWidth;
        Height = UiControls.StandardButtonHeight;
        Margin = new Padding(6, 2, 6, 2);
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left) pressed = true;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter) pressed = true;
        Invalidate();
        base.OnKeyDown(eventArgs);
    }

    protected override void OnKeyUp(KeyEventArgs eventArgs)
    {
        pressed = false;
        Invalidate();
        base.OnKeyUp(eventArgs);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnGotFocus(eventArgs);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        pressed = false;
        Invalidate();
        base.OnLostFocus(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs) =>
        eventArgs.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        var bounds = new RectangleF(1f, 1.5f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 4f));
        using var path = RoundedRectangle(bounds, 2f);
        var (fill, border, text) = ResolveColors();
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, Focused && Enabled ? 1.5f : 1f);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);

        TextRenderer.DrawText(graphics, Text, Font, Rectangle.Round(bounds), text,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }

    private (Color Fill, Color Border, Color Text) ResolveColors()
    {
        if (!Enabled)
            return (SystemColors.ControlDark, SystemColors.ControlDarkDark, SystemColors.GrayText);
        var fill = pressed ? DangerPressed : hovered ? DangerHover : Danger;
        return (fill, DangerBorder, Color.White);
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
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
