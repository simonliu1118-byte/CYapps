using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

// Privacy-safe, stable local exception reference. The original Device
// credential is DPAPI protected and used only as an HMAC key locally.
// No buyer, invoice number, order number, or message leaves this mapper.
public sealed record CloudLocalWorkItemReference(string ItemId, string ReferenceHash, string Kind, string? IssueCode);

public static class CloudLocalWorkItemReferences
{
    public static CloudLocalWorkItemReference FromLocalIssue(InvoiceSyncIssue issue, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Id <= 0 || string.IsNullOrWhiteSpace(issue.AccountKey) ||
            deviceToken is null ||
            !Regex.IsMatch(deviceToken, @"^cydev_[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid local Work Item source or Device credential.");
        var code = issue.IssueType switch
        {
            InvoiceVoidIssueTypes.ManualReview => null,
            InvoiceSyncIssueTypes.InvoiceListFailed => InvoiceSyncIssueTypes.InvoiceListFailed,
            InvoiceSyncIssueTypes.QueryFailed => InvoiceSyncIssueTypes.QueryFailed,
            InvoiceSyncIssueTypes.RemoteNotFound => InvoiceSyncIssueTypes.RemoteNotFound,
            InvoiceSyncIssueTypes.UnknownRemoteNotFound => InvoiceSyncIssueTypes.UnknownRemoteNotFound,
            InvoiceSyncIssueTypes.ReconciliationFailed => InvoiceSyncIssueTypes.ReconciliationFailed,
            InvoiceSyncIssueTypes.AmbiguousMatch => InvoiceSyncIssueTypes.AmbiguousMatch,
            InvoiceSyncIssueTypes.LocalWriteFailed => InvoiceSyncIssueTypes.LocalWriteFailed,
            _ => throw new ArgumentException("Only V3.1 non-allowance local exceptions are supported.", nameof(issue))
        };
        var source = "cyinvoice.work-item.local.v1\0" +
            issue.AccountKey.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" + issue.AccountKey + "\0" +
            issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + issue.IssueType;
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.ASCII.GetBytes(deviceToken.ToLowerInvariant()), Encoding.UTF8.GetBytes(source)));
        return new CloudLocalWorkItemReference("wi_" + hash[..40], hash,
            code is null ? "VOID" : "UPLOAD_ISSUE", code);
    }
}
