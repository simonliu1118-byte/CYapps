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

internal static class CyButtonGeometry
{
    /// <summary>
    /// Corner radius for every CY button. The visual guide's WinForms reference is ~2 px,
    /// which reads as square on the user's Windows; the user asked for visibly rounded
    /// buttons (2026-10-11), matching CYInvoice's 6–7 px.
    /// </summary>
    internal const float Radius = 6f;

    /// <summary>Paint inset: 1 px horizontal safety, ~1.5 px vertical (reference geometry).</summary>
    internal static RectangleF Bounds(Control c) => new(1f, 1.5f, Math.Max(1f, c.Width - 3f), Math.Max(1f, c.Height - 4f));
}

/// <summary>
/// Ordinary (secondary) CY button: a native WinForms Button with owner-painted appearance
/// only (CY WinForms theme-button reference) so its rounded corners match the themed
/// buttons; click, keyboard, focus and accessibility stay native.
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

/// <summary>Small button beside a date field that opens the calendar; draws a calendar glyph.</summary>
internal sealed class CalendarButton : CyButton
{
    public CalendarButton()
    {
        Size = new Size(26, 24);
        TabStop = false;
        AccessibleName = "選擇日期";
    }

    protected override void PaintContent(Graphics g, Rectangle bounds, Color text) => DrawGlyph(g, bounds, text);

    internal static void DrawGlyph(Graphics g, Rectangle bounds, Color text)
    {
        var w = 12; var h = 11;
        var x = bounds.Left + (bounds.Width - w) / 2;
        var y = bounds.Top + (bounds.Height - h) / 2 + 1;
        using var pen = new Pen(text, 1f);
        g.SmoothingMode = SmoothingMode.None;
        g.DrawRectangle(pen, x, y, w, h);
        g.DrawLine(pen, x, y + 3, x + w, y + 3);
        g.DrawLine(pen, x + 3, y - 2, x + 3, y + 1);
        g.DrawLine(pen, x + w - 3, y - 2, x + w - 3, y + 1);
        using var dot = new SolidBrush(text);
        for (var r = 0; r < 2; r++)
            for (var c = 0; c < 3; c++)
                g.FillRectangle(dot, x + 2 + c * 3, y + 5 + r * 3, 2, 2);
    }
}

/// <summary>Calendar popup (native MonthCalendar) under an anchor control.</summary>
internal static class DatePickerPopup
{
    public static void Show(Control anchor, Rectangle anchorBounds, string current, Action<string> picked)
    {
        var initial = InputRules.TryNormalizeValidDate(current, out var digits)
            ? DateTime.ParseExact(digits, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.Today;
        var calendar = new MonthCalendar { MaxSelectionCount = 1, SelectionStart = initial, SelectionEnd = initial, ShowToday = true };
        var host = new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = calendar.Size };
        var drop = new ToolStripDropDown { Padding = Padding.Empty };
        drop.Items.Add(host);
        calendar.DateSelected += (_, e) =>
        {
            picked(e.Start.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
            drop.Close();
        };
        drop.Closed += (_, _) => drop.BeginInvoke(drop.Dispose);
        drop.Show(anchor, new Point(anchorBounds.Left, anchorBounds.Bottom));
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
