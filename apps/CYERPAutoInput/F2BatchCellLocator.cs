using System.Drawing.Imaging;
using System.Globalization;

namespace CYERPAutoInput;

internal sealed record BatchStockSelection(Point Point, int RowNumber, decimal Stock);

internal sealed class F2BatchCellLocator
{
    private readonly PaddleOcrService _ocr;
    private readonly AppLogger _log;

    public F2BatchCellLocator(PaddleOcrService ocr, AppLogger log)
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
        // Screen capture reads whatever is on top: in the real test (V0.2.0 Build 6) the CY
        // window covered part of the lookup and its own 單位 header was read into a stock
        // cell. Only read a frame that is unobstructed, stable and shows both batch headers
        // without unit text.
        Bitmap? image = null;
        IReadOnlyList<OcrToken> tokens = [];
        Rectangle? stockHeader = null;
        Rectangle? batchHeader = null;
        var ready = false;
        for (var attempt = 1; attempt <= 4 && !ready; attempt++)
        {
            await Task.Delay(attempt == 1 ? 250 : 400, cancellationToken);
            if (!await EnsureUnobstructedAsync(lookupHwnd, captureRect, cancellationToken))
                throw new InvalidOperationException("F2 批號查詢視窗被其他視窗遮住，無法辨識；已停止目前單據。");
            image?.Dispose();
            image = await CaptureStableAsync(captureRect, cancellationToken);
            tokens = await _ocr.RecognizeAsync(image, cancellationToken, requireChinese: true);
            // The sorted 現有存量 header is drawn on a yellow background; accept a partial read.
            stockHeader = FastDetailGridVisionService.FindExactPhrase(tokens, ["現有存量", "有存量", "現有存", "存量"]);
            batchHeader = FastDetailGridVisionService.FindExactPhrase(tokens, ["批號"]);
            var staleUnitLookup = FastDetailGridVisionService.FindExactPhrase(tokens, ["換算", "單位"]) is not null;
            ready = stockHeader is not null && batchHeader is not null && !staleUnitLookup;
            if (!ready)
            {
                _log.Info("vision", $"F2 batch lookup not ready attempt={attempt} stock_header={stockHeader is not null} batch_header={batchHeader is not null} stale_unit_text={staleUnitLookup}");
                LogTokens(tokens, attempt);
            }
        }
        using var lookupImage = image;
        if (image is null || !ready || stockHeader is null)
            throw new InvalidOperationException("F2 批號查詢畫面未穩定顯示「批號／現有存量」欄；已停止目前單據，請查看 LOG 的 F2_BATCH_TOKEN。");

        var rawVertical = GridVisionService.FindVerticalLines(image, Math.Min(image.Height - 1, 120));
        var boundaries = NormalizeBoundaries(rawVertical, image.Width);
        var stockInterval = FindInterval(boundaries, stockHeader.Value.Left + stockHeader.Value.Width / 2);
        if (stockInterval < 0 || stockInterval + 1 >= boundaries.Count)
            throw new InvalidOperationException("F2 批號查詢無法由即時格線定位「現有存量」欄；已停止目前單據。");
        var stockLeft = boundaries[stockInterval];
        var stockRight = boundaries[stockInterval + 1];
        if (stockRight - stockLeft < 24)
            throw new InvalidOperationException("F2 批號查詢欄寬異常；已停止目前單據。");

        // 批號 only anchors the row centers; without it the grid lines give them.
        var batchInterval = batchHeader is null ? -1 : FindInterval(boundaries, batchHeader.Value.Left + batchHeader.Value.Width / 2);
        var hasBatchColumn = batchInterval >= 0 && batchInterval + 1 < boundaries.Count && batchInterval != stockInterval;
        var batchLeft = hasBatchColumn ? boundaries[batchInterval] : 0;
        var batchRight = hasBatchColumn ? boundaries[batchInterval + 1] : 0;

