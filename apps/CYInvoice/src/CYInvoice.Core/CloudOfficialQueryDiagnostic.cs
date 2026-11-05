using System.Text.Json;

namespace CYInvoice.Core.Cloud;

// A fixed diagnostic vocabulary, never a serialized provider response or Error.
public sealed record CloudOfficialQueryDiagnostic(string Endpoint, string? Phase, string? FailureKind,
    string? ErrorName, string? TransportCode, int? ElapsedMs, bool? ResponseReceived,
    int? HttpStatus, int? ProviderCode)
{
    internal static CloudOfficialQueryDiagnostic? Read(JsonElement error)
    {
        if (!error.TryGetProperty("officialQuery", out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        string? Choice(string key, params string[] choices) => value.TryGetProperty(key, out var item) &&
            item.ValueKind == JsonValueKind.String && choices.Contains(item.GetString()) ? item.GetString() : null;
        int? Number(string key, int min, int max) => value.TryGetProperty(key, out var item) &&
            item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) && number >= min && number <= max ? number : null;
        bool? Flag(string key) => value.TryGetProperty(key, out var item) &&
            item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
        if (Choice("endpoint", "invoice_query") is not { } endpoint) return null;
        return new(endpoint,
            Choice("phase", "input_validation", "request", "response_read", "response_parse", "response_validation"),
            Choice("failureKind", "unknown", "timeout", "aborted", "dns", "tls", "redirect", "connection", "http", "response"),
            Choice("errorName", "Unknown", "Error", "TypeError", "TimeoutError", "AbortError", "SyntaxError"),
            Choice("transportCode", "ENOTFOUND", "EAI_AGAIN", "ECONNREFUSED", "ECONNRESET", "ETIMEDOUT",
                "UND_ERR_CONNECT_TIMEOUT", "CERT_HAS_EXPIRED", "DEPTH_ZERO_SELF_SIGNED_CERT",
                "ERR_TLS_CERT_ALTNAME_INVALID", "UNABLE_TO_VERIFY_LEAF_SIGNATURE"),
            Number("elapsedMs", 0, 300000), Flag("responseReceived"), Number("httpStatus", 100, 599),
            Number("providerCode", 0, 9999999));
    }
}
