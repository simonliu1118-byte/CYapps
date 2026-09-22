using System.Drawing.Drawing2D;

namespace CYInvoiceVisualShell;

internal interface ICyVisualV2
{
    void ApplyVisual(CyTheme theme, ThemePalette palette, DensityMetrics metrics);
}

internal static class V2Paint
{
    internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
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

    internal static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }
}

internal sealed class V2Button : Button, ICyVisualV2
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);
    private CyTheme theme = CyTheme.Blue;
    private bool hovering;
    private bool pressing;

    internal CyButtonRole Role { get; set; } = CyButtonRole.Secondary;
    internal bool Large { get; set; }
    internal bool SolidDanger { get; set; }
    internal int FixedWidth { get; set; }

    internal V2Button(string text, CyButtonRole role = CyButtonRole.Secondary, bool large = false)
    {
        Text = text;
        Role = role;
        Large = large;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        Margin = new Padding(4, 0, 4, 0);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public void ApplyVisual(CyTheme newTheme, ThemePalette newPalette, DensityMetrics density)
    {
        theme = newTheme;
        palette = newPalette;
        metrics = density;
        Font = VisualTokens.Font(Large ? Math.Max(metrics.ButtonPt, 10.5f) : metrics.ButtonPt,
            Large ? FontStyle.Bold : (Role == CyButtonRole.Primary ? FontStyle.Bold : FontStyle.Regular));
        Height = Large ? metrics.LargeButtonHeight : metrics.ButtonHeight;
        RecalculateWidth();
        Invalidate();
    }

    internal void RecalculateWidth()
    {
        if (FixedWidth > 0)
        {
            Width = FixedWidth;
            return;
        }

        var horizontalPadding = Large ? 20 : 16;
        var measured = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        Width = Math.Clamp(measured + horizontalPadding * 2, Large ? 100 : 76, 190);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var radius = Large ? 9 : 7;
        using var path = V2Paint.RoundedRect(rect, radius);
        var (fill, border, text) = ResolveColors();
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border, 1f);
        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        TextRenderer.DrawText(e.Graphics, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Focused && Enabled)
        {
            var focusRect = Rectangle.Inflate(rect, -3, -3);
            using var focusPath = V2Paint.RoundedRect(focusRect, Math.Max(3, radius - 3));
            using var focusPen = new Pen(theme == CyTheme.Coral ? palette.Accent : palette.Focus, 1f)
            {
                DashStyle = DashStyle.Dot,
            };
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private (Color Fill, Color Border, Color Text) ResolveColors()
    {
        if (!Enabled)
            return (VisualTokens.ReadOnly, VisualTokens.Border, VisualTokens.TextDisabled);

        if (Role == CyButtonRole.Danger)
        {
            if (SolidDanger)
            {
                var fill = pressing ? Color.FromArgb(143, 38, 38) : hovering ? VisualTokens.DangerHover : VisualTokens.Danger;
                return (fill, fill, Color.White);
            }

            if (pressing) return (VisualTokens.DangerSoft, VisualTokens.DangerHover, VisualTokens.DangerHover);
            if (hovering) return (VisualTokens.DangerSoft, VisualTokens.Danger, VisualTokens.Danger);
            return (Color.White, VisualTokens.Danger, VisualTokens.Danger);
        }

        if (Role == CyButtonRole.Primary)
        {
            // Coral intentionally uses a soft pink primary surface so it cannot be mistaken for Danger red.
            if (theme == CyTheme.Coral)
            {
                var fill = pressing
                    ? V2Paint.Blend(palette.Soft, palette.Accent, 0.22f)
                    : hovering
                        ? V2Paint.Blend(palette.Soft, palette.Accent, 0.12f)
                        : palette.Soft;
                return (fill, palette.Accent, palette.Pressed);
            }

            var solid = pressing ? palette.Pressed : hovering ? palette.Hover : palette.Accent;
            return (solid, solid, Color.White);
        }

        if (pressing)
            return (V2Paint.Blend(VisualTokens.Subtle, palette.Soft, 0.75f), palette.Focus, VisualTokens.TextPrimary);
        if (hovering)
            return (palette.Soft, palette.Focus, VisualTokens.TextPrimary);
        return (Color.White, VisualTokens.Border, VisualTokens.TextPrimary);
    }
}

internal sealed class V2TextBox : UserControl, ICyVisualV2
{
    private readonly TextBox editor = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private bool readOnly;

    internal V2TextBox(string text = "")
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        editor.BorderStyle = BorderStyle.None;
        editor.Text = text;
        editor.BackColor = Color.White;
        editor.ForeColor = VisualTokens.TextPrimary;
        editor.Enter += (_, _) => Invalidate();
        editor.Leave += (_, _) => Invalidate();
        editor.TextChanged += (_, _) => base.OnTextChanged(EventArgs.Empty);
        Controls.Add(editor);
        Margin = Padding.Empty;
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

    public void ApplyVisual(CyTheme theme, ThemePalette value, DensityMetrics density)
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
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, 6);
        var fill = (!Enabled || ReadOnly) ? VisualTokens.ReadOnly : Color.White;
        using var fillBrush = new SolidBrush(fill);
        e.Graphics.FillPath(fillBrush, path);
        var border = editor.Focused && Enabled && !ReadOnly ? palette.Focus : VisualTokens.Border;
        using var pen = new Pen(border, 1f);
        e.Graphics.DrawPath(pen, path);
    }

    private void ApplyColors()
    {
        var disabled = !Enabled || ReadOnly;
        var fill = disabled ? VisualTokens.ReadOnly : Color.White;
        editor.BackColor = fill;
        editor.ForeColor = !Enabled ? VisualTokens.TextDisabled : (ReadOnly ? VisualTokens.TextSecondary : VisualTokens.TextPrimary);
    }

    private void LayoutEditor()
    {
        if (Height <= 0 || Width <= 0) return;
        var preferred = Math.Max(editor.PreferredHeight, 18);
        editor.SetBounds(10, Math.Max(3, (Height - preferred) / 2), Math.Max(10, Width - 20), preferred);
    }
}

