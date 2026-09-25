namespace CYERPAutoInput;

internal sealed class FastDetailGridVisionService
{
    private readonly WindowsOcrService _ocr;
    private readonly GridVisionService _fallback;
    private readonly AppLogger _log;
    private readonly Dictionary<nint, Dictionary<int, int>> _exactColumnX = [];

    private static readonly Dictionary<int, string[]> ColumnAliases = new()
    {
        [0] = ["品號"],
        [1] = ["數量"],
        [3] = ["贈/備品量", "贈備品量"],
        [4] = ["單位"],
        [5] = ["批號"],
        [6] = ["庫別"],
        [7] = ["單價"]
    };

    public FastDetailGridVisionService(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _fallback = new GridVisionService(ocr, log);
        _log = log;
    }

    public async Task<GridGeometry> AnalyzeDetailGridAsync(nint gridHwnd, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(gridHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("Detail grid rectangle is unavailable.");

        var started = Environment.TickCount64;
        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var headerHeight = Math.Clamp(Math.Min(image.Height, 62), 24, image.Height);
        using var header = image.Clone(new Rectangle(0, 0, image.Width, headerHeight), image.PixelFormat);
        var tokens = await _ocr.RecognizeAsync(header, cancellationToken, requireChinese: true);

        var geometry = new GridGeometry { ScreenRect = rect.ToRectangle() };
        var exact = new Dictionary<int, int>();
        var headerBottom = 0;

        var rawVertical = GridVisionService.FindVerticalLines(image, Math.Max(8, image.Height - 1));
        var boundaries = NormalizeVerticalBoundaries(rawVertical, image.Width);
        var canSnapToCells = boundaries.Count >= 6;
        _log.Info("vision", $"DETAIL_GRID_BOUNDARIES raw={rawVertical.Count} normalized={boundaries.Count} snap_enabled={canSnapToCells}");

        _log.Info("vision", $"OCR_DIAG context=detail-header-fast total_tokens={tokens.Count} header_height={headerHeight}");
        foreach (var token in tokens.OrderBy(t => t.Rect.Left).Take(80))
        {
            var raw = Sanitize(token.Text);
            if (raw.Length == 0) continue;
            var normalized = Sanitize(OcrTextNormalizer.Normalize(token.Text));
            _log.Info("vision", $"OCR_TOKEN context=detail-header-fast raw=\"{raw}\" normalized=\"{normalized}\" rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
        }

        foreach (var pair in ColumnAliases)
        {
            // Do not use the broad historical phrase matcher here. For a header such as
            // "贈 / 備 品 量 單 位", the old matcher could return the whole suffix
            // "備品量單位" when looking for "單位", shifting the click left into the
            // previous field. Build 17 returns only the character span that actually
            // matches the requested header text.
            var match = FindExactPhrase(tokens, pair.Value);
            if (match is null)
            {
                _log.Info("vision", $"OCR_HEADER_MATCH_FAST logical_col={pair.Key} label={pair.Value[0]} found=false");
                continue;
            }

            var textX = match.Value.Left + match.Value.Width / 2;
            var cellX = canSnapToCells ? SnapToContainingCell(textX, boundaries) : null;
            var resolvedX = cellX ?? textX;

            geometry.ColumnX[pair.Key] = resolvedX;
            exact[pair.Key] = resolvedX;
            headerBottom = Math.Max(headerBottom, match.Value.Bottom);
            _log.Info("vision", $"OCR_HEADER_MATCH_FAST logical_col={pair.Key} label={pair.Value[0]} found=true text_x={textX} cell_x={resolvedX} snapped={cellX.HasValue} rect={match.Value.Left},{match.Value.Top},{match.Value.Width},{match.Value.Height}");
        }

        // Kept under the existing API name because ErpAutomationService already treats
        // this dictionary as the trusted visible-column X. Values are now the grid cell
        // center whenever vertical boundaries are available, not merely a text-box center.
        _exactColumnX[gridHwnd] = exact;

        if (!geometry.ColumnX.ContainsKey(0) || !geometry.ColumnX.ContainsKey(1))
        {
            _log.Warn("vision", $"fast header OCR missing required column(s); falling back to full detail analysis item={geometry.ColumnX.ContainsKey(0)} quantity={geometry.ColumnX.ContainsKey(1)} elapsed_ms={Environment.TickCount64 - started}");
            return await _fallback.AnalyzeDetailGridAsync(gridHwnd, cancellationToken);
        }

        var horizontal = GridVisionService.FindHorizontalLines(image, Math.Max(0, headerBottom - 6));
        if (headerBottom <= 0)
        {
            headerBottom = horizontal.FirstOrDefault(y => y is >= 14 and <= 90);
            if (headerBottom > 0)
                _log.Info("vision", $"detail fast header baseline inferred y={headerBottom}");
        }

        var firstBoundaryIndex = horizontal.FindIndex(y => y >= headerBottom - 3);
        if (firstBoundaryIndex < 0 || horizontal.Count - firstBoundaryIndex < 2)
        {
            _log.Warn("vision", "fast detail row-line detection failed; falling back to full detail analysis");
            return await _fallback.AnalyzeDetailGridAsync(gridHwnd, cancellationToken);
        }

        for (var i = firstBoundaryIndex; i + 1 < horizontal.Count; i++)
        {
            var a = horizontal[i];
            var b = horizontal[i + 1];
            var h = b - a;
            if (h < 14 || h > 80) continue;
            geometry.RowCenterY.Add((a + b) / 2);
        }

        if (geometry.RowCenterY.Count == 0)
        {
            _log.Warn("vision", "fast detail row centers unavailable; falling back to full detail analysis");
            return await _fallback.AnalyzeDetailGridAsync(gridHwnd, cancellationToken);
        }

        _log.Info("vision", $"detail fast geometry ready columns={geometry.ColumnX.Count} rows={geometry.RowCenterY.Count} elapsed_ms={Environment.TickCount64 - started} size={image.Width}x{image.Height}");
        return geometry;
    }

    public bool TryGetExactColumnX(nint gridHwnd, int logicalColumn, out int relativeX)
    {
        relativeX = 0;
        return _exactColumnX.TryGetValue(gridHwnd, out var columns) && columns.TryGetValue(logicalColumn, out relativeX);
    }

    public Task<Point> FindUnitAsync(nint lookupHwnd, string requestedUnit, CancellationToken cancellationToken) =>
        _fallback.FindUnitAsync(lookupHwnd, requestedUnit, cancellationToken);

    internal static Rectangle? FindExactPhrase(IReadOnlyList<OcrToken> tokens, IReadOnlyList<string> aliases)
    {
        var normalizedAliases = aliases
            .Select(OcrTextNormalizer.Normalize)
            .Where(x => x.Length > 0)
            .OrderByDescending(x => x.Length)
            .ToArray();
        if (normalizedAliases.Length == 0) return null;

        // First handle a detector token that contains more text than the requested
        // phrase. Crop the returned rectangle to the matching character span instead
        // of returning the whole token rectangle.
        foreach (var token in tokens)
        {
            var text = OcrTextNormalizer.Normalize(token.Text);
            if (text.Length == 0) continue;
            foreach (var alias in normalizedAliases)
            {
                var index = text.IndexOf(alias, StringComparison.Ordinal);
                if (index >= 0)
                    return SliceRect(token.Rect, text.Length, index, alias.Length);
            }
        }

        var rows = tokens
            .GroupBy(t => (t.Rect.Top + t.Rect.Height / 2) / 8)
            .Select(g => g.OrderBy(t => t.Rect.Left).ToList());

        foreach (var row in rows)
        {
            for (var start = 0; start < row.Count; start++)
            {
                var pieces = new List<(string Text, Rectangle Rect, int Start)>();
                var combined = string.Empty;
                Rectangle? previousRect = null;

                for (var count = 0; count < 6 && start + count < row.Count; count++)
                {
                    var token = row[start + count];
                    if (previousRect is not null && token.Rect.Left - previousRect.Value.Right > 35)
                        break;

                    var text = OcrTextNormalizer.Normalize(token.Text);
                    if (text.Length == 0)
                    {
                        previousRect = token.Rect;
                        continue;
                    }

                    var pieceStart = combined.Length;
                    pieces.Add((text, token.Rect, pieceStart));
                    combined += text;
                    previousRect = token.Rect;

                    foreach (var alias in normalizedAliases)
                    {
                        var index = combined.IndexOf(alias, StringComparison.Ordinal);
                        if (index < 0) continue;
                        var matched = BuildSpanRect(pieces, index, alias.Length);
                        if (matched is not null) return matched;
                    }
                }
            }
        }

        return null;
    }

    internal static int? SnapToContainingCell(int x, IReadOnlyList<int> boundaries)
    {
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            var left = boundaries[i];
            var right = boundaries[i + 1];
            if (x < left || x > right) continue;
            if (right - left < 24) return null;
            return (left + right) / 2;
        }
        return null;
    }

    private static Rectangle? BuildSpanRect(
        IReadOnlyList<(string Text, Rectangle Rect, int Start)> pieces,
        int targetStart,
        int targetLength)
    {
        var targetEnd = targetStart + targetLength;
        Rectangle? result = null;

        foreach (var piece in pieces)
        {
            var pieceEnd = piece.Start + piece.Text.Length;
            var overlapStart = Math.Max(targetStart, piece.Start);
            var overlapEnd = Math.Min(targetEnd, pieceEnd);
            if (overlapStart >= overlapEnd) continue;

            var localStart = overlapStart - piece.Start;
            var localLength = overlapEnd - overlapStart;
            var slice = SliceRect(piece.Rect, piece.Text.Length, localStart, localLength);
            result = result is null ? slice : Rectangle.Union(result.Value, slice);
        }

        return result;
    }

    private static Rectangle SliceRect(Rectangle rect, int totalCharacters, int startCharacter, int characterCount)
    {
        if (totalCharacters <= 0 || startCharacter <= 0 && characterCount >= totalCharacters)
            return rect;

        var left = rect.Left + (int)Math.Floor(rect.Width * (double)startCharacter / totalCharacters);
        var right = rect.Left + (int)Math.Ceiling(rect.Width * (double)(startCharacter + characterCount) / totalCharacters);
        left = Math.Clamp(left, rect.Left, Math.Max(rect.Left, rect.Right - 1));
        right = Math.Clamp(right, left + 1, rect.Right);
        return Rectangle.FromLTRB(left, rect.Top, right, rect.Bottom);
    }

    private static List<int> NormalizeVerticalBoundaries(IReadOnlyList<int> rawLines, int width)
    {
        var boundaries = rawLines
            .Where(x => x >= 0 && x < width)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (boundaries.Count == 0 || boundaries[0] > 5) boundaries.Insert(0, 0);
        if (boundaries[^1] < width - 5) boundaries.Add(width - 1);

        var changed = true;
        while (changed && boundaries.Count > 2)
        {
            changed = false;
            for (var i = 0; i + 1 < boundaries.Count; i++)
            {
                if (boundaries[i + 1] - boundaries[i] >= 24) continue;
                // DevExpress lookup/ellipsis buttons create a narrow sub-cell near a
                // real column edge. Merge that narrow strip into the surrounding cell.
                if (i == 0) boundaries.RemoveAt(i + 1);
                else boundaries.RemoveAt(i);
                changed = true;
                break;
            }
        }

        return boundaries;
    }

    private static string Sanitize(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        return text.Length <= 80 ? text : text[..80];
    }
}
