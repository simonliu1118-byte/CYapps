namespace CYInvoiceVisualShell;

internal sealed class TableLabFormV1 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private readonly ComboBox themeCombo = new();
    private readonly CheckBox verticalGrid = new();
    private readonly Label dpiLabel = new();
    private readonly Label metricsLabel = new();
    private readonly BufferedDataGridView grid = new();

    internal TableLabFormV1()
    {
        Text = "CY Table Lab V2 — Neutral Header / Grid Continuity / Resize / Scrollbar";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(860, 560);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);

        BuildLayout();
        BuildGrid();
        ApplyTheme();
        Shown += (_, _) => RefreshDpiLabel();
        DpiChanged += (_, _) => RefreshDpiLabel();
        Resize += (_, _) => RefreshMetrics();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 22),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
            Text = "Table Lab — Header / Body 欄線連續性",
            AutoSize = true,
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Anchor = AnchorStyles.Left,
        };

        themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        themeCombo.FlatStyle = FlatStyle.System;
        themeCombo.Width = 112;
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        themeCombo.SelectedIndex = 0;
        themeCombo.Margin = new Padding(12, 0, 12, 0);
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (themeCombo.SelectedIndex < 0) return;
            currentTheme = (CyTheme)themeCombo.SelectedIndex;
            ApplyTheme();
        };

        verticalGrid.Text = "顯示垂直欄線";
        verticalGrid.AutoSize = true;
        verticalGrid.Checked = true;
        verticalGrid.Anchor = AnchorStyles.Right;
        verticalGrid.Margin = new Padding(0, 2, 16, 0);
        verticalGrid.CheckedChanged += (_, _) => ApplyGridLineMode();

        dpiLabel.AutoSize = true;
        dpiLabel.ForeColor = VisualTokens.TextSecondary;
        dpiLabel.Anchor = AnchorStyles.Right;

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(themeCombo, 1, 0);
        header.Controls.Add(verticalGrid, 2, 0);
        header.Controls.Add(dpiLabel, 3, 0);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            Text = "Header 維持中性淺灰，不跟 current cell 變深色；Theme 色只用於資料列 Selection。請拖曳欄寬、縮放視窗、上下捲動，特別觀察垂直線是否出現約 1px 錯位。",
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 2, 0, 12),
        };

        metricsLabel.AutoSize = true;
        metricsLabel.Font = VisualTokens.Font(9f);
        metricsLabel.ForeColor = VisualTokens.TextSecondary;
        metricsLabel.Margin = new Padding(0, 0, 0, 10);

        var section = new Label
        {
            Text = "A. 發票清單範例 — 10 pt / Row 30px / Header 34px（僅此 Lab 的 App Choice）",
            AutoSize = true,
            Font = VisualTokens.Font(11.5f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, 0, 0, 6),
        };

        grid.Dock = DockStyle.Fill;
        grid.Margin = Padding.Empty;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(metricsLabel, 0, 2);
        root.Controls.Add(section, 0, 3);
        root.Controls.Add(grid, 0, 4);
        Controls.Add(root);
    }

    private void BuildGrid()
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToResizeColumns = true;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = VisualTokens.Grid;
        grid.MultiSelect = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 30;
        grid.ScrollBars = ScrollBars.Both;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.StandardTab = true;

        grid.DefaultCellStyle.Font = VisualTokens.Font(10f);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.DefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(252, 253, 254);

        var headerBack = Color.FromArgb(245, 247, 250);
        grid.ColumnHeadersDefaultCellStyle.Font = VisualTokens.Font(10f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.BackColor = headerBack;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBack;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

        AddTextColumn("date", "日期", 82, 78);
        AddTextColumn("number", "發票號碼", 112, 104);
        AddTextColumn("buyer", "買受人", 180, 130);
        AddTextColumn("taxId", "統一編號", 100, 92);
        AddTextColumn("type", "類型", 95, 82, DataGridViewContentAlignment.MiddleCenter);
        AddTextColumn("amount", "金額", 110, 96, DataGridViewContentAlignment.MiddleRight);
        AddTextColumn("status", "狀態", 90, 80, DataGridViewContentAlignment.MiddleCenter);
        AddTextColumn("note", "備註", 180, 120);

        for (var i = 0; i < 80; i++)
        {
            var day = (i % 28) + 1;
            var number = $"AB{12345678 + i:D8}";
            var buyer = (i % 5) switch
            {
                0 => "志遠醫療器材行",
                1 => "一般消費者",
                2 => "範例企業有限公司",
                3 => "王小明",
                _ => "測試客戶",
            };
            var taxId = i % 3 == 0 ? "12345678" : string.Empty;
            var type = string.IsNullOrEmpty(taxId) ? "一般" : "統編";
            var amount = (1280 + i * 173).ToString("N0");
            var status = i % 11 == 0 ? "已註銷" : i % 7 == 0 ? "待處理" : "已開立";
            var note = i % 9 == 0 ? "跨機同步測試資料" : string.Empty;
            grid.Rows.Add($"09/{day:00}", number, buyer, taxId, type, amount, status, note);
        }

        grid.ColumnWidthChanged += (_, _) => RefreshMetrics();
        grid.Scroll += (_, _) => RefreshMetrics();
        grid.DataBindingComplete += (_, _) => RefreshMetrics();
        ApplyGridLineMode();
    }

    private void AddTextColumn(string name, string header, float fillWeight, int minWidth,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = fillWeight,
            MinimumWidth = minWidth,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.True,
        };
        column.DefaultCellStyle.Alignment = alignment;
        grid.Columns.Add(column);
    }

    private void ApplyTheme()
    {
        var palette = VisualTokens.GetPalette(currentTheme);
        grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.RowsDefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.Invalidate();
    }

    private void ApplyGridLineMode()
    {
        grid.CellBorderStyle = verticalGrid.Checked
            ? DataGridViewCellBorderStyle.Single
            : DataGridViewCellBorderStyle.SingleHorizontal;
        grid.Invalidate();
        RefreshMetrics();
    }

    private void RefreshDpiLabel()
    {
        var dpi = DeviceDpi;
        dpiLabel.Text = $"Windows DPI：{dpi} ({Math.Round(dpi / 96d * 100)}%)";
        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        if (grid.Columns.Count == 0) return;
        var widths = string.Join(" / ", grid.Columns.Cast<DataGridViewColumn>().Take(5).Select(c => c.Width));
        metricsLabel.Text = $"Grid viewport {grid.ClientSize.Width}×{grid.ClientSize.Height}px ｜ 前五欄 Width：{widths} ｜ Vertical grid：{(verticalGrid.Checked ? "ON" : "OFF")} ｜ 可直接拖曳欄界測試";
    }
}

internal sealed class BufferedDataGridView : DataGridView
{
    internal BufferedDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}
