namespace CYInvoice.Core.Cloud;

public static class CloudCompatibility
{
    public const string ServiceName = "cyinvoice-cloud";
    public const string ApiVersion = "1";
    public const string SchemaVersion = "7";

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
        if (!string.Equals(health.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            return $"Cloud schema 版本不相容，目前為 {Display(health.SchemaVersion)}，需要 {SchemaVersion}。";
        return string.Empty;
    }

    public static string SuccessSummary(CloudHealthResult health) =>
        $"連線正常｜API {health.ApiVersion}｜Schema {health.SchemaVersion}｜{Math.Max(0, health.RoundTripMilliseconds)} ms";

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "未提供" : value;
}
