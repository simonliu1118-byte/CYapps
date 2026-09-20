using System.Text.Json.Serialization;

namespace CYInvoice.Core.Storage;

public static class CloudModes
{
    public const string LocalOnly = "local_only";
    public const string CloudPreferred = "cloud_preferred";
}

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
}

public interface ISecretProtector
{
    string Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(string ciphertext);
}
