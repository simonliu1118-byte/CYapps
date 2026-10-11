using System.Drawing;

namespace CYERPAutoInput;

internal static class RibbonPrintLocator
{
    /// <summary>
    /// Where <paramref name="word"/> (「列印」, 「直接」) is among OCR tokens. On the user's PC
    /// the OCR returns the Ribbon one glyph per token (159 tokens; 「列」 and 「印」 apart,
    /// 2026-10-11), and elsewhere it may join neighbours (「列印上筆」), so tokens on one line
    /// are joined left to right when they touch, and the word's own glyphs are returned.
    /// </summary>
    public static Rectangle? FindWord(IReadOnlyList<(string Text, Rectangle Rect)> tokens, string word)
    {
        // Each glyph with its share of the token's box.
        var glyphs = new List<(char Char, Rectangle Rect)>();
        foreach (var (raw, rect) in tokens)
        {
            var text = new string(OcrTextNormalizer.Normalize(raw).Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (text.Length == 0) continue;
            var width = rect.Width / (double)text.Length;
            for (var i = 0; i < text.Length; i++)
                glyphs.Add((text[i], new Rectangle(rect.Left + (int)Math.Round(width * i), rect.Top, Math.Max(1, (int)Math.Round(width)), rect.Height)));
        }

        // Lines: glyphs whose vertical centres are within half a glyph height, left to right;
        // a gap wider than one glyph height starts a new run.
        var used = new bool[glyphs.Count];
        var order = Enumerable.Range(0, glyphs.Count).OrderBy(i => glyphs[i].Rect.Left).ToList();
        foreach (var start in order)
        {
            if (used[start]) continue;
            var run = new List<int> { start };
            used[start] = true;
            var last = glyphs[start].Rect;
            foreach (var next in order)
            {
                if (used[next]) continue;
                var r = glyphs[next].Rect;
                var centre = (last.Top + last.Bottom) / 2.0 - (r.Top + r.Bottom) / 2.0;
                if (Math.Abs(centre) > Math.Max(last.Height, r.Height) / 2.0) continue;
                if (r.Left < last.Left || r.Left - last.Right > Math.Max(last.Height, r.Height)) continue;
                run.Add(next);
                used[next] = true;
                last = r;
            }
            var text = new string(run.Select(i => glyphs[i].Char).ToArray());
            var index = text.IndexOf(word, StringComparison.Ordinal);
            if (index < 0) continue;
            var area = glyphs[run[index]].Rect;
            for (var k = 1; k < word.Length; k++) area = Rectangle.Union(area, glyphs[run[index + k]].Rect);
            return area;
        }
        return null;
    }
}
