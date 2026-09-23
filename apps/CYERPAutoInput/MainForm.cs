using System.ComponentModel;

namespace CYERPAutoInput;

internal sealed class MainForm : Form
{
    private readonly AppLogger _log;
    private readonly ErpAutomationService _automation;
    private readonly Dictionary<string, Control> _valueControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Panel> _fieldRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FlowLayoutPanel> _groupFlows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGridView _details = new();
    private readonly Label _status = new();
    private readonly RadioButton _standard = new();
    private readonly RadioButton _advanced = new();
    private readonly Button _start = new();
    private CancellationTokenSource? _automationCts;

    public MainForm(AppLogger log)
    {
        _log = log;
        _automation = new ErpAutomationService(log);
        Text = "CYERPAutoInput V0.1.0 — SMART ERP 自動輸入工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1540, 940);
        Font = new Font("Microsoft JhengHei UI", 9F);
        KeyPreview = true;
        BuildUi();
        ApplyMode(false);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var top = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(top, 0, 0);
        var title = new Label
        {
            Text = "SMART ERP 自動輸入工具",
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(6, 5)
        };
        top.Controls.Add(title);
        var sub = new Label
        {
            Text = "V0.1.0 · C# / .NET 8 · 新增模式 · Esc 緊急停止 · 目前不自動儲存 ERP",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(8, 34)
        };
        top.Controls.Add(sub);

        _standard.Text = "標準模式";
        _standard.AutoSize = true;
        _standard.Checked = true;
        _standard.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _standard.Location = new Point(1070, 18);
        _standard.CheckedChanged += (_, _) => { if (_standard.Checked) ApplyMode(false); };
        top.Controls.Add(_standard);
        _advanced.Text = "進階模式";
        _advanced.AutoSize = true;
        _advanced.Location = new Point(1160, 18);
        _advanced.CheckedChanged += (_, _) => { if (_advanced.Checked) ApplyMode(true); };
        top.Controls.Add(_advanced);
        var settings = new Button { Text = "設定", Size = new Size(100, 34), Location = new Point(1270, 10), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        settings.Click += (_, _) => MessageBox.Show(this, "目前 C# 重寫階段保留本機設定介面入口；下拉選項仍以現場 ERP／本機資料為準，不寫入 public source。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        top.Controls.Add(settings);
        top.Resize += (_, _) =>
        {
            settings.Left = top.ClientSize.Width - settings.Width - 8;
            _advanced.Left = settings.Left - _advanced.Width - 20;
            _standard.Left = _advanced.Left - _standard.Width - 20;
        };

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 7, 4, 4) };
        root.Controls.Add(actions, 0, 1);
        var find = MakeButton("尋找 ERP", 110);
        find.Click += (_, _) =>
        {
            var hwnd = _automation.FindErp();
            if (hwnd == 0) SetStatus("ERP：找不到 COPI08 視窗");
            else
            {
                Win32Automation.PrepareForeground(hwnd, _log);
                SetStatus("ERP：已找到 COPI08");
            }
        };
        actions.Controls.Add(find);
        var state = MakeButton("偵測狀態", 110);
        state.Click += (_, _) =>
        {
            var hwnd = _automation.FindErp();
            SetStatus(hwnd == 0 ? "ERP：找不到 COPI08" : $"ERP：{_automation.DetectMode(hwnd)}");
        };
        actions.Controls.Add(state);
        _start.Text = "開始輸入 ERP";
        _start.Size = new Size(170, 38);
        _start.Font = new Font(Font, FontStyle.Bold);
        _start.Click += async (_, _) => await StartAutomationAsync();
        actions.Controls.Add(_start);
        actions.Controls.Add(new Label { Text = "     訂單匯入：", AutoSize = true, Padding = new Padding(0, 10, 0, 0) });
        actions.Controls.Add(ImportButton("蝦皮"));
        actions.Controls.Add(ImportButton("MO店+"));
        actions.Controls.Add(ImportButton("酷澎商城"));

        var fieldsHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, AutoScroll = true, Padding = new Padding(0, 4, 0, 4) };
        for (var i = 0; i < 4; i++) fieldsHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        root.Controls.Add(fieldsHost, 0, 2);
        var groups = new[] { "表頭", "交易資料", "送貨資料", "發票資料(一)" };
        for (var i = 0; i < groups.Length; i++)
        {
            var box = new GroupBox { Text = groups[i].Replace("(一)", "（一）"), Dock = DockStyle.Fill, Padding = new Padding(8) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            box.Controls.Add(flow);
            fieldsHost.Controls.Add(box, i, 0);
            _groupFlows[groups[i]] = flow;
        }
        foreach (var field in FieldCatalog.All) AddField(field);

        var detailBox = new GroupBox { Text = "商品明細（直接輸入表格；有資料的列必須填品號＋數量）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        root.Controls.Add(detailBox, 0, 3);
        ConfigureDetailGrid();
        detailBox.Controls.Add(_details);

        _status.Text = "ERP：尚未偵測";
        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(6, 7, 0, 0);
        root.Controls.Add(_status, 0, 4);
    }

    private void AddField(FieldDefinition field)
    {
        var flow = _groupFlows[field.Group];
        var row = new Panel { Width = 315, Height = 31, Margin = new Padding(2) };
        var label = new Label { Text = field.Label, AutoSize = false, Width = 112, Height = 25, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 2) };
        row.Controls.Add(label);
        Control value;
        if (field.Kind == FieldKind.Boolean)
        {
            value = new CheckBox { Text = "啟用", Width = 176, Height = 25, Location = new Point(116, 3) };
        }
        else
        {
            value = new TextBox { Width = 185, Height = 26, Location = new Point(116, 2), Tag = field.Key };
        }
        row.Controls.Add(value);
        flow.Controls.Add(row);
        _valueControls[field.Key] = value;
        _fieldRows[field.Key] = row;
    }

