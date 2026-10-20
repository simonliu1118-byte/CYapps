using System.Drawing;

namespace CYERPAutoQuery;

internal sealed record OcrToken(string Text, Rectangle Rect);

// Glyph-joining word search, copied from CYERPAutoInput RibbonPrintLocator (user decision
// 2026-10-16: copy now, share later).
internal static class OcrWordFinder
{
    /// <summary>
    /// Where <paramref name="word"/> (a tab caption such as 「售價」) is among OCR tokens. On the
    /// user's PC the OCR returned ERP's Ribbon one glyph per token and elsewhere joined
    /// neighbours (CYERPAutoInput, 2026-10-11), so tokens on one line are joined left to right
    /// when they touch, and the word's own glyphs are returned.
    /// </summary>
    public static Rectangle? FindWord(IReadOnlyList<OcrToken> tokens, string word)
    {
        var all = FindWords(tokens, word);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Every place <paramref name="word"/> appears (one per joined line run), first run leftmost.</summary>
    public static IReadOnlyList<Rectangle> FindWords(IReadOnlyList<OcrToken> tokens, string word)
    {
        var found = new List<Rectangle>();
        var target = OcrTextNormalizer.Normalize(word);
        if (target.Length == 0) return found;

        // Each glyph with its share of the token's box.
        var glyphs = new List<(char Char, Rectangle Rect)>();
        foreach (var (raw, rect) in tokens.Select(t => (t.Text, t.Rect)))
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
            var index = text.IndexOf(target, StringComparison.Ordinal);
            if (index < 0) continue;
            var area = glyphs[run[index]].Rect;
            for (var k = 1; k < target.Length; k++) area = Rectangle.Union(area, glyphs[run[index + k]].Rect);
            found.Add(area);
        }
        return found;
    }
}
