using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class CloudCompanySettingsUsageTests
{
    public static Task RunAsync()
    {
        var local = new Settings {
            ProductionInvoice = "12345678",
            ProductionAppKeyEncrypted = "SYNTHETIC_DPAPI_CIPHERTEXT_ONLY",
            MoPasswordEncrypted = "SYNTHETIC_MO_CIPHERTEXT_ONLY"
        };
        var before = (local.ProductionInvoice, local.ProductionAppKeyEncrypted, local.MoPasswordEncrypted);
        var missing = CloudCompanySettingsUsage.Inspect(null, local);
        Check(!missing.HasCloudCard && !missing.SellerInvoiceMatches &&
            missing.LocalAmegoKeyConfigured && missing.LocalMoPasswordConfigured,
            "No cloud card must not change or mask local settings.");
        var matching = CloudCompanySettingsUsage.Inspect(
            new CloudCompanySettingsStatus("12345678", 1, true, true), local);
        Check(matching.HasCloudCard && matching.SellerInvoiceMatches, "Equal seller numbers detected.");
        var different = CloudCompanySettingsUsage.Inspect(
            new CloudCompanySettingsStatus("87654321", 2, true, true), local);
        Check(different.HasCloudCard && !different.SellerInvoiceMatches, "Mismatch must be visible.");
        Check((local.ProductionInvoice, local.ProductionAppKeyEncrypted, local.MoPasswordEncrypted)
            == before, "Read-only comparison may not modify any DPAPI data.");
        local.ProductionAppKeyEncrypted = string.Empty;
        local.MoPasswordEncrypted = string.Empty;
        var remoteOnly = CloudCompanySettingsUsage.Inspect(
            new CloudCompanySettingsStatus("12345678", 3, true, true), local);
        Check(!remoteOnly.LocalAmegoKeyConfigured && !remoteOnly.LocalMoPasswordConfigured,
            "Remote configured flags are not Windows-local usable credentials.");
        local.ProductionInvoice = string.Empty;
        var noLocalSeller = CloudCompanySettingsUsage.Inspect(
            new CloudCompanySettingsStatus("12345678", 4, true, true), local);
        Check(!noLocalSeller.HasLocalSellerInvoice && !noLocalSeller.SellerInvoiceMatches,
            "A missing Windows seller number must not be treated as ready.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
