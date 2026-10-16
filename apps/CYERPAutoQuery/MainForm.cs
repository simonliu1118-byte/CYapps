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

    private readonly TextBox _code = new() { MaxLength = ItemCodeRules.MaxLength };
    private readonly CyPrimaryButton _start = new();
    private readonly CyStatusBadge _erpStateTag = new();
    private readonly TextBox _itemCode = ReadOnlyBox();
    private readonly TextBox _itemName = ReadOnlyBox();
    private readonly TextBox _specification = ReadOnlyBox();
    private readonly DataGridView _fields = new();
    private readonly DataGridView _warehouses = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _dpiHint = new();
    private readonly ToolStripStatusLabel _buildStatus = new();
    private readonly System.Windows.Forms.Timer _statePoll = new() { Interval = 1000 };
    private Invi02Status? _shownState;
    private ItemQueryResult? _lastResult;
    private CancellationTokenSource? _queryCts;

    public MainForm(AppLogger log)
    {
        _log = log;
        _service = new ItemQueryService(log);
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
        };
        DpiChanged += (_, _) => UpdateDpiHint();
        FormClosing += (_, _) => { _statePoll.Stop(); _automationSession?.Dispose(); };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        Controls.Add(root);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(toolbar, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 2), Margin = Padding.Empty };
        toolbar.Controls.Add(actions, 0, 0);
        actions.Controls.Add(new Label { Text = "品號：", AutoSize = true, Padding = new Padding(0, 11, 0, 0), Margin = new Padding(4, 0, 2, 0) });
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
        var copy = MakeButton("複製明細", 120);
        copy.Click += (_, _) => CopyFields();
        rightTools.Controls.Add(copy);
        _erpStateTag.Size = new Size(150, ToolbarButtonHeight);
        _erpStateTag.Margin = new Padding(4, 1, 12, 1);
        _erpStateTag.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        _erpStateTag.AccessibleName = "ERP 品號資料狀態";
        rightTools.Controls.Add(_erpStateTag);

        var headerBox = new GroupBox { Text = "表頭", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(3, 2, 3, 2), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddHeaderRow(header, 0, "品號", _itemCode);
        AddHeaderRow(header, 1, "品名", _itemName);
        AddHeaderRow(header, 2, "規格", _specification);
        headerBox.Controls.Add(header);
        root.Controls.Add(headerBox, 0, 1);

        var detailBox = new GroupBox { Text = "品號資料（ERP 轉出 Excel 的單頭資料）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
        ConfigureGrid(_fields);
        _fields.Columns.Add("name", "欄位");
        _fields.Columns.Add("value", "內容");
        _fields.Columns[0].Width = 220;
        _fields.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _fields.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        detailBox.Controls.Add(_fields);
        root.Controls.Add(detailBox, 0, 2);

        var warehouseBox = new GroupBox { Text = "庫別（單身資料）", Dock = DockStyle.Fill, Padding = new Padding(7), Margin = new Padding(0, 4, 0, 4) };
        ConfigureGrid(_warehouses);
        _warehouses.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        warehouseBox.Controls.Add(_warehouses);
        root.Controls.Add(warehouseBox, 0, 3);

        var statusStrip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false, Font = new Font("Microsoft JhengHei UI", 8.5F), Padding = new Padding(2, 0, 2, 0), Margin = Padding.Empty };
        _status.Text = "請先在 ERP 開啟「品號資料建立作業」，輸入品號後按「開始查詢 ERP」。";
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_dpiHint);
        statusStrip.Items.Add(_buildStatus);
        root.Controls.Add(statusStrip, 0, 4);

        CyVisualTheme.Apply(this);
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

    private async Task StartQueryAsync()
    {
        if (_queryCts is not null) return;
        if (!ItemCodeRules.TryNormalize(_code.Text, out var code, out var error))
        {
            MessageBox.Show(this, error, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _queryCts = new CancellationTokenSource();
        _start.Enabled = false;
        _statePoll.Stop();
        ShowResult(null);
        var progress = new Progress<string>(SetStatus);
        var outcome = AutomationOutcome.Stopped;
        ItemQueryResult? result = null;
        Exception? failure = null;
        try
        {
            // The physical mouse is ignored and 「自動查詢中」 shows in the middle of the screen
            // while CY drives ERP, the same as CYERPAutoInput (user, 2026-10-16).
            Session.Begin(AutomationSession.QueryTitle);
            using var escape = Session.EscapeToken.Register(() => { try { _queryCts?.Cancel(); } catch (ObjectDisposedException) { } });
            result = await _service.QueryAsync(code, progress, _queryCts.Token);
            outcome = result.Found ? AutomationOutcome.Done : AutomationOutcome.NotFound;
        }
        catch (OperationCanceledException)
        {
            outcome = AutomationOutcome.Cancelled;
            _log.Info("query", "cancelled by Esc");
        }
        catch (Exception ex)
        {
            failure = ex;
            _log.Error("query", ex);
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

        Activate();
        switch (outcome)
        {
            case AutomationOutcome.Done:
                ShowResult(result);
                SetStatus($"ERP：品號 {result!.ItemCode} 查詢完成，共 {result.Fields.Count} 欄（{result.Elapsed.TotalSeconds:0.0} 秒）");
                break;
            case AutomationOutcome.NotFound:
                SetStatus($"ERP：查無品號 {code}");
                break;
            case AutomationOutcome.Cancelled:
                SetStatus("ERP：已按 Esc 緊急停止");
                break;
            default:
                SetStatus("ERP：查詢失敗");
                MessageBox.Show(this, failure?.Message ?? "查詢失敗。", "ERP 自動查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
        _code.Focus();
        _code.SelectAll();
    }

    private void ShowResult(ItemQueryResult? result)
    {
        _lastResult = result;
        _itemCode.Text = result?.ItemCode ?? string.Empty;
        _itemName.Text = result?.ItemName ?? string.Empty;
        _specification.Text = result?.Specification ?? string.Empty;
        _fields.Rows.Clear();
        _warehouses.Rows.Clear();
        _warehouses.Columns.Clear();
        if (result is null) return;
        foreach (var f in result.Fields)
            _fields.Rows.Add(f.Name, f.Value);
        if (result.Warehouses.Count == 0) return;
        foreach (var f in result.Warehouses[0])
            _warehouses.Columns.Add(f.Name, f.Name);
        foreach (var row in result.Warehouses)
            _warehouses.Rows.Add(row.Select(f => (object)f.Value).ToArray());
    }

    private void CopyFields()
    {
        if (_lastResult is null || _lastResult.Fields.Count == 0) return;
        try
        {
            Clipboard.SetText(_lastResult.ToTsv());
            SetStatus("已複製明細，可貼到 Excel。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"複製失敗：{ex.Message}", "複製明細", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
