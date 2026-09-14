using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

internal enum ImportBrand
{
    Digiwin,
    MoShop,
    Coupang,
}

internal sealed class ImportBrandButton : Button
{
    private readonly ImportBrand brand;
    private bool hovered;
    private bool pressed;

    public ImportBrandButton(string text, ImportBrand brand)
    {
        this.brand = brand;
        Text = text;
        AccessibleName = text;
        Width = UiControls.StandardButtonWidth;
        Height = UiControls.StandardButtonHeight;
        Margin = new Padding(6, 2, 6, 2);
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var shape = RoundedRectangle(bounds, 5);
        graphics.SetClip(shape);

        if (brand == ImportBrand.Digiwin) DrawDigiwin(graphics, bounds);
        else if (brand == ImportBrand.MoShop) DrawMoShop(graphics, bounds);
        else DrawCoupang(graphics, bounds);

        if (!Enabled)
        {
            using var disabled = new SolidBrush(Color.FromArgb(125, SystemColors.Control));
            graphics.FillRectangle(disabled, bounds);
        }
        else if (pressed)
        {
            using var down = new SolidBrush(Color.FromArgb(42, Color.Black));
            graphics.FillRectangle(down, bounds);
        }
        else if (hovered)
        {
            using var over = new SolidBrush(Color.FromArgb(32, Color.White));
            graphics.FillRectangle(over, bounds);
        }

        graphics.ResetClip();
        using var border = new Pen(brand == ImportBrand.Coupang ? Color.FromArgb(188, 188, 188) : Color.FromArgb(75, 75, 75));
        graphics.DrawPath(border, shape);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -4, -4));
    }

    private void DrawDigiwin(Graphics graphics, Rectangle bounds)
    {
        using (var background = new SolidBrush(Color.FromArgb(0, 157, 170)))
            graphics.FillRectangle(background, bounds);
        using (var accent = new SolidBrush(Color.FromArgb(229, 0, 45)))
            graphics.FillPolygon(accent,
            new[]
            {
                new Point(bounds.Right - 20, bounds.Bottom),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Right, bounds.Top + 8),
            });
        DrawCenteredText(graphics, Text, Color.White, bounds);
    }

    private void DrawMoShop(Graphics graphics, Rectangle bounds)
    {
        var split = Math.Max(46, (int)Math.Round(bounds.Width * 0.42));
        using (var left = new SolidBrush(Color.FromArgb(229, 0, 170)))
            graphics.FillRectangle(left, bounds.Left, bounds.Top, split, bounds.Height);
        using (var right = new SolidBrush(Color.FromArgb(45, 62, 117)))
            graphics.FillRectangle(right, bounds.Left + split, bounds.Top, bounds.Width - split, bounds.Height);
        DrawCenteredText(graphics, "MO", Color.White, new Rectangle(bounds.Left, bounds.Top, split, bounds.Height));
        DrawCenteredText(graphics, "店+", Color.White, new Rectangle(bounds.Left + split, bounds.Top, bounds.Width - split, bounds.Height));
    }

    private void DrawCoupang(Graphics graphics, Rectangle bounds)
    {
        using (var background = new SolidBrush(hovered && Enabled ? Color.FromArgb(246, 251, 255) : Color.White))
            graphics.FillRectangle(background, bounds);
        var characters = new[] { "酷", "澎", "商", "城" };
        var colors = new[]
        {
            Color.FromArgb(145, 69, 18),
            Color.FromArgb(238, 126, 0),
            Color.FromArgb(112, 176, 0),
            Color.FromArgb(0, 142, 196),
        };
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        var widths = characters.Select(character => TextRenderer.MeasureText(graphics, character, Font, Size.Empty, flags).Width).ToArray();
        var left = bounds.Left + ((bounds.Width - widths.Sum()) / 2);
        for (var index = 0; index < characters.Length; index++)
        {
            var color = Enabled ? colors[index] : SystemColors.GrayText;
            TextRenderer.DrawText(graphics, characters[index], Font,
                new Rectangle(left, bounds.Top, widths[index], bounds.Height), color, flags);
            left += widths[index];
        }
    }

    private void DrawCenteredText(Graphics graphics, string text, Color color, Rectangle bounds)
    {
        TextRenderer.DrawText(graphics, text, Font, bounds, Enabled ? color : SystemColors.GrayText,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
