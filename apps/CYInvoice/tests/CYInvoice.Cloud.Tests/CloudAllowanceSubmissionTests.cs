using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;

internal static class CloudAllowanceSubmissionTests
{
    public static Task RunAsync()
    {
        var original = new InvoiceRecord {
            InvoiceNumber = "AB12345678",
            InvoiceDate = "20261015",
            Environment = "test",
            BuyerIdentifier = "12345678", BuyerName = "Synthetic buyer"
        };
        AllowanceProductItem[] items = [
            new() {
                OriginalInvoiceNumber = "AB12345678",
                OriginalInvoiceDate = 20261015,
                OriginalDescription = "Original product",
                Quantity = 1, UnitPrice = 314.2857143m,
                Amount = 314, Tax = 16, TaxType = 1
            },
            new() {
                OriginalInvoiceNumber = "AB12345678",
                OriginalInvoiceDate = 20261015,
                OriginalDescription = "Discount",
                Quantity = 1, UnitPrice = -78.0952381m,
                Amount = -78, Tax = -4, TaxType = 1
            }
        ];
        var draft = CloudAllowanceSubmissionService.BuildDraft(
            "application_synthetic_01", original, new DateOnly(2026,10,16),
            248, "Partial return", items);
        Equal(248L, draft.RequestedGross);
        Equal("AB12345678", draft.OriginalInvoiceNumber);
        Equal("20261016", draft.AllowanceDay);
        Equal("test", draft.Environment);
        original.Environment=Environments.Production;
        var productionDraft=CloudAllowanceSubmissionService.BuildDraft(
            "application_production",original,new DateOnly(2026,10,16),248,"Partial return",items);
        Equal("production",productionDraft.Environment);
        Equal("production",Environments.ToCloud(Environments.Production));
        Equal(Environments.Production,Environments.FromCloud("production"));
        original.Environment=Environments.Test;
        Equal("Partial return", draft.Reason);
        using var doc = JsonDocument.Parse(draft.UnnumberedTemplateJson);
        var root = doc.RootElement;
        Equal("", root.GetProperty("AllowanceNumber").GetString());
        Equal(2, root.GetProperty("AllowanceType").GetInt32());
        Equal(236m, root.GetProperty("TotalAmount").GetDecimal());
        Equal(12m, root.GetProperty("TaxAmount").GetDecimal());
        Equal(2, root.GetProperty("ProductItem").GetArrayLength());
        if (root.TryGetProperty("applicationId", out _) ||
            root.TryGetProperty("employeeNo", out _) ||
            root.TryGetProperty("password", out _) ||
            root.TryGetProperty("workspaceId", out _))
            throw new InvalidOperationException("Frozen payload contains client authority fields");
        Equal(false, CloudAllowanceSubmissionService.HasPending(original));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "application_bad", original, new DateOnly(2026,10,16),
            249, "Partial return", items));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "application_bad", original, new DateOnly(2026,10,16),
            248, "Partial return", [items[0]]));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "application_bad", original, new DateOnly(2026,10,16),
            248, "Partial return", [new AllowanceProductItem { OriginalInvoiceNumber = "XY12345678", OriginalInvoiceDate = 20261015, OriginalDescription = "Other invoice", Quantity = 1, UnitPrice = 236, Amount = 236, Tax = 12, TaxType = 1 }]));
        Reject(() => CloudAllowanceSubmissionService.BuildDraft(
            "application_bad", original, new DateOnly(2026,10,16),
            248, "", items));
        return Task.CompletedTask;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid allowance draft was accepted");
    }
}
