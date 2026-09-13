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
    private readonly DataGridView items = UiControls.Grid();
    private readonly TextBox remark = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = InvoiceLimits.MaximumRemarkCharacters };
    private readonly Label remarkCounter = UiControls.Label("0 / 200", ContentAlignment.MiddleRight);
    private readonly Label salesTotal = TotalLabel(false);
    private readonly Label taxTotal = TotalLabel(false);
    private readonly Label invoiceTotal = TotalLabel(true);
    private readonly Button issueButton = new() { Width = 150, Height = 36 };
    private readonly Button addItemButton = new()
    {
        Text = "＋ 新增明細",
        Width = 137,
        Height = 34,
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        Margin = new Padding(4, 3, 4, 3),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.ControlText,
        UseVisualStyleBackColor = true,
    };
    private NameLookup? cachedLookup;
    private string cachedBan = string.Empty;
    private bool gridSelectionClearQueued;

    public InvoiceEntryControl(LocalRepository repository, InvoiceService service, Action recordsChanged)
    {
        this.repository = repository;
        this.service = service;
        this.recordsChanged = recordsChanged;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18, 12, 18, 12);
        BuildLayout();
        ConfigureEvents();
        ResetDraft();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(BuildImports(), 0, 0);
        root.Controls.Add(BuildBuyer(), 0, 1);
        root.Controls.Add(BuildItems(), 0, 2);
        root.Controls.Add(BuildSummary(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
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
        foreach (var width in new[] { 86, 260, 88, 190, 0 })
            layout.ColumnStyles.Add(width == 0 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, width));
        var orderModes = RadioGroup(automaticOrder, customOrder);
        var buyerModes = RadioGroup(consumerBuyer, companyBuyer);
        layout.Controls.Add(UiControls.Label("訂單編號"), 0, 0);
        layout.Controls.Add(orderModes, 1, 0);
        layout.Controls.Add(orderId, 3, 0);
        layout.SetColumnSpan(orderId, 2);
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
        var group = new GroupBox { Text = "商品明細資料（最多 50 筆）", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 5, 0, 0) };
        modes.Controls.Add(taxInclusive);
        modes.Controls.Add(taxExclusive);
        addItemButton.Click += (_, _) => AddRow(true);
        toolbar.Controls.Add(modes, 0, 0);
        toolbar.Controls.Add(addItemButton, 1, 0);
        ConfigureGrid();
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(items, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private void ConfigureGrid()
    {
        items.MultiSelect = false;
        items.SelectionMode = DataGridViewSelectionMode.CellSelect;
        items.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        items.Columns.Add(Column("序號", 48, readOnly: true));
        items.Columns.Add(Column("品名", 360));
        items.Columns.Add(Column("課稅別", 80, readOnly: true));
        items.Columns.Add(Column("數量", 80, right: true));
        items.Columns.Add(Column("單價（含稅）", 135, right: true));
        var amountColumn = Column("金額（含稅）", 140, right: true, readOnly: true);
        amountColumn.DefaultCellStyle.BackColor = Color.FromArgb(232, 232, 232);
        amountColumn.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 232, 232);
        amountColumn.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        items.Columns.Add(amountColumn);
        items.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "操作", Text = "刪除", UseColumnTextForButtonValue = false, Width = 76,
            SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Standard,
            DefaultCellStyle =
            {
                ForeColor = Color.FromArgb(190, 24, 24),
                BackColor = SystemColors.Control,
                SelectionBackColor = SystemColors.Control,
                SelectionForeColor = Color.FromArgb(190, 24, 24),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(3, 1, 3, 1),
            },
        });
        items.MinimumSize = new Size(0, items.ColumnHeadersHeight + (items.RowTemplate.Height * MinimumVisibleRows) + 4);
        UiControls.ReserveVerticalScrollBar(items, 1);
    }

    private Control BuildSummary()
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 10, 0, 0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        var remarkGroup = new GroupBox { Text = "發票總備註", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var remarkLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        remarkLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        remarkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        remarkLayout.Controls.Add(remark, 0, 0);
        remarkLayout.Controls.Add(remarkCounter, 0, 1);
        remarkGroup.Controls.Add(remarkLayout);
        var totalGroup = new GroupBox { Text = "金額總計", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var totals = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
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
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        actions.Controls.Add(FixedButton("清空", 140, (_, _) => ResetDraft()));
        issueButton.Click += async (_, _) => await IssueAsync();
        actions.Controls.Add(issueButton);
        actions.Controls.Add(FixedButton("預覽", 140, (_, _) => Preview()));
        actions.Resize += (_, _) => actions.Padding = new Padding(Math.Max(0, (actions.ClientSize.Width - 450) / 2), 8, 0, 0);
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
        items.CellBeginEdit += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || IsPlaceholder(items.Rows[eventArgs.RowIndex]))
            {
                eventArgs.Cancel = true;
                return;
            }
            if (eventArgs.RowIndex >= 0 && eventArgs.ColumnIndex == 4)
                items.Rows[eventArgs.RowIndex].Cells[4].Value = Cell(items.Rows[eventArgs.RowIndex], 4).Replace(",", string.Empty, StringComparison.Ordinal);
        };
        items.CellEndEdit += (_, eventArgs) => { if (eventArgs.RowIndex >= 0) CalculateRow(items.Rows[eventArgs.RowIndex]); Recalculate(); };
        items.CellContentClick += DeleteClicked;
        items.CellMouseDown += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0) return;
            if (eventArgs.ColumnIndex == 5 || IsPlaceholder(items.Rows[eventArgs.RowIndex]))
                QueueInvalidGridSelectionClear();
        };
        items.CellEnter += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0) return;
            if (eventArgs.ColumnIndex == 5 || IsPlaceholder(items.Rows[eventArgs.RowIndex]))
                QueueInvalidGridSelectionClear();
        };
        items.EditingControlShowing += ConfigureItemEditor;
        items.KeyDown += ItemGridKeyDown;
        items.DataError += (_, eventArgs) => eventArgs.ThrowException = false;
    }

    private void ResetDraft()
    {
        automaticOrder.Checked = true;
        consumerBuyer.Checked = true;
        taxInclusive.Checked = true;
        buyerBan.Clear(); buyerName.Clear(); remark.Clear();
        items.Rows.Clear();
        AddRow(false);
        EnsurePlaceholderRows();
        items.ClearSelection();
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
        buyerBan.ReadOnly = !companyBuyer.Checked;
        buyerName.ReadOnly = !companyBuyer.Checked;
        buyerBan.BackColor = companyBuyer.Checked ? Color.White : Color.FromArgb(242, 242, 242);
        buyerName.BackColor = companyBuyer.Checked ? Color.White : Color.FromArgb(242, 242, 242);
        taxExclusive.Enabled = companyBuyer.Checked;
        if (!companyBuyer.Checked) { buyerBan.Clear(); buyerName.Clear(); taxInclusive.Checked = true; }
    }

    private void UpdateHeaders()
    {
        var mode = taxExclusive.Checked ? "未稅" : "含稅";
        items.Columns[4].HeaderText = $"單價（{mode}）";
        items.Columns[5].HeaderText = $"金額（{mode}）";
        issueButton.Text = repository.Settings.LoadOrCreate().Environment == Environments.Production ? "開立正式發票" : "開立測試發票";
    }

    private void AddRow(bool focus)
    {
        if (ActualRows().Count >= InvoiceLimits.MaximumItems)
        {
            MessageBox.Show(this, $"商品明細最多 {InvoiceLimits.MaximumItems} 筆", "無法新增", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RemovePlaceholderRows();
        var index = items.Rows.Add(items.Rows.Count + 1, "", "應稅", "", "", "", "刪除");
        EnsurePlaceholderRows();
        if (focus) { items.CurrentCell = items.Rows[index].Cells[1]; items.BeginEdit(true); }
    }

    private void DeleteClicked(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != 6) return;
        var row = items.Rows[eventArgs.RowIndex];
        if (IsPlaceholder(row)) return;
        if (ActualRows().Count <= 1)
        {
            for (var column = 1; column <= 5; column++) row.Cells[column].Value = column == 2 ? "應稅" : "";
        }
        else
        {
            items.Rows.RemoveAt(eventArgs.RowIndex);
            RenumberActualRows();
        }
        EnsurePlaceholderRows();
        Recalculate();
    }

    private void CalculateRow(DataGridViewRow row)
    {
        if (IsPlaceholder(row)) return;
        try
        {
            var quantityText = Clean(Cell(row, 3));
            var priceText = Clean(Cell(row, 4));
            if (quantityText.Length == 0 || priceText.Length == 0) { row.Cells[5].Value = ""; return; }
            var quantity = FixedDecimal.Parse(quantityText);
            var price = FixedDecimal.Parse(priceText);
            row.Cells[4].Value = MoneyFormatter.Decimal(price.ToString());
            row.Cells[5].Value = MoneyFormatter.Decimal(FixedDecimal.Multiply(quantity, price).ToString());
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            row.Cells[5].Value = "格式錯誤";
        }
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
        for (var index = 0; index < items.Rows.Count; index++)
        {
            var row = items.Rows[index];
            if (IsPlaceholder(row)) continue;
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
        finally { buyerBan.Enabled = true; }
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
        using var dialog = FileDialog("Excel 檔案 (*.xlsx)|*.xlsx");
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

    private List<DataGridViewRow> ActualRows() => items.Rows.Cast<DataGridViewRow>().Where(row => !IsPlaceholder(row)).ToList();

    private static bool IsPlaceholder(DataGridViewRow row) => ReferenceEquals(row.Tag, PlaceholderRow);

    private void RemovePlaceholderRows()
    {
        for (var index = items.Rows.Count - 1; index >= 0; index--)
        {
            if (IsPlaceholder(items.Rows[index])) items.Rows.RemoveAt(index);
        }
    }

    private void EnsurePlaceholderRows()
    {
        RemovePlaceholderRows();
        for (var index = items.Rows.Count; index < MinimumVisibleRows; index++)
        {
            var placeholderIndex = items.Rows.Add("", "", "", "", "", "", "");
            var placeholder = items.Rows[placeholderIndex];
            placeholder.Tag = PlaceholderRow;
            placeholder.ReadOnly = true;
            placeholder.Cells[6] = new DataGridViewTextBoxCell { Value = string.Empty };
        }
    }

    private void RenumberActualRows()
    {
        var number = 1;
        foreach (var row in items.Rows.Cast<DataGridViewRow>().Where(row => !IsPlaceholder(row)))
            row.Cells[0].Value = number++;
    }

    private void ConfigureItemEditor(object? sender, DataGridViewEditingControlShowingEventArgs eventArgs)
    {
        if (eventArgs.Control is not TextBox editor) return;
        editor.KeyDown -= ItemEditorKeyDown;
        editor.KeyDown += ItemEditorKeyDown;
        if (items.CurrentCell?.ColumnIndex is 3 or 4) editor.SelectAll();
    }

    private void ItemEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        items.EndEdit();
        BeginInvoke(MoveToNextItemField);
    }

    private void ItemGridKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Enter || items.IsCurrentCellInEditMode) return;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        MoveToNextItemField();
    }

    private void MoveToNextItemField()
    {
        var current = items.CurrentCell;
        if (current is null || current.RowIndex < 0 || IsPlaceholder(items.Rows[current.RowIndex])) return;
        var nextColumn = current.ColumnIndex switch { 1 => 3, 3 => 4, _ => -1 };
        if (nextColumn >= 0)
        {
            FocusItemCell(current.RowIndex, nextColumn);
            return;
        }

        var actual = ActualRows();
        var position = actual.FindIndex(row => row.Index == current.RowIndex);
        if (position >= 0 && position + 1 < actual.Count)
            FocusItemCell(actual[position + 1].Index, 1);
        else
            addItemButton.Focus();
    }

    private void FocusItemCell(int rowIndex, int columnIndex)
    {
        items.CurrentCell = items.Rows[rowIndex].Cells[columnIndex];
        items.BeginEdit(true);
    }

    private void QueueInvalidGridSelectionClear()
    {
        if (gridSelectionClearQueued || !items.IsHandleCreated || items.IsDisposed) return;
        gridSelectionClearQueued = true;
        items.BeginInvoke((Action)(() =>
        {
            try
            {
                var current = items.CurrentCell;
                if (current is null || (current.ColumnIndex != 5 && !IsPlaceholder(items.Rows[current.RowIndex]))) return;
                items.ClearSelection();
                items.CurrentCell = null;
            }
            finally
            {
                gridSelectionClearQueued = false;
            }
        }));
    }
    private static string Cell(DataGridViewRow row, int column) => Convert.ToString(row.Cells[column].Value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
    private static string Clean(string value) => value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
    private static DataGridViewTextBoxColumn Column(string title, int width, bool right = false, bool readOnly = false) => new()
    {
        HeaderText = title, Width = width, MinimumWidth = Math.Min(width, 60), ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };
    private static Button FixedButton(string text, int width, EventHandler handler)
    {
        var button = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(6, 2, 6, 2) };
        button.Click += handler;
        return button;
    }
    private static Label TotalLabel(bool bold) => new()
    {
        Text = "0", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Microsoft JhengHei UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular), Padding = new Padding(0, 0, 8, 0),
    };
}
