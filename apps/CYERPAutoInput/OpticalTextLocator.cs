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

        var normalizedAliases = aliases.Select(Normalize).Where(x => x.Length > 0).ToArray();

        // COPI08's Ribbon sometimes exposes button captions as real child-window text.
        // Use that direct signal first for the New button only. Do not use this shortcut
        // for tab captions because a TcxTabSheet can expose the same text over a large
        // content rectangle whose center is not the actual tab header.
        if (normalizedAliases.Contains("新增", StringComparer.Ordinal))
        {
            var direct = Win32Automation.EnumerateChildren(hwnd)
                .Where(c => c.Visible && c.Rect.Width > 0 && c.Rect.Height > 0)
                .Select(c => new { Control = c, Text = Normalize(c.Text) })
                .Where(x => normalizedAliases.Any(a => x.Text == a || x.Text.Contains(a, StringComparison.Ordinal)))
                .OrderBy(x => x.Control.Rect.Top)
                .ThenBy(x => x.Control.Rect.Left)
                .FirstOrDefault();

            if (direct is not null)
            {
                var p = new Point(
                    (direct.Control.Rect.Left + direct.Control.Rect.Right) / 2,
                    (direct.Control.Rect.Top + direct.Control.Rect.Bottom) / 2);
                _log.Info("vision", $"Win32 text target found aliases={aliases.Count} class={direct.Control.ClassName} point={p.X},{p.Y}");
                return p;
            }
        }

        using (var image = ScreenCapture.Capture(rect.ToRectangle()))
        {
            var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
            var point = FindPoint(tokens, normalizedAliases, rect.Left, rect.Top);
            if (point is not null)
            {
                _log.Info("vision", $"text target found aliases={aliases.Count} point={point.Value.X},{point.Value.Y}");
                return point;
            }
        }

        // The Ribbon caption is very small compared with a full-size COPI08 window.
        // If full-window OCR missed "新增", retry only the upper-left Ribbon area so
        // Windows OCR receives a much larger effective glyph size. This remains optical
        // targeting: no fixed click coordinate is used unless the requested text is read.
        if (normalizedAliases.Contains("新增", StringComparer.Ordinal))
        {
            var retryRect = new Rectangle(
                rect.Left,
                rect.Top,
                Math.Min(rect.Width, 720),
                Math.Min(rect.Height, 220));

            using var ribbon = ScreenCapture.Capture(retryRect);
            var ribbonTokens = await _ocr.RecognizeAsync(ribbon, cancellationToken, requireChinese: true);
            var retryPoint = FindPoint(ribbonTokens, normalizedAliases, retryRect.Left, retryRect.Top);
            if (retryPoint is not null)
            {
                _log.Info("vision", $"Ribbon OCR target found aliases={aliases.Count} point={retryPoint.Value.X},{retryPoint.Value.Y}");
                return retryPoint;
            }

            var sample = string.Join("|", ribbonTokens.Take(24).Select(t => Normalize(t.Text)).Where(t => t.Length > 0));
            _log.Warn("vision", $"Ribbon OCR missed 新增 tokens={ribbonTokens.Count} sample={sample}");
        }

        return null;
    }

    private static Point? FindPoint(
        IReadOnlyList<OcrToken> tokens,
        IReadOnlyList<string> normalizedAliases,
        int offsetX,
        int offsetY)
    {
        foreach (var token in tokens)
        {
            var text = Normalize(token.Text);
            if (normalizedAliases.Any(a => text == a || text.Contains(a, StringComparison.Ordinal)))
            {
                return new Point(
                    offsetX + token.Rect.Left + token.Rect.Width / 2,
                    offsetY + token.Rect.Top + token.Rect.Height / 2);
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
                    return new Point(
                        offsetX + union.Left + union.Width / 2,
                        offsetY + union.Top + union.Height / 2);
                }
            }
        }

        return null;
    }

    private static string Normalize(string value) => value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
}
