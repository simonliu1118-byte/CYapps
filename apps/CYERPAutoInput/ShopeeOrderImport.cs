using System.Globalization;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

internal sealed record ShopeeOrderItem(string ItemCode, string Quantity, string UnitPrice, string Name);

internal sealed record ShopeeOrder(int SourceRow, string OrderSn, string TrackingNumber, IReadOnlyList<ShopeeOrderItem> Items,
    string BuyerRemark = "", string SellerNote = "")
{
    /// <summary>
    /// Orders with a buyer remark or seller note are entered up to the header only and
    /// handed to a person before any item (user, 2026-10-10). Empty when none.
    /// </summary>
    public string HandoffReason =>
        BuyerRemark.Length == 0 && SellerNote.Length == 0 ? string.Empty
        : string.Join("；", new[]
        {
            BuyerRemark.Length > 0 ? $"買家備註：{BuyerRemark}" : null,
            SellerNote.Length > 0 ? $"賣家備註：{SellerNote}" : null
        }.Where(x => x is not null));
}

internal sealed record ShopeeImportResult(IReadOnlyList<ShopeeOrder> Orders, IReadOnlyList<string> Errors);

/// <summary>Where the local Shopee settings put an order on the COPI08 form.</summary>
internal sealed record ShopeeMapping(string OrderType, string CustomerCode, string NotePrefix);

/// <summary>
/// Parses the official Shopee order export (one order per row; all items of an order in
/// product_info as "[1] 商品名稱:…; 商品選項名稱:…; 價格: $ 350; 數量: 1; 商品選項貨號: C00415; [2] …").
/// Pure: no Win32 or UI, unit tested. An order with any unusable item is reported as an
/// error and left out, never partly imported.
/// </summary>
internal static partial class ShopeeOrderImport
{
    private static readonly string[] RequiredHeaders = ["tracking_number", "order_sn", "product_info"];

    public static ShopeeImportResult Parse(IReadOnlyList<string[]> rows)
    {
        var errors = new List<string>();
        var orders = new List<ShopeeOrder>();
        var headerRow = rows.Select((r, i) => (Row: r, Index: i)).FirstOrDefault(x => x.Row.Any(c => c.Trim() == "order_sn"));
        if (headerRow.Row is null)
            return new([], ["找不到蝦皮匯出檔的表頭（order_sn）；請確認是蝦皮官方匯出的訂單檔。"]);

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerRow.Row.Length; i++)
        {
            var name = headerRow.Row[i].Trim();
            if (name.Length > 0 && !columns.ContainsKey(name)) columns[name] = i;
        }
        var missing = RequiredHeaders.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0)
            return new([], [$"蝦皮匯出檔缺少欄位：{string.Join("、", missing)}。"]);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var r = headerRow.Index + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(string header) => columns.TryGetValue(header, out var c) && c < row.Length ? row[c].Trim() : string.Empty;
            var excelRow = r + 1;
            var orderSn = Cell("order_sn");
            var tracking = Cell("tracking_number");
            var productInfo = Cell("product_info");
            if (orderSn.Length == 0 && tracking.Length == 0 && productInfo.Length == 0) continue;

            if (orderSn.Length == 0) { errors.Add($"第 {excelRow} 列：沒有訂單編號（order_sn）。"); continue; }
            if (!seen.Add(orderSn)) { errors.Add($"第 {excelRow} 列：訂單 {orderSn} 重複出現，只匯入第一筆。"); continue; }

            var items = ParseProductInfo(productInfo, out var itemErrors);
            if (itemErrors.Count > 0)
            {
                errors.AddRange(itemErrors.Select(e => $"第 {excelRow} 列訂單 {orderSn}：{e}"));
                continue;
            }
            if (items.Count == 0) { errors.Add($"第 {excelRow} 列訂單 {orderSn}：product_info 沒有商品。"); continue; }
            orders.Add(new ShopeeOrder(excelRow, orderSn, tracking, items, Cell("remark_from_buyer"), Cell("seller_note")));
        }
        return new(orders, errors);
    }

    internal static List<ShopeeOrderItem> ParseProductInfo(string productInfo, out List<string> errors)
    {
        errors = [];
        var items = new List<ShopeeOrderItem>();
        var segments = ItemMarker().Split(productInfo ?? string.Empty)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var number = i + 1;
            var code = Field(segment, "商品選項貨號");
            if (code.Length == 0) code = Field(segment, "商品貨號");
            var quantityText = Field(segment, "數量");
            var priceText = Field(segment, "價格").Replace("$", string.Empty).Replace(",", string.Empty).Trim();

            if (code.Length == 0) { errors.Add($"第 {number} 個商品沒有商品選項貨號（品號）。"); continue; }
            if (!int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0)
            { errors.Add($"第 {number} 個商品（{code}）的數量「{quantityText}」不是正整數。"); continue; }
            if (!decimal.TryParse(priceText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price < 0)
            { errors.Add($"第 {number} 個商品（{code}）的價格「{priceText}」無法辨識。"); continue; }

            items.Add(new ShopeeOrderItem(
                code,
                quantity.ToString(CultureInfo.InvariantCulture),
                price.ToString("0.####", CultureInfo.InvariantCulture),
                Field(segment, "商品名稱")));
        }
        return items;
    }

    /// <summary>The value after "key:" up to the next ";" (keys are matched as whole labels).</summary>
    private static string Field(string segment, string key)
    {
        var match = Regex.Match(segment, $@"(?:^|;)\s*{Regex.Escape(key)}\s*[:：]\s*(?<v>[^;]*)");
        return match.Success ? match.Groups["v"].Value.Trim() : string.Empty;
    }

    /// <summary>COPI08 form values for one order; other fields keep the form's current values.</summary>
    public static Dictionary<string, string> HeaderValues(ShopeeOrder order, ShopeeMapping mapping) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["order_type"] = mapping.OrderType,
        ["customer_code"] = mapping.CustomerCode,
        ["trade_note"] = mapping.NotePrefix + order.OrderSn,
        ["ship_addr1"] = order.TrackingNumber
    };

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex ItemMarker();
}
