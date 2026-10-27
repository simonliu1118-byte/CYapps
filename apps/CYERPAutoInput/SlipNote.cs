namespace CYERPAutoInput;

/// <summary>
/// 寄件單下方文字 (user, 2026-10-20): the person's own words, printed under the thermal slips the
/// program draws itself, in the room left under the slip at its own size. Each typed line is one
/// printed line, never broken (user, 2026-10-26); a line longer than fits at <see cref="MinPoints"/>
/// is not taken. The size is the largest from <see cref="MaxPoints"/> down that fits the room,
/// not below <see cref="MinPoints"/> (<see cref="FamilyMinPoints"/> under a 全家 slip).
/// </summary>
internal static class SlipNote
{
    public const string FontName = "Microsoft JhengHei";
    /// <summary>
    /// Symbols 微軟正黑體 has no shape for (user's slip, 2026-10-26: emoji printed as □) are drawn in
    /// this font instead, in black.
    /// </summary>
    public const string SymbolFontName = "Segoe UI Emoji";
    /// <summary>Never smaller (user, 2026-10-26: 10 pt was too small)…</summary>
    public const float MinPoints = 14f;
    /// <summary>…except under a 全家 slip, which leaves little room (user, 2026-10-26).</summary>
    public const float FamilyMinPoints = 10f;
    public const float MaxPoints = 40f;
    public const double LabelHeightMm = 150;
    public const double BlockWidthMm = 96;
    /// <summary>The least room the block needs; the space under a shorter slip is all used (user, 2026-10-26).</summary>
    public const double BlockHeightMm = 15;
    /// <summary>The least white between the slip and the block.</summary>
    public const double GapMm = 2;
    /// <summary>Line height over type size, a little above 微軟正黑體's own so the lines never crowd.</summary>
    public const double LineFactor = 1.35;
    private const double MmPerPoint = 25.4 / 72;

    /// <summary>The lines as typed, trailing spaces and blank lines at either end left out.</summary>
    public static IReadOnlyList<string> Lines(string? text)
    {
        // Emoji variation selectors and joiners draw nothing in black and white; left out.
        var clean = new string((text ?? string.Empty).Where(c => c is not ('\uFE0F' or '\uFE0E' or '\u200D')).ToArray());
        var lines = clean.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static double LineHeightMm(float points) => points * MmPerPoint * LineFactor;

    /// <summary>
    /// The size for the block: the largest from <see cref="MaxPoints"/> down to
    /// <paramref name="minPoints"/> at which every line fits its width and all fit the height;
    /// null when none (or no words). Lines are never broken (user, 2026-10-26).
    /// </summary>
    public static (float Points, IReadOnlyList<string> Lines)? Fit(string? text, Func<string, float, double> widthMm,
        double blockHeightMm = BlockHeightMm, float minPoints = MinPoints)
    {
        var lines = Lines(text);
        if (lines.Count == 0) return null;
        for (var points = MaxPoints; points >= minPoints; points -= 0.5f)
            if (lines.Count * LineHeightMm(points) <= blockHeightMm + 1e-9 && lines.All(l => widthMm(l, points) <= BlockWidthMm))
                return (points, lines);
        return null;
    }

    /// <summary>Whether a typed line fits one line at <see cref="MinPoints"/>; a longer one is not taken (user, 2026-10-26).</summary>
    public static bool LineFits(string line, Func<string, float, double> widthMm) =>
        widthMm(Lines(line).FirstOrDefault() ?? string.Empty, MinPoints) <= BlockWidthMm;

    /// <summary>A line cut where it changes between 微軟正黑體 and the symbol font (<see cref="SymbolFontName"/>).</summary>
    public static List<(string Text, bool Symbol)> Runs(string line)
    {
        var runs = new List<(string Text, bool Symbol)>();
        for (var i = 0; i < line.Length;)
        {
            var length = char.IsSurrogatePair(line, i) ? 2 : 1;
            var symbol = IsSymbol(char.ConvertToUtf32(line, i) is var cp && length == 2 ? cp : line[i]);
            var piece = line.Substring(i, length);
            if (runs.Count > 0 && runs[^1].Symbol == symbol) runs[^1] = (runs[^1].Text + piece, symbol);
            else runs.Add((piece, symbol));
            i += length;
        }
        return runs;
    }

    /// <summary>Emoji and the pictographic symbols (☎ ✂ ⚠ ✅ 📦 …) 微軟正黑體 lacks.</summary>
    public static bool IsSymbol(int codePoint) =>
        codePoint >= 0x1F000 || codePoint is >= 0x2300 and <= 0x23FF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF;
}
