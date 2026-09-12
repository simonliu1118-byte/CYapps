namespace CYInvoice.Core.Imports.Coupang;

public sealed class CoupangOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string BuyerBan { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public List<InvoiceItem> Items { get; } = [];
    public long TotalAmount { get; set; }
}

public static class CoupangImporter
{
    private static readonly string[] RequiredHeaders =
    [
        "訂單編號", "顯示產品名稱", "數量", "訂購人姓名",
        "應開立予買家之發票金額", "應開立予酷澎之發票金額", "統一編號",
    ];

    public static IReadOnlyList<CoupangOrder> ParseRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count < 2)
        {
            throw new InvalidDataException("Excel 沒有可匯入的酷澎資料");
        }
        var headerRow = -1;
        Dictionary<string, int>? columns = null;
        for (var index = 0; index < rows.Count; index++)
        {
            var candidate = HeaderMap(rows[index]);
            if (candidate.ContainsKey("訂單編號") && candidate.ContainsKey("應開立予買家之發票金額"))
            {
                headerRow = index;
                columns = candidate;
                break;
            }
        }
        if (headerRow < 0 || columns is null)
        {
            throw new InvalidDataException("找不到酷澎標題列（訂單編號、應開立予買家之發票金額）");
        }
        var missing = RequiredHeaders.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException("酷澎 Excel 缺少欄位：" + string.Join("、", missing));
        }

        var orders = new List<CoupangOrder>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var rowIndex = headerRow + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }
            var orderId = Value(row, columns, "訂單編號");
            if (orderId.Length == 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列沒有訂單編號");
            }
            FixedDecimal quantity;
            try
            {
                quantity = FixedDecimal.Parse(CleanNumber(Value(row, columns, "數量")));
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列數量格式錯誤", error);
            }
            if (quantity.ScaledValue <= 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列數量格式錯誤");
            }
            long amount;
            try
            {
                amount = ParseInteger(Value(row, columns, "應開立予買家之發票金額"));
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列應開立予買家之發票金額格式錯誤", error);
            }
            if (amount <= 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列應開立予買家之發票金額格式錯誤");
            }
            var description = Value(row, columns, "顯示產品名稱");
            if (description.Length == 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列缺少顯示產品名稱");
            }

            var buyerBan = NormalizeBan(Value(row, columns, "統一編號"));
            var buyerName = Value(row, columns, "訂購人姓名");
            if (!indexes.TryGetValue(orderId, out var orderIndex))
            {
                orderIndex = orders.Count;
                indexes.Add(orderId, orderIndex);
                orders.Add(new CoupangOrder { OrderId = orderId, BuyerBan = buyerBan, BuyerName = buyerName });
            }
            else if (orders[orderIndex].BuyerBan != buyerBan || orders[orderIndex].BuyerName != buyerName)
            {
                throw new InvalidDataException($"訂單 {orderId} 的買方資料在不同列不一致");
            }

            var amountDecimal = FixedDecimal.FromInt64(amount);
            var unitPrice = FixedDecimal.Divide(amountDecimal, quantity);
            var expected = FixedDecimal.Multiply(quantity, unitPrice);
            orders[orderIndex].Items.Add(new InvoiceItem
            {
                Description = description,
                Quantity = double.Parse(quantity.ToString(), System.Globalization.CultureInfo.InvariantCulture),
                QuantityDecimal = quantity.ToString(),
                UnitPrice = unitPrice.RoundInt64(),
                UnitPriceDecimal = unitPrice.ToString(),
                TaxType = "1",
                Amount = amount,
                AmountDecimal = amountDecimal.ToString(),
                AllowSubtotalRounding = expected != amountDecimal,
            });
            orders[orderIndex].TotalAmount = checked(orders[orderIndex].TotalAmount + amount);
        }

        if (orders.Count == 0)
        {
            throw new InvalidDataException("Excel 沒有可匯入的酷澎訂單");
        }
        foreach (var order in orders)
        {
            if (order.Items.Count > InvoiceLimits.MaximumItems)
            {
                throw new InvalidDataException($"訂單 {order.OrderId} 超過 {InvoiceLimits.MaximumItems} 筆商品");
            }
            var company = order.BuyerBan.Length != 0;
            var draft = new InvoiceDraft
            {
                OrderId = order.OrderId,
                CompanyBuyer = company,
                BuyerIdentifier = order.BuyerBan,
                BuyerName = company ? order.BuyerName : string.Empty,
                TotalAmount = order.TotalAmount,
            };
            draft.Items.AddRange(order.Items);
            try
            {
                InvoiceValidator.Validate(draft);
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"訂單 {order.OrderId}：{error.Message}", error);
            }
        }
        return orders;
    }

    private static Dictionary<string, int> HeaderMap(IReadOnlyList<string> row)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < row.Count; index++)
        {
            result[row[index].Trim().TrimStart('\ufeff')] = index;
        }
        return result;
    }

    private static string Value(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string name)
    {
        var index = columns[name];
        return index < row.Count ? row[index].Trim() : string.Empty;
    }

    private static long ParseInteger(string value)
    {
        var number = FixedDecimal.Parse(CleanNumber(value));
        var integer = number.RoundInt64();
        if (FixedDecimal.FromInt64(integer) != number)
        {
            throw new FormatException("invalid integer");
        }
        return integer;
    }

    private static string CleanNumber(string value) => value.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
    private static string NormalizeBan(string value) => value.Trim() == "0000000000" ? string.Empty : value.Trim();
}
