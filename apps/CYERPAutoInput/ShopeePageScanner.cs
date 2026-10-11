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
    /// <param name="readProgress">Reads finished so far and in all.</param>
    public async Task<Result> ScanAsync(nint browser, Func<bool> stopRequested, CancellationToken cancellationToken,
        Action? scrolled = null, IProgress<(int Done, int Total)>? readProgress = null)
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

        // The browser scrolls on while earlier captures are still being read (user, 2026-10-11:
        // the first scroll waited for the first OCR); the reads finish in order behind it.
        List<long>? previous = null;
        Task<List<OcrBox>>? previousRead = null;
        var reads = new List<(Task<List<OcrBox>> Read, int PageTop)>();
        var placed = 0;
        var notches = 0;
        var pixelsPerNotch = 0.0;
        var pageTop = 0;
        bool PlaceFinishedReads()
        {
            while (placed < reads.Count && reads[placed].Read.IsCompleted)
            {
                ShopeePageScan.AddCapture(result.Page, reads[placed].Read.Result, reads[placed].PageTop, result.FixedTop);
                placed++;
            }
            return ShopeePageScan.HasBuyerTotal(result.Page);
        }

        for (var step = 0; step < MaxSteps; step++)
        {
            if (stopRequested()) { result.Cancelled = true; break; }
            if (PlaceFinishedReads()) break;
            var image = await StableCaptureAsync(area, cancellationToken);
            var signature = Signature(image, dispose: false);
            var byPixels = -1;
            if (previous is not null)
            {
                // Bottom of the page: nothing moved. (Measuring the fixed band on two identical
                // captures made the whole screen "fixed" and dropped every box; Build 6 on the
                // user's PC, once scrolling ran ahead of the reads.)
                if (ShopeePageScan.SameScreen(previous, signature))
                {
                    _log.Info("page-scan", $"step={step} bottom reached");
                    image.Dispose();
                    break;
                }
                // The fixed band is measured on the first scroll only.
                if (result.FixedTop == 0) result.FixedTop = ShopeePageScan.StaticTopRows(previous, signature);
                byPixels = ShopeePageScan.FindScrollOffset(previous, signature, result.FixedTop);
            }

            // Only the content column is read, and when the pixels told how far the page moved,
            // only the newly shown strip (with a margin).
            var stripTop = byPixels > 0 ? Math.Max(result.FixedTop, image.Height - byPixels - 80) : 0;
            var read = ReadAsync(image, stripTop, cancellationToken);
            if (previous is not null && previousRead is not null)
            {
                // Text positions decide only when the pixels could not (shadow, hover, animation).
                var byText = byPixels < 0 ? ShopeePageScan.FindScrollOffsetByText(await previousRead, await read, result.FixedTop) : -1;
                var moved = byPixels >= 0 ? byPixels : byText;
                _log.Info("page-scan", $"step={step} moved={moved} pixels={byPixels} text={byText} fixed_top={result.FixedTop} ocr_top={stripTop}");
                if (moved < 0)
                {
                    await read;
                    image.Dispose();
                    result.Problems.Add("捲動後畫面對不上（頁面可能在捲動時變動），請改用框選截圖或貼上。");
                    break;
                }
                if (moved == 0) { await read; image.Dispose(); break; } // bottom of the page
                pageTop += moved;
                if (pixelsPerNotch == 0 && notches > 0) pixelsPerNotch = moved / (double)notches;
            }
            result.Captures.Add(new Capture(image, pageTop));
            reads.Add((read, pageTop));
            previous = signature;
            previousRead = read;

            // About 80 % of the visible page per scroll: enough overlap to line up, little reread.
            notches = pixelsPerNotch > 0
                ? Math.Max(1, (int)((image.Height - result.FixedTop) * 0.8 / pixelsPerNotch))
                : FirstWheelNotches;
            Wheel(wheelPoint, -notches);
            // Park the cursor on the page's empty left margin so no hover effect changes the next capture.
            NativeMethods.SetCursorPos(area.Left + 12, wheelPoint.Y);
        }
        // The page is captured; the user gets CY back while the reads finish (user, 2026-10-11).
        scrolled?.Invoke();
        for (var i = 0; i < reads.Count; i++)
        {
            readProgress?.Report((i, reads.Count));
            await reads[i].Read;
        }
        readProgress?.Report((reads.Count, reads.Count));
        PlaceFinishedReads();

        // The fixed band (browser bar, Shopee's header) is only known after the first scroll;
        // drop what the first capture put on the page from it.
        result.Page.RemoveAll(b => b.Top < result.FixedTop);
        if (!result.Cancelled) result.Sections = ShopeePageScan.Sections(result.Page, result.Problems);
        _log.Info("page-scan", $"captures={result.Captures.Count} boxes={result.Page.Count} sections={result.Sections.Count} cancelled={result.Cancelled}");
        foreach (var b in result.Page)
            _log.Info("page-scan", $"  page box x={b.Left} y={b.Top} w={b.Width} h={b.Height} text={_log.Value(b.Text)}");
        return result;
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
