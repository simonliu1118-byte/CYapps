using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CYERPAutoInput;

/// <summary>Why the batch stopped on an order; <see cref="Shipped"/> when Shopee had already shipped it.</summary>
internal sealed class ShopeeBatchStop(string message, bool shipped = false) : Exception(message)
{
    public bool Shipped { get; } = shipped;
}

/// <summary>What the batch knows of the order it is on, for the ERP step and the messages.</summary>
internal sealed class ShopeeBatchOrder
{
    public required string OrderSn { get; init; }
    public string Carrier { get; set; } = string.Empty;
    public string Tracking { get; set; } = string.Empty;
    public bool Shipped { get; set; }
}

/// <summary>
/// 蝦皮全自動 (user, 2026-10-12): ships and enters every pending order of 我的銷售 one by one in
/// a Chrome window of its own, so the user's other tabs are never touched. Per order: 待出貨 →
/// 安排出貨 → wait for the 寄件編號 → 列印寄件單 → 列印出貨文件 on the thermal printer → back
/// to the order (close the dialog) → the page scan and ERP (<c>enterErp</c>) → close the tab
/// and F5 the list. Only 蝦皮店到店 (隔日到貨) and 蝦皮店到宅 for now. Every screen is read
/// before anything is clicked; whatever does not look as expected stops the batch.
/// </summary>
internal sealed class ShopeeBatchRunner
{
    /// <summary>我的銷售 › 待出貨 › 待處理, sorted by the shipping deadline (the URL Shopee itself opens).</summary>
    public const string ListUrl = "https://seller.shopee.tw/portal/sale/order?type=toship&source=to_process&sort_by=ship_by_date_asc";

    private readonly PaddleOcrService _ocr;
    private readonly AppLogger _log;
    private readonly string _printer;
    private readonly int _scale;
    private readonly CancellationToken _token;
    private nint _window;

    public ShopeeBatchRunner(PaddleOcrService ocr, AppLogger log, string printer, int scale, CancellationToken token)
    {
        _ocr = ocr;
        _log = log;
        _printer = printer;
        _scale = scale;
        _token = token;
    }

    public nint Window => _window;

    /// <summary>The order being worked on (null between orders).</summary>
    public ShopeeBatchOrder? Current { get; private set; }

    /// <summary>Order numbers done in this batch (one coming back means the list did not move on).</summary>
    public List<string> Done { get; } = [];

    /// <param name="enterErp">The page scan and ERP for the order on screen; throws <see cref="ShopeeBatchStop"/> to stop.</param>
    /// <param name="status">Progress text for the main window.</param>
    public async Task RunAsync(Func<ShopeeBatchOrder, Task> enterErp, Action<string> status)
    {
        status("蝦皮全自動：開啟 Chrome…");
        await OpenWindowAsync();
        while (true)
        {
            _token.ThrowIfCancellationRequested();
            status($"蝦皮全自動：找待處理訂單（已完成 {Done.Count} 筆）…");
            Current = null;
            var order = await NextOrderAsync();
            if (order is null) break;
            Current = order;
            status($"蝦皮全自動：訂單 {order.OrderSn} 安排出貨…");
            await ShipAsync(order);
            status($"蝦皮全自動：訂單 {order.OrderSn} 輸入 ERP…");
            await enterErp(order);
            Done.Add(order.OrderSn);
            Current = null;
            _log.Info("shopee-batch", $"order done count={Done.Count}");
            await CloseOrderTabAsync(order);
        }
        Current = null;
        _log.Info("shopee-batch", $"finished orders={Done.Count}");
        // The window is the batch's own; it goes when everything is done.
        NativeMethods.PostMessage(_window, 0x0010 /* WM_CLOSE */, 0, 0);
    }

    // ---- the Chrome window ----

