using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

// Read-only comparison of CYID company-card metadata and existing Windows
// settings. Does not decrypt, copy or infer equality of secrets: an encrypted
// company-card "configured" flag cannot replace a usable local DPAPI key.
public sealed record CloudCompanySettingsUsage(
    bool HasCloudCard,
    bool HasLocalSellerInvoice,
    bool SellerInvoiceMatches,
    bool LocalAmegoKeyConfigured,
    bool LocalMoPasswordConfigured,
    bool CompanyCardAppliedToCurrentDevice,
    bool CompanyCardNeedsReview)
{
    public static CloudCompanySettingsUsage Inspect(
        CloudCompanySettingsStatus? card, Settings local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var seller = local.ProductionInvoice.Trim();
        return new CloudCompanySettingsUsage(
            card is not null,
            seller.Length == 8 && seller.All(char.IsAsciiDigit),
            card is not null && seller == card.SellerInvoice,
            !string.IsNullOrWhiteSpace(local.ProductionAppKeyEncrypted),
            !string.IsNullOrWhiteSpace(local.MoPasswordEncrypted),
            card is not null && local.CloudCompanyAppliedRevision == card.Revision &&
                local.CloudCompanyAppliedRevision > 0 &&
                local.CloudCompanyAppliedWorkspaceId == local.CloudWorkspaceId &&
                local.CloudCompanyAppliedDeviceId == local.CloudDeviceId &&
                local.CloudCompanyAppliedSellerInvoice == card.SellerInvoice &&
                seller == card.SellerInvoice &&
                !string.IsNullOrWhiteSpace(local.ProductionAppKeyEncrypted) &&
                !string.IsNullOrWhiteSpace(local.MoPasswordEncrypted),
            local.CloudCompanyAppliedRevision > 0 &&
                (card is null || local.CloudCompanyAppliedRevision != card.Revision ||
                 local.CloudCompanyAppliedWorkspaceId != local.CloudWorkspaceId ||
                 local.CloudCompanyAppliedDeviceId != local.CloudDeviceId ||
                 local.CloudCompanyAppliedSellerInvoice != card.SellerInvoice ||
                 seller != card.SellerInvoice));
    }
}
