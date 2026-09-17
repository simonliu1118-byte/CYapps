using System.Text.Json.Serialization;

namespace CYInvoice.Core.Storage;

public sealed class Settings
{
    [JsonPropertyName("environment")] public string Environment { get; set; } = Environments.Test;
    [JsonPropertyName("prod_invoice")] public string ProductionInvoice { get; set; } = string.Empty;
    [JsonPropertyName("prod_app_key_enc")] public string ProductionAppKeyEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("mo_password_enc")] public string MoPasswordEncrypted { get; set; } = string.Empty;
    [JsonPropertyName("password_salt")] public string PasswordSalt { get; set; } = string.Empty;
    [JsonPropertyName("password_hash")] public string PasswordHash { get; set; } = string.Empty;
    [JsonPropertyName("admin_password_set")] public bool AdminPasswordSet { get; set; }
    [JsonPropertyName("invoice_printer_name")] public string InvoicePrinterName { get; set; } = string.Empty;
}

public interface ISecretProtector
{
    string Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(string ciphertext);
}
