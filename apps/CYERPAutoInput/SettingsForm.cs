namespace CYERPAutoInput;

internal sealed class SettingsForm : Form
{
    private readonly UserSettings _settings;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _advanced = new();
    private readonly CheckBox _autoSave = new();
    private readonly CheckBox _autoPrint = new();
    private readonly CheckBox _firstRowWarehouse = new();
    private readonly TextBox _firstRowWarehouseCode = new();
    private readonly TextBox _shopeeOrderType = new();
    private readonly TextBox _shopeeCustomerCode = new();
    private readonly TextBox _shopeeNotePrefix = new();
    private readonly TextBox _moOrderType = new();
    private readonly TextBox _moCustomerCode = new();
    private readonly TextBox _moNotePrefix = new();
    private readonly TextBox _moPassword = new();
    private readonly TextBox _coupangOrderType = new();
    private readonly TextBox _coupangCustomerCode = new();
    private readonly TextBox _coupangNotePrefix = new();
    private readonly TabControl _tabs = new();
    private readonly TextBox _discountItem = new();
    private readonly TextBox _shippingItem = new();
    private readonly TextBox _promotionItem = new();
    private readonly Dictionary<string, TextBox> _freightCodes = [];

    private static readonly string[] ConfigurableKeys =
    [
        "order_type", "dept_code", "currency", "salesperson", "receipt_salesperson",
        "employee_code", "payment_terms", "delivery_slot", "freight_type",
        "inv_copies", "tax_type", "customs", "tax_rate"
    ];

    public SettingsForm(UserSettings settings)
    {
        _settings = settings;
        Text = "CYERPAutoInput 設定";
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        Size = new Size(640, 712);
        MinimumSize = new Size(600, 520);
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        BuildUi();
        CyVisualTheme.Apply(this);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        UiSnapshot.AllowOversize(ref m);
    }

