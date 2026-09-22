namespace CYInvoiceVisualShell;

internal sealed class MainFormV2 : Form
{
    private CyTheme currentTheme = CyTheme.Blue;
    private CyDensity currentDensity = CyDensity.Standard;
    private ThemePalette palette = VisualTokens.GetPalette(CyTheme.Blue);
    private DensityMetrics metrics = VisualTokens.GetDensity(CyDensity.Standard);
    private bool applyingVisuals;

    private readonly TableLayoutPanel root = new();
    private readonly TableLayoutPanel header = new();
    private readonly V2Badge environmentBadge = new("測試模式");
    private readonly Label companyLabel = new();
    private readonly FlowLayoutPanel headerActions = new();
    private readonly V2Button statusButton = new("狀態說明");
    private readonly V2Button settingsButton = new("設定");
    private readonly V2TabHost tabs = new();
    private readonly Panel entryPage = new() { Tag = "window" };
    private readonly Panel recordsPage = new() { Tag = "window" };
    private readonly TableLayoutPanel footer = new();
    private readonly Label footerStatus = new();
    private readonly FlowLayoutPanel footerTools = new();
    private readonly V2ComboBox themeCombo = new();
    private readonly V2ComboBox densityCombo = new();

    private readonly V2Banner entryBanner = new("ⓘ UI Shell V2：只使用假資料，不會連線、開票或寫入正式資料。");
    private readonly V2TextBox buyerNameBox = new("王小明");
    private readonly V2TextBox taxIdBox = new("");
    private readonly V2ComboBox carrierCombo = new();
    private readonly V2TextBox orderIdBox = new("SHELL-20260922-001");
    private readonly V2Grid itemGrid = new();
    private readonly V2SurfaceFrame itemGridFrame = new();
    private readonly Label totalLabel = new();
    private readonly V2Button clearButton = new("清空") { FixedWidth = 106 };
    private readonly V2Button simulateIssueButton = new("模擬開立", CyButtonRole.Primary) { FixedWidth = 106 };
    private readonly TableLayoutPanel entryForm = new();

    private readonly V2Banner recordsBanner = new("ⓘ 已開立發票清單為假資料；可測試選取、右鍵選單、Dialog 與 Theme。");
    private readonly V2DateField dateFrom = new();
    private readonly V2DateField dateTo = new();
    private readonly V2TextBox keywordBox = new("");
    private readonly V2Button searchButton = new("查詢", CyButtonRole.Primary) { FixedWidth = 84 };
    private readonly V2Grid recordsGrid = new();
    private readonly V2SurfaceFrame recordsGridFrame = new();
    private readonly V2Button viewButton = new("檢視詳細") { FixedWidth = 112 };
    private readonly V2Button deleteButton = new("刪除測試列", CyButtonRole.Danger) { FixedWidth = 112 };
    private readonly TableLayoutPanel filterTable = new();

