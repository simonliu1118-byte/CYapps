namespace CYERPAutoInput;

/// <summary>
/// 設定 › 印表機 › 寄件單下方文字 (user, 2026-10-20): the person's own words, printed in a block of
/// fixed size at the bottom of the 100×150 mm label, under the slips the program cuts itself
/// (蝦皮／酷澎 全家, MO店+ 全家／7-11); slips Chrome prints are not touched. The type is fixed at
/// <see cref="FontPoints"/> so it is never too small: the block holds <see cref="MaxLines"/>
/// lines of at most <see cref="BlockWidthMm"/> mm (about 27 Chinese characters; Latin letters
/// and digits about half as wide).
/// </summary>
internal static class SlipNote
{
    public const string FontName = "Microsoft JhengHei";
    public const float FontPoints = 10f;
    public const int MaxLines = 3;
    public const double LabelHeightMm = 150;
    public const double BlockWidthMm = 96;
    public const double BlockHeightMm = 15;
    /// <summary>The least white between the slip and the block.</summary>
    public const double GapMm = 2;

    /// <summary>The lines as typed, trailing spaces and blank lines at either end left out.</summary>
    public static IReadOnlyList<string> Lines(string? text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Null when the text fits the block; otherwise what is too long. <paramref name="widthMm"/> measures one line.</summary>
    public static string? Check(string? text, Func<string, double> widthMm)
    {
        var lines = Lines(text);
        if (lines.Count > MaxLines) return $"寄件單下方文字最多 {MaxLines} 行，現在 {lines.Count} 行。";
        for (var i = 0; i < lines.Count; i++)
        {
            var width = widthMm(lines[i]);
            if (width > BlockWidthMm)
                return $"寄件單下方文字第 {i + 1} 行太長（約 {width:F0} mm，最多 {BlockWidthMm:F0} mm，約 27 個中文字）。";
        }
        return null;
    }
}
