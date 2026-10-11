using System.Globalization;

namespace CYERPAutoInput;

internal sealed record CoupangOrderLine(string ItemCode, decimal Quantity, decimal Price, decimal InvoiceAmount, string Name);

internal sealed record CoupangOrder(
    int SourceRow,
    string OrderSn,
    IReadOnlyList<CoupangOrderLine> Lines,
    string Carrier,
    string TrackingNumber,
    string BuyerTaxId,
    decimal Shipping)
{
    /// <summary>應開立予買家之發票金額 summed over the rows (includes the shipping).</summary>
    public decimal InvoiceTotal => Lines.Sum(l => l.InvoiceAmount);

    /// <summary>折價券＝Σ(銷售價格×數量)＋運費−買家發票金額 (zero in the known samples).</summary>
    public decimal Discount => Lines.Sum(l => l.Price * l.Quantity) + Shipping - InvoiceTotal;
}

internal sealed record CoupangImportResult(IReadOnlyList<CoupangOrder> Orders, IReadOnlyList<string> Errors);

/// <summary>Local 酷澎 settings that place an order on COPI08 (real codes stay local, PROJECT_RULES §3).</summary>
internal sealed record CoupangMapping(
    string OrderType,
    string CustomerCode,
    string NotePrefix,
    string DiscountItemCode,
    string ShippingItemCode,
    IReadOnlyDictionary<string, string> FreightTypes);

/// <summary>
/// Parses the 酷澎 DeliveryList export (.xlsx). Rules (user, 2026-10-11): 單價＝銷售價格; 運費
/// as exported (0 when free); 應開立予酷澎之發票金額, 配送訊息 and 捆綁配送編號 are ignored; a
/// blank 統一編號 means no buyer tax ID. Only single-item orders have been seen so far; rows
/// with the same 訂單編號 are grouped and an order whose amounts do not reconcile is left out.
/// Pure: no Win32 or UI, unit tested.
/// </summary>
internal static class CoupangOrderImport
{
    public const string Source = "酷澎";
    private const string InvoiceHeader = "應開立予買家之發票金額";
    private static readonly string[] RequiredHeaders =
        ["訂單編號", "公司商品代碼", "數量", "銷售價格", "運費", InvoiceHeader, "快遞公司", "寄件單號"];

