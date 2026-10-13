using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CYERPAutoInput;

/// <summary>Why the batch stopped on an order; <see cref="Shipped"/> when Shopee had already shipped it.</summary>
internal sealed class ShopeeBatchStop(string message, bool shipped = false) : Exception(message)
{
    public bool Shipped { get; } = shipped;
}

/// <summary>
/// The label printer and, per carrier, Chrome's 縮放比例 and 頁面 (null = 全部). Chrome keeps the
/// last print's settings (user, 2026-10-13: after a 全家 label the next one would print page 2
/// at 165 %), so every label sets them again.
/// </summary>
internal sealed record ShopeeBatchPrint(string Printer, int ShopeeScale, int SevenScale, int FamilyScale, string FamilyPages);

/// <summary>What the batch knows of the order it is on, for the ERP step and the messages.</summary>
internal sealed class ShopeeBatchOrder
{
    public required string OrderSn { get; init; }
    public string Carrier { get; set; } = string.Empty;
    public ShopeeBatchCarrier Kind { get; set; }
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
    private readonly ShopeeBatchPrint _print;
    private readonly CancellationToken _token;
    private nint _window;

    public ShopeeBatchRunner(PaddleOcrService ocr, AppLogger log, ShopeeBatchPrint print, CancellationToken token)
    {
        _print = print;
        _printer = print.Printer;
        _ocr = ocr;
        _log = log;
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
            _shippedHint = false;
            var order = await NextOrderAsync();
            if (order is null) break;
            Current = order;
            status($"蝦皮全自動：訂單 {order.OrderSn} 安排出貨…");
            await ShipAsync(order);
            status($"蝦皮全自動：訂單 {order.OrderSn} 輸入 ERP…");
            await enterErp(order);
            _inputs++; // the page scan and ERP moved and clicked
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

    /// <summary>
    /// Counts the input the batch sends; a screen read since the last input can be used again
    /// instead of reading the same pixels twice (user, 2026-10-13: too slow — each read of the
    /// window takes seconds).
    /// </summary>
    private int _inputs;
    private (Bitmap Image, Rectangle Area, List<OcrBox> Boxes, int Inputs, RectangleF? Part)? _latest;

    /// <param name="front">False while a drop-down list is open: bringing the window forward would close it.</param>
    /// <param name="part">Only this share of the page (fractions of its width and height): fewer lines, a faster read.</param>
    /// <param name="reuse">The latest read, when nothing was clicked or typed since (a page that is still loading needs a new read).</param>
    /// <summary>Whether a stop from <see cref="Click"/> happens after 安排出貨 (for its message).</summary>
    private bool _shippedHint;

    /// <summary>OCR instead of page text (Chrome gave no page, or not what a step needs).</summary>
    private bool _ocrOnly;

    private async Task<Screen> ReadAsync(bool front = true, RectangleF? part = null, bool reuse = false, bool ocr = false)
    {
        if (reuse && _latest is { } latest && latest.Inputs == _inputs && latest.Part == part)
        {
            _log.Info("shopee-batch", "screen read reused");
            return new Screen((Bitmap)latest.Image.Clone(), latest.Area, latest.Boxes);
        }
        if (front) Front();
        await Task.Delay(150, _token);
        var page = PageArea();
        // The page's own text through UI Automation (user's PC, 2026-10-13: whole screens in
        // 22–150 ms, exact text); the pixels are still captured for the colour and × checks.
        if (!ocr && !_ocrOnly)
        {
            var texts = await Task.Run(() => ChromePageText.Read(_window, _log, out _), _token);
            if (texts.Any(t => t.Type == "Document"))
            {
                var pageImage = ScreenCapture.Capture(page);
                var textBoxes = ChromePageText.PageBoxes(texts, page);
                _log.Info("shopee-batch", $"page text lines={textBoxes.Count} list={ShopeeBatchScreens.IsOrderList(textBoxes)} detail={ShopeeBatchScreens.IsOrderDetail(textBoxes)} dialog={ShopeeBatchScreens.TrackingDialogTitle(textBoxes) is not null} awb={ShopeeBatchScreens.IsAwbPage(textBoxes)} print={ShopeeBatchScreens.IsPrintDialog(textBoxes)}");
                _latest?.Image.Dispose();
                _latest = ((Bitmap)pageImage.Clone(), page, textBoxes, _inputs, part);
                return new Screen(pageImage, page, textBoxes);
            }
            _log.Warn("shopee-batch", "no page text from Chrome; reading the screen with OCR");
        }
        var area = part is { } p
            ? Rectangle.FromLTRB(page.Left + (int)(page.Width * p.Left), page.Top + (int)(page.Height * p.Top), page.Left + (int)(page.Width * p.Right), page.Top + (int)(page.Height * p.Bottom))
            : page;
        var image = ScreenCapture.Capture(area);
        try
        {
            // Whole lines (user's PC, 2026-10-13: the word boxes failed inside the OCR library).
            var tokens = await _ocr.RecognizeLinesAsync(image, _token);
            var boxes = tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)).ToList();
            _log.Info("shopee-batch", $"screen read lines={boxes.Count} area={area.Width}x{area.Height} list={ShopeeBatchScreens.IsOrderList(boxes)} detail={ShopeeBatchScreens.IsOrderDetail(boxes)} dialog={ShopeeBatchScreens.TrackingDialogTitle(boxes) is not null} awb={ShopeeBatchScreens.IsAwbPage(boxes)} print={ShopeeBatchScreens.IsPrintDialog(boxes)}");
            _latest?.Image.Dispose();
            _latest = ((Bitmap)image.Clone(), area, boxes, _inputs, part);
            return new Screen(image, area, boxes);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The page itself, without Chrome's tabs, address and bookmarks bars (the largest
    /// Chrome_RenderWidgetHostHWND): fewer lines to read; the window when it is not found.
    /// </summary>
    private Rectangle PageArea()
    {
        if (!NativeMethods.GetWindowRect(_window, out var raw)) throw new ShopeeBatchStop("讀不到 Chrome 視窗的位置。");
        var bounds = System.Windows.Forms.Screen.FromHandle(_window).Bounds;
        var window = Rectangle.Intersect(raw.ToRectangle(), bounds);
        var best = Rectangle.Empty;
        NativeMethods.EnumChildWindows(_window, (child, _) =>
        {
            if (NativeMethods.IsWindowVisible(child) && NativeMethods.ClassName(child) == "Chrome_RenderWidgetHostHWND" &&
                NativeMethods.GetWindowRect(child, out var r) && r.Width * r.Height > best.Width * best.Height)
                best = r.ToRectangle();
            return true;
        }, 0);
        var page = Rectangle.Intersect(best, window);
        return page.Width >= window.Width / 2 && page.Height >= window.Height / 2 ? page : window;
    }

    /// <summary>Reads the window until <paramref name="probe"/> finds what it wants; stops the batch after <paramref name="seconds"/>.</summary>
    /// <param name="reuseFirst">Try the latest read first, when nothing was sent since.</param>
    private async Task<(Screen Screen, T Value)> WaitAsync<T>(string what, int seconds, Func<Screen, T?> probe, bool shipped = false, bool reuseFirst = false, RectangleF? part = null) where T : class
    {
        var watch = Stopwatch.StartNew();
        var reuse = reuseFirst;
        string? previous = null;
        while (true)
        {
            var screen = await ReadAsync(part: part, reuse: reuse);
            reuse = false;
            var value = probe(screen);
            // Page text is read the moment the page starts to show (user's PC, 2026-10-13: the
            // carrier was still 「Loading shipping channel」): a screen counts once two reads in a
            // row give the same text and nothing is still loading.
            var signature = string.Join("\n", screen.Boxes.Select(b => b.Text));
            var loading = screen.Boxes.Any(b => b.Text.Contains("Loading", StringComparison.OrdinalIgnoreCase) || b.Text.Contains("載入中", StringComparison.Ordinal));
            if (value is not null && !loading && signature == previous) return (screen, value);
            previous = loading ? null : signature;
            screen.Dispose();
            if (watch.Elapsed > TimeSpan.FromSeconds(seconds))
            {
                _log.Warn("shopee-batch", $"timeout waiting for {what} found={value is not null} loading={loading}");
                throw new ShopeeBatchStop($"等不到{what}（{seconds} 秒）。", shipped);
            }
            await Task.Delay(value is not null ? 250 : 500, _token);
        }
    }

    private void Click(Screen screen, OcrBox box, string what, bool front = true)
    {
        var point = new Point(screen.Area.Left + box.CenterX, screen.Area.Top + box.CenterY);
        // Page text includes what is scrolled out of view; a click goes only on what is shown.
        if (!screen.Area.Contains(point))
            throw new ShopeeBatchStop($"「{what}」不在畫面可見範圍內（{point.X},{point.Y}），為避免點錯已停止。", _shippedHint);
        _inputs++;
        if (front) Front();
        _log.Info("shopee-batch", $"click {what} at {point.X},{point.Y}");
        InputSender.Click(point);
        NativeMethods.SetCursorPos(screen.Area.Left + 12, screen.Area.Top + screen.Area.Height / 2);
    }

    private void Keys(string what, params ushort[] keys)
    {
        _inputs++;
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
        // The list may not show it (user's PC, 2026-10-13: a long row's text came cut before the
        // carrier); then the order page decides, before anything is shipped.
        if (first.Carrier.Length > 0 && !ShopeeBatchScreens.SupportedCarrier(first.Carrier))
            throw new ShopeeBatchStop($"訂單 {first.OrderSn} 的物流方式「{first.Carrier}」不是蝦皮店到店／店到宅／7-ELEVEN／全家，請人工處理。");
        var order = new ShopeeBatchOrder { OrderSn = first.OrderSn, Carrier = first.Carrier, Kind = ShopeeBatchScreens.CarrierKind(first.Carrier) };
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
        var (detail, button) = await WaitAsync("「安排出貨」按鈕", 15, s => ShopeeBatchScreens.ShipButton(s.Boxes), reuseFirst: true);
        using (detail)
        {
            var carrier = ShopeeBatchScreens.DetailCarrier(detail.Boxes);
            var kind = ShopeeBatchScreens.CarrierKind(carrier);
            _log.Info("shopee-batch", $"order page carrier={_log.Value(carrier)} kind={kind}");
            if (kind == ShopeeBatchCarrier.Other)
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的運送資訊是「{carrier}」，不是蝦皮店到店／店到宅／7-ELEVEN／全家，請人工處理。");
            if (order.Carrier.Length > 0 && kind != order.Kind)
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的運送資訊是「{carrier}」，與清單上的「{order.Carrier}」不同，請人工處理。");
            order.Kind = kind;
            if (order.Carrier.Length == 0) order.Carrier = carrier;
            if (!Orange(detail.Image, button))
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的「{button.Text.Trim()}」按鈕顏色不對，為避免按錯已停止。");
            Click(detail, button, button.Text.Trim());
        }
        order.Shipped = true;
        _shippedHint = true;

        // 寄件編號申請中… then the number. 全家 first shows 「申請寄件編號」 (user, 2026-10-13), pressed once.
        var applied = false;
        var (dialog, tracking) = await WaitAsync("寄件編號", 60, s =>
        {
            if (!applied && ShopeeBatchScreens.ApplyTrackingButton(s.Boxes) is { } apply)
            {
                Click(s, apply, "申請寄件編號");
                applied = true;
                return null;
            }
            if (ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is null || ShopeeBatchScreens.TrackingRequested(s.Boxes)) return null;
            var t = ShopeeBatchScreens.DialogTracking(s.Boxes);
            return t.Length > 0 ? t : null;
        }, shipped: true);
        order.Tracking = tracking;
        _log.Info("shopee-batch", $"tracking assigned length={tracking.Length}");
        dialog.Dispose();

        await ClickPrintLabelAsync(order);
        switch (order.Kind)
        {
            case ShopeeBatchCarrier.SevenEleven: await PrintSevenSlipAsync(order); break;
            case ShopeeBatchCarrier.FamilyMart: await PrintFamilySlipAsync(order); break;
            default: await PrintShopeeLabelAsync(order); break;
        }
        var (printed, _) = await WaitAsync("列印視窗關閉", 30, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? null : s, shipped: true);
        printed.Dispose();
        _log.Info("shopee-batch", "label printed");

        // Close the label's tab: back to the order with its 查看寄件編號 dialog, then close it.
        Keys("Ctrl+W (寄件單)", NativeMethods.VK_CONTROL, 0x57 /* W */);
        await Task.Delay(500, _token);
        await CloseTrackingDialogAsync();
    }

    /// <summary>Shopee's 預覽列印 tab: 下載出貨文件中 → 下載完成 → checked → 列印出貨文件 → Chrome's print dialog.</summary>
    private async Task PrintShopeeLabelAsync(ShopeeBatchOrder order)
    {
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
        await PrintDialogAsync(_print.ShopeeScale, pages: null);
    }

    /// <summary>
    /// 7-ELEVEN (user, 2026-10-13): 列印寄件單 opens 7-11's 交貨便 slip in a new tab with no print
    /// button of its own; once its order number and code are checked, Ctrl+P opens Chrome's print
    /// dialog, which prints it at 100 %.
    /// </summary>
    private async Task PrintSevenSlipAsync(ShopeeBatchOrder order)
    {
        var (slip, _) = await WaitAsync("7-ELEVEN 交貨便服務單", 60, s => ShopeeBatchScreens.IsSevenLabelPage(s.Boxes) && !ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null, shipped: true);
        using (slip)
        {
            var labelSn = ShopeeBatchScreens.LabelOrderSn(slip.Boxes);
            if (labelSn != order.OrderSn)
                throw new ShopeeBatchStop($"交貨便服務單上的寄貨訂單編號（{(labelSn.Length > 0 ? labelSn : "讀不到")}）不是 {order.OrderSn}，未列印。", true);
            var code = ShopeeBatchScreens.SevenLabelCode(slip.Boxes);
            if (!ShopeeBatchScreens.SameTracking(code, order.Tracking))
                throw new ShopeeBatchStop($"交貨便服務代碼（{(code.Length > 0 ? code : "讀不到")}）與寄件編號 {order.Tracking} 不同，未列印。", true);
        }
        Keys("Ctrl+P", NativeMethods.VK_CONTROL, 0x50 /* P */);
        await PrintDialogAsync(_print.SevenScale, pages: null);
    }

    /// <summary>
    /// 全家 (user, 2026-10-13): 列印寄件單 opens the FamilyMart寄取貨單 in a new tab; Ctrl+P, then
    /// 頁面 自訂 2 and 縮放比例 自訂 165 print it right.
    /// </summary>
    private async Task PrintFamilySlipAsync(ShopeeBatchOrder order)
    {
        var (slip, _) = await WaitAsync("全家 FamilyMart寄取貨單", 60, s => ShopeeBatchScreens.IsFamilyLabelPage(s.Boxes) && !ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null, shipped: true);
        using (slip)
        {
            if (!ShopeeBatchScreens.ShowsOrderSn(slip.Boxes, order.OrderSn))
                throw new ShopeeBatchStop($"全家寄取貨單上讀不到訂單編號 {order.OrderSn}，未列印。", true);
            // The 寄件編號 (user, 2026-10-13: 11 digits, e.g. 「# 1635…」 on the order page) is the
            // slip's own 訂單編號.
            if (!ShopeeBatchScreens.ShowsNumber(slip.Boxes, order.Tracking))
                throw new ShopeeBatchStop($"全家寄取貨單上讀不到寄件編號 {order.Tracking}，未列印。", true);
        }
        Keys("Ctrl+P", NativeMethods.VK_CONTROL, 0x50 /* P */);
        await PrintDialogAsync(_print.FamilyScale, _print.FamilyPages);
    }

    /// <summary>
    /// Chrome's own print dialog (user, 2026-10-12/13). Chrome keeps the last print's settings,
    /// so 頁面 (全部, or 自訂 with <paramref name="pages"/>) and 縮放比例 (自訂 <paramref name="wanted"/>)
    /// are set every time, opening 顯示更多設定 when 縮放比例 is folded away; then everything is
    /// read back — the set printer, the scale, the pages and 「1 張紙」 — before 列印 is pressed.
    /// What does not come right stops the batch without printing.
    /// </summary>
    private async Task PrintDialogAsync(int wanted, string? pages)
    {
        // The settings are the right of the dialog: only that half is read (faster).
        var panel = new RectangleF(0.5f, 0, 0.5f, 1);
        var (opened, _) = await WaitAsync("Chrome 列印視窗", 30, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null, shipped: true, part: panel);
        opened.Dispose();
        var reuse = true;
        var ocrDialog = false;
        var expanded = false;
        var actions = 0;
        var watch = Stopwatch.StartNew();
        string last = string.Empty;
        while (true)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(40) || actions > 10)
            {
                _log.Warn("shopee-batch", $"print dialog not as set: {last}");
                throw new ShopeeBatchStop($"Chrome 列印視窗設定不成（{last}），未列印。", true);
            }
            using var screen = await ReadAsync(part: panel, reuse: reuse, ocr: ocrDialog);
            reuse = false;
            var boxes = screen.Boxes;
            // Page text gives the drop-downs' labels; when it does not also give what they show,
            // the dialog is read from the screen instead.
            if (!ocrDialog && new[] { "目的地", "頁面", "縮放比例" }.Any(l => ShopeeBatchScreens.PrintRowValue(boxes, l) is { } v && ShopeeScreenshot.Compact(v.Text) == l))
            {
                _log.Info("shopee-batch", "print dialog values not in the page text; reading it with OCR");
                ocrDialog = true;
                continue;
            }
            if (!ShopeeBatchScreens.IsPrintDialog(boxes)) { last = "列印視窗不見了"; await Task.Delay(600, _token); continue; }

            var scaleBox = ShopeeBatchScreens.PrintScaleBox(boxes);
            var scaleValue = ShopeeBatchScreens.PrintRowValue(boxes, "縮放比例");
            if (scaleValue is null && !expanded && ShopeeBatchScreens.MoreSettings(boxes) is { } more)
            {
                Click(screen, more, "顯示更多設定");
                expanded = true;
                await Task.Delay(700, _token);
                continue;
            }

            // 頁面
            var pagesValue = ShopeeBatchScreens.PrintRowValue(boxes, "頁面");
            if (pagesValue is null) { last = "讀不到頁面"; await Task.Delay(600, _token); continue; }
            var shownPages = ShopeeScreenshot.Compact(pagesValue.Text);
            if (pages is null && shownPages != "全部") { actions++; await ChooseAsync(screen, pagesValue, "全部", panel); continue; }
            if (pages is not null)
            {
                if (shownPages != "自訂") { actions++; await ChooseAsync(screen, pagesValue, "自訂", panel); continue; }
                var typed = ShopeeBatchScreens.PrintPagesBox(boxes);
                if (typed is null || ShopeeScreenshot.Compact(typed.Text) != pages)
                {
                    actions++;
                    var at = typed is not null ? new Point(typed.CenterX, typed.CenterY) : new Point(pagesValue.CenterX, pagesValue.CenterY + RowGap(boxes));
                    await TypeIntoAsync(screen, at, pages, "頁面");
                    continue;
                }
            }

            // 縮放比例
            if (scaleValue is null) { last = "讀不到縮放比例"; await Task.Delay(600, _token); continue; }
            if (ShopeeScreenshot.Compact(scaleValue.Text) != "自訂") { actions++; await ChooseAsync(screen, scaleValue, "自訂", panel); continue; }
            var scale = scaleBox is null ? (int?)null : int.Parse(ShopeeScreenshot.Compact(scaleBox.Text));
            if (scale != wanted)
            {
                actions++;
                var at = scaleBox is not null ? new Point(scaleBox.CenterX, scaleBox.CenterY) : new Point(scaleValue.CenterX, scaleValue.CenterY + RowGap(boxes));
                await TypeIntoAsync(screen, at, wanted.ToString(System.Globalization.CultureInfo.InvariantCulture), "縮放比例");
                continue;
            }

            // Read back and print.
            var destination = ShopeeBatchScreens.PrintDestination(boxes);
            var sheets = ShopeeBatchScreens.SheetCount(boxes);
            last = $"目的地「{destination}」、頁面「{shownPages}{(pages is null ? string.Empty : " " + pages)}」、縮放 {scale}、{sheets?.ToString() ?? "?"} 張紙";
            if (!ShopeeBatchScreens.SamePrinter(destination, _printer))
                throw new ShopeeBatchStop($"Chrome 列印的目的地是「{destination}」，不是設定的「{_printer}」，未列印。", true);
            if (sheets is > 1 || pages is not null && sheets is null) { await Task.Delay(800, _token); continue; }
            if (ShopeeBatchScreens.PrintButton(boxes) is not { } printButton) { await Task.Delay(600, _token); continue; }
            _log.Info("shopee-batch", $"print dialog set: {last}");
            Click(screen, printButton, "列印");
            return;
        }
    }

    /// <summary>Distance from a setting's value to the box typed into under it (縮放比例 自訂 → its number), else 38 px.</summary>
    private static int RowGap(IReadOnlyList<OcrBox> boxes)
    {
        var value = ShopeeBatchScreens.PrintRowValue(boxes, "縮放比例");
        var number = ShopeeBatchScreens.PrintScaleBox(boxes);
        return value is not null && number is not null && number.CenterY > value.CenterY ? number.CenterY - value.CenterY : 38;
    }

    /// <summary>
    /// Picks a print setting's option by keyboard: open the list, Home for the first option
    /// (頁面 全部) or End for the last (自訂, in 頁面 and 縮放比例 alike), Enter. No option has to
    /// be found on screen; the value is read back afterwards.
    /// </summary>
    private async Task ChooseAsync(Screen screen, OcrBox dropdown, string option, RectangleF? part)
    {
        var key = option switch
        {
            "全部" => NativeMethods.VK_HOME,
            "自訂" => NativeMethods.VK_END,
            _ => throw new ShopeeBatchStop($"Chrome 列印設定不支援選「{option}」。", true)
        };
        Click(screen, dropdown, $"列印設定 {dropdown.Text.Trim()}");
        await Task.Delay(400, _token);
        _log.Info("shopee-batch", $"keys {(key == NativeMethods.VK_HOME ? "Home" : "End")}+Enter ({option})");
        InputSender.Press(key);
        await Task.Delay(150, _token);
        InputSender.Press(NativeMethods.VK_RETURN);
        await Task.Delay(900, _token);
    }

    /// <summary>Replaces a print setting's typed value (click, Ctrl+A, the text, Tab to apply).</summary>
    private async Task TypeIntoAsync(Screen screen, Point at, string text, string what)
    {
        _inputs++;
        Front();
        _log.Info("shopee-batch", $"type {what}={text} at {screen.Area.Left + at.X},{screen.Area.Top + at.Y}");
        InputSender.Click(new Point(screen.Area.Left + at.X, screen.Area.Top + at.Y));
        await Task.Delay(200, _token);
        InputSender.Chord(NativeMethods.VK_CONTROL, 0x41 /* A */);
        await Task.Delay(80, _token);
        InputSender.UnicodeText(text);
        await Task.Delay(80, _token);
        InputSender.Press(NativeMethods.VK_TAB);
        await Task.Delay(1000, _token);
    }

    /// <summary>「列印寄件單」 sits at the dialog's bottom; when the zoom hides it, the dialog is scrolled.</summary>
    private async Task ClickPrintLabelAsync(ShopeeBatchOrder order)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var screen = await ReadAsync(reuse: attempt == 0);
            var title = ShopeeBatchScreens.TrackingDialogTitle(screen.Boxes) ?? throw new ShopeeBatchStop("「查看寄件編號」視窗不見了。", true);
            if (ShopeeBatchScreens.PrintLabelButton(screen.Boxes) is { } button &&
                screen.Area.Contains(new Point(screen.Area.Left + button.CenterX, screen.Area.Top + button.CenterY)))
            {
                Click(screen, button, "列印寄件單");
                return;
            }
            // Scroll inside the dialog, below its title.
            var point = new Point(screen.Area.Left + title.Right + 40, screen.Area.Top + Math.Min(screen.Area.Height - 40, title.Bottom + 300));
            _inputs++;
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
                var mark = ShopeeBatchScreens.DialogCloseMark(screen.Boxes, title);
                var cross = (mark is not null ? new Point(mark.CenterX, mark.CenterY) : CloseCross(screen.Image, title))
                    ?? throw new ShopeeBatchStop("找不到「查看寄件編號」視窗右上角的 ×。", true);
                _log.Info("shopee-batch", $"click dialog close at {screen.Area.Left + cross.X},{screen.Area.Top + cross.Y}");
                _inputs++;
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
        // The page scan leaves the order scrolled to its bottom (user's PC, 2026-10-13: 訂單編號
        // was out of sight and the batch stopped); back to the top first.
        var page = PageArea();
        _inputs++;
        Front();
        InputSender.Click(new Point(page.Left + 12, page.Top + page.Height / 2));
        await Task.Delay(120, _token);
        InputSender.Press(NativeMethods.VK_HOME);
        await Task.Delay(700, _token);
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
