using System.Drawing.Drawing2D;

namespace CYInvoice.WinForms;

internal enum ImportBrand
{
    Digiwin,
    MoShop,
    Coupang,
}

internal sealed class ImportBrandButton : NoFocusCueButton
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

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var shape = RoundedRectangle(bounds, 6);
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
    }

    private void DrawDigiwin(Graphics graphics, Rectangle bounds)
    {
        using (var background = new SolidBrush(Color.FromArgb(0, 157, 170)))
            graphics.FillRectangle(background, bounds);
        using (var accent = new SolidBrush(Color.FromArgb(229, 0, 45)))
            graphics.FillPolygon(accent,
            [
                new Point(bounds.Right - 20, bounds.Bottom),
                new Point(bounds.Right, bounds.Bottom),
                new Point(bounds.Right, bounds.Top + 8),
            ]);
        DrawCenteredText(graphics, Text, Color.White, bounds);
    }

    private void DrawMoShop(Graphics graphics, Rectangle bounds)
    {
        var split = bounds.Left + (bounds.Width / 2);
        using (var left = new SolidBrush(Color.FromArgb(229, 0, 170)))
            graphics.FillRectangle(left, bounds.Left, bounds.Top, split - bounds.Left, bounds.Height);
        using (var right = new SolidBrush(Color.FromArgb(45, 62, 117)))
            graphics.FillRectangle(right, split, bounds.Top, bounds.Right - split, bounds.Height);
        var textFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        var moWidth = TextRenderer.MeasureText(graphics, "MO", Font, Size.Empty, textFlags).Width;
        var shopWidth = TextRenderer.MeasureText(graphics, "店+", Font, Size.Empty, textFlags).Width;
        TextRenderer.DrawText(graphics, "MO", Font,
            new Rectangle(split - moWidth, bounds.Top, moWidth, bounds.Height),
            Enabled ? Color.White : SystemColors.GrayText, textFlags | TextFormatFlags.Right);
        TextRenderer.DrawText(graphics, "店+", Font,
            new Rectangle(split, bounds.Top, shopWidth, bounds.Height),
            Enabled ? Color.White : SystemColors.GrayText, textFlags | TextFormatFlags.Left);
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
        DrawAlignedText(graphics, text, color, bounds, TextFormatFlags.HorizontalCenter);
    }

    private void DrawAlignedText(Graphics graphics, string text, Color color, Rectangle bounds, TextFormatFlags alignment)
    {
        TextRenderer.DrawText(graphics, text, Font, bounds, Enabled ? color : SystemColors.GrayText,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | alignment);
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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

internal sealed class PrimaryActionButton : NoFocusCueButton
{
    private bool hovered;
    private bool pressed;
    private bool production;

    public PrimaryActionButton()
    {
        Width = 190;
        Height = 46;
        Margin = Padding.Empty;
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Font = new Font("Microsoft JhengHei UI", 14F, FontStyle.Bold);
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetEnvironment(bool isProduction)
    {
        production = isProduction;
        Invalidate();
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

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var normal = production ? Color.FromArgb(3, 155, 229) : Color.FromArgb(25, 135, 84);
        var hover = production ? Color.FromArgb(41, 182, 246) : Color.FromArgb(31, 157, 99);
        var down = production ? Color.FromArgb(2, 119, 189) : Color.FromArgb(19, 108, 67);
        var borderColor = production ? Color.FromArgb(2, 119, 189) : Color.FromArgb(18, 105, 65);
        var fill = !Enabled ? SystemColors.ControlDark : pressed ? down : hovered ? hover : normal;
        var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        using var path = ImportBrandButton.RoundedRectangle(bounds, 7);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(borderColor);
        eventArgs.Graphics.FillPath(brush, path);
        eventArgs.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(eventArgs.Graphics, Text, Font, bounds, Enabled ? Color.White : SystemColors.GrayText,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        UiControls.HideFocusCue(this);
    }
}
