using System.Text.Json;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceAllowanceMetadata
{
    private const string OfficialAllowancesKey = "cyinvoice_official_allowances";

    public static void ApplyQuery(InvoiceRecord record, IReadOnlyList<InvoiceAllowanceResult> allowances)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(allowances);
        if (allowances.Count == 0)
        {
            if (record.ExtensionData is null) return;
            record.ExtensionData.Remove(OfficialAllowancesKey);
            if (record.ExtensionData.Count == 0) record.ExtensionData = null;
            return;
        }

        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[OfficialAllowancesKey] = JsonSerializer.SerializeToElement(allowances);
    }

    public static IReadOnlyList<InvoiceAllowanceResult> ReadOfficial(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(OfficialAllowancesKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        try
        {
            return value.Deserialize<List<InvoiceAllowanceResult>>() ?? [];
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("官方折讓資料格式錯誤", error);
        }
    }
}
