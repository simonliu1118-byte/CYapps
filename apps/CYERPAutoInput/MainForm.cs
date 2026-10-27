namespace CYERPAutoInput;

internal sealed partial class MainForm : Form
{
    private static readonly Size StandardWindowSize = new(1160, 720);

    private readonly AppLogger _log;
    private AutomationSession? _automationSession;
    /// <summary>Mouse lock and on-screen notice while CY works on its own (user, 2026-10-12).</summary>
    private AutomationSession Session
    {
        get
        {
            if (_automationSession is null)
            {
                _automationSession = new AutomationSession(_log);
                // Esc stops the background paper invoices too (user LOG 2026-10-15: after Esc and
                // the stop list, the paper prompt still came).
                _automationSession.Escaped += () => CancelPaperInvoices("已按 Esc 停止");
            }
            return _automationSession;
        }
    }

    /// <summary>Esc was pressed during this run (until the next run starts).</summary>
    private bool EscPressed => _automationSession?.EscapeToken.IsCancellationRequested == true;
    private readonly ErpAutomationService _automation;
    private readonly UserSettingsStore _settingsStore;
    private readonly UserSettings _settings;
    private readonly Dictionary<string, Control> _valueControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Panel> _fieldRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FlowLayoutPanel> _groupFlows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DetailDataGridView _details = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _buildStatus = new();
    private readonly ToolStripStatusLabel _dpiHint = new();
    private readonly ToolStripStatusLabel _employeeLink = new() { Text = "員工代號：未設定", IsLink = true };

    /// <summary>
    /// Employee code asked when the program opens and used for this session only (user,
    /// 2026-10-11). Temporary until CYID maps the signed-in user to the employee code.
    /// </summary>
    private string _sessionEmployeeCode = string.Empty;
    private readonly ModeToggle _modeToggle = new();
    private readonly CyStatusBadge _erpStateTag = new();
    private readonly ErpDocumentStateTracker _stateTracker = new();
    private readonly System.Windows.Forms.Timer _statePoll = new() { Interval = 1000 };
    private ErpDocumentState? _shownState;
    private bool _probeFailing;
    private readonly CyPrimaryButton _start = new();
    private TableLayoutPanel _root = null!;
    private CancellationTokenSource? _automationCts;

    public MainForm(AppLogger log)
    {
        _log = log;
        _automation = new ErpAutomationService(log);
        _settingsStore = new UserSettingsStore(log);
        _settings = _settingsStore.Load();
        // Always diagnostic (user, 2026-10-11): the local log keeps what CY read, for troubleshooting.
        _log.Diagnostic = true;
        UpdateBuildStatus();

        Text = "CYERPAutoInput — SMART ERP 自動輸入工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = StandardWindowSize;
        Size = StandardWindowSize;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        KeyPreview = true;

        BuildUi();
        ApplyDefaultsToBlankFields();
        // Advanced mode is hidden for now (user, 2026-10-09); the code stays for later.
        _modeToggle.Checked = false;
        ApplyMode(false);

        Load += (_, _) => { FitDetailRows(); FitAllFieldRows(); };
        _statePoll.Tick += (_, _) => RefreshErpState();
        Shown += (_, _) =>
        {
            FitAllFieldRows();
            if (_details.Rows.Count > 0) _details.CurrentCell = _details.Rows[0].Cells["ItemCode"];
            UpdateDpiHint();
            RefreshErpState();
            _statePoll.Start();
        };
        DpiChanged += (_, _) => UpdateDpiHint();
        FormClosing += (_, _) => { _statePoll.Stop(); _browserHotkey?.Dispose(); _browserHotkey = null; };
        Shown += (_, _) => ApplyBrowserHotkey();
        // Old Temp folders go at start too, not only when MO店+ 全自動 runs (user, 2026-10-15).
        Shown += (_, _) => { if (!UiSnapshot.Active) MoPrintedDocuments.Prune(_log); };
        Shown += (_, _) => StartUpdateChecks();
        // A printer out of paper, jammed or stuck after a print (user, 2026-10-27).
        PrintWatch.Warn = message => _log.Warn("print-watch", message);
        PrintWatch.Problem += message => { if (IsHandleCreated) BeginInvoke(() => OnPrintProblem(message)); };
    }

    /// <summary>Print problems noticed during a 全自動, shown when it ends.</summary>
    private readonly List<string> _printProblems = [];

    /// <summary>
    /// A print problem: a beep and the status line at once; a message now when nothing runs, or
    /// at the end of the 全自動 (a box now would wait behind the locked mouse).
    /// </summary>
    private void OnPrintProblem(string message)
    {
        Alarm(needsPerson: true);
        SetStatus(message);
        if (BatchRunning || _automationSession?.Active == true)
        {
            if (!_printProblems.Contains(message)) _printProblems.Add(message);
            return;
        }
        MessageBox.Show(this, message, "列印問題", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        UiSnapshot.AllowOversize(ref m);
    }

    internal void SetAdvancedModeForSnapshot(bool advanced) => _modeToggle.Checked = advanced;

    private void RefreshErpState()
    {
        ErpDocumentState state;
        try
        {
            state = _stateTracker.Update(_automation.ProbeStatus());
            _probeFailing = false;
        }
        catch (Exception ex)
        {
            if (!_probeFailing) _log.Warn("state", $"status probe failed: {ex.GetType().Name}");
            _probeFailing = true;
            state = ErpDocumentState.Unknown;
        }
        if (state == _shownState) return;
        _shownState = state;

        var (text, fore, back) = state switch
        {
            ErpDocumentState.Browse => ("檢視", CyVisualTheme.Info, CyVisualTheme.InfoSoft),
            ErpDocumentState.New => ("新增", CyVisualTheme.Success, CyVisualTheme.SuccessSoft),
            ErpDocumentState.Modify => ("修改", CyVisualTheme.Danger, CyVisualTheme.DangerSoft),
            ErpDocumentState.InputUnconfirmed => ("新增/修改？", CyVisualTheme.Warning, CyVisualTheme.WarningSoft),
            ErpDocumentState.MultipleWindows => ("多個 COPI08", CyVisualTheme.Warning, CyVisualTheme.WarningSoft),
            ErpDocumentState.NotFound => ("未開啟", CyVisualTheme.TextSecondary, CyVisualTheme.ReadOnly),
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

    private void BuildUi()
    {
        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            Margin = Padding.Empty
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 252));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        Controls.Add(_root);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _root.Controls.Add(toolbar, 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 2),
            Margin = Padding.Empty
        };
        toolbar.Controls.Add(actions, 0, 0);

        _start.Text = "開始輸入 ERP";
        // Toolbar buttons sized like CYInvoice's for easier use (user, 2026-10-11).
        _start.Size = new Size(140, ToolbarButtonHeight);
        _start.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
        _start.Margin = new Padding(4, 1, 4, 1);
        _start.Click += async (_, _) => await StartAutomationAsync();
        actions.Controls.Add(_start);
        // 出貨明細 of the 銷貨單 open in ERP (user, 2026-10-26: those entered by hand too; a small
        // button beside 開始輸入 ERP, the toolbar was too crowded).
        var sheetButton = MakeButton("印出貨明細", 86);
        sheetButton.Font = new Font(Font.FontFamily, 9F);
        sheetButton.Height = ToolbarButtonHeight - 10;
        sheetButton.Margin = new Padding(0, 6, 4, 6);
        sheetButton.Click += async (_, _) => await PrintShownShipSheetAsync();
        actions.Controls.Add(sheetButton);

        actions.Controls.Add(new Label
        {
            Text = "訂單匯入：",
            AutoSize = true,
            Padding = new Padding(0, 11, 0, 0),
            Margin = new Padding(10, 0, 2, 0)
        });
        actions.Controls.Add(ImportButton("蝦皮"));
        actions.Controls.Add(ImportButton("MO店+"));
        actions.Controls.Add(ImportButton("酷澎商城"));

        var rightTools = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 2),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        toolbar.Controls.Add(rightTools, 1, 0);

        var clearButton = MakeButton("清除表單", 120);
        clearButton.Click += (_, _) => ClearForm();
        rightTools.Controls.Add(clearButton);

        var settingsButton = MakeButton("設定", 120);
        settingsButton.Click += (_, _) => OpenSettings();
        rightTools.Controls.Add(settingsButton);



        _modeToggle.Margin = new Padding(4, 1, 8, 1);
        _modeToggle.Visible = false;
        _modeToggle.CheckedChanged += (_, _) => ApplyMode(_modeToggle.Checked);
        rightTools.Controls.Add(_modeToggle);

