using System.Globalization;

namespace CYInvoice.Core.Imports.Digiwin;

public sealed class DigiwinOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string BuyerBan { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string OriginalBuyerBan { get; init; } = string.Empty;
    public string OriginalBuyerName { get; init; } = string.Empty;
    public List<InvoiceItem> Items { get; } = [];
    public long TotalAmount { get; set; }
}

public static class DigiwinImporter
{
    private static readonly string[] HeadHeaders = ["銷貨單號", "客戶全名", "統一編號", "本幣合計"];
    private static readonly string[] DetailHeaders = ["品名", "數量", "金額"];

    public static DigiwinOrder ParseRows(
        IReadOnlyList<IReadOnlyList<string>> headRows,
        IReadOnlyList<IReadOnlyList<string>> detailRows)
    {
        ArgumentNullException.ThrowIfNull(headRows);
        ArgumentNullException.ThrowIfNull(detailRows);

        var headHeaderIndex = FindHeaderRow(headRows, HeadHeaders);
        if (headHeaderIndex < 0) throw new InvalidDataException("鼎新單頭資料找不到必要欄位：銷貨單號、客戶全名、統一編號、本幣合計");
        var headColumns = HeaderMap(headRows[headHeaderIndex]);
        RequireHeaders(headColumns, HeadHeaders, "鼎新單頭資料");
        var headData = headRows.Skip(headHeaderIndex + 1)
            .Where(row => !RowBlank(row))
            .ToArray();
        if (headData.Length != 1)
            throw new InvalidDataException(headData.Length == 0 ? "鼎新單頭資料沒有銷貨單" : "鼎新標準匯出檔必須只有一張銷貨單");

        var orderId = Value(headData[0], headColumns, "銷貨單號");
        if (orderId.Length == 0) throw new InvalidDataException("鼎新單頭資料的銷貨單號不可空白");
        var originalBan = Value(headData[0], headColumns, "統一編號");
        var originalName = Value(headData[0], headColumns, "客戶全名");
        var total = ParseInteger(Value(headData[0], headColumns, "本幣合計"), "鼎新單頭資料的本幣合計格式錯誤");
        if (total <= 0) throw new InvalidDataException("鼎新單頭資料的本幣合計必須大於 0");

        var detailHeaderIndex = FindHeaderRow(detailRows, DetailHeaders);
        if (detailHeaderIndex < 0) throw new InvalidDataException("鼎新單身資料找不到必要欄位：品名、數量、金額");
        var detailColumns = HeaderMap(detailRows[detailHeaderIndex]);
        RequireHeaders(detailColumns, DetailHeaders, "鼎新單身資料");

        var order = new DigiwinOrder
        {
            OrderId = orderId,
            BuyerBan = originalBan,
            BuyerName = originalBan.Length == 0 ? string.Empty : originalName,
            OriginalBuyerBan = originalBan,
            OriginalBuyerName = originalName,
            TotalAmount = total,
        };

        for (var rowIndex = detailHeaderIndex + 1; rowIndex < detailRows.Count; rowIndex++)
        {
            var row = detailRows[rowIndex];
            if (RowBlank(row)) continue;
            var description = Value(row, detailColumns, "品名");
            var quantityText = Value(row, detailColumns, "數量");
            var amountText = Value(row, detailColumns, "金額");
            if (description.Length == 0 && quantityText.Length == 0 && amountText.Length == 0) continue;
            if (description.Length == 0) throw new InvalidDataException($"鼎新單身資料第 {rowIndex + 1} 列品名不可空白");

            FixedDecimal quantity;
            try { quantity = FixedDecimal.Parse(CleanNumber(quantityText)); }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new InvalidDataException($"鼎新單身資料第 {rowIndex + 1} 列數量格式錯誤", error);
            }
            if (quantity.ScaledValue <= 0) throw new InvalidDataException($"鼎新單身資料第 {rowIndex + 1} 列數量必須大於 0");

            var amount = ParseInteger(amountText, $"鼎新單身資料第 {rowIndex + 1} 列金額格式錯誤");
            var amountValue = FixedDecimal.FromInt64(amount);
            var unitPrice = FixedDecimal.Divide(amountValue, quantity);
            var expected = FixedDecimal.Multiply(quantity, unitPrice);
            order.Items.Add(new InvoiceItem
            {
                Description = description,
                Quantity = double.Parse(quantity.ToString(), CultureInfo.InvariantCulture),
                QuantityDecimal = quantity.ToString(),
                UnitPrice = unitPrice.RoundInt64(),
                UnitPriceDecimal = unitPrice.ToString(),
                Amount = amount,
                AmountDecimal = amount.ToString(CultureInfo.InvariantCulture),
                TaxType = "1",
                AllowSubtotalRounding = expected != amountValue,
            });
        }

        if (order.Items.Count == 0) throw new InvalidDataException("鼎新單身資料沒有可開立的商品明細");
        if (order.Items.Count > InvoiceLimits.MaximumItems)
            throw new InvalidDataException($"鼎新銷貨單 {order.OrderId} 超過 {InvoiceLimits.MaximumItems} 筆商品");
        var detailTotal = order.Items.Aggregate(0L, (sum, item) => checked(sum + item.Amount));
        if (detailTotal != order.TotalAmount)
            throw new InvalidDataException($"鼎新銷貨單 {order.OrderId} 金額不一致：單身金額合計 {detailTotal}，單頭本幣合計 {order.TotalAmount}");

        var draft = new InvoiceDraft { OrderId = order.OrderId, TotalAmount = order.TotalAmount };
        draft.Items.AddRange(order.Items);
        try { InvoiceValidator.Validate(draft); }
        catch (Exception error) { throw new InvalidDataException($"鼎新銷貨單 {order.OrderId}：{error.Message}", error); }
        return order;
    }

    private static int FindHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows, params string[] names)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var columns = HeaderMap(rows[index]);
            if (names.All(columns.ContainsKey)) return index;
        }
        return -1;
    }

    private static Dictionary<string, int> HeaderMap(IReadOnlyList<string> row)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < row.Count; index++) result[row[index].Trim().TrimStart('\ufeff')] = index;
        return result;
    }

    private static void RequireHeaders(IReadOnlyDictionary<string, int> columns, IEnumerable<string> required, string label)
    {
        var missing = required.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length != 0) throw new InvalidDataException($"{label} 缺少欄位：{string.Join("、", missing)}");
    }

    private static string Value(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;

    private static bool RowBlank(IEnumerable<string> row) => row.All(string.IsNullOrWhiteSpace);
    private static string CleanNumber(string value) => value.Trim().Replace(",", string.Empty, StringComparison.Ordinal);

    private static long ParseInteger(string value, string message)
    {
        try
        {
            var number = FixedDecimal.Parse(CleanNumber(value));
            var integer = number.RoundInt64();
            if (FixedDecimal.FromInt64(integer) != number) throw new FormatException("not an integer");
            return integer;
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new InvalidDataException(message, error);
        }
    }
}
