namespace CYERPAutoInput;

/// <summary>One product of the 撿貨總表: everything to take off the shelves for it.</summary>
internal sealed record PickItem(string Code, string Name, decimal Quantity, string Unit);

/// <summary>
/// 撿貨總表 (user, 2026-10-27): what a 全自動 batch saved, added up per 品號 (數量 and 贈／備品量),
/// for the people picking. The 折價券／運費／促銷組合 lines are not goods and are left out. Pure.
/// </summary>
internal sealed class PickList(IEnumerable<string> notGoods)
{
    private readonly HashSet<string> _notGoods = new(notGoods.Where(c => c.Trim().Length > 0).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Name, decimal Quantity, string Unit)> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Documents added.</summary>
    public int Orders { get; private set; }

    public void Add(IEnumerable<ShipSheetLine> lines)
    {
        Orders++;
        foreach (var line in lines)
        {
            var code = line.Code.Trim();
            if (code.Length == 0 || _notGoods.Contains(code)) continue;
            var quantity = (ShipSheet.Number(line.Quantity) ?? 0) + (ShipSheet.Number(line.GiftQuantity) ?? 0);
            if (quantity == 0) continue;
            var known = _items.TryGetValue(code, out var item) ? item : (string.Empty, 0m, string.Empty);
            _items[code] = (known.Item1.Length > 0 ? known.Item1 : line.Name.Trim(), known.Item2 + quantity, known.Item3.Length > 0 ? known.Item3 : line.Unit.Trim());
        }
    }

    /// <summary>By 品號.</summary>
    public IReadOnlyList<PickItem> Items =>
        _items.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new PickItem(p.Key, p.Value.Name, p.Value.Quantity, p.Value.Unit)).ToList();
}
