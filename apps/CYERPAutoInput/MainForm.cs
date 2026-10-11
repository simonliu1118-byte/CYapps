namespace CYERPAutoInput;

internal sealed class MainForm : Form
{
    private static readonly Size StandardWindowSize = new(1160, 720);

    private readonly AppLogger _log;
    private readonly ErpAutomationService _automation;
    private readonly UserSettingsStore _settingsStore;
    private readonly UserSettings _settings;
    private readonly Dictionary<string, Control> _valueControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Panel> _fieldRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FlowLayoutPanel> _groupFlows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DetailDataGridView _details = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _buildStatus = new();
    private readonly ToolStripStatusLabel _logLink = new() { Text = "開啟 LOG 資料夾", IsLink = true };
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
        FormClosing += (_, _) => _statePoll.Stop();
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
        _logLink.Click += (_, _) => OpenLogFolder();
        statusStrip.Items.Add(_logLink);
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
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _settingsStore.Save(_settings);
            UpdateBuildStatus();
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

    /// <param name="fullAuto">Shopee full auto (user, 2026-10-11): save and print whatever the settings say.</param>
    private async Task StartAutomationAsync(bool fullAuto = false)
    {
        if (_automationCts is not null) return;
        var snapshot = CreateSnapshot(out var validationError);
        if (snapshot is null)
        {
            MessageBox.Show(this, validationError, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var order = _loadedOrder;
        if (order is not null && !ConfirmNotDuplicate(order)) return;
        // Imported orders were warned in the preview; a manual form is warned here.
        if (order is null && snapshot.Values.ContainsKey("tax_id") && !snapshot.Values.ContainsKey("inv_name") &&
            MessageBox.Show(this, "有統一編號，但沒有發票的客戶全名（經濟部查無或未填）。\n\n建議先填寫客戶全名；仍要開始輸入嗎？",
                "客戶全名", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        if (!ConfirmRestartIfErpBusy(out var abandonFirst)) return;

        await RunGuardedAsync(async token =>
        {
            if (abandonFirst) await AbandonCurrentErpDocumentAsync(token);
            var result = await RunDocumentAsync(snapshot, fullAuto || _settings.AutoSave, token, print: fullAuto ? true : null);
            var notes = new List<string>();
            if (order is not null)
            {
                var ledgerProblem = RecordOutcome(order, result);
                if (ledgerProblem.Length > 0) notes.Add(ledgerProblem);
                if (order.TrackingNumber.Trim().Length == 0) notes.Add("物流單號空白：取得後請回補到送貨地址(一)。");
            }
            if (result.HandedOff.Length > 0)
            {
                MessageBox.Show(this, $"銷貨單 {DocumentKeyText(result)} 已輸入單頭後停止，請人工輸入明細並儲存。\n\n{result.HandedOff}" +
                    (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : string.Empty),
                    "轉人工處理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowRunSummaryIfNeeded(result, DocumentKeyText(result), notes);
            // A saved document clears the form for the next order, after any summary was shown;
            // so does an unsaved run with nothing left to check (user, 2026-10-11).
            if (result.Saved || (result.Warnings.Count == 0 && result.SaveSkippedReason.Length == 0 && result.PrintProblem.Length == 0 && notes.Count == 0))
            {
                ResetForm();
                SetStatus($"ERP：銷貨單 {DocumentKeyText(result)} 輸入完成；{(result.Saved ? "已儲存" : "尚未儲存")}；表單已清空");
            }
        });
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
            SetStatus("ERP：自動輸入已停止；CY 未按 ERP 取消或儲存");
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
    /// Safeguard (user, 2026-10-10): when ERP is already in 新增／修改, CY asks before
    /// starting; on confirmation it first abandons that document (ERP back to 檢視) and then
    /// runs the normal flow from 新增. Returns false when the user declines.
    /// </summary>
    private bool ConfirmRestartIfErpBusy(out bool abandonFirst)
    {
        abandonFirst = false;
        RefreshErpState();
        var state = _shownState;
        if (state is not (ErpDocumentState.New or ErpDocumentState.Modify or ErpDocumentState.InputUnconfirmed)) return true;

        var what = state switch
        {
            ErpDocumentState.New => "新增",
            ErpDocumentState.Modify => "修改",
            _ => "新增或修改"
        };
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
        // The physical mouse is ignored while CY types (user, 2026-10-11); Esc releases it.
        using (PhysicalMouseLock.Engage(_log)) result = await _automation.RunAsync(snapshot, autoSave, progress, token, autoSave && (print ?? _settings.AutoPrint));
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

    private static string DocumentKeyText(AutomationRunResult result) =>
        string.IsNullOrWhiteSpace(result.DocumentKey) ? "未取得" : result.DocumentKey;

    private async Task ImportShopeeAsync()
    {
        if (_automationCts is not null) return;
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

        var mapping = new ShopeeMapping(_settings.ShopeeOrderType, _settings.ShopeeCustomerCode, _settings.ShopeeNotePrefix, _settings.FreightTypes);
        var orders = parsed.Orders.Select(o => ShopeeOrderImport.ToImported(o, mapping)).ToList();
        await PreviewAndRunAsync(ShopeeOrderImport.Source, Path.GetFileName(path), orders, parsed.Errors);
    }

    /// <summary>
    /// One Shopee order read from screenshots onto the form (user, 2026-10-11): no batch and
    /// no automatic start; anything to check is listed and the user starts the input.
    /// </summary>
    private async Task ImportShopeeScreenshotsAsync()
    {
        if (_automationCts is not null) return;
        if (_settings.ShopeeOrderType.Length == 0 || _settings.ShopeeCustomerCode.Length == 0)
        {
            MessageBox.Show(this, "請先在「設定」填入蝦皮的銷貨單別與客戶代號。", "蝦皮訂單截圖輸入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var ledger = LoadLedger();
        if (ledger is null) return;

        var mapping = new ShopeeShotMapping(_settings.ShopeeOrderType, _settings.ShopeeCustomerCode, _settings.ShopeeNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.PromotionItemCode, _settings.FreightTypes);
        using var dialog = new ShopeeScreenshotForm(_automation.Ocr, mapping, _log);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result?.Order is not { } order) return;

        LoadOrder(order);
        var notes = dialog.Result.Problems.ToList();
        if (ledger.Find(order.LedgerKey) is { } entry) notes.Insert(0, $"這張訂單{entry.Describe()}。");
        if (order.TrackingNumber.Length == 0) notes.Add("物流單號空白：取得後請填到送貨地址(一)。");
        SetStatus($"蝦皮訂單 {order.OrderSn} 已帶入表單；請確認後按「開始輸入 ERP」");
        _log.Info("shopee-shot", $"order loaded items={order.Items.Count} notes={notes.Count}");
        // Full auto (user, 2026-10-11): a clean order goes straight on to ERP, save and print;
        // with notes the user decides, defaulting to stop.
        if (_settings.ShopeeFullAuto)
        {
            if (notes.Count > 0 && MessageBox.Show(this,
                    $"蝦皮訂單 {order.OrderSn} 已帶入表單，但有以下項目需要確認：\n\n" + string.Join("\n", notes.Select(n => "・" + n)) +
                    "\n\n仍要繼續全自動輸入 ERP、儲存並列印嗎？（選「否」可先修改表單，再按「開始輸入 ERP」）",
                    "蝦皮全自動", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _log.Info("shopee-shot", $"full auto continues to ERP order_notes={notes.Count}");
            await StartAutomationAsync(fullAuto: true);
            return;
        }
        // A clean order only gets the status line (user, 2026-10-11); notes still need a look.
        if (notes.Count > 0)
            MessageBox.Show(this,
                $"蝦皮訂單 {order.OrderSn} 已帶入表單，請先確認以下項目，補齊後再按「開始輸入 ERP」：\n\n" + string.Join("\n", notes.Select(n => "・" + n)),
                "蝦皮訂單截圖輸入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task ImportMoAsync()
    {
        if (_automationCts is not null) return;
        var missing = new[]
        {
            _settings.MoOrderType.Length == 0 ? "銷貨單別" : null,
            _settings.MoCustomerCode.Length == 0 ? "客戶代號" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", "MO店+訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = PickExportFile("選擇 MO店+ 訂單匯出檔（OrderExport）", "MO店+ 匯出檔 (*.xls;*.xlsx)|*.xls;*.xlsx");
        if (path is null) return;

        MoImportResult parsed;
        try
        {
            await using var stream = File.OpenRead(path);
            var rows = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? XlsxReader.ReadFirstSheet(stream)
                : XlsReader.ReadFirstSheet(stream, LocalSecret.Unprotect(_settings.MoPasswordProtected));
            parsed = MoOrderImport.Parse(rows);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _log.Warn("mo", $"export read failed: {ex.GetType().Name}");
            MessageBox.Show(this, $"無法讀取這個檔案：{ex.Message}", "MO店+訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _log.Info("mo", $"export parsed orders={parsed.Orders.Count} errors={parsed.Errors.Count}");

        var mapping = new MoMapping(_settings.MoOrderType, _settings.MoCustomerCode, _settings.MoNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.FreightTypes);
        var orders = parsed.Orders.Select(o => MoOrderImport.ToImported(o, mapping)).ToList();
        await PreviewAndRunAsync(MoOrderImport.Source, Path.GetFileName(path), orders, parsed.Errors);
    }

    private async Task ImportCoupangAsync()
    {
        if (_automationCts is not null) return;
        var missing = new[]
        {
            _settings.CoupangOrderType.Length == 0 ? "酷澎銷貨單別" : null,
            _settings.CoupangCustomerCode.Length == 0 ? "酷澎客戶代號" : null,
            _settings.DiscountItemCode.Length == 0 ? "折價券品號" : null,
            _settings.ShippingItemCode.Length == 0 ? "運費品號" : null
        }.Where(x => x is not null).ToList();
        if (missing.Count > 0)
        {
            MessageBox.Show(this, $"請先在「設定」填入{string.Join("、", missing)}。", "酷澎訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var path = PickExportFile("選擇酷澎訂單匯出檔（DeliveryList）", "Excel 活頁簿 (*.xlsx)|*.xlsx");
        if (path is null) return;

        CoupangImportResult parsed;
        try
        {
            await using var stream = File.OpenRead(path);
            parsed = CoupangOrderImport.Parse(XlsxReader.ReadFirstSheet(stream));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _log.Warn("coupang", $"export read failed: {ex.GetType().Name}");
            MessageBox.Show(this, $"無法讀取這個檔案：{ex.Message}", "酷澎訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _log.Info("coupang", $"export parsed orders={parsed.Orders.Count} errors={parsed.Errors.Count}");

        var mapping = new CoupangMapping(_settings.CoupangOrderType, _settings.CoupangCustomerCode, _settings.CoupangNotePrefix,
            _settings.DiscountItemCode, _settings.ShippingItemCode, _settings.FreightTypes);
        var orders = parsed.Orders.Select(o => CoupangOrderImport.ToImported(o, mapping)).ToList();
        await PreviewAndRunAsync(CoupangOrderImport.Source, Path.GetFileName(path), orders, parsed.Errors);
    }

    private string? PickExportFile(string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private async Task PreviewAndRunAsync(string source, string fileName, List<ImportedOrder> orders, IReadOnlyList<string> errors)
    {
        var ledger = LoadLedger();
        if (ledger is null) return;

        using var preview = new OrderImportForm(source, fileName, orders, errors, _settings.AutoSave, o => ledger.Find(o.LedgerKey),
            ban => _companyNames.LookupAsync(ban));
        if (preview.ShowDialog(this) != DialogResult.OK) return;

        if (preview.Action == OrderImportAction.StartSelected && preview.SelectedOrder is not null)
        {
            LoadOrder(preview.SelectedOrder);
            SetStatus($"{source}訂單 {preview.SelectedOrder.OrderSn}：開始輸入 ERP");
            await StartAutomationAsync();
        }
        else if (preview.Action == OrderImportAction.RunAll)
        {
            await RunImportBatchAsync(source, orders, ledger);
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
        var items = order.Items.Where(i => i.ItemCode.Length > 0).ToList();
        var rowCount = Math.Max(DefaultDetailRows, items.Count + 1);
        for (var i = 0; i < rowCount; i++) _details.Rows.Add();
        for (var i = 0; i < items.Count; i++)
        {
            _details["ItemCode", i].Value = items[i].ItemCode;
            _details["Quantity", i].Value = items[i].Quantity;
            _details["UnitPrice", i].Value = items[i].UnitPrice;
        }
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
            MessageBox.Show(this, $"{order.Source}訂單 {order.OrderSn} {entry.Describe()}。\n\n為避免重複打單，已擋下。",
                "重複訂單", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
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
    private async Task RunImportBatchAsync(string source, IReadOnlyList<ImportedOrder> orders, OrderLedger ledger)
    {
        var earlier = orders
            .Where(o => o.HandoffReason.Length == 0 && ledger.Find(o.LedgerKey) is { Status: not LedgerStatus.Saved })
            .ToList();
        var skipEarlier = false;
        if (earlier.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"以下 {earlier.Count} 張曾經輸入，但不是由 CY 儲存（可能已人工儲存）：\n" +
                string.Join("\n", earlier.Take(15).Select(o => $"・{o.OrderSn}：{ledger.Find(o.LedgerKey)!.Describe()}")) +
                (earlier.Count > 15 ? $"\n…另 {earlier.Count - 15} 張" : string.Empty) +
                "\n\n是：仍要輸入　否：略過這些　取消：不開始批次",
                "可能重複", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Cancel) return;
            skipEarlier = answer == DialogResult.No;
        }
        if (!ConfirmRestartIfErpBusy(out var abandonFirst)) return;

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
                    if (createdNumber.Length == 0 && Win32Automation.FindCopi08Windows().Count == 1 &&
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
                done.Add($"{order.OrderSn} → {DocumentKeyText(result)}");
                if (order.TrackingNumber.Trim().Length == 0) noTracking.Add($"{order.OrderSn} → {DocumentKeyText(result)}");
                review.AddRange(result.Warnings.Select(w => $"{order.OrderSn}（{DocumentKeyText(result)}）第 {w.DetailRow} 列 {w.ItemCode}：{w.Message}"));
                _log.Info("import", $"batch order saved index={next + 1}/{orders.Count} document={_log.Value(DocumentKeyText(result))}");
            }
        });

        var lines = new List<string> { $"已儲存 {done.Count} / {orders.Count} 張" };
        lines.AddRange(done.Select(d => "・" + d));
        void Section(string title, List<string> items)
        {
            if (items.Count == 0) return;
            lines.Add(string.Empty);
            lines.Add(title);
            lines.AddRange(items.Select(i => "・" + i));
        }
        Section($"需回補物流單號 {noTracking.Count} 張（送貨地址(一) 空白）：", noTracking);
        Section("需人工確認：", review);
        Section($"需人工處理 {handOff.Count} 張（未輸入 ERP）：", handOff);
        Section($"重複略過 {duplicates.Count} 張（未輸入 ERP）：", duplicates);
        Section($"失敗 {failed.Count} 張（未儲存，已略過）：", failed);
        if (stoppedAt is not null)
        {
            lines.Add(string.Empty);
            lines.Add("停止於：" + stoppedAt);
            var rest = orders.Skip(next + 1).Select(o => o.OrderSn).ToList();
            if (rest.Count > 0) lines.Add($"未處理 {rest.Count} 張：{string.Join("、", rest)}（處理完 ERP 上這張單後再重新匯入）");
        }
        SetStatus($"{source}批次結束：已儲存 {done.Count}/{orders.Count} 張");
        var clean = stoppedAt is null && failed.Count == 0 && review.Count == 0 && handOff.Count == 0 && duplicates.Count == 0 && noTracking.Count == 0;
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), $"{source}批次結果", MessageBoxButtons.OK,
            clean ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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

    private void OpenLogFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _log.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Warn("app", $"open log folder failed: {ex.GetType().Name}");
            MessageBox.Show(this, $"無法開啟 LOG 資料夾：{_log.LogDirectory}", "LOG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
                UnitPrice = Cell(row, "UnitPrice")
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
        return new FormSnapshot { Values = values, Details = details, ComboOptions = comboOptions, HandoffBeforeDetails = _loadedOrder?.HandoffReason ?? string.Empty };
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
            menu.Items.Add("截圖輸入（OCR）", null, async (_, _) => await ImportShopeeScreenshotsAsync());
            menu.Items.Add("匯入匯出檔（.xlsx）", null, async (_, _) => await ImportShopeeAsync());
            button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        }
        else if (text == "MO店+")
            button.Click += async (_, _) => await ImportMoAsync();
        else if (text == "酷澎商城")
            button.Click += async (_, _) => await ImportCoupangAsync();
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
