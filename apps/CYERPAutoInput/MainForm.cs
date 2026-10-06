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
        _log.Diagnostic = _settings.DiagnosticLogging;
        UpdateBuildStatus();

        Text = "CYERPAutoInput — SMART ERP 自動輸入工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = StandardWindowSize;
        Size = StandardWindowSize;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        KeyPreview = true;

        BuildUi();
        ApplyDefaultsToBlankFields();
        _modeToggle.Checked = _settings.AdvancedMode;
        ApplyMode(_settings.AdvancedMode);

        _statePoll.Tick += (_, _) => RefreshErpState();
        Shown += (_, _) =>
        {
            UpdateDpiHint();
            RefreshErpState();
            _statePoll.Start();
        };
        DpiChanged += (_, _) => UpdateDpiHint();
        FormClosing += (_, _) => _statePoll.Stop();
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
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
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

        var find = MakeButton("尋找 ERP", 90);
        find.Click += (_, _) =>
        {
            var windows = Win32Automation.FindCopi08Windows();
            if (windows.Count == 0) SetStatus("ERP：找不到 COPI08 視窗");
            else if (windows.Count > 1) SetStatus($"ERP：偵測到 {windows.Count} 個 COPI08 視窗，請只保留一個");
            else
            {
                Win32Automation.PrepareForeground(windows[0], _log);
                SetStatus("ERP：已找到 COPI08");
            }
        };
        actions.Controls.Add(find);

        _start.Text = "開始輸入 ERP";
        _start.Size = new Size(122, 34);
        _start.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        _start.Margin = new Padding(4, 1, 4, 1);
        _start.Click += async (_, _) => await StartAutomationAsync();
        actions.Controls.Add(_start);

        actions.Controls.Add(new Label
        {
            Text = "訂單匯入：",
            AutoSize = true,
            Padding = new Padding(0, 9, 0, 0),
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

        var clearButton = MakeButton("清除表單", 90);
        clearButton.Click += (_, _) => ClearForm();
        rightTools.Controls.Add(clearButton);

        var settingsButton = MakeButton("設定", 76);
        settingsButton.Click += (_, _) => OpenSettings();
        rightTools.Controls.Add(settingsButton);

        _modeToggle.Margin = new Padding(4, 1, 8, 1);
        _modeToggle.CheckedChanged += (_, _) => ApplyMode(_modeToggle.Checked);
        rightTools.Controls.Add(_modeToggle);

        _erpStateTag.Size = new Size(170, 34);
        _erpStateTag.Margin = new Padding(4, 1, 12, 1);
        _erpStateTag.Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold);
        _erpStateTag.AccessibleName = "ERP 單據狀態";
        rightTools.Controls.Add(_erpStateTag);

        var fieldsHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            AutoScroll = false,
            Padding = new Padding(0, 4, 0, 4),
            Margin = Padding.Empty
        };
        for (var i = 0; i < 4; i++) fieldsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _root.Controls.Add(fieldsHost, 0, 1);

        var groups = new[] { "表頭", "交易資料", "送貨資料", "發票資料(一)" };
        for (var i = 0; i < groups.Length; i++)
        {
            var box = new GroupBox
            {
                Text = groups[i] == "發票資料(一)" ? "發票資料" : groups[i],
                Dock = DockStyle.Fill,
                Padding = new Padding(7),
                Margin = new Padding(i == 0 ? 0 : 4, 0, i == groups.Length - 1 ? 0 : 4, 0)
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(3, 2, 0, 2),
                Margin = Padding.Empty
            };
            box.Controls.Add(flow);
            fieldsHost.Controls.Add(box, i, 0);
            _groupFlows[groups[i]] = flow;
        }
        foreach (var field in FieldCatalog.All) AddField(field);

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
        _logLink.Click += (_, _) => OpenLogFolder();
        statusStrip.Items.Add(_logLink);
        _root.Controls.Add(statusStrip, 0, 3);
    }

    private void AddField(FieldDefinition field)
    {
        const int labelWidth = 68;
        const int inputLeft = 76;
        var flow = _groupFlows[field.Group];
        var row = new Panel { Width = 238, Height = 28, Margin = new Padding(1) };
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

    private void ConfigureDetailGrid()
    {
        _details.Dock = DockStyle.Fill;
        _details.AllowUserToAddRows = true;
        _details.AllowUserToDeleteRows = true;
        _details.AutoGenerateColumns = false;
        _details.RowHeadersVisible = false;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _details.EditMode = DataGridViewEditMode.EditOnEnter;
        _details.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _details.ColumnHeadersHeight = 29;
        _details.RowTemplate.Height = 27;

        _details.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Sequence",
            HeaderText = "序號",
            Width = 55,
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

        for (var i = 0; i < 10; i++) _details.Rows.Add();
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
            Size = StandardWindowSize;
        }

        _root.PerformLayout();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _settingsStore.Save(_settings);
            _log.Diagnostic = _settings.DiagnosticLogging;
            UpdateBuildStatus();
            _modeToggle.Checked = _settings.AdvancedMode;
            ApplyMode(_settings.AdvancedMode);
            ApplyDefaultsToBlankFields();
            SetStatus("設定：已儲存本機設定");
        }
        catch (Exception ex)
        {
            _log.Error("settings", ex);
            MessageBox.Show(this, "本機設定儲存失敗，請查看 LOG。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyDefaultsToBlankFields()
    {
        foreach (var pair in _settings.Defaults)
        {
            if (!_valueControls.TryGetValue(pair.Key, out var control) || control is CheckBox) continue;
            if (string.IsNullOrWhiteSpace(control.Text)) control.Text = pair.Value;
        }
    }

    private async Task StartAutomationAsync()
    {
        if (_automationCts is not null) return;
        var snapshot = CreateSnapshot(out var validationError);
        if (snapshot is null)
        {
            MessageBox.Show(this, validationError, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _automationCts = new CancellationTokenSource();
        _start.Enabled = false;
        var token = _automationCts.Token;
        var watcher = WatchGlobalEscapeAsync(_automationCts);
        try
        {
            var progress = new Progress<string>(SetStatus);
            var result = await _automation.RunAsync(snapshot, _settings.AutoSave, progress, token);
            var documentKey = string.IsNullOrWhiteSpace(result.DocumentKey) ? "未取得" : result.DocumentKey;
            var saveText = result.Saved ? "已儲存" : "尚未儲存";
            SetStatus(result.Warnings.Count > 0
                ? $"ERP：銷貨單 {documentKey} 輸入完成；{result.Warnings.Count} 筆需人工確認；{saveText}"
                : $"ERP：銷貨單 {documentKey} 輸入完成；{saveText}");
            ShowRunSummaryIfNeeded(result, documentKey);
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

    private void ShowRunSummaryIfNeeded(AutomationRunResult result, string documentKey)
    {
        if (result.Warnings.Count == 0 && result.SaveSkippedReason.Length == 0) return;

        var lines = new List<string> { $"銷貨單：{documentKey}", $"儲存：{(result.Saved ? "已儲存" : "尚未儲存")}" };
        if (result.SaveSkippedReason.Length > 0) lines.Add($"未自動儲存原因：{result.SaveSkippedReason}");
        if (result.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("需人工確認：");
            lines.AddRange(result.Warnings.Select(w => $"・第 {w.DetailRow} 列 {w.ItemCode}：{w.Message}"));
        }
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "需人工確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearForm()
    {
        if (_automationCts is not null) return;
        if (MessageBox.Show(this, "清除所有欄位與商品明細？（本機預設值會重新帶入）", "清除表單",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        foreach (var control in _valueControls.Values)
        {
            if (control is CheckBox checkBox) checkBox.Checked = false;
            else control.Text = string.Empty;
        }
        ApplyDefaultsToBlankFields();
        _details.EndEdit();
        _details.Rows.Clear();
        for (var i = 0; i < 10; i++) _details.Rows.Add();
        SetStatus("已清除表單");
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
            details.Add(d);
        }

        if (values.Count == 0 && details.Count == 0)
        {
            validationError = "尚未輸入任何要送到 ERP 的資料。";
            return null;
        }

        validationError = string.Empty;
        return new FormSnapshot { Values = values, Details = details };
    }

    private static string Cell(DataGridViewRow row, string column) =>
        Convert.ToString(row.Cells[column].Value)?.Trim() ?? string.Empty;

    private async Task WatchGlobalEscapeAsync(CancellationTokenSource cts)
    {
        var wasDown = false;
        while (!cts.IsCancellationRequested)
        {
            var down = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
            if (down && !wasDown)
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
        button.Size = new Size(90, 34);
        button.Margin = new Padding(4, 1, 4, 1);
        button.Click += (_, _) => MessageBox.Show(this,
            $"{text} 匯入解析會在 C# ERP 核心驗收後接續；入口固定保留。",
            "訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return button;
    }

    private static Button MakeButton(string text, int width) => new CyButton
    {
        Text = text,
        Size = new Size(width, 34),
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
