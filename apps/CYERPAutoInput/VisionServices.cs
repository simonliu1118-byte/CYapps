using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RapidOCRSharpOnnx;
using RapidOCRSharpOnnx.Configurations;
using RapidOCRSharpOnnx.Providers;
using RapidOCRSharpOnnx.Utils;

namespace CYERPAutoInput;

internal sealed record OcrToken(string Text, Rectangle Rect);

internal sealed class GridGeometry
{
    public Rectangle ScreenRect { get; init; }
    public Dictionary<int, int> ColumnX { get; } = [];
    public List<int> RowCenterY { get; } = [];

    public bool TryCellPoint(int visibleRow, int erpColumn, out Point point)
    {
        point = default;
        if (visibleRow < 0 || visibleRow >= RowCenterY.Count) return false;
        if (!ColumnX.TryGetValue(erpColumn, out var x)) return false;
        point = new Point(ScreenRect.Left + x, ScreenRect.Top + RowCenterY[visibleRow]);
        return true;
    }
}

internal static class ScreenCapture
{
    public static Bitmap CaptureWindow(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var r) || r.Width <= 0 || r.Height <= 0)
            throw new InvalidOperationException("Window rectangle is unavailable.");
        return Capture(r.ToRectangle());
    }

    public static Bitmap Capture(Rectangle screenRect)
    {
        if (screenRect.Width <= 0 || screenRect.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenRect));
        var bitmap = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
}

// Compatibility name retained through the V0.1.0 rewrite so established vision
// call sites remain small. Build 11+ uses local PP-OCRv5 inference, not Windows OCR.
internal sealed class WindowsOcrService
{
    private readonly AppLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOCRSharp? _engine;

    public WindowsOcrService(AppLogger log) => _log = log;

