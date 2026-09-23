namespace CYInvoiceVisualShell;

internal sealed class TabComparisonFormV5 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private readonly ComboBox themeCombo = new();
    private readonly Label dpiLabel = new();

    private readonly MinimalNativeTabControl nativeStandard = new();
    private readonly MinimalNativeTabControl nativeLarge = new();
    private readonly HeaderOnlyTabHost customStandard = new(TabHeaderScale.Standard);
    private readonly HeaderOnlyTabHost customLarge = new(TabHeaderScale.Large);

    internal TabComparisonFormV5()
    {
        Text = "CY Tab Lab V5 — Native-Minimal vs Header-Only Custom";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 860);
        MinimumSize = new Size(1040, 760);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);

        BuildLayout();
        ApplyTheme();
        Shown += (_, _) => RefreshDpiLabel();
        DpiChanged += (_, _) => RefreshDpiLabel();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Tab Lab — 原生頁面保留，只比較 Header 外觀",
            AutoSize = true,
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Anchor = AnchorStyles.Left,
        };

        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.Width = 120;
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        themeCombo.SelectedIndex = 0;
        themeCombo.Margin = new Padding(12, 0, 12, 0);
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (themeCombo.SelectedIndex < 0) return;
            currentTheme = (CyTheme)themeCombo.SelectedIndex;
            ApplyTheme();
        };

        dpiLabel.AutoSize = true;
        dpiLabel.ForeColor = VisualTokens.TextSecondary;
        dpiLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(themeCombo, 1, 0);
        header.Controls.Add(dpiLabel, 2, 0);

        var note = new Label
        {
            Text = "A = Native TabControl / TabPage + 最小 Header override：只覆蓋原生 Selected bevel / shadow，不重畫內容頁。B = Header-only Custom：自訂 Header bar，但下方仍由原生 TabControl / TabPage 管理；沒有整個頁面自繪。請快速切換頁籤觀察是否仍有跳動、閃灰或邊框抽動。",
            AutoSize = true,
            MaximumSize = new Size(1170, 0),
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 14),
        };

        var compare = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        compare.Controls.Add(BuildNativeColumn(), 0, 0);
        compare.Controls.Add(BuildCustomColumn(), 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(compare, 0, 2);
        Controls.Add(root);
    }

    private Control BuildNativeColumn()
    {
        var panel = NewColumnPanel(new Padding(0, 0, 10, 0));
        panel.Controls.Add(ColumnTitle("A. Native-Minimal — 原生 TabControl + 去凸框 / 去陰影 Header"), 0, 0);
        panel.Controls.Add(ColumnNote("頁面與切換機制仍是原生；只在 Tab header 區做最小 owner-draw，覆蓋原生 selected bevel。這是『最接近原生』的無陰影版本。"), 0, 1);

        nativeStandard.ConfigureStandard();
        BuildNativeTabs(nativeStandard, new[] { "開立發票", "已開立發票清單", "待處理", "設定" });
        panel.Controls.Add(SectionLabel("Standard"), 0, 2);
        panel.Controls.Add(Wrap(nativeStandard), 0, 3);

        nativeLarge.ConfigureLarge();
        BuildNativeTabs(nativeLarge, new[] { "發票作業", "開立紀錄", "同步處理", "系統設定" });
        panel.Controls.Add(SectionLabel("Large"), 0, 4);
        panel.Controls.Add(Wrap(nativeLarge), 0, 5);
        return panel;
    }

    private Control BuildCustomColumn()
    {
        var panel = NewColumnPanel(new Padding(10, 0, 0, 0));
        panel.Controls.Add(ColumnTitle("B. Header-only Custom — 只換 Header，不重畫 TabPage"), 0, 0);
        panel.Controls.Add(ColumnNote("自訂的只有上方 Header bar；真正內容仍放在原生 TabPage 中。這條路可完全避開原生 selected-tab 陰影，又不需要把整個頁面改成自繪。"), 0, 1);

        customStandard.SetTabs(new[] { "開立發票", "已開立發票清單", "待處理", "設定" });
        panel.Controls.Add(SectionLabel("Standard"), 0, 2);
        panel.Controls.Add(Wrap(customStandard), 0, 3);

        customLarge.SetTabs(new[] { "發票作業", "開立紀錄", "同步處理", "系統設定" });
        panel.Controls.Add(SectionLabel("Large"), 0, 4);
        panel.Controls.Add(Wrap(customLarge), 0, 5);
        return panel;
    }

    private static TableLayoutPanel NewColumnPanel(Padding margin) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 6,
        Margin = margin,
        BackColor = VisualTokens.Window,
        RowStyles =
        {
            new RowStyle(SizeType.AutoSize),
            new RowStyle(SizeType.AutoSize),
            new RowStyle(SizeType.AutoSize),
            new RowStyle(SizeType.Percent, 50),
            new RowStyle(SizeType.AutoSize),
            new RowStyle(SizeType.Percent, 50),
        },
    };

    private static Label ColumnTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = VisualTokens.Font(11.5f, FontStyle.Bold),
        ForeColor = VisualTokens.TextPrimary,
        Margin = new Padding(0, 0, 0, 5),
    };

    private static Label ColumnNote(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        Font = VisualTokens.Font(9f),
        ForeColor = VisualTokens.TextSecondary,
        Margin = new Padding(0, 0, 0, 12),
    };

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = VisualTokens.Font(10f, FontStyle.Bold),
        ForeColor = VisualTokens.TextPrimary,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Panel Wrap(Control control)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 14),
            Padding = Padding.Empty,
        };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control);
        return panel;
    }

    private static void BuildNativeTabs(MinimalNativeTabControl tabs, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var page = new TabPage(name) { BackColor = Color.White, Padding = new Padding(16) };
            page.Controls.Add(new Label
            {
                Text = $"{name}\r\n\r\n這裡仍是原生 TabPage。Header 外觀調整不會把內容頁改成自繪。",
                AutoSize = true,
                Font = VisualTokens.Font(10f),
                ForeColor = VisualTokens.TextPrimary,
            });
            tabs.TabPages.Add(page);
        }
    }

    private void ApplyTheme()
    {
        nativeStandard.ApplyTheme(currentTheme);
        nativeLarge.ApplyTheme(currentTheme);
        customStandard.ApplyTheme(currentTheme);
        customLarge.ApplyTheme(currentTheme);
    }

    private void RefreshDpiLabel()
    {
        var dpi = DeviceDpi;
        dpiLabel.Text = $"Windows DPI：{dpi} ({Math.Round(dpi / 96d * 100):0}%)";
    }
}

