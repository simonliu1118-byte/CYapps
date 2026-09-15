using System.Globalization;

namespace CYInvoice.Core;

public static class ManualOrderId
{
    public static string Next(DateTimeOffset now, IEnumerable<InvoiceRecord> records)
    {
        var prefix = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var highest = 0;
        foreach (var record in records)
        {
            var orderId = record.OrderId.Trim();
            if (orderId.Length != prefix.Length + 3 || !orderId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(orderId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > highest)
            {
                highest = sequence;
            }
        }
        if (highest >= 999) throw new InvalidOperationException("今日自動訂單編號已達 999 筆");
        return prefix + (highest + 1).ToString("D3", CultureInfo.InvariantCulture);
    }
}
