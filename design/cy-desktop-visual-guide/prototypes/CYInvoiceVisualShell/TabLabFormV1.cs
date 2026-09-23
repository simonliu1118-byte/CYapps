namespace CYInvoiceVisualShell;

internal sealed class TabLabFormV1 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private readonly ComboBox themeCombo = new();
    private readonly Label dpiLabel = new();
    private readonly ThemeTabControlV1 normalTabs = new();
    private readonly ThemeTabControlV1 manyTabs = new();
    private readonly ThemeTabControlV1 largeTabs = new();

    internal TabLabFormV1()
    {
        Text = "CY Tab Lab V4 — Flat Native Geometry / No Jump Validation";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 820);
        MinimumSize = new Size(940, 700);
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
            RowCount = 8,
            Padding = new Padding(26, 22, 26, 22),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 4),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Tab Lab — Flat Native Geometry / Standard + Large",
            AutoSize = true,
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Anchor = AnchorStyles.Left,
        };

        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.FlatStyle = FlatStyle.System;
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

        var openTable = new Button
        {
            Text = "開啟 Table Lab",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 2, 10, 2),
            Margin = new Padding(0, 0, 12, 0),
            Anchor = AnchorStyles.Right,
            UseVisualStyleBackColor = true,
        };
        openTable.Click += (_, _) => new TableLabFormV1().Show(this);

        dpiLabel.AutoSize = true;
        dpiLabel.ForeColor = VisualTokens.TextSecondary;
        dpiLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(themeCombo, 1, 0);
        header.Controls.Add(openTable, 2, 0);
        header.Controls.Add(dpiLabel, 3, 0);

        var note = new Label
        {
            Text = "V4 改用原生 TabControl 的 FlatButtons 幾何，避免傳統 Selected Tab 凸框／陰影在切換時改變尺寸而跳動。Header 仍只負責文字、Hover 與 Accent 底線；TabPage、鍵盤與頁面切換仍是原生行為。請特別快速切換 Tab，觀察外框是否還會跳／閃。",
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 14),
        };

        var sectionA = SectionTitle("A. Standard — 一般 4 Tabs / 常見商務主畫面");
        normalTabs.ConfigureStandard();
        BuildTabs(normalTabs, new[] { "開立發票", "已開立發票清單", "待處理", "設定" });
        normalTabs.Margin = new Padding(0, 5, 0, 12);

        var sectionB = SectionTitle("B. Standard — 6 Tabs + 長短標題混合 / 看排列與快速切換");
        manyTabs.ConfigureStandard();
        BuildTabs(manyTabs, new[] { "基本資料", "商品明細", "載具與買受人", "發票上傳與同步狀態", "列印與 PDF", "系統設定" });
        manyTabs.Margin = new Padding(0, 5, 0, 12);

        var sectionC = SectionTitle("C. Large — 較高層級功能切換 / 約 11 pt + 較大 padding（App Choice）");
        largeTabs.ConfigureLarge();
        BuildTabs(largeTabs, new[] { "發票作業", "開立紀錄", "同步處理", "系統設定" });
        largeTabs.Margin = new Padding(0, 5, 0, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(sectionA, 0, 2);
        root.Controls.Add(normalTabs, 0, 3);
        root.Controls.Add(sectionB, 0, 4);
        root.Controls.Add(manyTabs, 0, 5);
        root.Controls.Add(sectionC, 0, 6);
        root.Controls.Add(largeTabs, 0, 7);
        Controls.Add(root);
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = VisualTokens.Font(11.5f, FontStyle.Bold),
        ForeColor = VisualTokens.TextPrimary,
        Margin = Padding.Empty,
    };

    private static void BuildTabs(ThemeTabControlV1 tabs, IEnumerable<string> names)
    {
        tabs.Dock = DockStyle.Fill;
        foreach (var name in names)
        {
            var page = new TabPage(name) { BackColor = Color.White, Padding = new Padding(18) };
            var content = new Label
            {
                Text = $"{name}\r\n\r\n這裡只是原生 TabPage 內容區。請快速連續切換不同 Tab、滑過 Header，並縮放視窗觀察是否還有邊框跳動／閃爍。",
                AutoSize = true,
                Font = VisualTokens.Font(10f),
                ForeColor = VisualTokens.TextPrimary,
            };
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
        }
    }

    private void ApplyTheme()
    {
        normalTabs.ApplyTheme(currentTheme);
        manyTabs.ApplyTheme(currentTheme);
        largeTabs.ApplyTheme(currentTheme);
    }

    private void RefreshDpiLabel()
    {
        var dpi = DeviceDpi;
        dpiLabel.Text = $"Windows DPI：{dpi} ({Math.Round(dpi / 96d * 100)}%)";
    }
}
