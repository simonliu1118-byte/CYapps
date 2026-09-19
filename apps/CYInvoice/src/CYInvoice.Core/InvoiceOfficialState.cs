using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceOfficialState
{
    public static void ApplyList(InvoiceRecord record, long cancelDate)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (cancelDate > 0)
        {
            ApplyVoided(record, cancelDate);
            return;
        }

        InvoiceOfficialMetadata.SetCancelDate(record, 0);
        record.InvoiceState = ResolveList(record, cancelDate);
    }

    public static void ApplyQuery(InvoiceRecord record, QueryResult query)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(query);
        if (query.CancelDate > 0)
        {
            ApplyVoided(record, query.CancelDate);
            return;
        }

        InvoiceOfficialMetadata.SetCancelDate(record, 0);
        if (query.VoidPending) InvoiceVoidService.MarkPendingMarker(record);
        record.InvoiceState = ResolveQuery(record, query);
    }

    internal static string ResolveList(InvoiceRecord record, long cancelDate) =>
        cancelDate > 0
            ? InvoiceStates.Voided
            : InvoiceVoidService.HasPendingMarker(record)
                ? InvoiceStates.OpenedWaitingVoid
                : InvoiceStates.Opened;

    internal static string ResolveQuery(InvoiceRecord record, QueryResult query) =>
        query.CancelDate > 0
            ? InvoiceStates.Voided
            : query.VoidPending || InvoiceVoidService.HasPendingMarker(record)
                ? InvoiceStates.OpenedWaitingVoid
                : InvoiceStates.Opened;

    private static void ApplyVoided(InvoiceRecord record, long cancelDate)
    {
        record.InvoiceState = InvoiceStates.Voided;
        InvoiceVoidService.ClearPendingMarker(record);
        InvoiceOfficialMetadata.SetCancelDate(record, cancelDate);
    }
}
