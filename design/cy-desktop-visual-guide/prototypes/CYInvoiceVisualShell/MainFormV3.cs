namespace CYInvoiceVisualShell;

internal sealed class MainFormV3 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private CyDensity currentDensity = CyDensity.Standard;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);
    private bool applyingVisuals;

    private readonly TableLayoutPanel root = new();
    private readonly TableLayoutPanel header = new();
    private readonly Label environmentBadge = new();
    private readonly Label companyLabel = new();
    private readonly FlowLayoutPanel headerActions = new();
    private readonly Button statusButton = new();
    private readonly Button settingsButton = new();
    private readonly V3TabControl tabs = new();
    private readonly TabPage entryTab = new("開立發票");
    private readonly TabPage recordsTab = new("已開立發票清單");
    private readonly TableLayoutPanel footer = new();
    private readonly Label footerStatus = new();
    private readonly FlowLayoutPanel footerTools = new();
    private readonly ComboBox themeCombo = new();
    private readonly ComboBox densityCombo = new();

    private readonly Label entryBanner = new();
    private readonly TextBox buyerNameBox = new();
    private readonly TextBox taxIdBox = new();
    private readonly ComboBox carrierCombo = new();
    private readonly TextBox orderIdBox = new();
    private readonly DataGridView itemGrid = new();
    private readonly Panel itemGridFrame = new();
    private readonly Label totalLabel = new();
    private readonly Button clearButton = new();
    private readonly Button simulateIssueButton = new();
    private readonly TableLayoutPanel entryForm = new();

    private readonly Label recordsBanner = new();
    private readonly DateTimePicker dateFrom = new();
    private readonly DateTimePicker dateTo = new();
    private readonly TextBox keywordBox = new();
    private readonly Button searchButton = new();
    private readonly DataGridView recordsGrid = new();
    private readonly Panel recordsGridFrame = new();
    private readonly Button viewButton = new();
    private readonly Button deleteButton = new();
    private readonly TableLayoutPanel filterTable = new();

    public MainFormV3()
    {
        Text = "CYInvoice Visual Shell V3 — Native-first UI Prototype";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1264, 780);
        MinimumSize = new Size(1040, 660);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowIcon = true;
        TryApplyExecutableIcon();

        BuildShell();
        BuildEntryPage();
        BuildRecordsPage();
        PopulateFakeData();
        WireEvents();
        InitializeSelectors();
        ApplyVisuals();
    }

    private void BuildShell()
    {
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.RowCount = 3;
        root.Margin = Padding.Empty;
        root.Padding = Padding.Empty;
        root.BackColor = VisualTokens.Window;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.HeaderHeight + 6));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        header.Dock = DockStyle.Fill;
        header.ColumnCount = 3;
        header.RowCount = 1;
        header.Margin = Padding.Empty;
        header.Padding = new Padding(16, 0, 16, 0);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.BackColor = Color.White;

        environmentBadge.Text = "測試模式";
        environmentBadge.AutoSize = true;
        environmentBadge.Anchor = AnchorStyles.Left;
        environmentBadge.Padding = new Padding(10, 4, 10, 4);
        environmentBadge.Margin = Padding.Empty;
        environmentBadge.TextAlign = ContentAlignment.MiddleCenter;

        companyLabel.Text = "志遠醫療器材行 · 電子發票 UI Shell";
        companyLabel.Dock = DockStyle.Fill;
        companyLabel.TextAlign = ContentAlignment.MiddleCenter;
        companyLabel.AutoEllipsis = true;
        companyLabel.Margin = new Padding(14, 0, 14, 0);

        ConfigureNativeButton(statusButton, "狀態說明", 88);
        ConfigureNativeButton(settingsButton, "設定", 76);
        headerActions.AutoSize = true;
        headerActions.Anchor = AnchorStyles.Right;
        headerActions.FlowDirection = FlowDirection.LeftToRight;
        headerActions.WrapContents = false;
        headerActions.Margin = Padding.Empty;
        statusButton.Margin = new Padding(0, 0, 8, 0);
        settingsButton.Margin = Padding.Empty;
        headerActions.Controls.Add(statusButton);
        headerActions.Controls.Add(settingsButton);

        header.Controls.Add(environmentBadge, 0, 0);
        header.Controls.Add(companyLabel, 1, 0);
        header.Controls.Add(headerActions, 2, 0);

        tabs.Dock = DockStyle.Fill;
        tabs.Margin = new Padding(14, 0, 14, 0);
        entryTab.BackColor = VisualTokens.Window;
        recordsTab.BackColor = VisualTokens.Window;
        entryTab.Padding = Padding.Empty;
        recordsTab.Padding = Padding.Empty;
        tabs.TabPages.Add(entryTab);
        tabs.TabPages.Add(recordsTab);

        footer.Dock = DockStyle.Fill;
        footer.ColumnCount = 2;
        footer.RowCount = 1;
        footer.Margin = Padding.Empty;
        footer.Padding = new Padding(16, 3, 16, 3);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.BackColor = Color.White;

        footerStatus.Text = "● UI Shell V3 / Native-first / 無 API";
        footerStatus.Dock = DockStyle.Fill;
        footerStatus.TextAlign = ContentAlignment.MiddleLeft;
        footerStatus.Margin = Padding.Empty;

        footerTools.AutoSize = true;
        footerTools.FlowDirection = FlowDirection.LeftToRight;
        footerTools.WrapContents = false;
        footerTools.Anchor = AnchorStyles.Right;
        footerTools.Margin = Padding.Empty;
        var themeLabel = NewLabel("Theme", secondary: true);
        themeLabel.AutoSize = true;
        themeLabel.Anchor = AnchorStyles.Left;
        themeLabel.Margin = new Padding(0, 0, 6, 0);
        ConfigureNativeCombo(themeCombo, 108);
        themeCombo.Margin = new Padding(0, 0, 14, 0);
        var densityLabel = NewLabel("Density", secondary: true);
        densityLabel.AutoSize = true;
        densityLabel.Anchor = AnchorStyles.Left;
        densityLabel.Margin = new Padding(0, 0, 6, 0);
        ConfigureNativeCombo(densityCombo, 126);
        densityCombo.Margin = Padding.Empty;
        footerTools.Controls.Add(themeLabel);
        footerTools.Controls.Add(themeCombo);
        footerTools.Controls.Add(densityLabel);
        footerTools.Controls.Add(densityCombo);
        footer.Controls.Add(footerStatus, 0, 0);
        footer.Controls.Add(footerTools, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    private void BuildEntryPage()
    {
        var content = NewPageLayout(6);
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureBanner(entryBanner, "ⓘ UI Shell V3：標準互動控制改回 Windows / WinForms 原生控制。", new Padding(12, 0, 12, 0));

        entryForm.Dock = DockStyle.Top;
        entryForm.AutoSize = true;
        entryForm.ColumnCount = 5;
        entryForm.RowCount = 2;
        entryForm.Margin = new Padding(0, 0, 0, 18);
        entryForm.BackColor = VisualTokens.Window;
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        entryForm.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));
        entryForm.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));

        ConfigureNativeTextBox(buyerNameBox, "王小明");
        ConfigureNativeTextBox(taxIdBox, "");
        ConfigureNativeTextBox(orderIdBox, "SHELL-20260922-001");
        ConfigureNativeCombo(carrierCombo, 0);
        carrierCombo.Items.AddRange(new object[] { "一般消費者", "手機條碼載具", "公司統編" });
        carrierCombo.SelectedIndex = 0;

        AddField(entryForm, 0, 0, "買　受　人", buyerNameBox);
        AddField(entryForm, 0, 3, "統一編號", taxIdBox);
        AddField(entryForm, 1, 0, "載具類型", carrierCombo);
        AddField(entryForm, 1, 3, "訂單編號", orderIdBox);

        ConfigureGrid(itemGrid);
        ConfigureItemGrid();
        ConfigureGridFrame(itemGridFrame, itemGrid, minHeight: 250);

        totalLabel.Text = "合計　NT$ 1,890";
        totalLabel.AutoSize = true;
        totalLabel.Anchor = AnchorStyles.Left;
        totalLabel.Margin = Padding.Empty;

        ConfigureNativeButton(clearButton, "清空", 106);
        ConfigureNativeButton(simulateIssueButton, "模擬開立", 106, primary: true);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Right,
        };
        clearButton.Margin = new Padding(0, 0, 8, 0);
        simulateIssueButton.Margin = Padding.Empty;
        actions.Controls.Add(clearButton);
        actions.Controls.Add(simulateIssueButton);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = VisualTokens.Window,
            Margin = new Padding(0, 8, 0, 0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(totalLabel, 0, 0);
        bottom.Controls.Add(actions, 1, 0);

        content.Controls.Add(entryBanner, 0, 0);
        content.Controls.Add(CreateSectionHeader("發票基本資料"), 0, 1);
        content.Controls.Add(entryForm, 0, 2);
        content.Controls.Add(CreateSectionHeader("商品明細"), 0, 3);
        content.Controls.Add(itemGridFrame, 0, 4);
        content.Controls.Add(bottom, 0, 5);
        entryTab.Controls.Add(content);
    }

    private void BuildRecordsPage()
    {
        var content = NewPageLayout(5);
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureBanner(recordsBanner, "ⓘ 已開立發票清單為假資料；可測試原生欄位、右鍵選單、Dialog 與 Theme。", new Padding(12, 0, 12, 0));

        filterTable.Dock = DockStyle.Top;
        filterTable.AutoSize = true;
        filterTable.ColumnCount = 7;
        filterTable.RowCount = 1;
        filterTable.BackColor = VisualTokens.Window;
        filterTable.Margin = new Padding(0, 0, 0, 16);
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        filterTable.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));

        ConfigureNativeDate(dateFrom, DateTime.Today.AddDays(-5));
        ConfigureNativeDate(dateTo, DateTime.Today);
        ConfigureNativeTextBox(keywordBox, "");
        ConfigureNativeButton(searchButton, "查詢", 84, primary: true);
        searchButton.Margin = new Padding(12, 0, 0, 0);

        var dateLabel = NewLabel("日期");
        dateLabel.Dock = DockStyle.Fill;
        var toLabel = NewLabel("至");
        toLabel.Dock = DockStyle.Fill;
        toLabel.TextAlign = ContentAlignment.MiddleCenter;
        var keywordLabel = NewLabel("關鍵字");
        keywordLabel.Dock = DockStyle.Fill;
        dateFrom.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        dateTo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        keywordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        searchButton.Anchor = AnchorStyles.None;
        filterTable.Controls.Add(dateLabel, 0, 0);
        filterTable.Controls.Add(dateFrom, 1, 0);
        filterTable.Controls.Add(toLabel, 2, 0);
        filterTable.Controls.Add(dateTo, 3, 0);
        filterTable.Controls.Add(keywordLabel, 4, 0);
        filterTable.Controls.Add(keywordBox, 5, 0);
        filterTable.Controls.Add(searchButton, 6, 0);

        ConfigureGrid(recordsGrid);
        ConfigureRecordsGrid();
        ConfigureGridFrame(recordsGridFrame, recordsGrid);
        ConfigureRecordsContextMenu();

        ConfigureNativeButton(viewButton, "檢視詳細", 112);
        ConfigureNativeButton(deleteButton, "刪除測試列", 112, danger: true);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        deleteButton.Margin = Padding.Empty;
        viewButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(deleteButton);
        actions.Controls.Add(viewButton);

        content.Controls.Add(recordsBanner, 0, 0);
        content.Controls.Add(CreateSectionHeader("查詢條件"), 0, 1);
        content.Controls.Add(filterTable, 0, 2);
        content.Controls.Add(recordsGridFrame, 0, 3);
        content.Controls.Add(actions, 0, 4);
        recordsTab.Controls.Add(content);
    }

    private TableLayoutPanel NewPageLayout(int rows) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = rows,
        BackColor = VisualTokens.Window,
        Padding = new Padding(metrics.OuterPadding),
    };

    private void ConfigureItemGrid()
    {
        itemGrid.Columns.Clear();
        itemGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "品名", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
        itemGrid.Columns.Add(NewNumericColumn("數量", 84));
        itemGrid.Columns.Add(NewNumericColumn("單價", 110));
        itemGrid.Columns.Add(NewNumericColumn("金額", 120));
    }

    private void ConfigureRecordsGrid()
    {
        recordsGrid.Columns.Clear();
        recordsGrid.Columns.Add(NewTextColumn("開立時間", 145));
        recordsGrid.Columns.Add(NewTextColumn("發票號碼", 118));
        recordsGrid.Columns.Add(NewTextColumn("來源", 88));
        recordsGrid.Columns.Add(NewTextColumn("訂單編號", 160));
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "買受人", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 });
        recordsGrid.Columns.Add(NewTextColumn("統編", 92));
        recordsGrid.Columns.Add(NewNumericColumn("金額", 100));
        recordsGrid.Columns.Add(NewTextColumn("狀態", 88));
    }

    private void ConfigureRecordsContextMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        var view = new ToolStripMenuItem("檢視詳細");
        var refresh = new ToolStripMenuItem("模擬重新整理");
        var delete = new ToolStripMenuItem("刪除測試列") { ForeColor = VisualTokens.Danger };
        view.Click += (_, _) => ShowSelectedRecord();
        refresh.Click += (_, _) => recordsBanner.Text = "ⓘ 已完成模擬重新整理；沒有呼叫任何 API。";
        delete.Click += (_, _) => DeleteSelectedRecord();
        menu.Items.Add(view);
        menu.Items.Add(refresh);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(delete);
        recordsGrid.ContextMenuStrip = menu;
    }

    private void PopulateFakeData()
    {
        itemGrid.Rows.Add("一次性針灸針 0.25×40mm", "10", "120", "1,200");
        itemGrid.Rows.Add("不鏽鋼針盤", "1", "450", "450");
        itemGrid.Rows.Add("醫療用棉棒", "3", "80", "240");

        recordsGrid.Rows.Add("2026/09/22 10:31", "AB12345678", "手動", "MO-20260922-001", "王小明", "", "1,890", "已開立");
        recordsGrid.Rows.Add("2026/09/22 09:18", "AB12345677", "MO店+", "MO-20260922-002", "林○○中醫診所", "12345678", "3,260", "已上傳");
        recordsGrid.Rows.Add("2026/09/21 16:42", "AB12345676", "蝦皮", "SP-20260921-881", "陳小姐", "", "760", "待同步");
        recordsGrid.Rows.Add("2026/09/21 14:03", "AB12345675", "手動", "SHELL-0004", "測試公司", "87654321", "5,400", "作廢");
        if (recordsGrid.Rows.Count > 0) recordsGrid.Rows[0].Selected = true;
    }

    private void WireEvents()
    {
        settingsButton.Click += (_, _) => DemoDialogsV3.ShowSettings(this, metrics);
        statusButton.Click += (_, _) => DemoDialogsV3.ShowInfo(this, metrics);
        clearButton.Click += (_, _) =>
        {
            buyerNameBox.Clear(); taxIdBox.Clear(); orderIdBox.Clear();
            entryBanner.Text = "ⓘ 已清空 UI Shell 欄位；沒有修改任何正式資料。";
        };
        simulateIssueButton.Click += (_, _) =>
        {
            entryBanner.Text = "✓ 模擬開立完成：只測試狀態回饋，沒有呼叫 AMEGO。";
            footerStatus.Text = "● 模擬操作完成 / Native-first / 無 API";
        };
        searchButton.Click += (_, _) => recordsBanner.Text = "ⓘ 查詢完成：固定假資料，用來觀察 Table 與 Selection。";
        viewButton.Click += (_, _) => ShowSelectedRecord();
        deleteButton.Click += (_, _) => DeleteSelectedRecord();
        recordsGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowSelectedRecord(); };
        recordsGrid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != recordsGrid.Columns.Count - 1 || e.Value is not string text) return;
            e.CellStyle.ForeColor = text switch { "作廢" => VisualTokens.Danger, "待同步" => VisualTokens.Warning, _ => VisualTokens.Success };
        };
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (applyingVisuals || themeCombo.SelectedIndex < 0) return;
            currentTheme = (CyTheme)themeCombo.SelectedIndex;
            ApplyVisuals();
        };
        densityCombo.SelectedIndexChanged += (_, _) =>
        {
            if (applyingVisuals || densityCombo.SelectedIndex < 0) return;
            currentDensity = (CyDensity)densityCombo.SelectedIndex;
            ApplyVisuals();
        };
    }

    private void InitializeSelectors()
    {
        applyingVisuals = true;
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        densityCombo.Items.AddRange(new object[] { "Compact", "Standard", "Comfortable" });
        themeCombo.SelectedIndex = (int)currentTheme;
        densityCombo.SelectedIndex = (int)currentDensity;
        applyingVisuals = false;
    }

    private void ApplyVisuals()
    {
        applyingVisuals = true;
        palette = VisualTokens.GetPalette(currentTheme);
        metrics = VisualTokens.GetDensity(currentDensity);
        Font = VisualTokens.Font(metrics.BodyPt);
        BackColor = VisualTokens.Window;
        root.BackColor = VisualTokens.Window;
        root.RowStyles[0].Height = metrics.HeaderHeight + 6;
        root.RowStyles[2].Height = Math.Max(38, metrics.InputHeight + 8);
        header.Padding = new Padding(Math.Max(14, metrics.OuterPadding - 2), 0, Math.Max(14, metrics.OuterPadding - 2), 0);
        companyLabel.Font = VisualTokens.Font(Math.Min(14f, metrics.SectionPt + 1f), FontStyle.Bold);
        environmentBadge.BackColor = palette.Soft;
        environmentBadge.ForeColor = palette.Pressed;
        environmentBadge.Font = VisualTokens.Font(metrics.SecondaryPt, FontStyle.Bold);
        footerStatus.ForeColor = palette.Accent;
        footerStatus.Font = VisualTokens.Font(metrics.SecondaryPt);
        tabs.ApplyVisual(palette, metrics);
        entryTab.BackColor = VisualTokens.Window;
        recordsTab.BackColor = VisualTokens.Window;

        ApplyNativeControlVisuals(this);
        ApplyBanner(entryBanner);
        ApplyBanner(recordsBanner);
        ApplyGridVisual(itemGrid);
        ApplyGridVisual(recordsGrid);
        itemGridFrame.BackColor = VisualTokens.Border;
        recordsGridFrame.BackColor = VisualTokens.Border;
        foreach (RowStyle row in entryForm.RowStyles) row.Height = metrics.InputHeight + 8;
        filterTable.RowStyles[0].Height = metrics.InputHeight + 8;
        totalLabel.Font = VisualTokens.Font(metrics.SectionPt, FontStyle.Bold);

        themeCombo.SelectedIndex = (int)currentTheme;
        densityCombo.SelectedIndex = (int)currentDensity;
        applyingVisuals = false;
        PerformLayout();
        Invalidate(true);
    }

    private void ApplyNativeControlVisuals(Control rootControl)
    {
        foreach (Control control in rootControl.Controls)
        {
            switch (control)
            {
                case TextBox box:
                    box.Font = VisualTokens.Font(metrics.BodyPt);
                    box.Height = metrics.InputHeight;
                    box.BackColor = box.ReadOnly ? VisualTokens.ReadOnly : Color.White;
                    box.ForeColor = VisualTokens.TextPrimary;
                    break;
                case ComboBox combo:
                    combo.Font = VisualTokens.Font(metrics.BodyPt);
                    break;
                case DateTimePicker date:
                    date.Font = VisualTokens.Font(metrics.BodyPt);
                    break;
                case Button button:
                    button.Height = Math.Max(30, Math.Min(38, metrics.ButtonHeight));
                    button.Font = VisualTokens.Font(metrics.ButtonPt, button == simulateIssueButton || button == searchButton ? FontStyle.Bold : FontStyle.Regular);
                    break;
                case Label label when label != environmentBadge && label != companyLabel && label != footerStatus && label != entryBanner && label != recordsBanner && label != totalLabel:
                    label.Font = VisualTokens.Font(metrics.BodyPt);
                    label.ForeColor = VisualTokens.TextPrimary;
                    break;
            }
            ApplyNativeControlVisuals(control);
        }
    }

    private void ApplyBanner(Label banner)
    {
        banner.BackColor = palette.Soft;
        banner.ForeColor = VisualTokens.TextPrimary;
        banner.Font = VisualTokens.Font(metrics.BodyPt);
        banner.Height = metrics.InputHeight + 6;
    }

    private void ApplyGridVisual(DataGridView grid)
    {
        grid.Font = VisualTokens.Font(metrics.BodyPt);
        grid.GridColor = VisualTokens.Grid;
        grid.ColumnHeadersDefaultCellStyle.BackColor = VisualTokens.Window;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = VisualTokens.Font(Math.Max(9f, metrics.BodyPt), FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = VisualTokens.Window;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = metrics.RowHeight + 2;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = VisualTokens.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.DefaultCellStyle.SelectionForeColor = VisualTokens.TextPrimary;
        grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 253);
        foreach (DataGridViewRow row in grid.Rows) row.Height = metrics.RowHeight;
    }

    private void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Color.White;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoGenerateColumns = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ScrollBars = ScrollBars.Both;
    }

    private static void ConfigureGridFrame(Panel frame, DataGridView grid, int minHeight = 0)
    {
        frame.Dock = DockStyle.Fill;
        frame.Padding = new Padding(1);
        frame.Margin = new Padding(0, 0, 0, 12);
        frame.BackColor = VisualTokens.Border;
        if (minHeight > 0) frame.MinimumSize = new Size(0, minHeight);
        frame.Controls.Add(grid);
    }

    private static void ConfigureBanner(Label label, string text, Padding padding)
    {
        label.Text = text;
        label.AutoEllipsis = true;
        label.Dock = DockStyle.Top;
        label.Height = 40;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Padding = padding;
        label.Margin = new Padding(0, 0, 0, 12);
    }

    private void ConfigureNativeButton(Button button, string text, int width, bool primary = false, bool danger = false)
    {
        button.Text = text;
        button.Width = width;
        button.Height = Math.Max(30, Math.Min(38, metrics.ButtonHeight));
        button.AutoSize = false;
        button.FlatStyle = FlatStyle.System;
        button.UseVisualStyleBackColor = true;
        button.Font = VisualTokens.Font(metrics.ButtonPt, primary ? FontStyle.Bold : FontStyle.Regular);
        if (danger) button.ForeColor = VisualTokens.Danger;
    }

    private static void ConfigureNativeTextBox(TextBox box, string text)
    {
        box.Text = text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.AutoSize = false;
        box.BackColor = Color.White;
        box.ForeColor = VisualTokens.TextPrimary;
        box.Margin = Padding.Empty;
    }

    private static void ConfigureNativeCombo(ComboBox combo, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.System;
        combo.Margin = Padding.Empty;
        if (width > 0) combo.Width = width;
    }

    private static void ConfigureNativeDate(DateTimePicker picker, DateTime value)
    {
        picker.Format = DateTimePickerFormat.Short;
        picker.Value = value;
        picker.Margin = Padding.Empty;
    }

    private void AddField(TableLayoutPanel table, int row, int labelColumn, string labelText, Control control)
    {
        var label = NewLabel(labelText);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, 0, 10, 0);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = Padding.Empty;
        table.Controls.Add(label, labelColumn, row);
        table.Controls.Add(control, labelColumn + 1, row);
    }

    private Panel CreateSectionHeader(string text)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 34, Margin = new Padding(0, 0, 0, 8), BackColor = VisualTokens.Window };
        var label = NewLabel(text);
        label.Font = VisualTokens.Font(metrics.SectionPt, FontStyle.Bold);
        label.Dock = DockStyle.Top;
        label.Height = 28;
        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = VisualTokens.Divider };
        panel.Controls.Add(divider);
        panel.Controls.Add(label);
        return panel;
    }

    private Label NewLabel(string text, bool secondary = false) => new()
    {
        Text = text,
        ForeColor = secondary ? VisualTokens.TextSecondary : VisualTokens.TextPrimary,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = VisualTokens.Font(secondary ? metrics.SecondaryPt : metrics.BodyPt),
        Margin = Padding.Empty,
    };

    private static DataGridViewTextBoxColumn NewTextColumn(string header, int width) => new()
    {
        HeaderText = header,
        Width = width,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
    };

    private static DataGridViewTextBoxColumn NewNumericColumn(string header, int width)
    {
        var column = NewTextColumn(header, width);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    private void ShowSelectedRecord()
    {
        if (recordsGrid.SelectedRows.Count == 0)
        {
            recordsBanner.Text = "⚠ 請先選擇一筆測試資料。";
            return;
        }
        var number = Convert.ToString(recordsGrid.SelectedRows[0].Cells[1].Value) ?? "測試發票";
        DemoDialogsV3.ShowInfo(this, metrics);
        footerStatus.Text = $"● 已檢視 {number} / UI Shell V3";
    }

    private void DeleteSelectedRecord()
    {
        if (recordsGrid.SelectedRows.Count == 0)
        {
            recordsBanner.Text = "⚠ 請先選擇一筆測試資料。";
            return;
        }
        var row = recordsGrid.SelectedRows[0];
        var number = Convert.ToString(row.Cells[1].Value) ?? "測試發票";
        if (!DemoDialogsV3.ConfirmDanger(this, metrics, number)) return;
        recordsGrid.Rows.Remove(row);
        recordsBanner.Text = "✓ 已刪除一筆記憶體中的測試列；不影響任何正式資料。";
    }

    private void TryApplyExecutableIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) Icon = icon;
        }
        catch { }
    }
}
