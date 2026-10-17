using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class CloudCompanySettingsUsageTests
{
    public static Task RunAsync()
    {
        var local = new Settings {
            ProductionInvoice = "12345678",
            ProductionAppKeyEncrypted = "TEST_FAKE_PROTECTED_APPKEY",
            MoPasswordEncrypted = "TEST_FAKE_PROTECTED_MO"
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
        var card = new CloudCompanySettingsStatus("12345678", 5, true, true);
        var credentials = new CloudCompanyProvision("12345678", 5,
            "TEST_SYNTHETIC_APPKEY", "TEST_SYNTHETIC_MO_PASSWORD");
        Check(credentials.Matches(card), "Fresh complete credentials match exact status.");
        Check(!credentials.Matches(card with {Revision=6}), "Stale revision rejected.");
        Check(!credentials.Matches(card with {SellerInvoice="87654321"}), "Wrong seller rejected.");
        Check(!credentials.Matches(card with {HasAmegoAppKey=false}), "Unconfigured App Key rejected.");
        Check(!new CloudCompanyProvision("12345678",5,string.Empty,"TEST_MO").Matches(card),
            "Missing AMEGO key cannot be installed.");
        Check(!new CloudCompanyProvision("12345678",5,"TEST_KEY",string.Empty).Matches(card),
            "Missing MO password cannot be installed.");
        Check(!new CloudCompanyProvision("12345678",5,"TEST_KEY", "TEST_BAD\u0001MO").Matches(card),
            "Control characters cannot enter Windows DPAPI settings.");

        // Non-secret provenance prevents silent "synced" claims after a cloud
        // update, manual override, or a different Device binding.
        var dir = Path.Combine(Path.GetTempPath(), "cyinvoice-v30-card-source-",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir, new TestProtector());
            var stored = store.LoadOrCreate();
            stored.CloudMode = CloudModes.CloudPreferred;
            stored.CloudIdentityProvider = "CYID";
            stored.CloudEmployeeAuthorityReady = true;
            stored.CyIdBindingEncrypted = "TEST_SYNTHETIC_DPAPI_BINDING";
            stored.CloudBaseUrl = "https://cloud.example.test/";
            stored.CloudWorkspaceId = "ws_synthetic";
            stored.CloudDeviceId = "dev_synthetic";
            store.SetCloudDeviceToken(stored, "cydev_" + new string('a', 64));
            stored.ProductionInvoice = "12345678";
            store.SetProductionAppKey(stored, "TEST_SYNTHETIC_APPKEY");
            store.SetMoPassword(stored, "TEST_SYNTHETIC_MO_PASSWORD");
            store.StampCloudCompanyProvision(stored, 5);
            store.Save(stored);
            var persisted = store.LoadOrCreate();
            Check(CloudCompanySettingsUsage.Inspect(card, persisted).CompanyCardAppliedToCurrentDevice,
                "Matching DPAPI settings, company revision and Device ID are remembered.");
            Check(CloudCompanySettingsUsage.Inspect(card with {Revision=6}, persisted).CompanyCardNeedsReview,
                "Cloud revision changes invalidate the remembered synchronisation claim.");
            persisted.CloudDeviceId = "dev_other";
            Check(CloudCompanySettingsUsage.Inspect(card, persisted).CompanyCardNeedsReview,
                "Company provenance cannot be reused by another Device.");
            persisted.CloudDeviceId = "dev_synthetic";
            store.SetProductionAppKey(persisted, "TEST_LOCAL_OVERRIDE");
            Check(persisted.CloudCompanyAppliedRevision == 0,
                "A manual App Key edit must erase company source metadata.");
            store.StampCloudCompanyProvision(persisted, 6);
            store.SetMoPassword(persisted, "TEST_LOCAL_MO_OVERRIDE");
            Check(persisted.CloudCompanyAppliedRevision == 0,
                "A manual MO password edit must erase company source metadata.");
            store.StampCloudCompanyProvision(persisted, 7);
            store.ClearCloudIdentity(persisted);
            Check(persisted.CloudCompanyAppliedRevision == 0 &&
                  persisted.CloudCompanyAppliedDeviceId.Length == 0,
                "Cloud identity removal must discard former Device provenance.");
        }
        finally { Directory.Delete(dir, recursive:true); }
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
