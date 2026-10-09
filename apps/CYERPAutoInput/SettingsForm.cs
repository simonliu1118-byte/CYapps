namespace CYERPAutoInput;

internal sealed class SettingsForm : Form
{
    private readonly UserSettings _settings;
    private readonly Func<Task<string>>? _runProbe;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _advanced = new();
    private readonly CheckBox _diagnostic = new();
    private readonly CheckBox _autoSave = new();
    private readonly CheckBox _firstRowWarehouse = new();
    private readonly TextBox _firstRowWarehouseCode = new();
    private readonly TextBox _shopeeOrderType = new();
    private readonly TextBox _shopeeCustomerCode = new();
    private readonly TextBox _shopeeNotePrefix = new();

    private static readonly string[] ConfigurableKeys =
    [
        "order_type", "dept_code", "currency", "salesperson", "receipt_salesperson",
        "employee_code", "payment_terms", "delivery_slot", "freight_type",
        "inv_copies", "tax_type", "customs", "tax_rate"
    ];

    public SettingsForm(UserSettings settings, Func<Task<string>>? runProbe = null)
    {
        _settings = settings;
        _runProbe = runProbe;
        Text = "CYERPAutoInput 設定";
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(640, 726);
        MinimumSize = new Size(560, 480);
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        BuildUi();
        CyVisualTheme.Apply(this);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        UiSnapshot.AllowOversize(ref m);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "以下為本機預設值，只保存在這台電腦的 Data/settings.json。",
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _advanced.Text = "啟動時使用進階模式";
        _advanced.Checked = _settings.AdvancedMode;
        _advanced.AutoSize = true;
        _advanced.Padding = new Padding(3, 4, 0, 0);
        _advanced.Visible = false; // advanced mode is hidden for now (user, 2026-10-09)
        root.RowStyles[1].Height = 0;
        root.Controls.Add(_advanced, 0, 1);

        _diagnostic.Text = "診斷模式（LOG 記錄實際 ERP 內容；僅本機，除錯時才開）";
        _diagnostic.Checked = _settings.DiagnosticLogging;
        _diagnostic.AutoSize = true;
        _diagnostic.Padding = new Padding(3, 4, 0, 0);
        root.Controls.Add(_diagnostic, 0, 2);

        _autoSave.Text = "輸入完成後自動儲存 ERP 單據（只限 CY 新增的單據；需人工確認項目於完成後列出）";
        _autoSave.Checked = _settings.AutoSave;
        _autoSave.AutoSize = true;
        _autoSave.Padding = new Padding(3, 4, 0, 0);
        root.Controls.Add(_autoSave, 0, 3);

        var warehouseRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _firstRowWarehouse.Text = "商品明細第一列庫別預設填入：";
        _firstRowWarehouse.Checked = _settings.FirstRowWarehouseEnabled;
        _firstRowWarehouse.AutoSize = true;
        _firstRowWarehouse.Padding = new Padding(3, 4, 0, 0);
        _firstRowWarehouseCode.Width = 110;
        _firstRowWarehouseCode.MaxLength = 20;
        _firstRowWarehouseCode.Text = _settings.FirstRowWarehouse;
        _firstRowWarehouseCode.Margin = new Padding(0, 4, 0, 0);
        _firstRowWarehouseCode.Enabled = _firstRowWarehouse.Checked;
        _firstRowWarehouse.CheckedChanged += (_, _) => _firstRowWarehouseCode.Enabled = _firstRowWarehouse.Checked;
        warehouseRow.Controls.Add(_firstRowWarehouse);
        warehouseRow.Controls.Add(_firstRowWarehouseCode);
        root.Controls.Add(warehouseRow, 0, 4);

        var shopeeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        void AddShopeeField(string label, TextBox box, string value, int width)
        {
            shopeeRow.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(3, 7, 0, 0) });
            box.Width = width;
            box.Text = value;
            box.Margin = new Padding(0, 4, 10, 0);
            shopeeRow.Controls.Add(box);
        }
        AddShopeeField("蝦皮匯入　銷貨單別", _shopeeOrderType, _settings.ShopeeOrderType, 60);
        AddShopeeField("客戶代號", _shopeeCustomerCode, _settings.ShopeeCustomerCode, 90);
        AddShopeeField("備註前綴", _shopeeNotePrefix, _settings.ShopeeNotePrefix, 100);
        _shopeeOrderType.MaxLength = 4;
        root.Controls.Add(shopeeRow, 0, 5);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Label",
            HeaderText = "欄位",
            ReadOnly = true,
            Width = 190,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "本機預設值",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        foreach (var key in ConfigurableKeys)
        {
            var field = FieldCatalog.All.First(f => f.Key == key);
            var value = _settings.Defaults.TryGetValue(key, out var saved) ? saved : string.Empty;
            var index = _grid.Rows.Add(field.Label, value);
            _grid.Rows[index].Tag = key;
        }
        root.Controls.Add(_grid, 0, 6);
        Shown += (_, _) => _grid.CurrentCell = _grid.Rows.Count > 0 ? _grid.Rows[0].Cells["Value"] : null;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(4, 6, 4, 0)
        };
        var save = new CyPrimaryButton { Text = "儲存", Width = 92, Height = 34 };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        if (_runProbe is not null)
        {
            var probe = new CyButton { Text = "ERP 結構探測", Width = 120, Height = 34, Margin = new Padding(3, 3, 48, 3) };
            probe.Click += async (_, _) => await RunProbeAsync(probe);
            buttons.Controls.Add(probe);
        }
        root.Controls.Add(buttons, 0, 7);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private async Task RunProbeAsync(Button probe)
    {
        if (MessageBox.Show(this,
                "唯讀讀取 COPI08 與目前開著的 ERP 視窗結構（不會送出按鍵或點擊），結果存到本機 LOG 資料夾。\n" +
                "開啟診斷模式時會包含畫面上的實際內容；建議在 ERP 測試公司別執行。\n\n要開始嗎？",
                "ERP 結構探測", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

        probe.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var path = await _runProbe!();
            MessageBox.Show(this, $"探測完成，結果已存到：\n{path}", "ERP 結構探測", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ERP 結構探測", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            probe.Enabled = true;
        }
    }

    private void SaveAndClose()
    {
        var warehouse = _firstRowWarehouseCode.Text.Trim();
        if (_firstRowWarehouse.Checked && warehouse.Length == 0)
        {
            MessageBox.Show(this, "已勾選自動帶入第一列庫別，請填入庫別代號。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _firstRowWarehouseCode.Focus();
            return;
        }
        _settings.ShopeeOrderType = _shopeeOrderType.Text.Trim();
        _settings.ShopeeCustomerCode = _shopeeCustomerCode.Text.Trim();
        _settings.ShopeeNotePrefix = _shopeeNotePrefix.Text.Trim();
        _settings.FirstRowWarehouseEnabled = _firstRowWarehouse.Checked;
        _settings.FirstRowWarehouse = warehouse;
        _settings.AdvancedMode = _advanced.Checked;
        _settings.DiagnosticLogging = _diagnostic.Checked;
        _settings.AutoSave = _autoSave.Checked;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not string key) continue;
            var value = Convert.ToString(row.Cells["Value"].Value)?.Trim() ?? string.Empty;
            if (value.Length == 0) _settings.Defaults.Remove(key);
            else _settings.Defaults[key] = value;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
