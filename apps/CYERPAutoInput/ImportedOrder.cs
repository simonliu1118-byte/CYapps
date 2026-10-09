namespace CYERPAutoInput;

internal sealed record ImportedItem(string ItemCode, string Quantity, string UnitPrice);

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

    /// <summary>What the preview must fix before this order can be entered; empty when fine.</summary>
    public string ValidationError()
    {
        if (BuyerTaxId.Length > 0 && InvoiceName.Trim().Length == 0)
            return "有發票開立統編，請填寫發票的客戶全名。";
        if (InvoiceDate.Trim().Length > 0 && !InputRules.TryNormalizeValidDate(InvoiceDate.Trim(), out _))
            return "發票日期必須是有效日期（YYYY/MM/DD）。";
        return string.Empty;
    }

    public string ItemSummary() =>
        string.Join("、", Items.Select(i => $"{i.ItemCode}×{i.Quantity}＠{i.UnitPrice}"));
}
