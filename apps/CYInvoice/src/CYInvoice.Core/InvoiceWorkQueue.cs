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
        var byNumber = records.ToLookup(record => record.InvoiceNumber.Trim(), StringComparer.OrdinalIgnoreCase);
        var byOrder = records.SelectMany(record => new[] { record.ApiOrderId, record.OrderId, record.OriginalOrderId }
            .Select(order => (Order: order.Trim(), Record: record)))
            .ToLookup(pair => pair.Order, pair => pair.Record, StringComparer.Ordinal);
        IEnumerable<InvoiceRecord> MatchingRecords(InvoiceSyncIssue issue)
        {
            var number = issue.InvoiceNumber.Trim();
            var order = issue.OrderId.Trim();
            return (number.Length == 0 ? Enumerable.Empty<InvoiceRecord>() : byNumber[number])
                .Concat(order.Length == 0 ? Enumerable.Empty<InvoiceRecord>() : byOrder[order]).Distinct();
        }
        var allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        var allowanceVoidWorkflow = new EmployeeAllowanceVoidWorkflowService(repository);
        var processingIssues = new List<InvoiceSyncIssue>();
        foreach (var issue in issues.Where(issue => issue.ResolvedUtc is null))
        {
            if (issue.IssueType.StartsWith("cloud_allowance_", StringComparison.Ordinal))
            {
                if (issue.IssueType.EndsWith("_processing", StringComparison.Ordinal)) processingIssues.Add(issue);
                continue;
            }
            // Use the existing Processing entry for paper, manager and
            // official waits. Missing or ambiguous local data remains an issue.
            if (issue.IssueType is not (InvoiceVoidIssueTypes.ManualReview or
                InvoiceAllowanceIssueTypes.ManualReview or
                InvoiceAllowanceVoidIssueTypes.ManualReview)) continue;
            var matches = MatchingRecords(issue).ToArray();
            if (matches.Length != 1) continue;
            if (issue.IssueType == InvoiceAllowanceIssueTypes.ManualReview)
            {
                var review = allowanceWorkflow.ManualReviewFor(matches[0]);
                if (review is null || review.ConfirmationProblem == true) continue;
            }
            if (issue.IssueType == InvoiceAllowanceVoidIssueTypes.ManualReview &&
                allowanceVoidWorkflow.ManualReviewFor(matches[0])?.ConfirmationProblem != false) continue;
            processingIssues.Add(issue);
        }
        // Each allowance request is its own operation. An invoice-scoped
        // historical error cannot hide a different partial allowance/void.
        var processingIds = processingIssues.Select(issue => issue.Id).ToHashSet();
        var uploadIssues = issues.Where(issue => !processingIds.Contains(issue.Id)).ToArray();
        var blockedRecords = processingIssues.Concat(uploadIssues.Where(issue => issue.ResolvedUtc is null &&
                issue.IssueType is not (InvoiceVoidIssueTypes.ManualReview or
                    InvoiceAllowanceIssueTypes.ManualReview or InvoiceAllowanceVoidIssueTypes.ManualReview)))
            .SelectMany(MatchingRecords).ToHashSet();
        var items = new List<InvoiceProcessingItem>();
        foreach (var record in records)
        {
            if (record.InvoiceNumber.Trim().Length == 0 ||
                record.InvoiceState is InvoiceStates.Failed or InvoiceStates.Unknown or InvoiceStates.Changing) continue;
            // Failed lookups and other unresolved technical problems take precedence over stale status.
            if (blockedRecords.Contains(record)) continue;
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

}