internal sealed class V2ComboBox : UserControl, ICyVisualV2
{
    private readonly ComboBox editor = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal V2ComboBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        editor.DropDownStyle = ComboBoxStyle.DropDownList;
        editor.FlatStyle = FlatStyle.Flat;
        editor.BackColor = Color.White;
        editor.ForeColor = VisualTokens.TextPrimary;
        editor.Enter += (_, _) => Invalidate();
        editor.Leave += (_, _) => Invalidate();
        editor.SelectedIndexChanged += (_, _) => SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        Controls.Add(editor);
        Margin = Padding.Empty;
        MinimumSize = new Size(90, 28);
    }

    internal ComboBox.ObjectCollection Items => editor.Items;
    internal int SelectedIndex { get => editor.SelectedIndex; set => editor.SelectedIndex = value; }
    internal object? SelectedItem => editor.SelectedItem;
    internal event EventHandler? SelectedIndexChanged;

    public void ApplyVisual(CyTheme theme, ThemePalette value, DensityMetrics density)
    {
        palette = value;
        Font = VisualTokens.Font(density.BodyPt);
        editor.Font = Font;
        Height = density.InputHeight;
        LayoutEditor();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, 6);
        using var fill = new SolidBrush(Color.White);
        using var pen = new Pen(editor.Focused ? palette.Focus : VisualTokens.Border, 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    private void LayoutEditor()
    {
        if (Height <= 0 || Width <= 0) return;
        var preferred = Math.Max(editor.PreferredHeight, 24);
        editor.SetBounds(6, Math.Max(2, (Height - preferred) / 2), Math.Max(20, Width - 12), preferred);
    }
}

internal sealed class V2DateField : UserControl, ICyVisualV2
{
    private readonly Label textLabel = new();
    private readonly Button calendarButton = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DateTime value = DateTime.Today;