internal sealed class MinimalNativeTabControl : TabControl
{
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private float fontPt = 10f;
    private int underlineInset = 10;

    internal MinimalNativeTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Appearance = TabAppearance.Normal;
        SizeMode = TabSizeMode.Normal;
        Multiline = false;
        BackColor = Color.White;
        DoubleBuffered = true;
    }

    internal void ConfigureStandard()
    {
        fontPt = 10f;
        underlineInset = 10;
        Padding = new Point(14, 5);
        Font = VisualTokens.Font(fontPt);
    }

    internal void ConfigureLarge()
    {
        fontPt = 11f;
        underlineInset = 14;
        Padding = new Point(20, 9);
        Font = VisualTokens.Font(fontPt);
    }

    internal void ApplyTheme(CyTheme theme)
    {
        palette = VisualTokens.GetPalette(theme);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;

        var rect = GetTabRect(e.Index);
        var selected = e.Index == SelectedIndex;

        // Cover the native selected-tab bevel/shadow with a flat surface.
        // Only the header rectangle is repainted; TabPage remains native.
        var cover = rect;
        cover.Inflate(1, 1);
        using (var bg = new SolidBrush(Color.White))
            e.Graphics.FillRectangle(bg, cover);

        using var font = selected
            ? VisualTokens.Font(fontPt, FontStyle.Bold)
            : VisualTokens.Font(fontPt);

        TextRenderer.DrawText(
            e.Graphics,
            TabPages[e.Index].Text,
            font,
            rect,
            selected ? VisualTokens.TextPrimary : VisualTokens.TextSecondary,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);

        if (selected)
        {
            using var pen = new Pen(palette.Accent, 2f);
            var y = rect.Bottom - 2;
            e.Graphics.DrawLine(pen, rect.Left + underlineInset, y, rect.Right - underlineInset, y);
        }
    }
}

internal enum TabHeaderScale
{
    Standard,
    Large,
}

