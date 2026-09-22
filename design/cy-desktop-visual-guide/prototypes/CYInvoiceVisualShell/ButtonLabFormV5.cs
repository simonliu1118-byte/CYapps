namespace CYInvoiceVisualShell;

internal sealed class ButtonLabFormV5 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    private readonly ComboBox themeCombo = new();
    private readonly Label themeTitle = new();

    private readonly Button nativeCancel = new();
    private readonly RoundedThemeButtonV4 themedSave = new("儲存");
    private readonly Button nativePreview = new();
    private readonly RoundedThemeButtonV4 themedIssue = new("開立測試發票");
    private readonly Button nativeClose = new();
    private readonly RoundedThemeButtonV4 themedDanger = new("刪除", CyButtonRole.Danger);

    private readonly Button nativeSearch = new();
    private readonly RoundedThemeButtonV4 themedSearch = new("查詢");
    private readonly Button nativeLarge = new();
    private readonly RoundedThemeButtonV4 themedLarge = new("主要操作");

    internal ButtonLabFormV5()
    {
        Text = "CY Button V5 — Native / Theme Rounded Comparison";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 610);
        MinimumSize = new Size(820, 560);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);

        BuildLayout();
        ApplyTheme();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(26),
            BackColor = VisualTokens.Window,
        };
        for (var i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 12),
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        themeTitle.Text = "原生 × Theme 圓角按鈕混排比較";
        themeTitle.AutoSize = true;
        themeTitle.Font = VisualTokens.Font(16f, FontStyle.Bold);
        themeTitle.ForeColor = VisualTokens.TextPrimary;
        themeTitle.Anchor = AnchorStyles.Left;

        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.FlatStyle = FlatStyle.System;
        themeCombo.Width = 130;
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        themeCombo.SelectedIndex = 0;
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (themeCombo.SelectedIndex < 0) return;
            currentTheme = (CyTheme)themeCombo.SelectedIndex;
            ApplyTheme();
        };
        heading.Controls.Add(themeTitle, 0, 0);
        heading.Controls.Add(themeCombo, 1, 0);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Text = "V5 把原生與自繪按鈕直接放在同一組、同高度比較。自繪 Standard 圓角縮到 4px，大型按鈕縮到 5px；其餘繪製方式仍沿用 CYInvoice 已驗證的內縮 + AntiAlias 做法。",
            ForeColor = VisualTokens.TextSecondary,
            Font = VisualTokens.Font(9.5f),
            Margin = new Padding(0, 0, 0, 18),
        };

        ConfigureNative(nativeCancel, "取消", 96, 36);
        ConfigureThemed(themedSave, 96, 36, 4, 10f);
        var saveGroup = NewActionRow("A. 一般 Dialog：原生 Secondary + Theme Primary", nativeCancel, themedSave);

        ConfigureNative(nativePreview, "預覽", 132, 46, 12f);
        ConfigureThemed(themedIssue, 190, 46, 5, 12f, bold: true);
        var issueGroup = NewActionRow("B. 主畫面：原生一般操作 + 大型 Theme Primary", nativePreview, themedIssue);

        ConfigureNative(nativeClose, "關閉", 96, 36);
        ConfigureThemed(themedDanger, 96, 36, 4, 10f);
        var dangerGroup = NewActionRow("C. 危險操作：原生 Secondary + Theme Danger", nativeClose, themedDanger);

        ConfigureNative(nativeSearch, "查詢", 104, 36);
        ConfigureThemed(themedSearch, 104, 36, 4, 10f, bold: true);
        var directStandard = NewActionRow("D. 同文案直接比較（Standard 36px）", nativeSearch, themedSearch);

        ConfigureNative(nativeLarge, "主要操作", 170, 46, 12f);
        ConfigureThemed(themedLarge, 170, 46, 5, 12f, bold: true);
        var directLarge = NewActionRow("E. 同文案直接比較（Large 46px）", nativeLarge, themedLarge);

        var helper = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Text = "判斷重點：1) 同一排是否像兩套 UI；2) 4px / 5px 自繪圓角是否已接近 Windows 原生；3) Primary / Danger 的 Theme 強化是否值得保留；4) 125% / 150% DPI 下邊角是否仍完整。",
            ForeColor = VisualTokens.TextSecondary,
            Font = VisualTokens.Font(9f),
            Margin = new Padding(0, 18, 0, 0),
        };

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(saveGroup, 0, 2);
        root.Controls.Add(issueGroup, 0, 3);
        root.Controls.Add(dangerGroup, 0, 4);
        root.Controls.Add(directStandard, 0, 5);
        root.Controls.Add(directLarge, 0, 6);
        root.Controls.Add(helper, 0, 7);
        Controls.Add(root);
    }

    private static TableLayoutPanel NewActionRow(string title, Control first, Control second)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 14),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Font = VisualTokens.Font(10.5f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, 0, 0, 7),
        };
        row.SetColumnSpan(label, 2);
        row.Controls.Add(label, 0, 0);

        first.Margin = new Padding(0, 0, 10, 0);
        second.Margin = Padding.Empty;
        row.Controls.Add(first, 0, 1);
        row.Controls.Add(second, 1, 1);
        return row;
    }

    private static void ConfigureNative(Button button, string text, int width, int height, float fontPt = 10f)
    {
        button.Text = text;
        button.Size = new Size(width, height);
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.Font = VisualTokens.Font(fontPt);
    }

    private static void ConfigureThemed(RoundedThemeButtonV4 button, int width, int height, int radius, float fontPt, bool bold = false)
    {
        button.Size = new Size(width, height);
        button.CornerRadius = radius;
        button.Font = VisualTokens.Font(fontPt, bold ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ApplyTheme()
    {
        palette = VisualTokens.GetPalette(currentTheme);
        themedSave.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        themedIssue.ApplyTheme(currentTheme, palette, VisualTokens.Font(12f, FontStyle.Bold));
        themedDanger.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        themedSearch.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        themedLarge.ApplyTheme(currentTheme, palette, VisualTokens.Font(12f, FontStyle.Bold));
        themeTitle.ForeColor = palette.Accent;
        Invalidate(true);
    }
}
