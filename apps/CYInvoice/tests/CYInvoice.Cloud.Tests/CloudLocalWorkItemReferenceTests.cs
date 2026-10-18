using CYInvoice.Core.Cloud;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class CloudLocalWorkItemReferenceTests
{
    public static void Run()
    {
        var token = "cydev_" + new string('a',64);
        var now = DateTimeOffset.UtcNow;
        var issue = new InvoiceSyncIssue(123,"production|synthetic-tax","ZZ12345678",
            "PRIVATE-SYNTHETIC-ORDER",InvoiceSyncIssueTypes.QueryFailed,
            "synthetic buyer personal details",now,null);
        var first = CloudLocalWorkItemReferences.FromLocalIssue(issue,token);
        var withoutDetails = CloudLocalWorkItemReferences.FromLocalIssue(issue with {
            InvoiceNumber = "ANOTHER-PRIVATE-INVOICE", OrderId = "ANOTHER-PRIVATE-ORDER",
            Message = "other private customer" },token);
        Check(first == withoutDetails,"fingerprint cannot depend on invoice/customer details");
        Check(first.Kind == "UPLOAD_ISSUE" && first.IssueCode == "query_failed",
            "upload issue must use canonical non-allowance code");
        Check(first.ItemId.Length==43 && first.ItemId.StartsWith("wi_",StringComparison.Ordinal) &&
            first.ReferenceHash.Length==64 &&
            first.ReferenceHash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "IDs and hash must satisfy Cloud Work Item validation");
        var serialized=first.ItemId+first.ReferenceHash;
        Check(!serialized.Contains(issue.InvoiceNumber,StringComparison.Ordinal) &&
            !serialized.Contains(issue.OrderId,StringComparison.Ordinal) &&
            !serialized.Contains("synthetic-tax",StringComparison.Ordinal),
            "no original business identifier may be in a remote Work Item reference");
        Check(CloudLocalWorkItemReferences.FromLocalIssue(issue with { Id=124 },token)!=first,
            "distinct local source IDs must not collide");
        Check(CloudLocalWorkItemReferences.FromLocalIssue(issue,"cydev_"+new string('b',64))!=first,
            "one Device must not reconstruct another Device's source reference");
        var paper=CloudLocalWorkItemReferences.FromLocalIssue(
            issue with {IssueType=InvoiceVoidIssueTypes.ManualReview},token);
        Check(paper.Kind=="VOID" && paper.IssueCode is null,"manual void cannot imply official provider void");
        foreach(var unsupported in new[]{"allowance_manual_review","allowance_void_manual_review"})
        {
            var rejected=false;
            try { CloudLocalWorkItemReferences.FromLocalIssue(issue with {IssueType=unsupported},token); }
            catch(ArgumentException) { rejected=true; }
            Check(rejected,"V3.2 allowance cases must fail closed");
        }
    }
    private static void Check(bool valid,string reason)
    {
        if(!valid)throw new InvalidOperationException("V3.1 local source reference: "+reason);
    }
}
