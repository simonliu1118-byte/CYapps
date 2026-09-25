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

        var stockTokens = tokens
            .Where(t => t.Rect.Top > header.Value.Bottom + 2)
            .Where(t =>
            {
                var cx = t.Rect.Left + t.Rect.Width / 2;
                return cx >= left + 1 && cx <= right - 1;
            })
            .OrderBy(t => t.Rect.Top + t.Rect.Height / 2)
            .ThenBy(t => t.Rect.Left)
            .ToList();

        var numericRow = 0;
        foreach (var token in stockTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = token.Text.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'").Trim();
            var normalized = token.Text.Trim()
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .Replace(",", string.Empty)
                .Replace("，", string.Empty);

            if (!TryParseStockText(token.Text, out var stock))
            {
                _log.Info("vision", $"F2_BATCH_STOCK_TOKEN raw=\"{raw}\" normalized=\"{normalized}\" parse=false rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
                continue;
            }

            numericRow++;
            _log.Info("vision", $"F2_BATCH_STOCK_TOKEN row={numericRow} raw=\"{raw}\" normalized=\"{normalized}\" stock={stock.ToString(CultureInfo.InvariantCulture)} rect={token.Rect.Left},{token.Rect.Top},{token.Rect.Width},{token.Rect.Height}");
            if (stock <= 0) continue;

            var point = new Point(
                captureRect.Left + (left + right) / 2,
                captureRect.Top + token.Rect.Top + token.Rect.Height / 2);
            _log.Info("vision", $"F2 batch positive stock selected by exact OCR row={numericRow} point={point.X},{point.Y} stock={stock.ToString(CultureInfo.InvariantCulture)}");
            return new BatchStockSelection(point, numericRow, stock);
        }

        throw new InvalidOperationException("F2 批號查詢沒有找到可確認的「現有存量 > 0」批號；已停止，請查看 LOG 的 F2_BATCH_STOCK_TOKEN 原始辨識內容。");
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
