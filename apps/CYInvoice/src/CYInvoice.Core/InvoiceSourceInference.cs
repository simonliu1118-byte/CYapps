using System.Globalization;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceSourceInference
{
    public static string FromOrderId(string orderId)
    {
        var value = StripLegacyRetrySuffix(orderId.Trim());
        if (value.Length == 0) return string.Empty;
        if (value.StartsWith("66", StringComparison.Ordinal)) return InvoiceSources.Mo;
        if (value.StartsWith("1", StringComparison.Ordinal)) return InvoiceSources.Coupang;
        if (value.Length == 12 && value[0] == 'M' && IsDatedSequence(value[1..])) return InvoiceSources.Manual;
        if (value.Length == 11 && IsDatedSequence(value)) return InvoiceSources.Digiwin;
        return string.Empty;
    }

    public static string Display(InvoiceRecord record)
    {
        var source = record.Source.Trim();
        if (!string.Equals(record.RecordOrigin, RecordOrigins.Sync, StringComparison.Ordinal)) return source;
        return source.Length == 0 ? "光貿同步" : "光貿同步｜" + source;
    }

    private static bool IsDatedSequence(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit)) return false;
        return DateOnly.TryParseExact(
            value[..8],
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static string StripLegacyRetrySuffix(string value)
    {
        var marker = value.LastIndexOf("-R", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0 || marker + 2 >= value.Length) return value;
        return value[(marker + 2)..].All(char.IsAsciiDigit) ? value[..marker] : value;
    }
}
