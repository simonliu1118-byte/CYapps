namespace CYInvoice.Core.Cloud;

public static class CloudCompatibility
{
    public const string ServiceName = "cyinvoice-cloud";
    public const string ApiVersion = "1";

    // Kept for source compatibility and diagnostics. This is the API 1 legacy
    // compatibility marker expected by released V2.6.5 Build 4 clients; it is
    // not the D1 migration number and must not be used as a hard compatibility gate.
    public const string SchemaVersion = "8";

    public static string Problem(CloudHealthResult health)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (!health.Reachable)
            return "無法連線到 CYInvoice Cloud API，請確認網址與網路連線。";
        if (health.ErrorCode == "INVALID_RESPONSE")
            return "伺服器有回應，但不是有效的 CYInvoice Cloud API JSON 格式。";
        if (!string.Equals(health.ServiceName, ServiceName, StringComparison.Ordinal))
            return "此網址不是相容的 CYInvoice Cloud API 服務。";
        if (!health.StorageAvailable)
            return $"Cloud API 可連線，但後端儲存服務尚未就緒（{health.ErrorCode}）。";
        if (!string.Equals(health.ApiVersion, ApiVersion, StringComparison.Ordinal))
            return $"Cloud API 版本不相容，目前為 {Display(health.ApiVersion)}，需要 {ApiVersion}。";
        if (string.IsNullOrWhiteSpace(health.SchemaVersion))
            return "Cloud API 缺少相容性資訊，請更新 Cloud 服務後再試。";
        return string.Empty;
    }

    public static string SuccessSummary(CloudHealthResult health) =>
        $"連線正常｜API {health.ApiVersion}｜相容層 {health.SchemaVersion}｜{Math.Max(0, health.RoundTripMilliseconds)} ms";

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "未提供" : value;
}
