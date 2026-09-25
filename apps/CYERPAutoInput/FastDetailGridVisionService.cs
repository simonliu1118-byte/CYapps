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
            var match = GridVisionService.FindPhrase(tokens, pair.Value);
            if (match is null)
            {
                _log.Info("vision", $"OCR_HEADER_MATCH_FAST logical_col={pair.Key} label={pair.Value[0]} found=false");
                continue;
            }

            var x = match.Value.Left + match.Value.Width / 2;
            geometry.ColumnX[pair.Key] = x;
            exact[pair.Key] = x;
            headerBottom = Math.Max(headerBottom, match.Value.Bottom);
            _log.Info("vision", $"OCR_HEADER_MATCH_FAST logical_col={pair.Key} label={pair.Value[0]} found=true x={x} rect={match.Value.Left},{match.Value.Top},{match.Value.Width},{match.Value.Height}");
        }

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

    private static string Sanitize(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        return text.Length <= 80 ? text : text[..80];
    }
}
