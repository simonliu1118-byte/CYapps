namespace CYERPAutoQuery;

/// <summary>進貨單 (PURR18＋PURR22 退貨單, user 2026-10-18): the 進貨單 and 退貨單 above, the picked one's lines below, as 銷貨銷退單.</summary>
internal sealed partial class MainForm
{
    private readonly TabPage _purchasePage = new("進貨單");
    private readonly DataGridView _purchases = new();
    private readonly DataGridView _purchaseLines = new();
    private PurchaseQueryResult? _purchaseResult;

    private static readonly string[] PurchaseColumns = ["類別", "單據日期", "單號", "品項", "數量合計", "進貨金額", "稅額", "金額合計", "廠商單號", "發票號碼", "結帳"];
    private static readonly string[] PurchaseLineColumns = ["序號", "品號", "品名", "規格", "進貨數量", "驗收數量", "驗退數量", "單位", "單價", "金額", "庫別", "批號", "驗收日期", "採購單號", "備註", "原進貨單"];

    private void AddPurchasePage()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        foreach (var grid in new[] { _purchases, _purchaseLines })
        {
            ConfigureGrid(grid);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        }
        _purchases.MultiSelect = false;
        _purchases.SelectionChanged += (_, _) => ShowPurchaseLines();
        split.Panel1.Controls.Add(_purchases);
        split.Panel2.Controls.Add(_purchaseLines);
        _purchasePage.Padding = new Padding(4);
        _purchasePage.Controls.Add(split);
        _details.TabPages.Add(_purchasePage);
    }

    /// <summary>進貨單 mode: a supplier's 進貨單 in the 單據日期 range; failures shown in a message box.</summary>
    private async Task StartPurchaseQueryAsync()
    {
        PurchaseQuery query;
        try
        {
            query = PurchaseQuery.Resolve(_code.Text, Pick(_historyFrom), Pick(_historyTo), DateOnly.FromDateTime(DateTime.Today));
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, "資料檢查", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await ExecutePurchasesAsync(query, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Activate();
            MessageBox.Show(this, ex.Message, "ERP 進貨退貨單查詢失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Activate();
        _code.Focus();
        _code.SelectAll();
    }

    /// <summary>A supplier's 進貨單 (PURR18), shown; local and Web. Failures are rethrown.</summary>
    private async Task<PurchaseQueryResult> ExecutePurchasesAsync(PurchaseQuery query, CancellationToken external)
    {
        ShowPurchases(null);
        return await RunAutomationAsync(external, (progress, ct) => _service.QueryPurchasesAsync(query, progress, ct), result =>
        {
            ShowPurchases(result);
            _details.SelectedTab = _purchasePage;
            var range = $"{Invq01Layout.ErpDate(query.From)}～{Invq01Layout.ErpDate(query.To)}";
            if (!result.Found)
            {
                SetStatus($"ERP：廠商 {query.SupplierCode} 在 {range} 查無進貨單與退貨單");
                return AutomationOutcome.NotFound;
            }
            SetStatus($"ERP：廠商 {query.SupplierCode} {range} 進貨 {result.Count(PurchaseQueryResult.PurchaseKind)} 張、退貨 {result.Count(PurchaseQueryResult.ReturnKind)} 張（{result.Elapsed.TotalSeconds:0.0} 秒）");
            return AutomationOutcome.Done;
        });
    }

    private void ShowPurchases(PurchaseQueryResult? result)
    {
        _purchaseResult = result;
        SetHeaderCaptions(ErpParty.Supplier);
        _headerLabels[2].Text = "查詢範圍";
        if (_detailBox is not null) _detailBox.Text = "單頭（點選下方進貨單）";
        _itemCode.Text = result?.Query.SupplierCode ?? string.Empty;
        _itemName.Text = result?.Orders.FirstOrDefault()?.SupplierName ?? string.Empty;
        _specification.Text = result is null ? string.Empty
            : $"單據日期 {Invq01Layout.ErpDate(result.Query.From)}～{Invq01Layout.ErpDate(result.Query.To)}：進貨 {result.Count(PurchaseQueryResult.PurchaseKind)} 張、退貨 {result.Count(PurchaseQueryResult.ReturnKind)} 張";
        _fields.Rows.Clear();
        _purchases.Rows.Clear();
        _purchases.Columns.Clear();
        foreach (var c in PurchaseColumns) _purchases.Columns.Add(c, c);
        if (result is not null)
            foreach (var o in result.Orders)
                _purchases.Rows.Add(o.Kind, o.Date.ToString("yyyy/MM/dd"), o.OrderNo, o.Lines.Count, Num(o.TotalQuantity), Num(o.Amount),
                    Num(o.Tax), Num(o.Total), o.SupplierOrderNo, o.InvoiceNo, o.Settled);
        _purchasePage.Text = result is null ? "進貨單" : $"進貨單（{result.Orders.Count}）";
        ShowPurchaseLines();
    }

    /// <summary>The picked 進貨單: its 單頭 in the detail grid, its lines below the list.</summary>
    private void ShowPurchaseLines()
    {
        _purchaseLines.Rows.Clear();
        if (_purchaseLines.Columns.Count == 0)
            foreach (var c in PurchaseLineColumns) _purchaseLines.Columns.Add(c, c);
        var index = _purchases.CurrentRow?.Index ?? -1;
        if (_purchaseResult is null || index < 0 || index >= _purchaseResult.Orders.Count) return;
        var o = _purchaseResult.Orders[index];
        _fields.Rows.Clear();
        foreach (var (name, value) in new[]
        {
            ("類別", o.Kind), ("單號", o.OrderNo), ("單據日期", o.Date.ToString("yyyy/MM/dd")), ("廠商", $"{o.SupplierCode} {o.SupplierName}"),
            ("廠商單號", o.SupplierOrderNo), ("發票號碼", o.InvoiceNo), ("發票聯數", o.InvoiceType), ("課稅別", o.TaxType),
            ("幣別", o.Currency), ("數量合計", Num(o.TotalQuantity)), ($"{o.Kind}金額", Num(o.Amount)), ("扣款金額", Num(o.Deduction)),
            ("稅額", Num(o.Tax)), ("金額合計", Num(o.Total)), ("結帳", o.Settled),
        })
            if (value.Length > 0) _fields.Rows.Add(name, value);
        foreach (var l in o.Lines)
            _purchaseLines.Rows.Add(l.Seq, l.ItemCode, l.ItemName, l.Specification, Num(l.Quantity), Num(l.AcceptedQuantity),
                Num(l.ReturnedQuantity), l.Unit, Num(l.UnitPrice), Num(l.Amount), l.Warehouse, l.Batch,
                l.AcceptDate?.ToString("yyyy/MM/dd") ?? string.Empty, l.PurchaseOrderNo, l.Note, l.SourceOrder);
    }
}
