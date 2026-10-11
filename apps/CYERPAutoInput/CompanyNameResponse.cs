using System.Text.Json;

namespace CYERPAutoInput;

/// <summary>Pure parts of <see cref="CompanyNameLookup"/> (no network), unit tested.</summary>
internal static class CompanyNameResponse
{
    /// <summary>An 8-digit 統一編號.</summary>
    public static bool IsBan(string? value) => value is { Length: 8 } && value.All(char.IsAsciiDigit);

    /// <summary>The field of the first record of a GCIS JSON array; empty for no match (the API answers an empty body).</summary>
    internal static string ParseName(string json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
        foreach (var record in document.RootElement.EnumerateArray())
            if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;
        return string.Empty;
    }
}
