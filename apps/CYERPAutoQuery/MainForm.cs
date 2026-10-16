namespace CYERPAutoQuery;

/// <summary>
/// Main window, laid out like CYERPAutoInput's (user, 2026-10-16: the two will become parts
/// of one service and must not look different): toolbar with the action and the ERP status
/// badge, data groups, status strip. The query itself is <see cref="ItemQueryService"/>,
/// which the planned Web entry will call as well.
/// </summary>
internal sealed class MainForm : Form
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
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
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
    private ErpAgentState? _agentState;

    public MainForm(AppLogger log)
    {
        _log = log;
        _service = new ItemQueryService(log);
        _agentSettings = ErpAgentSettings.Load(log);
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
        _mode.Items.AddRange(["品號", "品名"]);
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
        AddHeaderRow(header, 0, "品號", _itemCode);
        AddHeaderRow(header, 1, "品名", _itemName);
        AddHeaderRow(header, 2, "規格", _specification);
        headerBox.Controls.Add(header);
        root.Controls.Add(headerBox, 0, 2);

        var detailBox = new GroupBox { Text = "品號資料（ERP 轉出 Excel 的單頭資料）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
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
        root.Controls.Add(_details, 0, 4);
        foreach (var c in ItemFieldCatalog.SearchList)
            _search.Columns.Add(c.Label, c.Label);
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

    private bool NameMode => _mode.SelectedIndex == 1;

    private void UpdateHistoryInputs()
    {
        // History belongs to one item; a 品名 search only lists items.
        _includeHistory.Enabled = !NameMode;
        _historyFrom.Enabled = _includeHistory.Checked && !NameMode;
        _historyTo.Enabled = _includeHistory.Checked && !NameMode;
    }

    /// <summary>The options of a local query; a bad date range throws ArgumentException with the message to show.</summary>
    private ItemQueryOptions LocalOptions()
    {
        if (!_includeHistory.Checked) return ItemQueryOptions.Default;
        DateOnly? Pick(DateTimePicker p) => p.Checked ? DateOnly.FromDateTime(p.Value) : null;
        return new ItemQueryOptions(HistoryRange.Resolve(Pick(_historyFrom), Pick(_historyTo), DateOnly.FromDateTime(DateTime.Today)));
    }

    private static TextBox ReadOnlyBox() => new() { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };

    private static void AddHeaderRow(TableLayoutPanel table, int row, string label, TextBox box)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) }, 0, row);
        table.Controls.Add(box, 1, row);
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
        if (NameMode)
        {
            await StartSearchAsync();
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

    /// <summary>品名 mode: lists the matching items on the 品名查詢 tab; failures shown in a message box.</summary>
    private async Task StartSearchAsync()
    {
        if (!ItemNameRules.TryNormalize(_code.Text, out var keyword, out var error))
        {
            MessageBox.Show(this, error, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await ExecuteSearchAsync(keyword, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 品名查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>Double-click on a 品名查詢 row: that item is queried by 品號 (user, 2026-10-16).</summary>
    private async Task QueryPickedItemAsync(int rowIndex)
    {
        if (_queryCts is not null || rowIndex < 0 || rowIndex >= _search.Rows.Count) return;
        var code = Convert.ToString(_search.Rows[rowIndex].Cells["品號"].Value) ?? string.Empty;
        if (code.Length == 0) return;
        _mode.SelectedIndex = 0;
        _code.Text = code;
        await StartQueryAsync();
    }

    private async Task<ItemSearchResult> ExecuteSearchAsync(string keyword, CancellationToken external)
    {
        ShowSearch(null);
        return await RunAutomationAsync(external, (progress, ct) => _service.SearchByNameAsync(keyword, progress, ct), result =>
        {
            ShowSearch(result);
            _details.SelectedTab = _searchPage;
            if (result.Items.Count == 0)
            {
                SetStatus($"ERP：品名含「{keyword}」的品號查無資料");
                return AutomationOutcome.NotFound;
            }
            var more = result.Truncated ? $"（已達 ERP 筆數上限 {result.Limit}，可能還有更多，請加長關鍵字）" : string.Empty;
            SetStatus($"ERP：品名含「{keyword}」共 {result.Items.Count} 筆{more}，雙擊一筆查詢明細（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    private void ShowSearch(ItemSearchResult? result)
    {
        _search.Rows.Clear();
        if (result is not null)
            foreach (var row in result.Items)
                _search.Rows.Add(ItemFieldCatalog.SearchList.Select(c => (object)ItemSearchResult.Value(row, c.Label)).ToArray());
        _searchPage.Text = result is null ? "品名查詢" : $"品名查詢（{result.Items.Count}{(result.Truncated ? "+" : string.Empty)}）";
    }

    /// <summary>A 品名 search received from CY Web: same path as the local one, the list goes back to Web.</summary>
    private async Task<ItemSearchResult> RunWebSearchAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        if (!ItemNameRules.TryNormalize(job.Keyword, out var keyword, out var error))
            throw new ArgumentException(error);
        _mode.SelectedIndex = 1;
        _code.Text = keyword;
        _log.Info("agent", $"web 品名 search keyword={_log.Value(keyword)}");
        return await ExecuteSearchAsync(keyword, cancellationToken);
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
            _queryCts.Dispose();
            _queryCts = null;
            _start.Enabled = true;
            _statePoll.Start();
        }
    }

    private void ShowResult(ItemQueryResult? result)
    {
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
            runSearch: RunWebSearchAsync);
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

    private void SetAgentStatus(ErpAgentState state, string text)
    {
        _agentStatus.Text = text.StartsWith("Web", StringComparison.Ordinal) ? text : $"Web：{text}";
        _agentStatus.ForeColor = state switch
        {
            ErpAgentState.Online or ErpAgentState.Querying => CyVisualTheme.Success,
            ErpAgentState.Offline or ErpAgentState.Unauthorized => CyVisualTheme.Warning,
            _ => CyVisualTheme.TextSecondary
        };
        // Only state changes go to the log: the idle poll repeats every few seconds.
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