    public async Task<IReadOnlyList<OcrToken>> RecognizeAsync(
        Bitmap bitmap,
        CancellationToken cancellationToken,
        bool requireChinese = false)
    {
        await _gate.WaitAsync(cancellationToken);
        var temp = Path.Combine(Path.GetTempPath(), $"CYERPAutoInput_paddle_{Guid.NewGuid():N}.png");
        try
        {
            using var prepared = PrepareForOcr(bitmap, out var scale);
            prepared.Save(temp, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();

            var engine = EnsureEngine();
            var result = await Task.Run(() => engine.RecognizeText(temp), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var tokens = new List<(int Line, OcrToken Token)>();
            if (result.WordResults is not null)
            {
                foreach (var word in result.WordResults)
                {
                    if (word.Box is null || word.Box.Length == 0 || string.IsNullOrWhiteSpace(word.Word))
                        continue;

                    var minX = word.Box.Min(p => p.X);
                    var minY = word.Box.Min(p => p.Y);
                    var maxX = word.Box.Max(p => p.X);
                    var maxY = word.Box.Max(p => p.Y);
                    var rect = new Rectangle(
                        Math.Max(0, (int)Math.Floor(minX / scale)),
                        Math.Max(0, (int)Math.Floor(minY / scale)),
                        Math.Max(1, (int)Math.Ceiling((maxX - minX) / scale)),
                        Math.Max(1, (int)Math.Ceiling((maxY - minY) / scale)));
                    tokens.Add((word.LineId, new OcrToken(word.Word.Trim(), rect)));
                }
            }

            var ordered = tokens
                .OrderBy(x => x.Line)
                .ThenBy(x => x.Token.Rect.Top)
                .ThenBy(x => x.Token.Rect.Left)
                .Select(x => x.Token)
                .ToArray();

            _log.Info("vision", $"PaddleOCR completed engine=PP-OCRv5_mobile_rec tokens={ordered.Length} image={bitmap.Width}x{bitmap.Height} prepared={prepared.Width}x{prepared.Height} scale={scale:0.##} require_chinese={requireChinese}");
            return ordered;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _gate.Release();
        }
    }

    private RapidOCRSharp EnsureEngine()
    {
        if (_engine is not null) return _engine;

        var modelDir = Path.Combine(AppContext.BaseDirectory, "runtime", "ocr");
        var detector = Path.Combine(modelDir, "ch_PP-OCRv5_det_mobile.onnx");
        var recognizer = Path.Combine(modelDir, "ch_PP-OCRv5_rec_mobile.onnx");
        var classifier = Path.Combine(modelDir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
        var missing = new[] { detector, recognizer, classifier }.Where(path => !File.Exists(path)).Select(Path.GetFileName).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"PaddleOCR runtime model 缺少：{string.Join("、", missing)}。請使用完整 CYERPAutoInput 測試包。");

        var config = new OcrConfig(detector, recognizer, LangRec.CH, OCRVersion.PPOCRV5, classifier)
        {
            MinHeight = 4,
            MinSideLen = 8,
            MaxSideLen = 3200,
            ReturnWordBox = true
        };
        config.DetectorConfig.LimitType = LimitType.Max;
        config.DetectorConfig.LimitSideLen = 1800;
        config.DetectorConfig.Thresh = 0.20f;
        config.DetectorConfig.BoxThresh = 0.32f;
        config.DetectorConfig.UnclipRatio = 1.45f;
        config.DetectorConfig.UseDilation = true;
        config.RecognizerConfig.TextScore = 0.30f;
        config.RecognizerConfig.RecBatchNum = 6;

        _engine = new RapidOCRSharp(new ExecutionProviderCPU(config));
        _log.Info("vision", "PaddleOCR initialized model=PP-OCRv5_mobile_rec provider=CPU detector=PP-OCRv5_mobile classifier=PP-LCNet_mobile");
        return _engine;
    }

    private static Bitmap PrepareForOcr(Bitmap source, out double scale)
    {
        var maxSide = Math.Max(source.Width, source.Height);
        scale = maxSide <= 1100 ? 2.0 : maxSide <= 1800 ? 1.5 : 1.0;
        scale = Math.Min(scale, 3000.0 / Math.Max(1, maxSide));
        scale = Math.Clamp(scale, 1.0, 2.0);

        if (Math.Abs(scale - 1.0) < 0.01)
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.Clear(Color.White);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return output;
    }
}

internal sealed class GridVisionService
{
    private readonly WindowsOcrService _ocr;
    private readonly AppLogger _log;

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

    private static readonly Dictionary<int, int> LogicalColumnOrder = new()
    {
        [0] = 1,
        [1] = 4,
        [3] = 6,
        [4] = 7,
        [5] = 8,
        [6] = 9,
        [7] = 11
    };

    private static readonly (int Order, string[] Aliases)[] GridOrderAnchors =
    [
        (0, ["序號", "序"]),
        (1, ["品號"]),
        (2, ["品名"]),
        (3, ["規格"]),
        (4, ["數量"]),
        (5, ["類型"]),
        (6, ["贈/備品量", "贈備品量"]),
        (7, ["單位"]),
        (8, ["批號"]),
        (9, ["庫別"]),
        (10, ["庫別名稱"]),
        (11, ["單價"]),
        (12, ["折扣率"]),
        (13, ["金額"]),
        (18, ["備註"])
    ];

    public GridVisionService(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    internal static bool TryGetLogicalOrder(int erpColumn, out int order) => LogicalColumnOrder.TryGetValue(erpColumn, out order);

    public async Task<GridGeometry> AnalyzeDetailGridAsync(nint gridHwnd, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(gridHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("Detail grid rectangle is unavailable.");

        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        LogDetailHeaderDiagnostics(tokens, image.Height);

        var geometry = new GridGeometry { ScreenRect = rect.ToRectangle() };
        var headerBottom = 0;
        foreach (var pair in ColumnAliases)
        {
            var match = FindPhrase(tokens, pair.Value);
            if (match is null)
            {
                _log.Info("vision", $"OCR_HEADER_MATCH logical_col={pair.Key} label={pair.Value[0]} found=false");
                continue;
            }
            geometry.ColumnX[pair.Key] = match.Value.Left + match.Value.Width / 2;
            headerBottom = Math.Max(headerBottom, match.Value.Bottom);
            _log.Info("vision", $"OCR_HEADER_MATCH logical_col={pair.Key} label={pair.Value[0]} found=true rect={match.Value.Left},{match.Value.Top},{match.Value.Width},{match.Value.Height}");
        }

        if (!geometry.ColumnX.ContainsKey(0) || !geometry.ColumnX.ContainsKey(1))
        {
            if (!TryInferColumnsFromGridLines(image, tokens, geometry, out var inferredHeaderBottom))
            {
                throw new InvalidOperationException("Optical detail header detection failed: item/quantity columns were not both found and grid-line fallback was not reliable.");
            }
            headerBottom = Math.Max(headerBottom, inferredHeaderBottom);
        }

        var horizontal = FindHorizontalLines(image, Math.Max(0, headerBottom - 6));
        if (headerBottom <= 0)
        {
            headerBottom = horizontal.FirstOrDefault(y => y is >= 14 and <= 90);
            if (headerBottom > 0)
                _log.Info("vision", $"detail header baseline inferred from horizontal grid line y={headerBottom}");
        }

        var firstBoundaryIndex = horizontal.FindIndex(y => y >= headerBottom - 3);
        if (firstBoundaryIndex < 0 || horizontal.Count - firstBoundaryIndex < 2)
        {
            throw new InvalidOperationException("Optical detail row-line detection failed.");
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
            throw new InvalidOperationException("Optical detail row centers were not detected.");

        _log.Info("vision", $"detail geometry columns={geometry.ColumnX.Count} rows={geometry.RowCenterY.Count} size={image.Width}x{image.Height}");
        return geometry;
    }

    private void LogDetailHeaderDiagnostics(IReadOnlyList<OcrToken> tokens, int imageHeight)
    {
        var headerTokens = tokens
            .Where(t => t.Rect.Top <= Math.Min(imageHeight, 48))
            .OrderBy(t => t.Rect.Left)
            .Take(80)
            .ToArray();
        _log.Info("vision", $"OCR_DIAG context=detail-header total_tokens={tokens.Count} header_tokens={headerTokens.Length}");
        foreach (var token in headerTokens)
        {
            var raw = Sanitize(token.Text);
            if (raw.Length == 0) continue;
            var normalized = Sanitize(OcrTextNormalizer.Normalize(token.Text));
            _log.Info("vision", $"OCR_TOKEN context=detail-header raw=\"{raw}\" normalized=\"{normalized}\" rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
        }
    }

    private bool TryInferColumnsFromGridLines(
        Bitmap image,
        IReadOnlyList<OcrToken> tokens,
        GridGeometry geometry,
        out int headerBottom)
    {
        headerBottom = 0;
        var rawLines = FindVerticalLines(image, Math.Min(image.Height - 1, 96));
        var boundaries = NormalizeColumnBoundaries(rawLines, image.Width);
        if (boundaries.Count < 6)
        {
            _log.Warn("vision", $"detail grid-line fallback rejected: vertical_boundaries={boundaries.Count}");
            return false;
        }

        var offsets = new List<int>();
        foreach (var anchor in GridOrderAnchors)
        {
            var match = FindPhrase(tokens, anchor.Aliases);
            if (match is null || match.Value.Top > 48) continue;
            headerBottom = Math.Max(headerBottom, match.Value.Bottom);
            var centerX = match.Value.Left + match.Value.Width / 2;
            var interval = FindInterval(boundaries, centerX);
            if (interval >= 0) offsets.Add(interval - anchor.Order);
        }

        var offset = 0;
        var consensus = 0;
        if (offsets.Count > 0)
        {
            var best = offsets
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => Math.Abs(g.Key))
                .First();
            offset = best.Key;
            consensus = best.Count();
        }

        if (consensus < 2)
        {
            offset = 0;
            _log.Warn("vision", $"detail grid-line fallback using semantic column order; OCR anchor_consensus={consensus}/{offsets.Count} boundaries={boundaries.Count}");
        }

        foreach (var pair in LogicalColumnOrder)
        {
            var interval = pair.Value + offset;
            if (interval < 0 || interval + 1 >= boundaries.Count)
                continue;
            var left = boundaries[interval];
            var right = boundaries[interval + 1];
            if (right - left < 24) continue;
            geometry.ColumnX[pair.Key] = (left + right) / 2;
        }

        var ok = geometry.ColumnX.ContainsKey(0) && geometry.ColumnX.ContainsKey(1);
        if (ok)
            _log.Info("vision", $"detail grid-line geometry accepted boundaries={boundaries.Count} anchor_consensus={consensus}/{offsets.Count} offset={offset}");
        else
            _log.Warn("vision", $"detail grid-line fallback rejected after mapping: item={geometry.ColumnX.ContainsKey(0)} quantity={geometry.ColumnX.ContainsKey(1)} boundaries={boundaries.Count}");
        return ok;
    }

    private static int FindInterval(IReadOnlyList<int> boundaries, int x)
    {
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            if (x >= boundaries[i] && x <= boundaries[i + 1]) return i;
        }
        return -1;
    }

    private static List<int> NormalizeColumnBoundaries(IReadOnlyList<int> rawLines, int width)
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
                if (i == 0) boundaries.RemoveAt(i + 1);
                else boundaries.RemoveAt(i);
                changed = true;
                break;
            }
        }
        return boundaries;
    }

    public async Task<Point> FindUnitAsync(nint lookupHwnd, string requestedUnit, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(lookupHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("F2 lookup rectangle is unavailable.");
        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);

        LogTokens("f2-window", tokens, null, 80);
        var best = FindBestUnitCandidate(tokens, requestedUnit);
        if (best is null)
            throw new InvalidOperationException("OCR 無法在 F2 的「換算單位」欄可靠定位指定單位；已停止，不進行座標猜測。");

        var point = new Point(rect.Left + best.Rect.Left + best.Rect.Width / 2, rect.Top + best.Rect.Top + best.Rect.Height / 2);
        _log.Info("vision", $"F2 requested unit located point={point.X},{point.Y} raw=\"{Sanitize(best.Text)}\" normalized=\"{Sanitize(OcrTextNormalizer.Normalize(best.Text))}\"");
        return point;
    }

    internal static OcrToken? FindBestUnitCandidate(IReadOnlyList<OcrToken> tokens, string requestedUnit)
    {
        var target = OcrTextNormalizer.Normalize(requestedUnit);
        if (target.Length == 0) return null;

        var unitHeader = FindPhrase(tokens, ["換算單位"]);
        if (unitHeader is null) return null;

        var headerX = unitHeader.Value.Left + unitHeader.Value.Width / 2;
        var corridor = Math.Max(80, unitHeader.Value.Width * 2);

        return tokens
            .Select(t => new
            {
                Token = t,
                Text = OcrTextNormalizer.Normalize(t.Text),
                CenterX = t.Rect.Left + t.Rect.Width / 2
            })
            .Where(x => x.Text == target || x.Text.Contains(target, StringComparison.Ordinal))
            .Where(x => x.Token.Rect.Top > unitHeader.Value.Bottom - 2)
            .Where(x => Math.Abs(x.CenterX - headerX) <= corridor)
            .OrderBy(x => x.Text == target ? 0 : 1)
            .ThenBy(x => Math.Abs(x.CenterX - headerX))
            .ThenBy(x => x.Token.Rect.Top)
            .Select(x => x.Token)
            .FirstOrDefault();
    }

    internal static Rectangle? FindPhrase(IReadOnlyList<OcrToken> tokens, IReadOnlyList<string> aliases)
    {
        var normalizedAliases = aliases.Select(OcrTextNormalizer.Normalize).ToArray();
        foreach (var alias in normalizedAliases)
        {
            var direct = tokens.FirstOrDefault(t => OcrTextNormalizer.Normalize(t.Text).Contains(alias, StringComparison.Ordinal));
            if (direct is not null) return direct.Rect;
        }

        var rows = tokens
            .GroupBy(t => (t.Rect.Top + t.Rect.Height / 2) / 8)
            .Select(g => g.OrderBy(t => t.Rect.Left).ToList());

        foreach (var row in rows)
        {
            for (var start = 0; start < row.Count; start++)
            {
                var union = row[start].Rect;
                var text = string.Empty;
                for (var count = 0; count < 5 && start + count < row.Count; count++)
                {
                    var token = row[start + count];
                    if (count > 0 && token.Rect.Left - union.Right > 35) break;
                    text += OcrTextNormalizer.Normalize(token.Text);
                    union = Rectangle.Union(union, token.Rect);
                    if (normalizedAliases.Any(a => text.Contains(a, StringComparison.Ordinal))) return union;
                }
            }
        }
        return null;
    }

    private void LogTokens(string context, IReadOnlyList<OcrToken> tokens, Func<OcrToken, bool>? filter, int maxTokens)
    {
        var selected = (filter is null ? tokens : tokens.Where(filter).ToArray()).Take(maxTokens).ToArray();
        _log.Info("vision", $"OCR_DIAG context={context} total_tokens={tokens.Count} logged_tokens={selected.Length}");
        foreach (var token in selected)
        {
            var raw = Sanitize(token.Text);
            if (raw.Length == 0) continue;
            _log.Info("vision", $"OCR_TOKEN context={context} raw=\"{raw}\" normalized=\"{Sanitize(OcrTextNormalizer.Normalize(token.Text))}\" rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
        }
    }

    private static string Sanitize(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        return text.Length <= 80 ? text : text[..80];
    }

    internal static List<int> FindHorizontalLines(Bitmap source, int startY)
    {
        using var bitmap = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var scores = new List<(int y, double score)>();
            for (var y = Math.Max(1, startY); y < bitmap.Height; y++)
            {
                var hits = 0;
                var samples = 0;
                for (var x = 2; x < bitmap.Width - 2; x += 3)
                {
                    var now = Luma(bytes, data.Stride, x, y);
                    var prev = Luma(bytes, data.Stride, x, y - 1);
                    if (Math.Abs(now - prev) >= 14) hits++;
                    samples++;
                }
                if (samples > 0)
                {
                    var score = (double)hits / samples;
                    if (score >= 0.22) scores.Add((y, score));
                }
            }

            var lines = new List<int>();
            for (var i = 0; i < scores.Count;)
            {
                var best = scores[i];
                var j = i + 1;
                while (j < scores.Count && scores[j].y <= scores[j - 1].y + 2)
                {
                    if (scores[j].score > best.score) best = scores[j];
                    j++;
                }
                if (lines.Count == 0 || best.y - lines[^1] >= 8) lines.Add(best.y);
                i = j;
            }
            return lines;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    internal static List<int> FindVerticalLines(Bitmap source, int endY)
    {
        using var bitmap = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var limitY = Math.Clamp(endY, 8, bitmap.Height - 1);
            var scores = new List<(int x, double score)>();
            for (var x = 1; x < bitmap.Width; x++)
            {
                var hits = 0;
                var samples = 0;
                for (var y = 2; y < limitY; y += 2)
                {
                    var now = Luma(bytes, data.Stride, x, y);
                    var prev = Luma(bytes, data.Stride, x - 1, y);
                    if (Math.Abs(now - prev) >= 14) hits++;
                    samples++;
                }
                if (samples > 0)
                {
                    var score = (double)hits / samples;
                    if (score >= 0.45) scores.Add((x, score));
                }
            }

            var lines = new List<int>();
            for (var i = 0; i < scores.Count;)
            {
                var best = scores[i];
                var j = i + 1;
                while (j < scores.Count && scores[j].x <= scores[j - 1].x + 2)
                {
                    if (scores[j].score > best.score) best = scores[j];
                    j++;
                }
                if (lines.Count == 0 || best.x - lines[^1] >= 6) lines.Add(best.x);
                i = j;
            }
            return lines;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int Luma(byte[] bytes, int stride, int x, int y)
    {
        var i = y * stride + x * 4;
        var b = bytes[i];
        var g = bytes[i + 1];
        var r = bytes[i + 2];
        return (r * 30 + g * 59 + b * 11) / 100;
    }
}
