using System.Text.RegularExpressions;
using System.Globalization;
namespace CYERPAutoInput;

internal sealed record ImportedItem(string ItemCode, string Quantity, string UnitPrice)
{
    /// <summary>For the parts of a bundle: the bundle's first code, so its lines stay together in that order.</summary>
    public string? SortKey { get; init; }
    /// <summary>
    /// On a bundle's first part: what the bundle sold for. Bundle parts carry no 單價, so ERP
    /// enters its own list price, and the gap between those and this goes to 促銷組合 (user,
    /// 2026-10-12: the report's figures stay right per product).
    /// </summary>
    public string? BundleTotal { get; init; }
    /// <summary>The 促銷組合 line that also takes the bundles' gap, worked out while ERP is filled in.</summary>
    public bool TakesBundleGap { get; init; }
    /// <summary>
    /// 贈／備品量: a part marked 「(贈)」 (A00175+A00173(贈); user, 2026-10-20) goes out free, in
    /// that column; alone it is a line with 數量 0 and 單價 0.
    /// </summary>
    public string GiftQuantity { get; init; } = string.Empty;

    /// <summary>
    /// Products in item-code order, smallest first (user, 2026-10-12); the lines for the
    /// configured 折價券／運費／促銷組合 codes stay last, in their own order.
    /// </summary>
    public static List<ImportedItem> InEntryOrder(IEnumerable<ImportedItem> items, IEnumerable<string> trailingCodes)
    {
        var trailing = new HashSet<string>(trailingCodes.Where(c => c.Length > 0), StringComparer.OrdinalIgnoreCase);
        var list = items.ToList();
        return list.Where(i => !trailing.Contains(i.ItemCode)).OrderBy(i => i.SortKey ?? i.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Concat(list.Where(i => trailing.Contains(i.ItemCode)))
            .ToList();
    }
}

/// <summary>
/// One platform order ready for the COPI08 form, whatever its source (蝦皮, MO店+). The
/// preview may still edit the tracking number and, for MO店+, the invoice fields. Pure:
/// no Win32 or UI, unit tested.
/// </summary>
internal sealed class ImportedOrder
{
    public required string Source { get; init; }
    public required string OrderSn { get; init; }
    /// <summary>Header values fixed by the import (單別, 客戶代號, 備註, 貨運別, 代收貨款…).</summary>
    public required IReadOnlyDictionary<string, string> Header { get; init; }
    public required IReadOnlyList<ImportedItem> Items { get; init; }
    /// <summary>Why a person must take over (entered up to the header only); empty when none.</summary>
    public string HandoffReason { get; init; } = string.Empty;
    public string Carrier { get; init; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    /// <summary>MO店+ only: the preview lets the user fill 客戶全名／發票日期／發票號碼.</summary>
    public bool InvoiceEditable { get; init; }
    public string BuyerTaxId { get; init; } = string.Empty;
    public string InvoiceName { get; set; } = string.Empty;
    public string InvoiceDate { get; set; } = string.Empty;
    public string InvoiceNo { get; set; } = string.Empty;
    /// <summary>Display total (代收貨款 for MO店+); empty when the source has none.</summary>
    public string Total { get; init; } = string.Empty;

    /// <summary>Key of this order in the duplicate ledger (source + platform order number).</summary>
    public string LedgerKey => $"{Source}:{OrderSn}";

    public Dictionary<string, string> HeaderValues()
    {
        var values = new Dictionary<string, string>(Header, StringComparer.OrdinalIgnoreCase);
        if (TrackingNumber.Trim().Length > 0) values["ship_addr1"] = TrackingNumber.Trim();
        if (BuyerTaxId.Length > 0) values["tax_id"] = BuyerTaxId;
        if (InvoiceEditable)
        {
            if (InvoiceName.Trim().Length > 0) values["inv_name"] = InvoiceName.Trim();
            if (InvoiceDate.Trim().Length > 0) values["inv_date"] = InvoiceDate.Trim();
            if (InvoiceNo.Trim().Length > 0) values["inv_no"] = InvoiceNo.Trim();
        }
        return values;
    }

    /// <summary>
    /// A buyer tax ID without 客戶全名: warned about before entering, not required (user,
    /// 2026-10-11; the name is looked up automatically and may stay blank).
    /// </summary>
    public bool MissingInvoiceName => BuyerTaxId.Length > 0 && InvoiceName.Trim().Length == 0;

    /// <summary>What the preview must fix before this order can be entered; empty when fine.</summary>
    public string ValidationError()
    {
        if (InvoiceDate.Trim().Length > 0 && !InputRules.TryNormalizeValidDate(InvoiceDate.Trim(), out _))
            return "發票日期必須是有效日期（YYYY/MM/DD）。";
        return string.Empty;
    }

    public string ItemSummary() =>
        string.Join("、", Items.Select(i => i.GiftQuantity.Length == 0 ? $"{i.ItemCode}×{i.Quantity}＠{i.UnitPrice}"
            : i.Quantity == "0" ? $"{i.ItemCode} 贈×{i.GiftQuantity}" : $"{i.ItemCode}×{i.Quantity}＠{i.UnitPrice}＋贈×{i.GiftQuantity}"));
}

/// <summary>
/// Carriers whose ERP 貨運別 code is set in 設定 (shared by every platform; user,
/// 2026-10-11). A carrier applies when its name appears in the export's carrier text
/// (e.g. 第三方物流-新竹貨運, 7-11店出); only the ERP codes are local settings.
/// </summary>
/// <summary>
/// Bundles 「A+B」 from any source (user, 2026-10-12): every part goes out as many times as
/// bundles were sold, with no 單價 so ERP enters its own list price; the first part remembers
/// what the bundle sold for, and the 促銷組合 line takes the gap while ERP is filled in.
/// A part marked 「(贈)」 (A00175+A00173(贈); user, 2026-10-20) is no part of the price: its
/// code goes out with the quantity in 贈／備品量, and the rest is entered as if sold alone.
/// </summary>
internal static partial class Bundles
{
    public static List<string> Parts(string code) =>
        code.Replace('＋', '+').Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static bool IsBundle(string code) => Parts(code).Where(p => !IsGift(p)).Count() > 1;