    private void ConfigureDetailGrid()
    {
        _details.Dock = DockStyle.Fill;
        _details.AllowUserToAddRows = true;
        _details.AllowUserToDeleteRows = true;
        _details.AutoGenerateColumns = false;
        _details.RowHeadersVisible = true;
        _details.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _details.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        _details.Columns.Add(TextColumn("ItemCode", "品號", 220));
        _details.Columns.Add(TextColumn("Unit", "單位", 105));
        _details.Columns.Add(TextColumn("Quantity", "數量", 105));
        _details.Columns.Add(TextColumn("GiftQuantity", "贈/備品量", 125));
        _details.Columns.Add(TextColumn("Batch", "批號", 170));
        _details.Columns.Add(TextColumn("Warehouse", "庫別", 140));
        _details.Columns.Add(TextColumn("UnitPrice", "單價", 130));
        _details.KeyDown += DetailGridKeyDown;
        for (var i = 0; i < 8; i++) _details.Rows.Add();
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string text, int width) => new()
    {
        Name = name,
        HeaderText = text,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private void DetailGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Tab) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        MoveDetailCell(e.Shift ? -1 : 1);
    }

    private void MoveDetailCell(int delta)
    {
        if (_details.CurrentCell is null) return;
        _details.EndEdit();
        var row = _details.CurrentCell.RowIndex;
        var col = _details.CurrentCell.ColumnIndex + delta;
        if (col >= _details.ColumnCount) { col = 0; row++; }
        if (col < 0) { col = _details.ColumnCount - 1; row--; }
        if (row < 0) row = 0;
        if (row >= _details.RowCount - 1 && _details.AllowUserToAddRows) _details.Rows.Add();
        row = Math.Min(row, _details.RowCount - 1);
        _details.CurrentCell = _details[col, row];
        _details.BeginEdit(true);
    }

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
        if (key == Keys.Enter && !_details.ContainsFocus)
        {
            SelectNextControl(ActiveControl, !shift, true, true, true);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ApplyMode(bool advanced)
    {
        foreach (var field in FieldCatalog.All)
            _fieldRows[field.Key].Visible = advanced || field.Standard;
        foreach (var flow in _groupFlows.Values) flow.PerformLayout();
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
            await _automation.RunAsync(snapshot, progress, token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("ERP：自動輸入已停止；未執行 ERP 取消或儲存");
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

    private FormSnapshot? CreateSnapshot(out string validationError)
    {
        _details.EndEdit();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in FieldCatalog.All)
        {
            if (!_advanced.Checked && !field.Standard) continue;
            var control = _valueControls[field.Key];
            string value;
            if (control is CheckBox cb) value = cb.Checked ? "true" : string.Empty;
            else value = control.Text.Trim();
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

    private static string Cell(DataGridViewRow row, string column) => Convert.ToString(row.Cells[column].Value)?.Trim() ?? string.Empty;

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
        var button = MakeButton(text, text == "酷澎商城" ? 128 : 90);
        button.Click += (_, _) => MessageBox.Show(this, $"{text} 匯入解析會在 C# 核心穩定後接續；按鈕固定保留。", "訂單匯入", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return button;
    }

    private static Button MakeButton(string text, int width) => new() { Text = text, Size = new Size(width, 34), Margin = new Padding(4) };

    private void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
        _status.Text = text;
    }
}