    public static CoupangImportResult Parse(IReadOnlyList<string[]> rows)
    {
        var headerIndex = -1;
        for (var i = 0; i < rows.Count && headerIndex < 0; i++)
            if (rows[i].Any(c => c.Trim() == "訂單編號") && rows[i].Any(c => c.Trim() == InvoiceHeader))
                headerIndex = i;
        if (headerIndex < 0)
            return new([], ["找不到酷澎匯出檔的表頭（訂單編號、應開立予買家之發票金額）；請選酷澎後台下載的 DeliveryList 檔。"]);

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 0; c < rows[headerIndex].Length; c++)
        {
            var name = rows[headerIndex][c].Trim().TrimStart('﻿');
            if (name.Length > 0) columns.TryAdd(name, c);
        }
        var missing = RequiredHeaders.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0) return new([], [$"酷澎匯出檔缺少欄位：{string.Join("、", missing)}。"]);

        string Cell(string[] row, string name) =>
            columns.TryGetValue(name, out var c) && c < row.Length ? row[c].Trim() : string.Empty;

        var errors = new List<string>();
        var groups = new List<(int Row, string OrderSn, List<string[]> Rows)>();
        var byOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(c => c.Trim().Length == 0)) continue;
            var orderSn = Cell(row, "訂單編號");
            if (orderSn.Length == 0) { errors.Add($"第 {r + 1} 列：沒有訂單編號。"); continue; }
            if (!byOrder.TryGetValue(orderSn, out var index))
            {
                index = groups.Count;
                byOrder[orderSn] = index;
                groups.Add((r + 1, orderSn, []));
            }
            groups[index].Rows.Add(row);
        }

        var orders = new List<CoupangOrder>();
        foreach (var (excelRow, orderSn, orderRows) in groups)
        {
            var problems = new List<string>();
            var lines = new List<CoupangOrderLine>();
            for (var i = 0; i < orderRows.Count; i++)
            {
                var row = orderRows[i];
                var number = i + 1;
                if (!TryAmount(Cell(row, "數量"), out var quantity) || quantity <= 0 || quantity != decimal.Truncate(quantity))
                { problems.Add($"第 {number} 個商品的數量「{Cell(row, "數量")}」不是正整數。"); continue; }
                if (!TryAmount(Cell(row, "銷售價格"), out var price) || price < 0)
                { problems.Add($"第 {number} 個商品的銷售價格「{Cell(row, "銷售價格")}」無法辨識。"); continue; }
                if (!TryAmount(Cell(row, InvoiceHeader), out var invoice) || invoice < 0)
                { problems.Add($"第 {number} 個商品的買家發票金額「{Cell(row, InvoiceHeader)}」無法辨識。"); continue; }
                var name = Cell(row, "顯示產品名稱") is { Length: > 0 } shown ? shown : Cell(row, "註冊商品名稱");
                lines.Add(new CoupangOrderLine(Cell(row, "公司商品代碼"), quantity, price, invoice, name));
            }

            // Multi-item layout is not known yet: shipping is taken once, from the first row
            // that has it; the reconciliation below rejects anything that does not add up.
            var shippingText = orderRows.Select(r => Cell(r, "運費")).FirstOrDefault(v => v.Length > 0) ?? "0";
            if (!TryAmount(shippingText, out var shipping) || shipping < 0)
                problems.Add($"運費「{shippingText}」無法辨識。");

            if (problems.Count == 0)
            {
                string First(string name) => orderRows.Select(r => Cell(r, name)).FirstOrDefault(v => v.Length > 0) ?? string.Empty;
                var order = new CoupangOrder(excelRow, orderSn, lines, First("快遞公司"), First("寄件單號"), First("統一編號"), shipping);
                if (order.Discount < 0)
                    problems.Add($"買家發票金額（{Number(order.InvoiceTotal)}）高於銷售價格合計加運費（{Number(order.InvoiceTotal + order.Discount)}），金額對不上。");
                else orders.Add(order);
            }
            errors.AddRange(problems.Select(p => $"第 {excelRow} 列訂單 {orderSn}：{p}"));
        }
        return new(orders, errors);
    }

    /// <summary>Converts a parsed order with the local settings into a form-ready order.</summary>
    public static ImportedOrder ToImported(CoupangOrder order, CoupangMapping mapping)
    {
        var handoff = new List<string>();
        var items = new List<ImportedItem>();
        foreach (var group in order.Lines.GroupBy(l => (l.ItemCode, l.Price)))
        {
            var code = group.Key.ItemCode;
            if (code.Length == 0) { handoff.Add($"商品「{group.First().Name}」沒有公司商品代碼"); continue; }
            if (MoOrderImport.IsCombination(code)) handoff.Add($"公司商品代碼「{code}」是組合品號");
            items.Add(new ImportedItem(code, Number(group.Sum(l => l.Quantity)), Number(group.Key.Price)));
        }
        if (order.Discount > 0) items.Add(new ImportedItem(mapping.DiscountItemCode, "1", Number(-order.Discount)));
        if (order.Shipping > 0) items.Add(new ImportedItem(mapping.ShippingItemCode, "1", Number(order.Shipping)));

        var freightType = FreightCarriers.Match(order.Carrier, mapping.FreightTypes);
        if (freightType.Length == 0) handoff.Add($"物流商「{order.Carrier}」沒有設定貨運別");

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
            Total = Number(order.InvoiceTotal)
        };
    }

    private static bool TryAmount(string text, out decimal value) =>
        decimal.TryParse(text.Replace(",", string.Empty).Replace("$", string.Empty).Trim(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
