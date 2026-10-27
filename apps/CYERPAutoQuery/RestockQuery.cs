namespace CYERPAutoQuery;

/// <summary>
/// 進貨輔助 (CY Web, user 2026-10-20): every customer's 銷貨單 and 銷退單 in a 單據日期 range, both
/// dates required, at most <see cref="MaxDays"/> days.
/// </summary>
internal sealed record RestockQuery(DateOnly From, DateOnly To)
{
    public const int MaxDays = 31;

    public static RestockQuery Resolve(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is not { } start || to is not { } end) throw new ArgumentException("進貨輔助的單據日期起訖都要填。");
        if (start > end) throw new ArgumentException("單據日期的起日不可晚於迄日。");
        if (end > today) throw new ArgumentException("單據日期的迄日不可晚於今天。");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) throw new ArgumentException($"單據日期範圍最多 {MaxDays} 天。");
        return new RestockQuery(start, end);
    }

    /// <summary>The reports are run for every customer: an empty 客戶代號 range.</summary>
    public SalesQuery Sales => new(string.Empty, From, To);
}

/// <summary>
/// One day of an item: what left and came back, 贈／備品量 included (user 2026-10-21: gifts count as
/// 銷貨／銷退); the gift part is kept apart too, for later 銷貨分析.
/// </summary>
internal sealed record RestockDay(DateOnly Date, decimal Sold, decimal Returned, decimal SoldGift = 0m, decimal ReturnedGift = 0m);

/// <summary>One 品號 sold or returned in the range, and its 庫存數量 once looked up.</summary>
internal sealed record RestockItem(string ItemCode, string ItemName, string Unit, IReadOnlyList<RestockDay> Days)
{
    public decimal Sold => Days.Sum(d => d.Sold);
    public decimal Returned => Days.Sum(d => d.Returned);
    public decimal SoldGift => Days.Sum(d => d.SoldGift);
    public decimal ReturnedGift => Days.Sum(d => d.ReturnedGift);
    /// <summary>INVI02 庫存數量; null when it was not looked up or could not be read (<see cref="StockNote"/> says why).</summary>
    public string? Stock { get; init; }
    public string StockNote { get; init; } = string.Empty;
}

internal sealed record RestockResult(RestockQuery Query, IReadOnlyList<RestockItem> Items, TimeSpan Elapsed)
{
    public bool Found => Items.Count > 0;
}

internal static class Restock
{
    /// <summary>Items whose 庫存 is looked up (as many as are reported to CY Web); past this the rest say so.</summary>
    public const int MaxStockLookups = ErpAgentProtocol.MaxRestockItems;

    /// <summary>ERP's 庫存數量 as a plain number (「1,234.000」 → 「1234」), or null when it is not one; CY Web takes digits only.</summary>
    public static string? Quantity(string text) =>
        decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>The orders' lines by 品號 (order of 品號) and by day; 品名／單位 from the first line seen.</summary>
    public static List<RestockItem> Summarize(IEnumerable<SalesOrder> orders)
    {
        var items = new SortedDictionary<string, (string Name, string Unit, SortedDictionary<DateOnly, RestockDay> Days)>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
            foreach (var line in order.Lines.Where(l => l.ItemCode.Length > 0))
            {
                if (!items.TryGetValue(line.ItemCode, out var item))
                    items[line.ItemCode] = item = (line.ItemName, line.Unit, new SortedDictionary<DateOnly, RestockDay>());
                var day = item.Days.GetValueOrDefault(order.Date) ?? new RestockDay(order.Date, 0m, 0m);
                item.Days[order.Date] = order.Kind == SalesReport.Returns.Kind
                    ? day with { Returned = day.Returned + line.Quantity + line.GiftQuantity, ReturnedGift = day.ReturnedGift + line.GiftQuantity }
                    : day with { Sold = day.Sold + line.Quantity + line.GiftQuantity, SoldGift = day.SoldGift + line.GiftQuantity };
            }
        return items.Select(p => new RestockItem(p.Key, p.Value.Name, p.Value.Unit, p.Value.Days.Values.ToList())).ToList();
    }
}
