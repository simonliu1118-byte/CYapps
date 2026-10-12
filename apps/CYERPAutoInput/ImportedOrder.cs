namespace CYERPAutoInput;

internal sealed record ImportedItem(string ItemCode, string Quantity, string UnitPrice)
{
    /// <summary>
    /// Products in item-code order, smallest first (user, 2026-10-12); the lines for the
    /// configured 折價券／運費／促銷組合 codes stay last, in their own order.
    /// </summary>
    public static List<ImportedItem> InEntryOrder(IEnumerable<ImportedItem> items, IEnumerable<string> trailingCodes)
    {
        var trailing = new HashSet<string>(trailingCodes.Where(c => c.Length > 0), StringComparer.OrdinalIgnoreCase);
        var list = items.ToList();
        return list.Where(i => !trailing.Contains(i.ItemCode)).OrderBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase)
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
        string.Join("、", Items.Select(i => $"{i.ItemCode}×{i.Quantity}＠{i.UnitPrice}"));
}

/// <summary>
/// Carriers whose ERP 貨運別 code is set in 設定 (shared by every platform; user,
/// 2026-10-11). A carrier applies when its name appears in the export's carrier text
/// (e.g. 第三方物流-新竹貨運, 7-11店出); only the ERP codes are local settings.
/// </summary>
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
