using System.Globalization;

namespace CYERPAutoInput;

/// <summary>One 滿件贈送 card: every <see cref="Every"/> of <see cref="ItemCode"/> in an order gets <see cref="Gift"/> free, on the listed platforms.</summary>
internal sealed class GiftRule
{
    public string ItemCode { get; set; } = string.Empty;
    public int Every { get; set; }
    public int Gift { get; set; }
    /// <summary>Import sources (蝦皮, MO店+, 酷澎) the card applies to.</summary>
    public List<string> Platforms { get; set; } = [];
}

/// <summary>
/// 滿件贈送 (user, 2026-10-14): e.g. 品號 X 買10送1 on 蝦皮 — an order with 10 of that item alone
/// gets 1 in 贈/備品量, 20 gets 2, and so on. Counted per item code over the whole order (its
/// rows added up), written on the item's first row. Pure: no UI, unit tested.
/// </summary>
internal static class GiftRules
{
    public static readonly string[] Platforms = [ShopeeOrderImport.Source, MoOrderImport.Source, CoupangOrderImport.Source];

    /// <summary>What is wrong with the cards, for the settings to refuse saving.</summary>
    public static List<string> Problems(IReadOnlyList<GiftRule> rules)
    {
        var problems = new List<string>();
        var owner = new Dictionary<(string, string), int>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var code = rule.ItemCode.Trim();
            if (code.Length == 0) { problems.Add($"第 {i + 1} 張沒有品號。"); continue; }
            if (rule.Every < 1) problems.Add($"第 {i + 1} 張（{code}）的「滿幾件」要是 1 以上的整數。");
            if (rule.Gift < 1) problems.Add($"第 {i + 1} 張（{code}）的「送幾件」要是 1 以上的整數。");
            if (rule.Platforms.Count == 0) problems.Add($"第 {i + 1} 張（{code}）沒有勾選適用平台。");
            foreach (var platform in rule.Platforms)
            {
                if (owner.TryGetValue((code.ToUpperInvariant(), platform), out var other))
                    problems.Add($"品號 {code} 在{platform}同時有第 {other + 1} 張和第 {i + 1} 張，請只留一張。");
                else owner[(code.ToUpperInvariant(), platform)] = i;
            }
        }
        return problems;
    }

    /// <summary>
    /// The free quantity per row index for an order from <paramref name="source"/>:
    /// floor(total of the code ÷ 滿幾件) × 送幾件, on the code's first row. Rows whose
    /// quantity is not a whole number are left out of the count.
    /// </summary>
    public static Dictionary<int, int> Gifts(string source, IReadOnlyList<(string ItemCode, string Quantity)> rows, IReadOnlyList<GiftRule> rules)
    {
        var result = new Dictionary<int, int>();
        foreach (var rule in rules)
        {
            var code = rule.ItemCode.Trim();
            if (code.Length == 0 || rule.Every < 1 || rule.Gift < 1 || !rule.Platforms.Contains(source)) continue;
            var first = -1;
            var total = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(rows[i].ItemCode.Trim(), code, StringComparison.OrdinalIgnoreCase)) continue;
                if (first < 0) first = i;
                if (int.TryParse(rows[i].Quantity.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) && quantity > 0)
                    total += quantity;
            }
            var gift = total / rule.Every * rule.Gift;
            if (first >= 0 && gift > 0) result[first] = gift;
        }
        return result;
    }

    /// <summary>Cards without a code are dropped; platforms kept in their usual order.</summary>
    public static List<GiftRule> Clean(IEnumerable<GiftRule> rules) =>
        rules.Where(r => r.ItemCode.Trim().Length > 0)
            .Select(r => new GiftRule { ItemCode = r.ItemCode.Trim(), Every = r.Every, Gift = r.Gift, Platforms = Platforms.Where(r.Platforms.Contains).ToList() })
            .ToList();
}