    internal V2DateField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        textLabel.TextAlign = ContentAlignment.MiddleLeft;
        textLabel.BackColor = Color.Transparent;
        textLabel.ForeColor = VisualTokens.TextPrimary;
        textLabel.Padding = new Padding(9, 0, 0, 0);
        textLabel.Cursor = Cursors.Hand;
        calendarButton.FlatStyle = FlatStyle.Flat;
        calendarButton.FlatAppearance.BorderSize = 0;
        calendarButton.Text = "▾";
        calendarButton.BackColor = Color.Transparent;
        calendarButton.Cursor = Cursors.Hand;
        calendarButton.TabStop = false;
        textLabel.Click += (_, _) => ShowCalendar();
        calendarButton.Click += (_, _) => ShowCalendar();
        Controls.Add(textLabel);
        Controls.Add(calendarButton);
        Margin = Padding.Empty;
        RefreshText();
    }

    internal DateTime Value
    {
        get => value;
        set { this.value = value.Date; RefreshText(); }
    }

    public void ApplyVisual(CyTheme theme, ThemePalette valuePalette, DensityMetrics density)
    {
        palette = valuePalette;
        Font = VisualTokens.Font(density.BodyPt);
        textLabel.Font = Font;
        calendarButton.Font = VisualTokens.Font(Math.Max(8.5f, density.SecondaryPt));
        calendarButton.ForeColor = VisualTokens.TextSecondary;
        Height = density.InputHeight;
        LayoutChildren();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, 6);
        using var fill = new SolidBrush(Color.White);
        using var pen = new Pen(ContainsFocus ? palette.Focus : VisualTokens.Border, 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    private void LayoutChildren()
    {
        var buttonWidth = Math.Max(28, Height - 2);
        textLabel.SetBounds(1, 1, Math.Max(10, Width - buttonWidth - 2), Math.Max(1, Height - 2));
        calendarButton.SetBounds(Math.Max(1, Width - buttonWidth - 1), 1, buttonWidth, Math.Max(1, Height - 2));
    }

    private void RefreshText() => textLabel.Text = value.ToString("yyyy/M/d");

    private void ShowCalendar()
    {
        using var popup = new Form
        {
            FormBorderStyle = FormBorderStyle.FixedSingle,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
        };
        var calendar = new MonthCalendar
        {
            MaxSelectionCount = 1,
            SelectionStart = value,
            SelectionEnd = value,
            ShowTodayCircle = true,
        };
        calendar.DateSelected += (_, e) =>
        {
            Value = e.Start;
            popup.DialogResult = DialogResult.OK;
            popup.Close();
        };
        popup.Controls.Add(calendar);
        var screen = PointToScreen(new Point(0, Height + 2));
        popup.Location = screen;
        popup.Deactivate += (_, _) => popup.Close();
        popup.ShowDialog(FindForm());
    }
}

internal sealed class V2Banner : UserControl, ICyVisualV2
{
    private readonly Label label = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal V2Banner(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        label.Text = text;
        label.AutoEllipsis = true;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.BackColor = Color.Transparent;
        label.Padding = new Padding(12, 0, 12, 0);
        label.Dock = DockStyle.Fill;
        Controls.Add(label);
        Margin = new Padding(0, 0, 0, 12);
    }

    internal string Message { get => label.Text; set => label.Text = value; }

