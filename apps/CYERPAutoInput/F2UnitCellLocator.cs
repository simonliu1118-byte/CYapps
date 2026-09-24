using System.Drawing.Imaging;

namespace CYERPAutoInput;

internal sealed class F2UnitCellLocator
{
    private readonly WindowsOcrService _ocr;
    private readonly AppLogger _log;

    public F2UnitCellLocator(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<Point?> FindAsync(nint lookupHwnd, string requestedUnit, CancellationToken cancellationToken)
    {
        var target = Normalize(requestedUnit);
        if (target.Length == 0) return null;

        if (!TryResolveCaptureRect(lookupHwnd, out var captureRect))
            return null;

        using var image = ScreenCapture.Capture(captureRect);
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var header = GridVisionService.FindPhrase(tokens, ["換算單位"]);
        if (header is null)
        {
            _log.Warn("vision", "F2 cell fallback stopped: 換算單位 header was not recognized inside lookup grid");
            return null;
        }

        var boundaries = NormalizeBoundaries(
            GridVisionService.FindVerticalLines(image, Math.Min(image.Height - 1, Math.Max(96, header.Value.Bottom + 32))),
            image.Width);
        var headerCenterX = header.Value.Left + header.Value.Width / 2;
        var interval = FindInterval(boundaries, headerCenterX);
        if (interval < 0 || interval + 1 >= boundaries.Count)
        {
            _log.Warn("vision", $"F2 cell fallback stopped: unit-column interval not found boundaries={boundaries.Count}");
            return null;
        }

        var left = boundaries[interval];
        var right = boundaries[interval + 1];
        if (right - left < 24)
        {
            _log.Warn("vision", $"F2 cell fallback stopped: unit-column width too narrow width={right - left}");
            return null;
        }

        var horizontal = GridVisionService.FindHorizontalLines(image, Math.Max(0, header.Value.Bottom - 5));
        var first = horizontal.FindIndex(y => y >= header.Value.Bottom - 3);
        if (first < 0 || horizontal.Count - first < 2)
        {
            _log.Warn("vision", $"F2 cell fallback stopped: row boundaries not found count={horizontal.Count}");
            return null;
        }

        var rowNumber = 0;
        for (var i = first; i + 1 < horizontal.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = horizontal[i];
            var bottom = horizontal[i + 1];
            var height = bottom - top;
            if (height < 14 || height > 64) continue;
            rowNumber++;

            var crop = Rectangle.FromLTRB(
                Math.Clamp(left + 2, 0, image.Width - 1),
                Math.Clamp(top + 1, 0, image.Height - 1),
                Math.Clamp(right - 2, 1, image.Width),
                Math.Clamp(bottom - 1, 1, image.Height));
            if (crop.Width < 8 || crop.Height < 8) continue;

            using var cell = image.Clone(crop, PixelFormat.Format32bppArgb);
            using var enhanced = EnhanceCell(cell, 3);
            var cellTokens = await _ocr.RecognizeAsync(enhanced, cancellationToken, requireChinese: true);
            var cellText = Normalize(string.Concat(cellTokens
                .OrderBy(t => t.Rect.Top)
                .ThenBy(t => t.Rect.Left)
                .Select(t => t.Text)));

            _log.Info("vision", $"F2 unit cell OCR row={rowNumber} text=\"{Sanitize(cellText)}\" cell={left},{top},{right - left},{height}");
            if (cellText != target && !cellText.Contains(target, StringComparison.Ordinal))
                continue;

            var point = new Point(
                captureRect.Left + (left + right) / 2,
                captureRect.Top + (top + bottom) / 2);
            _log.Info("vision", $"F2 requested unit located by cell OCR row={rowNumber} point={point.X},{point.Y}");
            return point;
        }

        return null;
    }

    private bool TryResolveCaptureRect(nint lookupHwnd, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var grid = Win32Automation.EnumerateChildren(lookupHwnd)
            .Where(c => c.Visible && c.ClassName.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Width >= 120 && c.Rect.Height >= 60)
            .OrderByDescending(c => c.Rect.Width * c.Rect.Height)
            .FirstOrDefault();
        if (grid is not null)
        {
            rect = grid.Rect.ToRectangle();
            _log.Info("vision", $"F2 cell fallback using TcxGridSite size={rect.Width}x{rect.Height}");
            return rect.Width > 0 && rect.Height > 0;
        }

        if (!NativeMethods.GetWindowRect(lookupHwnd, out var lookupRect) || lookupRect.Width <= 0 || lookupRect.Height <= 0)
            return false;
        rect = lookupRect.ToRectangle();
        _log.Warn("vision", $"F2 cell fallback had no TcxGridSite; using lookup window size={rect.Width}x{rect.Height}");
        return true;
    }

    private static int FindInterval(IReadOnlyList<int> boundaries, int x)
    {
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            if (x >= boundaries[i] && x <= boundaries[i + 1]) return i;
        }
        return -1;
    }

    private static List<int> NormalizeBoundaries(IReadOnlyList<int> rawLines, int width)
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
                if (boundaries[i + 1] - boundaries[i] >= 18) continue;
                if (i == 0) boundaries.RemoveAt(i + 1);
                else boundaries.RemoveAt(i);
                changed = true;
                break;
            }
        }
        return boundaries;
    }

    private static Bitmap EnhanceCell(Bitmap source, int scale)
    {
        var width = Math.Max(1, source.Width * scale);
        var height = Math.Max(1, source.Height * scale);
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        var total = 0L;
        var samples = 0;
        for (var y = 0; y < source.Height; y += 2)
        for (var x = 0; x < source.Width; x += 2)
        {
            var c = source.GetPixel(x, y);
            total += (c.R * 30 + c.G * 59 + c.B * 11) / 100;
            samples++;
        }
        var average = samples == 0 ? 255 : (int)(total / samples);
        var invert = average < 145;

        for (var y = 0; y < height; y++)
        {
            var sy = Math.Min(source.Height - 1, y / scale);
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Min(source.Width - 1, x / scale);
                var c = source.GetPixel(sx, sy);
                var luma = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                if (invert) luma = 255 - luma;
                var v = luma < 178 ? 0 : 255;
                output.SetPixel(x, y, Color.FromArgb(255, v, v, v));
            }
        }
        return output;
    }

    private static string Normalize(string value) =>
        value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);

    private static string Sanitize(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
        return text.Length <= 80 ? text : text[..80];
    }
}
