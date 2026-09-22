using System.Text.Json.Serialization;

namespace CYInvoice.Core.Storage;

public static class CloudModes
{
    public const string LocalOnly = "local_only";
    public const string CloudTransition = "cloud_transition";
    public const string CloudPreferred = "cloud_preferred";
}

public sealed record CloudPendingBootstrapState(
    string BaseUrl,
    string WorkspaceDisplayName,
    string DeviceDisplayName,
    DateTimeOffset StartedAtUtc,
    string DeviceToken);

public sealed record CloudPendingDeviceJoinState(
    string BaseUrl,
    string DeviceDisplayName,
    DateTimeOffset StartedAtUtc,
    string DeviceToken);

public sealed class Settings
{
    [JsonPropertyName("environment")] public string Environment { get; set; } = Environments.Test;
    [JsonPropertyName("prod_invoice")] public string ProductionInvoice { get; set; } = string.Empty;
    [JsonPropertyName("prod_app_key_enc")] public string ProductionAppKeyEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("mo_password_enc")] public string MoPasswordEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("invoice_printer_name")] public string InvoicePrinterName { get; set; } = string.Empty;
    [JsonPropertyName("cloud_mode")] public string CloudMode { get; set; } = CloudModes.LocalOnly;
    [JsonPropertyName("cloud_base_url")] public string CloudBaseUrl { get; set; } = string.Empty;
    [JsonPropertyName("cloud_workspace_id")] public string CloudWorkspaceId { get; set; } = string.Empty;
    [JsonPropertyName("cloud_device_id")] public string CloudDeviceId { get; set; } = string.Empty;
    [JsonPropertyName("cloud_device_token_enc")] public string CloudDeviceTokenEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_bootstrap_url")] public string CloudPendingBootstrapUrl { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_bootstrap_workspace_name")] public string CloudPendingBootstrapWorkspaceName { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_bootstrap_device_name")] public string CloudPendingBootstrapDeviceName { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_bootstrap_started_utc")] public string CloudPendingBootstrapStartedUtc { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_bootstrap_token_enc")] public string CloudPendingBootstrapTokenEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_device_join_url")] public string CloudPendingDeviceJoinUrl { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_device_join_device_name")] public string CloudPendingDeviceJoinDeviceName { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_device_join_started_utc")] public string CloudPendingDeviceJoinStartedUtc { get; set; } = string.Empty;
    [JsonPropertyName("cloud_pending_device_join_token_enc")] public string CloudPendingDeviceJoinTokenEncrypted { get; set; } = string.Empty;
}

public interface ISecretProtector
{
    string Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(string ciphertext);
}
