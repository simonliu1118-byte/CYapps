using System.Text.RegularExpressions;

namespace CYEnvelope;

public static class PhoneFormatting
{
    // Longest prefixes first; subscriber lengths follow the NCC local numbering plan.
    private static readonly (string Prefix, int[] Lengths)[] AreaCodes =
    [
        ("0826", [5]), ("0836", [5]),
        ("037", [6]), ("049", [7]), ("082", [6]), ("089", [6]),
        ("02", [8]), ("03", [7]), ("04", [7, 8]), ("05", [7]),
        ("06", [7]), ("07", [7]), ("08", [7])
    ];

    public static string Format(string input)
    {
        var text = input.Trim();
        var match = Regex.Match(text, @"^(.*?)(?:\s*(?:#|分機|ext\.?|轉)\s*(\d+))?$", RegexOptions.IgnoreCase);
        var body = match.Groups[1].Value;
        var extension = match.Groups[2].Value;
        var digits = Regex.Replace(body, @"\D", "");
        if (body.TrimStart().StartsWith('+') && digits.StartsWith("886")) digits = "0" + digits[3..];
        var number = FormatDigits(digits) ?? body.Trim();
        return extension.Length == 0 ? number : $"{number} #{extension}";
    }

    private static string? FormatDigits(string digits)
    {
        if (digits.Length == 10 && (digits.StartsWith("09") || digits.StartsWith("0800") || digits.StartsWith("0809")))
            return $"{digits[..4]}-{digits[4..7]}-{digits[7..]}";
        foreach (var (prefix, lengths) in AreaCodes)
        {
            if (!digits.StartsWith(prefix)) continue;
            return lengths.Contains(digits.Length - prefix.Length) ? $"{prefix}-{digits[prefix.Length..]}" : null;
        }
        return null;
    }
}
