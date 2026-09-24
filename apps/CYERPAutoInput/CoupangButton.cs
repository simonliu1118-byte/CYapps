using System.Drawing.Drawing2D;

namespace CYERPAutoInput;

internal sealed class CoupangButton : Button
{
    private static readonly Color[] BrandColors =
    [
        Color.FromArgb(145, 69, 18),
        Color.FromArgb(238, 126, 0),
        Color.FromArgb(112, 176, 0),
        Color.FromArgb(0, 142, 196)
    ];

    private bool _hovered;
    private bool _pressed;

    public CoupangButton()
    {
        Text = string.Empty;
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
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        var bounds = new RectangleF(1f, 1.5f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 4f));
        using var path = CyDrawing.RoundedRectangle(bounds, 2f);
        var fill = _pressed ? CyVisualTheme.ReadOnly : _hovered ? CyVisualTheme.Window : Color.White;
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Focused ? CyVisualTheme.AccentFocus : CyVisualTheme.Border, Focused ? 1.5f : 1f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);

        var chars = new[] { "酷", "澎", "商", "城" };
        var inner = Rectangle.Round(bounds);
        var available = Math.Max(1, inner.Width - 12);
        var cell = Math.Max(1, available / chars.Length);
        var left = inner.Left + (inner.Width - cell * chars.Length) / 2;
        for (var i = 0; i < chars.Length; i++)
        {
            var rect = new Rectangle(left + i * cell, inner.Top, cell, inner.Height);
            TextRenderer.DrawText(g, chars[i], Font, rect, BrandColors[i],
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
