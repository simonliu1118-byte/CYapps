using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public enum InvoiceProcessingKind { Upload, Void }

public sealed record InvoiceProcessingItem(InvoiceRecord Record, InvoiceProcessingKind Kind, string Message);

public sealed record InvoiceWorkQueueSnapshot(
    IReadOnlyList<InvoiceSyncIssue> UploadIssues,
    IReadOnlyList<InvoiceSyncIssue> ProcessingIssues,
    IReadOnlyList<InvoiceProcessingItem> ProcessingItems)
{
    public int ProcessingCount => ProcessingIssues.Count + ProcessingItems.Count;
}

// A read-only projection of the existing invoice/workflow owners, never a second pending store.
public sealed class InvoiceWorkQueue(LocalRepository repository)
{
    public InvoiceWorkQueueSnapshot Load()
    {
        var settings = repository.Settings.LoadOrCreate();
        var seller = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (seller.Length == 0) return new([], [], []);
        var issues = new InvoiceSyncIssueStore(repository.DataDirectory).All(settings.Environment + "|" + seller);
        var records = repository.Invoices.LoadOrCreate()
            .Where(record => record.Environment == settings.Environment &&
                (record.SellerInvoice.Trim().Length == 0 || record.SellerInvoice.Trim() == seller)).ToArray();
        var allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        var processingIssues = new List<InvoiceSyncIssue>();
        foreach (var issue in issues.Where(issue => issue.ResolvedUtc is null &&
                     issue.IssueType == InvoiceAllowanceIssueTypes.ManualReview))
        {
            var matches = records.Where(record => Matches(issue, record)).ToArray();
            if (matches.Length != 1) continue; // Ambiguity is never normal processing.
            var review = allowanceWorkflow.ManualReviewFor(matches[0]);
            if (review is { AwaitingConfirmation: true, ConfirmationProblem: false })
                processingIssues.Add(issue);
        }
        var processingIds = processingIssues.Select(issue => issue.Id).ToHashSet();
        var uploadIssues = issues.Where(issue => !processingIds.Contains(issue.Id)).ToArray();
        var items = new List<InvoiceProcessingItem>();
        foreach (var record in records)
        {
            if (record.InvoiceNumber.Trim().Length == 0 ||
                record.InvoiceState is InvoiceStates.Failed or InvoiceStates.Unknown or InvoiceStates.Changing) continue;
            // Failed lookups and other unresolved technical problems take precedence over stale status.
            if (uploadIssues.Any(issue => issue.ResolvedUtc is null && Matches(issue, record) &&
                issue.IssueType != InvoiceVoidIssueTypes.ManualReview &&
                issue.IssueType != InvoiceAllowanceIssueTypes.ManualReview &&
                issue.IssueType != InvoiceAllowanceVoidIssueTypes.ManualReview)) continue;
            if (InvoiceVoidService.HasPendingMarker(record))
            {
                if (InvoiceVoidService.IsOfficiallyPending(record))
                    items.Add(new(record, InvoiceProcessingKind.Void, "作廢已送出，等待光貿確認完成"));
                continue;
            }
            if (record.InvoiceState == InvoiceStates.Opened && IsPendingUpload(record.UploadStatus))
                items.Add(new(record, InvoiceProcessingKind.Upload, UploadMessage(record.UploadStatus)));
        }
        return new(uploadIssues, processingIssues, items);
    }

    public static bool IsPendingUpload(int status) => status is UploadStatuses.Pending or UploadStatuses.Uploading or
        UploadStatuses.Uploaded or UploadStatuses.Processing or UploadStatuses.Confirming;

    private static string UploadMessage(int status) => status switch
    {
        UploadStatuses.Pending => "等待光貿處理",
        UploadStatuses.Uploading => "光貿上傳中，等待電子發票平台處理",
        UploadStatuses.Uploaded => "光貿已上傳，等待電子發票平台處理",
        _ => "等待電子發票平台處理完成",
    };

    private static bool Matches(InvoiceSyncIssue issue, InvoiceRecord record) =>
        (issue.InvoiceNumber.Trim().Length != 0 && string.Equals(issue.InvoiceNumber.Trim(), record.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        (issue.OrderId.Trim().Length != 0 && new[] { record.ApiOrderId, record.OrderId, record.OriginalOrderId }
            .Any(order => string.Equals(issue.OrderId.Trim(), order.Trim(), StringComparison.Ordinal)));
}
