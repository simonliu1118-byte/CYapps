using System.Drawing.Drawing2D;

namespace CYInvoiceVisualShell;

internal sealed class CyButton : Button
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);
    private bool hovering;
    private bool pressing;

    internal CyButtonRole Role { get; set; } = CyButtonRole.Secondary;
    internal bool Large { get; set; }

    internal CyButton(string text, CyButtonRole role = CyButtonRole.Secondary, bool large = false)
    {
        Text = text;
        Role = role;
        Large = large;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(4, 0, 4, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    internal void ApplyVisual(ThemePalette value, DensityMetrics density)
    {
        palette = value;
        metrics = density;
        Font = VisualTokens.Font(Large ? Math.Max(metrics.ButtonPt, 10.5f) : metrics.ButtonPt,
            Large || Role == CyButtonRole.Primary ? FontStyle.Bold : FontStyle.Regular);
        Height = Large ? metrics.LargeButtonHeight : metrics.ButtonHeight;
        RecalculateWidth();
        Invalidate();
    }

    internal void RecalculateWidth()
    {
        var horizontalPadding = Large ? 20 : 16;
        var measured = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var min = Large ? 96 : 72;
        Width = Math.Clamp(measured + horizontalPadding * 2, min, 190);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (IsHandleCreated) RecalculateWidth();
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; pressing = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { pressing = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { pressing = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Large ? 6 : 4;
        using var path = RoundedRect(rect, radius);

        var (fill, border, text) = ResolveColors();
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border);
        pevent.Graphics.FillPath(fillBrush, path);
        pevent.Graphics.DrawPath(borderPen, path);

        TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Focused && Enabled)
        {
            var focusRect = Rectangle.Inflate(rect, -3, -3);
            using var focusPath = RoundedRect(focusRect, Math.Max(2, radius - 2));
            using var focusPen = new Pen(palette.Focus) { DashStyle = DashStyle.Dot };
            pevent.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private (Color Fill, Color Border, Color Text) ResolveColors()
    {
        if (!Enabled)
            return (VisualTokens.ReadOnly, VisualTokens.Border, VisualTokens.TextDisabled);

        if (Role == CyButtonRole.Danger)
        {
            if (pressing) return (VisualTokens.Danger, VisualTokens.Danger, Color.White);
            if (hovering) return (VisualTokens.DangerSoft, VisualTokens.DangerHover, VisualTokens.DangerHover);
            return (Color.White, VisualTokens.Danger, VisualTokens.Danger);
        }

        if (Role == CyButtonRole.Primary)
        {
            if (pressing) return (palette.Pressed, palette.Pressed, Color.White);
            if (hovering) return (palette.Hover, palette.Hover, Color.White);
            return (palette.Accent, palette.Accent, Color.White);
        }

        return hovering
            ? (VisualTokens.Subtle, palette.Focus, VisualTokens.TextPrimary)
            : (Color.White, VisualTokens.Border, VisualTokens.TextPrimary);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CyTextBox : UserControl
{
    private readonly TextBox editor = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private bool readOnly;

    internal CyTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = Color.White;
        editor.ForeColor = VisualTokens.TextPrimary;
        editor.Location = new Point(10, 7);
        editor.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        editor.Enter += (_, _) => Invalidate();
        editor.Leave += (_, _) => Invalidate();
        editor.TextChanged += (_, _) => base.OnTextChanged(EventArgs.Empty);
        Controls.Add(editor);
        Margin = new Padding(0, 2, 0, 2);
        MinimumSize = new Size(80, 28);
    }

    public override string Text
    {
        get => editor.Text;
        set => editor.Text = value;
    }

    internal bool ReadOnly
    {
        get => readOnly;
        set
        {
            readOnly = value;
            editor.ReadOnly = value;
            ApplyColors();
            Invalidate();
        }
    }

    internal TextBox Editor => editor;

    internal void ApplyVisual(ThemePalette value, DensityMetrics density)
    {
        palette = value;
        Font = VisualTokens.Font(density.BodyPt);
        editor.Font = Font;
        Height = density.InputHeight;
        ApplyColors();
        LayoutEditor();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        editor.Enabled = Enabled;
        ApplyColors();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 3);
        var border = editor.Focused && Enabled && !ReadOnly ? palette.Focus : VisualTokens.Border;
        using var pen = new Pen(border);
        e.Graphics.DrawPath(pen, path);
    }

    private void ApplyColors()
    {
        var disabled = !Enabled || ReadOnly;
        BackColor = disabled ? VisualTokens.ReadOnly : Color.White;
        editor.BackColor = BackColor;
        editor.ForeColor = !Enabled ? VisualTokens.TextDisabled : (ReadOnly ? VisualTokens.TextSecondary : VisualTokens.TextPrimary);
    }

    private void LayoutEditor()
    {
        if (Height <= 0) return;
        var preferred = Math.Max(editor.PreferredHeight, 18);
        editor.SetBounds(10, Math.Max(3, (Height - preferred) / 2), Math.Max(10, Width - 20), preferred);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class CyBanner : Panel
{
    private readonly Label label = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal CyBanner(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        label.Text = text;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Dock = DockStyle.Fill;
        label.Padding = new Padding(12, 0, 12, 0);
        Controls.Add(label);
        Height = 40;
        Margin = new Padding(0, 0, 0, 12);
    }

    internal string Message { get => label.Text; set => label.Text = value; }

    internal void ApplyVisual(ThemePalette value, DensityMetrics metrics)
    {
        palette = value;
        BackColor = palette.Soft;
        label.BackColor = palette.Soft;
        label.ForeColor = VisualTokens.TextPrimary;
        label.Font = VisualTokens.Font(metrics.BodyPt);
        Height = metrics.InputHeight + 6;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(120, palette.Focus));
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}

internal sealed class CyTabControl : TabControl
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);

    internal CyTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(180, metrics.TabHeight);
        Padding = new Point(14, 6);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void ApplyVisual(ThemePalette value, DensityMetrics density)
    {
        palette = value;
        metrics = density;
        Font = VisualTokens.Font(metrics.BodyPt, FontStyle.Regular);
        ItemSize = new Size(180, metrics.TabHeight);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var rect = GetTabRect(e.Index);
        var selected = e.Index == SelectedIndex;
        using var bg = new SolidBrush(selected ? Color.White : VisualTokens.Window);
        e.Graphics.FillRectangle(bg, rect);
        var textColor = selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary;
        var font = selected ? VisualTokens.Font(metrics.BodyPt, FontStyle.Bold) : Font;
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (selected)
        {
            using var pen = new Pen(palette.Accent, 2f);
            e.Graphics.DrawLine(pen, rect.Left + 10, rect.Bottom - 2, rect.Right - 10, rect.Bottom - 2);
        }
        if (!ReferenceEquals(font, Font)) font.Dispose();
    }
}

internal sealed class CyGrid : DataGridView
{
    internal CyGrid()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackgroundColor = Color.White;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoGenerateColumns = false;
        EnableHeadersVisualStyles = false;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        CellBorderStyle = DataGridViewCellBorderStyle.Single;
        ScrollBars = ScrollBars.Both;
    }

    internal void ApplyVisual(ThemePalette palette, DensityMetrics metrics)
    {
        Font = VisualTokens.Font(metrics.BodyPt);
        BackgroundColor = Color.White;
        GridColor = VisualTokens.Grid;
        ColumnHeadersDefaultCellStyle.BackColor = VisualTokens.Window;
        ColumnHeadersDefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        ColumnHeadersDefaultCellStyle.Font = VisualTokens.Font(Math.Max(9f, metrics.BodyPt), FontStyle.Bold);
        ColumnHeadersDefaultCellStyle.SelectionBackColor = VisualTokens.Window;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = metrics.RowHeight + 2;
        DefaultCellStyle.BackColor = Color.White;
        DefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        DefaultCellStyle.SelectionBackColor = palette.Selection;
        DefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        RowTemplate.Height = metrics.RowHeight;
        foreach (DataGridViewRow row in Rows) row.Height = metrics.RowHeight;
        AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);
    }
}
