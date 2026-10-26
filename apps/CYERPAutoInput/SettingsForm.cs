namespace CYERPAutoInput;

internal sealed class SettingsForm : Form
{
    private readonly UserSettings _settings;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _advanced = new();
    private readonly CheckBox _autoSave = new();
    private readonly CheckBox _autoPrint = new();
    private readonly CheckBox _clearAfterSave = new();
    private readonly CheckBox _firstRowWarehouse = new();
    private readonly TextBox _firstRowWarehouseCode = new();
    private readonly TextBox _shopeeOrderType = new();
    private readonly TextBox _shopeeCustomerCode = new();
    private readonly TextBox _shopeeNotePrefix = new();
    private PrinterPicker? _printers;
    private readonly TextBox _shopeeLabelScale = new();
    private readonly TextBox _sevenLabelScale = new();
    private readonly TextBox _familyLabelScale = new();
    private readonly TextBox _familyLabelPages = new();
    private readonly TextBox _hctLabelScale = new();
    private readonly TextBox _coupangHctLabelScale = new();
    private readonly TextBox _slipNote = new();
    private readonly Label _slipNoteStatus = new();
    private readonly TextBox _hctPickupAddress = new();
    private ChatRuleEditor? _shopeeChat;
    private ChatRuleEditor? _moChat;
    private ChatRuleEditor? _coupangChat;
    private GiftRuleEditor? _gifts;
    private readonly TextBox _moOrderType = new();
    private readonly TextBox _moCustomerCode = new();
    private readonly TextBox _moNotePrefix = new();
    private readonly TextBox _moPassword = new();
    private readonly TextBox _cyInvoicePath = new() { ReadOnly = true, TabStop = false };
    private readonly ComboBox _paperInvoiceStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };
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
        _tabs.TabPages.Add(PrinterPage());
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
        // F8 and 蝦皮自動擷取 end to end are stable and always on (user, 2026-10-15: no switches).

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
        // CYInvoice (user, 2026-10-15: under 一般).
        var invoice = CyInvoiceSection();
        invoice.Dock = DockStyle.None;
        invoice.Width = 560;
        invoice.Margin = new Padding(0, 14, 0, 0);
        flow.Controls.Add(invoice);


        // Advanced mode is hidden for now (user, 2026-10-09); the setting is kept.
        _advanced.Checked = _settings.AdvancedMode;
        _advanced.Visible = false;
        flow.Controls.Add(_advanced);
        page.Controls.Add(flow);
        return page;
    }

    /// <summary>A section of label + input pairs, two pairs per line, aligned in columns.</summary>
    private static GroupBox Section(string title, params (string Label, Control Box, string Value, int Width)[] fields)
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

    /// <summary>
    /// 印表機 (user, 2026-10-14: a tab of its own): the printers of this PC ticked for 熱感紙 and
    /// A4, and the 蝦皮 Chrome print scales.
    /// </summary>
    private TabPage PrinterPage()
    {
        var page = Page("印表機");
        page.AutoScroll = true;
        page.Controls.Add(Section("蝦皮寄件單（Chrome 列印視窗的縮放）",
            ("寄件單縮放 %", _shopeeLabelScale, _settings.ShopeeLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("7-11 縮放 %", _sevenLabelScale, _settings.SevenLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("全家縮放 %", _familyLabelScale, _settings.FamilyLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60),
            ("全家列印頁", _familyLabelPages, _settings.FamilyLabelPages, 60),
            ("新竹縮放 %", _hctLabelScale, _settings.HctLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60)));
        // 酷澎 宅配 寄件單 on the thermal printer (user, 2026-10-19: 22 %; 超商 print at 100 %).
        page.Controls.Add(Section("酷澎寄件單（熱感印表機，Chrome 列印視窗的縮放；超商固定 100%）",
            ("宅配縮放 %", _coupangHctLabelScale, _settings.CoupangHctLabelScale.ToString(System.Globalization.CultureInfo.InvariantCulture), 60)));
        page.Controls.Add(SlipNoteSection());
        // 紙本發票 (user, 2026-10-15): CYInvoice's PDF style, chosen beforehand; printed on the A4 printer.
        var invoiceBox = new GroupBox { Text = "紙本發票樣式（有統編的發票；酷澎無統編一律 A4 雙面；印在 A4 印表機）", Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Height = 64 };
        var invoiceRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        foreach (var (code, name) in CyInvoiceLink.PaperStyles) _paperInvoiceStyle.Items.Add(new PaperStyleItem(code, name));
        _paperInvoiceStyle.SelectedItem = _paperInvoiceStyle.Items.Cast<PaperStyleItem>().FirstOrDefault(i => i.Code == _settings.PaperInvoiceStyle) ?? _paperInvoiceStyle.Items[0];
        _paperInvoiceStyle.Width = 180;
        invoiceRow.Controls.Add(new Label { Text = "發票 PDF 格式", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        invoiceRow.Controls.Add(_paperInvoiceStyle);
        invoiceBox.Controls.Add(invoiceRow);
        page.Controls.Add(invoiceBox);
        _printers = new PrinterPicker(_settings.ShopeeLabelPrinter, _settings.A4Printer);
        page.Controls.Add(_printers);
        var sections = page.Controls.OfType<GroupBox>().ToList();
        for (var i = 0; i < sections.Count; i++) sections[i].TabIndex = sections.Count - 1 - i;
        return page;
    }

    /// <summary>
    /// 寄件單下方文字 (user, 2026-10-20): a box of the block's lines, checked as typed against
    /// the width it is printed in (<see cref="SlipNote"/>).
    /// </summary>
    private GroupBox SlipNoteSection()
    {
        var box = new GroupBox
        {
            Text = "寄件單下方文字（只印在程式自己切的寄件單：蝦皮／酷澎全家、MO店+ 全家／7-11）",
            Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Height = 150
        };
        var column = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(new Label
        {
            AutoSize = true, Margin = new Padding(0, 2, 0, 4),
            Text = $"字少就大、字多自動折行變小（{SlipNote.MinPoints:F0}–{SlipNote.MaxPoints:F0} pt 粗體）；最小時約 3 行 × 27 個中文字。空白就不印。"
        });
        _slipNote.Multiline = true;
        _slipNote.AcceptsReturn = true;
        _slipNote.ScrollBars = ScrollBars.None;
        _slipNote.Size = new Size(560, 66);
        _slipNote.Text = _settings.SlipNote.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _slipNote.TextChanged += (_, _) => ShowSlipNoteStatus();
        column.Controls.Add(_slipNote);
        _slipNoteStatus.AutoSize = true;
        _slipNoteStatus.Margin = new Padding(0, 4, 0, 0);
        column.Controls.Add(_slipNoteStatus);
        box.Controls.Add(column);
        ShowSlipNoteStatus();
        return box;
    }

    private void ShowSlipNoteStatus()
    {
        var error = SlipNote.Check(_slipNote.Text, SlipPrinter.NoteWidthMm);
        var fit = SlipNote.Fit(_slipNote.Text, SlipPrinter.NoteWidthMm);
        _slipNoteStatus.ForeColor = error is null ? SystemColors.GrayText : Color.Firebrick;
        _slipNoteStatus.Text = error ?? (fit is { } f ? $"印出：{f.Points:0.#} pt，{f.Lines.Count} 行" : "不印文字。");
    }

    private TabPage ImportPage()
    {
        var page = Page("平台匯入");
        page.AutoScroll = true;
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
            ("取件地址", _hctPickupAddress, _settings.HctPickupAddress, 300)));
        // Added in reverse (last added sits highest), so count TabIndex from the end.
        var sections = page.Controls.OfType<GroupBox>().ToList();
        for (var i = 0; i < sections.Count; i++) sections[i].TabIndex = sections.Count - 1 - i;
        return page;
    }

    /// <summary>
    /// CYInvoice (MO店+ 發票): the exe picked with a file dialog only (user, 2026-10-14), and
    /// whether 全自動 issues through it.
    /// </summary>
    private GroupBox CyInvoiceSection()
    {
        // Shared by MO店+ and 酷澎 (user, 2026-10-15): the exe only; 全自動 always invoices.
        var box = new GroupBox { Text = "CYInvoice", Dock = DockStyle.Top, Padding = new Padding(10, 6, 10, 8), Height = 66 };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _cyInvoicePath.Text = _settings.CyInvoicePath;
        _cyInvoicePath.Dock = DockStyle.Fill;
        _cyInvoicePath.Margin = new Padding(0, 4, 6, 4);
        var pick = new Button { Text = "選擇…", AutoSize = true, Margin = new Padding(0, 2, 6, 2) };
        pick.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "選擇 CYInvoice.exe", Filter = "CYInvoice 程式 (CYInvoice.exe)|CYInvoice.exe|所有程式 (*.exe)|*.exe", CheckFileExists = true };
            if (_cyInvoicePath.Text.Length > 0 && Directory.Exists(Path.GetDirectoryName(_cyInvoicePath.Text))) dialog.InitialDirectory = Path.GetDirectoryName(_cyInvoicePath.Text);
            if (dialog.ShowDialog(this) == DialogResult.OK) _cyInvoicePath.Text = dialog.FileName;
        };
        var clear = new Button { Text = "清除", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        clear.Click += (_, _) => _cyInvoicePath.Text = string.Empty;
        grid.Controls.Add(_cyInvoicePath, 0, 0);
        grid.Controls.Add(pick, 1, 0);
        grid.Controls.Add(clear, 2, 0);
        box.Controls.Add(grid);
        return box;
    }

    /// <summary>
    /// Enter moves to the next field of the current page; on the page's last field (or the
    /// last row of 本機預設值) it saves. Buttons keep their own Enter.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Enter || ActiveControl is Button) return base.ProcessCmdKey(ref msg, keyData);
        // Enter in a pill box makes the typed text a pill (user, 2026-10-14); empty, it moves on.
        for (Control? c = ActiveControl; c is not null; c = c is ContainerControl cc ? cc.ActiveControl : null)
            if (c.Parent is PillBox pills && pills.Holds(c) && pills.Commit()) return true;
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
            if (c is TextBox or CheckBox or ComboBox && c.Visible && c.Enabled && c.TabStop) fields.Add(c);
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
            Text = "買家備註出現關鍵字時，自動送出對應的訊息（訂單照常處理）。關鍵字輸入後按 Enter，同一個平台內一個關鍵字只能屬於一則訊息。"
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
            _tabs.SelectedIndex = 3;
            return;
        }
        var giftProblems = GiftRules.Problems(_gifts!.Rules());
        if (giftProblems.Count > 0)
        {
            MessageBox.Show(this, "滿件贈送：\n\n" + string.Join("\n", giftProblems), "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 4;
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
            _tabs.SelectedIndex = 2;
            _shopeeLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_sevenLabelScale.Text.Trim(), out var sevenScale) || sevenScale is < 10 or > 200)
        {
            MessageBox.Show(this, "7-11 交貨便縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            _sevenLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_familyLabelScale.Text.Trim(), out var familyScale) || familyScale is < 10 or > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(_familyLabelPages.Text.Trim(), @"^\d+$"))
        {
            MessageBox.Show(this, "全家縮放請填 10–200 的數字，全家列印頁請填頁碼（例如 2）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            _familyLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_hctLabelScale.Text.Trim(), out var hctScale) || hctScale is < 10 or > 200)
        {
            MessageBox.Show(this, "新竹縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            _hctLabelScale.Focus();
            return;
        }
        if (!int.TryParse(_coupangHctLabelScale.Text.Trim(), out var coupangHctScale) || coupangHctScale is < 10 or > 200)
        {
            MessageBox.Show(this, "酷澎宅配縮放請填 10–200 的數字（Chrome 列印的縮放比例）。", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            _coupangHctLabelScale.Focus();
            return;
        }
        if (SlipNote.Check(_slipNote.Text, SlipPrinter.NoteWidthMm) is { } slipNoteError)
        {
            MessageBox.Show(this, slipNoteError, "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedIndex = 2;
            _slipNote.Focus();
            return;
        }
        _settings.SlipNote = string.Join("\n", SlipNote.Lines(_slipNote.Text));
        _settings.HctLabelScale = hctScale;
        _settings.CoupangHctLabelScale = coupangHctScale;
        _settings.HctPickupAddress = _hctPickupAddress.Text.Trim();
        _settings.GiftRules = GiftRules.Clean(_gifts!.Rules());
        _settings.ShopeeChatRules = ChatRules.Clean(_shopeeChat!.Rules());
        _settings.MoChatRules = ChatRules.Clean(_moChat!.Rules());
        _settings.CoupangChatRules = ChatRules.Clean(_coupangChat!.Rules());
        _settings.SevenLabelScale = sevenScale;
        _settings.FamilyLabelScale = familyScale;
        _settings.FamilyLabelPages = _familyLabelPages.Text.Trim();
        _settings.ShopeeLabelPrinter = _printers!.Thermal;
        _settings.A4Printer = _printers.A4;
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
        _settings.CyInvoicePath = _cyInvoicePath.Text.Trim();
        _settings.PaperInvoiceStyle = (_paperInvoiceStyle.SelectedItem as PaperStyleItem)?.Code ?? 0;
        _settings.FirstRowWarehouseEnabled = _firstRowWarehouse.Checked;
        _settings.FirstRowWarehouse = warehouse;
        _settings.AdvancedMode = _advanced.Checked;
        _settings.AutoSave = _autoSave.Checked;
        _settings.AutoPrint = _autoPrint.Checked;
        _settings.ClearAfterSave = _clearAfterSave.Checked;
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
            Keywords = string.Join(",", ((PillBox)r.GetControlFromPosition(1, 0)!).Values),
            Message = ((TextBox)r.GetControlFromPosition(1, 1)!).Text
        }).ToList();

    private void AddRow(ChatRule rule)
    {
        var row = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 392));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        // 關鍵字 as pills (user, 2026-10-14).
        var keywords = new PillBox(ChatRules.Words(rule.Keywords), "輸入關鍵字後按 Enter", 384) { Margin = new Padding(0, 4, 0, 4) };
        var message = new TextBox { Width = 384, Height = 52, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = rule.Message, Margin = new Padding(0, 2, 0, 2) };
        var remove = new CyButton { Text = "刪除此則", Width = 84, Height = 30, Enabled = !_locked };
        remove.Click += (_, _) => { _rows.Controls.Remove(row); row.Dispose(); Renumber(); };
        row.Controls.Add(new Label { AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, 0, 0);
        row.Controls.Add(keywords, 1, 0);
        row.Controls.Add(remove, 2, 0);
        row.Controls.Add(new Label { Text = "訊息", AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, 0, 1);
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

/// <summary>
/// 滿件贈送 cards with 新增 at the top (user, 2026-10-14: three rows per card — 適用平台 with
/// 刪除組合 on the right; 品號 as pills; 合計滿 n 件 送 m 件，贈送 件數最多的／指定品號).
/// </summary>
internal sealed class GiftRuleEditor : Panel
{
    private sealed record Card(PillBox Codes, TextBox Every, TextBox Gift, RadioButton Most, TextBox GiftItem, CheckBox[] Platforms);

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
            Text = "匯入的訂單中，贈送組合裡的品號合計每滿幾件，就送幾件，填在指定品號或件數最多的那個品號第一列的「贈/備品量」（例如買 10 送 1：10 件填 1、20 件填 2）。品號輸入後按 Enter。"
        });
        var add = new CyButton { Text = "新增", Width = 80, Height = 30 };
        add.Click += (_, _) => AddCard(new GiftRule { Every = 10, Gift = 1, Platforms = [ShopeeOrderImport.Source] });
        head.Controls.Add(add);
        Controls.Add(_cards);
        Controls.Add(head);
        foreach (var rule in rules) AddCard(rule);
    }

    public List<GiftRule> Rules() =>
        _cards.Controls.OfType<GroupBox>().Select(box => (Card)box.Tag!).Select(c => new GiftRule
        {
            ItemCodes = string.Join(",", c.Codes.Values),
            Every = int.TryParse(c.Every.Text.Trim(), out var every) ? every : 0,
            Gift = int.TryParse(c.Gift.Text.Trim(), out var gift) ? gift : 0,
            GiftItem = c.Most.Checked ? string.Empty : c.GiftItem.Text,
            Platforms = c.Platforms.Where(p => p.Checked).Select(p => p.Text).ToList()
        }).ToList();

    private void AddCard(GiftRule rule)
    {
        const int inner = 536;
        var box = new GroupBox { Width = 560, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 8), Margin = new Padding(0, 0, 0, 8) };
        var table = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, AutoSize = true, Location = new Point(10, 22), Width = inner, Margin = Padding.Empty };
        Label L(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 8, 4, 0) };
        // Whole numbers only, no arrows (user, 2026-10-14).
        TextBox N(int value)
        {
            var t = new TextBox { Width = 44, Text = value > 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty, MaxLength = 4, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 4, 4, 4) };
            t.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            return t;
        }

        // Row 1: 適用平台 … 刪除組合 (right).
        var top = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Width = inner, Height = 36, Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var platformsFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        platformsFlow.Controls.Add(L("適用平台"));
        var platforms = GiftRules.Platforms.Select(p => new CheckBox { Text = p, AutoSize = true, Checked = rule.Platforms.Contains(p), Margin = new Padding(0, 6, 12, 0) }).ToArray();
        foreach (var p in platforms) platformsFlow.Controls.Add(p);
        var remove = new CyButton { Text = "刪除組合", Width = 84, Height = 30, Margin = new Padding(0, 2, 0, 0), Anchor = AnchorStyles.Right };
        remove.Click += (_, _) => { _cards.Controls.Remove(box); box.Dispose(); Renumber(); };
        top.Controls.Add(platformsFlow, 0, 0);
        top.Controls.Add(remove, 1, 0);

        // Row 2: 品號 pills.
        var codesRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        codesRow.Controls.Add(L("品號"));
        var codes = new PillBox(GiftRules.Codes(rule.ItemCodes), "輸入品號後按 Enter", inner - 44) { Margin = new Padding(0, 4, 0, 4) };
        codesRow.Controls.Add(codes);

        // Row 3: 合計滿 n 件 送 m 件，贈送 件數最多的／指定品號.
        var countRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var every = N(rule.Every);
        var gift = N(rule.Gift);
        var most = new RadioButton { Text = "件數最多的", AutoSize = true, Checked = rule.GiftItem.Trim().Length == 0, Margin = new Padding(0, 6, 6, 0) };
        var named = new RadioButton { Text = "指定品號", AutoSize = true, Checked = rule.GiftItem.Trim().Length > 0, Margin = new Padding(0, 6, 2, 0) };
        var giftItem = new TextBox { Width = 96, Text = rule.GiftItem, Enabled = named.Checked, Margin = new Padding(0, 4, 0, 4) };
        named.CheckedChanged += (_, _) => giftItem.Enabled = named.Checked;
        countRow.Controls.Add(L("合計滿"));
        countRow.Controls.Add(every);
        countRow.Controls.Add(L("件 送"));
        countRow.Controls.Add(gift);
        countRow.Controls.Add(L("件，贈送"));
        countRow.Controls.Add(most);
        countRow.Controls.Add(named);
        countRow.Controls.Add(giftItem);

        table.Controls.Add(top, 0, 0);
        table.Controls.Add(codesRow, 0, 1);
        table.Controls.Add(countRow, 0, 2);
        box.Controls.Add(table);
        box.Tag = new Card(codes, every, gift, most, giftItem, platforms);
        _cards.Controls.Add(box);
        Renumber();
    }

    /// <summary>贈送組合1, 贈送組合2… as the save check names them (user, 2026-10-14).</summary>
    private void Renumber()
    {
        var i = 1;
        foreach (var box in _cards.Controls.OfType<GroupBox>()) box.Text = $"贈送組合{i++}";
    }
}