    /// <summary>A part 「A00173(贈)」 (half- or full-width brackets, 贈 or 赠): its code, and whether it is free.</summary>
    public static (string Code, bool Gift) SplitGift(string part)
    {
        var match = GiftSuffix().Match(part.Trim());
        return match.Success ? (match.Groups["code"].Value.Trim(), true) : (part.Trim(), false);
    }

    private static bool IsGift(string part) => SplitGift(part).Gift;

    /// <summary>The items with every bundle expanded, and the 促銷組合 line marked (added at 0 when missing).</summary>
    public static List<ImportedItem> Expand(IEnumerable<ImportedItem> items, string promotionCode, List<string> problems)
    {
        var result = new List<ImportedItem>();
        var any = false;
        foreach (var source in items)
        {
            var item = source;
            var all = Parts(item.ItemCode);
            if (all.Any(IsGift))
            {
                var quantity = decimal.Parse(item.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture);
                foreach (var gift in all.Where(IsGift))
                {
                    var (code, times) = ShopeeScreenshot.SplitSet(SplitGift(gift).Code);
                    result.Add(GiftLine(code, quantity * times));
                }
                var sold = all.Where(p => !IsGift(p)).ToList();
                if (sold.Count == 0)
                {
                    if (item.UnitPrice.Length > 0 && decimal.Parse(item.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture) != 0)
                        problems.Add($"「{item.ItemCode}」只有贈品卻有單價 {item.UnitPrice}，請核對。");
                    continue;
                }
                item = item with { ItemCode = string.Join("+", sold) };
            }
            var parts = Parts(item.ItemCode);
            if (parts.Count < 2) { result.Add(item); continue; }
            any = true;
            var bundles = decimal.Parse(item.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture);
            var unit = decimal.Parse(item.UnitPrice.Length > 0 ? item.UnitPrice : "0", NumberStyles.Number, CultureInfo.InvariantCulture);
            for (var i = 0; i < parts.Count; i++)
            {
                var (code, times) = ShopeeScreenshot.SplitSet(parts[i]);
                result.Add(new ImportedItem(code, Number(bundles * times), string.Empty)
                {
                    SortKey = parts[0],
                    BundleTotal = i == 0 ? Number(unit * bundles) : null
                });
            }
        }
        result = MergeGifts(result);
        if (!any) return result;
        if (promotionCode.Length == 0)
        {
            problems.Add("有套裝商品，請在設定填入促銷組合品號（套裝與原價的差額要打在這裡）");
            return result;
        }
        var promotion = result.FindIndex(i => i.ItemCode.Equals(promotionCode, StringComparison.OrdinalIgnoreCase));
        if (promotion >= 0) result[promotion] = result[promotion] with { TakesBundleGap = true };
        else result.Add(new ImportedItem(promotionCode, "1", "0") { TakesBundleGap = true });
        return result;
    }

