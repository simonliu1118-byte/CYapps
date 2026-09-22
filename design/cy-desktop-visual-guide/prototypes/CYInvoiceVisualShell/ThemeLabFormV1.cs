namespace CYInvoiceVisualShell;

internal sealed class ThemeLabFormV1 : Form
{
    internal ThemeLabFormV1()
    {
        Text = "CY Theme Lab V1 — Four Theme Comparison";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(980, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);

        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            AutoSize = true,
            Text = "Theme Lab — Blue / Teal / Coral / Apricot 同畫面比較",
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, 0, 0, 4),
        };

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Text = "這版不調整數值，只把目前四組候選色同時放到真實 Windows 控制情境中。重點看家族一致性、Coral 與 Danger 是否分得開，以及 Soft / Selection 是否過重。",
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 18),
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = VisualTokens.Window,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        grid.Controls.Add(BuildThemeCard(CyTheme.Blue, "Blue"), 0, 0);
        grid.Controls.Add(BuildThemeCard(CyTheme.Teal, "Teal"), 1, 0);
        grid.Controls.Add(BuildThemeCard(CyTheme.Coral, "Coral"), 0, 1);
        grid.Controls.Add(BuildThemeCard(CyTheme.Apricot, "Apricot"), 1, 1);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(grid, 0, 2);
        Controls.Add(root);
    }

    private static Control BuildThemeCard(CyTheme theme, string name)
    {
        var palette = VisualTokens.GetPalette(theme);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 14, 14),
            Padding = new Padding(16),
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.White,
            Margin = Padding.Empty,
        };
        for (var i = 0; i < 6; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headingRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 10),
        };
        headingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headingRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var heading = new Label
        {
            AutoSize = true,
            Text = name,
            Font = VisualTokens.Font(14f, FontStyle.Bold),
            ForeColor = palette.Accent,
            Anchor = AnchorStyles.Left,
        };
        var accentHex = new Label
        {
            AutoSize = true,
            Text = Hex(palette.Accent),
            Font = VisualTokens.Font(9f),
            ForeColor = VisualTokens.TextSecondary,
            Anchor = AnchorStyles.Right,
        };
        headingRow.Controls.Add(heading, 0, 0);
        headingRow.Controls.Add(accentHex, 1, 0);

        var swatches = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 5,
            Margin = new Padding(0, 0, 0, 12),
        };
        for (var i = 0; i < 5; i++) swatches.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        swatches.Controls.Add(NewSwatch("Accent", palette.Accent), 0, 0);
        swatches.Controls.Add(NewSwatch("Hover", palette.Hover), 1, 0);
        swatches.Controls.Add(NewSwatch("Pressed", palette.Pressed), 2, 0);
        swatches.Controls.Add(NewSwatch("Soft", palette.Soft), 3, 0);
        swatches.Controls.Add(NewSwatch("Selection", palette.Selection), 4, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
        };

        var secondary = new Button
        {
            Text = "一般操作",
            Size = new Size(102, 36),
            AutoSize = false,
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            Font = VisualTokens.Font(10f),
            Margin = new Padding(0, 0, 8, 0),
        };

        var primary = new RoundedThemeButtonV7("主要操作")
        {
            Size = new Size(112, 36),
            CornerRadius = 2f,
            Margin = new Padding(0, 0, 8, 0),
        };
        primary.ApplyTheme(theme, palette, VisualTokens.Font(10f, FontStyle.Bold));

        var danger = new RoundedThemeButtonV7("刪除", CyButtonRole.Danger)
        {
            Size = new Size(88, 36),
            CornerRadius = 2f,
            Margin = Padding.Empty,
        };
        danger.ApplyTheme(theme, palette, VisualTokens.Font(10f, FontStyle.Bold));

        actions.Controls.Add(secondary);
        actions.Controls.Add(primary);
        actions.Controls.Add(danger);

        var tabSample = new Panel
        {
            Height = 38,
            Dock = DockStyle.Top,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 10),
        };
        var tabText = new Label
        {
            Text = "Active Tab",
            AutoSize = false,
            Size = new Size(118, 31),
            Location = new Point(0, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = VisualTokens.Font(10f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            BackColor = Color.White,
        };
        var underline = new Panel
        {
            BackColor = palette.Accent,
            Height = 2,
            Width = 92,
            Location = new Point(13, 31),
        };
        tabSample.Controls.Add(tabText);
        tabSample.Controls.Add(underline);

        var selectionSample = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            ColumnCount = 3,
            BackColor = palette.Selection,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10, 0, 10, 0),
        };
        selectionSample.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        selectionSample.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        selectionSample.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        selectionSample.Controls.Add(NewSelectionLabel("範例資料列", ContentAlignment.MiddleLeft), 0, 0);
        selectionSample.Controls.Add(NewSelectionLabel("NT$ 1,890", ContentAlignment.MiddleRight), 1, 0);
        selectionSample.Controls.Add(NewSelectionLabel("已選取", ContentAlignment.MiddleCenter), 2, 0);

        var softSample = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
            Text = "  Soft Surface / Info-like Highlight",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextPrimary,
            BackColor = palette.Soft,
            Margin = new Padding(0, 0, 0, 8),
        };

        var footer = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Text = $"Hover {Hex(palette.Hover)}   Pressed {Hex(palette.Pressed)}   Soft {Hex(palette.Soft)}   Selection {Hex(palette.Selection)}",
            Font = VisualTokens.Font(8.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = Padding.Empty,
        };

        content.Controls.Add(headingRow, 0, 0);
        content.Controls.Add(swatches, 0, 1);
        content.Controls.Add(actions, 0, 2);
        content.Controls.Add(tabSample, 0, 3);
        content.Controls.Add(selectionSample, 0, 4);
        content.Controls.Add(softSample, 0, 5);
        content.Controls.Add(footer, 0, 6);

        card.Controls.Add(content);
        return card;
    }

    private static Control NewSwatch(string label, Color color)
    {
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 6, 0),
        };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var colorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = color,
            Margin = new Padding(0, 0, 0, 3),
        };
        var name = new Label
        {
            AutoSize = true,
            Text = label,
            Font = VisualTokens.Font(8.5f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
        };
        var hex = new Label
        {
            AutoSize = true,
            Text = Hex(color),
            Font = VisualTokens.Font(8f),
            ForeColor = VisualTokens.TextSecondary,
        };
        box.Controls.Add(colorPanel, 0, 0);
        box.Controls.Add(name, 0, 1);
        box.Controls.Add(hex, 0, 2);
        return box;
    }

    private static Label NewSelectionLabel(string text, ContentAlignment alignment) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = alignment,
        Font = VisualTokens.Font(9.5f),
        ForeColor = VisualTokens.TextPrimary,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
    };

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