    private async Task OpenWindowAsync()
    {
        var chrome = ChromePath() ?? throw new ShopeeBatchStop("找不到 Chrome（chrome.exe）。蝦皮全自動需要 Chrome。");
        var before = ChromeWindows().ToHashSet();
        Process.Start(new ProcessStartInfo(chrome, $"--new-window \"{ListUrl}\"") { UseShellExecute = false })?.Dispose();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(300, _token);
            var added = ChromeWindows().Where(w => !before.Contains(w)).ToList();
            if (added.Count == 1)
            {
                _window = added[0];
                NativeMethods.ShowWindow(_window, 3 /* SW_MAXIMIZE */);
                Front();
                _log.Info("shopee-batch", "own Chrome window opened");
                return;
            }
        }
        throw new ShopeeBatchStop("開啟 Chrome 新視窗後找不到它。");
    }

    private static List<nint> ChromeWindows()
    {
        var list = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && NativeMethods.ClassName(hwnd) == "Chrome_WidgetWin_1" &&
                NativeMethods.GetWindowRect(hwnd, out var r) && r.Width >= 400 && r.Height >= 300 &&
                ProcessName(hwnd).Equals("chrome", StringComparison.OrdinalIgnoreCase))
                list.Add(hwnd);
            return true;
        }, 0);
        return list;
    }

    private static string ProcessName(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch (ArgumentException) { return string.Empty; }
        catch (InvalidOperationException) { return string.Empty; }
    }

    private static string? ChromePath()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string path && File.Exists(path)) return path;
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Brings the batch's window to the front (the ERP step leaves ERP in front).</summary>
    public void Front()
    {
        if (!NativeMethods.IsWindow(_window)) throw new ShopeeBatchStop("蝦皮全自動的 Chrome 視窗已被關閉。");
        if (NativeMethods.IsIconic(_window)) NativeMethods.ShowWindow(_window, 3 /* SW_MAXIMIZE */);
        NativeMethods.SetForegroundWindow(_window);
    }

    // ---- reading and clicking ----

    private sealed record Screen(Bitmap Image, Rectangle Area, List<OcrBox> Boxes) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    private async Task<Screen> ReadAsync()
    {
        Front();
        await Task.Delay(150, _token);
        if (!NativeMethods.GetWindowRect(_window, out var raw)) throw new ShopeeBatchStop("讀不到 Chrome 視窗的位置。");
        var area = Rectangle.Intersect(raw.ToRectangle(), System.Windows.Forms.Screen.FromHandle(_window).Bounds);
        var image = ScreenCapture.Capture(area);
        try
        {
            var tokens = await _ocr.RecognizeAsync(image, _token, maxScale: 1.0);
            return new Screen(image, area, tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The OCR engine failed on the whole window (user's PC, 2026-10-13: an
            // IndexOutOfRangeException inside it); the same pixels in two overlapping halves.
            _log.Warn("shopee-batch", $"window OCR failed, reading it in halves: {ex}");
            try { return new Screen(image, area, await ReadInHalvesAsync(image)); }
            catch { image.Dispose(); throw; }
        }
    }

    /// <summary>The window read as a top and a bottom half overlapping by 80 px; each line is kept by the half holding its centre.</summary>
    private async Task<List<OcrBox>> ReadInHalvesAsync(Bitmap image)
    {
        const int Overlap = 80;
        var split = image.Height / 2;
        var boxes = new List<OcrBox>();
        foreach (var (top, bottom) in new[] { (0, Math.Min(image.Height, split + Overlap / 2)), (Math.Max(0, split - Overlap / 2), image.Height) })
        {
            using var part = image.Clone(Rectangle.FromLTRB(0, top, image.Width, bottom), image.PixelFormat);
            var tokens = await _ocr.RecognizeAsync(part, _token, maxScale: 1.0);
            foreach (var t in tokens)
            {
                var centre = top + t.Rect.Top + t.Rect.Height / 2;
                if (top == 0 ? centre < split : centre >= split)
                    boxes.Add(new OcrBox(t.Text, t.Rect.Left, top + t.Rect.Top, t.Rect.Width, t.Rect.Height));
            }
        }
        return boxes;
    }

    /// <summary>Reads the window until <paramref name="probe"/> finds what it wants; stops the batch after <paramref name="seconds"/>.</summary>
    private async Task<(Screen Screen, T Value)> WaitAsync<T>(string what, int seconds, Func<Screen, T?> probe, bool shipped = false) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var screen = await ReadAsync();
            var value = probe(screen);
            if (value is not null) return (screen, value);
            screen.Dispose();
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds))
            {
                _log.Warn("shopee-batch", $"timeout waiting for {what}");
                throw new ShopeeBatchStop($"等不到{what}（{seconds} 秒）。", shipped);
            }
            await Task.Delay(600, _token);
        }
    }

    private void Click(Screen screen, OcrBox box, string what)
    {
        Front();
        var point = new Point(screen.Area.Left + box.CenterX, screen.Area.Top + box.CenterY);
        _log.Info("shopee-batch", $"click {what} at {point.X},{point.Y}");
        InputSender.Click(point);
        NativeMethods.SetCursorPos(screen.Area.Left + 12, screen.Area.Top + screen.Area.Height / 2);
    }

    private void Keys(string what, params ushort[] keys)
    {
        Front();
        _log.Info("shopee-batch", $"keys {what}");
        InputSender.Chord(keys);
    }

    // ---- the list ----

    /// <summary>
    /// The first order to do, or null when there is none (user, 2026-10-12): 建議今天內出貨
    /// first; when it has none, 建議出貨期限 › 全部; nothing there means done.
    /// </summary>
    private async Task<ShopeeBatchOrder?> NextOrderAsync()
    {
        var (screen, _) = await WaitAsync("「我的銷售 › 待處理」頁面", 30, s => ShopeeBatchScreens.IsOrderList(s.Boxes) && Settled(s.Boxes) ? s : null);
        // An order done in this batch still first: the list may not have caught up yet; F5
        // again a few times before giving up (never the same order twice).
        for (var retry = 0; ShopeeBatchScreens.FirstOrder(screen.Boxes) is { } stale && Done.Contains(stale.OrderSn); retry++)
        {
            screen.Dispose();
            if (retry >= 3) throw new ShopeeBatchStop($"訂單 {stale.OrderSn} 已處理過，卻還在待處理清單上；為避免重複出貨，已停止。");
            await Task.Delay(2000, _token);
            Keys("F5", NativeMethods.VK_F5);
            await Task.Delay(1500, _token);
            (screen, _) = await WaitAsync("「我的銷售 › 待處理」頁面", 30, s => ShopeeBatchScreens.IsOrderList(s.Boxes) && Settled(s.Boxes) ? s : null);
        }
        using (screen)
        {
            _log.Info("shopee-batch", $"list pending={ShopeeBatchScreens.PendingCount(screen.Boxes)} today={ShopeeBatchScreens.TodayCount(screen.Boxes)}");
            if (ShopeeBatchScreens.PendingCount(screen.Boxes) == 0) return null;
            if (ShopeeBatchScreens.FirstOrder(screen.Boxes) is { } first) return await OpenOrderAsync(screen, first);
            if (!ShopeeBatchScreens.NoOrders(screen.Boxes)) throw new ShopeeBatchStop("清單上讀不到第一筆訂單的「待出貨」或訂單編號。");
            var all = ShopeeBatchScreens.DeadlineAll(screen.Boxes) ?? throw new ShopeeBatchStop("讀不到「建議出貨期限」的「全部」。");
            Click(screen, all, "建議出貨期限 全部");
        }
        await Task.Delay(1200, _token);
        // Done only when 全部 has none and 待處理 does not count any: while it does, the list
        // is still loading (or the old 「找不到訂單」 is still shown).
        var (next, _) = await WaitAsync("「全部」的訂單清單", 20, s =>
            ShopeeBatchScreens.IsOrderList(s.Boxes) &&
            (ShopeeBatchScreens.FirstOrder(s.Boxes) is not null || ShopeeBatchScreens.NoOrders(s.Boxes) && !(ShopeeBatchScreens.PendingCount(s.Boxes) > 0))
                ? s : null);
        using (next)
        {
            if (ShopeeBatchScreens.FirstOrder(next.Boxes) is { } first) return await OpenOrderAsync(next, first);
            return null;
        }

        // Loaded: either an order or 「找不到訂單」 is on screen.
        static bool Settled(List<OcrBox> boxes) => ShopeeBatchScreens.NoOrders(boxes) || ShopeeBatchScreens.FirstOrder(boxes) is not null;
    }

    private async Task<ShopeeBatchOrder> OpenOrderAsync(Screen screen, ShopeeListOrder first)
    {
        if (Done.Contains(first.OrderSn))
            throw new ShopeeBatchStop($"訂單 {first.OrderSn} 已處理過，卻還在待處理清單上；為避免重複出貨，已停止。");
        _log.Info("shopee-batch", $"first order carrier={_log.Value(first.Carrier)}");
        if (!ShopeeBatchScreens.SupportedCarrier(first.Carrier))
            throw new ShopeeBatchStop($"訂單 {first.OrderSn} 的物流方式「{first.Carrier}」不是蝦皮店到店／店到宅，請人工處理。");
        var order = new ShopeeBatchOrder { OrderSn = first.OrderSn, Carrier = first.Carrier };
        Click(screen, first.Status, "待出貨");
        // The order opens in a new tab of this window.
        var (detail, _) = await WaitAsync("訂單明細頁", 30, s => ShopeeBatchScreens.IsOrderDetail(s.Boxes) && ShopeeBatchScreens.DetailOrderSn(s.Boxes).Length > 0 ? s : null);
        using (detail)
        {
            var shown = ShopeeBatchScreens.DetailOrderSn(detail.Boxes);
            if (shown != order.OrderSn)
                throw new ShopeeBatchStop($"開啟的訂單明細是 {shown}，不是清單上的 {order.OrderSn}；已停止。");
        }
        return order;
    }

    // ---- 安排出貨 → 列印寄件單 → 列印 ----

    private async Task ShipAsync(ShopeeBatchOrder order)
    {
        var (detail, button) = await WaitAsync("「安排出貨」按鈕", 15, s => ShopeeBatchScreens.ShipButton(s.Boxes));
        using (detail)
        {
            var carrier = ShopeeBatchScreens.DetailCarrier(detail.Boxes);
            if (carrier.Length > 0 && !ShopeeBatchScreens.SupportedCarrier(carrier))
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的運送資訊是「{carrier}」，不是蝦皮店到店／店到宅，請人工處理。");
            if (!Orange(detail.Image, button))
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的「{button.Text.Trim()}」按鈕顏色不對，為避免按錯已停止。");
            Click(detail, button, button.Text.Trim());
        }
        order.Shipped = true;

        // 寄件編號申請中… then the TW number.
        var (dialog, tracking) = await WaitAsync("寄件編號", 60, s =>
        {
            if (ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is null || ShopeeBatchScreens.TrackingRequested(s.Boxes)) return null;
            var t = ShopeeBatchScreens.DialogTracking(s.Boxes);
            return t.Length > 0 ? t : null;
        }, shipped: true);
        order.Tracking = tracking;
        _log.Info("shopee-batch", $"tracking assigned length={tracking.Length}");
        dialog.Dispose();

        await ClickPrintLabelAsync(order);

        // 預覽列印 opens in a new tab: 下載出貨文件中 → 下載完成.
        var (awb, download) = await WaitAsync("出貨文件下載完成", 120, s => ShopeeBatchScreens.IsAwbPage(s.Boxes) ? ShopeeBatchScreens.AwbDownloaded(s.Boxes) : null, shipped: true);
        using (awb)
        {
            if (download.Failed != 0 || download.Done != 1 || download.Total != 1)
                throw new ShopeeBatchStop($"出貨文件下載結果不對（成功 {download.Done}/{download.Total}，失敗 {download.Failed}）。", true);
            var labelSn = ShopeeBatchScreens.LabelOrderSn(awb.Boxes);
            if (labelSn != order.OrderSn)
                throw new ShopeeBatchStop($"寄件單上的訂單編號（{(labelSn.Length > 0 ? labelSn : "讀不到")}）不是 {order.OrderSn}，未列印。", true);
            var labelTracking = ShopeeBatchScreens.LabelTracking(awb.Boxes);
            if (labelTracking.Length > 0 && !ShopeeBatchScreens.SameTracking(labelTracking, order.Tracking))
                throw new ShopeeBatchStop($"寄件單上的寄件編號 {labelTracking} 與視窗顯示的 {order.Tracking} 不同，未列印。", true);
            var print = ShopeeBatchScreens.PrintDocumentsButton(awb.Boxes) ?? throw new ShopeeBatchStop("讀不到「列印出貨文件」按鈕。", true);
            Click(awb, print, "列印出貨文件");
        }

        // Chrome's own print dialog: the thermal printer at the set scale, or nothing is printed.
        // The dialog fills in its printer a moment after it opens, so it is read until it
        // matches; what it last showed is reported when it never does.
        var (opened, _) = await WaitAsync("Chrome 列印視窗", 30, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null, shipped: true);
        opened.Dispose();
        string destination = string.Empty;
        int? scale = null;
        var watch = Stopwatch.StartNew();
        while (true)
        {
            using var screen = await ReadAsync();
            destination = ShopeeBatchScreens.PrintDestination(screen.Boxes);
            scale = ShopeeBatchScreens.PrintScale(screen.Boxes);
            if (ShopeeBatchScreens.IsPrintDialog(screen.Boxes) && ShopeeBatchScreens.SamePrinter(destination, _printer) && scale == _scale &&
                ShopeeBatchScreens.PrintButton(screen.Boxes) is { } printButton)
            {
                _log.Info("shopee-batch", $"print dialog destination={_log.Value(destination)} scale={scale}");
                Click(screen, printButton, "列印");
                break;
            }
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                _log.Warn("shopee-batch", $"print dialog not as set destination={_log.Value(destination)} scale={scale}");
                if (!ShopeeBatchScreens.SamePrinter(destination, _printer))
                    throw new ShopeeBatchStop($"Chrome 列印的目的地是「{destination}」，不是設定的「{_printer}」，未列印。", true);
                if (scale != _scale)
                    throw new ShopeeBatchStop($"Chrome 列印的縮放比例是 {(scale?.ToString() ?? "讀不到")}，不是設定的 {_scale}，未列印。", true);
                throw new ShopeeBatchStop("Chrome 列印視窗讀不到「列印」按鈕，未列印。", true);
            }
            await Task.Delay(600, _token);
        }
        var (printed, _) = await WaitAsync("列印視窗關閉", 30, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? null : s, shipped: true);
        printed.Dispose();
        _log.Info("shopee-batch", "label printed");

        // Close the 預覽列印 tab: back to the order with its 查看寄件編號 dialog, then close it.
        Keys("Ctrl+W (預覽列印)", NativeMethods.VK_CONTROL, 0x57 /* W */);
        await Task.Delay(500, _token);
        await CloseTrackingDialogAsync();
    }

    /// <summary>「列印寄件單」 sits at the dialog's bottom; when the zoom hides it, the dialog is scrolled.</summary>
    private async Task ClickPrintLabelAsync(ShopeeBatchOrder order)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var screen = await ReadAsync();
            var title = ShopeeBatchScreens.TrackingDialogTitle(screen.Boxes) ?? throw new ShopeeBatchStop("「查看寄件編號」視窗不見了。", true);
            if (ShopeeBatchScreens.PrintLabelButton(screen.Boxes) is { } button)
            {
                Click(screen, button, "列印寄件單");
                return;
            }
            // Scroll inside the dialog, below its title.
            var point = new Point(screen.Area.Left + title.Right + 40, screen.Area.Top + Math.Min(screen.Area.Height - 40, title.Bottom + 300));
            Wheel(point, -5);
            await Task.Delay(500, _token);
        }
        throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的「查看寄件編號」視窗裡找不到「列印寄件單」。", true);
    }

    /// <summary>The 查看寄件編號 dialog's × (user, 2026-10-12): the only ink right of the title, inside the dialog.</summary>
    private async Task CloseTrackingDialogAsync()
    {
        for (var tab = 0; tab < 4; tab++)
        {
            using var screen = await ReadAsync();
            if (ShopeeBatchScreens.TrackingDialogTitle(screen.Boxes) is { } title)
            {
                var cross = CloseCross(screen.Image, title) ?? throw new ShopeeBatchStop("找不到「查看寄件編號」視窗右上角的 ×。", true);
                _log.Info("shopee-batch", $"click dialog close at {screen.Area.Left + cross.X},{screen.Area.Top + cross.Y}");
                Front();
                InputSender.Click(new Point(screen.Area.Left + cross.X, screen.Area.Top + cross.Y));
                var (closed, _) = await WaitAsync("「查看寄件編號」視窗關閉", 10,
                    s => ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is null && ShopeeBatchScreens.IsOrderDetail(s.Boxes) ? s : null, shipped: true);
                closed.Dispose();
                return;
            }
            // Chrome showed another tab after Ctrl+W: try the next one.
            Keys("Ctrl+Tab", NativeMethods.VK_CONTROL, NativeMethods.VK_TAB);
            await Task.Delay(500, _token);
        }
        throw new ShopeeBatchStop("關掉預覽列印後找不到訂單的「查看寄件編號」視窗。", true);
    }

    /// <summary>Brightness (0–255) of each pixel, for <see cref="ShopeeBatchScreens"/>' pixel checks.</summary>
    private static Func<int, int, int> Pixels(Bitmap image) => (x, y) =>
    {
        var c = image.GetPixel(x, y);
        return (c.R + c.G + c.B) / 3;
    };

    private static Point? CloseCross(Bitmap image, OcrBox title) => ShopeeBatchScreens.CloseCross(Pixels(image), image.Width, image.Height, title);

    /// <summary>Shopee's orange around the button text (inside the button, beside the text).</summary>
    private static bool Orange(Bitmap image, OcrBox button)
    {
        var hits = 0;
        foreach (var (x, y) in new[] { (button.Left - 5, button.CenterY), (button.Right + 5, button.CenterY), (button.CenterX, button.Top - 4), (button.CenterX, button.Bottom + 4) })
        {
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
            var c = image.GetPixel(x, y);
            if (c.R >= 200 && c.G is >= 50 and <= 130 && c.B <= 90) hits++;
        }
        return hits >= 3;
    }

    // ---- after ERP ----

    /// <summary>Closes the order's tab (only when it is the order) and refreshes the list (user, 2026-10-12).</summary>
    private async Task CloseOrderTabAsync(ShopeeBatchOrder order)
    {
        using (var screen = await ReadAsync())
        {
            if (!ShopeeBatchScreens.IsOrderDetail(screen.Boxes) || ShopeeBatchScreens.DetailOrderSn(screen.Boxes) != order.OrderSn)
                throw new ShopeeBatchStop($"ERP 完成後，Chrome 前景不是訂單 {order.OrderSn} 的明細頁；已停止。", true);
        }
        Keys("Ctrl+W (訂單明細)", NativeMethods.VK_CONTROL, 0x57 /* W */);
        await Task.Delay(500, _token);
        for (var tab = 0; tab < 4; tab++)
        {
            using var screen = await ReadAsync();
            if (ShopeeBatchScreens.IsOrderList(screen.Boxes))
            {
                Keys("F5", NativeMethods.VK_F5);
                await Task.Delay(1500, _token);
                return;
            }
            Keys("Ctrl+Tab", NativeMethods.VK_CONTROL, NativeMethods.VK_TAB);
            await Task.Delay(500, _token);
        }
        throw new ShopeeBatchStop("關掉訂單分頁後找不到「我的銷售」清單分頁。");
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
