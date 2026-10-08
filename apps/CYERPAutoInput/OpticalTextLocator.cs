namespace CYERPAutoInput;

internal sealed class OpticalTextLocator
{
    private readonly PaddleOcrService _ocr;
    private readonly AppLogger _log;

    private static readonly Dictionary<string, int> Copi08TabCenterX = new(StringComparer.Ordinal)
    {
        ["交易資料"] = 48,
        ["送貨資料"] = 137,
        ["發票資料(一)"] = 226,
        ["發票資料(二)"] = 316,
        ["其他資料"] = 405,
        ["訂金資料"] = 493,
        ["客戶描述"] = 582,
        ["資料瀏覽"] = 671
    };

    public OpticalTextLocator(PaddleOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<Point?> FindTextAsync(nint hwnd, IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return null;

        var normalizedAliases = aliases.Select(OcrTextNormalizer.Normalize).Where(x => x.Length > 0).ToArray();

        // COPI08 tabs already have a validated TcxPageControl geometry path.
        // OCR must not delay or redefine that proven interaction flow.
        if (NativeMethods.ClassName(hwnd).Equals("TcxPageControl", StringComparison.OrdinalIgnoreCase))
        {
            var tabName = normalizedAliases.FirstOrDefault(Copi08TabCenterX.ContainsKey);
            if (tabName is not null)
            {
                var x = Copi08TabCenterX[tabName];
                if (x < 0 || x >= rect.Width)
                {
                    _log.Warn("tab", $"proven tab-strip point outside page group={tabName} x={x} page_width={rect.Width}");
                    return null;
                }

                var p = new Point(rect.Left + x, rect.Top + 12);
                _log.Info("tab", $"resolved by proven TcxPageControl geometry group={tabName} point={p.X},{p.Y} page={rect.Width}x{rect.Height}");
                return p;
            }
        }

        // Generic optical locator remains available for bounded tasks that do not have
        // a stronger native/geometry signal.
        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var point = FindPoint(tokens, normalizedAliases, rect.Left, rect.Top);
        if (point is not null)
        {
            _log.Info("vision", $"text target found aliases={aliases.Count} point={point.Value.X},{point.Value.Y}");
            return point;
        }

        LogTokens("generic-text-miss", string.Join("/", aliases), tokens, 48);
        return null;
    }

    public Task<IReadOnlyList<OcrToken>> RecognizeAsync(Bitmap image, CancellationToken cancellationToken) =>
        _ocr.RecognizeAsync(image, cancellationToken, requireChinese: false);

    public async Task<bool> ContainsAllFragmentsAsync(nint hwnd, IReadOnlyList<string> fragments, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            return false;

        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var bag = string.Concat(tokens.Select(t => OcrTextNormalizer.Normalize(t.Text)));
        var normalizedFragments = fragments.Select(OcrTextNormalizer.Normalize).Where(f => f.Length > 0).ToArray();
        var found = normalizedFragments.All(fragment => bag.Contains(fragment, StringComparison.Ordinal));

        _log.Info("vision", $"OCR_FRAGMENT_CHECK target={string.Join("/", normalizedFragments)} found={found} tokens={tokens.Count}");
        if (!found)
            LogTokens("fragment-check-miss", string.Join("/", fragments), tokens, 48);
        return found;
    }

    private void LogTokens(string context, string target, IReadOnlyList<OcrToken> tokens, int maxTokens)
    {
        _log.Info("vision", $"OCR_DIAG context={context} target={target} tokens={tokens.Count}");
        foreach (var token in tokens.Take(maxTokens))
        {
            var text = Sanitize(token.Text);
            if (text.Length == 0) continue;
            _log.Info("vision", $"OCR_TOKEN context={context} target={target} raw=\"{_log.Value(text)}\" normalized=\"{_log.Value(Sanitize(OcrTextNormalizer.Normalize(token.Text)))}\" rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
        }
        if (tokens.Count > maxTokens)
            _log.Info("vision", $"OCR_DIAG context={context} target={target} truncated={tokens.Count - maxTokens}");
    }

    private static string Sanitize(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        return text.Length <= 80 ? text : text[..80];
    }

    private static Point? FindPoint(
        IReadOnlyList<OcrToken> tokens,
        IReadOnlyList<string> normalizedAliases,
        int offsetX,
        int offsetY)
    {
        foreach (var token in tokens)
        {
            var text = OcrTextNormalizer.Normalize(token.Text);
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
                    text += OcrTextNormalizer.Normalize(row[j].Text);
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
}