    public void ApplyVisual(CyTheme theme, ThemePalette value, DensityMetrics metrics)
    {
        palette = value;
        label.ForeColor = VisualTokens.TextPrimary;
        label.Font = VisualTokens.Font(metrics.BodyPt);
        Height = metrics.InputHeight + 6;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, 7);
        using var fill = new SolidBrush(palette.Soft);
        using var border = new Pen(Color.FromArgb(145, palette.Focus), 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class V2Badge : UserControl, ICyVisualV2
{
    private readonly string caption;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private Font badgeFont = VisualTokens.Font(9f, FontStyle.Bold);

    internal V2Badge(string text)
    {
        caption = text;
        Height = 28;
        Width = 96;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public void ApplyVisual(CyTheme theme, ThemePalette value, DensityMetrics metrics)
    {
        palette = value;
        badgeFont.Dispose();
        badgeFont = VisualTokens.Font(metrics.SecondaryPt, FontStyle.Bold);
        var measured = TextRenderer.MeasureText(caption, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
        Width = measured.Width + 24;
        Height = Math.Max(26, metrics.InputHeight - 4);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) badgeFont.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, Math.Max(8, Height / 2));
        using var fill = new SolidBrush(palette.Soft);
        using var pen = new Pen(Color.FromArgb(110, palette.Focus), 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, caption, badgeFont, rect, palette.Pressed,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal sealed class V2SurfaceFrame : Panel, ICyVisualV2
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal V2SurfaceFrame()
    {
        BackColor = Color.White;
        Padding = new Padding(1);
        Margin = Padding.Empty;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public void ApplyVisual(CyTheme theme, ThemePalette value, DensityMetrics metrics)
    {
        palette = value;
        BackColor = Color.White;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = V2Paint.RoundedRect(rect, 6);
        using var pen = new Pen(VisualTokens.Border, 1f);
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class V2Grid : DataGridView, ICyVisualV2
{
    internal V2Grid()
    {
        BorderStyle = BorderStyle.None;
        BackgroundColor = Color.White;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoGenerateColumns = false;
        EnableHeadersVisualStyles = false;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ScrollBars = ScrollBars.Both;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    public void ApplyVisual(CyTheme theme, ThemePalette palette, DensityMetrics metrics)
    {
        Font = VisualTokens.Font(metrics.BodyPt);
        BackgroundColor = Color.White;
        GridColor = VisualTokens.Grid;
        ColumnHeadersDefaultCellStyle.BackColor = VisualTokens.Subtle;
        ColumnHeadersDefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        ColumnHeadersDefaultCellStyle.Font = VisualTokens.Font(Math.Max(9f, metrics.BodyPt), FontStyle.Bold);
        ColumnHeadersDefaultCellStyle.SelectionBackColor = VisualTokens.Subtle;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = metrics.RowHeight + 4;
        DefaultCellStyle.BackColor = Color.White;
        DefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        DefaultCellStyle.SelectionBackColor = palette.Selection;
        DefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        RowTemplate.Height = metrics.RowHeight;
        foreach (DataGridViewRow row in Rows) row.Height = metrics.RowHeight;
        AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 253);
    }
}

internal sealed class V2TabHost : UserControl, ICyVisualV2
{
    private sealed class PageInfo
    {
        internal required string Title { get; init; }
        internal required Control Page { get; init; }
        internal required HeaderControl Header { get; init; }
    }

    private sealed class HeaderControl : Control
    {
        internal bool Selected { get; set; }
        internal ThemePalette Palette { get; set; } = VisualTokens.GetPalette(CyTheme.Blue);
        internal DensityMetrics Metrics { get; set; } = VisualTokens.GetDensity(CyDensity.Standard);

        internal HeaderControl(string text)
        {
            Text = text;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var bg = new SolidBrush(Color.White);
            e.Graphics.FillRectangle(bg, ClientRectangle);
            var font = VisualTokens.Font(Metrics.BodyPt, Selected ? FontStyle.Bold : FontStyle.Regular);
            var textColor = Selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary;
            TextRenderer.DrawText(e.Graphics, Text, font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            font.Dispose();
            if (Selected)
            {
                using var pen = new Pen(Palette.Accent, 2f);
                e.Graphics.DrawLine(pen, 12, Height - 2, Width - 12, Height - 2);
            }
        }
    }

    private readonly Panel headerBar = new();
    private readonly FlowLayoutPanel headers = new();
    private readonly Panel divider = new();
    private readonly Panel contentHost = new();
    private readonly List<PageInfo> pages = new();
    private int selectedIndex;

    internal V2TabHost()
    {
        BackColor = VisualTokens.Window;
        headerBar.Dock = DockStyle.Top;
        headerBar.BackColor = Color.White;
        headers.Dock = DockStyle.Left;
        headers.AutoSize = true;
        headers.WrapContents = false;
        headers.FlowDirection = FlowDirection.LeftToRight;
        headers.Margin = Padding.Empty;
        headers.Padding = Padding.Empty;
        divider.Dock = DockStyle.Bottom;
        divider.Height = 1;
        divider.BackColor = VisualTokens.Divider;
        headerBar.Controls.Add(headers);
        headerBar.Controls.Add(divider);
        contentHost.Dock = DockStyle.Fill;
        contentHost.BackColor = VisualTokens.Window;
        Controls.Add(contentHost);
        Controls.Add(headerBar);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
    }

    internal void AddPage(string title, Control page)
    {
        var header = new HeaderControl(title);
        var index = pages.Count;
        header.Click += (_, _) => SelectPage(index);
        page.Dock = DockStyle.Fill;
        page.Visible = index == selectedIndex;
        contentHost.Controls.Add(page);
        headers.Controls.Add(header);
        pages.Add(new PageInfo { Title = title, Page = page, Header = header });
        if (pages.Count == 1) SelectPage(0);
    }

    internal int SelectedIndex
    {
        get => selectedIndex;
        set => SelectPage(value);
    }

    internal event EventHandler? SelectedIndexChanged;

    public void ApplyVisual(CyTheme theme, ThemePalette palette, DensityMetrics metrics)
    {
        headerBar.Height = metrics.TabHeight + 1;
        headerBar.BackColor = Color.White;
        contentHost.BackColor = VisualTokens.Window;
        divider.BackColor = VisualTokens.Divider;
        foreach (var page in pages)
        {
            page.Header.Palette = palette;
            page.Header.Metrics = metrics;
            page.Header.Height = metrics.TabHeight;
            var measured = TextRenderer.MeasureText(page.Title, VisualTokens.Font(metrics.BodyPt, FontStyle.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
            page.Header.Width = Math.Clamp(measured + 48, 120, 210);
            page.Header.Selected = ReferenceEquals(page, pages.ElementAtOrDefault(selectedIndex));
            page.Header.Invalidate();
            page.Page.BackColor = VisualTokens.Window;
        }
        Invalidate();
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        selectedIndex = index;
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].Page.Visible = i == index;
            pages[i].Header.Selected = i == index;
            pages[i].Header.Invalidate();
        }
        pages[index].Page.BringToFront();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }
}
