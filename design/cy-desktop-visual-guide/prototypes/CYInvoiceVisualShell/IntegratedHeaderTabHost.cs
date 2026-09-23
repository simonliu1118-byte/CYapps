namespace CYInvoiceVisualShell;

// Preferred CY tab treatment for higher visual consistency:
// custom header only, native TabControl / TabPage content host underneath.
// No page content is custom-painted.
internal sealed class IntegratedHeaderTabHost : UserControl
{
    private readonly FlowLayoutPanel headerBar = new();
    private readonly Panel clipPanel = new();
    private readonly TabControl pages = new();
    private readonly List<IntegratedHeaderTabItem> headerItems = new();
    private readonly TabHeaderScale scale;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal IntegratedHeaderTabHost(TabHeaderScale scale = TabHeaderScale.Standard)
    {
        this.scale = scale;
        BackColor = Color.White;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        headerBar.Dock = DockStyle.Top;
        headerBar.Height = scale == TabHeaderScale.Large ? 47 : 38;
        headerBar.WrapContents = false;
        headerBar.AutoScroll = false;
        headerBar.FlowDirection = FlowDirection.LeftToRight;
        headerBar.BackColor = Color.FromArgb(248, 249, 251);
        headerBar.Padding = Padding.Empty;
        headerBar.Margin = Padding.Empty;

        clipPanel.Dock = DockStyle.Fill;
        clipPanel.BackColor = Color.White;
        clipPanel.Margin = Padding.Empty;
        clipPanel.Padding = Padding.Empty;
        clipPanel.Resize += (_, _) => LayoutNativePages();

        // The real TabControl remains responsible for page lifetime, focus routing,
        // control parenting, and selected page state. Its native header is clipped away.
        pages.Appearance = TabAppearance.FlatButtons;
        pages.SizeMode = TabSizeMode.Fixed;
        pages.ItemSize = new Size(1, 1);
        pages.Multiline = false;
        pages.Margin = Padding.Empty;
        pages.SelectedIndexChanged += (_, _) => SyncSelection();

        clipPanel.Controls.Add(pages);
        Controls.Add(clipPanel);
        Controls.Add(headerBar);
    }

    internal int SelectedIndex
    {
        get => pages.SelectedIndex;
        set
        {
            if (value >= 0 && value < pages.TabCount)
                pages.SelectedIndex = value;
        }
    }

    internal void AddPage(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = VisualTokens.Window,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        pages.TabPages.Add(page);

        var item = new IntegratedHeaderTabItem(title, scale)
        {
            Index = pages.TabCount - 1,
            Palette = palette,
            Margin = Padding.Empty,
        };
        item.Activated += (_, _) => pages.SelectedIndex = item.Index;
        headerItems.Add(item);
        headerBar.Controls.Add(item);

        if (pages.TabCount == 1)
            pages.SelectedIndex = 0;

        SyncSelection();
        LayoutNativePages();
    }

    internal void ApplyTheme(CyTheme theme)
    {
        palette = VisualTokens.GetPalette(theme);
        foreach (var item in headerItems)
        {
            item.Palette = palette;
            item.Invalidate();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (pages.TabCount > 1 && keyData == (Keys.Control | Keys.Tab))
        {
            pages.SelectedIndex = (pages.SelectedIndex + 1) % pages.TabCount;
            return true;
        }

        if (pages.TabCount > 1 && keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            pages.SelectedIndex = (pages.SelectedIndex - 1 + pages.TabCount) % pages.TabCount;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SyncSelection()
    {
        for (var i = 0; i < headerItems.Count; i++)
        {
            headerItems[i].Selected = i == pages.SelectedIndex;
            headerItems[i].Invalidate();
        }
    }

    private void LayoutNativePages()
    {
        // Move the native 1px header just above the clip area. The page itself is
        // still the real TabPage, so existing page controls are not redrawn here.
        pages.Location = new Point(-2, -5);
        pages.Size = new Size(
            Math.Max(0, clipPanel.ClientSize.Width + 4),
            Math.Max(0, clipPanel.ClientSize.Height + 7));
    }
}

internal sealed class IntegratedHeaderTabItem : Control
{
    private bool hovered;
    private bool selected;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private readonly TabHeaderScale scale;

    internal int Index { get; set; }
    internal event EventHandler? Activated;

    internal ThemePalette Palette
    {
        get => palette;
        set => palette = value;
    }

    internal bool Selected
    {
        get => selected;
        set => selected = value;
    }

    internal IntegratedHeaderTabItem(string text, TabHeaderScale scale)
    {
        Text = text;
        this.scale = scale;
        Height = scale == TabHeaderScale.Large ? 46 : 37;
        Width = MeasurePreferredWidth();
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Focus is shown as the same soft surface used by hover rather than the
        // legacy dotted focus rectangle. This keeps keyboard focus visible.
        var softState = (hovered || Focused) && !selected;
        var back = softState ? palette.Soft : Color.FromArgb(248, 249, 251);
        using (var bg = new SolidBrush(back))
            e.Graphics.FillRectangle(bg, ClientRectangle);

        var pt = scale == TabHeaderScale.Large ? 11f : 10f;
        using var font = selected
            ? VisualTokens.Font(pt, FontStyle.Bold)
            : VisualTokens.Font(pt);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            font,
            ClientRectangle,
            selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);

        if (selected)
        {
            using var pen = new Pen(palette.Accent, 2f);
            var inset = scale == TabHeaderScale.Large ? 16 : 12;
            e.Graphics.DrawLine(pen, inset, Height - 2, Width - inset, Height - 2);
        }
    }

    private int MeasurePreferredWidth()
    {
        var pt = scale == TabHeaderScale.Large ? 11f : 10f;
        using var font = VisualTokens.Font(pt);
        var size = TextRenderer.MeasureText(
            Text,
            font,
            new Size(int.MaxValue, Height),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return size.Width + (scale == TabHeaderScale.Large ? 40 : 28);
    }
}
