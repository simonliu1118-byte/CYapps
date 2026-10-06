using System.Globalization;

namespace CYERPAutoInput;

/// <summary>Pure input / ERP text rules shared by the UI and automation (no Win32 or UI dependency).</summary>
internal static class InputRules
{
    public static string NormalizeDateDigits(string value) =>
        new string(value.Where(char.IsDigit).Take(8).ToArray());

    public static string FormatDateForDisplay(string digits)
    {
        if (digits.Length <= 4) return digits;
        if (digits.Length <= 6) return $"{digits[..4]}/{digits[4..]}";
        return $"{digits[..4]}/{digits[4..6]}/{digits[6..]}";
    }

    public static bool TryNormalizeValidDate(string value, out string digits)
    {
        digits = NormalizeDateDigits(value);
        return digits.Length == 8 &&
               DateTime.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>ERP sales order numbers are YYYYMMDD + 3-digit sequence (001–999) for the document date.</summary>
    public static bool IsExpectedSalesOrderNumber(string value, string expectedDate)
    {
        if (value.Length != 11 || !value.All(char.IsDigit)) return false;
        if (!value.StartsWith(expectedDate, StringComparison.Ordinal)) return false;
        return int.TryParse(value.AsSpan(8, 3), out var sequence) && sequence is >= 1 and <= 999;
    }

    /// <summary>Parses the 現有存量 text OCR read from the F2 batch lookup.</summary>
    public static bool TryParseStockText(string raw, out decimal value)
    {
        value = 0;
        var text = raw.Trim()
            .Replace(" ", string.Empty)
            .Replace("　", string.Empty)
            .Replace(",", string.Empty)
            .Replace("，", string.Empty);
        if (text.Length == 0) return false;

        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value) ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }
}
