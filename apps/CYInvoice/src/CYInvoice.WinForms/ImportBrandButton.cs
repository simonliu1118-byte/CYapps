namespace CYInvoice.WinForms;

internal enum ImportBrand
{
    Digiwin,
    MoShop,
    Coupang,
}

internal sealed class ImportBrandButton : RoundedButton
{
    private readonly ImportBrand brand;
    public ImportBrandButton(string text, ImportBrand brand)
    {
        this.brand = brand;
        Text = text;
        AccessibleName = text;
        Width = UiControls.StandardButtonWidth;
        Height = UiControls.StandardButtonHeight;
        Margin = new Padding(6, 2, 6, 2);
        AutoSize = false;
    }

    protected override ButtonColors ResolveColors() => new(Color.White,
        brand == ImportBrand.Coupang ? Color.FromArgb(188, 188, 188) : Color.FromArgb(75, 75, 75), Color.White);

    protected override void PaintContent(Graphics graphics, Rectangle bounds, ButtonColors colors)
    {
        if (brand == ImportBrand.Digiwin) DrawDigiwin(graphics, bounds);
        else if (brand == ImportBrand.MoShop) DrawMoShop(graphics, bounds);
        else DrawCoupang(graphics, bounds);

        if (!Enabled)
        {
            using var disabled = new SolidBrush(Color.FromArgb(125, SystemColors.Control));
            graphics.FillRectangle(disabled, bounds);
        }
        else if (Pressed)
        {
            using var down = new SolidBrush(Color.FromArgb(42, Color.Black));
            graphics.FillRectangle(down, bounds);
        }
        else if (Hovered)
        {
            using var over = new SolidBrush(Color.FromArgb(32, Color.White));
            graphics.FillRectangle(over, bounds);
        }
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
        using (var background = new SolidBrush(Hovered && Enabled ? Color.FromArgb(246, 251, 255) : Color.White))
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

}

internal sealed class PrimaryActionButton : RoundedButton
{
    private bool production;

    public PrimaryActionButton()
    {
        Width = 190;
        Height = 46;
        Margin = Padding.Empty;
        AutoSize = false;
        ForeColor = Color.White;
        Font = new Font("Microsoft JhengHei UI", 14F, FontStyle.Bold);
    }

    public void SetEnvironment(bool isProduction)
    {
        production = isProduction;
        Invalidate();
    }

    protected override ButtonColors ResolveColors()
    {
        if (!Enabled) return RoundedButtonSurface.SecondaryColors(false, false, false);
        var normal = production ? Color.FromArgb(3, 155, 229) : Color.FromArgb(25, 135, 84);
        var hover = production ? Color.FromArgb(41, 182, 246) : Color.FromArgb(31, 157, 99);
        var down = production ? Color.FromArgb(2, 119, 189) : Color.FromArgb(19, 108, 67);
        var border = production ? Color.FromArgb(2, 119, 189) : Color.FromArgb(18, 105, 65);
        return new ButtonColors(Pressed ? down : Hovered ? hover : normal, border, Color.White);
    }
}
