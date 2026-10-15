using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

// Read-only, single-invoice official inspection for a manager's allowance
// review dialog. Does not require the manager's Windows Device to have the
// applicant's InvoiceRecord in local SQLite. It does NOT approve, allocate a
// number, or submit AMEGO g0401/g0501.
// The authorized caller must construct the gateway using this Workspace's
// server-authorized company configuration, not client-supplied credentials.
public sealed record AllowanceReviewInspection(
    string InvoiceNumber,
    string InvoiceDate,
    string InvoiceType,
    int InvoiceUploadStatus,
    long InvoiceCancelDate,
    string InvoiceTotalAmount,
    bool InvoiceVoidPending,
    bool AllowanceSummaryPresent,
    IReadOnlyList<InvoiceAllowanceResult> ExistingAllowances,
    DateTimeOffset CheckedAtUtc)
{
    public bool InvoiceAlreadyVoided => InvoiceCancelDate > 0;
    public bool HasUnsettledOfficialState =>
        InvoiceUploadStatus != UploadStatuses.Complete ||
        InvoiceVoidPending ||
        ExistingAllowances.Any(item => item.InvoiceStatus != UploadStatuses.Complete);
    public bool NeedsPerInvoiceAmountDetail => ExistingAllowances.Count != 0;
    public bool RequiresManualVerification =>
        InvoiceAlreadyVoided || HasUnsettledOfficialState ||
        !AllowanceSummaryPresent || NeedsPerInvoiceAmountDetail;
    // A displayed official summary must never be mistaken for an approval:
    // especially when an allowance has ProductItem from multiple invoices.
}

public sealed class AllowanceReviewInspectionService(
    IAmegoGateway gateway, Func<DateTimeOffset>? now = null)
{
    private readonly IAmegoGateway gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    private readonly Func<DateTimeOffset> now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<AllowanceReviewInspection> InspectAsync(
        string invoiceNumber, CancellationToken cancellationToken = default)
    {
        invoiceNumber = (invoiceNumber ?? string.Empty).Trim();
        if (invoiceNumber.Length != 10 ||
            invoiceNumber.Take(2).Any(ch => ch is < 'A' or > 'Z') ||
            invoiceNumber.Skip(2).Any(ch => ch is < '0' or > '9'))
            throw new ArgumentException("Original invoice number must be two letters and eight digits",
                nameof(invoiceNumber));

        // Exactly ONE scoped invoice_query, not a full allowance_list sweep.
        var response = await gateway.QueryByInvoiceNumberAsync(invoiceNumber, cancellationToken)
            .ConfigureAwait(false);
        if (response.Code != 0 || response.Data is null)
            throw new InvalidDataException("Could not confirm official invoice status");
        var official = response.Data;
        if (!string.Equals(official.InvoiceNumber, invoiceNumber, StringComparison.Ordinal))
            throw new InvalidDataException("Official query returned another invoice number");

        // Keep the raw per-allowance amounts as display information only:
        // a single allowance can reference multiple ORIGINAL invoice numbers.
        // Calculating remaining allowance amount requires item-level checks
        // and a separate final preflight before a manager may approve g0401.
        return new AllowanceReviewInspection(invoiceNumber, official.InvoiceDate,
            official.InvoiceType, official.InvoiceStatus, official.CancelDate,
            official.TotalAmount, official.VoidPending, official.AllowanceSummaryPresent,
            official.Allowances.ToArray(), now().ToUniversalTime());
    }
}