    /// <summary>A free line: 數量 0, 單價 0, the quantity in 贈／備品量.</summary>
    public static ImportedItem GiftLine(string code, decimal quantity) =>
        new(code, "0", "0") { GiftQuantity = Number(quantity) };

    /// <summary>
    /// Free lines join a sold line of the same code (its 贈／備品量), else one line per code;
    /// a sold line keeps its place.
    /// </summary>
    public static List<ImportedItem> MergeGifts(List<ImportedItem> items)
    {
        static bool FreeOnly(ImportedItem i) => i.GiftQuantity.Length > 0 && i.Quantity == "0";
        static decimal Gift(ImportedItem i) => i.GiftQuantity.Length == 0 ? 0 : decimal.Parse(i.GiftQuantity, NumberStyles.Number, CultureInfo.InvariantCulture);
        var result = items.Where(i => !FreeOnly(i)).ToList();
        foreach (var gifts in items.Where(FreeOnly).GroupBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase))
        {
            var quantity = gifts.Sum(Gift);
            var sold = result.FindIndex(i => i.ItemCode.Equals(gifts.Key, StringComparison.OrdinalIgnoreCase));
            if (sold >= 0) result[sold] = result[sold] with { GiftQuantity = Number(Gift(result[sold]) + quantity) };
            else result.Add(GiftLine(gifts.First().ItemCode, quantity));
        }
        return result;
    }

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<code>.*?[0-9A-Za-z])\s*[(（]\s*[贈赠]品?\s*[)）]$")]
    private static partial Regex GiftSuffix();
}

internal static class FreightCarriers
{
    /// <summary>The company's own delivery, kept apart from the carriers (user, 2026-10-11).</summary>
    public static readonly string[] Own = ["本公司"];

    /// <summary>Home-delivery carriers, in the order of the user's ERP codes (user, 2026-10-11).</summary>
    public static readonly string[] Logistics = ["郵局", "大榮", "新竹", "黑貓", "宅配通", "嘉里"];

    /// <summary>Convenience-store pickup channels, in the order of the user's ERP codes.</summary>
    public static readonly string[] Stores = ["7-11", "全家", "萊爾富", "蝦皮店到店", "蝦皮店到店 - 隔日到貨", "店到家宅配"];

    public static readonly string[] All = [.. Own, .. Logistics, .. Stores];

    /// <summary>Other names a platform may print for a carrier.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        ["郵局"] = ["中華郵政"],
        // Shopee prints 7-11 as 「7-ELEVEN」 (user, 2026-10-12).
        ["7-11"] = ["7-ELEVEN", "7ELEVEN", "統一超商"],
        ["全家"] = ["FamilyMart"],
        ["萊爾富"] = ["Hi-Life", "HiLife"]
    };

    /// <summary>
    /// The code of the carrier named in <paramref name="carrierText"/>; the longest name wins
    /// (蝦皮店到店 - 隔日到貨 over 蝦皮店到店). Spaces are ignored on both sides.
    /// </summary>
    public static string Match(string carrierText, IReadOnlyDictionary<string, string> codes)
    {
        var text = Compact(carrierText);
        if (text.Length == 0) return string.Empty;
        int Score(string key) =>
            new[] { key }.Concat(Aliases.TryGetValue(key, out var more) ? more : [])
                .Select(Compact)
                .Where(name => name.Length > 0 && text.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(name => name.Length)
                .DefaultIfEmpty(0)
                .Max();
        return codes
            .Where(p => p.Value.Trim().Length > 0)
            .Select(p => (Code: p.Value.Trim(), Score: Score(p.Key)))
            .Where(p => p.Score > 0)
            .OrderByDescending(p => p.Score)
            .Select(p => p.Code)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string Compact(string value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
}