    /// <summary>
    /// Four pages (user, 2026-10-11: one long page had become too busy): 一般, 平台匯入,
    /// 貨運別 (shared by every platform) and 本機預設值.
    /// </summary>
    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "以下設定只保存在這台電腦的 Data/settings.json。",
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(14, 5);
        _tabs.TabPages.Add(GeneralPage());
        _tabs.TabPages.Add(ImportPage());
        _tabs.TabPages.Add(FreightPage());
        _tabs.TabPages.Add(DefaultsPage());
        root.Controls.Add(_tabs, 0, 1);

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
        root.Controls.Add(buttons, 0, 2);
        // No AcceptButton: Enter moves to the next field and saves only from a page's last
        // field (user, 2026-10-11); see ProcessCmdKey.
        CancelButton = cancel;
    }

    private static TabPage Page(string title) => new(title) { Padding = new Padding(12), UseVisualStyleBackColor = true };

    private static CheckBox Option(CheckBox box, string text, bool value)
    {
        box.Text = text;
        box.Checked = value;
        box.AutoSize = true;
        box.Margin = new Padding(0, 4, 0, 8);
        return box;
    }

    private TabPage GeneralPage()
    {
        var page = Page("一般");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(Option(_autoSave, "輸入完成後自動儲存 ERP 單據（只限 CY 新增的單據；需人工確認項目於完成後列出）", _settings.AutoSave));
        flow.Controls.Add(Option(_autoPrint, "自動儲存後按 ERP「列印 → 直接列印」", _settings.AutoPrint));
        _autoPrint.Margin = new Padding(20, 0, 0, 8);
        _autoPrint.Enabled = _autoSave.Checked;
        _autoSave.CheckedChanged += (_, _) => _autoPrint.Enabled = _autoSave.Checked;

        var warehouseRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        Option(_firstRowWarehouse, "商品明細第一列庫別預設填入：", _settings.FirstRowWarehouseEnabled);
        _firstRowWarehouse.Margin = new Padding(0, 6, 0, 0);
        _firstRowWarehouseCode.Width = 110;
        _firstRowWarehouseCode.MaxLength = 20;
        _firstRowWarehouseCode.Text = _settings.FirstRowWarehouse;
        _firstRowWarehouseCode.Margin = new Padding(0, 3, 0, 0);
        _firstRowWarehouseCode.Enabled = _firstRowWarehouse.Checked;
        _firstRowWarehouse.CheckedChanged += (_, _) => _firstRowWarehouseCode.Enabled = _firstRowWarehouse.Checked;
        warehouseRow.Controls.Add(_firstRowWarehouse);
        warehouseRow.Controls.Add(_firstRowWarehouseCode);
        flow.Controls.Add(warehouseRow);

        // Advanced mode is hidden for now (user, 2026-10-09); the setting is kept.
        _advanced.Checked = _settings.AdvancedMode;
        _advanced.Visible = false;
        flow.Controls.Add(_advanced);
        page.Controls.Add(flow);
        return page;
    }

    /// <summary>A section of label + input pairs, two pairs per line, aligned in columns.</summary>
    private static GroupBox Section(string title, params (string Label, TextBox Box, string Value, int Width)[] fields)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        for (var i = 0; i < fields.Length; i++)
        {
            var (label, input, value, width) = fields[i];
            input.Text = value;
            input.Width = width;
            input.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, (i % 2) * 2, i / 2);
            grid.Controls.Add(input, (i % 2) * 2 + 1, i / 2);
        }
        var rows = (fields.Length + 1) / 2;
        box.Height = rows * 32 + 34;
        box.Controls.Add(grid);
        return box;
    }

    private TabPage ImportPage()
    {
        var page = Page("平台匯入");
        _shopeeOrderType.MaxLength = 4;
        _moOrderType.MaxLength = 4;
        _coupangOrderType.MaxLength = 4;
        _moPassword.UseSystemPasswordChar = true;
        _moPassword.PlaceholderText = _settings.MoPasswordProtected.Length > 0 ? "已設定，空白不變更" : string.Empty;
        // Docked to the top in reverse order: the last added sits highest; TabIndex follows
        // the screen order so Enter/Tab go 蝦皮 → MO店+ → 酷澎 → 共用.
        page.Controls.Add(Section("共用（所有平台）",
            ("折價券品號", _discountItem, _settings.DiscountItemCode, 120),
            ("運費品號", _shippingItem, _settings.ShippingItemCode, 120),
            ("促銷組合品號", _promotionItem, _settings.PromotionItemCode, 120)));
        page.Controls.Add(Section("酷澎",
            ("銷貨單別", _coupangOrderType, _settings.CoupangOrderType, 80),
            ("客戶代號", _coupangCustomerCode, _settings.CoupangCustomerCode, 120),
            ("備註前綴", _coupangNotePrefix, _settings.CoupangNotePrefix, 150)));
        page.Controls.Add(Section("MO店+",
            ("銷貨單別", _moOrderType, _settings.MoOrderType, 80),
            ("客戶代號", _moCustomerCode, _settings.MoCustomerCode, 120),
            ("備註前綴", _moNotePrefix, _settings.MoNotePrefix, 150),
            ("匯出檔密碼", _moPassword, string.Empty, 150)));
        page.Controls.Add(Section("蝦皮",
            ("銷貨單別", _shopeeOrderType, _settings.ShopeeOrderType, 80),
            ("客戶代號", _shopeeCustomerCode, _settings.ShopeeCustomerCode, 120),
            ("備註前綴", _shopeeNotePrefix, _settings.ShopeeNotePrefix, 150)));
        // Added in reverse (last added sits highest), so count TabIndex from the end.
        var sections = page.Controls.OfType<GroupBox>().ToList();
        for (var i = 0; i < sections.Count; i++) sections[i].TabIndex = sections.Count - 1 - i;
        return page;
    }

    /// <summary>
    /// Enter moves to the next field of the current page; on the page's last field (or the
    /// last row of 本機預設值) it saves. Buttons keep their own Enter.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Enter || ActiveControl is Button) return base.ProcessCmdKey(ref msg, keyData);
        var page = _tabs.SelectedTab;
        if (page is null) return base.ProcessCmdKey(ref msg, keyData);

        if (_grid.ContainsFocus)
        {
            _grid.EndEdit();
            var row = _grid.CurrentCell?.RowIndex ?? -1;
            if (row < 0 || row >= _grid.Rows.Count - 1) SaveAndClose();
            else
            {
                _grid.CurrentCell = _grid.Rows[row + 1].Cells["Value"];
                _grid.BeginEdit(true);
            }
            return true;
        }

        var fields = new List<Control>();
        for (Control? c = page; (c = page.GetNextControl(c, true)) is not null;)
            if (c is TextBox or CheckBox && c.Visible && c.Enabled && c.TabStop) fields.Add(c);
        var focused = fields.FindIndex(f => f.ContainsFocus);
        if (focused < 0 || focused == fields.Count - 1) SaveAndClose();
        else fields[focused + 1].Focus();
        return true;
    }

    private TabPage FreightPage()
    {
        var page = Page("貨運別");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "填入各物流商在 ERP 的貨運別代號（所有平台共用）。匯出檔的物流商文字包含左側名稱即套用；空白表示未設定，該訂單轉人工。",
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        // 物流 and 超商 side by side (user, 2026-10-11), each a column of name + code.
        var groups = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        GroupBox CarrierGroup(string title, string[] carriers, Padding margin)
        {
            var box = new GroupBox { Text = title, Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Margin = margin, Height = carriers.Length * 32 + 34 };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            for (var i = 0; i < carriers.Length; i++)
            {
                var carrier = carriers[i];
                var code = new TextBox
                {
                    Width = 70,
                    MaxLength = 10,
                    Margin = new Padding(0, 4, 0, 4),
                    Text = _settings.FreightTypes.TryGetValue(carrier, out var saved) ? saved : string.Empty,
                    AccessibleName = carrier + " 貨運別"
                };
                _freightCodes[carrier] = code;
                grid.Controls.Add(new Label { Text = carrier, AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, 0, i);
                grid.Controls.Add(code, 1, i);
            }
            box.Controls.Add(grid);
            return box;
        }
        // 本公司 has its own box under 物流, not inside it (user, 2026-10-11).
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var logistics = CarrierGroup("物流", FreightCarriers.Logistics, new Padding(0, 0, 6, 8));
        var own = CarrierGroup("本公司", FreightCarriers.Own, new Padding(0, 0, 6, 0));
        logistics.Dock = own.Dock = DockStyle.None;
        logistics.Width = own.Width = 290;
        left.Controls.Add(logistics);
        left.Controls.Add(own);
        groups.Controls.Add(left, 0, 0);
        groups.Controls.Add(CarrierGroup("超商", FreightCarriers.Stores, new Padding(6, 0, 0, 0)), 1, 0);
        layout.Controls.Add(groups, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage DefaultsPage()
    {
        var page = Page("本機預設值");
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeColumns = false;
        _grid.AllowUserToResizeRows = false;
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
        page.Controls.Add(_grid);
        return page;
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
        _settings.MoOrderType = _moOrderType.Text.Trim();
        _settings.MoCustomerCode = _moCustomerCode.Text.Trim();
        _settings.MoNotePrefix = _moNotePrefix.Text.Trim();
        _settings.CoupangOrderType = _coupangOrderType.Text.Trim();
        _settings.CoupangCustomerCode = _coupangCustomerCode.Text.Trim();
        _settings.CoupangNotePrefix = _coupangNotePrefix.Text.Trim();
        _settings.DiscountItemCode = _discountItem.Text.Trim();
        _settings.ShippingItemCode = _shippingItem.Text.Trim();
        _settings.PromotionItemCode = _promotionItem.Text.Trim();
        _settings.FreightTypes = _freightCodes
            .Where(p => p.Value.Text.Trim().Length > 0)
            .ToDictionary(p => p.Key, p => p.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase);
        if (_moPassword.Text.Length > 0) _settings.MoPasswordProtected = LocalSecret.Protect(_moPassword.Text);
        _settings.FirstRowWarehouseEnabled = _firstRowWarehouse.Checked;
        _settings.FirstRowWarehouse = warehouse;
        _settings.AdvancedMode = _advanced.Checked;
        _settings.AutoSave = _autoSave.Checked;
        _settings.AutoPrint = _autoPrint.Checked;
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
