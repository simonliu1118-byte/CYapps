using System.Text.RegularExpressions;

namespace CYEnvelope;

public static class PhoneFormatting
{
    public static string Format(string input)
    {
        var text = input.Trim();
        var match = Regex.Match(text, @"^(.*?)(?:\s*(?:#|分機|ext\.?|轉)\s*(\d+))?$", RegexOptions.IgnoreCase);
        var digits = Regex.Replace(match.Groups[1].Value, @"\D", "");
        var extension = match.Groups[2].Value;
        string number;
        if (digits.Length == 10 && digits.StartsWith("09"))
            number = $"{digits[..4]}-{digits[4..7]}-{digits[7..]}";
        else if (digits.StartsWith("0800") && digits.Length == 10)
            number = $"{digits[..4]}-{digits[4..7]}-{digits[7..]}";
        else if (digits.Length is 9 or 10 && digits.StartsWith("02"))
            number = $"02-{digits[2..]}";
        else if (digits.Length is 9 or 10 && digits.StartsWith('0'))
            number = $"{digits[..3]}-{digits[3..]}";
        else number = text;
        return extension.Length == 0 ? number : $"{number} #{extension}";
    }
}
