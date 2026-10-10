using System.Globalization;

namespace CYERPAutoInput;

internal sealed record MoOrderLine(string ItemCode, decimal Quantity, decimal ListPrice, decimal InvoiceAmount, string Name);

internal sealed record MoOrder(
    int SourceRow,
    string OrderSn,
    IReadOnlyList<MoOrderLine> Lines,
    string Carrier,
    string TrackingNumber,
    string StoreNote,
    string BuyerTaxId,
    string InvoiceNo,
    string InvoiceDate,
    decimal InvoiceTotal)
{
    /// <summary>Σ invoice amounts of the item rows (after every discount).</summary>
    public decimal ItemInvoiceTotal => Lines.Sum(l => l.InvoiceAmount);

    /// <summary>折價券＝Σ(商品售價×數量) − Σ開立發票金額_依品項 (user, 2026-10-10).</summary>
    public decimal Discount => Lines.Sum(l => l.ListPrice * l.Quantity) - ItemInvoiceTotal;

    /// <summary>Shipping the customer paid: invoice total minus the items' invoice amounts.</summary>
    public decimal Shipping => InvoiceTotal - ItemInvoiceTotal;
}

internal sealed record MoImportResult(IReadOnlyList<MoOrder> Orders, IReadOnlyList<string> Errors);

/// <summary>Local MO店+ settings that place an order on COPI08 (real codes stay local, PROJECT_RULES §3).</summary>
internal sealed record MoMapping(
    string OrderType,
    string CustomerCode,
    string NotePrefix,
    string DiscountItemCode,
    string ShippingItemCode,
    IReadOnlyDictionary<string, string> FreightTypes);

/// <summary>
/// Parses the MO店+ OrderExport (one row per item; an order spans rows with the same 訂單編號
/// and 序號 001, 002…; shipping is on the first row only). Pure: no Win32 or UI, unit tested.
/// An order whose amounts do not reconcile is reported and left out, never partly imported.
/// </summary>
internal static class MoOrderImport
{
    public const string Source = "MO店+";
    private const string ItemInvoiceHeader = "開立發票金額_依品項";
    private const string TotalInvoiceHeader = "開立發票金額加總";
    private static readonly string[] RequiredHeaders =
        ["訂單編號", "商品原廠編號", "數量", "商品售價", ItemInvoiceHeader, TotalInvoiceHeader, "物流商"];

    public static MoImportResult Parse(IReadOnlyList<string[]> rows)
    {
        var headerIndex = -1;
        for (var i = 0; i < rows.Count && headerIndex < 0; i++)
            if (rows[i].Any(c => c.Trim() == "訂單編號") && rows[i].Any(c => c.Trim().StartsWith(ItemInvoiceHeader, StringComparison.Ordinal)))
                headerIndex = i;
        if (headerIndex < 0)
            return new([], ["找不到 MO店+ 匯出檔的表頭（訂單編號、開立發票金額）；請直接選 MO店+ 後台下載的原始 OrderExport 檔。"]);

        // Long headers carry an explanation in parentheses, so they are matched by prefix.
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var header = rows[headerIndex];
        for (var c = 0; c < header.Length; c++)
        {
            var name = header[c].Trim();
            var paren = name.IndexOf('(');
            var key = name.StartsWith(ItemInvoiceHeader, StringComparison.Ordinal) ? ItemInvoiceHeader
                : name.StartsWith(TotalInvoiceHeader, StringComparison.Ordinal) ? TotalInvoiceHeader
                : name;
            if (key.Length > 0 && !columns.ContainsKey(key)) columns[key] = c;
            if (paren > 0 && key == name && !columns.ContainsKey(name[..paren])) columns[name[..paren]] = c;
        }
        var missing = RequiredHeaders.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0) return new([], [$"MO店+ 匯出檔缺少欄位：{string.Join("、", missing)}。"]);

