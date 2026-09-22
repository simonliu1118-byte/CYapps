namespace CYInvoiceVisualShell;

internal sealed class MainForm : Form
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
    private readonly CyButton statusButton = new("狀態說明");
    private readonly CyButton settingsButton = new("設定");
    private readonly CyTabControl tabs = new();
    private readonly TabPage entryTab = new("開立發票");
    private readonly TabPage recordsTab = new("已開立發票清單");
    private readonly TableLayoutPanel footer = new();
    private readonly Label footerStatus = new();
    private readonly FlowLayoutPanel footerTools = new();
    private readonly ComboBox themeCombo = new();
    private readonly ComboBox densityCombo = new();

    private readonly CyBanner entryBanner = new("ⓘ UI Shell 模式：此畫面只使用假資料，不會連線、開票或寫入正式資料。");
    private readonly CyTextBox buyerNameBox = new() { Text = "王小明" };
    private readonly CyTextBox taxIdBox = new() { Text = "" };
    private readonly ComboBox carrierCombo = new();
    private readonly CyTextBox orderIdBox = new() { Text = "SHELL-20260922-001" };
    private readonly CyGrid itemGrid = new();
    private readonly Label totalLabel = new();
    private readonly CyButton clearButton = new("清空");
    private readonly CyButton simulateIssueButton = new("模擬開立", CyButtonRole.Primary, large: true);

    private readonly CyBanner recordsBanner = new("ⓘ 已開立發票清單為假資料；可測試選取、右鍵選單、Dialog 與不同 Theme。 ");
    private readonly DateTimePicker dateFrom = new();
    private readonly DateTimePicker dateTo = new();
    private readonly CyTextBox keywordBox = new() { Text = "" };
    private readonly CyButton searchButton = new("查詢", CyButtonRole.Primary);
    private readonly CyGrid recordsGrid = new();
    private readonly CyButton viewButton = new("檢視詳細");
    private readonly CyButton deleteButton = new("刪除測試列", CyButtonRole.Danger);

    public MainForm()
    {
        Text = "CYInvoice Visual Shell — Phase 1 UI Prototype";
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
        InitializePreviewSelectors();
        ApplyVisuals();
    }

    private void BuildShell()
    {
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.RowCount = 3;
        root.Padding = Padding.Empty;
        root.Margin = Padding.Empty;
        root.BackColor = VisualTokens.Window;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        header.Dock = DockStyle.Fill;
        header.ColumnCount = 3;
        header.RowCount = 1;
        header.Margin = Padding.Empty;
        header.Padding = new Padding(14, 0, 14, 0);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.BackColor = Color.White;

        environmentBadge.Text = "測試模式";
        environmentBadge.AutoSize = true;
        environmentBadge.Anchor = AnchorStyles.Left;
        environmentBadge.Padding = new Padding(10, 4, 10, 4);
        environmentBadge.BorderStyle = BorderStyle.FixedSingle;
        environmentBadge.TextAlign = ContentAlignment.MiddleCenter;
        environmentBadge.Margin = new Padding(0);

        companyLabel.Text = "志遠醫療器材行 · 電子發票 UI Shell";
        companyLabel.Dock = DockStyle.Fill;
        companyLabel.TextAlign = ContentAlignment.MiddleCenter;
        companyLabel.AutoEllipsis = true;
        companyLabel.Margin = Padding.Empty;

        headerActions.AutoSize = true;
        headerActions.Anchor = AnchorStyles.Right;
        headerActions.FlowDirection = FlowDirection.LeftToRight;
        headerActions.WrapContents = false;
        headerActions.Margin = Padding.Empty;
        headerActions.Controls.Add(statusButton);
        headerActions.Controls.Add(settingsButton);

        header.Controls.Add(environmentBadge, 0, 0);
        header.Controls.Add(companyLabel, 1, 0);
        header.Controls.Add(headerActions, 2, 0);

        tabs.Dock = DockStyle.Fill;
        tabs.Margin = new Padding(14, 0, 14, 0);
        entryTab.Padding = Padding.Empty;
        recordsTab.Padding = Padding.Empty;
        entryTab.BackColor = VisualTokens.Window;
        recordsTab.BackColor = VisualTokens.Window;
        tabs.TabPages.Add(entryTab);
        tabs.TabPages.Add(recordsTab);

        footer.Dock = DockStyle.Fill;
        footer.ColumnCount = 2;
        footer.RowCount = 1;
        footer.Margin = Padding.Empty;
        footer.Padding = new Padding(14, 2, 14, 2);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.BackColor = Color.White;

        footerStatus.Text = "● UI Shell / 無 API / 無正式資料";
        footerStatus.Dock = DockStyle.Fill;
        footerStatus.TextAlign = ContentAlignment.MiddleLeft;
        footerStatus.Margin = Padding.Empty;

        footerTools.AutoSize = true;
        footerTools.FlowDirection = FlowDirection.LeftToRight;
        footerTools.WrapContents = false;
        footerTools.Anchor = AnchorStyles.Right;
        footerTools.Margin = Padding.Empty;

        var themeLabel = NewLabel("Theme", "secondary");
        themeLabel.AutoSize = true;
        themeLabel.Anchor = AnchorStyles.Left;
        var densityLabel = NewLabel("Density", "secondary");
        densityLabel.AutoSize = true;
        densityLabel.Anchor = AnchorStyles.Left;
        ConfigurePreviewCombo(themeCombo, 112);
        ConfigurePreviewCombo(densityCombo, 120);
        footerTools.Controls.Add(themeLabel);
        footerTools.Controls.Add(themeCombo);
        footerTools.Controls.Add(Spacer(8));
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
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = VisualTokens.Window,
            Tag = "window",
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
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

        var basicHeader = CreateSectionHeader("發票基本資料");
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 18),
            BackColor = VisualTokens.Window,
            Tag = "window",
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + metrics.FieldGap));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + metrics.FieldGap));

        carrierCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        carrierCombo.Items.AddRange(new object[] { "一般消費者", "手機條碼載具", "公司統編" });
        carrierCombo.SelectedIndex = 0;
        carrierCombo.FlatStyle = FlatStyle.Flat;
        carrierCombo.Margin = new Padding(0, 2, 0, 2);
        carrierCombo.Dock = DockStyle.Fill;

        AddFormField(form, 0, 0, "買 受 人", buyerNameBox);
        AddFormField(form, 0, 2, "統一編號", taxIdBox);
        AddFormField(form, 1, 0, "載具類型", carrierCombo);
        AddFormField(form, 1, 2, "訂單編號", orderIdBox);

        var itemsHeader = CreateSectionHeader("商品明細");
        ConfigureItemGrid();
        itemGrid.Dock = DockStyle.Fill;
        itemGrid.MinimumSize = new Size(0, 240);
        itemGrid.Margin = new Padding(0, 0, 0, 14);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = VisualTokens.Window,
            Tag = "window",
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
        actions.Controls.Add(clearButton);
        actions.Controls.Add(simulateIssueButton);

        bottom.Controls.Add(totalLabel, 0, 0);
        bottom.Controls.Add(actions, 1, 0);

        content.Controls.Add(entryBanner, 0, 0);
        content.Controls.Add(basicHeader, 0, 1);
        content.Controls.Add(form, 0, 2);
        content.Controls.Add(itemsHeader, 0, 3);
        content.Controls.Add(itemGrid, 0, 4);
        content.Controls.Add(bottom, 0, 5);
        scroll.Controls.Add(content);
        entryTab.Controls.Add(scroll);
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

        var filterHeader = CreateSectionHeader("查詢條件");
        var filter = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = VisualTokens.Window,
            Tag = "window",
            Margin = new Padding(0, 0, 0, 16),
        };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        dateFrom.Format = DateTimePickerFormat.Short;
        dateTo.Format = DateTimePickerFormat.Short;
        dateFrom.Value = DateTime.Today.AddDays(-3);
        dateTo.Value = DateTime.Today;
        dateFrom.Margin = new Padding(0, 2, 0, 2);
        dateTo.Margin = new Padding(0, 2, 0, 2);
        dateFrom.Dock = DockStyle.Fill;
        dateTo.Dock = DockStyle.Fill;
        keywordBox.Dock = DockStyle.Fill;

        var dateLabel = NewLabel("日期", "body");
        dateLabel.Dock = DockStyle.Fill;
        var toLabel = NewLabel("至", "body");
        toLabel.Dock = DockStyle.Fill;
        var keywordLabel = NewLabel("關鍵字", "body");
        keywordLabel.Dock = DockStyle.Fill;
        filter.Controls.Add(dateLabel, 0, 0);
        filter.Controls.Add(dateFrom, 1, 0);
        filter.Controls.Add(toLabel, 2, 0);
        filter.Controls.Add(dateTo, 3, 0);
        filter.Controls.Add(keywordLabel, 4, 0);
        filter.Controls.Add(keywordBox, 5, 0);
        filter.Controls.Add(searchButton, 6, 0);

        var recordsHeader = CreateSectionHeader("已開立發票");
        ConfigureRecordsGrid();
        recordsGrid.Dock = DockStyle.Fill;
        recordsGrid.Margin = new Padding(0, 0, 0, 12);
        ConfigureRecordsContextMenu();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        actions.Controls.Add(deleteButton);
        actions.Controls.Add(viewButton);

        content.Controls.Add(recordsBanner, 0, 0);
        content.Controls.Add(filterHeader, 0, 1);
        content.Controls.Add(filter, 0, 2);
        content.Controls.Add(recordsGrid, 0, 3);
        content.Controls.Add(actions, 0, 4);
        recordsTab.Controls.Add(content);
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
            MinimumWidth = 160,
        });
        recordsGrid.Columns.Add(NewTextColumn("統編", 92));
        recordsGrid.Columns.Add(NewNumericColumn("金額", 100));
        recordsGrid.Columns.Add(NewTextColumn("狀態", 88));
    }

    private void ConfigureRecordsContextMenu()
    {
        var menu = new ContextMenuStrip();
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
        settingsButton.Click += (_, _) => DemoDialogs.ShowSettings(this, palette, metrics);
        statusButton.Click += (_, _) => DemoDialogs.ShowInfo(this, palette, metrics);
        clearButton.Click += (_, _) =>
        {
            buyerNameBox.Text = string.Empty;
            taxIdBox.Text = string.Empty;
            orderIdBox.Text = string.Empty;
            entryBanner.Message = "ⓘ 已清空 UI Shell 欄位；沒有修改任何正式資料。";
        };
        simulateIssueButton.Click += (_, _) =>
        {
            entryBanner.Message = "✓ 模擬開立完成：此訊息只測試狀態回饋，沒有呼叫 AMEGO。";
            footerStatus.Text = "● 模擬操作完成 / 無 API";
        };
        searchButton.Click += (_, _) => recordsBanner.Message = "ⓘ 查詢完成：這是固定假資料，用來觀察 Table 與 Selection。";
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

    private void InitializePreviewSelectors()
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
        root.RowStyles[0].Height = metrics.HeaderHeight;
        header.BackColor = Color.White;
        header.Padding = new Padding(Math.Max(12, metrics.OuterPadding - 2), 0, Math.Max(12, metrics.OuterPadding - 2), 0);
        environmentBadge.BackColor = palette.Soft;
        environmentBadge.ForeColor = palette.Accent;
        environmentBadge.Font = VisualTokens.Font(metrics.SecondaryPt, FontStyle.Bold);
        companyLabel.ForeColor = VisualTokens.TextPrimary;
        companyLabel.Font = VisualTokens.Font(Math.Min(14f, metrics.SectionPt + 1f), FontStyle.Bold);

        footer.BackColor = Color.White;
        footerStatus.ForeColor = palette.Accent;
        footerStatus.Font = VisualTokens.Font(metrics.SecondaryPt);

        entryTab.BackColor = VisualTokens.Window;
        recordsTab.BackColor = VisualTokens.Window;
        ApplyToTree(this);

        carrierCombo.Font = VisualTokens.Font(metrics.BodyPt);
        carrierCombo.BackColor = Color.White;
        carrierCombo.ForeColor = VisualTokens.TextPrimary;
        dateFrom.Font = VisualTokens.Font(metrics.BodyPt);
        dateTo.Font = VisualTokens.Font(metrics.BodyPt);
        themeCombo.Font = VisualTokens.Font(metrics.SecondaryPt);
        densityCombo.Font = VisualTokens.Font(metrics.SecondaryPt);
        themeCombo.SelectedIndex = (int)currentTheme;
        densityCombo.SelectedIndex = (int)currentDensity;

        ResizeFormRows();
        Invalidate(true);
        applyingVisuals = false;
    }

    private void ApplyToTree(Control control)
    {
        switch (control)
        {
            case CyButton button:
                button.ApplyVisual(palette, metrics);
                break;
            case CyTextBox box:
                box.ApplyVisual(palette, metrics);
                break;
            case CyBanner banner:
                banner.ApplyVisual(palette, metrics);
                break;
            case CyTabControl tab:
                tab.ApplyVisual(palette, metrics);
                break;
            case CyGrid grid:
                grid.ApplyVisual(palette, metrics);
                break;
            case Label label:
                ApplyLabel(label);
                break;
            case Panel panel when Equals(panel.Tag, "divider"):
                panel.BackColor = VisualTokens.Divider;
                break;
            case Panel panel when Equals(panel.Tag, "window"):
                panel.BackColor = VisualTokens.Window;
                break;
            case TableLayoutPanel table when Equals(table.Tag, "window"):
                table.BackColor = VisualTokens.Window;
                if (table.Parent is TabPage || table.Parent is Panel) table.Padding = new Padding(metrics.OuterPadding);
                break;
        }

        foreach (Control child in control.Controls)
            ApplyToTree(child);
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

    private void ResizeFormRows()
    {
        foreach (var table in FindTables(this))
        {
            if (table.ColumnCount == 4 && table.RowCount == 2)
            {
                foreach (RowStyle row in table.RowStyles)
                    row.Height = metrics.InputHeight + metrics.FieldGap;
            }
        }
    }

    private static IEnumerable<TableLayoutPanel> FindTables(Control rootControl)
    {
        foreach (Control child in rootControl.Controls)
        {
            if (child is TableLayoutPanel table) yield return table;
            foreach (var nested in FindTables(child)) yield return nested;
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
            Tag = "divider",
        };
        panel.Controls.Add(divider);
        panel.Controls.Add(label);
        return panel;
    }

    private void AddFormField(TableLayoutPanel table, int row, int startColumn, string labelText, Control control)
    {
        var label = NewLabel(labelText, "body");
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, 0, 10, 0);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(label, startColumn, row);
        table.Controls.Add(control, startColumn + 1, row);
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

    private static Panel Spacer(int width) => new() { Width = width, Height = 1, Margin = Padding.Empty };

    private static void ConfigurePreviewCombo(ComboBox combo, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Width = width;
        combo.Margin = new Padding(5, 1, 0, 1);
        combo.Anchor = AnchorStyles.Left;
    }

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
        DemoDialogs.ShowInfo(this, palette, metrics);
        footerStatus.Text = $"● 已檢視 {number} / UI Shell";
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
        if (!DemoDialogs.ConfirmDanger(this, palette, metrics, number)) return;
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
