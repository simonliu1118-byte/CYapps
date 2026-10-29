namespace CYERPAutoQuery;

/// <summary>
/// Main window, laid out like CYERPAutoInput's (user, 2026-10-16: the two will become parts
/// of one service and must not look different): toolbar with the action and the ERP status
/// badge, data groups, status strip. The query itself is <see cref="ItemQueryService"/>,
/// which the planned Web entry will call as well.
/// </summary>
internal sealed partial class MainForm : Form
{
    private static readonly Size StandardWindowSize = new(1160, 720);
    private const int ToolbarButtonHeight = 38;

    private readonly AppLogger _log;
    private readonly ItemQueryService _service;
    private AutomationSession? _automationSession;
    /// <summary>Mouse lock and on-screen notice while CY queries ERP (user, 2026-10-16).</summary>
    private AutomationSession Session => _automationSession ??= new AutomationSession(_log);

    private readonly TextBox _code = new() { MaxLength = Math.Max(ItemCodeRules.MaxLength, ItemNameRules.MaxLength) };
    // 品號: one item with all its data; 品名: the list of items whose 品名 contains the text
    // (user, 2026-10-16), from which one is picked and queried by 品號.
    // 客戶代號／客戶名稱 query COPI01 the same way (user, 2026-10-16); 廠商代號／廠商名稱 PURI01
    // (user, 2026-10-17).
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    // 銷貨銷退單: a customer's 銷貨單 and 銷退單 (COPR15/COPR16) by 客戶代號, or by name through the
    // customer list first (user, 2026-10-16: nobody remembers 客戶代號).
    // 未收款 (ACRR18, user 2026-10-17): one customer (code, or name through the customer list), all
    // customers when left empty; 業務未收款: one 業務員's.
    // 進貨單 (PURR18, user 2026-10-18): a supplier's 進貨單, by code or by name like 銷貨銷退單.
    private const int ItemMode = 0, ItemNameMode = 1, CustomerMode = 2, CustomerNameMode = 3, SupplierMode = 4,
        SupplierNameMode = 5, PurchaseMode = 6, SalesMode = 7, ReceivableMode = 8, SalespersonReceivableMode = 9;
    private readonly Label[] _headerLabels = new Label[3];
    private GroupBox? _detailBox;
    /// <summary>Whose list the search tab shows: items (null), customers or suppliers (a double-click then queries that code).</summary>
    private ErpParty? _searchParty;
    private readonly DataGridView _search = new();
    private readonly TabPage _searchPage = new("品名查詢");
    private readonly CyPrimaryButton _start = new();
    private readonly CyStatusBadge _erpStateTag = new();
    private readonly TextBox _itemCode = ReadOnlyBox();
    private readonly TextBox _itemName = ReadOnlyBox();
    private readonly TextBox _specification = ReadOnlyBox();
    private readonly DataGridView _fields = new();
    private readonly DataGridView _warehouses = new();
    private readonly DataGridView _batches = new();
    private readonly DataGridView _history = new();
    private readonly TabControl _details = new();
    private readonly TabPage _warehousePage = new("庫別");
    private readonly TabPage _batchPage = new("批號");
    private readonly TabPage _historyPage = new("歷史異動");
    private readonly TabPage _salesPage = new("銷貨銷退單");
    private readonly DataGridView _orders = new();
    private readonly DataGridView _orderLines = new();
    private SalesQueryResult? _salesResult;
    private readonly TabPage _receivablePage = new("未收款");
    private readonly DataGridView _bills = new();
    private readonly DataGridView _vouchers = new();
    private ReceivableResult? _receivableResult;
    // 查歷史異動 only when asked, for a date range picked here (user, 2026-10-16); an
    // unticked date means 「not filled」: the last year.
    private readonly CheckBox _includeHistory = new() { Text = "查歷史異動", AutoSize = true };
    private readonly DateTimePicker _historyFrom = HistoryPicker();
    private readonly DateTimePicker _historyTo = HistoryPicker();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _dpiHint = new();
    private readonly ToolStripStatusLabel _buildStatus = new();
    private readonly System.Windows.Forms.Timer _statePoll = new() { Interval = 1000 };
    private Invi02Status? _shownState;
    private CancellationTokenSource? _queryCts;
    private readonly ToolStripStatusLabel _agentStatus = new() { Text = "Web：未啟用" };
    private readonly ErpAgentSettings _agentSettings;
    private CancellationTokenSource? _agentCts;
    private Task? _agentTask;
    /// <summary>The running Agent's client, for progress reports of a long job.</summary>
    private ErpAgentClient? _agentClient;
    private ErpAgentState? _agentState;

