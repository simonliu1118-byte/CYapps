using System.Globalization;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceSourceInference
{
    public const string SyncTag = "同步";
    public const string UpdateTag = "更新";

    public static string FromOrderId(string orderId)
    {
        var value = StripLegacyRetrySuffix(orderId.Trim());
        if (value.StartsWith("66", StringComparison.Ordinal)) return InvoiceSources.Mo;
        if (value.StartsWith("1", StringComparison.Ordinal)) return InvoiceSources.Coupang;
        if (value.Length == 11 && IsDatedSequence(value)) return InvoiceSources.Digiwin;
        return InvoiceSources.Manual;
    }

    public static string Display(InvoiceRecord record)
    {
        var source = record.Source.Trim();
        return source.Length == 0 ? FromOrderId(record.OrderId) : source;
    }

    public static string DisplayTag(InvoiceRecord record)
    {
        if (string.Equals(record.RecordOrigin, RecordOrigins.Sync, StringComparison.Ordinal))
            return SyncTag;

        var currentSource = Display(record);
        var originalSource = FromOrderId(record.OriginalOrderId);
        return string.Equals(currentSource, originalSource, StringComparison.Ordinal)
            ? string.Empty
            : UpdateTag;
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
