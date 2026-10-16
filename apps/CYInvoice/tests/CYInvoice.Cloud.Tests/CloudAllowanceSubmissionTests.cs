using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;

internal static class CloudAllowanceSubmissionTests
{
    public static Task RunAsync()
    {
        var original=new InvoiceRecord {
            InvoiceNumber="AB12345678",
            BuyerIdentifier="12345678", BuyerName="Synthetic buyer"
        };
        var items=new AllowanceProductItem[] {
            new() {
                OriginalInvoiceNumber="AB12345678",
                OriginalInvoiceDate=20261015,OriginalDescription="Original product",
                Quantity=1,UnitPrice=314.2857143m,Amount=314,Tax=16,TaxType=1
            },
            new() {
                OriginalInvoiceNumber="AB12345678",
                OriginalInvoiceDate=20261015,OriginalDescription="Employee-discount",
                Quantity=1,UnitPrice=-78.0952381m,Amount=-78,Tax=-4,TaxType=1
            }
        };
        var draft=CloudAllowanceSubmissionService.BuildDraft(
            "wi_test_allowance123",original,new DateOnly(2026,10,16),
            248,"Partial return",items);
        Equal(248,draft.RequestedGrossAmount);
        Equal("AB12345678",draft.UnnumberedTemplate.ProductItem[0].OriginalInvoiceNumber);
        Equal("",draft.UnnumberedTemplate.AllowanceNumber);
        Equal("20261016",draft.UnnumberedTemplate.AllowanceDate);
        Equal(236m,draft.UnnumberedTemplate.TotalAmount);
        Equal(12m,draft.UnnumberedTemplate.TaxAmount);
        Equal(2,draft.UnnumberedTemplate.ProductItem.Count);
        Equal(false,CloudAllowanceSubmissionService.HasPending(original));

        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "wi_bad",original,new DateOnly(2026,10,16),249,"Partial return",items));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "wi_bad",original,new DateOnly(2026,10,16),248,"Partial return",
            [items[0]]));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "wi_bad",original,new DateOnly(2026,10,16),248,"Partial return",
            [new AllowanceProductItem {
                OriginalInvoiceNumber="XY12345678",OriginalInvoiceDate=20261015,
                OriginalDescription="Other original",Quantity=1,UnitPrice=236,
                Amount=236,Tax=12,TaxType=1
            }]));
        return Task.CompletedTask;
    }

    private static void Equal<T>(T expected,T actual)
    {
        if(!EqualityComparer<T>.Default.Equals(expected,actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch(InvalidOperationException){ return; }
        throw new InvalidOperationException("Invalid allowance draft was accepted");
    }
}
