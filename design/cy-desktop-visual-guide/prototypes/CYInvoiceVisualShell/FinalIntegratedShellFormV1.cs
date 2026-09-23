namespace CYInvoiceVisualShell;

// Final Phase-1 integration shell: combines the approved visual decisions in
// one no-API WinForms window. It is a validation surface, not production code.
internal sealed class FinalIntegratedShellFormV1 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private bool rowHoverEnabled;
    private int hoveredRow = -1;

    private readonly TableLayoutPanel root = new();
    private readonly ComboBox themeCombo = new();
    private readonly Label dpiLabel = new();
    private readonly Button infoButton = new();
    private readonly IntegratedHeaderTabHost tabs = new(TabHeaderScale.Standard);
    private readonly Label statusLabel = new();

    private readonly TextBox buyerBox = new();
    private readonly TextBox taxIdBox = new();
    private readonly ComboBox carrierCombo = new();
    private readonly TextBox orderBox = new();
    private readonly DataGridView itemGrid = new();
    private readonly Button clearButton = new();
    private readonly RoundedThemeButtonV7 issueButton = new("模擬開立", CyButtonRole.Primary);

    private readonly DateTimePicker dateFrom = new();
    private readonly DateTimePicker dateTo = new();
    private readonly TextBox keywordBox = new();
    private readonly Button searchButton = new();
    private readonly ComboBox selectionModeCombo = new();
    private readonly DataGridView recordsGrid = new();
    private readonly Button viewButton = new();
    private readonly RoundedThemeButtonV7 deleteButton = new("刪除測試列", CyButtonRole.Danger);

    internal FinalIntegratedShellFormV1()
    {
        Text = "CY Desktop Visual Guide — Final Integrated Shell V1";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 790);
        MinimumSize = new Size(1000, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;

        BuildShell();
        BuildEntryPage();
        BuildRecordsPage();
        PopulateFakeData();
        WireEvents();
        InitializeSelectors();
        ApplyTheme();

        Shown += (_, _) => ApplyDpiValidationMetrics();
        DpiChanged += (_, _) => BeginInvoke(ApplyDpiValidationMetrics);
    }

    private void BuildShell()
    {
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.RowCount = 3;
        root.Padding = Padding.Empty;
        root.Margin = Padding.Empty;
        root.BackColor = VisualTokens.Window;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(18, 12, 18, 10),
            Margin = Padding.Empty,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "CY Desktop Visual Guide — Final Shell",
            AutoSize = true,
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
        };

        ConfigureCombo(themeCombo, 110);
        themeCombo.Margin = new Padding(12, 0, 12, 0);
        themeCombo.Anchor = AnchorStyles.Right;

        ConfigureNativeButton(infoButton, "說明", 76);
        infoButton.Margin = new Padding(0, 0, 14, 0);
        infoButton.Anchor = AnchorStyles.Right;

        dpiLabel.AutoSize = true;
        dpiLabel.ForeColor = VisualTokens.TextSecondary;
        dpiLabel.Anchor = AnchorStyles.Right;
        dpiLabel.Margin = Padding.Empty;

        top.Controls.Add(title, 0, 0);
        top.Controls.Add(themeCombo, 1, 0);
        top.Controls.Add(infoButton, 2, 0);
        top.Controls.Add(dpiLabel, 3, 0);

        tabs.Dock = DockStyle.Fill;
        tabs.Margin = new Padding(14, 0, 14, 0);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(18, 0, 18, 0),
            Margin = Padding.Empty,
        };
        statusLabel.Text = "Final validation shell · 無 API / 無資料寫入";
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = VisualTokens.TextSecondary;
        statusLabel.Font = VisualTokens.Font(9f);
        footer.Controls.Add(statusLabel);

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    private void BuildEntryPage()
    {
        var page = NewPageLayout(5);
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        page.Controls.Add(CreateSectionHeader("發票基本資料"), 0, 0);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 2,
            BackColor = VisualTokens.Window,
            Margin = new Padding(0, 6, 0, 18),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureTextBox(buyerBox, "王小明");
        ConfigureTextBox(taxIdBox, "");
        ConfigureTextBox(orderBox, "SHELL-20260923-001");
        ConfigureCombo(carrierCombo, 0);
        carrierCombo.Items.AddRange(new object[] { "一般消費者", "手機條碼載具", "公司統編" });
        carrierCombo.SelectedIndex = 0;

        AddField(form, 0, 0, "買受人", buyerBox);
        AddField(form, 0, 3, "統一編號", taxIdBox);
        AddField(form, 1, 0, "載具類型", carrierCombo);
        AddField(form, 1, 3, "訂單編號", orderBox);
        page.Controls.Add(form, 0, 1);

        page.Controls.Add(CreateSectionHeader("商品明細 — Editable Grid / Cell Selection"), 0, 2);

        ConfigureGrid(itemGrid, readOnly: false, DataGridViewSelectionMode.CellSelect);
        itemGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "品名",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 280,
        });
        itemGrid.Columns.Add(NewColumn("數量", 90, DataGridViewContentAlignment.MiddleRight));
        itemGrid.Columns.Add(NewColumn("單價", 110, DataGridViewContentAlignment.MiddleRight));
        itemGrid.Columns.Add(NewColumn("金額", 120, DataGridViewContentAlignment.MiddleRight));
        page.Controls.Add(WrapGrid(itemGrid), 0, 3);

        ConfigureNativeButton(clearButton, "清空", 90);
        ConfigureThemeButton(issueButton, 112);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        issueButton.Margin = Padding.Empty;
        clearButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(issueButton);
        actions.Controls.Add(clearButton);
        page.Controls.Add(actions, 0, 4);

        tabs.AddPage("開立發票", page);
    }

    private void BuildRecordsPage()
    {
        var page = NewPageLayout(5);
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        page.Controls.Add(CreateSectionHeader("查詢條件"), 0, 0);

        var filter = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 9,
            RowCount = 1,
            BackColor = VisualTokens.Window,
            Margin = new Padding(0, 6, 0, 18),
        };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));

        ConfigureDate(dateFrom, DateTime.Today.AddDays(-7));
        ConfigureDate(dateTo, DateTime.Today);
        ConfigureTextBox(keywordBox, "");
        ConfigureNativeButton(searchButton, "查詢", 76);
        ConfigureCombo(selectionModeCombo, 0);
        selectionModeCombo.Items.AddRange(new object[]
        {
            "整列選取（Record List）",
            "單格選取（Cell）",
            "Row Hover + 單格",
        });
        selectionModeCombo.SelectedIndex = 0;

        AddFilterControl(filter, NewLabel("日期"), 0);
        AddFilterControl(filter, dateFrom, 1);
        AddFilterControl(filter, NewLabel("至"), 2);
        AddFilterControl(filter, dateTo, 3);
        AddFilterControl(filter, NewLabel("關鍵字"), 4);
        AddFilterControl(filter, keywordBox, 5);
        AddFilterControl(filter, searchButton, 6);
        AddFilterControl(filter, NewLabel("Selection"), 7);
        AddFilterControl(filter, selectionModeCombo, 8);
        page.Controls.Add(filter, 0, 1);

        page.Controls.Add(CreateSectionHeader("已開立發票清單 — Native DataGridView / Neutral Header"), 0, 2);

        ConfigureGrid(recordsGrid, readOnly: true, DataGridViewSelectionMode.FullRowSelect);
        recordsGrid.Columns.Add(NewColumn("日期", 84));
        recordsGrid.Columns.Add(NewColumn("發票號碼", 120));
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "買受人",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 190,
        });
        recordsGrid.Columns.Add(NewColumn("統一編號", 110));
        recordsGrid.Columns.Add(NewColumn("類型", 92, DataGridViewContentAlignment.MiddleCenter));
        recordsGrid.Columns.Add(NewColumn("金額", 115, DataGridViewContentAlignment.MiddleRight));
        recordsGrid.Columns.Add(NewColumn("狀態", 95, DataGridViewContentAlignment.MiddleCenter));
        recordsGrid.Columns.Add(NewColumn("備註", 190));
        page.Controls.Add(WrapGrid(recordsGrid), 0, 3);

        ConfigureNativeButton(viewButton, "檢視詳細", 100);
        ConfigureThemeButton(deleteButton, 112);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        deleteButton.Margin = Padding.Empty;
        viewButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(deleteButton);
        actions.Controls.Add(viewButton);
        page.Controls.Add(actions, 0, 4);

        tabs.AddPage("已開立發票清單", page);
    }

    private void PopulateFakeData()
    {
        itemGrid.Rows.Add("一次性針灸針 0.25×40mm", "10", "120", "1,200");
        itemGrid.Rows.Add("不鏽鋼針盤", "1", "450", "450");
        itemGrid.Rows.Add("醫療用棉棒", "3", "80", "240");

        var buyers = new[] { "志遠醫療器材行", "一般消費者", "範例企業有限公司", "王小明", "測試客戶" };
        var statuses = new[] { "已開立", "已開立", "待處理", "已註銷" };
        for (var i = 0; i < 28; i++)
        {
            var date = new DateTime(2026, 9, 1).AddDays(i % 23);
            recordsGrid.Rows.Add(
                date.ToString("MM/dd"),
                $"AB{12345678 + i}",
                buyers[i % buyers.Length],
                i % 3 == 0 ? "12345678" : "",
                i % 4 == 0 ? "統編" : "一般",
                (1280 + (i * 173)).ToString("N0"),
                statuses[i % statuses.Length],
                i % 9 == 0 ? "跨機同步測試資料" : "");
        }

        if (recordsGrid.Rows.Count > 0)
            recordsGrid.Rows[0].Selected = true;
    }

    private void WireEvents()
    {
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (themeCombo.SelectedIndex < 0) return;
            currentTheme = (CyTheme)themeCombo.SelectedIndex;
            ApplyTheme();
        };

        infoButton.Click += (_, _) => MessageBox.Show(
            this,
            "這是 CY Desktop Visual Guide 的整合驗證 Shell。\r\n\r\n" +
            "重點：原生輸入控制、Native-first Table、Header-only Custom Tab、Native / Theme Button 混用，以及 Windows DPI。\r\n\r\n" +
            "所有資料均為假資料，不會呼叫 API。",
            "Final Shell 說明",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        clearButton.Click += (_, _) =>
        {
            buyerBox.Clear();
            taxIdBox.Clear();
            orderBox.Clear();
            statusLabel.Text = "已清空測試欄位 · 無資料寫入";
        };

        issueButton.Click += (_, _) =>
        {
            statusLabel.Text = "模擬開立完成 · 僅 UI 狀態測試";
            MessageBox.Show(this, "模擬開立完成。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        searchButton.Click += (_, _) => statusLabel.Text = "模擬查詢完成 · 顯示固定假資料";
        viewButton.Click += (_, _) => ShowSelectedRecord();
        deleteButton.Click += (_, _) => DeleteSelectedRecord();
        recordsGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) ShowSelectedRecord();
        };

        selectionModeCombo.SelectedIndexChanged += (_, _) => ApplySelectionMode();
        recordsGrid.CellMouseEnter += (_, e) =>
        {
            if (!rowHoverEnabled || e.RowIndex < 0) return;
            SetHoveredRow(e.RowIndex);
        };
        recordsGrid.CellMouseLeave += (_, e) =>
        {
            if (!rowHoverEnabled || e.RowIndex < 0) return;
            if (hoveredRow == e.RowIndex) SetHoveredRow(-1);
        };
    }

    private void InitializeSelectors()
    {
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        themeCombo.SelectedIndex = 0;
    }

    private void ApplyTheme()
    {
        palette = VisualTokens.GetPalette(currentTheme);
        tabs.ApplyTheme(currentTheme);
        issueButton.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        deleteButton.ApplyTheme(currentTheme, palette, VisualTokens.Font(10f, FontStyle.Bold));
        statusLabel.ForeColor = palette.Accent;
        ApplyGridTheme(itemGrid);
        ApplyGridTheme(recordsGrid);
        if (hoveredRow >= 0 && rowHoverEnabled)
            SetHoveredRow(hoveredRow);
        Invalidate(true);
    }

    private void ApplySelectionMode()
    {
        SetHoveredRow(-1);
        rowHoverEnabled = selectionModeCombo.SelectedIndex == 2;
        recordsGrid.SelectionMode = selectionModeCombo.SelectedIndex == 0
            ? DataGridViewSelectionMode.FullRowSelect
            : DataGridViewSelectionMode.CellSelect;
        recordsGrid.ClearSelection();
        statusLabel.Text = selectionModeCombo.SelectedIndex switch
        {
            0 => "Table App Choice：Record List / Full Row Selection",
            1 => "Table App Choice：Cell Selection",
            _ => "Table App Choice：Row Hover + Cell Selection",
        };
    }

    private void SetHoveredRow(int rowIndex)
    {
        if (hoveredRow >= 0 && hoveredRow < recordsGrid.Rows.Count)
            RestoreRowBackground(hoveredRow);

        hoveredRow = rowIndex;
        if (hoveredRow < 0 || hoveredRow >= recordsGrid.Rows.Count) return;

        recordsGrid.Rows[hoveredRow].DefaultCellStyle.BackColor = Blend(Color.White, palette.Soft, 0.55f);
    }

    private void RestoreRowBackground(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= recordsGrid.Rows.Count) return;
        recordsGrid.Rows[rowIndex].DefaultCellStyle.BackColor = rowIndex % 2 == 0
            ? Color.White
            : Color.FromArgb(252, 253, 254);
    }

    private void ShowSelectedRecord()
    {
        var row = recordsGrid.CurrentCell?.OwningRow;
        if (row is null) return;
        MessageBox.Show(
            this,
            $"發票號碼：{row.Cells[1].Value}\r\n買受人：{row.Cells[2].Value}\r\n金額：{row.Cells[5].Value}",
            "發票詳細",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void DeleteSelectedRecord()
    {
        var row = recordsGrid.CurrentCell?.OwningRow;
        if (row is null) return;

        var result = MessageBox.Show(
            this,
            $"確定要刪除測試列 {row.Cells[1].Value}？",
            "確認刪除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes) return;
        recordsGrid.Rows.Remove(row);
        statusLabel.Text = "已刪除一筆假資料 · 不影響任何正式資料";
    }

    private void ApplyDpiValidationMetrics()
    {
        var dpi = DeviceDpi;
        var scale = dpi / 96f;
        dpiLabel.Text = $"Windows DPI：{dpi} ({Math.Round(scale * 100):0}%)";

        var rowHeight = Math.Max(24, (int)Math.Round(30 * scale));
        var headerHeight = Math.Max(28, (int)Math.Round(34 * scale));
        foreach (var grid in new[] { itemGrid, recordsGrid })
        {
            grid.RowTemplate.Height = rowHeight;
            grid.ColumnHeadersHeight = headerHeight;
            foreach (DataGridViewRow row in grid.Rows)
                row.Height = rowHeight;
        }

        issueButton.CornerRadius = 2f * scale;
        deleteButton.CornerRadius = 2f * scale;
    }

    private static TableLayoutPanel NewPageLayout(int rows) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = rows,
        Padding = new Padding(18),
        Margin = Padding.Empty,
        BackColor = VisualTokens.Window,
    };

    private static Control CreateSectionHeader(string text)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = VisualTokens.Window,
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Font = VisualTokens.Font(12f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left,
        };
        var lineHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 0, 0),
            Height = 20,
        };
        var line = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = VisualTokens.Divider,
        };
        lineHost.Controls.Add(line);
        host.Controls.Add(label, 0, 0);
        host.Controls.Add(lineHost, 1, 0);
        return host;
    }

    private static void AddField(TableLayoutPanel form, int row, int labelColumn, string labelText, Control field)
    {
        var label = NewLabel(labelText);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 3, 8, 8);
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 0, 0, 8);
        form.Controls.Add(label, labelColumn, row);
        form.Controls.Add(field, labelColumn + 1, row);
    }

    private static void AddFilterControl(TableLayoutPanel table, Control control, int column)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(column == 0 ? 0 : 6, 0, 0, 0);
        table.Controls.Add(control, column, 0);
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = VisualTokens.Font(10f),
        ForeColor = VisualTokens.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Left,
    };

    private static void ConfigureTextBox(TextBox box, string value)
    {
        box.Text = value;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = VisualTokens.Font(10f);
        box.BackColor = Color.White;
        box.ForeColor = VisualTokens.TextPrimary;
        box.Margin = Padding.Empty;
    }

    private static void ConfigureCombo(ComboBox combo, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.System;
        combo.Font = VisualTokens.Font(10f);
        if (width > 0) combo.Width = width;
        combo.Margin = Padding.Empty;
    }

    private static void ConfigureDate(DateTimePicker picker, DateTime value)
    {
        picker.Value = value;
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy/MM/dd";
        picker.Font = VisualTokens.Font(10f);
        picker.Margin = Padding.Empty;
    }

    private static void ConfigureNativeButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 34;
        button.Font = VisualTokens.Font(10f);
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.Margin = Padding.Empty;
    }

    private static void ConfigureThemeButton(RoundedThemeButtonV7 button, int width)
    {
        button.Width = width;
        button.Height = 34;
        button.CornerRadius = 2f;
        button.Margin = Padding.Empty;
    }

    private static Panel WrapGrid(DataGridView grid)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = VisualTokens.Border,
            Padding = new Padding(1),
            Margin = new Padding(0, 6, 0, 0),
        };
        grid.Dock = DockStyle.Fill;
        frame.Controls.Add(grid);
        return frame;
    }

    private static void ConfigureGrid(DataGridView grid, bool readOnly, DataGridViewSelectionMode selectionMode)
    {
        grid.AllowUserToAddRows = !readOnly;
        grid.AllowUserToDeleteRows = !readOnly;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToResizeColumns = true;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = VisualTokens.Grid;
        grid.MultiSelect = false;
        grid.ReadOnly = readOnly;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 30;
        grid.ScrollBars = ScrollBars.Both;
        grid.SelectionMode = selectionMode;
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
    }

    private void ApplyGridTheme(DataGridView grid)
    {
        grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.DefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;

        // Header remains neutral even when current cell changes columns.
        var headerBack = Color.FromArgb(245, 247, 250);
        grid.ColumnHeadersDefaultCellStyle.BackColor = headerBack;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        grid.Refresh();
    }

    private static DataGridViewTextBoxColumn NewColumn(
        string text,
        int width,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft) => new()
    {
        HeaderText = text,
        Width = width,
        MinimumWidth = Math.Min(width, 70),
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment },
    };

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }
}
