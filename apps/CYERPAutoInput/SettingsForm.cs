namespace CYERPAutoInput;

internal sealed class SettingsForm : Form
{
    private readonly UserSettings _settings;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _advanced = new();
    private readonly CheckBox _autoSave = new();
    private readonly CheckBox _autoPrint = new();
    private readonly CheckBox _clearAfterSave = new();
    private readonly CheckBox _browserHotkey = new();
    private readonly CheckBox _shopeeFullAuto = new();
    private readonly CheckBox _firstRowWarehouse = new();
    private readonly TextBox _firstRowWarehouseCode = new();
    private readonly TextBox _shopeeOrderType = new();
    private readonly TextBox _shopeeCustomerCode = new();
    private readonly TextBox _shopeeNotePrefix = new();
    private readonly TextBox _shopeeLabelPrinter = new();
    private readonly TextBox _shopeeLabelScale = new();
    private readonly TextBox _sevenLabelScale = new();
    private readonly TextBox _familyLabelScale = new();
    private readonly TextBox _familyLabelPages = new();
    private readonly TextBox _hctLabelScale = new();
    private readonly TextBox _hctPickupAddress = new();
    private ChatRuleEditor? _shopeeChat;
    private ChatRuleEditor? _moChat;
    private ChatRuleEditor? _coupangChat;
    private GiftRuleEditor? _gifts;
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

    /// <summary>「讀取 Chrome 網頁文字（診斷）」 was pressed: the window closed for it to run.</summary>
    public bool DiagnoseChromeText { get; private set; }

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
        _tabs.TabPages.Add(MessagesPage());
        _tabs.TabPages.Add(GiftPage());
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
        flow.Controls.Add(Option(_autoSave, "輸入完成後自動儲存 ERP 單據（需人工確認項目於完成後列出）", _settings.AutoSave));
        flow.Controls.Add(Option(_autoPrint, "自動儲存後按 ERP「列印 → 直接列印」", _settings.AutoPrint));
        _autoPrint.Margin = new Padding(20, 0, 0, 8);
        _autoPrint.Enabled = _autoSave.Checked;
        _autoSave.CheckedChanged += (_, _) => _autoPrint.Enabled = _autoSave.Checked;
        flow.Controls.Add(Option(_clearAfterSave, "儲存單據後自動清除表單", _settings.ClearAfterSave));
        flow.Controls.Add(Option(_browserHotkey, $"在瀏覽器按 {CYERPAutoInput.BrowserHotkey.KeyName} 開啟蝦皮自動擷取輸入並開始擷取（程式開著即可）", _settings.BrowserHotkey));
        flow.Controls.Add(Option(_shopeeFullAuto, "蝦皮自動擷取：按「開始」後擷取、判讀、帶回、輸入 ERP、儲存並列印（只限蝦皮）", _settings.ShopeeFullAuto));

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

