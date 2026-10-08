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

    /// <summary>One CTC output character with its runner-up candidates (best first).</summary>
    public sealed record RecognizedChar(string Text, float Probability, IReadOnlyList<(string Text, float Probability)> Alternatives);

    private static readonly HashSet<string> ZeroLike = ["0", "O", "o", "Q", "Ø", "ø", "θ", "〇", "D"];
    private const float MinCharProbability = 0.2f;
    private const float RivalDigitProbability = 0.05f;

    /// <summary>
    /// Accepts a low-confidence 現有存量 read only when it is unambiguous: every character's
    /// best candidate is a digit, a number separator, or a zero-shaped glyph (ERP draws a
    /// slashed zero, read as 0/O/Q at ~0.3–0.5), and no other digit is a runner-up for that
    /// character. A real 8/6/9 shows up as a rival digit and is rejected.
    /// </summary>
    public static bool TryAcceptLowConfidenceStock(IReadOnlyList<RecognizedChar> chars, out string text)
    {
        text = string.Empty;
        if (chars.Count == 0) return false;
        var builder = new System.Text.StringBuilder();
        foreach (var c in chars)
        {
            if (c.Probability < MinCharProbability) return false;
            string mapped;
            if (ZeroLike.Contains(c.Text)) mapped = "0";
            else if (c.Text.Length == 1 && (char.IsAsciiDigit(c.Text[0]) || c.Text is "." or "," or "-")) mapped = c.Text;
            else return false;

            foreach (var (alt, probability) in c.Alternatives)
            {
                if (probability < RivalDigitProbability || alt.Length != 1 || !char.IsAsciiDigit(alt[0])) continue;
                if (alt != mapped) return false;
            }
            builder.Append(mapped);
        }
        text = builder.ToString();
        return TryParseStockText(text, out _);
    }
}
