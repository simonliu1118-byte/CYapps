using System.Globalization;

namespace CYInvoice.Core.Imports;

public static class SpreadsheetCellValue
{
    public static string FromExcel(string? displayedText, object? rawValue)
    {
        var text = displayedText?.Trim() ?? string.Empty;
        if (text.Length != 0 && !IsOnlyHashes(text) && !IsScientificNumber(text))
        {
            return text;
        }

        return rawValue switch
        {
            null => string.Empty,
            double value => FixedNumber(value),
            float value => FixedNumber(value),
            decimal value => value.ToString("0.############################", CultureInfo.InvariantCulture),
            _ => Convert.ToString(rawValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
        };
    }

    public static bool IsScientificNumber(string value)
    {
        var candidate = value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (candidate.IndexOf('E') < 0 && candidate.IndexOf('e') < 0)
        {
            return false;
        }
        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    public static bool IsOnlyHashes(string value) =>
        value.Length != 0 && value.All(character => character == '#');

    private static string FixedNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("Excel 儲存格含有無效數值");
        }
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