    public MainFormV2()
    {
        Text = "CYInvoice Visual Shell V2 — Phase 1 UI Prototype";
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

        environmentBadge.Anchor = AnchorStyles.Left;
        companyLabel.Text = "志遠醫療器材行 · 電子發票 UI Shell";
        companyLabel.Dock = DockStyle.Fill;
        companyLabel.TextAlign = ContentAlignment.MiddleCenter;
        companyLabel.AutoEllipsis = true;
        companyLabel.Margin = new Padding(14, 0, 14, 0);
        companyLabel.Tag = "section";

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
        tabs.AddPage("開立發票", entryPage);
        tabs.AddPage("已開立發票清單", recordsPage);

        footer.Dock = DockStyle.Fill;
        footer.ColumnCount = 2;
        footer.RowCount = 1;
        footer.Margin = Padding.Empty;
        footer.Padding = new Padding(16, 3, 16, 3);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.BackColor = Color.White;

        footerStatus.Text = "● UI Shell V2 / 無 API / 無正式資料";
        footerStatus.Dock = DockStyle.Fill;
        footerStatus.TextAlign = ContentAlignment.MiddleLeft;
        footerStatus.Margin = Padding.Empty;
        footerStatus.Tag = "secondary";

        footerTools.AutoSize = true;
        footerTools.FlowDirection = FlowDirection.LeftToRight;
        footerTools.WrapContents = false;
        footerTools.Anchor = AnchorStyles.Right;
        footerTools.Margin = Padding.Empty;

        var themeLabel = NewLabel("Theme", "secondary");
        themeLabel.AutoSize = true;
        themeLabel.Anchor = AnchorStyles.Left;
        themeLabel.Margin = new Padding(0, 0, 6, 0);
        themeCombo.Width = 108;
        themeCombo.Margin = new Padding(0, 0, 14, 0);

        var densityLabel = NewLabel("Density", "secondary");
        densityLabel.AutoSize = true;
        densityLabel.Anchor = AnchorStyles.Left;
        densityLabel.Margin = new Padding(0, 0, 6, 0);
        densityCombo.Width = 126;
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
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = VisualTokens.Window,
            Tag = "window",
            Padding = new Padding(metrics.OuterPadding),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        entryForm.Dock = DockStyle.Top;
        entryForm.AutoSize = true;
        entryForm.ColumnCount = 5;
        entryForm.RowCount = 2;
        entryForm.Margin = new Padding(0, 0, 0, 18);
        entryForm.BackColor = VisualTokens.Window;
        entryForm.Tag = "window-no-pad";
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        entryForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        entryForm.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));
        entryForm.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));

        carrierCombo.Items.AddRange(new object[] { "一般消費者", "手機條碼載具", "公司統編" });
        carrierCombo.SelectedIndex = 0;

        AddField(entryForm, 0, 0, "買　受　人", buyerNameBox);
        AddField(entryForm, 0, 3, "統一編號", taxIdBox);
        AddField(entryForm, 1, 0, "載具類型", carrierCombo);
        AddField(entryForm, 1, 3, "訂單編號", orderIdBox);

        ConfigureItemGrid();
        itemGridFrame.Dock = DockStyle.Fill;
        itemGridFrame.MinimumSize = new Size(0, 250);
        itemGridFrame.Margin = new Padding(0, 0, 0, 14);
        itemGridFrame.Controls.Add(itemGrid);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = VisualTokens.Window,
            Tag = "window-no-pad",
            Margin = new Padding(0, 8, 0, 0),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        totalLabel.Text = "合計　NT$ 1,890";
        totalLabel.AutoSize = true;
        totalLabel.Anchor = AnchorStyles.Left;
        totalLabel.Margin = Padding.Empty;
        totalLabel.Tag = "section";

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
        bottom.Controls.Add(totalLabel, 0, 0);
        bottom.Controls.Add(actions, 1, 0);

        content.Controls.Add(entryBanner, 0, 0);
        content.Controls.Add(CreateSectionHeader("發票基本資料"), 0, 1);
        content.Controls.Add(entryForm, 0, 2);
        content.Controls.Add(CreateSectionHeader("商品明細"), 0, 3);
        content.Controls.Add(itemGridFrame, 0, 4);
        content.Controls.Add(bottom, 0, 5);
        entryPage.Controls.Add(content);
    }

    private void BuildRecordsPage()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = VisualTokens.Window,
            Tag = "window",
            Padding = new Padding(metrics.OuterPadding),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        filterTable.Dock = DockStyle.Top;
        filterTable.AutoSize = true;
        filterTable.ColumnCount = 7;
        filterTable.RowCount = 1;
        filterTable.BackColor = VisualTokens.Window;
        filterTable.Tag = "window-no-pad";
        filterTable.Margin = new Padding(0, 0, 0, 16);
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        filterTable.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + 8));

        dateFrom.Value = DateTime.Today.AddDays(-3);
        dateTo.Value = DateTime.Today;
        keywordBox.Margin = Padding.Empty;
        searchButton.Margin = new Padding(12, 0, 0, 0);
        searchButton.Anchor = AnchorStyles.None;

        var dateLabel = NewLabel("日期", "body");
        dateLabel.Dock = DockStyle.Fill;
        var toLabel = NewLabel("至", "body");
        toLabel.Dock = DockStyle.Fill;
        toLabel.TextAlign = ContentAlignment.MiddleCenter;
        var keywordLabel = NewLabel("關鍵字", "body");
        keywordLabel.Dock = DockStyle.Fill;

        dateFrom.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        dateTo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        keywordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        filterTable.Controls.Add(dateLabel, 0, 0);
        filterTable.Controls.Add(dateFrom, 1, 0);
        filterTable.Controls.Add(toLabel, 2, 0);
        filterTable.Controls.Add(dateTo, 3, 0);
        filterTable.Controls.Add(keywordLabel, 4, 0);
        filterTable.Controls.Add(keywordBox, 5, 0);
        filterTable.Controls.Add(searchButton, 6, 0);

        ConfigureRecordsGrid();
        recordsGridFrame.Dock = DockStyle.Fill;
        recordsGridFrame.Margin = new Padding(0, 0, 0, 12);
        recordsGridFrame.Controls.Add(recordsGrid);
        ConfigureRecordsContextMenu();

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
        recordsPage.Controls.Add(content);
    }

    private void ConfigureItemGrid()
    {
        itemGrid.Columns.Clear();
        itemGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "品名",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260,
        });
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
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "買受人",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 180,
        });
        recordsGrid.Columns.Add(NewTextColumn("統編", 92));
        recordsGrid.Columns.Add(NewNumericColumn("金額", 100));
        recordsGrid.Columns.Add(NewTextColumn("狀態", 88));
    }

    private void ConfigureRecordsContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = VisualTokens.Font(metrics.BodyPt),
        };
        var view = new ToolStripMenuItem("檢視詳細");
        var refresh = new ToolStripMenuItem("模擬重新整理");
        var delete = new ToolStripMenuItem("刪除測試列") { ForeColor = VisualTokens.Danger };
        view.Click += (_, _) => ShowSelectedRecord();
        refresh.Click += (_, _) => recordsBanner.Message = "ⓘ 已完成模擬重新整理；沒有呼叫任何 API。";
        delete.Click += (_, _) => DeleteSelectedRecord();
        menu.Items.Add(view);
        menu.Items.Add(refresh);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(delete);
        recordsGrid.ContextMenuStrip = menu;
    }

    private void PopulateFakeData()
    {
        itemGrid.Rows.Clear();
        itemGrid.Rows.Add("一次性針灸針 0.25×40mm", "10", "120", "1,200");
        itemGrid.Rows.Add("不鏽鋼針盤", "1", "450", "450");
        itemGrid.Rows.Add("醫療用棉棒", "3", "80", "240");

        recordsGrid.Rows.Clear();
        recordsGrid.Rows.Add("2026/09/22 10:31", "AB12345678", "手動", "MO-20260922-001", "王小明", "", "1,890", "已開立");
        recordsGrid.Rows.Add("2026/09/22 09:18", "AB12345677", "MO店+", "MO-20260922-002", "林○○中醫診所", "12345678", "3,260", "已上傳");
        recordsGrid.Rows.Add("2026/09/21 16:42", "AB12345676", "蝦皮", "SP-20260921-881", "陳小姐", "", "760", "待同步");
        recordsGrid.Rows.Add("2026/09/21 14:03", "AB12345675", "手動", "SHELL-0004", "測試公司", "87654321", "5,400", "作廢");
        if (recordsGrid.Rows.Count > 0) recordsGrid.Rows[0].Selected = true;
    }

    private void WireEvents()
    {
        settingsButton.Click += (_, _) => DemoDialogsV2.ShowSettings(this, currentTheme, palette, metrics);
        statusButton.Click += (_, _) => DemoDialogsV2.ShowInfo(this, currentTheme, palette, metrics);
        clearButton.Click += (_, _) =>
        {
            buyerNameBox.Text = string.Empty;
            taxIdBox.Text = string.Empty;
            orderIdBox.Text = string.Empty;
            entryBanner.Message = "ⓘ 已清空 UI Shell 欄位；沒有修改任何正式資料。";
        };
        simulateIssueButton.Click += (_, _) =>
        {
            entryBanner.Message = "✓ 模擬開立完成：只測試狀態回饋，沒有呼叫 AMEGO。";
            footerStatus.Text = "● 模擬操作完成 / 無 API";
        };
        searchButton.Click += (_, _) => recordsBanner.Message = "ⓘ 查詢完成：固定假資料，用來觀察 Table 與 Selection。";
        viewButton.Click += (_, _) => ShowSelectedRecord();
        deleteButton.Click += (_, _) => DeleteSelectedRecord();
        recordsGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowSelectedRecord(); };
        recordsGrid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != recordsGrid.Columns.Count - 1 || e.Value is not string text) return;
            if (text == "作廢") e.CellStyle.ForeColor = VisualTokens.Danger;
            else if (text == "待同步") e.CellStyle.ForeColor = VisualTokens.Warning;
            else e.CellStyle.ForeColor = VisualTokens.Success;
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
        themeCombo.Items.Clear();
        themeCombo.Items.AddRange(new object[] { "Blue", "Teal", "Coral", "Apricot" });
        densityCombo.Items.Clear();
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
        header.BackColor = Color.White;
        header.Padding = new Padding(Math.Max(14, metrics.OuterPadding - 2), 0, Math.Max(14, metrics.OuterPadding - 2), 0);
        companyLabel.Font = VisualTokens.Font(Math.Min(14f, metrics.SectionPt + 1f), FontStyle.Bold);
        companyLabel.ForeColor = VisualTokens.TextPrimary;
        footer.BackColor = Color.White;
        footer.Padding = new Padding(Math.Max(14, metrics.OuterPadding - 2), 3, Math.Max(14, metrics.OuterPadding - 2), 3);
        footerStatus.ForeColor = palette.Accent;

        foreach (RowStyle row in entryForm.RowStyles)
            row.Height = metrics.InputHeight + 8;
        filterTable.RowStyles[0].Height = metrics.InputHeight + 8;

        ApplyTree(this);
        if (recordsGrid.ContextMenuStrip is not null)
            recordsGrid.ContextMenuStrip.Font = VisualTokens.Font(metrics.BodyPt);

        themeCombo.SelectedIndex = (int)currentTheme;
        densityCombo.SelectedIndex = (int)currentDensity;
        Invalidate(true);
        applyingVisuals = false;
    }

    private void ApplyTree(Control control)
    {
        if (control is ICyVisualV2 visual)
            visual.ApplyVisual(currentTheme, palette, metrics);

        if (control is Label label)
            ApplyLabel(label);
        else if (control is Panel panel && Equals(panel.Tag, "window"))
            panel.BackColor = VisualTokens.Window;
        else if (control is TableLayoutPanel table)
        {
            if (Equals(table.Tag, "window"))
            {
                table.BackColor = VisualTokens.Window;
                table.Padding = new Padding(metrics.OuterPadding);
            }
            else if (Equals(table.Tag, "window-no-pad"))
                table.BackColor = VisualTokens.Window;
        }

        foreach (Control child in control.Controls)
            ApplyTree(child);
    }

    private void ApplyLabel(Label label)
    {
        var tag = label.Tag as string;
        if (tag == "section")
        {
            label.Font = VisualTokens.Font(metrics.SectionPt, FontStyle.Bold);
            label.ForeColor = VisualTokens.TextPrimary;
        }
        else if (tag == "secondary")
        {
            label.Font = VisualTokens.Font(metrics.SecondaryPt);
            label.ForeColor = VisualTokens.TextSecondary;
        }
        else
        {
            label.Font = VisualTokens.Font(metrics.BodyPt);
            label.ForeColor = VisualTokens.TextPrimary;
        }
    }

    private Panel CreateSectionHeader(string text)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = VisualTokens.Window,
            Tag = "window",
        };
        var label = NewLabel(text, "section");
        label.Dock = DockStyle.Top;
        label.Height = 28;
        label.TextAlign = ContentAlignment.MiddleLeft;
        var divider = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = VisualTokens.Divider,
        };
        panel.Controls.Add(divider);
        panel.Controls.Add(label);
        return panel;
    }

    private void AddField(TableLayoutPanel table, int row, int labelColumn, string labelText, Control control)
    {
        var label = NewLabel(labelText, "body");
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, 0, 10, 0);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = Padding.Empty;
        table.Controls.Add(label, labelColumn, row);
        table.Controls.Add(control, labelColumn + 1, row);
    }

    private Label NewLabel(string text, string tag) => new()
    {
        Text = text,
        Tag = tag,
        ForeColor = VisualTokens.TextPrimary,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
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
            recordsBanner.Message = "⚠ 請先選擇一筆測試資料。";
            return;
        }

        var row = recordsGrid.SelectedRows[0];
        var number = Convert.ToString(row.Cells[1].Value) ?? "測試發票";
        DemoDialogsV2.ShowInfo(this, currentTheme, palette, metrics);
        footerStatus.Text = $"● 已檢視 {number} / UI Shell V2";
    }

    private void DeleteSelectedRecord()
    {
        if (recordsGrid.SelectedRows.Count == 0)
        {
            recordsBanner.Message = "⚠ 請先選擇一筆測試資料。";
            return;
        }

        var row = recordsGrid.SelectedRows[0];
        var number = Convert.ToString(row.Cells[1].Value) ?? "測試發票";
        if (!DemoDialogsV2.ConfirmDanger(this, currentTheme, palette, metrics, number)) return;
        recordsGrid.Rows.Remove(row);
        recordsBanner.Message = "✓ 已刪除一筆記憶體中的測試列；不影響任何正式資料。";
    }

    private void TryApplyExecutableIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) Icon = icon;
        }
        catch
        {
            // Prototype only: icon failure must not block visual shell startup.
        }
    }
}
