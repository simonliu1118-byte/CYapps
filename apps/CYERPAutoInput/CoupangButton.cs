using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

internal abstract class BrandButtonBase : Button
{
    private bool _hovered;
    private bool _pressed;

    protected BrandButtonBase()
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

    protected bool IsHovered => _hovered;
    protected bool IsPressed => _pressed;

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) _pressed = true; Invalidate(); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { _pressed = false; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        // Same proven V7 shell as the CY themed button: native Button behavior,
        // appearance-only owner paint, fixed ~2 px radius and symmetric inset.
        var bounds = new RectangleF(1f, 1.5f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 4f));
        using var path = CyDrawing.RoundedRectangle(bounds, 2f);
        PaintBrandSurface(g, path, bounds);

        using var borderPen = new Pen(ResolveBorderColor(), Focused && Enabled ? 1.5f : 1f);
        g.DrawPath(borderPen, path);
        PaintBrandContent(g, Rectangle.Round(bounds));
    }

    protected abstract void PaintBrandSurface(Graphics g, GraphicsPath path, RectangleF bounds);
    protected abstract void PaintBrandContent(Graphics g, Rectangle bounds);
    protected abstract Color NormalBorder { get; }

    protected virtual Color ResolveBorderColor()
    {
        if (!Enabled) return CyVisualTheme.Border;
        if (Focused) return CyVisualTheme.AccentFocus;
        return NormalBorder;
    }

    protected static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(color.A,
            (int)Math.Round(color.R * (1 - amount)),
            (int)Math.Round(color.G * (1 - amount)),
            (int)Math.Round(color.B * (1 - amount)));
    }
}

internal sealed class ShopeeButton : BrandButtonBase
{
    private static readonly Color Brand = Color.FromArgb(238, 77, 45);
    private static readonly Color Border = Color.FromArgb(205, 62, 35);

    public ShopeeButton()
    {
        Text = "蝦皮";
        AccessibleName = "蝦皮";
    }

    protected override Color NormalBorder => Border;

    protected override void PaintBrandSurface(Graphics g, GraphicsPath path, RectangleF bounds)
    {
        var fill = !Enabled ? CyVisualTheme.ReadOnly
            : IsPressed ? Darken(Brand, 0.14)
            : IsHovered ? Darken(Brand, 0.07)
            : Brand;
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    protected override void PaintBrandContent(Graphics g, Rectangle bounds)
    {
        var color = Enabled ? Color.White : CyVisualTheme.TextDisabled;
        TextRenderer.DrawText(g, Text, Font, bounds, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class MoStoreButton : BrandButtonBase
{
    private static readonly Color LeftBrand = Color.FromArgb(229, 0, 170);
    private static readonly Color RightBrand = Color.FromArgb(45, 62, 117);
    private static readonly Color Border = Color.FromArgb(75, 75, 75);

    public MoStoreButton()
    {
        Text = "MO店+";
        AccessibleName = "MO店+";
    }

    protected override Color NormalBorder => Border;

    protected override void PaintBrandSurface(Graphics g, GraphicsPath path, RectangleF bounds)
    {
        if (!Enabled)
        {
            using var disabled = new SolidBrush(CyVisualTheme.ReadOnly);
            g.FillPath(disabled, path);
            return;
        }

        var amount = IsPressed ? 0.14 : IsHovered ? 0.07 : 0.0;
        var left = amount > 0 ? Darken(LeftBrand, amount) : LeftBrand;
        var right = amount > 0 ? Darken(RightBrand, amount) : RightBrand;

        var state = g.Save();
        g.SetClip(path);
        using var leftBrush = new SolidBrush(left);
        using var rightBrush = new SolidBrush(right);
        var half = bounds.Left + bounds.Width / 2f;
        g.FillRectangle(leftBrush, bounds.Left, bounds.Top, bounds.Width / 2f + 1f, bounds.Height);
        g.FillRectangle(rightBrush, half, bounds.Top, bounds.Right - half, bounds.Height);
        g.Restore(state);
    }

    protected override void PaintBrandContent(Graphics g, Rectangle bounds)
    {
        var color = Enabled ? Color.White : CyVisualTheme.TextDisabled;
        TextRenderer.DrawText(g, Text, Font, bounds, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class CoupangButton : BrandButtonBase
{
    private static readonly Color[] BrandColors =
    [
        Color.FromArgb(145, 69, 18),
        Color.FromArgb(238, 126, 0),
        Color.FromArgb(112, 176, 0),
        Color.FromArgb(0, 142, 196)
    ];

    public CoupangButton()
    {
        Text = "酷澎商城";
        AccessibleName = "酷澎商城";
    }

    protected override Color NormalBorder => IsPressed
        ? CyVisualTheme.TextSecondary
        : IsHovered ? Color.FromArgb(156, 163, 175) : Color.FromArgb(188, 188, 188);

    protected override void PaintBrandSurface(Graphics g, GraphicsPath path, RectangleF bounds)
    {
        var fill = !Enabled ? CyVisualTheme.ReadOnly
            : IsPressed ? CyVisualTheme.ReadOnly
            : IsHovered ? CyVisualTheme.Window
            : Color.White;
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    protected override void PaintBrandContent(Graphics g, Rectangle bounds)
    {
        var chars = new[] { "酷", "澎", "商", "城" };
        var available = Math.Max(1, bounds.Width - 12);
        var cell = Math.Max(1, available / chars.Length);
        var left = bounds.Left + (bounds.Width - cell * chars.Length) / 2;
        for (var i = 0; i < chars.Length; i++)
        {
            var rect = new Rectangle(left + i * cell, bounds.Top, cell, bounds.Height);
            var color = Enabled ? BrandColors[i] : CyVisualTheme.TextDisabled;
            TextRenderer.DrawText(g, chars[i], Font, rect, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
