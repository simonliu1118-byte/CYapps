namespace CYERPAutoInput;

/// <summary>
/// 設定 › 印表機 › 寄件單下方文字 (user, 2026-10-20): the person's own words, printed in a block of
/// fixed size at the bottom of the 100×150 mm label, under the slips the program cuts itself
/// (蝦皮／酷澎 全家, MO店+ 全家／7-11); slips Chrome prints are not touched. The type is sized to
/// the words (user, 2026-10-20: few words large, many words wrapped and smaller): the largest
/// size from <see cref="MaxPoints"/> down to <see cref="MinPoints"/> whose wrapped lines fit the
/// block. Lines typed stay lines; a line too wide wraps (a Latin word is kept whole when it fits).
/// </summary>
internal static class SlipNote
{
    public const string FontName = "Microsoft JhengHei";
    public const float MinPoints = 10f;
    public const float MaxPoints = 28f;
    public const double LabelHeightMm = 150;
    public const double BlockWidthMm = 96;
    public const double BlockHeightMm = 15;
    /// <summary>The least white between the slip and the block.</summary>
    public const double GapMm = 2;
    /// <summary>Line height over type size, a little above 微軟正黑體's own so the lines never crowd.</summary>
    public const double LineFactor = 1.35;
    private const double MmPerPoint = 25.4 / 72;

    /// <summary>The lines as typed, trailing spaces and blank lines at either end left out.</summary>
    public static IReadOnlyList<string> Lines(string? text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static double LineHeightMm(float points) => points * MmPerPoint * LineFactor;

    /// <summary>
    /// The size and wrapped lines for the block; null when the words do not fit even at
    /// <see cref="MinPoints"/> (or there are none). <paramref name="widthMm"/> measures a line at a size.
    /// </summary>
    public static (float Points, IReadOnlyList<string> Lines)? Fit(string? text, Func<string, float, double> widthMm)
    {
        var typed = Lines(text);
        if (typed.Count == 0) return null;
        for (var points = MaxPoints; points >= MinPoints; points -= 0.5f)
        {
            var most = (int)Math.Floor(BlockHeightMm / LineHeightMm(points) + 1e-9);
            if (most < typed.Count) continue;
            var wrapped = new List<string>();
            foreach (var line in typed)
            {
                wrapped.AddRange(Wrap(line, points, widthMm));
                if (wrapped.Count > most) break;
            }
            if (wrapped.Count <= most) return (points, wrapped);
        }
        return null;
    }

    /// <summary>Null when the words fit the block; otherwise why not.</summary>
    public static string? Check(string? text, Func<string, float, double> widthMm)
    {
        if (Lines(text).Count == 0 || Fit(text, widthMm) is not null) return null;
        var most = (int)Math.Floor(BlockHeightMm / LineHeightMm(MinPoints) + 1e-9);
        return $"寄件單下方文字太多：最小 {MinPoints:F0} pt 時最多 {most} 行（每行約 27 個中文字），請刪減。";
    }

    /// <summary>One typed line broken to the block's width: by character, a Latin word kept whole when it fits a line.</summary>
    private static IEnumerable<string> Wrap(string line, float points, Func<string, float, double> widthMm)
    {
        if (line.Length == 0) { yield return line; yield break; }
        var current = string.Empty;
        var i = 0;
        while (i < line.Length)
        {
            var end = i + 1;
            if (IsWordChar(line[i])) while (end < line.Length && IsWordChar(line[end])) end++;
            var piece = line[i..end];
            if (current.Length > 0 && widthMm(current + piece, points) > BlockWidthMm)
            {
                yield return current.TrimEnd();
                current = string.Empty;
                if (piece == " ") { i = end; continue; }
            }
            if (widthMm(piece, points) > BlockWidthMm)
            {
                // A word longer than the line: by character.
                foreach (var c in piece)
                {
                    if (current.Length > 0 && widthMm(current + c, points) > BlockWidthMm) { yield return current; current = string.Empty; }
                    current += c;
                }
            }
            else current += piece;
            i = end;
        }
        if (current.Length > 0) yield return current.TrimEnd();
    }

    private static bool IsWordChar(char c) => c < 0x2E80 && !char.IsWhiteSpace(c);
}
