using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class InvoiceEntryControl : UserControl
{
    private const int MinimumVisibleRows = 5;
    private const int PreferredImportsHeight = 82;
    private const int MinimumImportsHeight = 72;
    private const int PreferredBuyerHeight = 116;
    private const int MinimumBuyerHeight = 116;
    private const int PreferredActionsHeight = 52;
    private const int MinimumActionsHeight = 52;
    private const int MaximumDefaultFlexibleGap = 80;
    private const int SummaryOuterTopPadding = 10;
    private const int SummaryGroupChromeHeight = 34;
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
    private readonly Button issueButton = UiControls.PrimaryIssueButton();
    private readonly Button addItemButton = UiControls.StandardButton("＋ 新增明細");
    private readonly Button clearButton = UiControls.StandardButton("清空");
    private readonly Button previewButton = UiControls.StandardButton("預覽");
    private int hotDeleteRow = -1;
    private int pressedDeleteRow = -1;
    private NameLookup? cachedLookup;
    private string cachedBan = string.Empty;
    private TextBox? cellEditor;
    private int editorRow = -1;
    private int editorColumn = -1;
    private bool committingEditor;
    private TableLayoutPanel? rootLayout;
    private TableLayoutPanel? itemsLayout;
    private TableLayoutPanel? remarkLayout;
    private GroupBox? itemsGroup;
    private GroupBox? remarkGroup;
    private GroupBox? totalGroup;
    private TableLayoutPanel? totalsLayout;
    private Panel? totalsSeparator;

    private ListView Items => itemsHost.List;

    public InvoiceEntryControl(LocalRepository repository, InvoiceService service, Action recordsChanged)
    {
        this.repository = repository;
        this.service = service;
        this.recordsChanged = recordsChanged;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(18, 4, 18, 4);
        Font = new Font("Microsoft JhengHei UI", 12F);
        BuildLayout();
        ConfigureEvents();
        ResetDraft();
        HandleCreated += (_, _) => BeginInvoke((Action)ApplyMeasuredLayout);
    }

    private void BuildLayout()
    {
        rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreferredImportsHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreferredBuyerHeight));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, SummaryPanelHeight()));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreferredActionsHeight));
        rootLayout.Controls.Add(BuildImports(), 0, 0);
        rootLayout.Controls.Add(BuildBuyer(), 0, 1);
        rootLayout.Controls.Add(BuildItems(), 0, 2);
        rootLayout.Controls.Add(BuildSummary(), 0, 3);
        rootLayout.Controls.Add(BuildActions(), 0, 5);
        Controls.Add(rootLayout);
    }

    private Control BuildImports()
    {
        var group = new GroupBox { Text = "Excel 匯入開立", Dock = DockStyle.Fill, Padding = new Padding(12, 7, 12, 8) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(8, 4, 0, 0) };
        var digiwin = UiControls.ImportButton("鼎新ERP", ImportBrand.Digiwin);
        var moShop = UiControls.ImportButton("MO店+", ImportBrand.MoShop);
        var coupang = UiControls.ImportButton("酷澎商城", ImportBrand.Coupang);
        digiwin.Click += (_, _) => Pending("鼎新 ERP 匯入");
        moShop.Click += async (_, _) => await ImportMoAsync();
        coupang.Click += async (_, _) => await ImportCoupangAsync();
        buttons.Controls.Add(digiwin);
        buttons.Controls.Add(moShop);
        buttons.Controls.Add(coupang);
        group.Controls.Add(buttons);
        return group;
    }

    private Control BuildBuyer()
    {
        var group = new GroupBox { Text = "發票基本資料", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        automaticOrder.Font = customOrder.Font = consumerBuyer.Font = companyBuyer.Font = Font;
        var firstModeWidth = Math.Max(
            automaticOrder.GetPreferredSize(Size.Empty).Width,
            consumerBuyer.GetPreferredSize(Size.Empty).Width) + 8;
        var secondModeWidth = Math.Max(
            customOrder.GetPreferredSize(Size.Empty).Width,
            companyBuyer.GetPreferredSize(Size.Empty).Width) + 8;

        var orderLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        orderLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, firstModeWidth));
        orderLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, secondModeWidth));
        orderLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        automaticOrder.Margin = customOrder.Margin = new Padding(3);
        orderLine.Controls.Add(automaticOrder, 0, 0);
        orderLine.Controls.Add(customOrder, 1, 0);
        orderLine.Controls.Add(orderId, 2, 0);

        var banLabelWidth = TextRenderer.MeasureText("統一編號", Font).Width + 12;
        var buyerNameLabelWidth = TextRenderer.MeasureText("買方名稱", Font).Width + 12;
        var buyerLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, firstModeWidth));
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, secondModeWidth));
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, banLabelWidth));
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buyerNameLabelWidth));
        buyerLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        consumerBuyer.Margin = companyBuyer.Margin = new Padding(3);
        buyerLine.Controls.Add(consumerBuyer, 0, 0);
        buyerLine.Controls.Add(companyBuyer, 1, 0);
        buyerLine.Controls.Add(UiControls.Label("統一編號"), 2, 0);
        buyerLine.Controls.Add(buyerBan, 3, 0);
        buyerLine.Controls.Add(UiControls.Label("買方名稱"), 4, 0);
        buyerLine.Controls.Add(buyerName, 5, 0);

        layout.Controls.Add(UiControls.Label("訂單編號"), 0, 0);
        layout.Controls.Add(orderLine, 1, 0);
        layout.Controls.Add(UiControls.Label("買方資料"), 0, 1);
        layout.Controls.Add(buyerLine, 1, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildItems()
    {
        itemsGroup = new GroupBox { Text = "商品明細資料（最多 50 筆）", Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
        itemsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        itemsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        itemsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        var modes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 0, 0, 8) };
        addItemButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addItemButton.Margin = new Padding(2, 2, 4, 4);
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
        Items.Columns.Add("課稅別", TaxColumnWidth(), HorizontalAlignment.Left);
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
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, SummaryOuterTopPadding, 0, 0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        remarkGroup = new GroupBox { Text = "發票總備註", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        remarkLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1 };
        remarkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, RemarkInputHeight()));
        remarkLayout.Controls.Add(remark, 0, 0);
        remarkCounter.Dock = DockStyle.None;
        remarkCounter.AutoSize = false;
        remarkCounter.Width = 92;
        remarkCounter.Height = 22;
        remarkCounter.BackColor = Color.White;
        void PositionRemarkCounter() => remarkCounter.SetBounds(
            Math.Max(0, remarkGroup.ClientSize.Width - remarkCounter.Width - 10),
            0,
            remarkCounter.Width,
            remarkCounter.Height);
        remarkGroup.Resize += (_, _) => PositionRemarkCounter();
        remarkGroup.Controls.Add(remarkLayout);
        remarkGroup.Controls.Add(remarkCounter);
        PositionRemarkCounter();
        remarkCounter.BringToFront();

        totalGroup = new GroupBox { Text = "金額總計", Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        totalsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
        totalsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        totalsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        totalsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, TotalRowHeight()));
        totalsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, TotalRowHeight()));
        totalsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        totalsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, TotalRowHeight()));
        AddTotal(totalsLayout, 0, "應稅銷售額", salesTotal);
        AddTotal(totalsLayout, 1, "營業稅額（5%）", taxTotal);
        totalsSeparator = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(185, 185, 185), Margin = new Padding(3, 0, 3, 0) };
        totalsLayout.Controls.Add(totalsSeparator, 0, 2);
        totalsLayout.SetColumnSpan(totalsSeparator, 2);
        AddTotal(totalsLayout, 3, "發票總額", invoiceTotal);
        totalGroup.Controls.Add(totalsLayout);
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
        var actions = new Panel { Dock = DockStyle.Fill };
        clearButton.Margin = Padding.Empty;
        previewButton.Margin = Padding.Empty;
        issueButton.Margin = Padding.Empty;
        clearButton.Click += (_, _) => ResetDraft();
        previewButton.Click += (_, _) => Preview();
        issueButton.Click += async (_, _) => await IssueAsync();
        actions.Controls.Add(clearButton);
        actions.Controls.Add(issueButton);
        actions.Controls.Add(previewButton);

        void PositionActions()
        {
            const int gap = 12;
            var contentWidth = clearButton.Width + issueButton.Width + previewButton.Width + (gap * 2);
            var left = Math.Max(0, (actions.ClientSize.Width - contentWidth) / 2);
            clearButton.SetBounds(left, Math.Max(0, (actions.ClientSize.Height - clearButton.Height) / 2), clearButton.Width, clearButton.Height);
            issueButton.SetBounds(clearButton.Right + gap, Math.Max(0, (actions.ClientSize.Height - issueButton.Height) / 2), issueButton.Width, issueButton.Height);
            previewButton.SetBounds(issueButton.Right + gap, Math.Max(0, (actions.ClientSize.Height - previewButton.Height) / 2), previewButton.Width, previewButton.Height);
        }

        actions.Resize += (_, _) => PositionActions();
        PositionActions();
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
        Items.MouseMove += (_, eventArgs) => UpdateDeleteHotState(DeleteButtonRowAt(eventArgs.Location));
        Items.MouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left) UpdateDeletePressedState(DeleteButtonRowAt(eventArgs.Location));
        };
        Items.MouseUp += (_, eventArgs) => HandleItemMouseUp(eventArgs);
        Items.MouseLeave += (_, _) =>
        {
            UpdateDeleteHotState(-1);
            UpdateDeletePressedState(-1);
        };
        Items.MouseCaptureChanged += (_, _) =>
        {
            if (Control.MouseButtons == MouseButtons.None) UpdateDeletePressedState(-1);
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

    private void HandleItemMouseUp(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left) return;
        var pressedRow = pressedDeleteRow;
        UpdateDeletePressedState(-1);
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
            if (pressedRow == row) DeleteRow(row);
            else QueueClearItemSelection();
            return;
        }
        if (column is 1 or 3 or 4)
        {
            BeginCellEdit(row, column);
            return;
        }
        CommitCellEditor(false);
        QueueClearItemSelection();
    }

    private int DeleteButtonRowAt(Point location)
    {
        var hit = Items.HitTest(location);
        if (hit.Item is null || IsPlaceholder(hit.Item) || hit.SubItem is null ||
            hit.Item.SubItems.IndexOf(hit.SubItem) != 6 || !DeleteButtonBounds(hit.Item).Contains(location))
            return -1;
        return hit.Item.Index;
    }

    private void UpdateDeleteHotState(int row)
    {
        if (hotDeleteRow == row) return;
        var previous = hotDeleteRow;
        hotDeleteRow = row;
        InvalidateDeleteRow(previous);
        InvalidateDeleteRow(row);
    }

    private void UpdateDeletePressedState(int row)
    {
        if (pressedDeleteRow == row) return;
        var previous = pressedDeleteRow;
        pressedDeleteRow = row;
        InvalidateDeleteRow(previous);
        InvalidateDeleteRow(row);
    }

    private void InvalidateDeleteRow(int row)
    {
        if (row >= 0 && row < Items.Items.Count) Items.Invalidate(Items.Items[row].Bounds);
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
        UiControls.SetTextBoxLocked(orderId, automaticOrder.Checked);
        if (automaticOrder.Checked) orderId.Text = ManualOrderId.Next(DateTimeOffset.Now, repository.Invoices.LoadOrCreate());
    }

    private void UpdateBuyerMode()
    {
        UiControls.SetTextBoxLocked(buyerBan, !companyBuyer.Checked);
        UiControls.SetTextBoxLocked(buyerName, !companyBuyer.Checked);
        taxExclusive.Enabled = companyBuyer.Checked;
        if (!companyBuyer.Checked) { buyerBan.Clear(); buyerName.Clear(); taxInclusive.Checked = true; }
    }

    private void UpdateHeaders()
    {
        CommitCellEditor(false);
        var mode = taxExclusive.Checked ? "未稅" : "含稅";
        Items.Columns[4].Text = $"單價（{mode}）";
        Items.Columns[5].Text = $"金額（{mode}）";
        var production = repository.Settings.LoadOrCreate().Environment == Environments.Production;
        issueButton.Text = production ? "開立正式發票" : "開立測試發票";
        UiControls.ApplyIssueButtonTheme(issueButton, production);
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
            UiControls.SetTextBoxLocked(buyerBan, true);
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
        finally { UiControls.SetTextBoxLocked(buyerBan, !companyBuyer.Checked); }
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
    private static TableLayoutPanel RadioGroup(RadioButton first, RadioButton second, int firstWidth)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 3, 0, 0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, firstWidth));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        first.Margin = new Padding(3, 3, 3, 3);
        second.Margin = new Padding(3, 3, 3, 3);
        panel.Controls.Add(first, 0, 0);
        panel.Controls.Add(second, 1, 0);
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
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Items.Font,
            MaxLength = columnIndex == 1 ? 256 : 64,
            TextAlign = columnIndex is 3 or 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Bounds = Rectangle.Inflate(bounds, -1, -1),
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

    private static Color ItemRowBackground(int rowIndex) =>
        rowIndex % 2 == 0 ? Color.White : Color.FromArgb(238, 244, 250);

    private static Color ReadOnlyItemBackground(int rowIndex) =>
        rowIndex % 2 == 0 ? Color.FromArgb(244, 244, 244) : Color.FromArgb(235, 240, 245);

    private void StyleItemRows()
    {
        for (var rowIndex = 0; rowIndex < Items.Items.Count; rowIndex++)
        {
            var row = Items.Items[rowIndex];
            row.UseItemStyleForSubItems = false;
            var zebra = ItemRowBackground(rowIndex);
            foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
            {
                subItem.BackColor = zebra;
                subItem.ForeColor = SystemColors.ControlText;
            }
            var readOnlyBackground = ReadOnlyItemBackground(rowIndex);
            row.SubItems[0].BackColor = readOnlyBackground;
            row.SubItems[5].BackColor = readOnlyBackground;
            row.SubItems[5].ForeColor = Color.FromArgb(88, 88, 88);
        }
        Items.Invalidate();
    }

    private void LayoutItemColumns()
    {
        if (Items.Columns.Count != 7 || Items.ClientSize.Width <= 0) return;
        var available = itemsHost.ColumnViewportWidth;
        var fixedWidths = new[] { 56, TaxColumnWidth(), 88, 150, 155, 86 };
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
        var background = columnIndex is 0 or 5
            ? ReadOnlyItemBackground(rowIndex)
            : ItemRowBackground(rowIndex);
        using (var brush = new SolidBrush(background)) eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);

        if (columnIndex == 6 && !IsPlaceholder(eventArgs.Item))
        {
            var button = DeleteButtonBounds(eventArgs.Item);
            var state = pressedDeleteRow == rowIndex
                ? System.Windows.Forms.VisualStyles.PushButtonState.Pressed
                : hotDeleteRow == rowIndex
                    ? System.Windows.Forms.VisualStyles.PushButtonState.Hot
                    : System.Windows.Forms.VisualStyles.PushButtonState.Normal;
            ButtonRenderer.DrawButton(eventArgs.Graphics, button, state);
            TextRenderer.DrawText(eventArgs.Graphics, "刪除", Items.Font, button, Color.FromArgb(190, 24, 24),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            flags |= columnIndex is 3 or 4 or 5 ? TextFormatFlags.Right :
                columnIndex == 0 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
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
        if (!consumerBuyer.Checked || !buyerBan.Enabled || !buyerName.Enabled || !buyerBan.ReadOnly || !buyerName.ReadOnly ||
            buyerBan.TabStop || buyerName.TabStop)
            throw new InvalidOperationException("一般消費者模式未以一致灰底唯讀方式鎖定統編與買方名稱");
        companyBuyer.Checked = true;
        if (buyerBan.ReadOnly || buyerName.ReadOnly || !buyerBan.TabStop || !buyerName.TabStop)
            throw new InvalidOperationException("公司統編模式未啟用必要買方欄位");
        var buyerNameLabel = Descendants(this).OfType<Label>().FirstOrDefault(label => label.Text == "買方名稱");
        var buyerBanLabel = Descendants(this).OfType<Label>().FirstOrDefault(label => label.Text == "統一編號");
        var buyerLine = consumerBuyer.Parent;
        if (buyerNameLabel is null || buyerBanLabel is null || buyerLine is null ||
            !ReferenceEquals(companyBuyer.Parent, buyerLine) ||
            !ReferenceEquals(buyerBan.Parent, buyerLine) ||
            !ReferenceEquals(buyerName.Parent, buyerLine) ||
            consumerBuyer.Width < consumerBuyer.GetPreferredSize(Size.Empty).Width ||
            companyBuyer.Width < companyBuyer.GetPreferredSize(Size.Empty).Width ||
            buyerBan.Bottom > buyerLine.ClientSize.Height ||
            buyerName.Bottom > buyerLine.ClientSize.Height ||
            buyerBan.Height < buyerBan.PreferredHeight ||
            buyerName.Height < buyerName.PreferredHeight)
            throw new InvalidOperationException("買方資料未排成單列，或統編／買方名稱輸入欄位遭裁切");
        consumerBuyer.Checked = true;
        if (Items.Columns.Count != 7 || Items.Items.Count != MinimumVisibleRows || ActualRows().Count != 1)
            throw new InvalidOperationException("商品原生 ListView 未建立一筆實際資料與五列顯示區");
        if (!itemsHost.UsesOnlyNativeScrollBar) throw new InvalidOperationException("商品清單仍含額外 scrollbar 控制項");
        var importButtons = Descendants(this).OfType<ImportBrandButton>().ToArray();
        if (importButtons.Length != 3 ||
            importButtons.Any(button => !UiControls.HasLogicalSize(button, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight)))
            throw new InvalidOperationException("三個 Excel 平台按鈕未使用一致的品牌按鈕尺寸");
        if (Math.Abs(Items.Font.SizeInPoints - 12F) > 0.1F) throw new InvalidOperationException("商品清單未使用 12pt 字級");
        if (Items.Items[0].SubItems[0].BackColor != ReadOnlyItemBackground(0) ||
            Items.Items[0].SubItems[5].BackColor != ReadOnlyItemBackground(0) ||
            Items.Items[1].SubItems[1].BackColor != ItemRowBackground(1) ||
            Items.Items[1].SubItems[2].BackColor != ItemRowBackground(1))
            throw new InvalidOperationException("商品清單的唯讀灰底或淡藍斑馬紋配置不正確");
        var initialColumnWidth = Items.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
        if (initialColumnWidth > itemsHost.ColumnViewportWidth ||
            itemsHost.ColumnViewportWidth - initialColumnWidth > 3 ||
            itemsHost.HorizontalScrollVisible || Items.GridLines)
            throw new InvalidOperationException("商品清單欄寬、水平 scrollbar 或格線繪製方式不正確");
        if (itemsHost.VisibleRowCapacity() != MinimumVisibleRows)
            throw new InvalidOperationException($"商品清單可視列數不是五列：{itemsHost.VisibleRowCapacity()}");
        var rowHeights = rootLayout?.GetRowHeights() ?? [];
        var totalRowHeight = rowHeights.Sum();
        var lineHeight = (int)Math.Ceiling(remark.Font.GetHeight());
        if (rowHeights.Length != 6 || Math.Abs(rowHeights[3] - MeasuredSummaryPanelHeight()) > 2 ||
            rowHeights[0] < MinimumImportsHeight || rowHeights[1] < MinimumBuyerHeight ||
            rowHeights[4] > MaximumDefaultFlexibleGap || rowHeights[5] < MinimumActionsHeight ||
            remark.ClientSize.Height < lineHeight * 3 || remark.ClientSize.Height > lineHeight * 3 + 12 ||
            totalRowHeight > ClientSize.Height)
            throw new InvalidOperationException(
                $"主畫面配置不正確：列高 {string.Join(",", rowHeights)}，備註 {remark.ClientSize.Height}px，行高 {lineHeight}px");
        Items.Focus();
        var editBounds = Items.Items[0].SubItems[1].Bounds;
        HandleItemMouseUp(new MouseEventArgs(MouseButtons.Left, 1, editBounds.Left + 4, editBounds.Top + (editBounds.Height / 2), 0));
        Application.DoEvents();
        if (cellEditor is null || cellEditor.IsDisposed || !cellEditor.ContainsFocus ||
            cellEditor.Bounds.Top < editBounds.Top || cellEditor.Bounds.Bottom > editBounds.Bottom)
            throw new InvalidOperationException("商品儲存格在滑鼠放開後未維持焦點或輸入框高度未貼合資料列");
        if (!UiControls.HasLogicalSize(addItemButton, UiControls.StandardButtonWidth, UiControls.StandardButtonHeight) ||
            !UiControls.HasLogicalSize(issueButton, 190, 46) || issueButton is not PrimaryActionButton)
            throw new InvalidOperationException("商品新增或主開立按鈕尺寸／樣式不正確");
        var actionCenter = issueButton.Top + (issueButton.Height / 2);
        if (Math.Abs(actionCenter - (clearButton.Top + clearButton.Height / 2)) > 1 ||
            Math.Abs(actionCenter - (previewButton.Top + previewButton.Height / 2)) > 1)
            throw new InvalidOperationException("主畫面底部三個按鈕未垂直置中對齊");
        if (remarkGroup is null || remarkCounter.Parent != remarkGroup || remarkCounter.Top != 0 ||
            remarkCounter.Right > remarkGroup.ClientSize.Width ||
            remark.ClientSize.Height < lineHeight * 3 || remark.Bottom > remarkLayout!.ClientSize.Height)
            throw new InvalidOperationException("備註標題、字數位置或三列輸入高度不正確");
        if (totalGroup is null || totalsLayout is null || totalsSeparator is null ||
            totalsLayout.GetPositionFromControl(totalsSeparator).Row != 2 ||
            totalsLayout.GetPositionFromControl(invoiceTotal).Row != 3 ||
            totalsSeparator.Height < 1 ||
            totalsLayout.GetRowHeights().Sum() > totalsLayout.ClientSize.Height ||
            totalsLayout.Controls.OfType<Label>().Any(label => label.Height < label.PreferredHeight))
            throw new InvalidOperationException("金額總計文字、列高或稅額分隔線配置不正確");
        UpdateDeleteHotState(0);
        UpdateDeletePressedState(0);
        if (hotDeleteRow != 0 || pressedDeleteRow != 0)
            throw new InvalidOperationException("商品刪除按鈕未建立滑過與按下狀態");
        UpdateDeleteHotState(-1);
        UpdateDeletePressedState(-1);
        CommitCellEditor(true);
        while (ActualRows().Count <= MinimumVisibleRows) AddRow(false);
        EnsurePlaceholderRows();
        Application.DoEvents();
        LayoutItemColumns();
        Application.DoEvents();
        var scrolledColumnWidth = Items.Columns.Cast<ColumnHeader>().Sum(column => column.Width);
        if (!itemsHost.NativeScrollNeeded || itemsHost.HorizontalScrollVisible ||
            scrolledColumnWidth > itemsHost.ColumnViewportWidth ||
            itemsHost.ColumnViewportWidth - scrolledColumnWidth > 3)
            throw new InvalidOperationException("商品清單出現垂直 scrollbar 後未重新計算欄寬，或產生水平 scrollbar");
    }

    private void ApplyMeasuredLayout()
    {
        if (rootLayout is null || itemsLayout is null || itemsGroup is null || remarkLayout is null || IsDisposed) return;
        var listHeight = itemsHost.HeightForRows(MinimumVisibleRows);
        itemsLayout.RowStyles[1].SizeType = SizeType.Absolute;
        itemsLayout.RowStyles[1].Height = listHeight;
        var chrome = Math.Max(18, itemsGroup.Height - itemsGroup.DisplayRectangle.Height);
        rootLayout.RowStyles[2].Height = itemsLayout.RowStyles[0].Height + listHeight + chrome + itemsGroup.Margin.Vertical;
        remarkLayout.RowStyles[0].SizeType = SizeType.Absolute;
        remarkLayout.RowStyles[0].Height = RemarkInputHeight();
        rootLayout.RowStyles[3].SizeType = SizeType.Absolute;
        rootLayout.RowStyles[3].Height = MeasuredSummaryPanelHeight();
        FitSectionRowsToClient();
        PerformLayout();
    }

    private void FitSectionRowsToClient()
    {
        if (rootLayout is null) return;
        var importsHeight = PreferredImportsHeight;
        var buyerHeight = PreferredBuyerHeight;
        var actionsHeight = PreferredActionsHeight;
        var availableHeight = Math.Max(0, ClientSize.Height - Padding.Vertical);
        var fixedHeight = importsHeight + buyerHeight + actionsHeight +
            (int)Math.Ceiling(rootLayout.RowStyles[2].Height) +
            (int)Math.Ceiling(rootLayout.RowStyles[3].Height);
        var overflow = Math.Max(0, fixedHeight - availableHeight);
        buyerHeight = ReduceHeight(buyerHeight, MinimumBuyerHeight, ref overflow);
        actionsHeight = ReduceHeight(actionsHeight, MinimumActionsHeight, ref overflow);
        importsHeight = ReduceHeight(importsHeight, MinimumImportsHeight, ref overflow);
        rootLayout.RowStyles[0].Height = importsHeight;
        rootLayout.RowStyles[1].Height = buyerHeight;
        rootLayout.RowStyles[5].Height = actionsHeight;
    }

    private static int ReduceHeight(int current, int minimum, ref int overflow)
    {
        var reduction = Math.Min(overflow, current - minimum);
        overflow -= reduction;
        return current - reduction;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private int TextLineHeight() => (int)Math.Ceiling(remark.Font.GetHeight());
    private int RemarkInputHeight() => (TextLineHeight() * 3) + 10;
    private int TotalRowHeight() => Math.Max(TextLineHeight() + 6, salesTotal.GetPreferredSize(Size.Empty).Height + 6);
    private int TaxColumnWidth() => Math.Max(62, TextRenderer.MeasureText("課稅別", Items.Font).Width + 14);
    private int SummaryPanelHeight() => SummaryOuterTopPadding + 12 +
        Math.Max(RemarkInputHeight() + SummaryGroupChromeHeight, (TotalRowHeight() * 3) + 1 + SummaryGroupChromeHeight);

    private int MeasuredSummaryPanelHeight()
    {
        var remarkChrome = remarkGroup is null
            ? SummaryGroupChromeHeight
            : Math.Max(SummaryGroupChromeHeight, remarkGroup.Height - remarkGroup.DisplayRectangle.Height);
        var totalChrome = totalGroup is null
            ? SummaryGroupChromeHeight
            : Math.Max(SummaryGroupChromeHeight, totalGroup.Height - totalGroup.DisplayRectangle.Height);
        return SummaryOuterTopPadding + 12 +
            Math.Max(RemarkInputHeight() + remarkChrome, (TotalRowHeight() * 3) + 1 + totalChrome);
    }

    private static string Cell(ListViewItem row, int column) => row.SubItems[column].Text.Trim();
    private static string Clean(string value) => value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
    private static Label TotalLabel(bool bold) => new()
    {
        Text = "0", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Microsoft JhengHei UI", 12F, bold ? FontStyle.Bold : FontStyle.Regular), Padding = new Padding(0, 0, 8, 0),
    };
}