        var dataStart = Math.Max(stockHeader.Value.Bottom, batchHeader?.Bottom ?? 0) + 12;
        var rowCenters = hasBatchColumn ? BuildRowCentersFromBatchTokens(tokens, batchLeft, batchRight, dataStart) : [];
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
            var (rawText, variant) = await ReadStockCellAsync(cell, cancellationToken);
            if (!InputRules.TryParseStockText(rawText, out _))
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
                variant = "window";
            }

            // Grid-line rows may include the filter row ("=" under 現有存量); it is not data.
            if (index == 0 && !hasBatchColumn && rawText.Trim() is "=" or "＝")
            {
                _log.Info("vision", "F2 batch filter row skipped (grid-line rows)");
                continue;
            }

            var normalized = rawText.Trim().Replace(" ", string.Empty).Replace("　", string.Empty).Replace(",", string.Empty).Replace("，", string.Empty);
            if (!InputRules.TryParseStockText(rawText, out var stock))
            {
                _log.Info("vision", $"F2_BATCH_STOCK_ROW row={rowNumber} raw={_log.Value(Sanitize(rawText))} normalized={_log.Value(Sanitize(normalized))} parse=false y={centerY} crop={crop.Width}x{crop.Height}");
                // Skipping an unreadable row could pick a later batch while an earlier one
                // still has stock; the rule is "first confirmed positive from the top".
                throw new InvalidOperationException($"F2 批號查詢第 {rowNumber} 列的「現有存量」無法辨識；為避免跳過較早的批號已停止，請人工選擇批號並查看 LOG 的 F2_BATCH_STOCK_ROW。");
            }

            _log.Info("vision", $"F2_BATCH_STOCK_ROW row={rowNumber} raw={_log.Value(Sanitize(rawText))} normalized={_log.Value(Sanitize(normalized))} stock={stock.ToString(CultureInfo.InvariantCulture)} y={centerY} variant={variant}");
            if (stock <= 0) continue;

            var point = new Point(captureRect.Left + (stockLeft + stockRight) / 2, captureRect.Top + centerY);
            _log.Info("vision", $"F2 batch positive stock selected by row-bound OCR row={rowNumber} point={point.X},{point.Y} stock={stock.ToString(CultureInfo.InvariantCulture)}");
            return new BatchStockSelection(point, rowNumber, stock);
        }

        throw new InvalidOperationException("F2 批號查詢沒有找到可確認的「現有存量 > 0」批號；已停止，請查看 LOG 的 F2_BATCH_STOCK_ROW 原始辨識內容。");
    }

    /// <summary>
    /// Makes sure nothing covers the lookup grid: brings the lookup to the front and, if a
    /// window of this process (the CY form) still covers it, sends that window to the bottom.
    /// </summary>
    private async Task<bool> EnsureUnobstructedAsync(nint lookupHwnd, Rectangle area, CancellationToken cancellationToken)
    {
        if (Win32Automation.IsAreaShownBy(lookupHwnd, area)) return true;
        _log.Warn("vision", "F2 batch lookup covered by another window; bringing it to the front");
        Win32Automation.PrepareForeground(lookupHwnd, _log);
        await Task.Delay(150, cancellationToken);
        if (Win32Automation.IsAreaShownBy(lookupHwnd, area)) return true;

        foreach (Form form in Application.OpenForms)
        {
            if (form.IsDisposed || !form.Visible) continue;
            var handle = form.Handle;
            form.Invoke(() => NativeMethods.SetWindowPos(handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | 0x0010 /* SWP_NOACTIVATE */));
        }
        _log.Warn("vision", "CY windows sent to the bottom so the F2 batch lookup is fully visible");
        Win32Automation.PrepareForeground(lookupHwnd, _log);
        await Task.Delay(200, cancellationToken);
        return Win32Automation.IsAreaShownBy(lookupHwnd, area);
    }

    /// <summary>Captures until two frames 200 ms apart are identical (at most ~1.4 s).</summary>
    private static async Task<Bitmap> CaptureStableAsync(Rectangle rect, CancellationToken cancellationToken)
    {
        var previous = ScreenCapture.Capture(rect);
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(200, cancellationToken);
            var current = ScreenCapture.Capture(rect);
            if (SameFrame(previous, current))
            {
                previous.Dispose();
                return current;
            }
            previous.Dispose();
            previous = current;
        }
        return previous;
    }

    private static bool SameFrame(Bitmap a, Bitmap b)
    {
        if (a.Size != b.Size) return false;
        for (var y = 0; y < a.Height; y += 3)
        for (var x = 0; x < a.Width; x += 3)
            if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
        return true;
    }

    private void LogTokens(IReadOnlyList<OcrToken> tokens, int attempt)
    {
        _log.Info("vision", $"F2_BATCH_TOKEN attempt={attempt} total={tokens.Count}");
        foreach (var t in tokens.Take(120))
            _log.Info("vision", $"F2_BATCH_TOKEN raw=\"{_log.Value(Sanitize(t.Text))}\" normalized=\"{_log.Value(Sanitize(OcrTextNormalizer.Normalize(t.Text)))}\" rect={t.Rect.Left},{t.Rect.Top},{t.Rect.Width},{t.Rect.Height}");
    }

    private static List<int> BuildRowCentersFromBatchTokens(IReadOnlyList<OcrToken> tokens, int left, int right, int dataStart)
    {
        var ys = tokens
            .Where(t => t.Rect.Top > dataStart)
            .Where(t =>
            {
                var text = OcrTextNormalizer.Normalize(t.Text).Trim();
                // Filter-row icons ("=", "ABC") are not data rows; batch numbers carry digits.
                if (!text.Any(char.IsDigit)) return false;
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

    /// <summary>
    /// Reads one 現有存量 cell. The text detector misses a lone short number (typically
    /// "0", often on the blue selected row), so the cell is first trimmed to its glyphs
    /// and read by the recognizer alone; detection on a padded, enlarged crop is the
    /// fallback.
    /// </summary>
    internal async Task<(string Text, string Variant)> ReadStockCellAsync(Bitmap cell, CancellationToken cancellationToken)
    {
        var last = string.Empty;
        using (var normalized = NormalizeCell(cell))
        using (var glyphs = TrimToGlyphs(normalized))
        {
            if (glyphs is null) return (string.Empty, "blank");
            var line = await _ocr.RecognizeLineAsync(glyphs, cancellationToken);
            if (line.Confidence >= MinLineConfidence && InputRules.TryParseStockText(line.Text, out _))
                return (line.Text, $"line-{line.Confidence:0.00}");
            // ERP draws a slashed zero; alone it reads as 0/O/Q at ~0.3–0.5.
            if (InputRules.TryAcceptLowConfidenceStock(line.Chars, out var accepted))
                return (accepted, $"line-unambiguous-{line.Confidence:0.00}");
            _log.Info("vision", $"F2 stock line read rejected text={_log.Value(Sanitize(line.Text))} confidence={line.Confidence:0.00} candidates={_log.Value(string.Join(" ", line.Chars.Select(c => $"{c.Text}:{c.Probability:0.00}[{string.Join(",", c.Alternatives.Select(a => $"{(a.Text.Length == 0 ? "_" : a.Text)}:{a.Probability:0.00}"))}]")))}");
            last = line.Text;
        }

        foreach (var scale in new[] { 4, 3 })
        {
            using var enhanced = EnhanceCell(cell, scale);
            using var padded = Pad(enhanced, enhanced.Height, enhanced.Height / 2);
            var text = JoinTokens(await _ocr.RecognizeAsync(padded, cancellationToken, requireChinese: false));
            if (InputRules.TryParseStockText(text, out _)) return (text, $"padded-x{scale}");
            if (text.Length > 0) last = text;
        }
        return (last, "none");
    }

    internal const float MinLineConfidence = 0.6f;

    /// <summary>
    /// Grayscale, dark glyphs on white: the polarity comes from whether the glyph pixels
    /// are darker or lighter than the cell background (not from the average brightness,
    /// which fails for black text on the mid-blue selected cell), then the range between
    /// the darkest glyph pixel and the background is stretched to full contrast.
    /// </summary>
    internal static Bitmap NormalizeCell(Bitmap source)
    {
        var w = source.Width;
        var h = source.Height;
        var luma = new int[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = source.GetPixel(x, y);
            luma[y * w + x] = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
        }
        var sorted = (int[])luma.Clone();
        Array.Sort(sorted);
        var background = sorted[sorted.Length / 2];
        var invert = sorted[^1] - background > background - sorted[0];
        if (invert)
        {
            for (var i = 0; i < luma.Length; i++) luma[i] = 255 - luma[i];
            background = 255 - background;
        }
        var darkest = luma.Min();
        var range = Math.Max(1, background - darkest);

        var output = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var v = Math.Clamp((luma[y * w + x] - darkest) * 255 / range, 0, 255);
            output.SetPixel(x, y, Color.FromArgb(255, v, v, v));
        }
        return output;
    }

    /// <summary>
    /// Crops an enhanced (dark-on-light) cell to its glyphs plus a small margin. Rows and
    /// columns that are almost entirely foreground are grid lines and are ignored.
    /// Returns null for a blank cell.
    /// </summary>
    internal static Bitmap? TrimToGlyphs(Bitmap source, int margin = 4)
    {
        var w = source.Width;
        var h = source.Height;
        var luma = new int[w, h];
        var values = new List<int>(w * h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = source.GetPixel(x, y);
            luma[x, y] = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
            values.Add(luma[x, y]);
        }
        values.Sort();
        var background = values[values.Count / 2];

        var mask = new bool[w, h];
        var rowCount = new int[h];
        var colCount = new int[w];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (Math.Abs(luma[x, y] - background) <= 60) continue;
            mask[x, y] = true;
            rowCount[y]++;
            colCount[x]++;
        }

        int left = w, right = -1, top = h, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            if (rowCount[y] > w * 0.6) continue;
            for (var x = 0; x < w; x++)
            {
                if (!mask[x, y] || colCount[x] > h * 0.8) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0) return null;

        var rect = Rectangle.FromLTRB(
            Math.Max(0, left - margin), Math.Max(0, top - margin),
            Math.Min(w, right + margin + 1), Math.Min(h, bottom + margin + 1));
        return source.Clone(rect, PixelFormat.Format32bppArgb);
    }

    private static string JoinTokens(IReadOnlyList<OcrToken> tokens) => string.Concat(tokens
        .OrderBy(t => t.Rect.Top / 12)
        .ThenBy(t => t.Rect.Left)
        .Select(t => t.Text));

    private static Bitmap Pad(Bitmap source, int padX, int padY)
    {
        var output = new Bitmap(source.Width + padX * 2, source.Height + padY * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.Clear(Color.White);
        g.DrawImageUnscaled(source, padX, padY);
        return output;
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
