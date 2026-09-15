using System.Globalization;

namespace CYInvoice.Core;

public static class MoneyFormatter
{
    public static string Integer(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Decimal(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var sign = string.Empty;
        var unsigned = value;
        if (unsigned[0] is '-' or '+')
        {
            sign = unsigned[..1];
            unsigned = unsigned[1..];
        }

        var parts = unsigned.Split('.', 2);
        if (parts[0].Length == 0 || !DigitsOnly(parts[0]) ||
            (parts.Length == 2 && (parts[1].Length == 0 || !DigitsOnly(parts[1]))))
        {
            return value;
        }

        var integer = long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("N0", CultureInfo.InvariantCulture)
            : AddSeparators(parts[0]);
        return parts.Length == 2 ? $"{sign}{integer}.{parts[1]}" : sign + integer;
    }

    private static string AddSeparators(string digits)
    {
        for (var index = digits.Length - 3; index > 0; index -= 3)
        {
            digits = digits.Insert(index, ",");
        }

        return digits;
    }

    private static bool DigitsOnly(string value) => value.All(character => character is >= '0' and <= '9');
}
