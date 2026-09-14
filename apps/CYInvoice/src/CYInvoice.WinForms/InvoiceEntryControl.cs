using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InvoiceEntryControl : UserControl
{
    private const int MinimumVisibleRows = 5;
    private static readonly object PlaceholderRow = new();
    private readonly LocalRepository repository;
    private readonly InvoiceService service;
    private readonly Action recordsChanged;
    private readonly RadioButton automaticOrder = new() { Text = "自動產生", AutoSize = true, Checked = true };
    private readonly RadioButton customOrder = new() { Text = "自訂", AutoSize = true };
    private readonly TextBox orderId = UiControls.TextBox(40);
    private readonly RadioButton consumerBuyer = new() { Text = "一般消費者（紙本）", AutoSize = true, Checked = true };
    private readonly RadioButton companyBuyer = new() { Text = "公司統編（紙本）", AutoSize = true };
    private readonly TextBox buyerBan = UiControls.TextBox(8);
    private readonly TextBox buyerName = UiControls.TextBox(200);
    private readonly RadioButton taxInclusive = new() { Text = "以含稅輸入", AutoSize = true, Checked = true };
    private readonly RadioButton taxExclusive = new() { Text = "以未稅輸入", AutoSize = true };
    private readonly NativeListViewHost itemsHost = new(12F, 26);
    private readonly TextBox remark = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = InvoiceLimits.MaximumRemarkCharacters };
    private readonly Label remarkCounter = UiControls.Label("0 / 200", ContentAlignment.MiddleRight);
    private readonly Label salesTotal = TotalLabel(false);
    private readonly Label taxTotal = TotalLabel(false);
    private readonly Label invoiceTotal = TotalLabel(true);
    private readonly Button issueButton = new() { Width = 160, Height = 40 };
    private readonly Button addItemButton = new()
    {
        Text = "＋ 新增明細",
        Width = 150,
        Height = 40,
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        Margin = new Padding(4, 3, 4, 3),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.ControlText,
        UseVisualStyleBackColor = true,
    };
    private NameLookup? cachedLookup;
    private string cachedBan = string.Empty;
    private TextBox? cellEditor;
    private int editorRow = -1;
    private int editorColumn = -1;
    private bool committingEditor;
    private TableLayoutPanel? rootLayout;
    private TableLayoutPanel? itemsLayout;
    private GroupBox? itemsGroup;

    private ListView Items => itemsHost.List;

    public InvoiceEntryControl(LocalRepository repository, InvoiceService service, Action recordsChanged)
    {
        this.repository = repository;
        this.service = service;
        this.recordsChanged = recordsChanged;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18, 12, 18, 12);
        Font = new Font("Microsoft JhengHei UI", 12F);
        BuildLayout();
        ConfigureEvents();
        ResetDraft();
        HandleCreated += (_, _) => BeginInvoke((Action)ApplyMeasuredLayout);
    }

    private void BuildLayout()
    {
        rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        rootLayout.Controls.Add(BuildImports(), 0, 0);
        rootLayout.Controls.Add(BuildBuyer(), 0, 1);
        rootLayout.Controls.Add(BuildItems(), 0, 2);
        rootLayout.Controls.Add(BuildSummary(), 0, 3);
        rootLayout.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(rootLayout);
    }

    private Control BuildImports()
    {
        var group = new GroupBox { Text = "Excel 匯入開立", Dock = DockStyle.Fill, Padding = new Padding(12, 7, 12, 8) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(8, 4, 0, 0) };
        buttons.Controls.Add(FixedButton("匯入鼎新 ERP 銷貨單", 210, (_, _) => Pending("鼎新 ERP 匯入")));
        buttons.Controls.Add(FixedButton("匯入 MO店+", 145, async (_, _) => await ImportMoAsync()));
        buttons.Controls.Add(FixedButton("匯入酷澎", 135, async (_, _) => await ImportCoupangAsync()));
        group.Controls.Add(buttons);
        return group;
    }

    private Control BuildBuyer()
    {
        var group = new GroupBox { Text = "發票基本資料", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        foreach (var width in new[] { 100, 250, 105, 230, 0 })
            layout.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, width));
        var orderModes = RadioGroup(automaticOrder, customOrder);
        var buyerModes = RadioGroup(consumerBuyer, companyBuyer);
        layout.Controls.Add(UiControls.Label("訂單編號"), 0, 0);
        layout.Controls.Add(orderModes, 1, 0);
        layout.Controls.Add(orderId, 2, 0);
        layout.SetColumnSpan(orderId, 3);
        layout.Controls.Add(UiControls.Label("買方資料"), 0, 1);
        layout.Controls.Add(buyerModes, 1, 1);
        layout.SetColumnSpan(buyerModes, 4);
        layout.Controls.Add(UiControls.Label("統一編號"), 0, 2);
        layout.Controls.Add(buyerBan, 1, 2);
        layout.Controls.Add(UiControls.Label("買方名稱"), 2, 2);
        layout.Controls.Add(buyerName, 3, 2);
        layout.SetColumnSpan(buyerName, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildItems()
    {
        itemsGroup = new GroupBox { Text = "商品明細資料（最多 50 筆）", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 10) };
        itemsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        itemsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        itemsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 5, 0, 0) };
        modes.Controls.Add(taxInclusive);
        modes.Controls.Add(taxExclusive);
        addItemButton.Click += (_, _) => AddRow(true);
        toolbar.Controls.Add(modes, 0, 0);
        toolbar.Controls.Add(addItemButton, 1, 0);
        ConfigureItemsList();
        itemsLayout.Controls.Add(toolbar, 0, 0);
        itemsLayout.Controls.Add(itemsHost, 0, 1);
        itemsGroup.Controls.Add(itemsLayout);
        return itemsGroup;
    }

    private void ConfigureItemsList()
    {
        Items.Columns.Add("序號", 56, HorizontalAlignment.Center);
        Items.Columns.Add("品名", 360, HorizontalAlignment.Left);
        Items.Columns.Add("課稅別", 88, HorizontalAlignment.Center);
        Items.Columns.Add("數量", 88, HorizontalAlignment.Right);
        Items.Columns.Add("單價（含稅）", 150, HorizontalAlignment.Right);
        Items.Columns.Add("金額（含稅）", 155, HorizontalAlignment.Right);
        Items.Columns.Add("操作", 86, HorizontalAlignment.Center);
        Items.OwnerDraw = true;
        Items.DrawColumnHeader += (_, eventArgs) => NativeListViewHost.DrawHeader(eventArgs, Items.Font);
        Items.DrawItem += (_, eventArgs) => { if (Items.View != View.Details) eventArgs.DrawDefault = true; };
        Items.DrawSubItem += DrawItemSubItem;
        itemsHost.ViewportChanged += (_, _) => LayoutItemColumns();
    }

    private Control BuildSummary()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 10, 0, 0), MinimumSize = new Size(0, 150) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        var remarkGroup = new GroupBox { Text = "發票總備註", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var remarkLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        remarkLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        remarkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        remarkLayout.Controls.Add(remark, 0, 0);
        remarkLayout.Controls.Add(remarkCounter, 0, 1);
        remarkGroup.Controls.Add(remarkLayout);
        var totalGroup = new GroupBox { Text = "金額總計", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334F));
        AddTotal(totals, 0, "應稅銷售額", salesTotal);
        AddTotal(totals, 1, "營業稅額（5%）", taxTotal);
        AddTotal(totals, 2, "發票總額", invoiceTotal);
        totalGroup.Controls.Add(totals);
        split.Controls.Add(remarkGroup, 0, 0);
        split.Controls.Add(totalGroup, 1, 0);
        return split;
    }

    private static void AddTotal(TableLayoutPanel panel, int row, string text, Label value)
    {
        panel.Controls.Add(UiControls.Label(text), 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private Control BuildActions()
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        actions.Controls.Add(FixedButton("清空", 150, (_, _) => ResetDraft()));
        issueButton.Click += async (_, _) => await IssueAsync();
        actions.Controls.Add(issueButton);
        actions.Controls.Add(FixedButton("預覽", 150, (_, _) => Preview()));
        actions.Resize += (_, _) => actions.Padding = new Padding(Math.Max(0, (actions.ClientSize.Width - 480) / 2), 6, 0, 0);
        return actions;
    }

    private void ConfigureEvents()
    {
        automaticOrder.CheckedChanged += (_, _) => UpdateOrderMode();
        companyBuyer.CheckedChanged += (_, _) => UpdateBuyerMode();
        taxInclusive.CheckedChanged += (_, _) => { UpdateHeaders(); Recalculate(); };
        buyerBan.TextChanged += (_, _) => { cachedLookup = null; cachedBan = string.Empty; };
        buyerBan.Leave += async (_, _) => await LookupBuyerAsync(true);
        remark.TextChanged += (_, _) => remarkCounter.Text = $"{remark.Text.EnumerateRunes().Count()} / {InvoiceLimits.MaximumRemarkCharacters}";
        Items.MouseDown += (_, eventArgs) =>
        {
            var hit = Items.HitTest(eventArgs.X, eventArgs.Y);
            var row = hit.Item?.Index ?? -1;
            var column = hit.SubItem is null || hit.Item is null ? -1 : hit.Item.SubItems.IndexOf(hit.SubItem);
            if (row < 0 || row >= Items.Items.Count || IsPlaceholder(Items.Items[row]))
            {
                CommitCellEditor(false);
                QueueClearItemSelection();
                return;
            }
            if (column == 6 && hit.Item is { } hitItem && DeleteButtonBounds(hitItem).Contains(eventArgs.Location))
            {
                CommitCellEditor(false);
                DeleteRow(row);
                return;
            }
            if (column is 1 or 3 or 4)
            {
                BeginInvoke((Action)(() => BeginCellEdit(row, column)));
                return;
            }
            CommitCellEditor(false);
            QueueClearItemSelection();
        };
        Items.MouseWheel += (_, _) => CommitCellEditor(false);
        Items.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter || Items.FocusedItem is null || IsPlaceholder(Items.FocusedItem)) return;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            BeginCellEdit(Items.FocusedItem.Index, 1);
        };
    }

    private void ResetDraft()
    {
        automaticOrder.Checked = true;
        consumerBuyer.Checked = true;
        taxInclusive.Checked = true;
        buyerBan.Clear(); buyerName.Clear(); remark.Clear();
        CommitCellEditor(true);
        Items.Items.Clear();
        AddRow(false);
        EnsurePlaceholderRows();
        ClearItemSelection();
        UpdateOrderMode(); UpdateBuyerMode(); UpdateHeaders(); Recalculate();
    }

    public void RefreshEnvironment() => UpdateHeaders();

    private void UpdateOrderMode()
    {
        orderId.ReadOnly = automaticOrder.Checked;
        orderId.BackColor = automaticOrder.Checked ? Color.FromArgb(242, 242, 242) : Color.White;
        if (automaticOrder.Checked) orderId.Text = ManualOrderId.Next(DateTimeOffset.Now, repository.Invoices.LoadOrCreate());
    }

    private void UpdateBuyerMode()
    {
        buyerBan.Enabled = companyBuyer.Checked;
        buyerName.Enabled = companyBuyer.Checked;
        taxExclusive.Enabled = companyBuyer.Checked;
        if (!companyBuyer.Checked) { buyerBan.Clear(); buyerName.Clear(); taxInclusive.Checked = true; }
    }

    private void UpdateHeaders()
    {
        CommitCellEditor(false);
        var mode = taxExclusive.Checked ? "未稅" : "含稅";
        Items.Columns[4].Text = $"單價（{mode}）";
        Items.Columns[5].Text = $"金額（{mode}）";
        issueButton.Text = repository.Settings.LoadOrCreate().Environment == Environments.Production ? "開立正式發票" : "開立測試發票";
    }

    private void AddRow(bool focus)
    {
        CommitCellEditor(false);
        if (ActualRows().Count >= InvoiceLimits.MaximumItems)
        {
            MessageBox.Show(this, $"商品明細最多 {InvoiceLimits.MaximumItems} 筆", "無法新增", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RemovePlaceholderRows();
        var item = NewRow([(ActualRows().Count + 1).ToString(CultureInfo.InvariantCulture), "", "應稅", "", "", "", "刪除"]);
        Items.Items.Add(item);
        var index = item.Index;
        EnsurePlaceholderRows();
        if (focus) BeginCellEdit(index, 1);
    }

    private void DeleteRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Items.Items.Count) return;
        var row = Items.Items[rowIndex];
        if (IsPlaceholder(row)) return;
        if (ActualRows().Count <= 1)
        {
            for (var column = 1; column <= 5; column++) row.SubItems[column].Text = column == 2 ? "應稅" : "";
        }
        else
        {
            Items.Items.RemoveAt(rowIndex);
            RenumberActualRows();
        }
        EnsurePlaceholderRows();
        Recalculate();
    }

    private void CalculateRow(ListViewItem row)
    {
        if (IsPlaceholder(row)) return;
        try
        {
            var quantityText = Clean(Cell(row, 3));
            var priceText = Clean(Cell(row, 4));
            if (quantityText.Length == 0 || priceText.Length == 0) { row.SubItems[5].Text = ""; return; }
            var quantity = FixedDecimal.Parse(quantityText);
            var price = FixedDecimal.Parse(priceText);
            row.SubItems[4].Text = MoneyFormatter.Decimal(price.ToString());
            row.SubItems[5].Text = MoneyFormatter.Decimal(FixedDecimal.Multiply(quantity, price).ToString());
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            row.SubItems[5].Text = "格式錯誤";
        }
        Items.Invalidate(row.Bounds);
    }

    private void Recalculate()
    {
        try
        {
            var draft = BuildDraft(false);
            var totals = InvoiceCalculator.CalculateTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax);
            salesTotal.Text = MoneyFormatter.Integer(totals.SalesAmount);
            taxTotal.Text = MoneyFormatter.Integer(totals.TaxAmount);
            invoiceTotal.Text = MoneyFormatter.Integer(totals.TotalAmount);
        }
        catch (Exception)
        {
            salesTotal.Text = "—"; taxTotal.Text = "—"; invoiceTotal.Text = "—";
        }
    }

    private InvoiceDraft BuildDraft(bool validate)
    {
        var draft = new InvoiceDraft
        {
            OrderId = orderId.Text.Trim(), CompanyBuyer = companyBuyer.Checked, BuyerIdentifier = buyerBan.Text.Trim(),
            BuyerName = buyerName.Text.Trim(), PricesExcludeTax = taxExclusive.Checked, MainRemark = remark.Text,
        };
        CommitCellEditor(false);
        var rows = ActualRows();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var description = Cell(row, 1);
            var quantityText = Clean(Cell(row, 3));
            var priceText = Clean(Cell(row, 4));
            if (description.Length == 0 && quantityText.Length == 0 && priceText.Length == 0) continue;
            var quantity = Parse(quantityText, $"第 {index + 1} 筆商品數量格式錯誤");
            var price = Parse(priceText, $"第 {index + 1} 筆商品單價格式錯誤");
            var amount = FixedDecimal.Multiply(quantity, price);
            draft.Items.Add(new InvoiceItem
            {
                Description = description, Quantity = double.Parse(quantity.ToString(), CultureInfo.InvariantCulture), QuantityDecimal = quantity.ToString(),
                UnitPrice = price.RoundInt64(), UnitPriceDecimal = price.ToString(), Amount = amount.RoundInt64(), AmountDecimal = amount.ToString(), TaxType = "1",
            });
        }
        draft.TotalAmount = InvoiceCalculator.CalculateTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax).TotalAmount;
        if (validate) InvoiceValidator.Validate(draft);
        return draft;
    }

    private static FixedDecimal Parse(string value, string message)
    {
        try { return FixedDecimal.Parse(value); }
        catch (Exception error) when (error is FormatException or OverflowException) { throw new InvalidOperationException(message, error); }
    }

    private void Preview()
    {
        try
        {
            var draft = BuildDraft(true);
            var lines = string.Join(Environment.NewLine, draft.Items.Select((item, index) =>
            {
                var value = InvoiceCalculator.ItemDecimals(item);
                return $"{index + 1}. {item.Description}　{value.Quantity} × {MoneyFormatter.Decimal(value.UnitPrice.ToString())} ＝ {MoneyFormatter.Decimal(value.Amount.ToString())}";
            }));
            var buyer = draft.CompanyBuyer ? $"{draft.BuyerIdentifier}　{draft.BuyerName}" : "一般消費者（紙本）";
            MessageBox.Show(this, $"訂單編號：{draft.OrderId}\n買受人：{buyer}\n\n商品明細：\n{lines}\n\n發票總額：{MoneyFormatter.Integer(draft.TotalAmount)}", "發票預覽", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "無法預覽", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task IssueAsync()
    {
        try
        {
            var draft = BuildDraft(true);
            NameLookup lookup = new();
            if (draft.CompanyBuyer)
            {
                lookup = cachedBan == draft.BuyerIdentifier && cachedLookup is not null ? cachedLookup : await service.LookupBuyerNameAsync(draft.BuyerIdentifier);
                if (lookup.Name.Length != 0) { draft.BuyerName = lookup.Name; buyerName.Text = lookup.Name; }
                if (draft.BuyerName.Trim().Length == 0) throw new InvalidOperationException("查無此統一編號，請再次確認或自行輸入買方名稱");
            }
            var formal = repository.Settings.LoadOrCreate().Environment == Environments.Production;
            var confirm = MessageBox.Show(this,
                $"即將開立{(formal ? "正式發票" : "測試發票")}\n\n訂單編號：{draft.OrderId}\n發票總額：{MoneyFormatter.Integer(draft.TotalAmount)}\n\n請再次確認，送出後不可因等待時間較長而重複開立。",
                "確認開立", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.OK) return;
            Enabled = false;
            var result = await service.IssueManualWithLookupAsync(draft, lookup);
            recordsChanged();
            MessageBox.Show(this, $"發票開立成功\n\n發票號碼：{result.Record.InvoiceNumber}\n訂單編號：{result.Record.OrderId}\n金額：{MoneyFormatter.Integer(result.Record.Amount)}", "開立成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ResetDraft();
        }
        catch (Exception error)
        {
            recordsChanged();
            MessageBox.Show(this, error.Message, "開立未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Enabled = true; }
    }

    private async Task LookupBuyerAsync(bool showNotFound)
    {
        var ban = buyerBan.Text.Trim();
        if (!companyBuyer.Checked || ban.Length != 8 || !ban.All(char.IsAsciiDigit)) return;
        try
        {
            buyerBan.Enabled = false;
            cachedLookup = await service.LookupBuyerNameAsync(ban);
            cachedBan = ban;
            if (cachedLookup.Name.Length != 0) buyerName.Text = cachedLookup.Name;
            else if (showNotFound)
            {
                MessageBox.Show(this, "查無此統一編號，請再次確認或自行輸入買方名稱", "查無統編", MessageBoxButtons.OK, MessageBoxIcon.Information);
                buyerName.Focus();
            }
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "統編查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { buyerBan.Enabled = companyBuyer.Checked; }
    }

    private Task ImportMoAsync()
    {
        using var dialog = FileDialog("Excel 檔案 (*.xls;*.xlsx;*.xlsm)|*.xls;*.xlsx;*.xlsm");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return Task.CompletedTask;
        try
        {
            var password = repository.Settings.MoPassword(repository.Settings.LoadOrCreate());
            if (password.Length == 0) throw new InvalidOperationException("請先到設定輸入 MO店+ Excel 保護密碼");
            using var confirmation = ImportConfirmationForm.ForMo(repository, service, recordsChanged, dialog.FileName, password);
            confirmation.ShowDialog(FindForm());
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "MO店+ 匯入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        return Task.CompletedTask;
    }

    private Task ImportCoupangAsync()
    {
        using var dialog = FileDialog("Excel 檔案 (*.xls;*.xlsx;*.xlsm)|*.xls;*.xlsx;*.xlsm");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return Task.CompletedTask;
        try
        {
            using var confirmation = ImportConfirmationForm.ForCoupang(repository, service, recordsChanged, dialog.FileName);
            confirmation.ShowDialog(FindForm());
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "酷澎匯入失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        return Task.CompletedTask;
    }
    private void Pending(string feature) => MessageBox.Show(this, $"{feature}尚未接入 C# 重製測試線，現在不會讀檔或送出發票。", "功能尚未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private static OpenFileDialog FileDialog(string filter) => new() { Filter = filter, CheckFileExists = true, Multiselect = false, RestoreDirectory = true };
    private static FlowLayoutPanel RadioGroup(params RadioButton[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        foreach (var button in buttons)
        {
            button.Margin = new Padding(3, 3, 26, 3);
            panel.Controls.Add(button);
        }
        return panel;
    }

    private List<ListViewItem> ActualRows() => Items.Items.Cast<ListViewItem>().Where(row => !IsPlaceholder(row)).ToList();

    private static bool IsPlaceholder(ListViewItem row) => ReferenceEquals(row.Tag, PlaceholderRow);

    private void RemovePlaceholderRows()
    {
        for (var index = Items.Items.Count - 1; index >= 0; index--)
        {
            if (IsPlaceholder(Items.Items[index])) Items.Items.RemoveAt(index);
        }
    }

    private void EnsurePlaceholderRows()
    {
        RemovePlaceholderRows();
        for (var index = Items.Items.Count; index < MinimumVisibleRows; index++)
        {
            var placeholder = NewRow(["", "", "", "", "", "", ""]);
            placeholder.Tag = PlaceholderRow;
            Items.Items.Add(placeholder);
        }
        itemsHost.SetScrollNeeded(ActualRows().Count > MinimumVisibleRows);
        StyleItemRows();
        LayoutItemColumns();
    }

    private void RenumberActualRows()
    {
        var number = 1;
        foreach (var row in ActualRows()) row.SubItems[0].Text = (number++).ToString(CultureInfo.InvariantCulture);
    }

    private void BeginCellEdit(int rowIndex, int columnIndex)
    {
        CommitCellEditor(false);
        if (rowIndex < 0 || rowIndex >= Items.Items.Count || columnIndex is not (1 or 3 or 4)) return;
        var row = Items.Items[rowIndex];
        if (IsPlaceholder(row)) return;
        var bounds = row.SubItems[columnIndex].Bounds;
        if (bounds.Width <= 8 || bounds.Height <= 4) return;
        ClearItemSelection();
        var value = columnIndex == 4 ? Clean(Cell(row, columnIndex)) : Cell(row, columnIndex);
        var editor = new TextBox
        {
            Text = value,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Items.Font,
            MaxLength = columnIndex == 1 ? 256 : 64,
            TextAlign = columnIndex is 3 or 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Bounds = Rectangle.Inflate(bounds, -2, -1),
        };
        editorRow = rowIndex;
        editorColumn = columnIndex;
        cellEditor = editor;
        editor.KeyDown += CellEditorKeyDown;
        editor.Leave += (_, _) => CommitCellEditor(false);
        Items.Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
        editor.SelectAll();
    }

    private void CellEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            CommitCellEditor(true);
            Items.Focus();
            return;
        }
        if (eventArgs.KeyCode is not (Keys.Enter or Keys.Tab)) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        var row = editorRow;
        var column = editorColumn;
        CommitCellEditor(false);
        BeginInvoke((Action)(() => MoveToNextItemField(row, column)));
    }

    private void CommitCellEditor(bool cancel)
    {
        if (cellEditor is null || committingEditor) return;
        committingEditor = true;
        var editor = cellEditor;
        var rowIndex = editorRow;
        var columnIndex = editorColumn;
        cellEditor = null;
        editorRow = -1;
        editorColumn = -1;
        try
        {
            if (!cancel && rowIndex >= 0 && rowIndex < Items.Items.Count && !IsPlaceholder(Items.Items[rowIndex]))
            {
                Items.Items[rowIndex].SubItems[columnIndex].Text = editor.Text.Trim();
                CalculateRow(Items.Items[rowIndex]);
                Recalculate();
            }
            editor.Dispose();
        }
        finally
        {
            committingEditor = false;
        }
    }

    private void MoveToNextItemField(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= Items.Items.Count || IsPlaceholder(Items.Items[rowIndex])) return;
        var nextColumn = columnIndex switch { 1 => 3, 3 => 4, _ => -1 };
        if (nextColumn >= 0)
        {
            BeginCellEdit(rowIndex, nextColumn);
            return;
        }

        var actual = ActualRows();
        var position = actual.FindIndex(row => row.Index == rowIndex);
        if (position >= 0 && position + 1 < actual.Count)
            BeginCellEdit(actual[position + 1].Index, 1);
        else
            addItemButton.Focus();
    }

    private void QueueClearItemSelection()
    {
        if (cellEditor is not null || !Items.IsHandleCreated || Items.IsDisposed) return;
        Items.BeginInvoke((Action)ClearItemSelection);
    }

    private void ClearItemSelection()
    {
        if (cellEditor is not null && cellEditor.ContainsFocus) return;
        while (Items.SelectedItems.Count > 0) Items.SelectedItems[0].Selected = false;
        Items.FocusedItem = null;
    }

    private void StyleItemRows()
    {
        for (var rowIndex = 0; rowIndex < Items.Items.Count; rowIndex++)
        {
            var row = Items.Items[rowIndex];
            row.UseItemStyleForSubItems = false;
            var zebra = rowIndex % 2 == 0 ? Color.White : Color.FromArgb(247, 247, 247);
            foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
            {
                subItem.BackColor = zebra;
                subItem.ForeColor = SystemColors.ControlText;
            }
            row.SubItems[5].BackColor = rowIndex % 2 == 0 ? Color.FromArgb(232, 232, 232) : Color.FromArgb(225, 225, 225);
            row.SubItems[5].ForeColor = Color.FromArgb(88, 88, 88);
        }
        Items.Invalidate();
    }

    private void LayoutItemColumns()
    {
        if (Items.Columns.Count != 7 || Items.ClientSize.Width <= 0) return;
        var available = itemsHost.ColumnViewportWidth;
        var fixedWidths = new[] { 56, 88, 88, 150, 155, 86 };
        var nameWidth = Math.Max(160, available - fixedWidths.Sum());
        var widths = new[] { fixedWidths[0], nameWidth, fixedWidths[1], fixedWidths[2], fixedWidths[3], fixedWidths[4], fixedWidths[5] };
        widths[1] += available - widths.Sum();
        itemsHost.SetColumnWidths(widths);
    }

    private void DrawItemSubItem(object? sender, DrawListViewSubItemEventArgs eventArgs)
    {
        if (eventArgs.Item is null || eventArgs.SubItem is null) return;
        var rowIndex = eventArgs.ItemIndex;
        var columnIndex = eventArgs.ColumnIndex;
        var background = columnIndex == 5
            ? (rowIndex % 2 == 0 ? Color.FromArgb(232, 232, 232) : Color.FromArgb(225, 225, 225))
            : (rowIndex % 2 == 0 ? Color.White : Color.FromArgb(247, 247, 247));
        using (var brush = new SolidBrush(background)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);

        if (columnIndex == 6 && !IsPlaceholder(eventArgs.Item))
        {
            var button = DeleteButtonBounds(eventArgs.Item);
            ButtonRenderer.DrawButton(eventArgs.Graphics, button, System.Windows.Forms.VisualStyles.PushButtonState.Normal);
            TextRenderer.DrawText(eventArgs.Graphics, "刪除", Items.Font, button, Color.FromArgb(190, 24, 24),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            flags |= columnIndex is 3 or 4 or 5 ? TextFormatFlags.Right : columnIndex is 0 or 2 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            var textBounds = Rectangle.Inflate(eventArgs.Bounds, -5, 0);
            var color = columnIndex == 5 ? Color.FromArgb(88, 88, 88) : SystemColors.ControlText;
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem.Text, Items.Font, textBounds, color, flags);
        }
        using var pen = new Pen(Color.FromArgb(190, 190, 190));
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Top, eventArgs.Bounds.Right - 1, eventArgs.Bounds.Bottom);
        eventArgs.Graphics.DrawLine(pen, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
    }

    private static Rectangle DeleteButtonBounds(ListViewItem item)
    {
        var bounds = item.SubItems[6].Bounds;
        return Rectangle.Inflate(bounds, -6, -2);
    }

    private static ListViewItem NewRow(IReadOnlyList<string> values)
    {
        var row = new ListViewItem(values[0]);
        for (var index = 1; index < values.Count; index++) row.SubItems.Add(values[index]);
        return row;
    }

    internal void VerifySmokeLayout()
    {
        if (consumerBuyer.Parent is null || companyBuyer.Parent is null || buyerBan.Parent is null || buyerName.Parent is null)
            throw new InvalidOperationException("發票基本資料的買方控制項未建立");
        if (ReferenceEquals(automaticOrder.Parent, consumerBuyer.Parent))
            throw new InvalidOperationException("訂單與買方 RadioButton 未分成兩組");
        if (!consumerBuyer.Checked || buyerBan.Enabled || buyerName.Enabled)
            throw new InvalidOperationException("一般消費者模式未鎖定統編與買方名稱");
        companyBuyer.Checked = true;
        if (!buyerBan.Enabled || !buyerName.Enabled)
            throw new InvalidOperationException("公司統編模式未啟用必要買方欄位");
        consumerBuyer.Checked = true;
        if (Items.Columns.Count != 7 || Items.Items.Count != MinimumVisibleRows || ActualRows().Count != 1)
            throw new InvalidOperationException("商品原生 ListView 未建立一筆實際資料與五列顯示區");
        if (!itemsHost.ScrollSlotReserved) throw new InvalidOperationException("商品清單未保留停用垂直 scrollbar");
        if (Math.Abs(Items.Font.SizeInPoints - 12F) > 0.1F) throw new InvalidOperationException("商品清單未使用 12pt 字級");
        if (Math.Abs(Items.Columns.Cast<ColumnHeader>().Sum(column => column.Width) - itemsHost.ColumnViewportWidth) > 1)
            throw new InvalidOperationException("商品清單欄寬未對齊 scrollbar 前的可視範圍");
        if (itemsHost.VisibleRowCapacity() != MinimumVisibleRows)
            throw new InvalidOperationException($"商品清單可視列數不是五列：{itemsHost.VisibleRowCapacity()}");
        var rowHeights = rootLayout?.GetRowHeights() ?? [];
        if (rowHeights.Length != 5 || rowHeights[3] < 150 || rowHeights.Sum() > ClientSize.Height)
            throw new InvalidOperationException("主畫面摘要或底部操作區高度不足");
        BeginCellEdit(0, 1);
        Application.DoEvents();
        if (cellEditor is null || cellEditor.IsDisposed)
            throw new InvalidOperationException("商品儲存格單擊編輯器建立後立即失去焦點並關閉");
        CommitCellEditor(true);
    }

    private void ApplyMeasuredLayout()
    {
        if (rootLayout is null || itemsLayout is null || itemsGroup is null || IsDisposed) return;
        var listHeight = itemsHost.HeightForRows(MinimumVisibleRows);
        itemsLayout.RowStyles[1].SizeType = SizeType.Absolute;
        itemsLayout.RowStyles[1].Height = listHeight;
        var chrome = Math.Max(18, itemsGroup.Height - itemsGroup.DisplayRectangle.Height);
        rootLayout.RowStyles[2].Height = itemsLayout.RowStyles[0].Height + listHeight + chrome + itemsGroup.Margin.Vertical;
        PerformLayout();
    }

    private static string Cell(ListViewItem row, int column) => row.SubItems[column].Text.Trim();
    private static string Clean(string value) => value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
    private static Button FixedButton(string text, int width, EventHandler handler)
    {
        var button = new Button { Text = text, Width = width, Height = 40, Margin = new Padding(6, 2, 6, 2) };
        button.Click += handler;
        return button;
    }
    private static Label TotalLabel(bool bold) => new()
    {
        Text = "0", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Microsoft JhengHei UI", 12F, bold ? FontStyle.Bold : FontStyle.Regular), Padding = new Padding(0, 0, 8, 0),
    };
}
