namespace CYInvoice.Core.Amego;

// Immutable snapshot of the user's edited allowance detail. At V3.1.2 this
// is local draft metadata only; it does NOT perform a g0401 operation.
public sealed record AllowanceProductItem
{
    public string OriginalInvoiceNumber { get; init; } = string.Empty;
    public int OriginalInvoiceDate { get; init; }
    public string OriginalDescription { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    // Canonical price sent to AMEGO is ALWAYS tax-exclusive, even when the
    // employee typed a tax-inclusive unit price in the editor.
    public decimal UnitPrice { get; init; }
    // Exact original gross-unit input for taxable lines, if the user chose
    // gross-entry mode. Preserves gross-first rounding for Core revalidation.
    // Null means the entered unit price was already tax-exclusive.
    public decimal? GrossUnitPrice { get; init; }
    public decimal Amount { get; init; }
    public decimal Tax { get; init; }
    public int TaxType { get; init; }
}
