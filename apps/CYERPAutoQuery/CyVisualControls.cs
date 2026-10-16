using System.Drawing.Drawing2D;

namespace CYERPAutoQuery;

// Buttons copied from CYERPAutoInput CyVisualControls.cs (only the ones used here).
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

internal static class CyButtonGeometry
{
    /// <summary>
    /// Corner radius for every CY button: the visual guide's WinForms reference ~2 px.
    /// Secondary buttons are owner-painted with the same radius so the whole row matches
    /// (user, 2026-10-11; 6 px in Build 18 was too round).
    /// </summary>
    internal const float Radius = 2f;

    /// <summary>Paint inset: 1 px horizontal safety, ~1.5 px vertical (reference geometry).</summary>
    internal static RectangleF Bounds(Control c) => new(1f, 1.5f, Math.Max(1f, c.Width - 3f), Math.Max(1f, c.Height - 4f));
}

/// <summary>
/// Ordinary (secondary) CY button: a native WinForms Button with owner-painted appearance
/// only (CY WinForms theme-button reference) so its corners match the themed buttons;
/// click, keyboard, focus and accessibility stay native.
/// </summary>
internal class CyButton : Button
{
    private const int WmUpdateUiState = 0x0128;
    private static readonly nint HideFocusState = 0x00010001;
    private static readonly Color Hover = Color.FromArgb(243, 244, 246);
    private static readonly Color Down = Color.FromArgb(229, 231, 235);
    private bool _hovered;
    private bool _pressed;

    public CyButton()
    {
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); HideFocusCue(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); HideFocusCue(); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); HideFocusCue(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) _pressed = true; Invalidate(); base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { _pressed = false; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        var bounds = CyButtonGeometry.Bounds(this);
        using var path = CyDrawing.RoundedRectangle(bounds, CyButtonGeometry.Radius);
        var fill = !Enabled ? CyVisualTheme.ReadOnly : _pressed ? Down : _hovered ? Hover : CyVisualTheme.White;
        var border = Focused && Enabled ? CyVisualTheme.AccentFocus : CyVisualTheme.Border;
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, Focused && Enabled ? 1.5f : 1f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        PaintContent(g, Rectangle.Round(bounds), Enabled ? CyVisualTheme.TextPrimary : CyVisualTheme.TextDisabled);
    }

    protected virtual void PaintContent(Graphics g, Rectangle bounds, Color text) =>
        TextRenderer.DrawText(g, Text, Font, bounds, text,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);

    private void HideFocusCue()
    {
        if (IsHandleCreated)
            NativeMethods.SendMessage(Handle, WmUpdateUiState, HideFocusState, 0);
    }
}

/// <summary>
/// Status badge (CY Desktop Visual Guide 11.5): soft state surface, state-colored text
/// and a 1 px border on a small rounded rectangle; the text carries the meaning, not
/// the color alone.
/// </summary>
internal sealed class CyStatusBadge : Label
{
    private Color _border = CyVisualTheme.Border;

    public CyStatusBadge()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleCenter;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    public void SetState(string text, Color foreground, Color surface, Color border)
    {
        Text = text;
        ForeColor = foreground;
        BackColor = surface;
        _border = border;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? CyVisualTheme.Window);

        var bounds = new RectangleF(1f, 1.5f, Math.Max(1f, Width - 3f), Math.Max(1f, Height - 4f));
        using var path = CyDrawing.RoundedRectangle(bounds, 4f);
        using var fill = new SolidBrush(BackColor);
        using var pen = new Pen(_border, 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(bounds), ForeColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

internal sealed class CyPrimaryButton : Button
{
    private bool _hovered;
    private bool _pressed;

    internal float CornerRadius { get; set; } = CyButtonGeometry.Radius;

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

        // CY WinForms theme-button reference geometry: 1 px horizontal safety inset,
        // ~1.5 px vertical inset; corner radius from CyButtonGeometry.
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
