using System.Globalization;
using System.Text.Json;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceOfficialMetadata
{
    private const string CancelDateKey = "cyinvoice_cancel_date";

    public static void SetCancelDate(InvoiceRecord record, long cancelDate)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (cancelDate <= 0)
        {
            ClearCancelDate(record);
            return;
        }

        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[CancelDateKey] = JsonSerializer.SerializeToElement(cancelDate);
    }

    public static long CancelDate(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ExtensionData is null || !record.ExtensionData.TryGetValue(CancelDateKey, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return 0;
    }

    public static string CancelDateText(InvoiceRecord record)
    {
        var value = CancelDate(record);
        if (value <= 0) return string.Empty;
        if (value >= 1_000_000_000)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime()
                    .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static void ClearCancelDate(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(CancelDateKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }
}
