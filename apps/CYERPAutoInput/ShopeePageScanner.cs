using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// Scrolls the browser showing a Shopee order page from the top and captures it step by step
/// (user, 2026-10-11). Each scroll is measured from the pixels (<see cref="ShopeePageScan"/>),
/// so the browser's zoom and the scroll speed do not matter; the scan stops once the
/// 買家實付金額 card's total is on the page or the page stops moving.
/// </summary>
internal sealed class ShopeePageScanner
{
    internal sealed record Capture(Bitmap Image, int PageTop);

    internal sealed class Result
    {
        public List<Capture> Captures { get; } = [];
        public int FixedTop { get; set; }
        public List<OcrBox> Page { get; } = [];
        public List<ShopeePageSection> Sections { get; set; } = [];
        public List<string> Problems { get; } = [];
        public bool Cancelled { get; set; }
    }

    /// <summary>The first scroll; later ones are sized from what it measured.</summary>
    private const int FirstWheelNotches = 4;
    private const int MaxSteps = 40;
    private readonly PaddleOcrService _ocr;
    private readonly AppLogger _log;

    public ShopeePageScanner(PaddleOcrService ocr, AppLogger log)
    {
        _ocr = ocr;
        _log = log;
    }

    /// <summary>
    /// The browser window showing Shopee: the topmost visible window, not CY's own, whose
    /// title names Shopee／蝦皮; otherwise the topmost visible Chrome／Edge／Firefox window.
    /// </summary>
    public static nint FindBrowser()
    {
        var own = (uint)Environment.ProcessId;
        nint titled = 0, browser = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var r) || r.Width < 400 || r.Height < 300) return true;
            var title = NativeMethods.WindowText(hwnd);
            var cls = NativeMethods.ClassName(hwnd);
            if (titled == 0 && (title.Contains("Shopee", StringComparison.OrdinalIgnoreCase) || title.Contains("蝦皮", StringComparison.Ordinal)))
                titled = hwnd;
            if (browser == 0 && (cls == "Chrome_WidgetWin_1" || cls == "MozillaWindowClass")) browser = hwnd;
            return titled == 0;
        }, 0);
        return titled != 0 ? titled : browser;
    }

    /// <param name="scrolled">Called once the browser is no longer needed (scrolling done), before the reads finish.</param>
    /// <param name="readProgress">Share of the reading done, 0 to 1 (by pixel rows read).</param>
    public async Task<Result> ScanAsync(nint browser, Func<bool> stopRequested, CancellationToken cancellationToken,
        Action? scrolled = null, IProgress<double>? readProgress = null)
    {
        var result = new Result();
        if (NativeMethods.IsIconic(browser)) NativeMethods.ShowWindow(browser, 9 /* SW_RESTORE */);
        NativeMethods.SetForegroundWindow(browser);
        await Task.Delay(400, cancellationToken);
        if (!NativeMethods.GetWindowRect(browser, out var raw))
        {
            result.Problems.Add("讀不到瀏覽器視窗的位置。");
            return result;
        }
        var area = Rectangle.Intersect(raw.ToRectangle(), Screen.FromHandle(browser).Bounds);
        var wheelPoint = new Point(area.Left + area.Width * 2 / 5, area.Top + area.Height * 3 / 5);
        _log.Info("page-scan", $"browser window class={NativeMethods.ClassName(browser)} area={area.Width}x{area.Height}");

        // From the top of the page (user, 2026-10-11): the page may have been left scrolled.
        // A click on the page's empty left margin gives it the keyboard focus (not the address
        // bar), then Home jumps to the top.
        InputSender.Click(new Point(area.Left + 12, wheelPoint.Y));
        await Task.Delay(120, cancellationToken);
        InputSender.Press(0x24 /* VK_HOME */);
        _log.Info("page-scan", "Home pressed to start from the top");

        // Scrolling only captures (user, 2026-10-11: the reads were the wait); the cards are read
        // afterwards. OCR runs here only when the pixels cannot tell how far the page moved.
        List<long>? previous = null;
        Bitmap? previousImage = null;
        var notches = 0;
        var pixelsPerNotch = 0.0;
        var pageTop = 0;
        for (var step = 0; step < MaxSteps; step++)
        {
            if (stopRequested()) { result.Cancelled = true; break; }
            var image = await StableCaptureAsync(area, cancellationToken);
            var signature = Signature(image, dispose: false);
            if (previous is not null && previousImage is not null)
            {
                // Bottom of the page: nothing moved.
                if (ShopeePageScan.SameScreen(previous, signature))
                {
                    _log.Info("page-scan", $"step={step} bottom reached");
                    image.Dispose();
                    break;
                }
                // The fixed band is measured on the first scroll only.
                if (result.FixedTop == 0) result.FixedTop = ShopeePageScan.StaticTopRows(previous, signature);
                var byPixels = ShopeePageScan.FindScrollOffset(previous, signature, result.FixedTop);
                // Text positions decide only when the pixels could not (shadow, hover, animation).
                var byText = byPixels < 0
                    ? ShopeePageScan.FindScrollOffsetByText(await ReadAsync(previousImage, 0, cancellationToken), await ReadAsync(image, 0, cancellationToken), result.FixedTop)
                    : -1;
                var moved = byPixels >= 0 ? byPixels : byText;
                _log.Info("page-scan", $"step={step} moved={moved} pixels={byPixels} text={byText} fixed_top={result.FixedTop}");
                if (moved < 0)
                {
                    image.Dispose();
                    result.Problems.Add("捲動後畫面對不上（頁面可能在捲動時變動），請改用框選截圖或貼上。");
                    break;
                }
                if (moved == 0) { image.Dispose(); break; }
                pageTop += moved;
                if (pixelsPerNotch == 0 && notches > 0) pixelsPerNotch = moved / (double)notches;
            }
            result.Captures.Add(new Capture(image, pageTop));
            previous = signature;
            previousImage = image;

            // About 80 % of the visible page per scroll: enough overlap to line up.
            notches = pixelsPerNotch > 0
                ? Math.Max(1, (int)((image.Height - result.FixedTop) * 0.8 / pixelsPerNotch))
                : FirstWheelNotches;
            Wheel(wheelPoint, -notches);
            // Park the cursor on the page's empty left margin so no hover effect changes the next capture.
            NativeMethods.SetCursorPos(area.Left + 12, wheelPoint.Y);
        }
        // The page is captured; the user gets CY back while the cards are read (user, 2026-10-11).
        scrolled?.Invoke();
        if (!result.Cancelled && result.Captures.Count > 0)
            await ReadCardsAsync(result, readProgress, cancellationToken);

        // The fixed band (browser bar, Shopee's header) is only known after the first scroll;
        // drop what the first capture put on the page from it.
        result.Page.RemoveAll(b => b.Top < result.FixedTop);
        if (!result.Cancelled) result.Sections = ShopeePageScan.Sections(result.Page, result.Problems);
        _log.Info("page-scan", $"captures={result.Captures.Count} boxes={result.Page.Count} sections={result.Sections.Count} cancelled={result.Cancelled}");
        foreach (var b in result.Page)
            _log.Info("page-scan", $"  page box x={b.Left} y={b.Top} w={b.Width} h={b.Height} text={_log.Value(b.Text)}");
        return result;
    }

    /// <summary>
    /// Reads only the cards the order needs (user, 2026-10-11: keep accuracy, cut the wait).
    /// The page column is stitched, cut into cards at the grey gaps between them, and every
    /// card's title (its top-left corner) is read in one small OCR. Then only 訂單編號,
    /// 進帳資訊 (down to 隱藏入帳明細) and 買家實付金額 are read in full — the same pixels at the
    /// same size as before, so nothing read changes. If the cards cannot be told apart, the
    /// whole column is read as before.
    /// </summary>
    private async Task ReadCardsAsync(Result result, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var width = result.Captures[0].Image.Width;
        var column = Rectangle.FromLTRB(width * 15 / 100, result.FixedTop, width * 66 / 100,
            result.Captures.Max(c => c.PageTop + c.Image.Height));
        using var page = Stitch(result, column);
        var cards = ShopeePageScan.Cards(RowBackgroundShares(page));
        _log.Info("page-scan", $"cards={cards.Count} column={column.Width}x{column.Height}");

        // Titles: the top-left of each card, stacked into one image.
        const int TitleHeight = 70, TitleWidth = 320, Gap = 12;
        var wanted = new List<(ShopeePageScan.CardRange Card, string Title)>();
        if (cards.Count > 0)
        {
            using var titles = new Bitmap(TitleWidth, cards.Count * (TitleHeight + Gap), PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(titles))
            {
                g.Clear(Color.White);
                for (var i = 0; i < cards.Count; i++)
                    g.DrawImage(page, new Rectangle(0, i * (TitleHeight + Gap), TitleWidth, TitleHeight),
                        new Rectangle(0, cards[i].Top, TitleWidth, Math.Min(TitleHeight, cards[i].Bottom - cards[i].Top)), GraphicsUnit.Pixel);
            }
            progress?.Report(0);
            var tokens = await _ocr.RecognizeAsync(titles, cancellationToken, maxScale: 1.0);
            for (var i = 0; i < cards.Count; i++)
            {
                var band = (Top: i * (TitleHeight + Gap), Bottom: i * (TitleHeight + Gap) + TitleHeight);
                var title = string.Concat(tokens.Where(t => t.Rect.Top + t.Rect.Height / 2 >= band.Top && t.Rect.Top + t.Rect.Height / 2 < band.Bottom)
                    .OrderBy(t => t.Rect.Top).ThenBy(t => t.Rect.Left).Select(t => t.Text));
                _log.Info("page-scan", $"  card top={cards[i].Top + column.Top} height={cards[i].Bottom - cards[i].Top} title={_log.Value(title)}");
                if (ShopeePageScan.WantedCard(title)) wanted.Add((cards[i], title));
            }
        }

        var targeted = wanted.Count >= 3;
        if (!targeted) _log.Info("page-scan", $"card titles not all found wanted={wanted.Count}; reading the whole column");
        // The product table can run on past its card: a long order (user's PC, 2026-10-13: 8
        // products) showed a grey gap inside it, and the rest read as a card titled with a
        // product name. So 進帳資訊 is read on, over whatever follows, until 隱藏入帳明細 or the
        // next wanted card (買家實付金額).
        var parts = targeted
            ? wanted.Select((w, i) =>
            {
                var ledger = w.Title.Contains("進帳", StringComparison.Ordinal);
                var bottom = ledger ? (i + 1 < wanted.Count ? wanted[i + 1].Card.Top : page.Height) : w.Card.Bottom;
                return (w.Card.Top, Bottom: bottom, StopAtLedger: ledger);
            }).ToList()
            : [(0, page.Height, false)];
        // Progress follows the rows still to read (user, 2026-10-12: a step count stood still
        // while one long 進帳資訊 card was read); the title strip counts as a short card.
        const int TitleWork = 400;
        double total = TitleWork + parts.Sum(p => p.Item2 - p.Item1), done = TitleWork;
        progress?.Report(done / total);
        for (var i = 0; i < parts.Count; i++)
        {
            // Long cards (a 進帳資訊 with many products) are read in overlapping chunks; the
            // product table stops at 隱藏入帳明細, so the income breakdown below is not read.
            const int Chunk = 900, Overlap = 60;
            for (var top = parts[i].Top; top < parts[i].Bottom; top += Chunk - Overlap)
            {
                var bottom = Math.Min(parts[i].Bottom, top + Chunk);
                using var piece = page.Clone(Rectangle.FromLTRB(0, top, page.Width, bottom), page.PixelFormat);
                var tokens = await _ocr.RecognizeAsync(piece, cancellationToken, maxScale: 1.0);
                // A line the chunk edge cuts in half is read whole by the neighbouring chunk; each
                // line is kept by the chunk whose half of the overlap holds its centre.
                var first = top == parts[i].Top;
                var last = bottom >= parts[i].Bottom;
                tokens = tokens.Where(t =>
                {
                    var centre = t.Rect.Top + t.Rect.Height / 2;
                    return (first || centre >= Overlap / 2) && (last || centre < piece.Height - Overlap / 2);
                }).ToList();
                var boxes = ShopeeScreenshot.MergeLines(tokens.Select(t => new OcrBox(t.Text, t.Rect.Left + column.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)));
                ShopeePageScan.AddCapture(result.Page, boxes, top + column.Top, 0);
                done += (last ? bottom : bottom - Overlap) - top;
                progress?.Report(Math.Min(1, done / total));
                if (parts[i].StopAtLedger && boxes.Any(b => b.Text.Contains("入帳明細", StringComparison.Ordinal) || b.Text.StartsWith("訂單金額", StringComparison.Ordinal)))
                    break;
                if (bottom >= parts[i].Bottom) break;
            }
        }
        progress?.Report(1);
    }

    /// <summary>Per row of the stitched column, the share of pixels in Shopee's grey page background.</summary>
    private static List<double> RowBackgroundShares(Bitmap page)
    {
        var shares = new List<double>(page.Height);
        var data = page.LockBits(new Rectangle(0, 0, page.Width, page.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var buffer = new byte[data.Stride];
            for (var y = 0; y < page.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, 0, data.Stride);
                int grey = 0, count = 0;
                for (var x = 0; x < page.Width; x += 3, count++)
                {
                    int b = buffer[x * 3], gr = buffer[x * 3 + 1], r = buffer[x * 3 + 2];
                    if (r is >= 241 and <= 248 && Math.Abs(r - gr) <= 2 && Math.Abs(r - b) <= 2) grey++;
                }
                shares.Add(count == 0 ? 0 : grey / (double)count);
            }
        }
        finally { page.UnlockBits(data); }
        return shares;
    }

    /// <summary>OCR of a capture's content column from <paramref name="top"/> down, in capture coordinates.</summary>
    private async Task<List<OcrBox>> ReadAsync(Bitmap image, int top, CancellationToken cancellationToken)
    {
        // The order cards sit at about 16 %–64 % of the window (user's Chrome, 2026-10-11), and
        // page text is ordinary screen text read at its own size: about half the pixels of
        // Build 7 (10 %–72 % at 1.25×), whose reads took ~7.7 s after the scrolling.
        var column = Rectangle.FromLTRB(image.Width * 15 / 100, top, image.Width * 66 / 100, image.Height);
        using var part = image.Clone(column, image.PixelFormat);
        var tokens = await _ocr.RecognizeAsync(part, cancellationToken, maxScale: 1.0);
        return ShopeeScreenshot.MergeLines(tokens.Select(t => new OcrBox(t.Text, t.Rect.Left + column.Left, t.Rect.Top + column.Top, t.Rect.Width, t.Rect.Height)));
    }

    /// <summary>
    /// The order from Chrome's page text (user, 2026-10-13): every piece of text of the whole
    /// page with its position, so no scrolling and no OCR. Positions are relative to the page's
    /// top left; pieces close together on a row are joined as the OCR lines were, and the three
    /// parts are cut as from the scan. Null when Chrome gives no page.
    /// </summary>
    public static Result? ReadPageText(nint browser, AppLogger log)
    {
        var texts = ChromePageText.Read(browser, log, out var elapsed);
        var document = texts.Where(t => t.Type == "Document").OrderByDescending(t => t.Rect.Width * t.Rect.Height).FirstOrDefault();
        if (document is null) return null;
        var result = new Result();
        var boxes = ChromePageText.PageBoxes(texts, document.Rect, pieces: true)
            .Where(b => b.Left >= 0 && b.Left < document.Rect.Width);
        result.Page.AddRange(ShopeeScreenshot.MergeLines(boxes));
        result.Sections = ShopeePageScan.Sections(result.Page, result.Problems);
        log.Info("page-text", $"order page text lines={result.Page.Count} sections={result.Sections.Count} problems={result.Problems.Count} elapsed_ms={elapsed}");
        foreach (var b in result.Page)
            log.Info("page-text", $"  page box x={b.Left} y={b.Top} w={b.Width} h={b.Height} text={log.Value(b.Text)}");
        return result;
    }

    /// <summary>A picture of a part read as text, for the card on the screen (the text drawn where it stands).</summary>
    public static Bitmap TextImage(ShopeePageSection section)
    {
        var bitmap = new Bitmap(Math.Max(1, section.PageArea.Width), Math.Max(1, section.PageArea.Height), PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Microsoft JhengHei UI", 10f, GraphicsUnit.Pixel);
        foreach (var line in section.Lines)
        {
            using var lineFont = new Font("Microsoft JhengHei UI", Math.Clamp(line.Height * 0.72f, 8f, 28f), GraphicsUnit.Pixel);
            g.DrawString(line.Text, lineFont, Brushes.Black, line.Left, line.Top);
        }
        return bitmap;
    }

    /// <summary>The page area assembled from the captures that show it.</summary>
    public static Bitmap Stitch(Result result, Rectangle pageArea)
    {
        var bitmap = new Bitmap(Math.Max(1, pageArea.Width), Math.Max(1, pageArea.Height), PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        foreach (var capture in result.Captures)
        {
            // Visible page rows of this capture, below the fixed band.
            var visible = Rectangle.FromLTRB(0, capture.PageTop + result.FixedTop, capture.Image.Width, capture.PageTop + capture.Image.Height);
            var part = Rectangle.Intersect(visible, pageArea);
            if (part.Width <= 0 || part.Height <= 0) continue;
            var source = new Rectangle(part.Left, part.Top - capture.PageTop, part.Width, part.Height);
            var target = new Rectangle(part.Left - pageArea.Left, part.Top - pageArea.Top, part.Width, part.Height);
            g.DrawImage(capture.Image, target, source, GraphicsUnit.Pixel);
        }
        return bitmap;
    }

    private static async Task<Bitmap> StableCaptureAsync(Rectangle area, CancellationToken cancellationToken)
    {
        // Smooth scrolling and lazy images: wait until two captures in a row match.
        var image = ScreenCapture.Capture(area);
        var signature = Signature(image, dispose: false);
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(180, cancellationToken);
            var next = ScreenCapture.Capture(area);
            var nextSignature = Signature(next, dispose: false);
            image.Dispose();
            image = next;
            if (ShopeePageScan.SameScreen(nextSignature, signature)) break;
            signature = nextSignature;
        }
        return image;
    }

    /// <summary>Per-row brightness of the page's content column (15 %–65 % of the width).</summary>
    private static List<long> Signature(Bitmap image, bool dispose)
    {
        var rows = new List<long>(image.Height);
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[data.Stride];
            var x0 = image.Width * 15 / 100;
            var x1 = image.Width * 65 / 100;
            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, 0, data.Stride);
                long sum = 0;
                for (var x = x0; x < x1; x += 2)
                {
                    var i = x * 4;
                    sum += (buffer[i] + buffer[i + 1] + buffer[i + 2]) / 3;
                }
                rows.Add(sum);
            }
        }
        finally
        {
            image.UnlockBits(data);
            if (dispose) image.Dispose();
        }
        return rows;
    }

    private static void Wheel(Point point, int notches)
    {
        NativeMethods.SetCursorPos(point.X, point.Y);
        Thread.Sleep(30);
        var input = new NativeMethods.INPUT
        {
            type = 0,
            U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { mouseData = unchecked((uint)(notches * 120)), dwFlags = 0x0800 /* MOUSEEVENTF_WHEEL */ } }
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