        // Diagnostic (user, 2026-10-13: kept out of the 蝦皮 menu, here in 設定).
        var diagnose = new CyButton { Text = "讀取 Chrome 網頁文字（診斷）", AutoSize = true, Height = 34, Margin = new Padding(0, 14, 0, 0) };
        diagnose.Click += (_, _) => { DiagnoseChromeText = true; DialogResult = DialogResult.Cancel; Close(); };
        flow.Controls.Add(diagnose);

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
            ("備註前綴", _shopeeNotePrefix, _settings.ShopeeNotePrefix, 150),
            ("寄件單印表機", _shopeeLabelPrinter, _settings.ShopeeLabelPrinter, 150),
            ("寄件單縮放 %", _shopeeLabelScale, _settings.ShopeeLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("7-11 縮放 %", _sevenLabelScale, _settings.SevenLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("全家縮放 %", _familyLabelScale, _settings.FamilyLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("全家列印頁", _familyLabelPages, _settings.FamilyLabelPages, 60),
            ("新竹縮放 %", _hctLabelScale, _settings.HctLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("新竹取件地址", _hctPickupAddress, _settings.HctPickupAddress, 300)));
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

    /// <summary>
    /// 發送訊息 (user, 2026-10-14): per platform, rules of words looked for in the buyer's note and
    /// the message sent for them; a word may sit in one rule only. Only 蝦皮 is used for now
    /// (全自動 sends through 聊聊); MO店+ and 酷澎 are shown locked.
    /// </summary>
    private TabPage MessagesPage()
    {
        var page = Page("發送訊息");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 8),
            Text = "買家備註出現關鍵字時，自動送出對應的訊息（訂單照常處理）。關鍵字用逗號分隔，同一個平台內一個關鍵字只能屬於一則訊息。"
        });
        _shopeeChat = new ChatRuleEditor("蝦皮（全自動：安排出貨前以聊聊送出）", _settings.ShopeeChatRules, locked: false);
        _moChat = new ChatRuleEditor("MO店+（尚未開放）", _settings.MoChatRules, locked: true);
        _coupangChat = new ChatRuleEditor("酷澎（尚未開放）", _settings.CoupangChatRules, locked: true);
        flow.Controls.Add(_shopeeChat);
        flow.Controls.Add(_moChat);
        flow.Controls.Add(_coupangChat);
        scroll.Controls.Add(flow);
        page.Controls.Add(scroll);
        return page;
    }

    /// <summary>
    /// 滿件贈送 (user, 2026-10-14): cards of 品號, 滿幾件, 送幾件 and the platforms; an imported
    /// order gets floor(件數 ÷ 滿幾件) × 送幾件 in that item's 贈/備品量.
    /// </summary>
    private TabPage GiftPage()
    {
        var page = Page("滿件贈送");
        _gifts = new GiftRuleEditor(_settings.GiftRules);
        page.Controls.Add(_gifts);
        return page;
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
        foreach (var editor in new[] { _shopeeChat!, _moChat!, _coupangChat! })
        {
            var problems = ChatRules.Problems(editor.Rules());
            if (problems.Count == 0) continue;
            MessageBox.Show(this, $"發送訊息 › {editor.Text}：\n\n" + string.Join("\n", problems), "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            return;
        }
        var giftProblems = GiftRules.Problems(_gifts!.Rules());
        if (giftProblems.Count > 0)
        {
            MessageBox.Show(this, "滿件贈送：\n\n" + string.Join("\n", giftProblems), "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 3;
            return;
        }
        var warehouse = _firstRowWarehouseCode.Text.Trim();
        if (_firstRowWarehouse.Checked && warehouse.Length == 0)
        {
            MessageBox.Show(this, "已勾選自動帶入第一列庫別，請填入庫別代號。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _firstRowWarehouseCode.Focus();
            return;
        }
        if (!int.TryParse(_shopeeLabelScale.Text.Trim(), out var labelScale) || labelScale is < 10 or > 200)
        {
            MessageBox.Show(this, "蝦皮寄件單縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            _shopeeLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_sevenLabelScale.Text.Trim(), out var sevenScale) || sevenScale is < 10 or > 200)
        {
            MessageBox.Show(this, "7-11 交貨便縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            _sevenLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_familyLabelScale.Text.Trim(), out var familyScale) || familyScale is < 10 or > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(_familyLabelPages.Text.Trim(), @"^\d+$"))
        {
            MessageBox.Show(this, "全家縮放請填 10–200 的數字，全家列印頁請填頁碼（例如 2）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            _familyLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_hctLabelScale.Text.Trim(), out var hctScale) || hctScale is < 10 or > 200)
        {
            MessageBox.Show(this, "新竹縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 1;
            _hctLabelScale.Focus();
            return;
        }
        _settings.HctLabelScale = hctScale;
        _settings.HctPickupAddress = _hctPickupAddress.Text.Trim();
        _settings.GiftRules = GiftRules.Clean(_gifts!.Rules());
        _settings.ShopeeChatRules = ChatRules.Clean(_shopeeChat!.Rules());
        _settings.MoChatRules = ChatRules.Clean(_moChat!.Rules());
        _settings.CoupangChatRules = ChatRules.Clean(_coupangChat!.Rules());
        _settings.SevenLabelScale = sevenScale;
        _settings.FamilyLabelScale = familyScale;
        _settings.FamilyLabelPages = _familyLabelPages.Text.Trim();
        _settings.ShopeeLabelPrinter = _shopeeLabelPrinter.Text.Trim();
        _settings.ShopeeLabelScale = labelScale;
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
        _settings.ClearAfterSave = _clearAfterSave.Checked;
        _settings.BrowserHotkey = _browserHotkey.Checked;
        _settings.ShopeeFullAuto = _shopeeFullAuto.Checked;
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

/// <summary>A platform's 發送訊息 rules: one row per rule (關鍵字, 訊息, 刪除) and 新增 below.</summary>
internal sealed class ChatRuleEditor : GroupBox
{
    private readonly FlowLayoutPanel _rows = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
    private readonly bool _locked;

    public ChatRuleEditor(string title, IEnumerable<ChatRule> rules, bool locked)
    {
        _locked = locked;
        Text = title;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10, 6, 10, 8);
        Margin = new Padding(0, 0, 0, 10);
        MinimumSize = new Size(570, 0);
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        body.Controls.Add(_rows);
        var add = new CyButton { Text = "新增", Width = 80, Height = 30, Enabled = !locked, Margin = new Padding(0, 4, 0, 0) };
        add.Click += (_, _) => AddRow(new ChatRule());
        body.Controls.Add(add);
        Controls.Add(body);
        foreach (var rule in rules) AddRow(rule);
        if (_rows.Controls.Count == 0 && !locked) AddRow(new ChatRule { Keywords = "發票,收據,統編,統一編號,抬頭" });
        Enabled = !locked;
    }

    public List<ChatRule> Rules() =>
        _rows.Controls.OfType<TableLayoutPanel>().Select(r => new ChatRule
        {
            Keywords = ((TextBox)r.GetControlFromPosition(1, 0)!).Text,
            Message = ((TextBox)r.GetControlFromPosition(1, 1)!).Text
        }).ToList();

    private void AddRow(ChatRule rule)
    {
        var row = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        var keywords = new TextBox { Width = 400, Text = rule.Keywords, PlaceholderText = "關鍵字，用逗號分隔，例如：發票,統編", Margin = new Padding(0, 4, 0, 4) };
        var message = new TextBox { Width = 400, Height = 52, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = rule.Message, Margin = new Padding(0, 2, 0, 2) };
        var remove = new CyButton { Text = "刪除", Width = 64, Height = 30, Enabled = !_locked };
        remove.Click += (_, _) => { _rows.Controls.Remove(row); row.Dispose(); Renumber(); };
        row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, 0, 0);
        row.Controls.Add(keywords, 1, 0);
        row.Controls.Add(remove, 2, 0);
        row.Controls.Add(new Label { Text = "訊息", AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, 0, 1);
        new ToolTip().SetToolTip(keywords, "關鍵字，用逗號分隔");
        row.Controls.Add(message, 1, 1);
        _rows.Controls.Add(row);
        Renumber();
    }

    /// <summary>Rows are numbered (第 1 則, 第 2 則…) as the save check names them; the first row's label says it holds the words.</summary>
    private void Renumber()
    {
        var i = 1;
        foreach (var row in _rows.Controls.OfType<TableLayoutPanel>())
            ((Label)row.GetControlFromPosition(0, 0)!).Text = $"第 {i++} 則";
    }
}

/// <summary>滿件贈送 cards (品號, 滿幾件, 送幾件, 適用平台, 刪除) with 新增 at the top.</summary>
internal sealed class GiftRuleEditor : Panel
{
    private readonly FlowLayoutPanel _cards = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

    public GiftRuleEditor(IEnumerable<GiftRule> rules)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        head.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 6, 12, 0),
            Text = "匯入的訂單中，同一品號合計每滿幾件，就在該品號第一列的「贈/備品量」填入送幾件（例如買 10 送 1：10 件填 1、20 件填 2）。"
        });
        var add = new CyButton { Text = "新增", Width = 80, Height = 30 };
        add.Click += (_, _) => AddCard(new GiftRule { Every = 10, Gift = 1, Platforms = [ShopeeOrderImport.Source] });
        head.Controls.Add(add);
        Controls.Add(_cards);
        Controls.Add(head);
        foreach (var rule in rules) AddCard(rule);
    }

    public List<GiftRule> Rules() =>
        _cards.Controls.OfType<GroupBox>().Select(card =>
        {
            var c = card.Tag as (TextBox Code, NumericUpDown Every, NumericUpDown Gift, CheckBox[] Platforms)?;
            var (code, every, gift, platforms) = c!.Value;
            return new GiftRule
            {
                ItemCode = code.Text,
                Every = (int)every.Value,
                Gift = (int)gift.Value,
                Platforms = platforms.Where(p => p.Checked).Select(p => p.Text).ToList()
            };
        }).ToList();

    private void AddCard(GiftRule rule)
    {
        var card = new GroupBox { Width = 560, Height = 98, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 8) };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        Label L(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 4, 0) };
        var code = new TextBox { Width = 120, Text = rule.ItemCode, Margin = new Padding(0, 4, 12, 4) };
        NumericUpDown N(int value) => new() { Minimum = 0, Maximum = 9999, Width = 64, Value = Math.Clamp(value, 0, 9999), Margin = new Padding(0, 4, 4, 4) };
        var every = N(rule.Every);
        var gift = N(rule.Gift);
        var remove = new CyButton { Text = "刪除", Width = 64, Height = 30, Margin = new Padding(12, 2, 0, 0) };
        remove.Click += (_, _) => { _cards.Controls.Remove(card); card.Dispose(); Renumber(); };
        grid.Controls.Add(L("品號"));
        grid.Controls.Add(code);
        grid.Controls.Add(L("滿"));
        grid.Controls.Add(every);
        grid.Controls.Add(L("件 送"));
        grid.Controls.Add(gift);
        grid.Controls.Add(L("件"));
        grid.Controls.Add(remove);
        grid.SetFlowBreak(remove, true);
        grid.Controls.Add(L("適用平台"));
        var platforms = GiftRules.Platforms.Select(p => new CheckBox { Text = p, AutoSize = true, Checked = rule.Platforms.Contains(p), Margin = new Padding(0, 6, 12, 0) }).ToArray();
        foreach (var p in platforms) grid.Controls.Add(p);
        card.Controls.Add(grid);
        card.Tag = ((TextBox, NumericUpDown, NumericUpDown, CheckBox[])?)(code, every, gift, platforms);
        _cards.Controls.Add(card);
        Renumber();
    }

    private void Renumber()
    {
        var i = 1;
        foreach (var card in _cards.Controls.OfType<GroupBox>()) card.Text = $"第 {i++} 張";
    }
}
