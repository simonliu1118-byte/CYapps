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
        var stockHeader = FastDetailGridVisionService.FindExactPhrase(tokens, ["現有存量"]);
        var batchHeader = FastDetailGridVisionService.FindExactPhrase(tokens, ["批號"]);
        if (stockHeader is null || batchHeader is null)
  throw new InvalidOperationException("F2 批號查詢無法可靠辨識「批號／現有存量」欄；已停止目前單據。");

        var rawVertical = GridVisionService.FindVerticalLines(image, Math.Min(image.Height - 1, 120));
        var boundaries = NormalizeBoundaries(rawVertical, image.Width);
        var stockInterval = FindInterval(boundaries, stockHeader.Value.Left + stockHeader.Value.Width / 2);
        var batchInterval = FindInterval(boundaries, batchHeader.Value.Left + batchHeader.Value.Width / 2);
        if (stockInterval < 0 || stockInterval + 1 >= boundaries.Count || batchInterval < 0 || batchInterval + 1 >= boundaries.Count)
  throw new InvalidOperationException("F2 批號查詢無法由即時格線定位「批號／現有存量」欄；已停止目前單據。");

        var stockLeft = boundaries[stockInterval];
        var stockRight = boundaries[stockInterval + 1];
        var batchLeft = boundaries[batchInterval];
        var batchRight = boundaries[batchInterval + 1];
        if (stockRight - stockLeft < 24 || batchRight - batchLeft < 24)
  throw new InvalidOperationException("F2 批號查詢欄寬異常；已停止目前單據。");

        var dataStart = Math.Max(stockHeader.Value.Bottom, batchHeader.Value.Bottom) + 12;
        var rowCenters = BuildRowCentersFromBatchTokens(tokens, batchLeft, batchRight, dataStart);
        if (rowCenters.Count == 0)
        {
  rowCenters = BuildRowCentersFromGridLines(image, dataStart);
  _log.Info("vision", $"F2 batch row centers fallback=grid-lines count={rowCenters.Count}");
        }
        else
        {
  _log.Info("vision", $"F2 batch row centers source=batch-ocr count={rowCenters.Count}");
        }

        for (var index = 0; index < rowCenters.Count; index++)
        {
  cancellationToken.ThrowIfCancellationRequested();
  var rowNumber = index + 1;
  var centerY = rowCenters[index];
  var top = Math.Max(dataStart, centerY - 11);
  var bottom = Math.Min(image.Height, centerY + 11);
  if (bottom - top < 8) continue;

  var crop = Rectangle.FromLTRB(
      Math.Clamp(stockLeft + 2, 0, image.Width - 1),
      Math.Clamp(top, 0, image.Height - 1),
      Math.Clamp(stockRight - 2, 1, image.Width),
      Math.Clamp(bottom, 1, image.Height));
  if (crop.Width < 8 || crop.Height < 8) continue;

  using var cell = image.Clone(crop, PixelFormat.Format32bppArgb);
  using var enhanced = EnhanceCell(cell, 4);
  var cellTokens = await _ocr.RecognizeAsync(enhanced, cancellationToken, requireChinese: false);
  var rawText = string.Concat(cellTokens
      .OrderBy(t => t.Rect.Top)
      .ThenBy(t => t.Rect.Left)
      .Select(t => t.Text));

  if (!TryParseStockText(rawText, out var stock))
  {
      var nearby = tokens
          .Where(t => Math.Abs((t.Rect.Top + t.Rect.Height / 2) - centerY) <= 10)
          .Where(t =>
          {
              var cx = t.Rect.Left + t.Rect.Width / 2;
              return cx >= stockLeft + 1 && cx <= stockRight - 1;
          })
          .OrderBy(t => t.Rect.Left)
          .Select(t => t.Text);
      rawText = string.Concat(nearby);
  }

  var normalized = rawText.Trim().Replace(" ", string.Empty).Replace("　", string.Empty).Replace(",", string.Empty).Replace("，", string.Empty);
  if (!TryParseStockText(rawText, out stock))
  {
      _log.Info("vision", $"F2_BATCH_STOCK_ROW row={rowNumber} raw={Sanitize(rawText)} normalized={Sanitize(normalized)} parse=false y={centerY}");
      continue;
  }

  _log.Info("vision", $"F2_BATCH_STOCK_ROW row={rowNumber} raw={Sanitize(rawText)} normalized={Sanitize(normalized)} stock={stock.ToString(CultureInfo.InvariantCulture)} y={centerY}");
  if (stock <= 0) continue;

  var point = new Point(captureRect.Left + (stockLeft + stockRight) / 2, captureRect.Top + centerY);
  _log.Info("vision", $"F2 batch positive stock selected by row-bound OCR row={rowNumber} point={point.X},{point.Y} stock={stock.ToString(CultureInfo.InvariantCulture)}");
  return new BatchStockSelection(point, rowNumber, stock);
        }

        throw new InvalidOperationException("F2 批號查詢沒有找到可確認的「現有存量 > 0」批號；已停止，請查看 LOG 的 F2_BATCH_STOCK_ROW 原始辨識內容。");
    }

    private static List<int> BuildRowCentersFromBatchTokens(IReadOnlyList<OcrToken> tokens, int left, int right, int dataStart)
    {
        var ys = tokens
  .Where(t => t.Rect.Top > dataStart)
  .Where(t =>
  {
      var text = OcrTextNormalizer.Normalize(t.Text).Trim();
      if (text.Length == 0 || text == "=") return false;
      var cx = t.Rect.Left + t.Rect.Width / 2;
      return cx >= left + 2 && cx <= right - 2;
  })
  .Select(t => t.Rect.Top + t.Rect.Height / 2)
  .OrderBy(y => y)
  .ToList();

        var centers = new List<int>();
        foreach (var y in ys)
        {
  if (centers.Count == 0 || y - centers[^1] > 9)
      centers.Add(y);
  else
      centers[^1] = (centers[^1] + y) / 2;
        }
        return centers.Take(20).ToList();
    }

    private static List<int> BuildRowCentersFromGridLines(Bitmap image, int dataStart)
    {
        var lines = GridVisionService.FindHorizontalLines(image, Math.Max(0, dataStart - 4));
        var centers = new List<int>();
        for (var i = 0; i + 1 < lines.Count; i++)
        {
  var top = lines[i];
  var bottom = lines[i + 1];
  var height = bottom - top;
  if (top < dataStart || height < 14 || height > 42) continue;
  centers.Add((top + bottom) / 2);
        }
        return centers.Take(20).ToList();
    }

    private static string Sanitize(string value)
    {
        var text = value.Replace((char)13, (char)32).Replace((char)10, (char)32).Trim();
        return text.Length <= 64 ? text : text[..64];
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
