namespace CYERPAutoInput;

internal sealed class OpticalTextLocator
{
    private readonly WindowsOcrService _ocr;
    private readonly AppLogger _log;

    public OpticalTextLocator(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<Point?> FindTextAsync(nint hwnd, IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return null;

        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var normalizedAliases = aliases.Select(Normalize).Where(x => x.Length > 0).ToArray();

        foreach (var token in tokens)
        {
            var text = Normalize(token.Text);
            if (normalizedAliases.Any(a => text == a || text.Contains(a, StringComparison.Ordinal)))
            {
                var p = new Point(rect.Left + token.Rect.Left + token.Rect.Width / 2,
                    rect.Top + token.Rect.Top + token.Rect.Height / 2);
                _log.Info("vision", $"text target found aliases={aliases.Count} point={p.X},{p.Y}");
                return p;
            }
        }

        var rows = tokens
            .GroupBy(t => (t.Rect.Top + t.Rect.Height / 2) / 8)
            .Select(g => g.OrderBy(t => t.Rect.Left).ToList());
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                var union = row[i].Rect;
                var text = string.Empty;
                for (var j = i; j < row.Count && j < i + 5; j++)
                {
                    if (j > i && row[j].Rect.Left - union.Right > 40) break;
                    text += Normalize(row[j].Text);
                    union = Rectangle.Union(union, row[j].Rect);
                    if (!normalizedAliases.Any(a => text.Contains(a, StringComparison.Ordinal))) continue;
                    var p = new Point(rect.Left + union.Left + union.Width / 2,
                        rect.Top + union.Top + union.Height / 2);
                    _log.Info("vision", $"joined text target found aliases={aliases.Count} point={p.X},{p.Y}");
                    return p;
                }
            }
        }
        return null;
    }

    private static string Normalize(string value) => value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
}
