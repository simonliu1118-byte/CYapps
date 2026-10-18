namespace CYInvoice.Core.Amego;

// Immutable snapshot of the user's edited allowance detail. At V3.1.2 this
// is local draft metadata only; it does NOT perform a g0401 operation.
public sealed record AllowanceProductItem
{
    public string OriginalInvoiceNumber { get; init; } = string.Empty;
    public int OriginalInvoiceDate { get; init; }
    public string OriginalDescription { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; } // Always tax-exclusive.
    public decimal Amount { get; init; }
    public decimal Tax { get; init; }
    public int TaxType { get; init; }
}