/// <summary>
/// 印表機 (user, 2026-10-14): the printers installed on this PC, read by the program, one row
/// each with 熱感紙 and A4 boxes; one printer per column. A saved printer that is not installed
/// shows as 未安裝 so the choice is not lost silently.
/// </summary>
internal sealed class PrinterPicker : GroupBox
{
    private readonly List<(string Name, CheckBox Thermal, CheckBox A4)> _rows = [];

    public PrinterPicker(string thermal, string a4)
    {
        Text = "印表機（程式讀取這台電腦的印表機，勾選用途；每種只能一台）";
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10, 6, 10, 8);
        Margin = new Padding(0, 0, 0, 10);
        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Location = new Point(10, 22), Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        Label Head(string text) => new() { Text = text, AutoSize = true, ForeColor = CyVisualTheme.TextSecondary, Margin = new Padding(0, 2, 0, 2) };
        grid.Controls.Add(Head("印表機"), 0, 0);
        grid.Controls.Add(Head("熱感紙"), 1, 0);
        grid.Controls.Add(Head("A4"), 2, 0);

        var names = new List<string>();
        try { foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters) names.Add(printer); }
        catch (System.ComponentModel.Win32Exception) { }
        // The saved ones match an installed printer the same way printing does (a shortened name works).
        string? Match(string saved) => saved.Length == 0 ? null
            : names.FirstOrDefault(n => string.Equals(n, saved, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault(n => ShopeeBatchScreens.SamePrinter(n, saved));
        var thermalName = Match(thermal);
        var a4Name = Match(a4);
        var missing = new[] { (thermal, thermalName), (a4, a4Name) }.Where(p => p.Item1.Length > 0 && p.Item2 is null).Select(p => p.Item1).Distinct().ToList();

        foreach (var name in names.Concat(missing))
        {
            var installed = names.Contains(name);
            var row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = installed ? name : name + "（未安裝）", AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = installed ? CyVisualTheme.TextPrimary : CyVisualTheme.Danger }, 0, row);
            var t = new CheckBox { AutoSize = true, Checked = name == (thermalName ?? (missing.Contains(thermal) ? thermal : null)), Margin = new Padding(16, 4, 0, 0) };
            var f = new CheckBox { AutoSize = true, Checked = name == (a4Name ?? (missing.Contains(a4) ? a4 : null)), Margin = new Padding(8, 4, 0, 0) };
            _rows.Add((name, t, f));
            grid.Controls.Add(t, 1, row);
            grid.Controls.Add(f, 2, row);
        }
        foreach (var (_, t, f) in _rows)
        {
            t.CheckedChanged += (_, _) => { if (t.Checked) foreach (var r in _rows) if (r.Thermal != t) r.Thermal.Checked = false; };
            f.CheckedChanged += (_, _) => { if (f.Checked) foreach (var r in _rows) if (r.A4 != f) r.A4.Checked = false; };
        }
        if (_rows.Count == 0) grid.Controls.Add(new Label { Text = "這台電腦沒有安裝印表機。", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, grid.RowCount++);
        Controls.Add(grid);
    }

    public string Thermal => _rows.FirstOrDefault(r => r.Thermal.Checked).Name ?? string.Empty;
    public string A4 => _rows.FirstOrDefault(r => r.A4.Checked).Name ?? string.Empty;
}

/// <summary>One CYInvoice PDF style in the 紙本發票 drop-down.</summary>
internal sealed record PaperStyleItem(int Code, string Name)
{
    public override string ToString() => Name;
}
