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

    private const int WheelNotchesPerStep = 4;
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

    public async Task<Result> ScanAsync(nint browser, Func<bool> stopRequested, CancellationToken cancellationToken)
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

        // From the top of the page.
        for (var i = 0; i < 12; i++)
        {
            var before = Signature(await StableCaptureAsync(area, cancellationToken), dispose: true);
            Wheel(wheelPoint, +15);
            var after = Signature(await StableCaptureAsync(area, cancellationToken), dispose: true);
            if (ShopeePageScan.SameScreen(before, after)) break;
        }

        List<long>? previous = null;
        List<OcrBox>? previousBoxes = null;
        var pageTop = 0;
        for (var step = 0; step < MaxSteps; step++)
        {
            if (stopRequested()) { result.Cancelled = true; break; }
            var image = await StableCaptureAsync(area, cancellationToken);
            var signature = Signature(image, dispose: false);
            var tokens = await _ocr.RecognizeAsync(image, cancellationToken);
            var boxes = ShopeeScreenshot.MergeLines(tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)));
            if (previous is not null && previousBoxes is not null)
            {
                result.FixedTop = Math.Max(result.FixedTop, ShopeePageScan.StaticTopRows(previous, signature));
                var byPixels = ShopeePageScan.FindScrollOffset(previous, signature, result.FixedTop);
                var byText = ShopeePageScan.FindScrollOffsetByText(previousBoxes, boxes, result.FixedTop);
                // Text positions are the tie-breaker: they ignore shadows and hover effects.
                var moved = byText >= 0 && (byPixels < 0 || Math.Abs(byPixels - byText) > 3) ? byText : byPixels;
                _log.Info("page-scan", $"step={step} moved={moved} pixels={byPixels} text={byText} fixed_top={result.FixedTop}");
                if (moved < 0)
                {
                    image.Dispose();
                    result.Problems.Add("捲動後畫面對不上（頁面可能在捲動時變動），請改用框選截圖或貼上。");
                    break;
                }
                if (moved == 0) { image.Dispose(); break; } // bottom of the page
                pageTop += moved;
            }
            result.Captures.Add(new Capture(image, pageTop));
            previous = signature;
            previousBoxes = boxes;
            ShopeePageScan.AddCapture(result.Page, boxes, pageTop, result.FixedTop);
            if (ShopeePageScan.HasBuyerTotal(result.Page)) break;
            Wheel(wheelPoint, -WheelNotchesPerStep);
            // Park the cursor on the page's empty left margin so no hover effect changes the next capture.
            NativeMethods.SetCursorPos(area.Left + 12, wheelPoint.Y);
        }

        // The fixed band (browser bar, Shopee's header) is only known after the first scroll;
        // drop what the first capture put on the page from it.
        result.Page.RemoveAll(b => b.Top < result.FixedTop);
        if (!result.Cancelled) result.Sections = ShopeePageScan.Sections(result.Page, result.Problems);
        _log.Info("page-scan", $"captures={result.Captures.Count} boxes={result.Page.Count} sections={result.Sections.Count} cancelled={result.Cancelled}");
        foreach (var b in result.Page)
            _log.Info("page-scan", $"  page box x={b.Left} y={b.Top} w={b.Width} h={b.Height} text={_log.Value(b.Text)}");
        return result;
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
