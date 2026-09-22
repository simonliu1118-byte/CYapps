namespace CYInvoiceVisualShell;

internal sealed class ButtonLabFormV4 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);

    private readonly ComboBox themeCombo = new();
    private readonly Label themeTitle = new();
    private readonly RoundedThemeButtonV4 largePrimary = new("開立測試發票");
    private readonly RoundedThemeButtonV4 compactPrimary = new("查詢");
    private readonly RoundedThemeButtonV4 dangerButton = new("刪除", CyButtonRole.Danger);
    private readonly Button nativeSecondary = new();
    private readonly Button nativeSettings = new();
    private readonly Button openShell = new();

    internal ButtonLabFormV4()
    {
        Text = "CY Rounded Theme Button V4 — Validation Lab";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 430);
        MinimumSize = new Size(680, 390);
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
            RowCount = 6,
            Padding = new Padding(24),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 18),
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        themeTitle.Text = "Rounded Theme Button 驗證";
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
            MaximumSize = new Size(690, 0),
            Text = "這版只驗證『繼承原生 Button + owner-paint 圓角外觀』。一般按鈕仍維持 Windows 原生；只有 Primary / Danger 關鍵按鈕使用 Theme 色。",
            ForeColor = VisualTokens.TextSecondary,
            Font = VisualTokens.Font(9.5f),
            Margin = new Padding(0, 0, 0, 18),
        };

        var nativePanel = NewSection("一般按鈕（Windows 原生）");
        ConfigureNative(nativeSecondary, "清空", 104);
        ConfigureNative(nativeSettings, "設定", 88);
        nativeSecondary.Margin = new Padding(0, 0, 8, 0);
        nativePanel.Controls.Add(nativeSecondary);
        nativePanel.Controls.Add(nativeSettings);

        var themedPanel = NewSection("關鍵按鈕（Theme 圓角 owner-paint）");
        largePrimary.Size = new Size(190, 46);
        largePrimary.CornerRadius = 7;
        largePrimary.Font = VisualTokens.Font(14f, FontStyle.Bold);
        largePrimary.Margin = new Padding(0, 0, 14, 0);
        compactPrimary.Size = new Size(104, 36);
        compactPrimary.CornerRadius = 7;
        compactPrimary.Font = VisualTokens.Font(10f, FontStyle.Bold);
        compactPrimary.Margin = new Padding(0, 0, 14, 0);
        dangerButton.Size = new Size(104, 36);
        dangerButton.CornerRadius = 7;
        dangerButton.Font = VisualTokens.Font(10f, FontStyle.Bold);
        themedPanel.Controls.Add(largePrimary);
        themedPanel.Controls.Add(compactPrimary);
        themedPanel.Controls.Add(dangerButton);

        var helper = new Label
        {
            AutoSize = true,
            Text = "請特別測：四個角是否完整、滑鼠 Hover / Pressed 是否乾淨、100% / 125% / 150% DPI 是否還有切角。Coral Primary 刻意使用淡粉紅，Danger 維持正紅。",
            ForeColor = VisualTokens.TextSecondary,
            Font = VisualTokens.Font(9f),
            Margin = new Padding(0, 14, 0, 0),
        };

        ConfigureNative(openShell, "開啟目前完整 V3 UI Shell", 210);
        openShell.Anchor = AnchorStyles.Left;
        openShell.Margin = new Padding(0, 20, 0, 0);
        openShell.Click += (_, _) =>
        {
            using var shell = new MainFormV3();
            shell.ShowDialog(this);
        };

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(nativePanel, 0, 2);
        root.Controls.Add(themedPanel, 0, 3);
        root.Controls.Add(helper, 0, 4);
        root.Controls.Add(openShell, 0, 5);
        Controls.Add(root);
    }

    private static FlowLayoutPanel NewSection(string title)
    {
        var outer = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16),
            Padding = new Padding(0, 30, 0, 4),
        };
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Font = VisualTokens.Font(11f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, -28, 20, 0),
        };
        outer.Controls.Add(label);
        return outer;
    }

    private static void ConfigureNative(Button button, string text, int width)
    {
        button.Text = text;
        button.Size = new Size(width, 34);
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.Font = VisualTokens.Font(10f);
    }

    private void ApplyTheme()
    {
        palette = VisualTokens.GetPalette(currentTheme);
        largePrimary.ApplyTheme(currentTheme, palette, VisualTokens.Font(14f, FontStyle.Bold));
        compactPrimary.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        dangerButton.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        themeTitle.ForeColor = palette.Accent;
        Invalidate(true);
    }
}
