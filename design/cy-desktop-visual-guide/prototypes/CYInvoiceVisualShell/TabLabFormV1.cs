namespace CYInvoiceVisualShell;

internal sealed class TabLabFormV1 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private readonly ComboBox themeCombo = new();
    private readonly Label dpiLabel = new();
    private readonly ThemeTabControlV1 normalTabs = new();
    private readonly ThemeTabControlV1 manyTabs = new();

    internal TabLabFormV1()
    {
        Text = "CY Tab Lab V1 — Native TabControl / Header-only Owner Draw";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 720);
        MinimumSize = new Size(920, 620);
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
            RowCount = 6,
            Padding = new Padding(26, 22, 26, 22),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 4),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Tab Lab — 原生 TabControl + Header-only Owner Draw",
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

        dpiLabel.AutoSize = true;
        dpiLabel.ForeColor = VisualTokens.TextSecondary;
        dpiLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(themeCombo, 1, 0);
        header.Controls.Add(dpiLabel, 2, 0);

        var note = new Label
        {
            Text = "只畫 Tab Header：Active = Accent 底線 + 較強字重；Hover = Accent Soft。頁面切換、鍵盤、焦點與原生 TabControl 行為不重做。",
            AutoSize = true,
            MaximumSize = new Size(1000, 0),
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 18),
        };

        var sectionA = SectionTitle("A. 一般 4 Tabs — 常見商務主畫面");
        BuildTabs(normalTabs, new[] { "開立發票", "已開立發票清單", "待處理", "設定" });
        normalTabs.Margin = new Padding(0, 6, 0, 18);

        var sectionB = SectionTitle("B. 6 Tabs + 長短標題混合 — 看排列、Hover 與空間不足時的原生行為");
        BuildTabs(manyTabs, new[] { "基本資料", "商品明細", "載具與買受人", "發票上傳與同步狀態", "列印與 PDF", "系統設定" });
        manyTabs.Margin = new Padding(0, 6, 0, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(sectionA, 0, 2);
        root.Controls.Add(normalTabs, 0, 3);
        root.Controls.Add(sectionB, 0, 4);
        root.Controls.Add(manyTabs, 0, 5);
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
                Text = $"{name}\r\n\r\n這裡只是原生 TabPage 內容區。請切換頁籤、用滑鼠移過其他 Tab，並縮放視窗觀察 Header。",
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
    }

    private void RefreshDpiLabel()
    {
        var dpi = DeviceDpi;
        dpiLabel.Text = $"Windows DPI：{dpi} ({Math.Round(dpi / 96d * 100)}%)";
    }
}
