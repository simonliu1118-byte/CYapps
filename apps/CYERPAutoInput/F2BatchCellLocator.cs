using System.Drawing.Imaging;
using System.Globalization;

namespace CYERPAutoInput;

internal sealed record BatchStockSelection(Point Point, int RowNumber, decimal Stock);

internal sealed class F2BatchCellLocator
{
    private readonly WindowsOcrService _ocr;
    private readonly AppLogger _log;

    public F2BatchCellLocator(WindowsOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    public async Task<BatchStockSelection> FindFirstPositiveStockAsync(
        nint lookupHwnd,
        CancellationToken cancellationToken)
    {
        var grid = Win32Automation.EnumerateChildren(lookupHwnd)
            .Where(c => c.Visible && c.ClassName.Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Rect.Width >= 180 && c.Rect.Height >= 70)
            .OrderByDescending(c => c.Rect.Width * c.Rect.Height)
            .FirstOrDefault();
        if (grid is null)
            throw new InvalidOperationException("F2 批號查詢找不到可用的 TcxGridSite；已停止目前單據。");

        var captureRect = grid.Rect.ToRectangle();
        using var image = ScreenCapture.Capture(captureRect);
        var tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
        var header = FastDetailGridVisionService.FindExactPhrase(tokens, ["現有存量"]);
        if (header is null)
            throw new InvalidOperationException("F2 批號查詢無法可靠辨識「現有存量」欄；已停止目前單據。");

        var rawVertical = GridVisionService.FindVerticalLines(image, Math.Min(image.Height - 1, 120));
        var boundaries = NormalizeBoundaries(rawVertical, image.Width);
        var headerCenterX = header.Value.Left + header.Value.Width / 2;
        var interval = FindInterval(boundaries, headerCenterX);
        if (interval < 0 || interval + 1 >= boundaries.Count)
            throw new InvalidOperationException("F2 批號查詢無法由即時格線定位「現有存量」欄；已停止目前單據。");

        var left = boundaries[interval];
        var right = boundaries[interval + 1];
        if (right - left < 24)
            throw new InvalidOperationException("F2 批號查詢的「現有存量」欄寬異常；已停止目前單據。");

        var horizontal = GridVisionService.FindHorizontalLines(image, Math.Max(0, header.Value.Bottom - 5));
        var first = horizontal.FindIndex(y => y >= header.Value.Bottom - 3);
        if (first < 0 || horizontal.Count - first < 2)
            throw new InvalidOperationException("F2 批號查詢無法辨識資料列；已停止目前單據。");

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
            var cellTokens = await _ocr.RecognizeAsync(enhanced, cancellationToken);
            var rawText = string.Concat(cellTokens
                .OrderBy(t => t.Rect.Top)
                .ThenBy(t => t.Rect.Left)
                .Select(t => t.Text));

            if (!TryParseStockText(rawText, out var stock))
            {
                _log.Info("vision", $"F2 batch stock row={rowNumber} parse=false chars={rawText.Trim().Length}");
                continue;
            }

            _log.Info("vision", $"F2 batch stock row={rowNumber} stock={stock.ToString(CultureInfo.InvariantCulture)}");
            if (stock <= 0) continue;

            var point = new Point(
                captureRect.Left + (left + right) / 2,
                captureRect.Top + (top + bottom) / 2);
            _log.Info("vision", $"F2 batch first positive stock selected row={rowNumber} point={point.X},{point.Y} stock={stock.ToString(CultureInfo.InvariantCulture)}");
            return new BatchStockSelection(point, rowNumber, stock);
        }

        throw new InvalidOperationException("F2 批號查詢沒有找到「現有存量 > 0」的批號；已停止目前單據。");
    }

    internal static bool TryParseStockText(string raw, out decimal value)
    {
        value = 0;
        var text = raw.Trim()
            .Replace(" ", string.Empty)
            .Replace("　", string.Empty)
            .Replace(",", string.Empty)
            .Replace("，", string.Empty);
        if (text.Length == 0) return false;

        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value) ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    private static int FindInterval(IReadOnlyList<int> boundaries, int x)
    {
        for (var i = 0; i + 1 < boundaries.Count; i++)
            if (x >= boundaries[i] && x <= boundaries[i + 1]) return i;
        return -1;
    }

    private static List<int> NormalizeBoundaries(IReadOnlyList<int> rawLines, int width)
    {
        var boundaries = rawLines.Where(x => x >= 0 && x < width).Distinct().OrderBy(x => x).ToList();
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
                var contrasted = (int)Math.Round((luma - 128) * 1.35 + 128);
                var v = Math.Clamp(contrasted, 0, 255);
                output.SetPixel(x, y, Color.FromArgb(255, v, v, v));
            }
        }
        return output;
    }
}
