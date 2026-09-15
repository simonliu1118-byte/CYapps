using System.Globalization;

namespace CYInvoice.Core.Imports.Mo;

public static class MoCarriers
{
    public const string Member = "會員載具";
    public const string Mobile = "手機條碼";
    public const string Citizen = "自然人憑證";
}

public sealed class MoOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string CarrierId1 { get; set; } = string.Empty;
    public string CarrierId2 { get; set; } = string.Empty;
    public string NpoBan { get; set; } = string.Empty;
    public string BuyerBan { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerAddress { get; set; } = string.Empty;
    public string BuyerPhone { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public List<InvoiceItem> Items { get; } = [];
    public string MainRemark { get; set; } = string.Empty;
    public long TotalAmount { get; set; }
}

public static class MoImporter
{
    public const string RawItemAmountHeader = "開立發票金額_依品項(若您是自行開立發票，請依此金額開立予消費者)";
    public const string RawTotalAmountHeader = "開立發票金額加總(若您是自行開立發票，請依此金額開立予消費者)";

    public static IReadOnlyList<MoOrder> ParseRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (FindHeaderRow(rows, "訂單編號", "商品名稱", RawItemAmountHeader) < 0)
        {
            throw new InvalidDataException("找不到 MO店+ 原始 OrderExport 標題列；請直接選擇平台下載的原始檔案");
        }
        return ParseOrderExportRows(rows);
    }

    private static IReadOnlyList<MoOrder> ParseOrderExportRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count < 2)
        {
            throw new InvalidDataException("Excel 沒有可匯入的資料");
        }
        var headerRow = FindHeaderRow(rows, "訂單編號", "商品名稱", RawItemAmountHeader);
        if (headerRow < 0)
        {
            throw new InvalidDataException("找不到 MO店+ OrderExport 標題列");
        }
        var columns = HeaderMap(rows[headerRow]);
        var required = new[]
        {
            "訂單編號", "商品名稱", "數量", "應稅(免稅)", "客人支付運費", "平台補貼運費",
            "商品滿額免運費", RawItemAmountHeader, RawTotalAmountHeader, "發票開立統編",
        };
        RequireHeaders(columns, required, "MO店+ OrderExport");

        var orders = new List<MoOrder>();
        var amounts = new List<RawOrderAmounts>();
        var orderIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var currentOrderId = string.Empty;
        for (var rowIndex = headerRow + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (RowBlank(row))
            {
                continue;
            }
            var orderId = Value(row, columns, "訂單編號");
            if (orderId.Length == 0)
            {
                orderId = currentOrderId;
            }
            else
            {
                currentOrderId = orderId;
            }
            if (orderId.Length == 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列沒有訂單編號，也沒有可承接的上一筆訂單");
            }

            if (!orderIndexes.TryGetValue(orderId, out var orderIndex))
            {
                var buyerBan = Value(row, columns, "發票開立統編");
                var order = new MoOrder
                {
                    OrderId = orderId,
                    BuyerBan = buyerBan,
                    BuyerName = Value(row, columns, "收件人姓名"),
                    InvoiceType = buyerBan.Length == 0 ? "B2C" : "B2B",
                    Carrier = buyerBan.Length == 0 ? MoCarriers.Member : "公司戶",
                    CarrierId1 = buyerBan.Length == 0 ? "motmp_" + orderId : string.Empty,
                    CarrierId2 = buyerBan.Length == 0 ? "motmp_" + orderId : string.Empty,
                };
                orderIndex = orders.Count;
                orders.Add(order);
                amounts.Add(new RawOrderAmounts());
                orderIndexes.Add(orderId, orderIndex);
            }

            var rowBan = Value(row, columns, "發票開立統編");
            if (rowBan.Length != 0)
            {
                if (orders[orderIndex].BuyerBan.Length != 0 && orders[orderIndex].BuyerBan != rowBan)
                {
                    throw new InvalidDataException($"訂單 {orderId} 的發票開立統編不一致");
                }
                orders[orderIndex].BuyerBan = rowBan;
                orders[orderIndex].InvoiceType = "B2B";
                orders[orderIndex].Carrier = "公司戶";
                orders[orderIndex].CarrierId1 = string.Empty;
                orders[orderIndex].CarrierId2 = string.Empty;
            }
            if (orders[orderIndex].BuyerName.Length == 0)
            {
                orders[orderIndex].BuyerName = Value(row, columns, "收件人姓名");
            }
            var tax = Value(row, columns, "應稅(免稅)");
            if (tax.Length != 0 && tax is not "應稅" and not "1")
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列不是應稅商品，目前不可匯入");
            }
            var productName = Value(row, columns, "商品名稱");
            if (productName.Length == 0)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列缺少商品名稱");
            }
            var parts = new List<string> { productName };
            foreach (var header in new[] { "規格1", "規格2" })
            {
                var specification = Value(row, columns, header);
                if (specification.Length != 0)
                {
                    parts.Add(specification);
                }
            }
            var quantityText = CleanNumber(Value(row, columns, "數量"));
            long itemAmount;
            try
            {
                itemAmount = ParseInteger(Value(row, columns, RawItemAmountHeader));
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"第 {rowIndex + 1} 列官方開立發票金額（依品項）格式錯誤", error);
            }
            orders[orderIndex].Items.Add(OfficialInvoiceItem(string.Join(" ", parts), quantityText, itemAmount, rowIndex));

            amounts[orderIndex].OfficialTotal.Capture(Value(row, columns, RawTotalAmountHeader), "官方開立發票金額加總", orderId);
            amounts[orderIndex].Shipping.Capture(Value(row, columns, "客人支付運費"), "客人支付運費", orderId);
            amounts[orderIndex].Subsidy.Capture(Value(row, columns, "平台補貼運費"), "平台補貼運費", orderId);
            amounts[orderIndex].FreeShipping.Capture(Value(row, columns, "商品滿額免運費"), "商品滿額免運費", orderId);
        }

        if (orders.Count == 0)
        {
            throw new InvalidDataException("Excel 沒有可匯入的訂單");
        }
        for (var index = 0; index < orders.Count; index++)
        {
            var order = orders[index];
            var source = amounts[index];
            if (!source.OfficialTotal.Seen)
            {
                throw new InvalidDataException($"訂單 {order.OrderId} 缺少官方開立發票金額加總");
            }
            AppendAmountItem(order, "運費", source.Shipping);
            AppendAmountItem(order, "滿額免運費", source.FreeShipping);
            var detailTotal = ItemTotal(order.Items);
            if (detailTotal != source.OfficialTotal.Value && source.Subsidy.Seen && source.Subsidy.Value != 0 &&
                checked(detailTotal + source.Subsidy.Value) == source.OfficialTotal.Value)
            {
                AppendAmountItem(order, "運費補貼", source.Subsidy);
                detailTotal = ItemTotal(order.Items);
            }
            if (detailTotal != source.OfficialTotal.Value)
            {
                throw new InvalidDataException(
                    $"訂單 {order.OrderId} 的官方欄位互相不一致：明細及運費合計 {detailTotal}，官方開立發票金額加總 {source.OfficialTotal.Value}");
            }
            order.TotalAmount = source.OfficialTotal.Value;
        }
        ValidateOrders(orders, allowBuyerNameLookup: true);
        return orders;
    }

    private static InvoiceItem OfficialInvoiceItem(string description, string quantityText, long amount, int rowIndex)
    {
        FixedDecimal quantity;
        try
        {
            quantity = FixedDecimal.Parse(quantityText);
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"第 {rowIndex + 1} 列數量格式錯誤", error);
        }
        if (quantity.ScaledValue <= 0)
        {
            throw new InvalidDataException($"第 {rowIndex + 1} 列數量格式錯誤");
        }
        var amountValue = FixedDecimal.FromInt64(amount);
        var unitPrice = FixedDecimal.Divide(amountValue, quantity);
        var expected = FixedDecimal.Multiply(quantity, unitPrice);
        return new InvoiceItem
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
        };
    }

    private static void ValidateOrders(IEnumerable<MoOrder> orders, bool allowBuyerNameLookup)
    {
        foreach (var order in orders)
        {
            if (order.Items.Count > InvoiceLimits.MaximumItems)
            {
                throw new InvalidDataException($"訂單 {order.OrderId} 超過 {InvoiceLimits.MaximumItems} 筆商品");
            }
            var company = order.BuyerBan.Length != 0 && order.BuyerBan != "0000000000";
            var buyerName = company && allowBuyerNameLookup && string.IsNullOrWhiteSpace(order.BuyerName)
                ? "待由光貿查詢"
                : order.BuyerName;
            var draft = new InvoiceDraft
            {
                OrderId = order.OrderId,
                CompanyBuyer = company,
                BuyerIdentifier = company ? order.BuyerBan : string.Empty,
                BuyerName = company ? buyerName : string.Empty,
                TotalAmount = order.TotalAmount,
                MainRemark = order.MainRemark,
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
    }

    private static void AppendAmountItem(MoOrder order, string description, RawAmount amount)
    {
        if (!amount.Seen || amount.Value == 0)
        {
            return;
        }
        order.Items.Add(new InvoiceItem
        {
            Description = description,
            Quantity = 1,
            QuantityDecimal = "1",
            UnitPrice = amount.Value,
            UnitPriceDecimal = amount.Value.ToString(CultureInfo.InvariantCulture),
            Amount = amount.Value,
            AmountDecimal = amount.Value.ToString(CultureInfo.InvariantCulture),
            TaxType = "1",
        });
    }

    private static long ItemTotal(IEnumerable<InvoiceItem> items)
    {
        long result = 0;
        foreach (var item in items)
        {
            result = checked(result + item.Amount);
        }
        return result;
    }

    private static int FindHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows, params string[] names)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var columns = HeaderMap(rows[index]);
            if (names.All(columns.ContainsKey))
            {
                return index;
            }
        }
        return -1;
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

    private static void RequireHeaders(IReadOnlyDictionary<string, int> columns, IEnumerable<string> required, string label)
    {
        var missing = required.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException($"{label} 缺少欄位：{string.Join("、", missing)}");
        }
    }

    private static string Value(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : string.Empty;

    private static bool RowBlank(IEnumerable<string> row) => row.All(string.IsNullOrWhiteSpace);
    private static string CleanNumber(string value) => value.Trim().Replace(",", string.Empty, StringComparison.Ordinal);

    private static long ParseInteger(string value)
    {
        var number = FixedDecimal.Parse(CleanNumber(value));
        var integer = number.RoundInt64();
        if (FixedDecimal.FromInt64(integer) != number)
        {
            throw new FormatException("not an integer");
        }
        return integer;
    }

    private sealed class RawOrderAmounts
    {
        public RawAmount OfficialTotal { get; } = new();
        public RawAmount Shipping { get; } = new();
        public RawAmount Subsidy { get; } = new();
        public RawAmount FreeShipping { get; } = new();
    }

    private sealed class RawAmount
    {
        public bool Seen { get; private set; }
        public long Value { get; private set; }

        public void Capture(string text, string label, string orderId)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                return;
            }
            long value;
            try
            {
                value = ParseInteger(text);
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"訂單 {orderId}：{label}格式錯誤", error);
            }
            if (Seen && Value != value)
            {
                throw new InvalidDataException($"訂單 {orderId}：{label}在同一訂單內不一致");
            }
            Seen = true;
            Value = value;
        }
    }
}
