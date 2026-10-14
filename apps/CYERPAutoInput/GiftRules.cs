using System.Globalization;

namespace CYERPAutoInput;

/// <summary>
/// One 滿件贈送 card: every <see cref="Every"/> of <see cref="ItemCodes"/> together in an order
/// gets <see cref="Gift"/> free, on the listed platforms; the free quantity goes to
/// <see cref="GiftItem"/>, or (blank) to the code the order has most of.
/// </summary>
internal sealed class GiftRule
{
    /// <summary>One or more item codes, comma separated, counted together.</summary>
    public string ItemCodes { get; set; } = string.Empty;
    public int Every { get; set; }
    public int Gift { get; set; }
    /// <summary>The code that gets the free quantity; blank = the one with the most in the order.</summary>
    public string GiftItem { get; set; } = string.Empty;
    /// <summary>Import sources (蝦皮, MO店+, 酷澎) the card applies to.</summary>
    public List<string> Platforms { get; set; } = [];
}

/// <summary>
/// 滿件贈送 (user, 2026-10-14): e.g. 品號 X 買10送1 on 蝦皮 — 10 of it get 1 in 贈/備品量, 20 get
/// 2, and so on. A card may name several codes counted together (X, Y, Z: 5+3+2 = 10 → 1);
/// the free quantity goes to the named code, or to the one the order has most of (ties: the
/// card's order). Each code's rows are added up; the gift is written on its first row. Pure:
/// no UI, unit tested.
/// </summary>
internal static class GiftRules
{
    public static readonly string[] Platforms = [ShopeeOrderImport.Source, MoOrderImport.Source, CoupangOrderImport.Source];

    public static IReadOnlyList<string> Codes(string codes) =>
        codes.Split([',', '，', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>What is wrong with the cards, for the settings to refuse saving.</summary>
    public static List<string> Problems(IReadOnlyList<GiftRule> rules)
    {
        var problems = new List<string>();
        var owner = new Dictionary<(string, string), int>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var codes = Codes(rule.ItemCodes);
            if (codes.Count == 0) { problems.Add($"第 {i + 1} 張沒有品號。"); continue; }
            var name = string.Join("、", codes);
            if (rule.Every < 1) problems.Add($"第 {i + 1} 張（{name}）的「滿幾件」要是 1 以上的整數。");
            if (rule.Gift < 1) problems.Add($"第 {i + 1} 張（{name}）的「送幾件」要是 1 以上的整數。");
            if (rule.Platforms.Count == 0) problems.Add($"第 {i + 1} 張（{name}）沒有勾選適用平台。");
            var giftItem = rule.GiftItem.Trim();
            if (giftItem.Length > 0 && !codes.Contains(giftItem, StringComparer.OrdinalIgnoreCase))
                problems.Add($"第 {i + 1} 張的贈送品號 {giftItem} 不在這張的品號裡。");
            foreach (var platform in rule.Platforms)
                foreach (var code in codes)
                {
                    if (owner.TryGetValue((code.ToUpperInvariant(), platform), out var other))
                        problems.Add($"品號 {code} 在{platform}同時出現在第 {other + 1} 張和第 {i + 1} 張，請只留在一張。");
                    else owner[(code.ToUpperInvariant(), platform)] = i;
                }
        }
        return problems;
    }

    /// <summary>
    /// The free quantity per row index for an order from <paramref name="source"/>:
    /// floor(total of the card's codes ÷ 滿幾件) × 送幾件 on the gift code's first row. Rows whose
    /// quantity is not a whole number are left out of the count. A named gift code the order
    /// does not have is listed in <paramref name="missing"/> (no row to put it on).
    /// </summary>
    public static Dictionary<int, int> Gifts(string source, IReadOnlyList<(string ItemCode, string Quantity)> rows, IReadOnlyList<GiftRule> rules,
        List<string>? missing = null)
    {
        var result = new Dictionary<int, int>();
        foreach (var rule in rules)
        {
            var codes = Codes(rule.ItemCodes);
            if (codes.Count == 0 || rule.Every < 1 || rule.Gift < 1 || !rule.Platforms.Contains(source)) continue;
            var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rows.Count; i++)
            {
                var code = codes.FirstOrDefault(c => string.Equals(c, rows[i].ItemCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (code is null) continue;
                first.TryAdd(code, i);
                if (int.TryParse(rows[i].Quantity.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) && quantity > 0)
                    totals[code] = totals.GetValueOrDefault(code) + quantity;
            }
            var gift = totals.Values.Sum() / rule.Every * rule.Gift;
            if (gift <= 0) continue;
            var giftItem = rule.GiftItem.Trim();
            string? target;
            if (giftItem.Length > 0)
            {
                target = codes.FirstOrDefault(c => string.Equals(c, giftItem, StringComparison.OrdinalIgnoreCase));
                if (target is null || !first.ContainsKey(target))
                {
                    missing?.Add($"滿件贈送：{string.Join("、", codes)} 合計已滿，應送 {giftItem} {gift} 件，但訂單裡沒有 {giftItem}，請人工加列。");
                    continue;
                }
            }
            else target = codes.Where(totals.ContainsKey).OrderByDescending(c => totals[c]).First();
            var row = first[target];
            result[row] = result.GetValueOrDefault(row) + gift;
        }
        return result;
    }

    /// <summary>Cards without a code are dropped; codes tidied; platforms kept in their usual order.</summary>
    public static List<GiftRule> Clean(IEnumerable<GiftRule> rules) =>
        rules.Where(r => Codes(r.ItemCodes).Count > 0)
            .Select(r => new GiftRule
            {
                ItemCodes = string.Join(",", Codes(r.ItemCodes)),
                Every = r.Every,
                Gift = r.Gift,
                GiftItem = r.GiftItem.Trim(),
                Platforms = Platforms.Where(r.Platforms.Contains).ToList()
            })
            .ToList();
}
