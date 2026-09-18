using System.Globalization;

namespace CYInvoice.Core.Invoicing;

/// <summary>
/// Namespaces CYInvoice orders inside AMEGO's shared test environment.
/// The four-character prefix is deterministic per local calendar date:
/// 'C' + three base-36 digits representing days since 2020-01-01.
/// It is an identifier, not a security token.
/// </summary>
public static class TestOrderIdPrefix
{
    private static readonly DateOnly Epoch = new(2020, 1, 1);
    private const int PrefixLength = 4;
    private const int Base = 36;
    private const int MaximumDayCode = Base * Base * Base - 1;

    public static string ForDate(DateOnly date)
    {
        var days = date.DayNumber - Epoch.DayNumber;
        if (days < 0 || days > MaximumDayCode)
            throw new ArgumentOutOfRangeException(nameof(date), "測試訂單日期超出 4 碼前綴可表示範圍");
        return "C" + ToBase36(days).PadLeft(3, '0');
    }

    public static string Apply(string orderId, DateOnly date)
    {
        orderId = orderId.Trim();
        if (orderId.Length == 0) throw new ArgumentException("OrderID 不可空白", nameof(orderId));
        return ForDate(date) + orderId;
    }

    public static bool TryStripForDate(string apiOrderId, DateOnly date, out string orderId)
    {
        apiOrderId = apiOrderId.Trim();
        var prefix = ForDate(date);
        if (!apiOrderId.StartsWith(prefix, StringComparison.Ordinal) || apiOrderId.Length <= PrefixLength)
        {
            orderId = string.Empty;
            return false;
        }
        orderId = apiOrderId[PrefixLength..];
        return true;
    }

    public static string StripIfPresent(string apiOrderId)
    {
        apiOrderId = apiOrderId.Trim();
        if (apiOrderId.Length <= PrefixLength || apiOrderId[0] != 'C') return apiOrderId;
        for (var index = 1; index < PrefixLength; index++)
            if (!IsBase36(apiOrderId[index])) return apiOrderId;
        return apiOrderId[PrefixLength..];
    }

    private static string ToBase36(int value)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (value == 0) return "0";
        Span<char> buffer = stackalloc char[8];
        var index = buffer.Length;
        while (value > 0)
        {
            buffer[--index] = digits[value % Base];
            value /= Base;
        }
        return new string(buffer[index..]);
    }

    private static bool IsBase36(char value) =>
        char.IsAsciiDigit(value) || value is >= 'A' and <= 'Z';
}