    public MainForm(AppLogger log)
    {
        _log = log;
        _service = new ItemQueryService(log);
        _agentSettings = ErpAgentSettings.Load(log);
        LoadErpLogin();
        // Same as CYERPAutoInput: the local log keeps what CY read, for troubleshooting.
        _log.Diagnostic = true;

        Text = "CYERPAutoQuery — SMART ERP 自動查詢工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = StandardWindowSize;
        Size = StandardWindowSize;
        Font = new Font("Microsoft JhengHei UI", 9.5F);

        BuildUi();
        _buildStatus.Text = $"{AppVersionInfo.Display} · Esc：緊急停止";

        _statePoll.Tick += (_, _) => RefreshErpState();
        Shown += (_, _) =>
        {
            UpdateDpiHint();
            RefreshErpState();
            _statePoll.Start();
            StartUpdateChecks();
            StartIdleChecks();
            _code.Focus();
            if (_agentSettings.Enabled) StartAgent();
        };
        DpiChanged += (_, _) => UpdateDpiHint();
        FormClosing += (_, _) => { _statePoll.Stop(); StopAgent(); _automationSession?.Dispose(); };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12), Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        Controls.Add(root);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(toolbar, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 2), Margin = Padding.Empty };
        toolbar.Controls.Add(actions, 0, 0);
        actions.Controls.Add(new Label { Text = "查詢：", AutoSize = true, Padding = new Padding(0, 11, 0, 0), Margin = new Padding(4, 0, 2, 0) });
        _mode.Items.AddRange(["品號", "品名", "客戶代號", "客戶名稱", "廠商代號", "廠商名稱", "進貨退貨單", "銷貨銷退單", "未收款", "業務未收款"]);
        _mode.SelectedIndex = 0;
        _mode.Font = new Font(Font.FontFamily, 10.5F);
        _mode.Margin = new Padding(4, 7, 0, 1);
        _mode.SelectedIndexChanged += (_, _) => UpdateHistoryInputs();
        actions.Controls.Add(_mode);
        _code.Font = new Font(Font.FontFamily, 10.5F);
        _code.Width = 220;
        _code.Margin = new Padding(4, 7, 8, 1);
        _code.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await StartQueryAsync();
        };
        actions.Controls.Add(_code);

        _start.Text = "開始查詢 ERP";
        _start.Size = new Size(140, ToolbarButtonHeight);
        _start.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _start.Margin = new Padding(4, 1, 4, 1);
        _start.Click += async (_, _) => await StartQueryAsync();
        actions.Controls.Add(_start);

        var rightTools = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 4, 0, 2), Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        toolbar.Controls.Add(rightTools, 1, 0);
        var web = MakeButton("Web 連線", 120);
        web.Click += (_, _) => OpenAgentSettings();
        rightTools.Controls.Add(web);
        var erpLogin = MakeButton("ERP 登入", 110);
        erpLogin.Click += (_, _) => OpenErpLoginSettings();
        rightTools.Controls.Add(erpLogin);
        _erpStateTag.Size = new Size(150, ToolbarButtonHeight);
        _erpStateTag.Margin = new Padding(4, 1, 12, 1);
        _erpStateTag.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        _erpStateTag.AccessibleName = "ERP 品號資料狀態";
        rightTools.Controls.Add(_erpStateTag);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 2, 0, 0), Margin = Padding.Empty };
        _includeHistory.Margin = new Padding(4, 5, 12, 0);
        _includeHistory.CheckedChanged += (_, _) => UpdateHistoryInputs();
        options.Controls.Add(_includeHistory);
        options.Controls.Add(new Label { Text = "起", AutoSize = true, Margin = new Padding(4, 7, 2, 0) });
        options.Controls.Add(_historyFrom);
        options.Controls.Add(new Label { Text = "迄", AutoSize = true, Margin = new Padding(8, 7, 2, 0) });
        options.Controls.Add(_historyTo);
        options.Controls.Add(new Label
        {
            Text = "未勾的日期不輸入 ERP。批號管理的品號會自動查各批庫存量。",
            AutoSize = true,
            ForeColor = CyVisualTheme.TextSecondary,
            Margin = new Padding(12, 7, 0, 0)
        });
        root.Controls.Add(options, 0, 1);
        UpdateHistoryInputs();

        var headerBox = new GroupBox { Text = "表頭", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(3, 2, 3, 2), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _headerLabels[0] = AddHeaderRow(header, 0, "品號", _itemCode);
        _headerLabels[1] = AddHeaderRow(header, 1, "品名", _itemName);
        _headerLabels[2] = AddHeaderRow(header, 2, "規格", _specification);
        headerBox.Controls.Add(header);
        root.Controls.Add(headerBox, 0, 2);

        var detailBox = _detailBox = new GroupBox { Text = "品號資料（ERP 轉出 Excel 的單頭資料）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
        ConfigureGrid(_fields);
        _fields.Columns.Add("name", "欄位");
        _fields.Columns.Add("value", "內容");
        _fields.Columns[0].Width = 220;
        _fields.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _fields.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        detailBox.Controls.Add(_fields);
        root.Controls.Add(detailBox, 0, 3);

        // 庫別 (INVI02 單身資料), 批號 (INVQ01 查各批庫存量) and 歷史異動 (INVQ01 查歷史異動).
        _details.Dock = DockStyle.Fill;
        _details.Margin = new Padding(0, 4, 0, 4);
        foreach (var (page, grid) in new[] { (_warehousePage, _warehouses), (_batchPage, _batches), (_historyPage, _history), (_searchPage, _search) })
        {
            ConfigureGrid(grid);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            page.Padding = new Padding(4);
            page.Controls.Add(grid);
            _details.TabPages.Add(page);
        }
        // 銷貨單: the orders above, the picked order's lines below.
        var salesSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        foreach (var grid in new[] { _orders, _orderLines })
        {
            ConfigureGrid(grid);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        }
        _orders.MultiSelect = false;
        _orders.SelectionChanged += (_, _) => ShowOrderLines();
        salesSplit.Panel1.Controls.Add(_orders);
        salesSplit.Panel2.Controls.Add(_orderLines);
        _salesPage.Padding = new Padding(4);
        _salesPage.Controls.Add(salesSplit);
        _details.TabPages.Add(_salesPage);
        AddPurchasePage();
        // 未收款: the 結帳單 above, the picked one's 憑證 below.
        var receivableSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        foreach (var grid in new[] { _bills, _vouchers })
        {
            ConfigureGrid(grid);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        }
        _bills.MultiSelect = false;
        _bills.SelectionChanged += (_, _) => ShowVouchers();
        _vouchers.CellDoubleClick += async (_, e) => await ShowOrderWindowAsync(e.RowIndex);
        receivableSplit.Panel1.Controls.Add(_bills);
        receivableSplit.Panel2.Controls.Add(_vouchers);
        _receivablePage.Padding = new Padding(4);
        _receivablePage.Controls.Add(receivableSplit);
        _details.TabPages.Add(_receivablePage);
        root.Controls.Add(_details, 0, 4);
        _search.CellDoubleClick += async (_, e) => await QueryPickedItemAsync(e.RowIndex);

        var statusStrip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false, Font = new Font("Microsoft JhengHei UI", 8.5F), Padding = new Padding(2, 0, 2, 0), Margin = Padding.Empty };
        _status.Text = "請先在 ERP 開啟「品號資料建立作業」，輸入品號後按「開始查詢 ERP」。";
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_agentStatus);
        statusStrip.Items.Add(_dpiHint);
        statusStrip.Items.Add(_buildStatus);
        root.Controls.Add(statusStrip, 0, 5);

        CyVisualTheme.Apply(this);
    }

    private static DateTimePicker HistoryPicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy/MM/dd",
        ShowCheckBox = true,
        Checked = false,
        Width = 140,
        Margin = new Padding(0, 3, 0, 0)
    };

    private bool NameMode => _mode.SelectedIndex == ItemNameMode;

    private void UpdateHistoryInputs()
    {
        // History belongs to one item; a 品名 search only lists items. 銷貨單 uses the same two
        // dates as its 單據日期 range.
        var itemQuery = _mode.SelectedIndex == ItemMode;
        var sales = _mode.SelectedIndex is SalesMode or PurchaseMode;
        _includeHistory.Enabled = itemQuery;
        _historyFrom.Enabled = (_includeHistory.Checked && itemQuery) || sales;
        _historyTo.Enabled = (_includeHistory.Checked && itemQuery) || sales;
    }

    private static DateOnly? Pick(DateTimePicker p) => p.Checked ? DateOnly.FromDateTime(p.Value) : null;

    /// <summary>The options of a local query; a bad date range throws ArgumentException with the message to show.</summary>
    private ItemQueryOptions LocalOptions()
    {
        if (!_includeHistory.Checked) return ItemQueryOptions.Default;
        return new ItemQueryOptions(HistoryRange.Resolve(Pick(_historyFrom), Pick(_historyTo), DateOnly.FromDateTime(DateTime.Today)));
    }

    private static TextBox ReadOnlyBox() => new() { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };

    private static Label AddHeaderRow(TableLayoutPanel table, int row, string label, TextBox box)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(box, 1, row);
        return caption;
    }

    /// <summary>The header and detail captions follow what is shown: an item (null), a customer or a supplier.</summary>
    private void SetHeaderCaptions(ErpParty? party)
    {
        string[] captions = party is null ? ["品號", "品名", "規格"] : [party.CodeName, party.ShortLabel, party.FullLabel];
        for (var i = 0; i < 3; i++) _headerLabels[i].Text = captions[i];
        if (_detailBox is not null)
            _detailBox.Text = $"{(party is null ? "品號" : party.Kind)}資料（ERP 轉出 Excel 的單頭資料）";
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
    }

    private Button MakeButton(string text, int width) => new CyButton
    {
        Text = text,
        Font = new Font(Font.FontFamily, 10.5F),
        Size = new Size(width, ToolbarButtonHeight),
        Margin = new Padding(4, 1, 4, 1)
    };

    private void SetStatus(string text) => _status.Text = text;

    /// <summary>The 開始查詢 button / Enter: one local query, failures shown in a message box.</summary>
    private async Task StartQueryAsync()
    {
        if (_queryCts is not null) return;
        switch (_mode.SelectedIndex)
        {
            case ItemNameMode:
                await StartSearchAsync(null);
                return;
            case CustomerNameMode:
                await StartSearchAsync(ErpParty.Customer);
                return;
            case SupplierNameMode:
                await StartSearchAsync(ErpParty.Supplier);
                return;
            case CustomerMode:
                await StartPartyQueryAsync(ErpParty.Customer);
                return;
            case SupplierMode:
                await StartPartyQueryAsync(ErpParty.Supplier);
                return;
            case ReceivableMode:
                // Empty: every customer. A name lists the customers first (one match is queried at once).
                if (_code.Text.Trim().Length == 0)
                    await StartReceivableQueryAsync(ReceivableScope.All);
                else if (!ItemCodeRules.TryNormalize(_code.Text, out _, out _, ErpParty.Customer.CodeName) &&
                         ItemNameRules.TryNormalize(_code.Text, out _, out _))
                {
                    if (await StartSearchAsync(ErpParty.Customer) is { Items.Count: 1 } match &&
                        ItemSearchResult.Value(match.Items[0], "客戶代號") is { Length: > 0 } matched)
                    {
                        _code.Text = matched;
                        await StartReceivableQueryAsync(ReceivableScope.Customer);
                    }
                }
                else
                    await StartReceivableQueryAsync(ReceivableScope.Customer);
                return;
            case SalespersonReceivableMode:
                await StartReceivableQueryAsync(ReceivableScope.Salesperson);
                return;
            case PurchaseMode:
                // As 銷貨銷退單: a name lists the suppliers first, one match is queried at once.
                if (!ItemCodeRules.TryNormalize(_code.Text, out _, out _, ErpParty.Supplier.CodeName) &&
                    ItemNameRules.TryNormalize(_code.Text, out _, out _))
                {
                    if (await StartSearchAsync(ErpParty.Supplier) is { Items.Count: 1 } supplier &&
                        ItemSearchResult.Value(supplier.Items[0], ErpParty.Supplier.CodeName) is { Length: > 0 } supplierCode)
                    {
                        _code.Text = supplierCode;
                        await StartPurchaseQueryAsync();
                    }
                }
                else
                    await StartPurchaseQueryAsync();
                return;
            case SalesMode:
                // A name instead of a 客戶代號 lists the customers first; a double-click then queries
                // that customer's 銷貨單.
                if (!ItemCodeRules.TryNormalize(_code.Text, out _, out _, ErpParty.Customer.CodeName) &&
                    ItemNameRules.TryNormalize(_code.Text, out _, out _))
                {
                    // Only one customer matches: query that one at once (user, 2026-10-17).
                    if (await StartSearchAsync(ErpParty.Customer) is { Items.Count: 1 } one &&
                        ItemSearchResult.Value(one.Items[0], "客戶代號") is { Length: > 0 } only)
                    {
                        _log.Info("sales", "one customer matched; querying it");
                        _code.Text = only;
                        await StartSalesQueryAsync();
                    }
                }
                else
                    await StartSalesQueryAsync();
                return;
        }
        if (!ItemCodeRules.TryNormalize(_code.Text, out var code, out var error))
        {
            MessageBox.Show(this, error, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ItemQueryOptions options;
        try
        {
            options = LocalOptions();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await ExecuteQueryAsync(code, options, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Status already says so.
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 自動查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>客戶代號／廠商代號 mode: one customer (COPI01) or supplier (PURI01); failures shown in a message box.</summary>
    private async Task StartPartyQueryAsync(ErpParty party)
    {
        if (!ItemCodeRules.TryNormalize(_code.Text, out var code, out var error, party.CodeName))
        {
            MessageBox.Show(this, error, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await ExecutePartyAsync(party, code, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, $"ERP {party.Kind}查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>One customer or supplier, shown; local and Web queries both run here. Failures are rethrown.</summary>
    private async Task<PartyQueryResult> ExecutePartyAsync(ErpParty party, string code, CancellationToken external)
    {
        ShowResult(null);
        ShowParty(party, null);
        return await RunAutomationAsync(external, (progress, ct) => _service.QueryPartyAsync(party, code, progress, ct), result =>
        {
            if (!result.Found)
            {
                SetStatus($"ERP：查無{party.CodeName} {code}");
                return AutomationOutcome.NotFound;
            }
            ShowParty(party, result);
            SetStatus($"ERP：{party.Kind} {result.Code} 查詢完成，共 {result.Fields.Count} 欄（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    /// <summary>銷貨銷退單 mode: a customer's 銷貨單 and 銷退單 in the 單據日期 range; failures shown in a message box.</summary>
    private async Task StartSalesQueryAsync()
    {
        SalesQuery query;
        try
        {
            query = SalesQuery.Resolve(_code.Text, Pick(_historyFrom), Pick(_historyTo), DateOnly.FromDateTime(DateTime.Today));
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await ExecuteSalesAsync(query, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 銷貨銷退單查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>A customer's 銷貨單 and 銷退單 (COPR15＋COPR16), shown; local and Web. Failures are rethrown.</summary>
    private async Task<SalesQueryResult> ExecuteSalesAsync(SalesQuery query, CancellationToken external)
    {
        ShowSales(null);
        return await RunAutomationAsync(external, (progress, ct) => _service.QuerySalesAsync(query, progress, ct), result =>
        {
            ShowSales(result);
            _details.SelectedTab = _salesPage;
            var range = $"{Invq01Layout.ErpDate(query.From)}～{Invq01Layout.ErpDate(query.To)}";
            if (!result.Found)
            {
                SetStatus($"ERP：客戶 {query.CustomerCode} 在 {range} 查無銷貨單與銷退單");
                return AutomationOutcome.NotFound;
            }
            SetStatus($"ERP：客戶 {query.CustomerCode} {range} 銷貨 {result.Count(SalesReport.Sales.Kind)} 張、銷退 {result.Count(SalesReport.Returns.Kind)} 張（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    /// <summary>未收款 modes: one customer, one 業務員 or everyone; failures shown in a message box.</summary>
    private async Task StartReceivableQueryAsync(ReceivableScope scope)
    {
        ReceivableQuery query;
        try
        {
            query = ReceivableQuery.Resolve(scope, _code.Text);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await ExecuteReceivablesAsync(query, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 未收款查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>未收款 (ACRR18), shown; local and Web. Failures are rethrown.</summary>
    private async Task<ReceivableResult> ExecuteReceivablesAsync(ReceivableQuery query, CancellationToken external)
    {
        ShowReceivables(null);
        return await RunAutomationAsync(external, (progress, ct) => _service.QueryReceivablesAsync(query, progress, ct), result =>
        {
            ShowReceivables(result);
            _details.SelectedTab = _receivablePage;
            if (!result.Found)
            {
                SetStatus($"ERP：{query.Describe()}沒有未收款");
                return AutomationOutcome.NotFound;
            }
            SetStatus($"ERP：{query.Describe()}未收款 {result.Bills.Count} 張結帳單、{result.CustomerCount} 位客戶，未收合計 {Num(result.TotalUnpaid)}（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    /// <summary>
    /// Double-click on a 憑證: that 銷貨單／銷退單 in its own window (user, 2026-10-17), so the 未收款
    /// list stays as it is. Only 23x／24x numbers are orders; others are refused before ERP is touched.
    /// </summary>
    private async Task ShowOrderWindowAsync(int rowIndex)
    {
        if (_queryCts is not null || rowIndex < 0 || rowIndex >= _vouchers.Rows.Count) return;
        var voucherNo = Convert.ToString(_vouchers.Rows[rowIndex].Cells[0].Value) ?? string.Empty;
        SalesQuery query;
        try
        {
            query = SalesQuery.ForOrder(voucherNo);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "查單筆憑證", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var result = await RunAutomationAsync(CancellationToken.None, (progress, ct) => _service.QueryOrderAsync(query, progress, ct), r =>
            {
                SetStatus(r.Found ? $"ERP：單號 {query.OrderNo} 查詢完成（{r.Elapsed.TotalSeconds:0.0} 秒）" : $"ERP：查無單號 {query.OrderNo}");
                return r.Found ? AutomationOutcome.Done : AutomationOutcome.NotFound;
            });
            Activate();
            if (result.Orders.FirstOrDefault() is { } order)
                new OrderDetailForm(order).Show(this);
            else
                MessageBox.Show(this, $"ERP 查無單號 {query.OrderNo}。", "查單筆憑證", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 單筆憑證查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static readonly string[] BillColumns = ["客戶代號", "客戶名稱", "日期", "結帳單號", "業務員", "發票號碼", "幣別", "應收帳款", "應收稅額", "未收帳款", "憑證數"];
    private static readonly string[] VoucherColumns = ["憑證號碼", "憑證日期", "未稅金額", "稅額"];

    private void ShowReceivables(ReceivableResult? result)
    {
        _receivableResult = result;
        SetHeaderCaptions(ErpParty.Customer);
        _headerLabels[2].Text = "未收合計";
        if (_detailBox is not null) _detailBox.Text = "結帳單（點選下方結帳單）";
        var single = result?.Query.Scope == ReceivableScope.Customer;
        _itemCode.Text = result is null ? string.Empty : single ? result.Query.Code : result.Query.Describe();
        _itemName.Text = single ? result!.Bills.FirstOrDefault()?.CustomerName ?? string.Empty
            : result is null ? string.Empty : $"{result.CustomerCount} 位客戶";
        _specification.Text = result is null ? string.Empty : $"{Num(result.TotalUnpaid)}（{result.Bills.Count} 張結帳單）";
        _fields.Rows.Clear();
        _bills.Rows.Clear();
        _bills.Columns.Clear();
        foreach (var c in BillColumns) _bills.Columns.Add(c, c);
        if (result is not null)
            foreach (var b in result.Bills)
                _bills.Rows.Add(b.CustomerCode, b.CustomerName, b.Date.ToString("yyyy/MM/dd"), b.BillNo, $"{b.SalespersonCode} {b.SalespersonName}".Trim(),
                    b.InvoiceNo, b.Currency, Num(b.Receivable), Num(b.ReceivableTax), Num(b.Unpaid), b.Vouchers.Count);
        _receivablePage.Text = result is null ? "未收款" : $"未收款（{result.Bills.Count}）";
        ShowVouchers();
    }

    private void ShowVouchers()
    {
        _vouchers.Rows.Clear();
        if (_vouchers.Columns.Count == 0)
            foreach (var c in VoucherColumns) _vouchers.Columns.Add(c, c);
        var index = _bills.CurrentRow?.Index ?? -1;
        if (_receivableResult is null || index < 0 || index >= _receivableResult.Bills.Count) return;
        var b = _receivableResult.Bills[index];
        _fields.Rows.Clear();
        foreach (var (name, value) in new[]
        {
            ("客戶", $"{b.CustomerCode} {b.CustomerName}"), ("結帳單號", b.BillNo), ("日期", b.Date.ToString("yyyy/MM/dd")),
            ("業務員", $"{b.SalespersonCode} {b.SalespersonName}".Trim()), ("發票號碼", b.InvoiceNo), ("幣別", b.Currency),
            ("應收帳款", Num(b.Receivable)), ("應收稅額", Num(b.ReceivableTax)), ("未收帳款", Num(b.Unpaid)),
        })
            if (value.Length > 0) _fields.Rows.Add(name, value);
        foreach (var v in b.Vouchers)
            _vouchers.Rows.Add(v.VoucherNo, v.Date?.ToString("yyyy/MM/dd") ?? string.Empty, Num(v.Amount), Num(v.Tax));
    }

    private static readonly string[] OrderColumns = ["類別", "單據日期", "單號", "品項", "總數量", "金額", "稅額", "合計", "業務員", "備註", "銷退原因"];
    private static readonly string[] LineColumns = ["序號", "品號", "品名", "規格", "數量", "單位", "單價", "金額", "類型", "贈/備品量", "庫別", "批號", "備註", "原銷貨單"];

    private static string Num(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The orders newest first (the 銷退單 ↔ 銷貨單 links are CY Web's, user 2026-10-17).</summary>
    private void ShowSales(SalesQueryResult? result)
    {
        _salesResult = result;
        SetHeaderCaptions(ErpParty.Customer);
        _headerLabels[2].Text = "查詢範圍";
        if (_detailBox is not null) _detailBox.Text = "單頭（點選下方單據）";
        _itemCode.Text = result?.Query.CustomerCode ?? string.Empty;
        _itemName.Text = result?.Orders.FirstOrDefault()?.CustomerName ?? string.Empty;
        _specification.Text = result is null ? string.Empty
            : $"單據日期 {Invq01Layout.ErpDate(result.Query.From)}～{Invq01Layout.ErpDate(result.Query.To)}：銷貨 {result.Count(SalesReport.Sales.Kind)} 張、銷退 {result.Count(SalesReport.Returns.Kind)} 張";
        _fields.Rows.Clear();
        _orders.Rows.Clear();
        _orders.Columns.Clear();
        foreach (var c in OrderColumns) _orders.Columns.Add(c, c);
        if (result is not null)
            foreach (var o in result.Orders)
                _orders.Rows.Add(o.Kind, o.Date.ToString("yyyy/MM/dd"), o.OrderNo, o.Lines.Count, Num(o.TotalQuantity),
                    Num(o.Amount), Num(o.Tax), Num(o.Total), o.Salesperson, o.Note, o.Reason);
        _salesPage.Text = result is null ? "銷貨銷退單" : $"銷貨銷退單（{result.Orders.Count}）";
        ShowOrderLines();
    }

    /// <summary>The picked order: its 單頭 in the detail grid, its lines below the order list.</summary>
    private void ShowOrderLines()
    {
        _orderLines.Rows.Clear();
        if (_orderLines.Columns.Count == 0)
            foreach (var c in LineColumns) _orderLines.Columns.Add(c, c);
        var index = _orders.CurrentRow?.Index ?? -1;
        if (_salesResult is null || index < 0 || index >= _salesResult.Orders.Count) return;
        var o = _salesResult.Orders[index];
        _fields.Rows.Clear();
        foreach (var (name, value) in new[]
        {
            ("類別", o.Kind), ("單號", o.OrderNo), ("單據日期", o.Date.ToString("yyyy/MM/dd")),
            ("客戶", $"{o.CustomerCode} {o.CustomerName}"), ("業務員", o.Salesperson), ("部門", o.Department),
            ("付款條件", o.PaymentTerm), ("課稅別", o.TaxType), ("發票聯數", o.InvoiceType),
            ("總數量", Num(o.TotalQuantity)), ("金額", Num(o.Amount)), ("稅額", Num(o.Tax)), ("合計", Num(o.Total)),
            ("備註", o.Note), ("銷退原因", o.Reason),
        })
            if (value.Length > 0) _fields.Rows.Add(name, value);
        foreach (var l in o.Lines)
            _orderLines.Rows.Add(l.Seq, l.ItemCode, l.ItemName, l.Specification, Num(l.Quantity), l.Unit, Num(l.UnitPrice),
                Num(l.Amount), l.GiftType, Num(l.GiftQuantity), l.Warehouse, l.Batch, l.Note, l.SourceOrder);
    }

    private void ShowParty(ErpParty party, PartyQueryResult? result)
    {
        SetHeaderCaptions(party);
        _itemCode.Text = result?.Code ?? string.Empty;
        _itemName.Text = result?.Value(party.ShortLabel) ?? string.Empty;
        _specification.Text = result?.Value(party.FullLabel) ?? string.Empty;
        _fields.Rows.Clear();
        if (result is not null)
            foreach (var f in result.Fields)
                _fields.Rows.Add(f.Name, f.Value);
    }

    /// <summary>品名／客戶名稱／廠商名稱 mode: lists the matches on the search tab; failures shown in a message box.</summary>
    private async Task<ItemSearchResult?> StartSearchAsync(ErpParty? party)
    {
        if (!ItemNameRules.TryNormalize(_code.Text, out var keyword, out var error))
        {
            MessageBox.Show(this, error, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        ItemSearchResult? result = null;
        try
        {
            result = await ExecuteSearchAsync(keyword, CancellationToken.None, party);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, party is null ? "ERP 品名查詢失敗" : $"ERP {party.Kind}名稱查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
        return result;
    }

    /// <summary>Double-click on a search row: that item (品號), customer or supplier (代號) is queried (user, 2026-10-16).</summary>
    private async Task QueryPickedItemAsync(int rowIndex)
    {
        if (_queryCts is not null || rowIndex < 0 || rowIndex >= _search.Rows.Count) return;
        var column = _searchParty?.CodeName ?? "品號";
        var code = Convert.ToString(_search.Rows[rowIndex].Cells[column].Value) ?? string.Empty;
        if (code.Length == 0) return;
        // A customer picked for 銷貨銷退單 or 未收款, or a supplier for 進貨單, stays in that mode.
        if (!(_searchParty == ErpParty.Customer && _mode.SelectedIndex is SalesMode or ReceivableMode) &&
            !(_searchParty == ErpParty.Supplier && _mode.SelectedIndex == PurchaseMode))
            _mode.SelectedIndex = _searchParty == ErpParty.Customer ? CustomerMode : _searchParty == ErpParty.Supplier ? SupplierMode : ItemMode;
        _code.Text = code;
        await StartQueryAsync();
    }

    private async Task<ItemSearchResult> ExecuteSearchAsync(string keyword, CancellationToken external, ErpParty? party = null)
    {
        ShowSearch(null, party);
        var what = party is null ? "品名" : $"{party.Kind}簡稱或全名";
        return await RunAutomationAsync(external, (progress, ct) => party is null
            ? _service.SearchByNameAsync(keyword, progress, ct)
            : _service.SearchPartiesAsync(party, keyword, progress, ct), result =>
        {
            ShowSearch(result, party);
            _details.SelectedTab = _searchPage;
            if (result.Items.Count == 0)
            {
                SetStatus($"ERP：{what}含「{keyword}」查無資料");
                return AutomationOutcome.NotFound;
            }
            var more = result.Truncated ? $"（已達 ERP 筆數上限 {result.Limit}，可能還有更多，請加長關鍵字）" : string.Empty;
            SetStatus($"ERP：{what}含「{keyword}」共 {result.Items.Count} 筆{more}，雙擊一筆查詢明細（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    private void ShowSearch(ItemSearchResult? result, ErpParty? party)
    {
        _searchParty = party;
        var labels = party is null
            ? ItemFieldCatalog.SearchList.Select(c => c.Label).ToList()
            : party.SearchList.Select(f => f.Label).ToList();
        _search.Rows.Clear();
        _search.Columns.Clear();
        foreach (var label in labels) _search.Columns.Add(label, label);
        if (result is not null)
            foreach (var row in result.Items)
                _search.Rows.Add(labels.Select(l => (object)ItemSearchResult.Value(row, l)).ToArray());
        var title = party is null ? "品名查詢" : $"{party.Kind}查詢";
        _searchPage.Text = result is null ? title : $"{title}（{result.Items.Count}{(result.Truncated ? "+" : string.Empty)}）";
    }

    /// <summary>A 品名 search received from CY Web: same path as the local one, the list goes back to Web.</summary>
    private async Task<ItemSearchResult> RunWebSearchAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        if (!ItemNameRules.TryNormalize(job.Keyword, out var keyword, out var error))
            throw new ArgumentException(error);
        _mode.SelectedIndex = ItemNameMode;
        _code.Text = keyword;
        _log.Info("agent", $"web 品名 search keyword={_log.Value(keyword)}");
        return await ExecuteSearchAsync(keyword, cancellationToken);
    }

    /// <summary>
    /// A customer, supplier, 銷貨銷退 or 未收款 job from CY Web (user, 2026-10-17): the same path as the local
    /// query, shown in the window; the complete body goes back to Web.
    /// </summary>
    private async Task<(bool Found, string Body)> RunWebBusinessAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        var version = AppVersionInfo.Display;
        _log.Info("agent", $"web {job.QueryType} job");
        switch (job.QueryType)
        {
            case ErpAgentJob.CustomerQuery or ErpAgentJob.SupplierQuery:
            {
                var party = job.Party;
                if (!ItemCodeRules.TryNormalize(job.PartyCode ?? string.Empty, out var code, out var error, party.CodeName))
                    throw new ArgumentException(error);
                _mode.SelectedIndex = party == ErpParty.Customer ? CustomerMode : SupplierMode;
                _code.Text = code;
                var result = await ExecutePartyAsync(party, code, cancellationToken);
                return (result.Found, ErpAgentProtocol.PartyBody(job, result, version));
            }
            case ErpAgentJob.CustomerSearch or ErpAgentJob.SupplierSearch:
            {
                if (!ItemNameRules.TryNormalize(job.Keyword, out var keyword, out var error))
                    throw new ArgumentException(error);
                var party = job.Party;
                _mode.SelectedIndex = party == ErpParty.Customer ? CustomerNameMode : SupplierNameMode;
                _code.Text = keyword;
                var result = await ExecuteSearchAsync(keyword, cancellationToken, party);
                return (result.Items.Count > 0, ErpAgentProtocol.SearchBody(result, version));
            }
            case ErpAgentJob.SalesQueryType:
            {
                var query = job.Sales(DateOnly.FromDateTime(DateTime.Today));
                _mode.SelectedIndex = SalesMode;
                _code.Text = query.CustomerCode;
                var result = await ExecuteSalesAsync(query, cancellationToken);
                return (result.Found, ErpAgentProtocol.SalesBody(job, result, version));
            }
            case ErpAgentJob.PurchaseQueryType:
            {
                var query = job.Purchases(DateOnly.FromDateTime(DateTime.Today));
                _mode.SelectedIndex = PurchaseMode;
                _code.Text = query.SupplierCode;
                var result = await ExecutePurchasesAsync(query, cancellationToken);
                return (result.Found, ErpAgentProtocol.PurchaseBody(job, result, version));
            }
            case ErpAgentJob.OrderQueryType:
            {
                var query = SalesQuery.ForOrder(job.OrderNo ?? string.Empty);
                var result = await RunAutomationAsync(cancellationToken, (progress, ct) => _service.QueryOrderAsync(query, progress, ct), r =>
                {
                    SetStatus(r.Found ? $"ERP：單號 {query.OrderNo} 查詢完成（{r.Elapsed.TotalSeconds:0.0} 秒）" : $"ERP：查無單號 {query.OrderNo}");
                    return r.Found ? AutomationOutcome.Done : AutomationOutcome.NotFound;
                });
                return (result.Found, ErpAgentProtocol.SalesBody(job, result, version));
            }
            case ErpAgentJob.RestockQueryType:
            {
                var query = job.Restock(DateOnly.FromDateTime(DateTime.Today));
                var report = WebProgress(job.Id, cancellationToken);
                var result = await RunAutomationAsync(cancellationToken, (progress, ct) => _service.QueryRestockAsync(query, progress, ct, report), r =>
                {
                    SetStatus($"ERP：進貨輔助 {query.From:yyyy/MM/dd}～{query.To:yyyy/MM/dd} 完成，{r.Items.Count} 個品號（{r.Elapsed.TotalSeconds:0} 秒）");
                    return r.Found ? AutomationOutcome.Done : AutomationOutcome.NotFound;
                });
                return (result.Found, ErpAgentProtocol.RestockBody(job, result, version));
            }
            case ErpAgentJob.ReceivableQueryType:
            {
                var query = job.Receivable();
                _mode.SelectedIndex = query.Scope == ReceivableScope.Salesperson ? SalespersonReceivableMode : ReceivableMode;
                _code.Text = query.Code;
                var result = await ExecuteReceivablesAsync(query, cancellationToken);
                return (result.Found, ErpAgentProtocol.ReceivableBody(job, result, version));
            }
            default:
                throw new NotSupportedException($"不支援的查詢類型 {job.QueryType}。");
        }
    }

    /// <summary>
    /// Tells CY Web how far a long job is (進貨輔助, user 2026-10-20): at most every 2 seconds, and
    /// always the first and last step. Best effort: a failed report is logged once and the job goes on.
    /// </summary>
    private Action<string, int, int> WebProgress(string jobId, CancellationToken cancellationToken)
    {
        var last = DateTime.MinValue;
        var warned = false;
        return (stage, done, total) =>
        {
            if (_agentClient is not { } client) return;
            var now = DateTime.UtcNow;
            if (done != 0 && done != total && now - last < TimeSpan.FromSeconds(2)) return;
            last = now;
            _ = SendAsync();
            async Task SendAsync()
            {
                try { await client.ProgressAsync(jobId, ErpAgentProtocol.ProgressBody(stage, done, total), cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    if (warned) return;
                    warned = true;
                    _log.Warn("agent", $"progress not sent: {ex.GetType().Name}: {ex.Message}");
                }
            }
        };
    }

    /// <summary>A query received from CY Web: same path as the local one, no message box (the outcome goes back to Web).</summary>
    private async Task<ItemQueryResult> RunWebQueryAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        _mode.SelectedIndex = 0;
        _code.Text = job.ItemNo;
        var options = job.Options(DateOnly.FromDateTime(DateTime.Today));
        _log.Info("agent", $"web query item={_log.Value(job.ItemNo)} history={(options.History is { } h ? $"{HistoryRange.Iso(h.From)}~{HistoryRange.Iso(h.To)}" : "no")}");
        return await ExecuteQueryAsync(job.ItemNo, options, cancellationToken);
    }

    /// <summary>
    /// The single query runner for local and Web queries: locks the physical mouse and shows
    /// 「自動查詢中」 (same as CYERPAutoInput), shows the result, and rethrows failures to the caller.
    /// </summary>
    private async Task<ItemQueryResult> ExecuteQueryAsync(string code, ItemQueryOptions options, CancellationToken external)
    {
        ShowResult(null);
        return await RunAutomationAsync(external, (progress, ct) => _service.QueryAsync(code, options, progress, ct), result =>
        {
            if (!result.Found)
            {
                SetStatus($"ERP：查無品號 {code}");
                return AutomationOutcome.NotFound;
            }
            ShowResult(result);
            var extra = (result.BatchManaged ? $"，批號 {result.Batches.Count} 筆" : string.Empty) +
                        (result.HistoryRange is not null ? $"，歷史異動 {result.History.Count} 筆" : string.Empty);
            SetStatus($"ERP：品號 {result.ItemCode} 查詢完成，共 {result.Fields.Count} 欄{extra}（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    /// <summary>
    /// The single runner for every ERP automation (品號 query, 品名 search; local or Web): locks
    /// the physical mouse and shows 「自動查詢中」 (same as CYERPAutoInput), lets
    /// <paramref name="show"/> display the result, and rethrows failures to the caller.
    /// </summary>
    private async Task<T> RunAutomationAsync<T>(CancellationToken external,
        Func<IProgress<string>, CancellationToken, Task<T>> run, Func<T, AutomationOutcome> show)
    {
        _queryCts = CancellationTokenSource.CreateLinkedTokenSource(external);
        _start.Enabled = false;
        _statePoll.Stop();
        var progress = new Progress<string>(SetStatus);
        var outcome = AutomationOutcome.Stopped;
        try
        {
            Session.Begin(AutomationSession.QueryTitle);
            using var escape = Session.EscapeToken.Register(() => { try { _queryCts?.Cancel(); } catch (ObjectDisposedException) { } });
            var result = await run(progress, _queryCts.Token);
            outcome = show(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = AutomationOutcome.Cancelled;
            _log.Info("query", "cancelled");
            SetStatus("ERP：已按 Esc 緊急停止");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error("query", ex);
            SetStatus("ERP：查詢失敗");
            throw;
        }
        finally
        {
            // The mouse comes back before any message box is shown.
            Session.End(outcome);
            _lastErpUse = DateTime.Now;
            _queryCts.Dispose();
            _queryCts = null;
            _start.Enabled = true;
            _statePoll.Start();
        }
    }

    private void ShowResult(ItemQueryResult? result)
    {
        SetHeaderCaptions(null);
        _itemCode.Text = result?.ItemCode ?? string.Empty;
        _itemName.Text = result?.ItemName ?? string.Empty;
        _specification.Text = result?.Specification ?? string.Empty;
        _fields.Rows.Clear();
        if (result is not null)
            foreach (var f in result.Fields)
                _fields.Rows.Add(f.Name, f.Value);

        FillTable(_warehouses, result?.Warehouses ?? [], ItemFieldCatalog.Warehouse);
        FillTable(_batches, result?.Batches ?? [], ItemFieldCatalog.Batch);
        FillTable(_history, result?.History ?? [], ItemFieldCatalog.History);
        _warehousePage.Text = result is null ? "庫別" : $"庫別（{result.Warehouses.Count}）";
        _batchPage.Text = result is null ? "批號" : result.BatchManaged ? $"批號（{result.Batches.Count}）" : "批號（非批號管理）";
        _historyPage.Text = result?.HistoryRange is { } range
            ? $"歷史異動（{Invq01Layout.ErpDate(range.From)}～{Invq01Layout.ErpDate(range.To)}，{result.History.Count}）"
            : "歷史異動（未查）";
    }

    private static void FillTable(DataGridView grid, IReadOnlyList<IReadOnlyList<ErpField>> rows, IReadOnlyList<CatalogField> catalog)
    {
        grid.Rows.Clear();
        grid.Columns.Clear();
        foreach (var c in catalog)
            grid.Columns.Add(c.Label, c.Label);
        foreach (var row in rows)
            grid.Rows.Add(catalog.Select(c => (object)(row.FirstOrDefault(f => f.Name == c.Label)?.Value ?? string.Empty)).ToArray());
    }

    private void RefreshErpState()
    {
        var state = _service.ProbeStatus();
        if (state == _shownState) return;
        _shownState = state;
        var (text, fore, back) = state switch
        {
            Invi02Status.Browse => ("檢視", CyVisualTheme.Info, CyVisualTheme.InfoSoft),
            Invi02Status.Editing => ("新增/修改中", CyVisualTheme.Danger, CyVisualTheme.DangerSoft),
            Invi02Status.DialogOpen => ("對話框開啟", CyVisualTheme.Warning, CyVisualTheme.WarningSoft),
            Invi02Status.Multiple => ("多個 INVI02", CyVisualTheme.Warning, CyVisualTheme.WarningSoft),
            Invi02Status.NotOpen => ("未開啟", CyVisualTheme.TextSecondary, CyVisualTheme.ReadOnly),
            _ => ("無法判斷", CyVisualTheme.TextSecondary, CyVisualTheme.ReadOnly)
        };
        _erpStateTag.SetState($"ERP：{text}", fore, back, CyVisualTheme.Blend(fore, back, 0.35));
        _log.Info("state", $"status tag {state}");
    }

    // ---- CY Web Agent ----------------------------------------------------------------

    private void OpenAgentSettings()
    {
        using var form = new ErpAgentSettingsForm(_agentSettings);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _agentSettings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Web 連線設定儲存失敗：{ex.Message}", "Web 連線", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _log.Info("agent", $"settings saved enabled={_agentSettings.Enabled} token={_agentSettings.HasToken}");
        StopAgent();
        if (_agentSettings.Enabled) StartAgent();
        else SetAgentStatus(ErpAgentState.Stopped, "Web：未啟用");
    }

    private void StartAgent()
    {
        if (_agentTask is not null) return;
        ErpAgentClient client;
        try
        {
            client = new ErpAgentClient(new Uri(_agentSettings.BaseUrl), _agentSettings.ReadToken());
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            _log.Warn("agent", $"agent not started: {ex.GetType().Name}");
            SetAgentStatus(ErpAgentState.Unauthorized, "Web：設定無法使用，請重新設定網址與 Token");
            return;
        }

        _agentCts = new CancellationTokenSource();
        var loop = new ErpAgentLoop(
            client,
            AppVersionInfo.Display,
            () => ErpAgentProtocol.ErpState(_service.ProbeStatus()),
            () => _queryCts is not null,
            RunWebQueryAsync,
            SetAgentStatus,
            runSearch: RunWebSearchAsync,
            runBusiness: RunWebBusinessAsync,
            runControl: RunWebControlAsync,
            holdChanged: SetErpHold,
            runStocktake: RunWebStocktakeAsync);
        _agentClient = client;
        _agentTask = RunAgentAsync(loop, client, _agentCts.Token);
    }

    private async Task RunAgentAsync(ErpAgentLoop loop, ErpAgentClient client, CancellationToken cancellationToken)
    {
        try
        {
            await loop.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetAgentStatus(ErpAgentState.Stopped, "Web：已停止");
        }
        catch (Exception ex)
        {
            _log.Error("agent", ex);
            SetAgentStatus(ErpAgentState.Offline, "Web：連線程式發生錯誤，已停止");
        }
        finally
        {
            if (_agentClient == client) _agentClient = null;
            client.Dispose();
            _agentTask = null;
        }
    }

    private void StopAgent()
    {
        _agentCts?.Cancel();
        _agentCts?.Dispose();
        _agentCts = null;
    }

    /// <summary>The last problem text logged, so a long outage (retrying every 30 s) logs it once.</summary>
    private string? _agentWarned;

    private void SetAgentStatus(ErpAgentState state, string text)
    {
        _agentStatus.Text = text.StartsWith("Web", StringComparison.Ordinal) ? text : $"Web：{text}";
        _agentStatus.ForeColor = state switch
        {
            ErpAgentState.Online or ErpAgentState.Querying => CyVisualTheme.Success,
            ErpAgentState.Offline or ErpAgentState.Unauthorized => CyVisualTheme.Warning,
            _ => CyVisualTheme.TextSecondary
        };
        // Problems are logged with their text (e.g. which field CY Web refused); otherwise only
        // state changes go to the log: the idle poll repeats every few seconds.
        if (state is ErpAgentState.Offline or ErpAgentState.Unauthorized && text != _agentWarned) _log.Warn("agent", _agentWarned = text);
        if (state == _agentState) return;
        _agentState = state;
        _log.Info("agent", $"state {state}");
    }

    private void UpdateDpiHint()
    {
        var percent = (int)Math.Round(DeviceDpi * 100.0 / 96);
        if (percent == 100)
        {
            _dpiHint.Text = "顯示比例 100%";
            _dpiHint.ForeColor = CyVisualTheme.TextSecondary;
        }
        else
        {
            _dpiHint.Text = $"顯示比例 {percent}%：請改為 100%，否則 ERP 定位可能錯誤";
            _dpiHint.ForeColor = CyVisualTheme.Warning;
        }
    }
}
