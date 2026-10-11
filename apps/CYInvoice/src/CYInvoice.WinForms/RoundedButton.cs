using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

internal readonly record struct ButtonColors(Color Fill, Color Border, Color Text);

// One paint/state owner for all app buttons. Click, DialogResult, keyboard and
// accessibility remain on WinForms Button; only the surface is custom painted.
internal class RoundedButton : Button
{
    protected bool Hovered { get; private set; }
    protected bool Pressed { get; private set; }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected virtual ButtonColors ResolveColors() => RoundedButtonSurface.SecondaryColors(Enabled, Hovered, Pressed);

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UiControls.HideFocusCue(this); }
    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Pressed = true;
        Invalidate();
        base.OnMouseDown(e);
        UiControls.HideFocusCue(this);
    }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) Pressed = true;
        Invalidate();
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e) { Pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); UiControls.HideFocusCue(this); }
    protected override void OnLostFocus(EventArgs e) { Pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) Pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Pressed = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var saved = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            var bounds = RoundedButtonSurface.PaintBounds(ClientRectangle, DeviceDpi);
            using var path = RoundedButtonSurface.Path(bounds, DeviceDpi);
            var colors = SystemInformation.HighContrast
                ? RoundedButtonSurface.SecondaryColors(Enabled, Hovered, Pressed) : ResolveColors();
            using (var fill = new SolidBrush(colors.Fill)) graphics.FillPath(fill, path);
            var clip = graphics.Save();
            graphics.SetClip(path, CombineMode.Intersect);
            if (SystemInformation.HighContrast) PaintDefaultContent(graphics, Rectangle.Round(bounds), colors.Text);
            else PaintContent(graphics, Rectangle.Round(bounds), colors);
            graphics.Restore(clip);
            var border = Focused && Enabled
                ? (SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(37, 99, 235)) : colors.Border;
            using var pen = new Pen(border, (Focused && Enabled ? 1.5f : 1f) * DeviceDpi / 96f);
            graphics.DrawPath(pen, path);
        }
        finally { graphics.Restore(saved); }
    }

    protected virtual void PaintContent(Graphics graphics, Rectangle bounds, ButtonColors colors) =>
        PaintDefaultContent(graphics, bounds, colors.Text);

    private void PaintDefaultContent(Graphics graphics, Rectangle bounds, Color text)
    {
        if (Image is { } image)
        {
            var imageBounds = Align(image.Size, bounds, ImageAlign);
            if (Enabled) graphics.DrawImage(image, imageBounds);
            else ControlPaint.DrawImageDisabled(graphics, image, imageBounds.X, imageBounds.Y, BackColor);
            if (TextImageRelation == TextImageRelation.ImageAboveText)
                bounds = new Rectangle(bounds.Left, Math.Max(bounds.Top, imageBounds.Bottom + 4), bounds.Width,
                    Math.Max(0, bounds.Bottom - imageBounds.Bottom - 4));
        }
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };
        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter,
        };
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
        else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(graphics, Text, Font, bounds, text, flags);
    }

    private static Rectangle Align(Size size, Rectangle bounds, ContentAlignment alignment)
    {
        var left = alignment is ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft
            ? bounds.Left : alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
            ? bounds.Right - size.Width : bounds.Left + (bounds.Width - size.Width) / 2;
        var top = alignment is ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight
            ? bounds.Top : alignment is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
            ? bounds.Bottom - size.Height : bounds.Top + (bounds.Height - size.Height) / 2;
        return new Rectangle(left, top, size.Width, size.Height);
    }
}

internal static class RoundedButtonSurface
{
    // AITeam's validated WinForms V7 geometry: fixed 2px radius, symmetric 1.5px inset.
    internal static RectangleF PaintBounds(Rectangle bounds, int dpi)
    {
        var scale = dpi / 96f;
        return new RectangleF(bounds.Left + scale, bounds.Top + 1.5f * scale,
            Math.Max(1, bounds.Width - 3 * scale), Math.Max(1, bounds.Height - 4 * scale));
    }

    internal static GraphicsPath Path(RectangleF bounds, int dpi)
    {
        var diameter = Math.Min(4f * dpi / 96f, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static ButtonColors SecondaryColors(bool enabled, bool hovered, bool pressed)
    {
        if (SystemInformation.HighContrast)
            return new ButtonColors(pressed ? SystemColors.Highlight : SystemColors.Control,
                SystemColors.WindowText, !enabled ? SystemColors.GrayText : pressed ? SystemColors.HighlightText : SystemColors.ControlText);
        return new ButtonColors(!enabled ? Color.FromArgb(241, 243, 245) : pressed ? Color.FromArgb(229, 232, 236)
            : hovered ? Color.FromArgb(241, 243, 245) : Color.White,
            Color.FromArgb(209, 213, 219), enabled ? Color.FromArgb(31, 41, 55) : Color.FromArgb(152, 162, 179));
    }

    internal static void DrawRowAction(Graphics graphics, Rectangle bounds, Font font, string text, int dpi,
        bool enabled, bool hovered, bool pressed)
    {
        var saved = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var paint = PaintBounds(bounds, dpi);
            using var path = Path(paint, dpi);
            var colors = SecondaryColors(enabled, hovered, pressed);
            using (var fill = new SolidBrush(colors.Fill)) graphics.FillPath(fill, path);
            using (var border = new Pen(colors.Border)) graphics.DrawPath(border, path);
            TextRenderer.DrawText(graphics, text, font, Rectangle.Round(paint),
                SystemInformation.HighContrast || !enabled ? colors.Text : Color.FromArgb(180, 55, 55),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
        finally { graphics.Restore(saved); }
    }
}
