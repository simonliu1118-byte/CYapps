using System.Drawing.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

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

internal sealed class WindowsOcrService
{
    private readonly AppLogger _log;
    public WindowsOcrService(AppLogger log) => _log = log;

    public async Task<IReadOnlyList<OcrToken>> RecognizeAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"CYERPAutoInput_ocr_{Guid.NewGuid():N}.png");
        try
        {
            bitmap.Save(temp, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();

            var file = await StorageFile.GetFileFromPathAsync(temp);
            await using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                var language = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(x => x.LanguageTag.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                    ?? OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (language is not null) engine = OcrEngine.TryCreateFromLanguage(language);
            }
            if (engine is null)
                throw new InvalidOperationException("Windows OCR engine is unavailable on this computer.");

            cancellationToken.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(software);
            var tokens = new List<OcrToken>();
            foreach (var line in result.Lines)
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                tokens.Add(new OcrToken(word.Text, Rectangle.Round(new RectangleF((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height))));
            }
            _log.Info("vision", $"OCR completed tokens={tokens.Count} image={bitmap.Width}x{bitmap.Height}");
            return tokens;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
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

    public GridVisionService(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<GridGeometry> AnalyzeDetailGridAsync(nint gridHwnd, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(gridHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("Detail grid rectangle is unavailable.");

        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken);
        var geometry = new GridGeometry { ScreenRect = rect.ToRectangle() };

        var headerBottom = 0;
        foreach (var pair in ColumnAliases)
        {
            var match = FindPhrase(tokens, pair.Value);
            if (match is null) continue;
            geometry.ColumnX[pair.Key] = match.Value.Left + match.Value.Width / 2;
            headerBottom = Math.Max(headerBottom, match.Value.Bottom);
        }

        if (!geometry.ColumnX.ContainsKey(0) || !geometry.ColumnX.ContainsKey(1))
            throw new InvalidOperationException("Optical detail header detection failed: item/quantity columns were not both found.");

        var horizontal = FindHorizontalLines(image, Math.Max(0, headerBottom - 6));
        var firstBoundaryIndex = horizontal.FindIndex(y => y >= headerBottom - 3);
        if (firstBoundaryIndex < 0 || horizontal.Count - firstBoundaryIndex < 2)
            throw new InvalidOperationException("Optical detail row-line detection failed.");

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

    public async Task<Point> FindUnitAsync(nint lookupHwnd, string requestedUnit, CancellationToken cancellationToken)
    {
        if (!NativeMethods.GetWindowRect(lookupHwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException("F2 lookup rectangle is unavailable.");
        using var image = ScreenCapture.Capture(rect.ToRectangle());
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken);

        var unitHeader = FindPhrase(tokens, ["換算單位"]);
        var target = Normalize(requestedUnit);
        if (target.Length == 0) throw new InvalidOperationException("Requested unit is blank.");

        var candidates = tokens
            .Where(t => Normalize(t.Text) == target)
            .Where(t => unitHeader is null || t.Rect.Top > unitHeader.Value.Bottom - 2)
            .ToList();

        if (unitHeader is not null && candidates.Count > 1)
        {
            var headerX = unitHeader.Value.Left + unitHeader.Value.Width / 2;
            candidates = candidates.OrderBy(t => Math.Abs((t.Rect.Left + t.Rect.Width / 2) - headerX)).ToList();
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("OCR could not find the requested unit in the F2 lookup.");

        var best = candidates[0].Rect;
        var point = new Point(rect.Left + best.Left + best.Width / 2, rect.Top + best.Top + best.Height / 2);
        _log.Info("vision", $"F2 requested unit located candidates={candidates.Count} point={point.X},{point.Y}");
        return point;
    }

    private static Rectangle? FindPhrase(IReadOnlyList<OcrToken> tokens, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            var a = Normalize(alias);
            var direct = tokens.FirstOrDefault(t => Normalize(t.Text).Contains(a, StringComparison.Ordinal));
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
                    text += Normalize(token.Text);
                    union = Rectangle.Union(union, token.Rect);
                    if (aliases.Any(a => text.Contains(Normalize(a), StringComparison.Ordinal))) return union;
                }
            }
        }
        return null;
    }

    private static string Normalize(string value) => value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);

    private static List<int> FindHorizontalLines(Bitmap source, int startY)
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

    private static int Luma(byte[] bytes, int stride, int x, int y)
    {
        var i = y * stride + x * 4;
        var b = bytes[i];
        var g = bytes[i + 1];
        var r = bytes[i + 2];
        return (r * 30 + g * 59 + b * 11) / 100;
    }
}