        _erpStateTag.Size = new Size(150, ToolbarButtonHeight);
        _erpStateTag.Margin = new Padding(4, 1, 12, 1);
        _erpStateTag.Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold);
        _erpStateTag.AccessibleName = "ERP 單據狀態";
        rightTools.Controls.Add(_erpStateTag);

        var fieldsHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoScroll = false,
            Padding = new Padding(0, 4, 0, 4),
            Margin = Padding.Empty
        };
        // 交易資料 and 送貨資料 are stacked in a double-width middle column so long values
        // such as 備註 and 送貨地址 have room (user, 2026-10-10).
        fieldsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        fieldsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fieldsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _root.Controls.Add(fieldsHost, 0, 1);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(4, 0, 4, 0), Padding = Padding.Empty };
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        middle.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        fieldsHost.Controls.Add(middle, 1, 0);

        GroupBox AddGroup(string group, bool wide, Padding margin)
        {
            var box = new GroupBox
            {
                Text = group == "發票資料(一)" ? "發票資料" : group,
                Dock = DockStyle.Fill,
                Padding = new Padding(7),
                Margin = margin
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = wide ? FlowDirection.LeftToRight : FlowDirection.TopDown,
                WrapContents = wide,
                AutoScroll = true,
                Padding = new Padding(3, 2, 0, 2),
                Margin = Padding.Empty
            };
            DisableHorizontalScroll(flow);
            box.Controls.Add(flow);
            _groupFlows[group] = flow;
            flow.ClientSizeChanged += (_, _) => FitFieldRows(flow, wide);
            _flowIsWide[flow] = wide;
            return box;
        }
        fieldsHost.Controls.Add(AddGroup("表頭", false, new Padding(0, 0, 4, 0)), 0, 0);
        middle.Controls.Add(AddGroup("交易資料", true, new Padding(0, 0, 0, 3)), 0, 0);
        middle.Controls.Add(AddGroup("送貨資料", true, new Padding(0, 3, 0, 0)), 0, 1);
        fieldsHost.Controls.Add(AddGroup("發票資料(一)", false, new Padding(4, 0, 0, 0)), 2, 0);
        foreach (var field in FieldCatalog.All) AddField(field);
        WireCompanyNameLookup();

        var detailBox = new GroupBox
        {
            Text = "商品明細（有資料的列必須填品號＋數量）",
            Dock = DockStyle.Fill,
            Padding = new Padding(7),
            Margin = new Padding(0, 4, 0, 4)
        };
        _root.Controls.Add(detailBox, 0, 2);
        ConfigureDetailGrid();
        detailBox.Controls.Add(_details);

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false,
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            Padding = new Padding(2, 0, 2, 0),
            Margin = Padding.Empty
        };
        _status.Text = "ERP：尚未偵測";
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_dpiHint);
        statusStrip.Items.Add(_buildStatus);
        _employeeLink.Click += (_, _) => AskEmployeeCode();
        statusStrip.Items.Add(_employeeLink);
        _root.Controls.Add(statusStrip, 0, 3);
    }

    private const int FieldLabelWidth = 98;
    private const int FieldInputLeft = 104;

    private readonly Dictionary<FlowLayoutPanel, bool> _flowIsWide = [];

    /// <summary>
    /// Sizes every group's rows once the window has its final size: a group whose client
    /// size never changed after creation kept the default row width and clipped its inputs
    /// (表頭, user 2026-10-11).
    /// </summary>
    private void FitAllFieldRows()
    {
        foreach (var (flow, wide) in _flowIsWide) FitFieldRows(flow, wide);
    }

    /// <summary>Fields that take a whole line in the double-width groups (long free text).</summary>
    private static readonly HashSet<string> FullLineKeys = new(["trade_note", "ship_name", "ship_addr1", "ship_addr2"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sizes every field row to the group's client width (reserving the vertical
    /// scrollbar) so inputs are never clipped and no horizontal scrollbar appears. In a
    /// double-width group two fields share a line, except the long free-text ones.
    /// </summary>
    private static void FitFieldRows(FlowLayoutPanel flow, bool wide)
    {
        var fullWidth = flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2;
        var halfWidth = fullWidth / 2 - 2;
        if ((wide ? halfWidth : fullWidth) < FieldInputLeft + 60) return;
        flow.SuspendLayout();
        foreach (Control row in flow.Controls)
        {
            var rowWidth = !wide || (row.Tag is string key && FullLineKeys.Contains(key)) ? fullWidth : halfWidth;
            row.Width = rowWidth;
            // A date field keeps room for its calendar button on the right.
            var picker = row.Controls.OfType<CalendarButton>().FirstOrDefault();
            var inputWidth = rowWidth - FieldInputLeft - 2 - (picker is null ? 0 : picker.Width + 2);
            foreach (Control child in row.Controls)
                if (child is TextBox or ComboBox) child.Width = inputWidth;
            if (picker is not null) picker.Left = FieldInputLeft + inputWidth + 2;
        }
        flow.ResumeLayout();
    }

    /// <summary>
    /// FlowLayoutPanel keeps the widest layout it has seen as its scroll extent, so after
    /// a wider layout (advanced mode, maximized) a horizontal scrollbar stayed behind.
    /// </summary>
    private static void DisableHorizontalScroll(FlowLayoutPanel flow)
    {
        flow.AutoScroll = false;
        flow.HorizontalScroll.Maximum = 0;
        flow.HorizontalScroll.Enabled = false;
        flow.HorizontalScroll.Visible = false;
        flow.AutoScroll = true;
    }

    private void AddField(FieldDefinition field)
    {
        const int labelWidth = FieldLabelWidth;
        const int inputLeft = FieldInputLeft;
        var flow = _groupFlows[field.Group];
        var row = new Panel { Width = 238, Height = 28, Margin = new Padding(1), Tag = field.Key };
        row.Controls.Add(new Label
        {
            Text = field.Label,
            AutoSize = false,
            AutoEllipsis = true,
            Width = labelWidth,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(0, 1),
            AccessibleName = field.Label
        });

        Control value;
        if (field.Kind == FieldKind.Boolean)
        {
            value = new CheckBox
            {
                Text = "啟用",
                AutoSize = true,
                Location = new Point(inputLeft, 4),
                Tag = field.Key
            };
        }
        else if (ErpComboOptions.ByField.TryGetValue(field.Key, out var options))
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 158,
                Location = new Point(inputLeft, 2),
                Tag = field.Key,
                AccessibleName = field.Label
            };
            combo.Items.Add(string.Empty);
            combo.Items.AddRange(options);
            combo.SelectedIndex = 0;
            value = combo;
        }
        else
        {
            var text = new TextBox
            {
                Width = 158,
                Location = new Point(inputLeft, 2),
                Tag = field.Key
            };
            if (field.Key == "order_type")
                text.MaxLength = 4;
            if (field.Kind == FieldKind.Date)
            {
                text.MaxLength = 10;
                ConfigureDateTextBox(text);
                var pick = new CalendarButton { Location = new Point(inputLeft + text.Width + 2, 1), Height = text.Height + 2 };
                pick.Click += (_, _) => DatePickerPopup.Show(text, new Rectangle(0, 0, text.Width, text.Height), text.Text,
                    date => { SetFieldText(text, date); text.Focus(); });
                row.Controls.Add(pick);
            }
            value = text;
        }

        row.Controls.Add(value);
        flow.Controls.Add(row);
        _valueControls[field.Key] = value;
        _fieldRows[field.Key] = row;
    }

    private static void ConfigureDateTextBox(TextBox text)
    {
        var updating = false;
        text.TextChanged += (_, _) =>
        {
            if (updating) return;
            var formatted = InputRules.FormatDateForDisplay(InputRules.NormalizeDateDigits(text.Text));
            if (text.Text == formatted) return;
            updating = true;
            text.Text = formatted;
            text.SelectionStart = text.Text.Length;
            updating = false;
        };
    }

    private const int DefaultDetailRows = 10;

    /// <summary>
    /// Grows the window so the detail grid shows exactly the default 10 rows without a
    /// scrollbar at the standard size; more rows bring the scrollbar.
    /// </summary>
    private void FitDetailRows()
    {
        if (WindowState != FormWindowState.Normal || _modeToggle.Checked) return;
        var delta = DetailRowsShortfall();
        if (delta <= 0) return;
        Height += delta;
        MinimumSize = new Size(MinimumSize.Width, Math.Max(MinimumSize.Height, Height));
    }

    /// <summary>Pixels the detail grid lacks to show the default rows without a scrollbar.</summary>
    internal int DetailRowsShortfall()
    {
        _root.PerformLayout();
        // A few spare pixels: DataGridView shows its scrollbar when the last row is even 1 px short.
        var needed = _details.ColumnHeadersHeight + DefaultDetailRows * _details.RowTemplate.Height + 4;
        return needed - _details.ClientSize.Height;
    }

    private void ConfigureDetailGrid()
    {
        _details.Dock = DockStyle.Fill;
        // No built-in "new row": it would be an 11th row and force a scrollbar on the
        // default 10. A blank row is appended when the last row is edited instead.
        _details.AllowUserToAddRows = false;
        _details.AllowUserToDeleteRows = false;
        // Column widths and row heights are fixed (user, 2026-10-10).
        _details.AllowUserToResizeColumns = false;
        _details.AllowUserToResizeRows = false;
        _details.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _details.AutoGenerateColumns = false;
        _details.RowHeadersVisible = false;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _details.EditMode = DataGridViewEditMode.EditOnEnter;
        _details.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _details.ColumnHeadersHeight = 29;
        // Columns share the visible width by weight so the last column is never hidden.
        _details.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _details.RowTemplate.Height = 27;

        _details.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Sequence",
            HeaderText = "序號",
            Width = 55,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ReadOnly = true,
            Frozen = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });
        _details.Columns.Add(TextColumn("ItemCode", "品號", 220));
        _details.Columns.Add(TextColumn("Unit", "單位", 105));
        _details.Columns.Add(TextColumn("Quantity", "數量", 105, DataGridViewContentAlignment.MiddleRight));
        _details.Columns.Add(TextColumn("GiftQuantity", "贈/備品量", 125, DataGridViewContentAlignment.MiddleRight));
        var batchColumn = TextColumn("Batch", "批號（自動）", 170);
        batchColumn.ReadOnly = true;
        _details.Columns.Add(batchColumn);
        _details.Columns.Add(TextColumn("Warehouse", "庫別", 140));
        _details.Columns.Add(TextColumn("UnitPrice", "單價", 130, DataGridViewContentAlignment.MiddleRight));
        // Bundle bookkeeping travels with the row (hidden), so an edited form keeps it right.
        foreach (var hidden in new[] { "BundleKey", "BundleTotal", "BundleGap" })
            _details.Columns.Add(new DataGridViewTextBoxColumn { Name = hidden, Visible = false });

        _details.RowsDefaultCellStyle.BackColor = CyVisualTheme.White;
        _details.RowsDefaultCellStyle.ForeColor = CyVisualTheme.TextPrimary;
        _details.RowsDefaultCellStyle.SelectionBackColor = CyVisualTheme.Selection;
        _details.RowsDefaultCellStyle.SelectionForeColor = CyVisualTheme.TextPrimary;
        _details.AlternatingRowsDefaultCellStyle.BackColor = CyVisualTheme.Window;
        _details.AlternatingRowsDefaultCellStyle.ForeColor = CyVisualTheme.TextPrimary;
        _details.AlternatingRowsDefaultCellStyle.SelectionBackColor = CyVisualTheme.Selection;
        _details.AlternatingRowsDefaultCellStyle.SelectionForeColor = CyVisualTheme.TextPrimary;

        for (var i = 0; i < DefaultDetailRows; i++) _details.Rows.Add();
        // Typing into the last row appends a blank one, so a document can have more than 10 rows.
        _details.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex == _details.Rows.Count - 1 && !string.IsNullOrWhiteSpace(Convert.ToString(_details[e.ColumnIndex, e.RowIndex].Value)))
                BeginInvoke(() => { if (e.RowIndex == _details.Rows.Count - 1) _details.Rows.Add(); });
        };
        _details.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!_details.Columns[e.ColumnIndex].Name.Equals("Sequence", StringComparison.Ordinal)) return;
            e.Value = (e.RowIndex + 1).ToString();
            e.FormattingApplied = true;
        };
    }

    private static DataGridViewTextBoxColumn TextColumn(
        string name,
        string text,
        int width,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft) => new()
    {
        Name = name,
        HeaderText = text,
        Width = width,
        FillWeight = width,
        MinimumWidth = 60,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var shift = (keyData & Keys.Shift) == Keys.Shift;

        if (key == Keys.Escape && _automationCts is not null)
        {
            _automationCts.Cancel();
            SetStatus("ERP：已要求緊急停止；不會替你按 ERP 取消");
            return true;
        }

        if (key is Keys.Enter or Keys.Tab && !_details.ContainsFocus)
        {
            SelectNextControl(ActiveControl, !shift, true, true, true);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ApplyMode(bool advanced)
    {
        if (_fieldRows.Count == 0) return;

        foreach (var field in FieldCatalog.All)
            _fieldRows[field.Key].Visible = advanced || field.Standard;
        foreach (var flow in _groupFlows.Values)
            flow.PerformLayout();

        if (advanced)
        {
            _root.RowStyles[1].SizeType = SizeType.Percent;
            _root.RowStyles[1].Height = 100;
            _root.RowStyles[2].SizeType = SizeType.Absolute;
            _root.RowStyles[2].Height = 330;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            _root.RowStyles[1].SizeType = SizeType.Absolute;
            _root.RowStyles[1].Height = 252;
            _root.RowStyles[2].SizeType = SizeType.Percent;
            _root.RowStyles[2].Height = 100;
            WindowState = FormWindowState.Normal;
            Size = new Size(StandardWindowSize.Width, Math.Max(StandardWindowSize.Height, MinimumSize.Height));
        }

        _root.PerformLayout();
        if (!advanced) FitDetailRows();
        FitAllFieldRows();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        var answer = form.ShowDialog(this);
        if (answer != DialogResult.OK) return;
        try
        {
            _settingsStore.Save(_settings);
            UpdateBuildStatus();
            ApplyBrowserHotkey();
            _modeToggle.Checked = false;
            ApplyMode(false);
            ApplyDefaultsToBlankFields();
            SetStatus("設定：已儲存本機設定");
        }
        catch (Exception ex)
        {
            _log.Error("settings", ex);
            MessageBox.Show(this, "本機設定儲存失敗，請查看 LOG。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>A drop-down only takes one of its options; anything else leaves it blank.</summary>
    private static void SetFieldText(Control control, string value)
    {
        if (control is ComboBox combo)
        {
            var index = combo.Items.IndexOf(value.Trim());
            combo.SelectedIndex = index >= 0 ? index : 0;
            return;
        }
        control.Text = value;
    }

    private void ApplyDefaultsToBlankFields()
    {
        // The employee code asked at start-up wins over the local default for this session.
        if (_sessionEmployeeCode.Length > 0 && _valueControls.TryGetValue("employee_code", out var employee) &&
            string.IsNullOrWhiteSpace(employee.Text))
            SetFieldText(employee, _sessionEmployeeCode);

        foreach (var pair in _settings.Defaults)
        {
            if (!_valueControls.TryGetValue(pair.Key, out var control) || control is CheckBox) continue;
            if (string.IsNullOrWhiteSpace(control.Text)) SetFieldText(control, pair.Value);
        }

        // ERP pre-fills its own default warehouse on every new row, so the configured 庫別
        // is put straight into the form's first detail row and typed into ERP like any cell.
        if (_settings.FirstRowWarehouseEnabled && _settings.FirstRowWarehouse.Length > 0 && _details.Rows.Count > 0 &&
            !_details.Rows[0].IsNewRow && string.IsNullOrWhiteSpace(Convert.ToString(_details["Warehouse", 0].Value)))
            _details["Warehouse", 0].Value = _settings.FirstRowWarehouse;
    }

    /// <summary>
    /// Typing an 8-digit 統一編號 fills a blank 客戶全名 from the registry (user, 2026-10-11);
    /// a result that arrives after the tax ID changed or the name was typed is dropped.
    /// </summary>
    private void WireCompanyNameLookup()
    {
        if (!_valueControls.TryGetValue("tax_id", out var taxId) || !_valueControls.TryGetValue("inv_name", out var invName)) return;
        taxId.TextChanged += async (_, _) =>
        {
            var ban = taxId.Text.Trim();
            if (!CompanyNameLookup.IsBan(ban) || invName.Text.Trim().Length > 0) return;
            var name = await _companyNames.LookupAsync(ban);
            if (name.Length > 0 && taxId.Text.Trim() == ban && invName.Text.Trim().Length == 0)
            {
                SetFieldText(invName, name);
                SetStatus("已依統一編號帶入客戶全名（經濟部登記名稱）");
            }
        };
    }

    /// <summary>Asks for this session's employee code (start-up, or the status bar link); blank keeps the local default.</summary>
    internal void AskEmployeeCode()
    {
        using var dialog = new EmployeeCodeForm(_sessionEmployeeCode);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _sessionEmployeeCode = dialog.EmployeeCode;
        _employeeLink.Text = _sessionEmployeeCode.Length > 0 ? $"員工代號：{_sessionEmployeeCode}" : "員工代號：未設定";
        if (_valueControls.TryGetValue("employee_code", out var employee))
            SetFieldText(employee, _sessionEmployeeCode);
        ApplyDefaultsToBlankFields();
        _log.Info("app", $"session employee code set length={_sessionEmployeeCode.Length}");
    }

    /// <summary>The last run of <see cref="StartAutomationAsync"/>, for 蝦皮全自動 to judge (null when it failed or stopped).</summary>
    private AutomationRunResult? _lastRunResult;
    /// <summary>撿貨單 of the 全自動 batch running (user, 2026-10-27); null outside one.</summary>
    private PickList? _pickList;
    /// <summary>理貨表 of the batch running: its saved documents (user, 2026-10-27).</summary>
    private readonly List<PackingRow> _packingRows = [];
    /// <summary>The lines ERP exported for the document just saved (全熱感紙列印), for the 撿貨單.</summary>
    private List<ShipSheetLine>? _lastSheetLines;

    /// <param name="fullAuto">Shopee full auto (user, 2026-10-11): save and print whatever the settings say.</param>
    /// <param name="batch">蝦皮全自動: the batch reports problems itself, so no summary box here.</param>
    /// <returns>False when the run did not start (a check failed or the user said no).</returns>
    private async Task<bool> StartAutomationAsync(bool fullAuto = false, bool batch = false)
    {
        _lastRunResult = null;
        if (_automationCts is not null) return false;
        var snapshot = CreateSnapshot(out var validationError);
        if (snapshot is null)
        {
            _automationSession?.End(); // a question is never behind a locked mouse (full auto, user 2026-10-12)
            MessageBox.Show(this, validationError, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var order = _loadedOrder;
        if (order is not null && !ConfirmNotDuplicate(order)) return false;
        // Imported orders were warned in the preview; a manual form is warned here.
        if (order is null && snapshot.Values.ContainsKey("tax_id") && !snapshot.Values.ContainsKey("inv_name") &&
            MessageBox.Show(this, "有統一編號，但沒有發票的客戶全名（經濟部查無或未填）。\n\n建議先填寫客戶全名；仍要開始輸入嗎？",
                "客戶全名", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return false;
        if (!ConfirmRestartIfErpBusy(out var abandonFirst)) return false;

        await RunGuardedAsync(async token =>
        {
            if (abandonFirst) await AbandonCurrentErpDocumentAsync(token);
            var result = await RunDocumentAsync(snapshot, fullAuto || _settings.AutoSave, token, print: fullAuto ? true : null);
            _lastRunResult = result;
            var notes = new List<string>();
            if (order is not null)
            {
                var ledgerProblem = RecordOutcome(order, result);
                if (ledgerProblem.Length > 0) notes.Add(ledgerProblem);
                if (order.TrackingNumber.Trim().Length == 0) notes.Add("物流單號空白：取得後請回補到送貨地址(一)。");
            }
            if (batch)
            {
                ClearFormIfSaved(result);
                return;
            }
            if (result.HandedOff.Length > 0)
            {
                MessageBox.Show(this, $"銷貨單 {DocumentKeyText(result)} 已輸入單頭後停止，請人工輸入明細並儲存。\n\n{result.HandedOff}" +
                    (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : string.Empty),
                    "轉人工處理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowRunSummaryIfNeeded(result, DocumentKeyText(result), notes);
            ClearFormIfSaved(result);
        });
        return true;
    }

    /// <summary>
    /// The one place a run clears the form (user, 2026-10-12): a document CY saved, when the
    /// 「儲存單據後自動清除表單」 setting is on; single orders, Shopee full auto and every
    /// order of an import batch all come through here.
    /// </summary>
    private void ClearFormIfSaved(AutomationRunResult result)
    {
        if (!result.Saved || !_settings.ClearAfterSave) return;
        ResetForm();
        SetStatus($"ERP：銷貨單 {DocumentKeyText(result)} 已儲存；表單已清空");
    }

    /// <summary>
    /// Shared automation envelope: one run at a time, global Esc watcher, status and error
    /// reporting. A cancelled or failed run never presses ERP 取消 or 儲存.
    /// </summary>
    private async Task RunGuardedAsync(Func<CancellationToken, Task> work)
    {
        _automationCts = new CancellationTokenSource();
        _start.Enabled = false;
        var watcher = WatchGlobalEscapeAsync(_automationCts);
        try
        {
            await work(_automationCts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("ERP：自動輸入已停止；程式未按 ERP 取消或儲存");
            _log.Warn("automation", "cancelled by Esc/user");
        }
        catch (Exception ex)
        {
            _log.Error("automation", ex);
            MessageBox.Show(this, ex.Message, "ERP 自動輸入失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus("ERP：輸入失敗，請查看本機 LOG");
        }
        finally
        {
            _automationCts.Cancel();
            try { await watcher; } catch { }
            _automationCts.Dispose();
            _automationCts = null;
            _start.Enabled = true;
        }
    }

    /// <summary>
    /// The one ERP check of every entry that ends in ERP input (蝦皮／MO店+／酷澎 全自動, 自動擷取,
    /// 匯入; user, 2026-10-14／15: at the entry, before any web step, not after the order is
    /// filled): exactly one COPI08, and no other ERP window open over it (a query or message
    /// window stops F5 新增 later). The 新增／修改 question stays at the start of ERP input.
    /// </summary>
    private bool ErpWindowReady()
    {
        RefreshErpState();
        var state = _shownState;
        // No COPI08 open: the run opens it through 作業速查 (user, 2026-10-17, as CYERPAutoQuery).
        if (state == ErpDocumentState.NotFound) return true;
        if (state != ErpDocumentState.MultipleWindows)
        {
            var copi = _automation.SelectCopi08(out _);
            var others = copi != 0 ? Win32Automation.FindVisibleProcessPeerWindows(copi) : [];
            if (others.Count == 0) return true;
            var names = others.Select(NativeMethods.WindowText).Where(t => t.Trim().Length > 0).Distinct().ToList();
            _log.Info("automation", $"entry refused: erp has {others.Count} other window(s)");
            MessageBox.Show(this, "ERP 目前有其他視窗開著" + (names.Count > 0 ? $"（{string.Join("、", names)}）" : string.Empty) +
                    "。\n\n請先關閉這些對話框再開始。", "ERP 視窗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        MessageBox.Show(this, MultipleCopi08Message, "ERP 視窗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _log.Info("automation", $"entry refused: erp state {state}");
        return false;
    }

    private const string MultipleCopi08Message =
        "開著多個銷貨單建立作業（COPI08），但沒有一個在檢視狀態。\n\n請把其中一個儲存或取消後再開始。";

    /// <summary>
    /// Safeguard (user, 2026-10-10): when ERP is already in 新增／修改, CY asks before
    /// starting; on confirmation it first abandons that document (ERP back to 檢視) and then
    /// runs the normal flow from 新增. Returns false when the user declines.
    /// </summary>
    private bool ConfirmRestartIfErpBusy(out bool abandonFirst)
    {
        abandonFirst = false;
        RefreshErpState();
        var state = _shownState;
        // Checked before anything starts (user, 2026-10-13: from the MO店+ preview two COPI08
        // windows ended in an error mid-run), so the preview comes back unchanged.
        if (state == ErpDocumentState.MultipleWindows)
        {
            _automationSession?.End();
            MessageBox.Show(this, MultipleCopi08Message, "ERP 視窗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _log.Info("automation", $"start refused: erp state {state}");
            return false;
        }
        if (state is not (ErpDocumentState.New or ErpDocumentState.Modify or ErpDocumentState.InputUnconfirmed)) return true;

        var what = state switch
        {
            ErpDocumentState.New => "新增",
            ErpDocumentState.Modify => "修改",
            _ => "新增或修改"
        };
        _automationSession?.End();
        var answer = MessageBox.Show(this,
            $"ERP 目前在「{what}」狀態。\n\n要放棄 ERP 上這張尚未儲存的單據（{what}的內容不會保留），回到檢視後再從新增開始輸入嗎？",
            "ERP 正在輸入中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return false;
        abandonFirst = true;
        _log.Info("automation", $"user confirmed abandoning the ERP document in state {state} before starting");
        return true;
    }

    private async Task AbandonCurrentErpDocumentAsync(CancellationToken token)
    {
        SetStatus("ERP：放棄目前的輸入，回到檢視…");
        using (PhysicalMouseLock.Engage(_log)) await _automation.AbandonInputAsync(null, token);
        SetStatus("ERP：已回到檢視");
    }

    private async Task<AutomationRunResult> RunDocumentAsync(FormSnapshot snapshot, bool autoSave, CancellationToken token, bool? print = null)
    {
        var progress = new Progress<string>(SetStatus);
        AutomationRunResult result;
        // The physical mouse is ignored while CY types (user, 2026-10-11; Esc releases it), and
        // the notice says 自動輸入中, or 全自動運轉中 in Shopee full auto (user, 2026-10-12).
        // How it ended shows on the notice (user, 2026-10-12): done means everything the
        // settings asked for (input, and save／print when on) went through with nothing to check.
        var outcome = AutomationOutcome.Stopped;
        try
        {
            Session.Begin(print == true ? AutomationSession.FullAutoTitle : AutomationSession.InputTitle);
            // The physical Esc stops the run as soon as it is pressed, not at the next key poll.
            using var escape = Session.EscapeToken.Register(() => { try { _automationCts?.Cancel(); } catch (ObjectDisposedException) { } });
            // 全熱感紙列印 (user, 2026-10-26): the print step becomes the thermal 出貨明細.
            var printWanted = autoSave && (print ?? _settings.AutoPrint);
            var thermal = printWanted && _settings.ThermalSheet;
            _lastSheetLines = null;
            result = await _automation.RunAsync(snapshot, autoSave, progress, token, printWanted && !thermal, thermal);
            if (result.ExportFile.Length > 0) PrintShipSheet(result);
            // 撿貨單: ERP's own lines when exported, else what was entered (no 品名 then).
            if (result.Saved && _pickList is not null && thermal)
                _packingRows.Add(new PackingRow(result.DocumentKey, _loadedOrder?.Source ?? string.Empty,
                    _loadedOrder?.OrderSn ?? string.Empty, _loadedOrder?.TrackingNumber.Trim() ?? string.Empty));
            if (result.Saved && _pickList is { } pick)
                pick.Add(_lastSheetLines ?? snapshot.Details.Where(d => d.HasAnyData)
                    .Select(d => new ShipSheetLine(d.ItemCode, string.Empty, d.Quantity, d.Unit, d.UnitPrice, string.Empty) { GiftQuantity = d.GiftQuantity }).ToList());
            outcome = result.HandedOff.Length > 0 || result.Warnings.Count > 0 || result.SaveSkippedReason.Length > 0 || result.PrintProblem.Length > 0
                ? AutomationOutcome.NeedsAttention
                : AutomationOutcome.Done;
        }
        catch (OperationCanceledException)
        {
            outcome = AutomationOutcome.Cancelled; // Esc
            throw;
        }
        finally
        {
            Session.End(outcome);
        }
        if (result.HandedOff.Length > 0)
        {
            SetStatus($"ERP：銷貨單 {DocumentKeyText(result)} 單頭已輸入，轉人工處理（未輸入明細、未儲存）");
            return result;
        }
        var saveText = result.Saved ? "已儲存" : "尚未儲存";
        SetStatus(result.Warnings.Count > 0
            ? $"ERP：銷貨單 {DocumentKeyText(result)} 輸入完成；{result.Warnings.Count} 筆需人工確認；{saveText}"
            : $"ERP：銷貨單 {DocumentKeyText(result)} 輸入完成；{saveText}");
        return result;
    }

    /// <summary>
    /// 全熱感紙列印 (user, 2026-10-26): the 出貨明細 of the document just saved — its lines from
    /// ERP's export, the order's platform numbers, 貨運 and the platform's 註記 — on the thermal
    /// printer. A problem goes to <see cref="AutomationRunResult.PrintProblem"/>; the export is
    /// kept then, deleted once printed.
    /// </summary>
    private void PrintShipSheet(AutomationRunResult result)
    {
        var file = result.ExportFile;
        try
        {
            var printer = SlipPrinter.Installed(_settings.ShopeeLabelPrinter)
                ?? throw new InvalidOperationException("找不到設定的熱感紙印表機");
            IReadOnlyList<(string Name, List<string[]> Rows)> sheets;
            using (var stream = File.OpenRead(file)) sheets = XlsxReader.ReadSheets(stream);
            _log.Info("ship-sheet", $"export columns: {ShipSheetExport.Describe(sheets)}");
            var lines = ShipSheetExport.Lines(sheets);
            _lastSheetLines = lines;
            var order = _loadedOrder;
            var source = order?.Source ?? string.Empty;
            var sheet = new ShipSheet(result.DocumentKey, source, order?.OrderSn ?? string.Empty, order?.TrackingNumber.Trim() ?? string.Empty,
                order?.Carrier ?? string.Empty, lines, ShipNoteFor(source), DateTime.Now);
            using var logo = LoadShipLogo();
            var pages = ShipSheetPrinter.Print(printer, sheet, _settings.ShipSheetTitle, logo);
            result.Printed = true;
            _log.Info("ship-sheet", $"printed document={_log.Value(result.DocumentKey)} lines={lines.Count} labels={pages}");
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.ComponentModel.Win32Exception)
        {
            result.PrintProblem = $"出貨明細未列印（{ex.Message}），ERP 匯出檔在 {file}";
            _log.Warn("ship-sheet", $"not printed document={_log.Value(result.DocumentKey)}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 印出貨明細 (user, 2026-10-26): the 銷貨單 open in COPI08 (saved, in browse mode) exported and
    /// printed as the thermal 出貨明細, its platform numbers and 貨運 read from the document itself.
    /// </summary>
    private async Task PrintShownShipSheetAsync()
    {
        if (_automationCts is not null || _shopeeBatchRunning) return;
        var printer = SlipPrinter.Installed(_settings.ShopeeLabelPrinter);
        if (printer is null)
        {
            MessageBox.Show(this, "請先在「設定 › 印表機」勾選熱感紙印表機。", "印出貨明細", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? problem = null;
        await RunGuardedAsync(async token =>
        {
            string file;
            Session.Begin(AutomationSession.InputTitle);
            try
            {
                file = await _automation.ExportShownDocumentAsync(new Progress<string>(SetStatus), token);
                Session.End(AutomationOutcome.Done);
            }
            catch (InvalidOperationException ex)
            {
                // A failure is shown as one (user, 2026-10-26: it ended with the 「完成」 notice).
                Session.End(AutomationOutcome.Stopped);
                SetStatus("出貨明細未列印：ERP 轉出 Excel 失敗");
                problem = $"ERP 轉出 Excel 失敗：{ex.Message}";
                return;
            }
            catch (OperationCanceledException)
            {
                Session.End(AutomationOutcome.Cancelled);
                throw;
            }
            try
            {
                IReadOnlyList<(string Name, List<string[]> Rows)> sheets;
                using (var stream = File.OpenRead(file)) sheets = XlsxReader.ReadSheets(stream);
                _log.Info("ship-sheet", $"export columns: {ShipSheetExport.Describe(sheets)}");
                var sheet = ShipSheetExport.FromDocument(sheets,
                    [(ShopeeOrderImport.Source, _settings.ShopeeNotePrefix), (MoOrderImport.Source, _settings.MoNotePrefix), (CoupangOrderImport.Source, _settings.CoupangNotePrefix)],
                    _settings.FreightTypes, ShipNoteFor, DateTime.Now);
                using var logo = LoadShipLogo();
                var pages = ShipSheetPrinter.Print(printer, sheet, _settings.ShipSheetTitle, logo);
                _log.Info("ship-sheet", $"printed shown document={_log.Value(sheet.Document)} lines={sheet.Lines.Count} labels={pages} platform={sheet.Platform}");
                SetStatus($"出貨明細已印出：銷貨單 {sheet.Document}");
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.ComponentModel.Win32Exception)
            {
                problem = $"出貨明細未列印：{ex.Message}\n\nERP 匯出檔在 {file}";
                SetStatus("出貨明細未列印");
                _log.Warn("ship-sheet", $"shown document not printed: {ex.GetType().Name}: {ex.Message}");
            }
        });
        if (problem is not null) MessageBox.Show(this, problem, "印出貨明細", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private string ShipNoteFor(string source) => source switch
    {
        ShopeeOrderImport.Source => _settings.ShipNoteShopee,
        MoOrderImport.Source => _settings.ShipNoteMo,
        CoupangOrderImport.Source => _settings.ShipNoteCoupang,
        _ => string.Empty
    };

    /// <summary>The logo from 設定 in pure black and white (a colour logo would come out grey and fuzzy); null when none.</summary>
    private Bitmap? LoadShipLogo()
    {
        try
        {
            if (!File.Exists(UserSettings.ShipLogoPath)) return null;
            using var source = new Bitmap(UserSettings.ShipLogoPath);
            var logo = new Bitmap(source.Width, source.Height);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                {
                    var c = source.GetPixel(x, y);
                    var light = c.A < 128 || c.R * 299 + c.G * 587 + c.B * 114 > 180_000;
                    logo.SetPixel(x, y, light ? Color.White : Color.Black);
                }
            return logo;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
        {
            _log.Warn("ship-sheet", $"logo not loaded: {ex.GetType().Name}");
            return null;
        }
    }

    private PickList NewPickList()
    {
        _packingRows.Clear();
        return new([_settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode]);
    }

    /// <summary>
    /// The end of a 全自動 batch, in the order the user set (2026-10-27), each step after the one
    /// before so nothing waits on another: 1 the paper invoices still to print (their paper prompts)
    /// are done; 2 the 理貨表 and the 撿貨單 print on A4 at once (no paper prompt); 3 the 撿貨單 is
    /// shown; the caller then shows the results. Paper prompts wait until <see cref="EndBatchHold"/>.
    /// </summary>
    private async Task FinishBatchAsync(string title)
    {
        if (_printProblems.Count > 0)
        {
            var problems = string.Join("\n", _printProblems.Distinct());
            _printProblems.Clear();
            MessageBox.Show(this, "全自動途中印表機有狀況：\n\n" + problems, "列印問題", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        await FinishPaperInvoicesAsync();
        _paperHold++;
        PrintPackingSheet();
        var list = _pickList;
        _pickList = null;
        if (list is null || list.Items.Count == 0) return;
        var printer = SlipPrinter.Installed(_settings.A4Printer) ?? string.Empty;
        if (printer.Length > 0)
        {
            try
            {
                var pages = PickListForm.PrintTable(printer, PickListForm.Heading(list, title), list.Items);
                _log.Info("pick-list", $"printed items={list.Items.Count} pages={pages}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Warn("pick-list", $"not printed: {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show(this, $"撿貨單沒有印出：{ex.Message}（可在撿貨單視窗再按列印）", "撿貨單", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        using var form = new PickListForm(list, title, printer, _log);
        form.ShowDialog(this);
    }

    /// <summary>After the results are closed: paper prompts may come again.</summary>
    private void EndBatchHold() => _paperHold = Math.Max(0, _paperHold - 1);

    /// <summary>
    /// Paper invoices of the batch first (user, 2026-10-27): waits while jobs remain, asking
    /// CYInvoice for their PDFs straight away; the prompts and printing go on as usual. Not
    /// forever — after two minutes the rest is left to the usual timer, so the end never hangs.
    /// </summary>
    private async Task FinishPaperInvoicesAsync()
    {
        HidePaperPanel();
        if (_paperJobs.Count == 0 && _paperNotes.Count == 0) return;
        _paperNextAsk = DateTime.MinValue;
        var deadline = DateTime.Now.AddMinutes(2);
        while ((_paperJobs.Count > 0 || _paperNotes.Count > 0) && DateTime.Now < deadline)
        {
            if (!_paperBusy) await PaperTickAsync();
            await Task.Delay(500);
        }
        if (_paperJobs.Count > 0) _log.Warn("paper", $"batch end: {_paperJobs.Count} paper invoice(s) still waiting; going on");
    }

    /// <summary>
    /// 理貨表 (user, 2026-10-27): with 全熱感紙列印 on, the batch's saved documents on the A4
    /// printer, one side, as soon as the batch ends.
    /// </summary>
    private void PrintPackingSheet()
    {
        if (_packingRows.Count == 0) return;
        var rows = _packingRows.ToList();
        _packingRows.Clear();
        var printer = SlipPrinter.Installed(_settings.A4Printer);
        try
        {
            if (printer is null) throw new InvalidOperationException("沒有設定 A4 印表機");
            var pages = PackingSheet.Print(printer, rows, DateTime.Today);
            _log.Info("packing-sheet", $"printed rows={rows.Count} pages={pages}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("packing-sheet", $"not printed: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(this, $"理貨表沒有印出：{ex.Message}", "理貨表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string DocumentKeyText(AutomationRunResult result) =>
        string.IsNullOrWhiteSpace(result.DocumentKey) ? "未取得" : result.DocumentKey;

    private async Task ImportShopeeAsync()
    {
        if (_automationCts is not null) return;
        if (!ErpWindowReady()) return;
        if (_settings.ShopeeOrderType.Length == 0 || _settings.ShopeeCustomerCode.Length == 0)
        {
            MessageBox.Show(this, "請先在「設定」填入蝦皮匯入的銷貨單別與客戶代號。", "蝦皮訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = PickExportFile("選擇蝦皮訂單匯出檔", "Excel 活頁簿 (*.xlsx)|*.xlsx");
        if (path is null) return;

        ShopeeImportResult parsed;
        try
        {
            await using var stream = File.OpenRead(path);
            parsed = ShopeeOrderImport.Parse(XlsxReader.ReadFirstSheet(stream));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _log.Warn("shopee", $"export read failed: {ex.GetType().Name}");
            MessageBox.Show(this, $"無法讀取這個檔案：{ex.Message}", "蝦皮訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _log.Info("shopee", $"export parsed orders={parsed.Orders.Count} errors={parsed.Errors.Count}");

        var mapping = new ShopeeMapping(_settings.ShopeeOrderType, _settings.ShopeeCustomerCode, _settings.ShopeeNotePrefix, _settings.FreightTypes, _settings.PromotionItemCode);
        var orders = parsed.Orders.Select(o => ShopeeOrderImport.ToImported(o, mapping)).ToList();
        await PreviewAndRunAsync(ShopeeOrderImport.Source, Path.GetFileName(path), orders, parsed.Errors);
    }

    /// <summary>
    /// One Shopee order read from screenshots onto the form (user, 2026-10-11): no batch and
    /// no automatic start; anything to check is listed and the user starts the input.
    /// </summary>
    private BrowserHotkey? _browserHotkey;

    /// <summary>F8 in the browser starts Shopee 光學辨識 while CY runs (always on since 2026-10-15).</summary>
    private void ApplyBrowserHotkey()
    {
        if (UiSnapshot.Active || _browserHotkey is not null) return;
        _browserHotkey = BrowserHotkey.Start(_log, () => BeginInvoke(OnBrowserHotkey));
    }

    private async void OnBrowserHotkey()
    {
        // Only from an idle CY: not while ERP is being typed into or another CY window is open.
        if (_automationCts is not null || _shopeeBatchRunning ||
            Application.OpenForms.Cast<Form>().Any(f => f != this && f.Visible && f is not AutomationOverlay))
        {
            _log.Info("hotkey", $"{BrowserHotkey.KeyName} ignored: CY is busy");
            return;
        }
        _log.Info("hotkey", $"{BrowserHotkey.KeyName} pressed in the browser; opening 光學辨識");
        // A minimized CY would keep its dialog hidden too.
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        try { await ImportShopeeScreenshotsAsync(); }
        catch (Exception ex) { _log.Error("hotkey", ex); }
    }

    private async Task ImportShopeeScreenshotsAsync()
    {
        if (_automationCts is not null) return;
        if (!ErpWindowReady()) return;
        if (_settings.ShopeeOrderType.Length == 0 || _settings.ShopeeCustomerCode.Length == 0)
        {
            MessageBox.Show(this, "請先在「設定」填入蝦皮的銷貨單別與客戶代號。", "蝦皮訂單自動擷取輸入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var ledger = LoadLedger();
        if (ledger is null) return;

        var mapping = new ShopeeShotMapping(_settings.ShopeeOrderType, _settings.ShopeeCustomerCode, _settings.ShopeeNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode, _settings.FreightTypes);
        try
        {
            using var dialog = new ShopeeScreenshotForm(_automation.Ocr, mapping, _log, autoStart: true, session: Session);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result?.Order is not { } order) return;
            await ContinueShopeeScreenshotOrderAsync(dialog.Result.Problems, order, ledger);
        }
        finally
        {
            // Whatever happened, the mouse is free again once the order is done or stopped.
            Session.End();
        }
    }

    /// <summary>
    /// 蝦皮全自動 (user, 2026-10-12): every pending 蝦皮店到店／店到宅 order is shipped, its label
    /// printed and entered into ERP, one by one, in a Chrome window of its own
    /// (<see cref="ShopeeBatchRunner"/>). Anything that cannot be done stops the whole batch with
    /// a beep and says where it stopped and whether Shopee had already shipped that order.
    /// </summary>
    /// <param name="resume">
    /// 測試 (user, 2026-10-26): only the order whose 寄件編號 dialog is open in the Chrome in front,
    /// from its 列印寄件單 to the end (<see cref="ShopeeBatchRunner.ResumeFromDialogAsync"/>).
    /// </param>
    private async Task RunShopeeBatchAsync(bool resume = false)
    {
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (!ErpWindowReady()) return;
        var missing = new[]
        {
            _settings.ShopeeOrderType.Length == 0 ? "蝦皮銷貨單別" : null,
            _settings.ShopeeCustomerCode.Length == 0 ? "蝦皮客戶代號" : null,
            _settings.ShopeeLabelPrinter.Length == 0 ? "熱感紙印表機（印表機區勾選）" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", ShopeeBatchTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (resume)
        {
            if (MessageBox.Show(this,
                    "測試：接續 Chrome 前景這張蝦皮訂單已打開的寄件編號視窗（快遞取件詳情／查看寄件編號）：\n\n" +
                    "按「列印寄件單」→ 照全自動的方式列印（黑貓存 PDF 後印在 A4 三格紙最上格）→ 擷取訂單、輸入 ERP。只做這一筆。\n\n" +
                    "過程中請勿操作電腦，按 Esc 可隨時停止。\n\n要開始嗎？",
                    ShopeeBatchTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }
        else if (MessageBox.Show(this,
                "全自動會開一個新的 Chrome 視窗，對「我的銷售 › 待出貨 › 待處理」的訂單逐筆：\n\n" +
                "・按「安排出貨」（新竹物流、黑貓宅急便為「安排取件」，取件地址須與設定相同；黑貓寄件單另存 PDF 後請你放一張 A4 三格紙，印在最上面一格）並列印寄件單（" + _settings.ShopeeLabelPrinter + "，蝦皮 " + _settings.ShopeeLabelScale + "%、7-11 " + _settings.SevenLabelScale + "%、全家裁切成標籤直接列印（不行時第 " + _settings.FamilyLabelPages + " 頁 " + _settings.FamilyLabelScale + "%）、新竹 " + _settings.HctLabelScale + "%）\n" +
                "・擷取訂單、輸入 ERP、儲存並列印\n\n" +
                "目前處理蝦皮店到店（含隔日到貨）、蝦皮店到宅、7-ELEVEN、全家、新竹物流與黑貓宅急便；遇到其他物流或任何問題會停下並發出聲音。\n" +
                "過程中請勿操作電腦，按 Esc 可隨時停止。\n\n要開始嗎？",
                ShopeeBatchTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        var ledger = LoadLedger();
        if (ledger is null) return;
        var mapping = new ShopeeShotMapping(_settings.ShopeeOrderType, _settings.ShopeeCustomerCode, _settings.ShopeeNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode, _settings.FreightTypes);

        _shopeeBatchRunning = true;
        _pickList = NewPickList();
        Session.Pin(AutomationSession.BatchTitle);
        Session.Begin(AutomationSession.BatchTitle);
        var token = Session.EscapeToken;
        var runner = new ShopeeBatchRunner(_automation.Ocr, _log,
            new ShopeeBatchPrint(_settings.ShopeeLabelPrinter, _settings.ShopeeLabelScale, _settings.SevenLabelScale, _settings.FamilyLabelScale, _settings.FamilyLabelPages,
                _settings.HctLabelScale, _settings.HctPickupAddress, SlipNote: _settings.SlipNote, SlipNoteAll: _settings.SlipNoteAllThermal), token,
            new ShopeeBatchChat(_settings.ShopeeChatRules));
        runner.PrintBlackCat = (order, pdf) => PrintBlackCatLabelAsync(order, pdf, token);
        var outcome = AutomationOutcome.Stopped;
        string? problem = null;
        var shipped = false;
        _log.Info("shopee-batch", "started");
        try
        {
            async Task<string?> EnterAsync(ShopeeBatchOrder order)
            {
                var note = await EnterShopeeBatchOrderAsync(order, runner, mapping, ledger, token);
                // Back to the batch's notice and the locked mouse for the next order.
                Session.Begin(AutomationSession.BatchTitle);
                return note;
            }
            if (resume) await runner.ResumeFromDialogAsync(EnterAsync, SetStatus);
            else await runner.RunAsync(EnterAsync, SetStatus);
            outcome = AutomationOutcome.Done;
        }
        catch (OperationCanceledException)
        {
            outcome = AutomationOutcome.Cancelled;
            shipped = runner.Current?.Shipped == true;
        }
        catch (ShopeeBatchStop ex)
        {
            outcome = AutomationOutcome.NeedsAttention;
            problem = ex.Message;
            shipped = ex.Shipped || runner.Current?.Shipped == true;
        }
        catch (Exception ex)
        {
            _log.Error("shopee-batch", ex);
            problem = ex.Message;
            shipped = runner.Current?.Shipped == true;
        }
        finally
        {
            Session.Unpin();
            Session.End(outcome == AutomationOutcome.Done && runner.Notes.Count > 0 ? AutomationOutcome.NeedsAttention : outcome);
            _shopeeBatchRunning = false;
        }
        _log.Info("shopee-batch", $"ended outcome={outcome} done={runner.Done.Count} shipped_current={shipped}");

        // One list at the end (user, 2026-10-13): what is done, what was passed over or needs a
        // look, and where it stopped if it did.
        var headline = outcome switch
        {
            AutomationOutcome.Done => $"全自動完成：已完成 {runner.Done.Count} 筆。",
            AutomationOutcome.Cancelled => $"已按 Esc 停止全自動：已完成 {runner.Done.Count} 筆。",
            _ => $"全自動已停止：已完成 {runner.Done.Count} 筆。"
        };
        string? stopped = null;
        if (outcome != AutomationOutcome.Done)
        {
            var lines = new List<string>();
            if (outcome != AutomationOutcome.Cancelled) lines.Add("停止原因：" + problem);
            if (runner.Current is { } current)
                lines.Add($"停在訂單 {current.OrderSn}：" + (shipped
                    ? "這筆在蝦皮已按「安排出貨」" + (current.Tracking.Length > 0 ? $"（寄件編號 {current.Tracking}）" : string.Empty) + "，請人工確認寄件單是否已列印，並完成 ERP 輸入。"
                    : "這筆在蝦皮尚未安排出貨。"));
            stopped = lines.Count > 0 ? string.Join("\r\n", lines) : null;
        }
        SetStatus(outcome == AutomationOutcome.Done
            ? $"全自動完成：{runner.Done.Count} 筆，需人工處理 {runner.Notes.Count} 筆"
            : $"全自動已停止：已完成 {runner.Done.Count} 筆");
        // Esc is the person's own stop: no sound for it.
        if (outcome != AutomationOutcome.Cancelled) Alarm(needsPerson: outcome != AutomationOutcome.Done || runner.Notes.Count > 0);
        await FinishBatchAsync(ShopeeBatchTitle);
        using var summary = new BatchSummaryForm(ShopeeBatchTitle, headline,
            [new BatchSummarySection($"需要人工處理 {runner.Notes.Count} 筆：", runner.Notes.Select(n => $"訂單 {n.OrderSn}：{n.Message}").ToList()),
             new BatchSummarySection($"已用聊聊通知買家 {runner.Chats.Count} 筆（括號內為買家備註裡的關鍵字）：", runner.Chats.Select(c => $"訂單 {c}").ToList())],
            stopped);
        summary.ShowDialog(this);
        EndBatchHold();
    }

    /// <summary>
    /// A MO店+ 全家／7-11 標籤 PDF cut slip by slip onto the thermal printer (as 蝦皮 全家):
    /// a page that is one picture is used as it is (MO 7-11: no sample yet, taken the same way,
    /// user 2026-10-14).
    /// </summary>
    private async Task<(int Printed, List<string> Notes)> PrintMoSlipsAsync(string file, string printer, string password, MoShipKind? kind = null)
    {
        var label = SlipPrinter.Label(printer, 100, 150);
        // A drawn page (7-11) at the printer's own resolution, printed 1:1 — its true size (user
        // 2026-10-15 sample: the slip is 83×122 mm on A4; 「請勿調整尺寸」). 全家 is a picture, used as is.
        var pages = await MoSlipPdf.PagesAsync(file, password, label.Dpi);
        var printed = 0;
        var notes = new List<string>();
        foreach (var (page, from) in pages)
        {
            // 7-11: each slip in its solid frame on an A4 page (user, 2026-10-15); 全家: one picture
            // of four cells between dashed lines. A file of unknown kind (測試) goes by that.
            var seven = kind is null ? !from.Contains("原圖", StringComparison.Ordinal) : kind == MoShipKind.Seven;
            var slips = await Task.Run(() => seven && SlipImage.FindFramedSlips(page) is { Count: > 0 } framed ? framed : SlipImage.FindSlips(page));
            _log.Info("mo-slip", $"{from}: picture {page.Width}x{page.Height}, slips={slips.Count}, printer dpi={label.Dpi}");
            foreach (var area in slips)
            {
                var plan = SlipImage.Plan(page, area, label.Width, label.Height);
                var mm = $"{plan.Width * 25.4 / label.Dpi:F0}×{plan.Height * 25.4 / label.Dpi:F0} mm";
                _log.Info("mo-slip", $"slip area={area} module={plan.Module} up={plan.Up} down={plan.Down} size={plan.Width}x{plan.Height} ({mm})");
                if (plan.Width > label.Width || plan.Height > label.Height) { notes.Add($"{from}一張太大印不下"); continue; }
                var slip = await Task.Run(() => SlipImage.Render(page, plan));
                var done = SlipPrinter.Print(printer, slip, "MO店+ 寄件單", _settings.SlipNote,
                    seven ? SlipNote.MinPoints : SlipNote.FamilyMinPoints);
                if (!done.Noted && SlipNote.Lines(_settings.SlipNote).Count > 0) _log.Warn("mo-slip", "slip too tall for the note block; printed without it");
                printed++;
                notes.Add($"{from}：條碼最細線 {plan.Module} 點，" + (plan.Up > 1 ? $"放大 {plan.Up} 倍" : plan.Down > 1 ? $"縮小為 1/{plan.Down}" : "原尺寸") + $"，約 {mm}");
            }
        }
        return (printed, notes);
    }

    /// <summary>A4 寄件單 sheets of 黑貓 are cut in three; one label goes in each (user, 2026-10-26).</summary>
    private const int BlackCatCells = 3;

    /// <summary>
    /// 蝦皮全自動 黑貓宅急便 (user, 2026-10-26): the saved label PDF on the A4 printer, in the top
    /// third of the sheet, after the person is asked to load one sheet (a pause: the mouse is
    /// free, Esc still stops the batch). One label per sheet, so no count is given.
    /// </summary>
    private async Task PrintBlackCatLabelAsync(ShopeeBatchOrder order, string pdf, CancellationToken token)
    {
        var folder = Path.GetDirectoryName(pdf)!;
        var printer = SlipPrinter.Installed(_settings.A4Printer)
            ?? throw new ShopeeBatchStop($"黑貓寄件單沒有列印：找不到設定的 A4 印表機（PDF 在 {folder}）。", true);
        // The label at its own size and place, top left as in the PDF (user, 2026-10-26: not
        // enlarged, top left); smaller only if it would not fit the top cell.
        var dpi = SlipPrinter.Label(printer, 210, 297).Dpi;
        var drawn = await MoSlipPdf.RenderFitAsync(pdf, 210, 297.0 / BlackCatCells, dpi, enlarge: false);
        using var label = drawn.Image;
        _log.Info("shopee-batch", $"black cat label {label.Width}x{label.Height} at {dpi} dpi ({label.Width * 25.4 / dpi:F0}x{label.Height * 25.4 / dpi:F0} mm) at {drawn.LeftMm:F1},{drawn.TopMm:F1} mm");
        Session.End();
        Alarm(needsPerson: true);
        bool go;
        using (var prompt = new PaperPromptForm("黑貓三格 A4 寄件單用紙", printer, $"訂單 {order.OrderSn}，印在最上面那一格"))
            go = prompt.ShowDialog(this) == DialogResult.OK;
        token.ThrowIfCancellationRequested();
        Session.Begin(AutomationSession.BatchTitle);
        if (!go) throw new ShopeeBatchStop($"黑貓寄件單按了取消，沒有列印（PDF 在 {folder}）。", true);
        try
        {
            await Task.Run(() => SlipPrinter.PrintAt(printer, label, drawn.LeftMm, drawn.TopMm, "黑貓寄件單 " + order.OrderSn), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn("shopee-batch", $"black cat print failed: {ex.GetType().Name}: {ex.Message}");
            throw new ShopeeBatchStop($"黑貓寄件單送 {printer} 時出錯（{ex.Message}），PDF 在 {folder}。", true);
        }
        _log.Info("shopee-batch", "black cat label printed on A4");
    }

    /// <summary>
    /// A 新竹物流 寄件單 or 到廠取貨總表 PDF on the A4 printer, after the very large 「請在印表機放入…用紙」
    /// and 好了 (user, 2026-10-14). The pages printed, or null when 取消 was pressed.
    /// </summary>
    private async Task<int?> PrintA4Async(string file, bool summary, string printer, string password)
    {
        var paper = "取貨總表用紙";
        string? detail = null;
        if (!summary)
        {
            // One label fits half an A4 sheet; more need whole sheets (user, 2026-10-14).
            var labels = MoPickupSummary.LabelCount(file, password);
            paper = labels == 1 ? "半張 A4 的寄件單用紙" : "整張 A4 的寄件單用紙";
            detail = labels > 1 ? $"共 {labels} 張標籤（一張紙 2 張）" : labels == 1 ? "只有 1 張標籤" : null;
            _log.Info("mo-a4", $"label pdf labels={labels}");
        }
        _log.Info("mo-a4", $"{(summary ? "pickup summary" : "label")} waits for paper");
        Alarm(needsPerson: true);
        using (var prompt = new PaperPromptForm(paper, printer, detail))
            if (prompt.ShowDialog(this) != DialogResult.OK) return null;
        var pages = await MoSlipPdf.RenderPagesAsync(file, password, 300);
        try
        {
            await Task.Run(() => SlipPrinter.PrintPages(printer, pages, (summary ? "到廠取貨總表 " : "新竹寄件單 ") + Path.GetFileName(file)));
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }
        _log.Info("mo-a4", $"printed pages={pages.Count} {(summary ? "pickup summary" : "label")}");
        return pages.Count;
    }

    private const string ShopeeBatchTitle = "全自動";
    private bool _shopeeBatchRunning;

    /// <summary>
    /// The page scan and ERP for the batch's order on screen (shipped already). Null when clean;
    /// a note for the closing list when ERP was left untouched (something to check before
    /// input) or the document was saved with something to check (user, 2026-10-13: note it and
    /// go on). A document ERP left open or unsaved stops the batch: ERP is not in a state to go on.
    /// </summary>
    private async Task<string?> EnterShopeeBatchOrderAsync(ShopeeBatchOrder order, ShopeeBatchRunner runner, ShopeeShotMapping mapping, OrderLedger ledger, CancellationToken token)
    {
        runner.Front();
        ShopeeShotResult? read;
        using (var dialog = new ShopeeScreenshotForm(_automation.Ocr, mapping, _log, autoStart: true, session: Session, browser: runner.Window))
        {
            // Esc while it waits on the person stops the batch, not just this order (user, 2026-10-26).
            using var escape = token.Register(() => BeginInvoke(() => { if (!dialog.IsDisposed && dialog.Visible) dialog.DialogResult = DialogResult.Cancel; }));
            var ok = dialog.ShowDialog(this) == DialogResult.OK;
            token.ThrowIfCancellationRequested();
            read = ok ? dialog.Result : null;
        }
        const string NotEntered = "已出貨（寄件單已印），ERP 未輸入：";
        if (read?.Order is not { } imported) return NotEntered + "訂單頁沒有讀進表單。";
        if (imported.OrderSn != order.OrderSn)
            return NotEntered + $"訂單頁讀到的訂單編號 {imported.OrderSn} 不是這筆。";
        if (!ShopeeBatchScreens.SameTracking(imported.TrackingNumber, order.Tracking))
            return NotEntered + $"訂單頁讀到的物流單號「{imported.TrackingNumber}」與寄件編號 {order.Tracking} 不同。";
        var notes = read.Problems.ToList();
        if (ledger.Find(imported.LedgerKey) is { } entry) notes.Insert(0, $"這張訂單{entry.Describe()}。");
        if (notes.Count > 0)
            return NotEntered + "有項目要確認：\n" + string.Join("\n", notes.Select(n => "・" + n));
        LoadOrder(imported);

        var started = await StartAutomationAsync(fullAuto: true, batch: true);
        token.ThrowIfCancellationRequested();
        if (!started || _lastRunResult is not { } result) throw new ShopeeBatchStop("ERP 輸入沒有完成（請看前一個訊息或本機 LOG）。", true);
        var key = DocumentKeyText(result);
        if (result.HandedOff.Length > 0) throw new ShopeeBatchStop($"ERP 銷貨單 {key} 轉人工：{result.HandedOff}", true);
        if (!result.Saved) throw new ShopeeBatchStop($"ERP 銷貨單 {key} 未儲存：{result.SaveSkippedReason}", true);
        var later = new List<string>();
        if (result.Warnings.Count > 0) later.AddRange(result.Warnings.Select(w => "・" + w.Message));
        if (result.PrintProblem.Length > 0) later.Add("・ERP 列印：" + result.PrintProblem);
        return later.Count > 0 ? $"已出貨，ERP 銷貨單 {key} 已儲存，請確認：\n" + string.Join("\n", later) : null;
    }

    /// <summary>
    /// The batch's end (user, 2026-10-12: with a sound). The work PCs have no speakers, only
    /// the board's buzzer (user, 2026-10-14), and since Windows 7 a program's beep goes to the
    /// sound card, never the buzzer — so the beep stays for PCs with speakers, and the window
    /// also flashes on the taskbar until it is brought to the front (the summary itself opens
    /// on top). Logged.
    /// </summary>
    private void Alarm(bool needsPerson)
    {
        // A person is needed: the mouse is theirs again (the run may have kept it locked).
        if (needsPerson) _automationSession?.End();
        var info = new NativeMethods.FLASHWINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
            hwnd = Handle,
            dwFlags = NativeMethods.FLASHW_UNTIL_FRONT
        };
        NativeMethods.FlashWindowEx(ref info);
        _log.Info("sound", $"end alert {(needsPerson ? "needs-person" : "done")}: beep and taskbar flash");
        if (needsPerson) System.Media.SystemSounds.Hand.Play();
        _ = Task.Run(() =>
        {
            for (var i = 0; i < (needsPerson ? 3 : 1); i++)
            {
                try { Console.Beep(1200, 350); } catch (PlatformNotSupportedException) { } catch (InvalidOperationException) { }
                Thread.Sleep(150);
            }
        });
    }

    private async Task ContinueShopeeScreenshotOrderAsync(IEnumerable<string> problems, ImportedOrder order, OrderLedger ledger)
    {
        LoadOrder(order);
        var notes = problems.ToList();
        if (ledger.Find(order.LedgerKey) is { } entry) notes.Insert(0, $"這張訂單{entry.Describe()}。");
        if (order.TrackingNumber.Length == 0) notes.Add("物流單號空白：取得後請填到送貨地址(一)。");
        SetStatus($"蝦皮訂單 {order.OrderSn} 已帶入表單；請確認後按「開始輸入 ERP」");
        _log.Info("shopee-shot", $"order loaded items={order.Items.Count} notes={notes.Count}");
        // Full auto (user, 2026-10-11; always since 2026-10-15): a clean order goes straight on to
        // ERP, save and print; with notes the user decides, defaulting to stop.
        if (notes.Count > 0) _automationSession?.End();
        if (notes.Count > 0 && MessageBox.Show(this,
                $"蝦皮訂單 {order.OrderSn} 已帶入表單，但有以下項目需要確認：\n\n" + string.Join("\n", notes.Select(n => "・" + n)) +
                "\n\n仍要繼續自動輸入 ERP、儲存並列印嗎？（選「否」可先修改表單，再按「開始輸入 ERP」）",
                "蝦皮自動擷取", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _log.Info("shopee-shot", $"full auto continues to ERP order_notes={notes.Count}");
        await StartAutomationAsync(fullAuto: true);
    }

    /// <summary>
    /// MO店+ 全自動 (user, 2026-10-14): the MO店+ 後台 steps in a Chrome window of the program's own
    /// (<see cref="ShopeeBatchRunner.RunMoAsync"/>), then — before ERP — the documents: 全家／7-11
    /// 標籤 cut onto the thermal printer, 新竹 寄件單 and 到廠取貨總表 on the A4 printer after a
    /// paper prompt each; then the two exports go into the MO店+ import (invoice details are filled
    /// in the preview until CYInvoice can be called) and ERP. The Temp folder goes once every order
    /// is saved in ERP.
    /// </summary>
    private async Task RunMoBatchAsync()
    {
        const string title = "MO店+ 全自動";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (!ErpWindowReady()) return;
        var missing = new[]
        {
            _settings.MoOrderType.Length == 0 ? "MO店+ 銷貨單別" : null,
            _settings.MoCustomerCode.Length == 0 ? "MO店+ 客戶代號" : null,
            _settings.MoPasswordProtected.Length == 0 ? "MO店+ 匯出檔密碼" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null,
            _settings.ShopeeLabelPrinter.Length == 0 ? "熱感紙印表機" : null,
            _settings.A4Printer.Length == 0 ? "A4 印表機" : null,
            _settings.CyInvoicePath.Length == 0 || !File.Exists(_settings.CyInvoicePath) ? "CYInvoice.exe（設定 › 一般）" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var thermal = SlipPrinter.Installed(_settings.ShopeeLabelPrinter);
        var a4 = SlipPrinter.Installed(_settings.A4Printer);
        if (thermal is null || a4 is null)
        {
            MessageBox.Show(this, $"這台電腦找不到{(thermal is null ? "熱感紙" : "A4 ")}印表機「{(thermal is null ? _settings.ShopeeLabelPrinter : _settings.A4Printer)}」，請到「設定 › 印表機」重新勾選。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this,
                "MO店+ 全自動會開一個新的 Chrome 視窗（MO店+ 後台需已登入），然後：\n\n" +
                "1. 我的訂單 › 未出貨：匯出報表（開發票用）\n" +
                "2. 批次出貨：新竹貨運、7-11店出、全家店出各自全選 → 批次出貨（併箱）→ 確認出貨，存下標籤（新竹另存出貨總表）\n" +
                "3. 我的訂單 › 配送中：匯出報表（物流單號）\n" +
                "4. CYInvoice 開立發票並帶入（全部沒問題就不開預覽）\n" +
                "5. 列印：全家／7-11 標籤印在「" + thermal + "」；新竹寄件單與取貨總表印在「" + a4 + "」，印之前會請你放紙；紙本發票拿到 PDF 後另外請你放 A4 白紙\n" +
                "6. 輸入 ERP\n\n" +
                "其他配送方式（倉出、冷凍等）不處理，會列在結果裡。網頁操作期間請勿操作電腦，按 Esc 可停止。\n\n要開始嗎？",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var password = LocalSecret.Unprotect(_settings.MoPasswordProtected);
        List<string[]> Read(string path)
        {
            using var stream = File.OpenRead(path);
            return Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? XlsxReader.ReadFirstSheet(stream)
                : XlsReader.ReadFirstSheet(stream, password);
        }
        IReadOnlyCollection<string> ReadOrders(string path) =>
            MoOrderImport.ReadTracking(Read(path))?.Keys.ToHashSet(StringComparer.Ordinal) ?? throw new ShopeeBatchStop("未出貨匯出檔讀不到訂單編號。");

        MoPrintedDocuments.Prune(_log);
        var folder = Path.Combine(MoPrintedDocuments.TempRoot, "mo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        _shopeeBatchRunning = true;
        Session.Pin(AutomationSession.BatchTitle);
        Session.Begin(AutomationSession.BatchTitle);
        var runner = new ShopeeBatchRunner(_automation.Ocr, _log,
            new ShopeeBatchPrint(_settings.ShopeeLabelPrinter, _settings.ShopeeLabelScale, _settings.SevenLabelScale, _settings.FamilyLabelScale, _settings.FamilyLabelPages,
                _settings.HctLabelScale, _settings.HctPickupAddress, SlipNote: _settings.SlipNote), Session.EscapeToken);
        MoBatchResult? result = null;
        string? stopped = null;
        _log.Info("mo-batch", "started");
        try
        {
            result = await runner.RunMoAsync(folder, ReadOrders, SetStatus);
        }
        catch (ShopeeBatchStop stop)
        {
            _log.Warn("mo-batch", $"stopped: {stop.Message}");
            stopped = stop.Message;
        }
        catch (OperationCanceledException)
        {
            stopped = "已按 Esc 停止。";
        }
        finally
        {
            // The run goes straight on to CYInvoice and ERP: no pause in between (user LOG
            // 2026-10-15: a 「已暫停」 notice flashed and the run went on); stopped → ended.
            Session.Unpin();
            if (stopped is not null || result is null) Session.End(AutomationOutcome.Stopped);
            _shopeeBatchRunning = false;
        }
        if (stopped is not null || result is null)
        {
            Alarm(needsPerson: true);
            MessageBox.Show(this, $"MO店+ 全自動停止：{stopped}\n\n已存下的檔案在：{folder}\n已出貨的訂單請到 MO店+ 後台確認，再用「匯入訂單匯出檔」接續。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (result.Before is null)
        {
            Session.End(AutomationOutcome.Done);
            TryDelete(folder);
            MessageBox.Show(this, string.Join("\n", result.Notes.DefaultIfEmpty("沒有要處理的訂單。")), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Documents after the preview, before ERP (user, 2026-10-14: a paper invoice may be printed
        // in the same step), kept for 重新列印. A printer error does not
        // stop the run (user, 2026-10-14): it is noted and the document can be reprinted; only
        // 停止 on the paper prompt stops.
        var printed = new List<string>();
        var documents = new List<MoPrintedDocument>();
        async Task<bool> Print(string kind, string file, Func<Task<string?>> print)
        {
            try
            {
                var detail = await print();
                if (detail is null) return false;
                printed.Add($"{kind} {detail}");
                documents.Add(new MoPrintedDocument(kind, file, DateTime.Now, detail));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                _log.Warn("mo-batch", $"print failed {kind}: {ex.GetType().Name}: {ex.Message}");
                printed.Add($"{kind}列印失敗（{ex.Message}），請用「重新列印寄件單」");
                documents.Add(new MoPrintedDocument(kind, file, DateTime.Now, "列印失敗"));
            }
            return true;
        }
        var voidNotes = new List<string>();
        MoInvoiceOutcome? invoice = null;
        async Task<bool> PrintAllAsync()
        {
            var go = true;
            foreach (var (kind, file) in result.Labels.Where(l => l.Kind != MoShipKind.Hct))
            {
                var name = kind == MoShipKind.Family ? MoPrintedDocument.Family : MoPrintedDocument.Seven;
                SetStatus($"MO店+ 全自動：列印{name}…");
                await Print(name, file, async () => $"{(await PrintMoSlipsAsync(file, thermal, password, kind)).Printed} 張");
            }
            foreach (var (_, file) in result.Labels.Where(l => l.Kind == MoShipKind.Hct))
            {
                if (!go) break;
                go = await Print(MoPrintedDocument.HctLabel, file, async () => await PrintA4Async(file, summary: false, a4, password) is { } pages ? $"{pages} 頁" : null);
            }
            // All of a sheet's orders printed before → not again; some → printed, the earlier
            // sheet to be voided (user, 2026-10-14).
            var sheets = new PickupSummaryLedger(PickupSummaryLedger.DefaultPath);
            foreach (var file in result.Summaries)
            {
                if (!go) break;
                List<string> numbers;
                try { numbers = MoPickupSummary.Read(file, password); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { _log.Warn("mo-batch", $"pickup summary unread: {ex.Message}"); numbers = []; }
                var (print, voided) = MoPickupSummary.Decide(numbers, sheets.AlreadyPrinted(numbers));
                _log.Info("mo-batch", $"pickup summary orders={numbers.Count} print={print} void={voided.Count}");
                if (!print)
                {
                    printed.Add($"新竹到廠取貨總表未重印（宅配單號 {string.Join("、", numbers)} 的總表都已印過）");
                    continue;
                }
                var ok = false;
                go = await Print(MoPrintedDocument.Summary, file, async () =>
                {
                    if (await PrintA4Async(file, summary: true, a4, password) is not { } pages) return null;
                    ok = true;
                    return $"{pages} 頁，{numbers.Count} 筆";
                });
                if (!ok) continue;
                sheets.Record(numbers);
                if (voided.Count > 0)
                    voidNotes.Add($"【請作廢舊的到廠取貨總表】宅配單號 {string.Join("、", voided)} 先前印的總表已由這次的新總表取代（新總表含 {numbers.Count} 筆），舊的那張請作廢。");
            }
            if (!go)
            {
                // What was not printed is listed for 重新列印 too.
                foreach (var (kind, file) in result.Labels.Select(l => (l.Kind == MoShipKind.Hct ? MoPrintedDocument.HctLabel : l.Kind == MoShipKind.Family ? MoPrintedDocument.Family : MoPrintedDocument.Seven, l.Path))
                             .Concat(result.Summaries.Select(f => (MoPrintedDocument.Summary, f))))
                    if (!documents.Any(d => d.File == file)) documents.Add(new MoPrintedDocument(kind, file, DateTime.Now, "未印"));
                MoPrintedDocuments.Save(folder, documents);
                _log.Info("mo-batch", "stopped on the paper prompt");
                MessageBox.Show(this, $"已在放紙提醒按停止。已出貨 {result.Shipped} 筆；檔案在：{folder}\n沒印的寄件單可用「重新列印寄件單」或到 MO店+ 後台列印，再用「匯入訂單匯出檔」接續打單。",
                    title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            // Paper invoices whose PDF is already there go in the same printing step; the rest
            // come in the background when ready, never holding up ERP (user, 2026-10-15).
            await PrintReadyPaperAsync(printed);
            MoPrintedDocuments.Save(folder, documents);
            _log.Info("mo-batch", $"printed {string.Join("; ", printed)}");
            if (voidNotes.Count > 0)
                MessageBox.Show(this, string.Join("\n", voidNotes), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        // Listed for 重新列印 even when the preview is closed before anything is printed.
        MoPrintedDocuments.Save(folder, [.. result.Labels.Select(l => new MoPrintedDocument(l.Kind == MoShipKind.Hct ? MoPrintedDocument.HctLabel : l.Kind == MoShipKind.Family ? MoPrintedDocument.Family : MoPrintedDocument.Seven, l.Path, DateTime.Now, "未印")),
            .. result.Summaries.Select(f => new MoPrintedDocument(MoPrintedDocument.Summary, f, DateTime.Now, "未印"))]);

        // The two exports into the import, then the preview, the printing and ERP.
        using var files = new MoImportForm(Read);
        files.Preload([result.Before, .. result.After is null ? Array.Empty<string>() : [result.After]]);
        // Straight on (user, 2026-10-15); the import window only when the two exports do not fit
        // together.
        if (!files.Accept())
        {
            _log.Info("mo-batch", "exports do not fit together; import window shown");
            Alarm(needsPerson: true);
            if (files.ShowDialog(this) != DialogResult.OK) return;
        }
        var mapping = new MoMapping(_settings.MoOrderType, _settings.MoCustomerCode, _settings.MoNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.FreightTypes, _settings.PromotionItemCode);
        var orders = files.Orders.Select(o => MoOrderImport.ToImported(o, mapping)).ToList();
        // 發票 through CYInvoice when set, else filled by hand in the preview.
        // 全自動 always invoices through CYInvoice (user, 2026-10-15).
        {
            invoice = await InvoiceMoAsync(title, files.BatchPath ?? result.Before, files.Orders, orders, fullAuto: true);
            QueuePaperInvoices(invoice);
        }
        var printedOnce = false;
        async Task<bool> BeforeErp()
        {
            if (!(printedOnce || (printedOnce = await PrintAllAsync()))) return false;
            // A test CYInvoice's invoices never go into ERP; the labels are printed by now.
            if (invoice is { IsProduction: false, Environment.Length: > 0 })
            {
                MessageBox.Show(this, "CYInvoice 是測試環境：寄件單已列印，發票不輸入 ERP（測試到此為止）。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        // The complete chain (user, 2026-10-15): every order with its invoice, nothing to check,
        // never entered before, 自動儲存 on → no preview: printing, then ERP for all. Anything
        // else (or a batch check that stops) → the preview as before.
        var ledger = LoadLedger();
        if (ledger is null) return;
        if (EscPressed)
        {
            CancelPaperInvoices("已按 Esc 停止");
            MessageBox.Show(this, $"已按 Esc 停止：已出貨 {result.Shipped} 筆，尚未列印、尚未輸入 ERP。檔案在：{folder}\n請用「匯入訂單匯出檔」接續。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var clean = invoice is not null && _settings.AutoSave && orders.Count > 0 && files.Errors.Count == 0 &&
                    orders.All(o => o.HandoffReason.Length == 0 && o.InvoiceNo.Trim().Length > 0 && o.ValidationError().Length == 0 &&
                                    !o.MissingInvoiceName && ledger.Find(o.LedgerKey) is null);
        var entered = false;
        if (clean)
        {
            _log.Info("mo-batch", $"all {orders.Count} orders ready; no preview");
            SetStatus("MO店+ 全自動：列印並輸入 ERP…");
            if (!await BeforeErp()) entered = true;
            else
            {
                _paperHold++;
                try { entered = await RunImportBatchAsync(MoOrderImport.Source, orders, ledger, [.. invoice?.Notes ?? [], .. files.Notes, .. result.Notes]); }
                finally { _paperHold--; }
            }
        }
        if (!entered)
        {
            result.Notes.Insert(0, $"已出貨 {result.Shipped} 筆；" + (invoice is null ? "在預覽補好發票資料後按開始，" : "在預覽確認後按開始，") + "會先列印寄件單再輸入 ERP。");
            SetStatus("MO店+ 全自動：請在預覽確認");
            Alarm(needsPerson: true);
            await PreviewAndRunAsync(MoOrderImport.Source, files.FileName, orders, files.Errors, [.. invoice?.Notes ?? [], .. files.Notes, .. result.Notes], BeforeErp);
        }

        // Once every order of the batch is saved the exports go (user, 2026-10-13); the printed
        // documents stay 7 days for 重新列印 (user, 2026-10-14).
        if (files.Errors.Count == 0 && orders.Count > 0 && orders.All(o => ledger.Find(o.LedgerKey)?.Status == LedgerStatus.Saved))
        {
            try { MoPrintedDocuments.KeepOnlyDocuments(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { _log.Warn("mo-batch", $"exports left: {ex.Message}"); }
        }
        else _log.Info("mo-batch", "temp folder kept: not every order is saved");
    }

    /// <summary>
    /// 重新列印寄件單 (user, 2026-10-14): a printer that failed is noticed by a person; the
    /// documents of the last 7 days are printed again — 全家／7-11 cut onto the thermal printer,
    /// 新竹 on A4 after the paper prompt (a reprinted 總表 is not recorded again).
    /// </summary>
    private async Task ReprintMoAsync()
    {
        const string title = "MO店+ 重新列印";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        MoPrintedDocuments.Prune(_log);
        using var form = new MoReprintForm(MoPrintedDocuments.All());
        if (form.ShowDialog(this) != DialogResult.OK || form.Selected.Count == 0) return;
        var thermal = SlipPrinter.Installed(_settings.ShopeeLabelPrinter);
        var a4 = SlipPrinter.Installed(_settings.A4Printer);
        var password = LocalSecret.Unprotect(_settings.MoPasswordProtected);
        var lines = new List<string>();
        _shopeeBatchRunning = true;
        try
        {
            foreach (var d in form.Selected)
            {
                var name = Path.GetFileName(d.File);
                try
                {
                if (d.Kind is MoPrintedDocument.Family or MoPrintedDocument.Seven)
                {
                    if (thermal is null) { lines.Add($"{d.Kind}：找不到熱感紙印表機（設定 › 印表機）。"); continue; }
                    var (count, _) = await PrintMoSlipsAsync(d.File, thermal, password, d.Kind == MoPrintedDocument.Seven ? MoShipKind.Seven : MoShipKind.Family);
                    lines.Add($"{d.Kind} {count} 張已重印（{name}）");
                }
                else
                {
                    if (a4 is null) { lines.Add($"{d.Kind}：找不到 A4 印表機（設定 › 印表機）。"); continue; }
                    var pages = await PrintA4Async(d.File, summary: d.Kind == MoPrintedDocument.Summary, a4, password);
                    if (pages is null) { lines.Add($"{d.Kind}：已停止，沒有重印。"); break; }
                    lines.Add($"{d.Kind} {pages} 頁已重印（{name}）");
                }
                _log.Info("mo-batch", $"reprinted {d.Kind}");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _log.Warn("mo-batch", $"reprint {d.Kind}: {ex.GetType().Name}: {ex.Message}");
                    lines.Add($"{d.Kind} 重印失敗（{ex.Message}）");
                }
            }
        }
        finally
        {
            _shopeeBatchRunning = false;
        }
        MessageBox.Show(this, string.Join("\n", lines), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Warn("mo-batch", $"temp folder left: {ex.Message}"); }
    }

    /// <summary>
    /// 酷澎 › 匯入訂單匯出檔 (user, 2026-10-19: the former 測試 after its download, now the import):
    /// a DeliveryList → CYInvoice's 酷澎 entry issues (always paper, printed in the background) →
    /// every order clean (and CYInvoice in production) straight into ERP, otherwise the preview.
    /// </summary>
    private async Task ImportCoupangAsync()
    {
        const string title = "酷澎訂單匯入";
        if (_automationCts is not null || _shopeeBatchRunning) return;
        if (!ErpWindowReady()) return;
        var missing = new[]
        {
            _settings.CoupangOrderType.Length == 0 ? "酷澎銷貨單別" : null,
            _settings.CoupangCustomerCode.Length == 0 ? "酷澎客戶代號" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null,
            _settings.CyInvoicePath.Length == 0 || !File.Exists(_settings.CyInvoicePath) ? "CYInvoice.exe" : null,
            _settings.A4Printer.Length == 0 ? "A4 印表機（紙本發票）" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = PickExportFile("選擇酷澎訂單匯出檔（DeliveryList）", "Excel 活頁簿 (*.xlsx)|*.xlsx");
        if (path is null) return;
        _log.Info("coupang", "import picked a DeliveryList");
        Session.Begin(AutomationSession.BatchTitle);
        await CoupangToErpAsync(title, path, []);
    }

    private string? PickExportFile(string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <param name="beforeErp">Run once the preview is confirmed, before ERP (MO店+ 全自動: printing); false stops.</param>
    private async Task PreviewAndRunAsync(string source, string fileName, List<ImportedOrder> orders, IReadOnlyList<string> errors,
        IReadOnlyList<string>? notes = null, Func<Task<bool>>? beforeErp = null)
    {
        var ledger = LoadLedger();
        if (ledger is null) return;

        using var preview = new OrderImportForm(source, fileName, orders, errors, _settings.AutoSave, o => ledger.Find(o.LedgerKey),
            ban => _companyNames.LookupAsync(ban), notes);
        // When a check stops the start, the same preview comes back with what the user filled
        // in (user, 2026-10-12: MO店+ and 酷澎 invoice details were lost).
        while (preview.ShowDialog(this) == DialogResult.OK)
        {
            if (preview.Action is OrderImportAction.StartSelected or OrderImportAction.RunAll && beforeErp is not null && !await beforeErp()) return;
            if (preview.Action == OrderImportAction.StartSelected && preview.SelectedOrder is not null)
            {
                LoadOrder(preview.SelectedOrder);
                SetStatus($"{source}訂單 {preview.SelectedOrder.OrderSn}：開始輸入 ERP");
                _paperHold++;
                try { if (await StartAutomationAsync()) return; }
                finally { _paperHold--; }
            }
            else if (preview.Action == OrderImportAction.RunAll)
            {
                _paperHold++;
                try { if (await RunImportBatchAsync(source, preview.SelectedOrders, ledger)) return; }
                finally { _paperHold--; }
            }
            else return;
            _log.Info("import", "start stopped by a check; back to the preview");
        }
    }

    /// <summary>Duplicate-order ledger; null (with a message) when it cannot be read, so nothing is entered blind.</summary>
    private OrderLedger? LoadLedger()
    {
        try
        {
            return _ledger ??= new OrderLedger(OrderLedger.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _log.Error("ledger", ex);
            _automationSession?.End();
            MessageBox.Show(this, $"無法讀取防重複紀錄（{OrderLedger.DefaultPath}）：{ex.Message}\n為避免重複打單，暫不匯入。",
                "訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    private OrderLedger? _ledger;
    private CompanyNameLookup? _companyNameLookup;
    private CompanyNameLookup _companyNames => _companyNameLookup ??= new CompanyNameLookup(_log);

    /// <summary>Platform order currently loaded on the form (null after 清除 or manual input).</summary>
    private ImportedOrder? _loadedOrder;

    /// <summary>Form fields an import may set; cleared on each load so one order's values never leak into the next.</summary>
    private static readonly string[] ImportedFieldKeys =
        ["order_type", "customer_code", "trade_note", "ship_addr1", "freight_type", "cod", "freight_fee", "tax_id", "inv_name", "inv_date", "inv_no"];

    /// <summary>
    /// Puts one order on the form: its header fields and a fresh detail grid; every other
    /// field keeps its current value (local defaults such as 單據日期 or 員工代號).
    /// </summary>
    private void LoadOrder(ImportedOrder order)
    {
        foreach (var key in ImportedFieldKeys)
            if (_valueControls.TryGetValue(key, out var control)) SetFieldText(control, string.Empty);
        foreach (var pair in order.HeaderValues())
            if (_valueControls.TryGetValue(pair.Key, out var control)) SetFieldText(control, pair.Value);

        _details.EndEdit();
        _details.Rows.Clear();
        // A handed-off order stops before the details; items it cannot enter are left out.
        var items = ImportedItem.InEntryOrder(order.Items.Where(i => i.ItemCode.Length > 0),
            [_settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode]);
        var rowCount = Math.Max(DefaultDetailRows, items.Count + 1);
        for (var i = 0; i < rowCount; i++) _details.Rows.Add();
        for (var i = 0; i < items.Count; i++)
        {
            _details["ItemCode", i].Value = items[i].ItemCode;
            _details["Quantity", i].Value = items[i].Quantity;
            _details["UnitPrice", i].Value = items[i].UnitPrice;
            _details["BundleKey", i].Value = items[i].BundleTotal is not null || (items[i].SortKey is not null && items[i].UnitPrice.Length == 0) ? items[i].SortKey : null;
            _details["BundleTotal", i].Value = items[i].BundleTotal;
            _details["BundleGap", i].Value = items[i].TakesBundleGap ? "1" : null;
            _details["GiftQuantity", i].Value = items[i].GiftQuantity.Length > 0 ? items[i].GiftQuantity : null;
        }
        // 滿件贈送 (user, 2026-10-14): the free quantity per the settings' cards.
        var giftMissing = new List<string>();
        foreach (var (row, gift) in GiftRules.Gifts(order.Source, items.Select(i => (i.ItemCode, i.Quantity)).ToList(), _settings.GiftRules, giftMissing))
        {
            // Added to a 「(贈)」 quantity already on the row.
            var marked = decimal.TryParse(items[row].GiftQuantity, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 0;
            _details["GiftQuantity", row].Value = (marked + gift).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _log.Info("import", $"gift quantity row={row + 1} item={_log.Value(items[row].ItemCode)} gift={gift}");
        }
        foreach (var note in giftMissing) _log.Warn("import", _log.Value(note));
        ApplyDefaultsToBlankFields();
        _loadedOrder = order;
    }

    /// <summary>
    /// Duplicate check before a single order (user, 2026-10-10): an order CY saved is blocked;
    /// one entered without saving or handed off only warns.
    /// </summary>
    private bool ConfirmNotDuplicate(ImportedOrder order)
    {
        var entry = _ledger?.Find(order.LedgerKey);
        if (entry is null) return true;
        if (entry.Status == LedgerStatus.Saved)
        {
            _automationSession?.End();
            MessageBox.Show(this, $"{order.Source}訂單 {order.OrderSn} {entry.Describe()}。\n\n為避免重複打單，已擋下。",
                "重複訂單", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _automationSession?.End();
        return MessageBox.Show(this,
            $"{order.Source}訂單 {order.OrderSn} {entry.Describe()}。\n\n請先確認 ERP 沒有這張單。仍要再輸入一次嗎？",
            "可能重複", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    /// <summary>Writes a run's outcome to the ledger; a write failure is logged and reported, never fatal.</summary>
    private string RecordOutcome(ImportedOrder order, AutomationRunResult result)
    {
        var status = result.HandedOff.Length > 0 ? LedgerStatus.HandedOff
            : result.Saved ? LedgerStatus.Saved
            : LedgerStatus.Entered;
        try
        {
            _ledger?.Record(order.LedgerKey, status, result.DocumentKey);
            return string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("ledger", ex);
            return $"{order.OrderSn}：防重複紀錄寫入失敗（{ex.Message}），下次匯入不會提醒這張已輸入";
        }
    }

    /// <summary>
    /// Enters every order in turn; each must be saved before the next starts. When an
    /// order fails, the document CY created for it is abandoned (PROJECT_RULES §1) and the
    /// batch continues; orders to hand off and orders CY already saved are skipped and
    /// listed. The batch only stops when abandoning is not safe or on Esc. Ends with saved /
    /// review / handoff / duplicate / failed / missing tracking / stopped / not started lists.
    /// </summary>
    /// <returns>False when the batch did not start, so the preview comes back.</returns>
    /// <param name="notes">More for the closing list (MO店+ 全自動: other carriers, 總表 to void, invoices).</param>
    private async Task<bool> RunImportBatchAsync(string source, IReadOnlyList<ImportedOrder> orders, OrderLedger ledger, IReadOnlyList<string>? notes = null)
    {
        var earlier = orders
            .Where(o => o.HandoffReason.Length == 0 && ledger.Find(o.LedgerKey) is { Status: not LedgerStatus.Saved })
            .ToList();
        var skipEarlier = false;
        if (earlier.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"以下 {earlier.Count} 張曾經輸入，但不是由本程式儲存（可能已人工儲存）：\n" +
                string.Join("\n", earlier.Take(15).Select(o => $"・{o.OrderSn}：{ledger.Find(o.LedgerKey)!.Describe()}")) +
                (earlier.Count > 15 ? $"\n…另 {earlier.Count - 15} 張" : string.Empty) +
                "\n\n是：仍要輸入　否：略過這些　取消：不開始批次",
                "可能重複", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Cancel) return false;
            skipEarlier = answer == DialogResult.No;
        }

        // Every order to enter is checked before the first one starts, so a missing value is
        // fixed in the preview rather than found mid-batch (user, 2026-10-12).
        var invalid = new List<string>();
        foreach (var order in orders)
        {
            if (order.HandoffReason.Length > 0) continue;
            if (ledger.Find(order.LedgerKey) is { } seen && (seen.Status == LedgerStatus.Saved || skipEarlier)) continue;
            LoadOrder(order);
            if (CreateSnapshot(out var validationError) is null) invalid.Add($"{order.OrderSn}：{validationError}");
        }
        if (invalid.Count > 0)
        {
            ResetForm();
            MessageBox.Show(this, $"以下 {invalid.Count} 張資料不完整，請在匯入視窗補齊後再開始：\n\n" +
                string.Join("\n", invalid.Take(15).Select(i => "・" + i)) +
                (invalid.Count > 15 ? $"\n…另 {invalid.Count - 15} 張" : string.Empty),
                "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!ConfirmRestartIfErpBusy(out var abandonFirst)) return false;

        _pickList = NewPickList();
        var done = new List<string>();
        var review = new List<string>();
        var failed = new List<string>();
        var handOff = new List<string>();
        var duplicates = new List<string>();
        var noTracking = new List<string>();
        string? stoppedAt = null;
        var next = 0;

        await RunGuardedAsync(async token =>
        {
            if (abandonFirst) await AbandonCurrentErpDocumentAsync(token);
            for (; next < orders.Count; next++)
            {
                token.ThrowIfCancellationRequested();
                var order = orders[next];
                // Orders for a person are not entered in a batch at all; they are listed at
                // the end (user, 2026-10-10).
                if (order.HandoffReason.Length > 0)
                {
                    handOff.Add($"{order.OrderSn}：{order.HandoffReason}");
                    _log.Info("import", $"batch order skipped for handoff index={next + 1}/{orders.Count}");
                    continue;
                }
                var entry = ledger.Find(order.LedgerKey);
                if (entry is not null && (entry.Status == LedgerStatus.Saved || skipEarlier))
                {
                    duplicates.Add($"{order.OrderSn}：{entry.Describe()}");
                    _log.Info("import", $"batch order skipped as duplicate index={next + 1}/{orders.Count} status={entry.Status}");
                    continue;
                }
                LoadOrder(order);
                SetStatus($"{source}批次 {next + 1}/{orders.Count}：訂單 {order.OrderSn}");
                var snapshot = CreateSnapshot(out var validationError);
                if (snapshot is null) { failed.Add($"{order.OrderSn}：{validationError}"); continue; }

                AutomationRunResult result;
                try
                {
                    result = await RunDocumentAsync(snapshot, autoSave: true, token);
                }
                catch (OperationCanceledException)
                {
                    stoppedAt = $"訂單 {order.OrderSn}：已按 Esc 停止，這張可能已部分輸入，請在 ERP 確認";
                    throw;
                }
                catch (Exception ex)
                {
                    _log.Error("import", ex);
                    var createdNumber = _automation.LastSalesOrderNumber;
                    if (createdNumber.Length == 0 && _automation.SelectCopi08(out _) != 0 &&
                        _automation.ProbeStatus().Mode == ErpMode.Browse)
                    {
                        failed.Add($"{order.OrderSn}：{ex.Message}");
                        continue; // failed before ERP opened a document; nothing to abandon
                    }
                    try
                    {
                        using (PhysicalMouseLock.Engage(_log)) await _automation.AbandonInputAsync(createdNumber, token);
                        failed.Add($"{order.OrderSn}：{ex.Message}（ERP 單據已放棄）");
                        continue;
                    }
                    catch (Exception abandonError) when (abandonError is not OperationCanceledException)
                    {
                        _log.Error("import", abandonError);
                        stoppedAt = $"訂單 {order.OrderSn}：{ex.Message}；無法安全放棄這張 ERP 單據（{abandonError.Message}），請人工處理";
                        return;
                    }
                }
                var ledgerProblem = RecordOutcome(order, result);
                if (ledgerProblem.Length > 0) review.Add(ledgerProblem);
                if (!result.Saved)
                {
                    stoppedAt = $"訂單 {order.OrderSn}：未儲存（{result.SaveSkippedReason}）";
                    return;
                }
                done.Add($"{order.OrderSn} → {DocumentKeyText(result)}" + (order.InvoiceNo.Trim().Length > 0 ? $"，發票 {order.InvoiceNo.Trim()}" : string.Empty));
                ClearFormIfSaved(result);
                if (order.TrackingNumber.Trim().Length == 0) noTracking.Add($"{order.OrderSn} → {DocumentKeyText(result)}");
                review.AddRange(result.Warnings.Select(w => $"{order.OrderSn}（{DocumentKeyText(result)}）第 {w.DetailRow} 列 {w.ItemCode}：{w.Message}"));
                _log.Info("import", $"batch order saved index={next + 1}/{orders.Count} document={_log.Value(DocumentKeyText(result))}");
            }
        });

        string? stopped = null;
        if (stoppedAt is not null)
        {
            stopped = "停止於：" + stoppedAt;
            var rest = orders.Skip(next + 1).Select(o => o.OrderSn).ToList();
            if (rest.Count > 0) stopped += $"\r\n未處理 {rest.Count} 張：{string.Join("、", rest)}（處理完 ERP 上這張單後再重新匯入）";
        }
        SetStatus($"{source}批次結束：已儲存 {done.Count}/{orders.Count} 張");
        await FinishBatchAsync($"{source}批次");
        using var summary = new BatchSummaryForm($"{source}批次結果", $"已儲存 {done.Count} / {orders.Count} 張。",
        [
            new($"已完成 {done.Count} 張：", done),
            new($"需回補物流單號 {noTracking.Count} 張（送貨地址(一) 空白）：", noTracking),
            new("需人工確認：", review),
            new($"需人工處理 {handOff.Count} 張（未輸入 ERP）：", handOff),
            new($"重複略過 {duplicates.Count} 張（未輸入 ERP）：", duplicates),
            new($"失敗 {failed.Count} 張（未儲存，已略過）：", failed),
            new("其他事項：", notes ?? [])
        ], stopped);
        summary.ShowDialog(this);
        EndBatchHold();
        return true;
    }

    private void ShowRunSummaryIfNeeded(AutomationRunResult result, string documentKey, IReadOnlyList<string>? notes = null)
    {
        notes ??= [];
        if (result.Warnings.Count == 0 && result.SaveSkippedReason.Length == 0 && result.PrintProblem.Length == 0 && notes.Count == 0) return;

        var lines = new List<string> { $"銷貨單：{documentKey}", $"儲存：{(result.Saved ? "已儲存" : "尚未儲存")}" };
        if (result.SaveSkippedReason.Length > 0) lines.Add($"未自動儲存原因：{result.SaveSkippedReason}");
        if (result.PrintProblem.Length > 0) lines.Add($"未自動列印：{result.PrintProblem}");
        if (result.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("需人工確認：");
            lines.AddRange(result.Warnings.Select(w => $"・第 {w.DetailRow} 列 {w.ItemCode}：{w.Message}"));
        }
        if (notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(notes.Select(n => "・" + n));
        }
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "需人工確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearForm()
    {
        if (_automationCts is not null) return;
        if (MessageBox.Show(this, "清除所有欄位與商品明細？（本機預設值會重新帶入）", "清除表單",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        ResetForm();
        SetStatus("已清除表單");
    }

    private void ResetForm()
    {
        foreach (var control in _valueControls.Values)
        {
            if (control is CheckBox checkBox) checkBox.Checked = false;
            else SetFieldText(control, string.Empty);
        }
        _details.EndEdit();
        _details.Rows.Clear();
        for (var i = 0; i < DefaultDetailRows; i++) _details.Rows.Add();
        ApplyDefaultsToBlankFields();
        _loadedOrder = null;
    }

    private void UpdateBuildStatus() =>
        _buildStatus.Text = $"{AppVersionInfo.Display} · Esc：緊急停止 · 自動儲存：{(_settings.AutoSave ? "開" : "關")}";

    private FormSnapshot? CreateSnapshot(out string validationError)
    {
        _details.EndEdit();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in FieldCatalog.All)
        {
            if (!_modeToggle.Checked && !field.Standard) continue;
            var control = _valueControls[field.Key];
            string value;
            if (control is CheckBox cb) value = cb.Checked ? "true" : string.Empty;
            else value = control.Text.Trim();

            if (value.Length > 0 && field.Kind == FieldKind.Date)
            {
                if (!InputRules.TryNormalizeValidDate(value, out var normalizedDate))
                {
                    validationError = $"{field.Label}必須是有效日期（YYYY/MM/DD）。";
                    control.Focus();
                    return null;
                }
                value = normalizedDate;
            }

            if (field.Key == "order_type" && value.Length > 4)
            {
                validationError = "銷貨單別最多 4 個字元。";
                control.Focus();
                return null;
            }

            if (value.Length > 0) values[field.Key] = value;
        }

        var details = new List<DetailRow>();
        var firstDetailGridRow = -1;
        for (var r = 0; r < _details.Rows.Count; r++)
        {
            var row = _details.Rows[r];
            if (row.IsNewRow) continue;
            var d = new DetailRow
            {
                ItemCode = Cell(row, "ItemCode"),
                Unit = Cell(row, "Unit"),
                Quantity = Cell(row, "Quantity"),
                GiftQuantity = Cell(row, "GiftQuantity"),
                Batch = Cell(row, "Batch"),
                Warehouse = Cell(row, "Warehouse"),
                UnitPrice = Cell(row, "UnitPrice"),
                BundleKey = Cell(row, "BundleKey"),
                BundleTotal = Cell(row, "BundleTotal"),
                TakesBundleGap = Cell(row, "BundleGap") == "1"
            };
            if (!d.HasAnyData) continue;

            if (string.IsNullOrWhiteSpace(d.ItemCode) || string.IsNullOrWhiteSpace(d.Quantity))
            {
                var missing = string.Join("、", new[]
                {
                    string.IsNullOrWhiteSpace(d.ItemCode) ? "品號" : null,
                    string.IsNullOrWhiteSpace(d.Quantity) ? "數量" : null
                }.Where(x => x is not null));
                validationError = $"第 {r + 1} 列已有資料，但缺少 {missing}。\n有資料的列必須同時有品號＋數量。";
                _details.CurrentCell = _details[string.IsNullOrWhiteSpace(d.ItemCode) ? "ItemCode" : "Quantity", r];
                return null;
            }
            if (details.Count == 0) firstDetailGridRow = r;
            details.Add(d);
        }

        // Fallback for a form whose first data row is not the grid's first row.
        if (_settings.FirstRowWarehouseEnabled && _settings.FirstRowWarehouse.Length > 0 &&
            details.Count > 0 && string.IsNullOrWhiteSpace(details[0].Warehouse))
        {
            details[0].Warehouse = _settings.FirstRowWarehouse;
            _details["Warehouse", firstDetailGridRow].Value = _settings.FirstRowWarehouse;
        }

        // The shipping item's amount goes to 送貨資料・運費 when that field was left blank
        // (user, 2026-10-10), whatever the order's source.
        var shippingCode = _settings.ShippingItemCode;
        if (shippingCode.Length > 0 && !values.ContainsKey("freight_fee"))
        {
            var shipping = details.FirstOrDefault(d => d.ItemCode.Equals(shippingCode, StringComparison.OrdinalIgnoreCase));
            if (shipping is not null && InputRules.TryShippingAmount(shipping.Quantity, shipping.UnitPrice, out var fee))
            {
                values["freight_fee"] = fee;
                if (_valueControls.TryGetValue("freight_fee", out var feeControl)) SetFieldText(feeControl, fee);
                _log.Info("form", "freight_fee filled from the shipping item");
            }
        }

        if (values.Count == 0 && details.Count == 0)
        {
            validationError = "尚未輸入任何要送到 ERP 的資料。";
            return null;
        }

        validationError = string.Empty;
        var comboOptions = ErpComboOptions.ByField.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        var noBatch = new HashSet<string>(new[] { _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode }
            .Select(c => c.Trim()).Where(c => c.Length > 0), StringComparer.OrdinalIgnoreCase);
        return new FormSnapshot { Values = values, Details = details, ComboOptions = comboOptions, HandoffBeforeDetails = _loadedOrder?.HandoffReason ?? string.Empty, NoBatchItemCodes = noBatch };
    }

    private static string Cell(DataGridViewRow row, string column) =>
        Convert.ToString(row.Cells[column].Value)?.Trim() ?? string.Empty;

    private async Task WatchGlobalEscapeAsync(CancellationTokenSource cts)
    {
        var wasDown = false;
        while (!cts.IsCancellationRequested)
        {
            var down = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
            // An Esc CY sent itself (abandoning an ERP document) is not an emergency stop.
            if (down && !wasDown && !InputSender.IsRecentSyntheticEscape)
            {
                cts.Cancel();
                break;
            }
            wasDown = down;
            try { await Task.Delay(45, cts.Token); } catch { break; }
        }
    }

    private Button ImportButton(string text)
    {
        Button button = text switch
        {
            "蝦皮" => new ShopeeButton(),
            "MO店+" => new MoStoreButton(),
            "酷澎商城" => new CoupangButton(),
            _ => new CyButton { Text = text }
        };
        button.Size = new Size(120, ToolbarButtonHeight);
        button.Font = new Font(Font.FontFamily, 10.5F);
        button.Margin = new Padding(4, 1, 4, 1);
        if (text == "蝦皮")
        {
            // Screenshots (OCR) are the main way for now; the official export stays available.
            var menu = new ContextMenuStrip { Font = new Font("Microsoft JhengHei UI", 9.5F) };
            // 全自動 / 自動擷取＋匯入 (user, 2026-10-15; both 蝦皮 tests passed and were removed).
            menu.Items.Add("全自動（安排出貨＋寄件單＋ERP）", null, async (_, _) => await RunShopeeBatchAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add($"自動擷取輸入 ({BrowserHotkey.KeyName})", null, async (_, _) => await ImportShopeeScreenshotsAsync());
            menu.Items.Add("匯入訂單匯出檔（.xlsx）", null, async (_, _) => await ImportShopeeAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("測試：從寄件編號視窗接續（列印寄件單 → ERP）", null, async (_, _) => await RunShopeeBatchAsync(resume: true));
            button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        }
        else if (text == "MO店+")
        {
            var menu = new ContextMenuStrip { Font = new Font("Microsoft JhengHei UI", 9.5F) };
            // 全自動 / 匯入＋重印 (user, 2026-10-15); the import is the former 測試：發票 (user, 2026-10-19).
            menu.Items.Add("全自動（匯出＋批次出貨＋發票＋列印＋ERP）", null, async (_, _) => await RunMoBatchAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("匯入訂單匯出檔（兩個 .xls → 發票 → 預覽 → ERP）", null, async (_, _) => await ImportMoAsync());
            menu.Items.Add("重新列印寄件單", null, async (_, _) => await ReprintMoAsync());
            button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        }
        else if (text == "酷澎商城")
        {
            // 全自動 / 匯入 (user, 2026-10-19: 全自動 from the Wing screenshots).
            var menu = new ContextMenuStrip { Font = new Font("Microsoft JhengHei UI", 9.5F) };
            menu.Items.Add("全自動（確認訂單＋安排配送＋寄件單＋發票＋ERP）", null, async (_, _) => await RunCoupangBatchAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("匯入訂單匯出檔（.xlsx → 發票 → 列印 → ERP）", null, async (_, _) => await ImportCoupangAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("列印寄件單（Wing 先打開寄件詳情）", null, async (_, _) => await PrintCoupangSlipAsync());

            button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        }
        return button;
    }

    private const int ToolbarButtonHeight = 38;

    private Button MakeButton(string text, int width) => new CyButton
    {
        Text = text,
        Font = new Font(Font.FontFamily, 10.5F),
        Size = new Size(width, ToolbarButtonHeight),
        Margin = new Padding(4, 1, 4, 1)
    };

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }
        _status.Text = text;
    }
}