        var errors = new List<string>();
        var groups = new List<(int Row, string OrderSn, List<string[]> Rows)>();
        var byOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(c => c.Trim().Length == 0)) continue;
            var orderSn = CellOf(row, columns, "訂單編號");
            if (orderSn.Length == 0)
            {
                errors.Add($"第 {r + 1} 列：沒有訂單編號。");
                continue;
            }
            if (!byOrder.TryGetValue(orderSn, out var index))
            {
                index = groups.Count;
                byOrder[orderSn] = index;
                groups.Add((r + 1, orderSn, []));
            }
            groups[index].Rows.Add(row);
        }

        var orders = new List<MoOrder>();
        foreach (var (excelRow, orderSn, orderRows) in groups)
        {
            var problems = new List<string>();
            var order = BuildOrder(excelRow, orderSn, orderRows, columns, problems);
            if (order is null || problems.Count > 0)
            {
                errors.AddRange(problems.Select(p => $"第 {excelRow} 列訂單 {orderSn}：{p}"));
                continue;
            }
            orders.Add(order);
        }
        return new(orders, errors);
    }

    private static MoOrder? BuildOrder(int excelRow, string orderSn, List<string[]> rows,
        Dictionary<string, int> columns, List<string> problems)
    {
        string Cell(string[] row, string name) => CellOf(row, columns, name);
        string First(string name) => rows.Select(r => Cell(r, name)).FirstOrDefault(v => v.Length > 0) ?? string.Empty;

        var lines = new List<MoOrderLine>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var number = i + 1;
            if (!TryAmount(Cell(row, "數量"), out var quantity) || quantity <= 0 || quantity != decimal.Truncate(quantity))
            { problems.Add($"第 {number} 個商品的數量「{Cell(row, "數量")}」不是正整數。"); continue; }
            if (!TryAmount(Cell(row, "商品售價"), out var price) || price < 0)
            { problems.Add($"第 {number} 個商品的商品售價「{Cell(row, "商品售價")}」無法辨識。"); continue; }
            if (!TryAmount(Cell(row, ItemInvoiceHeader), out var invoiceAmount) || invoiceAmount < 0)
            { problems.Add($"第 {number} 個商品的開立發票金額「{Cell(row, ItemInvoiceHeader)}」無法辨識。"); continue; }
            var name = Cell(row, "規格1") is { Length: > 0 } spec and not "無" ? spec : Cell(row, "商品名稱");
            lines.Add(new MoOrderLine(Cell(row, "商品原廠編號"), quantity, price, invoiceAmount, name));
        }
        if (problems.Count > 0) return null;

        var totals = rows.Select(r => Cell(r, TotalInvoiceHeader)).Where(v => v.Length > 0).Distinct().ToList();
        if (totals.Count != 1 || !TryAmount(totals[0], out var invoiceTotal))
        {
            problems.Add("開立發票金額加總缺少或各列不一致。");
            return null;
        }

        var order = new MoOrder(excelRow, orderSn, lines, First("物流商"), First("物流單號"), First("備註(店家自行備註)"),
            First("發票開立統編"), First("發票號碼"), NormalizeInvoiceDate(First("發票日期")), invoiceTotal);

        if (order.Discount < 0)
            problems.Add($"開立發票金額（{Money(order.ItemInvoiceTotal)}）高於商品售價合計，無法算出折價券金額。");
        if (order.Shipping < 0)
            problems.Add($"開立發票金額加總（{Money(invoiceTotal)}）小於各品項開立發票金額合計（{Money(order.ItemInvoiceTotal)}）。");

        // Cross-check: the shipping implied by the invoice amounts must match MO店+'s own
        // shipping columns (customer fee + platform subsidy + free-shipping discount).
        var shippingParts = new[] { "客人支付運費", "平台補貼運費", "商品滿額免運費" };
        if (shippingParts.All(columns.ContainsKey))
        {
            var stated = 0m;
            foreach (var part in shippingParts)
                if (TryAmount(First(part), out var value)) stated += value;
            if (stated != order.Shipping)
                problems.Add($"運費對不上：開立發票金額推算為 {Money(order.Shipping)}，運費欄位合計為 {Money(stated)}。");
        }
        return order;
    }

    /// <summary>Converts a parsed order with the local settings into a form-ready order.</summary>
    public static ImportedOrder ToImported(MoOrder order, MoMapping mapping)
    {
        var handoff = new List<string>();
        if (order.StoreNote.Length > 0) handoff.Add($"店家備註：{order.StoreNote}");

        var items = new List<ImportedItem>();
        foreach (var group in order.Lines.GroupBy(l => (l.ItemCode, l.ListPrice)))
        {
            var code = group.Key.ItemCode;
            if (code.Length == 0) { handoff.Add($"商品「{group.First().Name}」沒有商品原廠編號"); continue; }
            if (IsCombination(code)) handoff.Add($"商品原廠編號「{code}」是組合品號");
            items.Add(new ImportedItem(code, Number(group.Sum(l => l.Quantity)), Number(group.Key.ListPrice)));
        }
        if (order.Discount > 0) items.Add(new ImportedItem(mapping.DiscountItemCode, "1", Number(-order.Discount)));
        if (order.Shipping > 0) items.Add(new ImportedItem(mapping.ShippingItemCode, "1", Number(order.Shipping)));

        var freightType = MatchFreightType(order.Carrier, mapping.FreightTypes);
        if (freightType.Length == 0) handoff.Add($"物流商「{order.Carrier}」不在貨運別對照表");

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_type"] = mapping.OrderType,
            ["customer_code"] = mapping.CustomerCode,
            ["trade_note"] = mapping.NotePrefix + order.OrderSn,
            ["cod"] = Number(order.InvoiceTotal)
        };
        if (freightType.Length > 0) header["freight_type"] = freightType;
        if (order.Shipping > 0) header["freight_fee"] = Number(order.Shipping);

        return new ImportedOrder
        {
            Source = Source,
            OrderSn = order.OrderSn,
            Header = header,
            Items = items,
            HandoffReason = string.Join("；", handoff),
            Carrier = order.Carrier,
            TrackingNumber = order.TrackingNumber,
            InvoiceEditable = true,
            BuyerTaxId = order.BuyerTaxId,
            InvoiceDate = order.InvoiceDate,
            InvoiceNo = order.InvoiceNo,
            Total = Number(order.InvoiceTotal)
        };
    }

    /// <summary>"A + B" style codes name a set of items; they go to a person for now.</summary>
    internal static bool IsCombination(string code) => code.IndexOfAny(['+', '＋']) >= 0;

    /// <summary>The 貨運別 whose keyword appears in the carrier text; the longest keyword wins.</summary>
    internal static string MatchFreightType(string carrier, IReadOnlyDictionary<string, string> table) =>
        table.Where(p => p.Key.Trim().Length > 0 && p.Value.Trim().Length > 0 &&
                         carrier.Contains(p.Key.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Key.Trim().Length)
            .Select(p => p.Value.Trim())
            .FirstOrDefault() ?? string.Empty;

    /// <summary>Parses "關鍵字=貨運別" lines (settings text box) into the carrier table.</summary>
    internal static Dictionary<string, string> ParseFreightTable(string text)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var parts = line.Split(['=', '＝'], 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.Length > 0 && value.Length > 0) table[key] = value;
        }
        return table;
    }

    internal static string FormatFreightTable(IReadOnlyDictionary<string, string> table) =>
        string.Join(Environment.NewLine, table.Select(p => $"{p.Key}={p.Value}"));

    private static string CellOf(string[] row, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : string.Empty;

    private static bool TryAmount(string text, out decimal value) =>
        decimal.TryParse(text.Replace(",", string.Empty).Replace("$", string.Empty).Trim(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => Number(value);

    /// <summary>Keeps a text date as is; an Excel serial date number becomes YYYY/MM/DD.</summary>
    private static string NormalizeInvoiceDate(string value)
    {
        if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial) &&
            serial is > 20000 and < 80000)
            return DateTime.FromOADate(serial).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        return value;
    }
}