internal sealed class HeaderOnlyTabHost : UserControl
{
    private readonly FlowLayoutPanel headerBar = new();
    private readonly Panel clipPanel = new();
    private readonly HiddenHeaderTabControl pages = new();
    private readonly TabHeaderScale scale;
    private readonly List<HeaderTabItem> headerItems = new();
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    internal HeaderOnlyTabHost(TabHeaderScale scale)
    {
        this.scale = scale;
        BackColor = Color.White;

        headerBar.Dock = DockStyle.Top;
        headerBar.Height = scale == TabHeaderScale.Large ? 47 : 38;
        headerBar.WrapContents = false;
        headerBar.AutoScroll = false;
        headerBar.FlowDirection = FlowDirection.LeftToRight;
        headerBar.BackColor = Color.FromArgb(248, 249, 251);
        headerBar.Padding = new Padding(0);
        headerBar.Margin = Padding.Empty;

        clipPanel.Dock = DockStyle.Fill;
        clipPanel.BackColor = Color.White;
        clipPanel.Margin = Padding.Empty;
        clipPanel.Resize += (_, _) => LayoutNativePages();

        pages.Appearance = TabAppearance.FlatButtons;
        pages.SizeMode = TabSizeMode.Fixed;
        pages.ItemSize = new Size(1, 1);
        pages.Multiline = false;
        pages.SelectedIndexChanged += (_, _) => SyncSelection();

        clipPanel.Controls.Add(pages);
        Controls.Add(clipPanel);
        Controls.Add(headerBar);
    }

    internal void SetTabs(IEnumerable<string> names)
    {
        headerBar.Controls.Clear();
        headerItems.Clear();
        pages.TabPages.Clear();

        var index = 0;
        foreach (var name in names)
        {
            var page = new TabPage(name)
            {
                BackColor = Color.White,
                Padding = new Padding(16),
            };
            page.Controls.Add(new Label
            {
                Text = $"{name}\r\n\r\n下方內容仍由原生 TabControl / TabPage 管理；只有上面的 Header bar 是 Custom。",
                AutoSize = true,
                Font = VisualTokens.Font(10f),
                ForeColor = VisualTokens.TextPrimary,
            });
            pages.TabPages.Add(page);

            var item = new HeaderTabItem(name, scale)
            {
                Index = index,
                Palette = palette,
                Margin = Padding.Empty,
            };
            item.Activated += (_, _) => pages.SelectedIndex = item.Index;
            headerItems.Add(item);
            headerBar.Controls.Add(item);
            index++;
        }

        if (pages.TabCount > 0) pages.SelectedIndex = 0;
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
        // The native header is reduced to 1 px and moved above the clipping panel.
        // The page host remains the real WinForms TabControl; we are not repainting pages.
        pages.Location = new Point(-2, -4);
        pages.Size = new Size(Math.Max(0, clipPanel.ClientSize.Width + 4), Math.Max(0, clipPanel.ClientSize.Height + 6));
    }
}

internal sealed class HiddenHeaderTabControl : TabControl
{
    internal HiddenHeaderTabControl()
    {
        DoubleBuffered = true;
    }
}

internal sealed class HeaderTabItem : Control
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

    internal HeaderTabItem(string text, TabHeaderScale scale)
    {
        Text = text;
        this.scale = scale;
        Height = scale == TabHeaderScale.Large ? 46 : 37;
        Width = MeasurePreferredWidth();
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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
        if (e.Button == MouseButtons.Left) Activated?.Invoke(this, EventArgs.Empty);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var back = hovered && !selected ? palette.Soft : Color.FromArgb(248, 249, 251);
        using (var bg = new SolidBrush(back))
            e.Graphics.FillRectangle(bg, ClientRectangle);

        using var font = selected
            ? VisualTokens.Font(scale == TabHeaderScale.Large ? 11f : 10f, FontStyle.Bold)
            : VisualTokens.Font(scale == TabHeaderScale.Large ? 11f : 10f);

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
        using var g = CreateGraphics();
        using var font = VisualTokens.Font(scale == TabHeaderScale.Large ? 11f : 10f);
        var size = TextRenderer.MeasureText(g, Text, font, new Size(int.MaxValue, Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        return size.Width + (scale == TabHeaderScale.Large ? 40 : 28);
    }
}
