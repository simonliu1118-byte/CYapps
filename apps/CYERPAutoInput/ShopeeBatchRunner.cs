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
internal sealed record ShopeeBatchPrint(string Printer, int ShopeeScale, int SevenScale, int FamilyScale, string FamilyPages,
    int HctScale = 95, string HctPickupAddress = "", int CoupangHctScale = 22, string SlipNote = "", bool SlipNoteAll = false);

/// <summary>
/// 設定 › 發送訊息 › 蝦皮 (user, 2026-10-14): each rule whose words the 買家備註 mentions has its
/// message (one line) typed into the order's 聊聊 and sent. No rules = off.
/// </summary>
internal sealed record ShopeeBatchChat(IReadOnlyList<ChatRule> Rules);

/// <summary>
/// An order left for a person without stopping the batch (user, 2026-10-13: note it, go on,
/// list everything at the end): nothing was done in Shopee for it (a carrier with no flow yet),
/// so it stays in 待處理 and is passed over.
/// </summary>
internal sealed class ShopeeBatchSkip(string message) : Exception(message);

/// <summary>One line of the batch's closing list.</summary>
internal sealed record ShopeeBatchNote(string OrderSn, string Message);

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
internal sealed partial class ShopeeBatchRunner
{
    /// <summary>我的銷售 › 待出貨 › 待處理, sorted by the shipping deadline (the URL Shopee itself opens).</summary>
    public const string ListUrl = "https://seller.shopee.tw/portal/sale/order?type=toship&source=to_process&sort_by=ship_by_date_asc";

    private readonly PaddleOcrService _ocr;
    private readonly AppLogger _log;
    private readonly string _printer;
    private readonly ShopeeBatchPrint _print;
    private readonly CancellationToken _token;
    private nint _window;

    private readonly ShopeeBatchChat _chat;

    public ShopeeBatchRunner(PaddleOcrService ocr, AppLogger log, ShopeeBatchPrint print, CancellationToken token, ShopeeBatchChat? chat = null)
    {
        _chat = chat ?? new ShopeeBatchChat([]);
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

    /// <summary>Orders left in 待處理 for a person; passed over in the list.</summary>
    public HashSet<string> Skipped { get; } = [];

    /// <summary>Everything a person has to look at, in order (user, 2026-10-13: listed at the end).</summary>
    public List<ShopeeBatchNote> Notes { get; } = [];

    /// <summary>Orders whose buyer got the 聊聊 reply (for the closing list).</summary>
    public List<string> Chats { get; } = [];

    /// <summary>
    /// 測試 (user, 2026-10-26): one order taken up where Shopee's 寄件編號 dialog (快遞取件詳情 or
    /// 查看寄件編號) is open in the Chrome in front — 列印寄件單 and on to the end as in 全自動
    /// (label, ERP); the tab is left open.
    /// </summary>
    public async Task ResumeFromDialogAsync(Func<ShopeeBatchOrder, Task<string?>> enterErp, Action<string> status)
    {
        _window = ChromeWindows().FirstOrDefault();
        if (_window == 0) throw new ShopeeBatchStop("找不到 Chrome 視窗：請先在 Chrome 打開蝦皮訂單的寄件編號視窗（快遞取件詳情／查看寄件編號）。");
        _shippedHint = true;
        Front();
        await Task.Delay(500, _token);
        var (shown, tracking) = await WaitOrNullAsync(8, s =>
            ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is not null && ShopeeBatchScreens.DialogTracking(s.Boxes) is { Length: > 0 } t ? t : null);
        if (shown is null || tracking is null)
        {
            shown?.Dispose();
            throw new ShopeeBatchStop("畫面上沒有蝦皮的寄件編號視窗（快遞取件詳情／查看寄件編號），或讀不到寄件編號：請在訂單頁打開後再試。");
        }
        ShopeeBatchOrder order;
        using (shown)
        {
            var orderSn = ShopeeBatchScreens.DetailOrderSn(shown.Boxes);
            var carrier = ShopeeBatchScreens.DetailCarrier(shown.Boxes);
            if (orderSn.Length == 0) throw new ShopeeBatchStop("讀不到這張訂單的訂單編號。");
            order = new ShopeeBatchOrder { OrderSn = orderSn, Carrier = carrier, Kind = ShopeeBatchScreens.CarrierKind(carrier), Tracking = tracking, Shipped = true };
        }
        _log.Info("shopee-batch", $"resume from the tracking dialog carrier={_log.Value(order.Carrier)} kind={order.Kind}");
        if (order.Kind == ShopeeBatchCarrier.Other) throw new ShopeeBatchStop($"物流「{order.Carrier}」程式還沒有列印流程。", true);
        Current = order;
        status($"測試：訂單 {order.OrderSn} 列印寄件單…");
        await PrintFromDialogAsync(order);
        status($"測試：訂單 {order.OrderSn} 輸入 ERP…");
        var note = await enterErp(order);
        if (note is not null) Notes.Add(new ShopeeBatchNote(order.OrderSn, note));
        Done.Add(order.OrderSn);
        Current = null;
    }

    /// <param name="enterErp">
    /// The page scan and ERP for the order on screen: null when clean, a note to list when it
    /// went on with something for a person to check; throws <see cref="ShopeeBatchStop"/> to stop.
    /// </param>
    /// <param name="status">Progress text for the main window.</param>
    public async Task RunAsync(Func<ShopeeBatchOrder, Task<string?>> enterErp, Action<string> status)
    {
        // Orders passed over earlier today stay passed over until tomorrow (user, 2026-10-26).
        foreach (var (orderSn, reason) in ShopeeDaySkips.Load(DateTime.Today))
        {
            Skipped.Add(orderSn);
            Notes.Add(new ShopeeBatchNote(orderSn, $"今天已略過：{reason}（明天再試）"));
        }
        if (Skipped.Count > 0) _log.Info("shopee-batch", $"passed over today already count={Skipped.Count}");
        status("全自動：開啟 Chrome…");
        await OpenWindowAsync();
        while (true)
        {
            _token.ThrowIfCancellationRequested();
            status($"全自動：找待處理訂單（已完成 {Done.Count} 筆）…");
            Current = null;
            _shippedHint = false;
            var order = await NextOrderAsync();
            if (order is null) break;
            Current = order;
            status($"全自動：訂單 {order.OrderSn} 安排出貨…");
            try { await ShipAsync(order); }
            catch (ShopeeBatchSkip skip)
            {
                // Nothing done in Shopee: noted, passed over, on to the next.
                _log.Info("shopee-batch", "order passed over before shipping");
                Notes.Add(new ShopeeBatchNote(order.OrderSn, skip.Message));
                Skipped.Add(order.OrderSn);
                Current = null;
                await CloseOrderTabAsync(order);
                continue;
            }
            status($"全自動：訂單 {order.OrderSn} 輸入 ERP…");
            var note = await enterErp(order);
            _inputs++; // the page scan and ERP moved and clicked
            if (note is not null) Notes.Add(new ShopeeBatchNote(order.OrderSn, note));
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

    private Task OpenWindowAsync() => OpenWindowAsync(ListUrl);

    private async Task OpenWindowAsync(string url)
    {
        var chrome = ChromePath() ?? throw new ShopeeBatchStop("找不到 Chrome（chrome.exe）。全自動需要 Chrome。");
        var before = ChromeWindows().ToHashSet();
        Process.Start(new ProcessStartInfo(chrome, $"--new-window \"{url}\"") { UseShellExecute = false })?.Dispose();
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
        if (!NativeMethods.IsWindow(_window)) throw new ShopeeBatchStop("全自動的 Chrome 視窗已被關閉。");
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

    /// <summary>The page's own tabs are read too (MO店+).</summary>
    private bool _pageTabs;

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
                var textBoxes = ChromePageText.PageBoxes(texts, page, pageTabs: _pageTabs);
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
                // What the page showed, to see why (short lines only).
                _log.Info("shopee-batch", $"page at timeout: {string.Join(" | ", screen.Boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 24).Take(80))}");
                throw new ShopeeBatchStop($"等不到{what}（{seconds} 秒）。", shipped);
            }
            await Task.Delay(value is not null ? 250 : 500, _token);
        }
    }

    private static bool Visible(Screen screen, OcrBox box) =>
        screen.Area.Contains(new Point(screen.Area.Left + box.CenterX, screen.Area.Top + box.CenterY)) &&
        box.CenterY > 40 && box.CenterY < screen.Area.Height - 40;

    private void Click(Screen screen, OcrBox box, string what, bool front = true, bool hover = false)
    {
        var point = new Point(screen.Area.Left + box.CenterX, screen.Area.Top + box.CenterY);
        // Page text includes what is scrolled out of view; a click goes only on what is shown.
        if (!screen.Area.Contains(point))
            throw new ShopeeBatchStop($"「{what}」不在畫面可見範圍內（{point.X},{point.Y}），為避免點錯已停止。", _shippedHint);
        _inputs++;
        if (front) Front();
        _log.Info("shopee-batch", $"click {what} at {point.X},{point.Y}");
        if (hover)
        {
            // Resting on it first, as a hand would, for a button that ignored a bare click.
            NativeMethods.SetCursorPos(point.X, point.Y);
            Thread.Sleep(350);
        }
        InputSender.Click(point);
        NativeMethods.SetCursorPos(screen.Area.Left + 12, screen.Area.Top + screen.Area.Height / 2);
    }

    /// <summary>
    /// <paramref name="text"/> pasted (Ctrl+V) where the caret is — faster than typing it (user,
    /// 2026-10-16: the 另存新檔 path, 聊聊 messages). The clipboard's own text is put back; when the clipboard
    /// cannot be used, the text is typed as before.
    /// </summary>
    private async Task PasteAsync(string text)
    {
        string? previous = null;
        try
        {
            if (Clipboard.ContainsText()) previous = Clipboard.GetText();
            Clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            _log.Info("shopee-batch", $"clipboard busy ({ex.GetType().Name}); typed instead");
            InputSender.UnicodeText(text);
            await Task.Delay(200, _token);
            return;
        }
        InputSender.Chord(NativeMethods.VK_CONTROL, 0x56 /* V */);
        await Task.Delay(200, _token);
        try
        {
            if (previous is not null) Clipboard.SetText(previous);
            else Clipboard.Clear();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            _log.Info("shopee-batch", "clipboard not restored");
        }
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
            _log.Info("shopee-batch", $"list pending={ShopeeBatchScreens.PendingCount(screen.Boxes)} today={ShopeeBatchScreens.TodayCount(screen.Boxes)} passed_over={Skipped.Count}");
            if (ShopeeBatchScreens.PendingCount(screen.Boxes) == 0) return null;
            if (NextInList(screen) is { } first) return await OpenOrderAsync(screen, first);
            if (ShopeeBatchScreens.FirstOrder(screen.Boxes) is null && !ShopeeBatchScreens.NoOrders(screen.Boxes))
                throw new ShopeeBatchStop("清單上讀不到第一筆訂單的「待出貨」或訂單編號。");
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
            if (NextInList(next) is { } first) return await OpenOrderAsync(next, first);
            return null;
        }

        // Loaded: either an order or 「找不到訂單」 is on screen.
        static bool Settled(List<OcrBox> boxes) => ShopeeBatchScreens.NoOrders(boxes) || ShopeeBatchScreens.FirstOrder(boxes) is not null;
    }

    /// <summary>The first order of the list not passed over (orders left for a person stay in 待處理).</summary>
    private ShopeeListOrder? NextInList(Screen screen) =>
        ShopeeBatchScreens.Orders(screen.Boxes).FirstOrDefault(o => !Skipped.Contains(o.OrderSn));

    private async Task<ShopeeBatchOrder> OpenOrderAsync(Screen screen, ShopeeListOrder first)
    {
        // An order further down (the ones above passed over) is scrolled into view first.
        for (var scroll = 0; !Visible(screen, first.Status) && scroll < 20; scroll++)
        {
            var page = screen.Area;
            _inputs++;
            Wheel(new Point(page.Left + page.Width / 2, page.Top + page.Height / 2), -3);
            await Task.Delay(400, _token);
            var (moved, found) = await WaitAsync("清單上的下一筆訂單", 15,
                s => ShopeeBatchScreens.Orders(s.Boxes).FirstOrDefault(o => o.OrderSn == first.OrderSn));
            if (!ReferenceEquals(screen, moved))
            {
                if (scroll > 0) screen.Dispose(); // the caller disposes the first screen
                screen = moved;
            }
            first = found;
        }
        if (Done.Contains(first.OrderSn))
            throw new ShopeeBatchStop($"訂單 {first.OrderSn} 已處理過，卻還在待處理清單上；為避免重複出貨，已停止。");
        // The carrier is not taken from the list (user, 2026-10-13: a long row's text came cut
        // before it, and every carrier is to be supported): the order page decides it.
        var order = new ShopeeBatchOrder { OrderSn = first.OrderSn };
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
        // The page draws 安排出貨 before its 物流方式 (user's LOG, 2026-10-14: an order read at
        // 39 lines had no carrier yet and was passed over as unsupported): the carrier is
        // waited for too, a while, before the page is taken without it.
        var loading = Stopwatch.StartNew();
        var (detail, button) = await WaitAsync("「安排出貨」按鈕", 20, s =>
            ShopeeBatchScreens.DetailCarrier(s.Boxes).Length > 0 || loading.Elapsed.TotalSeconds > 8 ? ShopeeBatchScreens.ShipButton(s.Boxes) : null, reuseFirst: true);
        // 買家備註 with a rule's words: its 聊聊 message first, then the order as usual.
        var matches = ChatRules.Matches(ShopeeBatchScreens.BuyerNote(detail.Boxes), _chat.Rules);
        if (matches.Count > 0)
        {
            detail.Dispose();
            var words = string.Join("、", matches.Select(m => m.Word));
            _log.Info("shopee-batch", $"buyer note matches {matches.Count} message rule(s); sending through 聊聊");
            var problem = await SendChatReplyAsync(matches.Select(m => ChatRules.OneLine(m.Rule.Message)).ToList());
            if (problem is null) Chats.Add($"{order.OrderSn}（{words}）");
            else Notes.Add(new ShopeeBatchNote(order.OrderSn, $"買家備註提到「{words}」，聊聊訊息未全部送出（{problem}），請人工傳送。"));
            await BackToOrderTopAsync();
            (detail, button) = await WaitAsync("「安排出貨」按鈕", 15, s => ShopeeBatchScreens.ShipButton(s.Boxes));
        }
        using (detail)
        {
            var carrier = ShopeeBatchScreens.DetailCarrier(detail.Boxes);
            var kind = ShopeeBatchScreens.CarrierKind(carrier);
            _log.Info("shopee-batch", $"order page carrier={_log.Value(carrier)} kind={kind}");
            // Each carrier has its own label flow after 安排出貨; one without a flow yet stops here,
            // before anything is shipped.
            // 任選／下單請備註 (user, 2026-10-26): the items depend on the buyer's note; passed over unshipped.
            if (ShopeeBatchScreens.BuyerChoiceSpec(detail.Boxes) is { } spec)
                throw new ShopeeBatchSkip($"未出貨：商品「{spec}」要依買家備註決定品項，請人工處理。");
            if (kind == ShopeeBatchCarrier.Other)
                throw new ShopeeBatchSkip($"未出貨：物流「{carrier}」程式還沒有出貨流程，請人工處理。");
            if (ShopeeBatchScreens.IsPickup(kind) && ShopeeScreenshot.Compact(_print.HctPickupAddress).Length < 6)
                throw new ShopeeBatchSkip($"未出貨：{carrier}要先在「設定 › 平台匯入 › 蝦皮」填入「取件地址」，請人工處理。");
            order.Kind = kind;
            order.Carrier = carrier;
            if (!Orange(detail.Image, button))
                throw new ShopeeBatchStop($"訂單 {order.OrderSn} 的「{button.Text.Trim()}」按鈕顏色不對，為避免按錯已停止。");
            _log.Info("shopee-batch", $"ship button box={button.Left},{button.Top} {button.Width}x{button.Height} area={detail.Area}");
            Click(detail, button, button.Text.Trim());
        }
        if (ShopeeBatchScreens.IsPickup(order.Kind)) await ConfirmPickupAsync(order);
        order.Shipped = true;
        _shippedHint = true;

        // 寄件編號申請中… then the number. 全家 first shows 「申請寄件編號」 (user, 2026-10-13), and
        // its own 申請寄件編號 dialog (寄件人真實姓名, 2026-10-14). Each kind of button is pressed
        // once only (user's LOG, 2026-10-14: the dialog's button was pressed twice, 13 px apart):
        // the one in 查看寄件編號, then the one beside 取消.
        var pressedAsk = false;
        var pressedApply = false;
        // 安排出貨 pressed and nothing opened (user's LOG／screenshot, 2026-10-26: the same order
        // twice, the click did not take): while the button is still there, orange, with no
        // dialog over it, it is found again and pressed again, resting on it first; twice more at most.
        var shipPresses = 1;
        var sincePress = Stopwatch.StartNew();
        var (dialog, tracking) = await WaitAsync("寄件編號", 60, s =>
        {
            // Shopee refused (user's screenshot, 2026-10-26: 買家門市關轉 … (error:10514)): nothing
            // was shipped; 門市關轉 waits for the buyer, so the order is passed over until tomorrow.
            if (ShopeeBatchScreens.ShipError(s.Boxes) is { } error)
            {
                s.Dispose();
                order.Shipped = false;
                _shippedHint = false;
                _log.Warn("shopee-batch", $"安排出貨 refused: {error}");
                if (!ShopeeBatchScreens.StoreClosed(error)) throw new ShopeeBatchSkip($"未出貨：蝦皮回覆「{error}」，請人工處理。");
                ShopeeDaySkips.Add(DateTime.Today, order.OrderSn, "買家門市關轉，等買家重選門市");
                throw new ShopeeBatchSkip($"未出貨：蝦皮回覆「{error}」。今天略過，明天再試。");
            }
            if (!ShopeeBatchScreens.IsPickup(order.Kind) && shipPresses < 3 && sincePress.Elapsed.TotalSeconds > 6 &&
                ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is null && ShopeeBatchScreens.ApplyTrackingButton(s.Boxes) is null &&
                ShopeeBatchScreens.ShipButton(s.Boxes) is { } again && Orange(s.Image, again))
            {
                shipPresses++;
                sincePress.Restart();
                _log.Warn("shopee-batch", $"安排出貨 pressed, nothing opened; pressing again ({shipPresses}) box={again.Left},{again.Top} {again.Width}x{again.Height} area={s.Area}");
                Click(s, again, "安排出貨（再按一次）", hover: true);
                return null;
            }
            if (ShopeeBatchScreens.ApplyTrackingButton(s.Boxes) is { } apply)
            {
                var inAsk = ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is not null && !ShopeeBatchScreens.ApplyBesideCancel(s.Boxes);
                if (inAsk ? pressedAsk : pressedApply) return null;
                Click(s, apply, "申請寄件編號");
                if (inAsk) pressedAsk = true; else pressedApply = true;
                return null;
            }
            if (ShopeeBatchScreens.TrackingDialogTitle(s.Boxes) is null || ShopeeBatchScreens.TrackingRequested(s.Boxes)) return null;
            var t = ShopeeBatchScreens.DialogTracking(s.Boxes);
            return t.Length > 0 ? t : null;
        }, shipped: true);
        order.Tracking = tracking;
        _log.Info("shopee-batch", $"tracking assigned length={tracking.Length}");
        dialog.Dispose();
        await PrintFromDialogAsync(order);
    }

    /// <summary>The 寄件編號 dialog open: 列印寄件單, the label printed by its carrier's way, its tab closed, the dialog closed.</summary>
    private async Task PrintFromDialogAsync(ShopeeBatchOrder order)
    {
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

    /// <summary>
    /// 聊聊 (user, 2026-10-14): the buyer card's 聊聊 opens the side panel and the order's chat;
    /// the message from the settings is typed into the input and, once seen there, sent with
    /// Enter; » folds the panel away. Null when sent, else what went wrong (the order goes on
    /// either way; the panel is folded, or left with Esc).
    /// </summary>
    private async Task<string?> SendChatReplyAsync(IReadOnlyList<string> messages)
    {
        // The buyer card is below the fold: scrolled into view.
        OcrBox? chat = null;
        Screen? screen = null;
        for (var attempt = 0; attempt < 8 && chat is null; attempt++)
        {
            screen?.Dispose();
            screen = await ReadAsync(reuse: attempt == 0);
            if (ShopeeBatchScreens.ChatButton(screen.Boxes) is { } b && Visible(screen, b)) chat = b;
            else
            {
                _inputs++;
                Wheel(new Point(screen.Area.Left + screen.Area.Width / 3, screen.Area.Top + screen.Area.Height / 2), -3);
                await Task.Delay(400, _token);
            }
        }
        if (chat is null || screen is null) { screen?.Dispose(); return "找不到「聊聊」按鈕"; }
        using (screen) Click(screen, chat, "聊聊");

        var sent = false;
        try
        {
            OcrBox? Ready(Screen s) =>
                ShopeeBatchScreens.ChatInput(s.Boxes) is { } i && Visible(s, i) ? i
                : ShopeeBatchScreens.ChatReopenButton(s.Boxes) is { } r && Visible(s, r) ? r : null;
            var (open, input) = await WaitOrNullAsync(10, Ready);
            if (open is null || input is null) return "聊聊視窗沒有出現";
            if (input.Text.Trim() == "重新啟用對話")
            {
                // The conversation was closed by 蝦皮 (user LOG 2026-10-15): opened again first.
                using (open) Click(open, input, "重新啟用對話");
                (open, input) = await WaitOrNullAsync(10, s => ShopeeBatchScreens.ChatInput(s.Boxes) is { } i && Visible(s, i) ? i : null);
                if (open is null || input is null) return "按「重新啟用對話」後聊聊輸入框沒有出現";
            }
            using (open) Click(open, input, "聊聊輸入框");
            for (var m = 0; m < messages.Count; m++)
            {
                var text = messages[m];
                await Task.Delay(300, _token);
                _inputs++;
                _log.Info("shopee-batch", $"paste 聊聊 message {m + 1}/{messages.Count} length={text.Length}");
                // Pasted, not typed (user, 2026-10-16: faster); still checked in the input below.
                await PasteAsync(text);

                var (filled, _) = await WaitOrNullAsync(5, s => ShopeeBatchScreens.ChatInputHolds(s.Boxes, input, text) ? s : null);
                if (filled is null) { ClearChatInput(); return $"第 {m + 1} 則訊息沒有出現在輸入框"; }
                filled.Dispose();
                Keys("Enter (送出聊聊)", NativeMethods.VK_RETURN);
                var (empty, _) = await WaitOrNullAsync(8, s => ShopeeBatchScreens.ChatInput(s.Boxes) is not null ? s : null);
                if (empty is null) return $"第 {m + 1} 則按 Enter 後輸入框沒有清空，不確定是否已送出";
                empty.Dispose();
                sent = true;
                _log.Info("shopee-batch", $"聊聊 message {m + 1} sent");
            }
            return null;
        }
        finally
        {
            await FoldChatAsync(sent);
        }
    }

    /// <summary>What was typed is removed, so nothing half-written is left in the chat.</summary>
    private void ClearChatInput()
    {
        Keys("Ctrl+A (聊聊輸入框)", NativeMethods.VK_CONTROL, 0x41 /* A */);
        Keys("Delete", 0x2E /* VK_DELETE */);
    }

    /// <summary>
    /// » folds the 聊聊 side panel (user, 2026-10-14); Esc when it is not found. The panel must be
    /// gone afterwards: left open, its text (another order's number, user LOG 2026-10-15) mixes
    /// into the order page that is read next. Still open → the page is reloaded (F5); still open
    /// after that → stop.
    /// </summary>
    private async Task FoldChatAsync(bool sent)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using (var screen = await ReadAsync())
            {
                if (!ShopeeBatchScreens.ChatPanelOpen(screen.Boxes))
                {
                    _log.Info("shopee-batch", $"聊聊 folded sent={sent} attempts={attempt}");
                    return;
                }
                if (attempt == 2) Keys("F5（收不起聊聊，重新整理）", NativeMethods.VK_F5);
                else if (ShopeeBatchScreens.ChatCollapseButton(screen.Boxes) is { } fold && Visible(screen, fold)) Click(screen, fold, "收起聊聊");
                else Keys("Esc (聊聊)", 0x1B /* VK_ESCAPE */);
            }
            await Task.Delay(attempt == 2 ? 3000 : 800, _token);
        }
        var (closed, _) = await WaitOrNullAsync(15, s => !ShopeeBatchScreens.ChatPanelOpen(s.Boxes) && ShopeeBatchScreens.IsOrderDetail(s.Boxes) ? s : null);
        if (closed is null)
            throw new ShopeeBatchStop($"聊聊視窗收不起來（{(sent ? "訊息已送出" : "訊息沒有送出")}），為避免讀錯訂單已停止，請關掉聊聊後再開始。");
        closed.Dispose();
        _log.Info("shopee-batch", $"聊聊 folded by reload sent={sent}");
    }

    /// <summary>The order page back at its top (left margin clicked for focus, then Home).</summary>
    private async Task BackToOrderTopAsync()
    {
        using var screen = await ReadAsync();
        _inputs++;
        Front();
        InputSender.Click(new Point(screen.Area.Left + 12, screen.Area.Top + screen.Area.Height / 2));
        await Task.Delay(150, _token);
        InputSender.Press(NativeMethods.VK_HOME);
        await Task.Delay(700, _token);
    }

    /// <summary>Like <see cref="WaitAsync"/>, but null instead of stopping the batch when it does not come.</summary>
    private async Task<(Screen? Screen, T? Value)> WaitOrNullAsync<T>(int seconds, Func<Screen, T?> probe) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var screen = await ReadAsync();
            if (probe(screen) is { } value) return (screen, value);
            screen.Dispose();
            await Task.Delay(400, _token);
        }
        return (null, null);
    }

    /// <summary>
    /// 新竹物流 安排取件 (user, 2026-10-14): the dialog's 快遞向你取件的地址 must be the set address
    /// before its 確認 is pressed; otherwise 取消 and the order is left for a person, unshipped.
    /// </summary>
    private async Task ConfirmPickupAsync(ShopeeBatchOrder order)
    {
        var (dialog, confirm) = await WaitAsync("「安排取件」視窗", 30, s => ShopeeBatchScreens.PickupConfirmButton(s.Boxes));
        using (dialog)
        {
            if (!ShopeeBatchScreens.PickupAddressMatches(dialog.Boxes, _print.HctPickupAddress))
            {
                _log.Info("shopee-batch", "pickup address is not the set one; cancelled");
                if (ShopeeBatchScreens.PickupCancelButton(dialog.Boxes) is { } cancel) Click(dialog, cancel, "取消（安排取件）");
                await Task.Delay(800, _token);
                throw new ShopeeBatchSkip("未出貨：「安排取件」視窗的取件地址與設定的「取件地址」不同，已按取消，請人工處理。");
            }
            Click(dialog, confirm, "確認（安排取件）");
        }
        _log.Info("shopee-batch", $"pickup confirmed order={_log.Value(order.OrderSn)}");
    }

    /// <summary>Shopee's 預覽列印 tab: 下載出貨文件中 → 下載完成 → checked → 列印出貨文件 → Chrome's print dialog.</summary>
    private async Task PrintShopeeLabelAsync(ShopeeBatchOrder order)
    {
        // 下載完成 shows before the label itself is drawn (user's LOG, 2026-10-13: 11 lines, then
        // 17 with the label), so the label's page is waited for too.
        var (awb, download) = await WaitAsync("出貨文件下載完成", 120, s =>
            ShopeeBatchScreens.IsAwbPage(s.Boxes) && ShopeeBatchScreens.LabelShown(s.Boxes) ? ShopeeBatchScreens.AwbDownloaded(s.Boxes) : null, shipped: true);
        using (awb)
        {
            if (download.Failed != 0 || download.Done != 1 || download.Total != 1)
                throw new ShopeeBatchStop($"出貨文件下載結果不對（成功 {download.Done}/{download.Total}，失敗 {download.Failed}）。", true);
            var labelSn = ShopeeBatchScreens.LabelOrderSn(awb.Boxes);
            var labelTracking = ShopeeBatchScreens.LabelTracking(awb.Boxes);
            // 新竹物流 labels print both numbers bare, without 訂單編號／寄件編號 (user, 2026-10-14).
            if (labelSn.Length == 0 && ShopeeBatchScreens.ShowsOrderSn(awb.Boxes, order.OrderSn)) labelSn = order.OrderSn;
            if (labelTracking.Length == 0 && ShopeeBatchScreens.ShowsNumber(awb.Boxes, order.Tracking)) labelTracking = order.Tracking;
            // The PDF's own text can leave lines out or map a letter wrong (user's LOG,
            // 2026-10-14: a 隔日到貨 label had no 蝦皮訂單編號 in its text at all): when neither
            // number settles it, the label as drawn is read with OCR.
            // The 寄件編號 is the order's own (the order page's dialog gave it), so the label's text
            // showing it is enough without the 訂單編號 (user, 2026-10-14: page text is fast, OCR slow).
            var textTracks = labelTracking.Length > 0 && ShopeeBatchScreens.SameTracking(labelTracking, order.Tracking);
            if (labelSn.Length == 0 && textTracks)
            {
                _log.Info("shopee-batch", "label text has no order number; its 寄件編號 matches the order's");
                labelSn = order.OrderSn;
            }
            if (labelSn != order.OrderSn || labelTracking.Length > 0 && !textTracks)
            {
                _log.Info("shopee-batch", $"label text order_found={labelSn.Length > 0} order_match={labelSn == order.OrderSn} tracking_found={labelTracking.Length > 0}; reading the label with OCR");
                var seen = await ReadLabelAsync(awb);
                var seenSn = ShopeeBatchScreens.LabelOrderSn(seen);
                if (seenSn.Length == 0 && ShopeeBatchScreens.ShowsOrderSnLoosely(seen, order.OrderSn)) seenSn = order.OrderSn;
                var seenTracking = ShopeeBatchScreens.LabelTracking(seen);
                if (seenTracking.Length == 0 && ShopeeBatchScreens.ShowsNumber(seen, order.Tracking)) seenTracking = order.Tracking;
                _log.Info("shopee-batch", $"label OCR order_match={ShopeeBatchScreens.SameOrderSn(seenSn, order.OrderSn)} tracking_match={seenTracking.Length > 0 && ShopeeBatchScreens.SameTracking(seenTracking, order.Tracking)}");
                if (ShopeeBatchScreens.SameOrderSn(seenSn, order.OrderSn)) labelSn = order.OrderSn;
                else if (seenSn.Length > 0) labelSn = seenSn;
                if (seenTracking.Length > 0) labelTracking = seenTracking;
            }
            if (labelSn != order.OrderSn)
                throw new ShopeeBatchStop($"寄件單上的訂單編號（{(labelSn.Length > 0 ? labelSn : "讀不到")}）不是 {order.OrderSn}，未列印。", true);
            if (labelTracking.Length > 0 && !ShopeeBatchScreens.SameTracking(labelTracking, order.Tracking))
                throw new ShopeeBatchStop($"寄件單上的寄件編號 {labelTracking} 與視窗顯示的 {order.Tracking} 不同，未列印。", true);
            var print = ShopeeBatchScreens.PrintDocumentsButton(awb.Boxes) ?? throw new ShopeeBatchStop("讀不到「列印出貨文件」按鈕。", true);
            Click(awb, print, "列印出貨文件");
        }
        if (order.Kind == ShopeeBatchCarrier.BlackCat) await SaveBlackCatLabelAsync(order);
        else if (NoteOnStoreSlips && ShopeeBatchScreens.IsStoreDelivery(order.Carrier)) await PrintPdfWithNoteAsync(order);
        else await PrintDialogAsync(order.Kind == ShopeeBatchCarrier.Hct ? _print.HctScale : _print.ShopeeScale, pages: null);
    }

    /// <summary>
    /// Prints a saved 黑貓 label PDF on the A4 printer after asking for the paper (the main
    /// window's); throws <see cref="ShopeeBatchStop"/> when it is not printed.
    /// </summary>
    public Func<ShopeeBatchOrder, string, Task>? PrintBlackCat { get; set; }

    /// <summary>
    /// 黑貓宅急便 (user, 2026-10-26): its label is a third of an A4 page that Chrome cannot put at
    /// the top, so Chrome's print dialog saves it as a PDF (另存為 PDF) into Temp instead and the
    /// label is printed from it into the top cell of a sheet; the PDF is deleted once printed.
    /// </summary>
    private async Task SaveBlackCatLabelAsync(ShopeeBatchOrder order)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "blackcat-" + order.OrderSn + "-" + DateTime.Now.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var pdf = await SavePrintToPdfAsync(folder);
        _log.Info("shopee-batch", "black cat label saved as PDF");
        if (PrintBlackCat is null) throw new ShopeeBatchStop($"黑貓寄件單已存成 PDF 但沒有列印（{folder}）。", true);
        await PrintBlackCat(order, pdf);
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The label as drawn (the PDF viewer's page, else the page under the header), read with OCR at twice the size.</summary>
    private async Task<List<OcrBox>> ReadLabelAsync(Screen awb)
    {
        var page = ShopeeBatchScreens.LabelPageBox(awb.Boxes);
        var rect = page is { } p
            ? new Rectangle(p.Left, p.Top, p.Width, p.Height)
            : new Rectangle(0, 100, awb.Image.Width, awb.Image.Height - 100);
        rect.Intersect(new Rectangle(0, 0, awb.Image.Width, awb.Image.Height));
        if (rect.Width < 20 || rect.Height < 20) return [];
        using var crop = awb.Image.Clone(rect, awb.Image.PixelFormat);
        var tokens = await _ocr.RecognizeLinesAsync(crop, _token, maxScale: 2.0);
        return tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)).ToList();
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
        if (NoteOnStoreSlips) await PrintPdfWithNoteAsync(order);
        else await PrintDialogAsync(_print.SevenScale, pages: null);
    }

    /// <summary>設定 › 一般 › 熱感紙寄件單都印下方文字, with words to print (user, 2026-10-26).</summary>
    private bool NoteOnStoreSlips => _print.SlipNoteAll && SlipNote.Lines(_print.SlipNote).Count > 0;

    /// <summary>
    /// A 超商 slip Chrome would print (蝦皮店到店, 7-11) with the 寄件單下方文字 (user, 2026-10-26):
    /// Chrome's print dialog, open, saves it as a PDF; its printed part is drawn at the thermal
    /// printer's resolution as large as fits above the note's block and printed with the note.
    /// </summary>
    private async Task PrintPdfWithNoteAsync(ShopeeBatchOrder order)
    {
        var printer = SlipPrinter.Installed(_printer) ?? throw new ShopeeBatchStop($"找不到名稱符合「{_printer}」的印表機，未列印。", true);
        var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "note-slip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var pdf = await SavePrintToPdfAsync(folder);
        var label = SlipPrinter.Label(printer, 100, 150);
        var widthMm = label.Width * 25.4 / label.Dpi - 4;
        var heightMm = label.Height * 25.4 / label.Dpi - SlipNote.BlockHeightMm - SlipNote.GapMm - 2;
        GrayImage slip;
        using (var drawn = await MoSlipPdf.RenderFitAsync(pdf, widthMm, heightMm, label.Dpi))
            slip = SlipPrinter.BlackWhite(drawn);
        _log.Info("shopee-batch", $"store slip from PDF {slip.Width}x{slip.Height} dots at {label.Dpi} dpi");
        PrintCutSlip(printer, slip, order.OrderSn);
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 全家 (user, 2026-10-13): 列印寄件單 opens the FamilyMart寄取貨單 in a new tab; Ctrl+P, then
    /// 頁面 自訂 2 and 縮放比例 自訂 165 print it right.
    /// </summary>
    private async Task PrintFamilySlipAsync(ShopeeBatchOrder order)
    {
        using (var slip = await WaitFamilyLabelPageAsync())
        {
            if (!ShopeeBatchScreens.ShowsOrderSnLoosely(slip.Boxes, order.OrderSn))
                throw new ShopeeBatchStop($"全家寄取貨單上讀不到訂單編號 {order.OrderSn}，未列印。", true);
            // The 寄件編號 (user, 2026-10-13: 11 digits, e.g. 「# 1635…」 on the order page) is the
            // slip's own 訂單編號.
            if (!ShopeeBatchScreens.ShowsNumber(slip.Boxes, order.Tracking))
                throw new ShopeeBatchStop($"全家寄取貨單上讀不到寄件編號 {order.Tracking}，未列印。", true);
        }
        if (await PrintFamilyCutAsync(order)) return;
        Keys("Ctrl+P", NativeMethods.VK_CONTROL, 0x50 /* P */);
        await PrintDialogAsync(_print.FamilyScale, _print.FamilyPages);
    }

    /// <summary>The FamilyMart寄取貨單 tab, once it shows (蝦皮 and 酷澎 open the same page).</summary>
    private async Task<Screen> WaitFamilyLabelPageAsync()
    {
        // The slip may be a picture with no page text (user's LOG, 2026-10-14: one line of text
        // for 18 s): then the screen is read with OCR.
        var watch = Stopwatch.StartNew();
        Screen? slip = null;
        while (slip is null)
        {
            var screen = await ReadAsync();
            if (ShopeeBatchScreens.IsFamilyLabelPage(screen.Boxes) && !ShopeeBatchScreens.IsPrintDialog(screen.Boxes)) { slip = screen; break; }
            var pictureOnly = screen.Boxes.Count <= 3 && !ShopeeBatchScreens.IsOrderDetail(screen.Boxes);
            screen.Dispose();
            if (pictureOnly && watch.Elapsed.TotalSeconds >= 2)
            {
                var seen = await ReadAsync(ocr: true);
                if (ShopeeBatchScreens.IsFamilyLabelPage(seen.Boxes)) { slip = seen; break; }
                seen.Dispose();
            }
            if (watch.Elapsed.TotalSeconds > 60)
                throw new ShopeeBatchStop("等不到全家 FamilyMart寄取貨單（60 秒）。", true);
            await Task.Delay(500, _token);
        }
        return slip;
    }

    /// <summary>
    /// 全家 cut to the label (user, 2026-10-14: page 2 at 165 % looked sharp but often did not
    /// scan): the page's picture is saved with Ctrl+S, the slip cut out of it and printed dot for
    /// dot (<see cref="SlipImage"/>). 蝦皮 and MO店+ slips need not look alike (user), so nothing
    /// about the layout is assumed: one slip must be found, with a barcode, and the cut slip
    /// must itself show the 寄件編號 (OCR). Anything short of that, before printing, returns
    /// false and the slip is printed through Chrome as before.
    /// </summary>
    private async Task<bool> PrintFamilyCutAsync(ShopeeBatchOrder order)
    {
        var cut = await CutFamilySlipAsync(order.Tracking);
        if (cut.Slip is null)
        {
            _log.Info("shopee-batch", $"family cut: {cut.Info}; printing through Chrome");
            return false;
        }
        PrintCutSlip(cut.Printer, cut.Slip, order.OrderSn);
        Front();
        return true;
    }

    /// <summary>The cut slip sent to the printer; an error there stops the batch (it may have printed).</summary>
    private void PrintCutSlip(string printer, GrayImage slip, string orderSn)
    {
        try
        {
            var dpi = SlipPrinter.Print(printer, slip, "全家寄取貨單 " + orderSn, _print.SlipNote);
            _log.Info("shopee-batch", $"family slip printed cut to the label ({slip.Width}x{slip.Height} dots at {dpi.DpiX}x{dpi.DpiY} dpi) note={dpi.Noted}");
            if (!dpi.Noted && SlipNote.Lines(_print.SlipNote).Count > 0) _log.Warn("shopee-batch", "slip too tall for the note block; printed without it");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warn("shopee-batch", $"family cut print failed: {ex.GetType().Name}: {ex.Message}");
            throw new ShopeeBatchStop($"全家寄取貨單送印表機 {printer} 時出錯（{ex.Message}），請確認是否已印出，未印出請手動列印。", true);
        }
    }

    /// <summary>
    /// Saves the slip's tab (in front) into a temp folder of its own, cuts the slip and
    /// deletes the folder (user, 2026-10-14: the saved page goes to TEMP and is deleted after).
    /// Slip null with the reason in Info when it cannot be cut; the 寄件編號 is checked by OCR
    /// when given.
    /// </summary>
    private async Task<(GrayImage? Slip, string Printer, string Info)> CutFamilySlipAsync(string? tracking)
    {
        // The program's own Temp folder beside the exe (user, 2026-10-14: not in AppData).
        var folder = Path.Combine(AppContext.BaseDirectory, "Temp", "slip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var printer = SlipPrinter.Installed(_printer);
            if (printer is null) return (null, string.Empty, $"找不到名稱符合「{_printer}」的印表機");
            Directory.CreateDirectory(folder);
            Keys("Ctrl+S (save the slip's page)", NativeMethods.VK_CONTROL, 0x53 /* S */);
            if ((await SaveFromDialogAsync(folder, "Ctrl+S")).Error is { } failed) return (null, printer, failed);
            var saved = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => $"{Path.GetExtension(f)}:{new FileInfo(f).Length}").ToList();
            _log.Info("shopee-batch", $"family page saved files={saved.Count} [{string.Join(", ", saved.Take(12))}]");
            if (SlipPrinter.LargestPicture(folder) is not { } picture) return (null, printer, $"存下的網頁裡沒有寄取貨單圖片（{saved.Count} 個檔案）");
            var page = picture.Image;
            var slips = SlipImage.FindSlips(page);
            var label = SlipPrinter.Label(printer, 100, 150);
            _log.Info("shopee-batch", $"family cut: picture {page.Width}x{page.Height} from {picture.From}, slips={slips.Count}, printer dpi={label.Dpi} label={label.Width}x{label.Height} paper={label.Paper}");
            if (slips.Count != 1) return (null, printer, $"圖片 {page.Width}×{page.Height} 裡找到 {slips.Count} 張寄取貨單，不是 1 張");
            var plan = SlipImage.Plan(page, slips[0], label.Width, label.Height);
            var mm = $"{plan.Width * 25.4 / label.Dpi:F0}×{plan.Height * 25.4 / label.Dpi:F0} mm";
            var info = $"原圖 {page.Width}×{page.Height}，寄取貨單 {slips[0].Width}×{slips[0].Height}，條碼最細線 {plan.Module} 點，" +
                (plan.Up > 1 ? $"放大 {plan.Up} 倍" : plan.Down > 1 ? $"縮小為 1/{plan.Down}" : "原尺寸") +
                $"，印出 {plan.Width}×{plan.Height} 點（{label.Dpi} dpi，約 {mm}）";
            _log.Info("shopee-batch", $"family cut: area={slips[0]} module={plan.Module} up={plan.Up} down={plan.Down} size={plan.Width}x{plan.Height} dots ({mm})");
            if (plan.Module == 0) return (null, printer, info + "；量不到條碼線寬");
            if (plan.Width > label.Width || plan.Height > label.Height || plan.Width * 25.4 / label.Dpi < 50) return (null, printer, info + "；尺寸不合標籤");
            var slip = SlipImage.Render(page, plan);
            if (tracking is not null && !await SlipShowsAsync(slip, tracking)) return (null, printer, info + "；裁好的圖讀不到寄件編號");
            return (slip, printer, info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ShopeeBatchStop)
        {
            _log.Warn("shopee-batch", $"family cut failed before printing: {ex.GetType().Name}: {ex.Message}");
            return (null, string.Empty, $"裁切時出錯：{ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Warn("shopee-batch", $"family cut: temp folder left: {ex.Message}"); }
        }
    }

    /// <summary>The cut slip read with OCR shows the 寄件編號.</summary>
    private async Task<bool> SlipShowsAsync(GrayImage slip, string tracking)
    {
        using var bmp = new Bitmap(slip.Width, slip.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, slip.Width, slip.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var row = new byte[data.Stride];
        for (var y = 0; y < slip.Height; y++)
        {
            for (var x = 0; x < slip.Width; x++) row[x * 3] = row[x * 3 + 1] = row[x * 3 + 2] = slip[x, y];
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
        }
        bmp.UnlockBits(data);
        var tokens = await _ocr.RecognizeLinesAsync(bmp, _token, maxScale: 2.0);
        var boxes = tokens.Select(t => new OcrBox(t.Text, t.Rect.Left, t.Rect.Top, t.Rect.Width, t.Rect.Height)).ToList();
        return ShopeeBatchScreens.ShowsNumber(boxes, tracking);
    }

    private static bool IsChromeProcess(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return string.Equals(p.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Chrome's 另存新檔 dialog, opened by the caller (Ctrl+S, a download): waited for until its
    /// name box holds Chrome's name, the folder changed through the address box (Alt+D) to
    /// <paramref name="folder"/>, 存檔 pressed; the saved files are found (in the folder, or where
    /// the dialog showed, then moved in). Error null with the files when saved.
    /// </summary>
    private async Task<(string? Error, List<string> Files)> SaveFromDialogAsync(string folder, string after, int waitSeconds = 10)
    {
        NativeMethods.GetWindowThreadProcessId(_window, out var chrome);
        nint dialog = 0;
        for (var i = 0; i < waitSeconds * 4 && dialog == 0; i++)
        {
            await Task.Delay(250, _token);
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.ClassName(hwnd) != "#32770") return true;
                // Chrome opens its file dialogs from a utility process of its own (user's LOG,
                // 2026-10-14: not found under the browser's process), so any chrome process.
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != chrome && !IsChromeProcess(pid)) return true;
                dialog = hwnd;
                return false;
            }, 0);
        }
        if (dialog == 0) return ($"按{after}後沒有出現另存新檔視窗", []);
        _log.Info("shopee-batch", $"save dialog title={NativeMethods.WindowText(dialog)}");
        try
        {
            // The dialog shows before its controls are built and filled (user's LOG,
            // 2026-10-14: searched 1 ms after it showed, no name box; earlier a name set too soon
            // was overwritten by Chrome's): waited for until the name box holds Chrome's name.
            nint edit = 0, save = 0;
            var candidates = new List<string>();
            for (var i = 0; i < 40; i++)
            {
                edit = 0;
                save = 0;
                candidates.Clear();
                nint fallback = 0;
                NativeMethods.EnumChildWindows(dialog, (hwnd, _) =>
                {
                    var cls = NativeMethods.ClassName(hwnd);
                    var id = NativeMethods.GetDlgCtrlID(hwnd);
                    if (cls == "Edit")
                    {
                        var parent = NativeMethods.GetParent(hwnd);
                        var parentClass = NativeMethods.ClassName(parent);
                        var grand = NativeMethods.ClassName(NativeMethods.GetParent(parent));
                        var visible = NativeMethods.IsWindowVisible(hwnd);
                        candidates.Add($"{id}/{parentClass}:{NativeMethods.GetDlgCtrlID(parent)}/{grand}/{(visible ? "v" : "h")}");
                        if (id == 1001 && NativeMethods.GetDlgCtrlID(parent) == 1148) edit = hwnd;
                        // The address bar's edit sits in a ComboBoxEx32; the name box's does not.
                        else if (fallback == 0 && visible && parentClass == "ComboBox" && grand != "ComboBoxEx32") fallback = hwnd;
                    }
                    if (cls == "Button" && id == 1 && save == 0) save = hwnd;
                    return true;
                }, 0);
                if (edit == 0) edit = fallback;
                if (edit != 0 && save != 0 && (NativeMethods.ControlText(edit, 500) ?? string.Empty).Trim().Length > 0) break;
                await Task.Delay(250, _token);
            }
            _log.Info("shopee-batch", $"save dialog edits [{string.Join(", ", candidates.Take(8))}] name_box={edit != 0} save={save != 0}");
            if (edit == 0 || save == 0) return ($"另存新檔視窗裡找不到{(edit == 0 ? "檔案名稱欄" : "存檔按鈕")}", []);
            await Task.Delay(300, _token);
            // The dialog takes no path from a program, however typed (user, 2026-10-14): it is
            // left as Chrome filled it and the page saved where Chrome saves; the files are then
            // found there and moved into the temp folder (user: moved to TEMP, deleted after).
            var name = (NativeMethods.ControlText(edit, 1000) ?? string.Empty).Trim().Trim('"');
            if (name.Length == 0) return ("另存新檔視窗沒有預設檔名", []);
            var stem = Path.GetFileNameWithoutExtension(name);
            // The folder is changed in the address box at the top (user, 2026-10-14: type the path
            // there): Alt+D, the temp folder, Enter. The folder the dialog then shows is read back
            // from that box (the edit in the ComboBoxEx32, which holds the path).
            _inputs++;
            NativeMethods.SetForegroundWindow(dialog);
            await Task.Delay(300, _token);
            InputSender.Chord(NativeMethods.VK_MENU, 0x44 /* D */);
            await Task.Delay(300, _token);
            InputSender.Chord(NativeMethods.VK_CONTROL, 0x41 /* A */);
            await Task.Delay(100, _token);
            await PasteAsync(folder);
            InputSender.Press(NativeMethods.VK_RETURN);
            string? shownFolder = null;
            for (var i = 0; i < 8; i++)
            {
                await Task.Delay(250, _token);
                shownFolder = null;
                NativeMethods.EnumChildWindows(dialog, (hwnd, _) =>
                {
                    if (NativeMethods.ClassName(hwnd) != "Edit" || NativeMethods.ClassName(NativeMethods.GetParent(NativeMethods.GetParent(hwnd))) != "ComboBoxEx32") return true;
                    var text = (NativeMethods.ControlText(hwnd, 500) ?? string.Empty).Trim();
                    if (Path.IsPathRooted(text) && Directory.Exists(text)) { shownFolder = text; return false; }
                    return true;
                }, 0);
                if (shownFolder is not null && string.Equals(Path.GetFullPath(shownFolder).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
            }
            var inTemp = shownFolder is not null && string.Equals(Path.GetFullPath(shownFolder).TrimEnd('\\'), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            _log.Info("shopee-batch", $"save dialog: address box to temp {(inTemp ? "worked" : "did not take; saving where it shows")} (folder {(shownFolder is null ? "not read" : "read")})");
            if (!NativeMethods.IsWindow(dialog) || !NativeMethods.IsWindowVisible(dialog)) return ("在路徑欄輸入後另存新檔視窗關閉了", []);
            var started = DateTime.Now.AddSeconds(-3);
            _inputs++;
            NativeMethods.SetForegroundWindow(dialog);
            await Task.Delay(300, _token);
            NativeMethods.PostMessage(save, 0x00F5 /* BM_CLICK */, 0, 0);
            for (var i = 0; i < 40 && NativeMethods.IsWindow(dialog) && NativeMethods.IsWindowVisible(dialog); i++)
            {
                await Task.Delay(250, _token);
                // A page of the same name saved before: 「確認另存新檔…要取代嗎？」 → 是 (IDYES).
                var confirm = ChromeDialogs().FirstOrDefault(h => h != dialog);
                if (confirm != 0)
                {
                    nint yes = 0;
                    NativeMethods.EnumChildWindows(confirm, (hwnd, _) =>
                    {
                        if (NativeMethods.ClassName(hwnd) == "Button" && NativeMethods.GetDlgCtrlID(hwnd) == 6) { yes = hwnd; return false; }
                        return true;
                    }, 0);
                    _log.Info("shopee-batch", $"save dialog: confirm title={NativeMethods.WindowText(confirm)} yes={yes != 0}");
                    if (yes != 0) NativeMethods.PostMessage(yes, 0x00F5 /* BM_CLICK */, 0, 0);
                    await Task.Delay(500, _token);
                }
            }
            if (NativeMethods.IsWindow(dialog) && NativeMethods.IsWindowVisible(dialog)) return ("按了存檔，另存新檔視窗沒有關閉", []);
            dialog = 0;

            // Chrome writes .crdownload first; done when the page file stops changing.
            var places = SavePlaces();
            if (shownFolder is not null && !inTemp) { places.RemoveAll(d => string.Equals(d, shownFolder, StringComparison.OrdinalIgnoreCase)); places.Insert(0, shownFolder); }
            places.Insert(0, folder);
            List<string> found = [];
            var last = string.Empty;
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(500, _token);
                found = places.SelectMany(d => Directory.EnumerateFileSystemEntries(d, stem + "*"))
                    .Where(e => File.GetLastWriteTime(e) >= started).ToList();
                var pageFile = found.FirstOrDefault(e => File.Exists(e) && !e.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) && !e.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
                if (pageFile is null || found.Any(e => e.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase))) continue;
                var now = string.Join("|", found.Select(e => e + ":" + (File.Exists(e) ? new FileInfo(e).Length : 0)));
                if (now != last) { last = now; continue; }
                foreach (var entry in found.Where(e => !string.Equals(Path.GetDirectoryName(e), folder, StringComparison.OrdinalIgnoreCase)))
                {
                    var to = Path.Combine(folder, Path.GetFileName(entry));
                    if (File.Exists(entry)) File.Move(entry, to);
                    else MoveFolder(entry, to);
                }
                _log.Info("shopee-batch", $"saved in temp (saved {(inTemp ? "there directly" : "elsewhere and moved")}) entries={found.Count}");
                return (null, found.Select(e => Path.Combine(folder, Path.GetFileName(e))).ToList());
            }
            return ($"存檔後 30 秒內在{string.Join("、", places.Select(Path.GetFileName))}找不到剛存的「{stem}」", []);
        }
        finally
        {
            if (dialog != 0 && NativeMethods.IsWindow(dialog)) NativeMethods.PostMessage(dialog, 0x0010 /* WM_CLOSE */, 0, 0);
        }
    }

    /// <summary>Visible dialog windows (#32770) of any chrome process.</summary>
    private static List<nint> ChromeDialogs()
    {
        var list = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.ClassName(hwnd) != "#32770") return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (IsChromeProcess(pid)) list.Add(hwnd);
            return true;
        }, 0);
        return list;
    }

    /// <summary>Where Chrome's 另存新檔 lands: 桌面, 下載, 文件 (the ones that exist).</summary>
    private static List<string> SavePlaces() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            SlipPrinter.DownloadsFolder(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>A folder moved, also onto another drive (copy, then delete).</summary>
    private static void MoveFolder(string from, string to)
    {
        if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase)) { Directory.Move(from, to); return; }
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        Directory.Delete(from, recursive: true);
    }

    /// <summary>
    /// Chrome's own print dialog (user, 2026-10-12/13). Chrome keeps the last print's settings,
    /// so 目的地 (the set printer, picked from its list when another shows), 頁面 (全部, or 自訂 with <paramref name="pages"/>) and 縮放比例 (自訂 <paramref name="wanted"/>)
    /// are set every time, opening 顯示更多設定 when 縮放比例 is folded away; then everything is
    /// read back — the set printer, the scale, the pages and 「1 張紙」 — before 列印 is pressed.
    /// What does not come right stops the batch without printing.
    /// </summary>
    private async Task PrintDialogAsync(int wanted, string? pages, int sheetsAllowed = 1)
    {
        // The settings are the right of the dialog: only that half is read (faster).
        var panel = new RectangleF(0.5f, 0, 0.5f, 1);
        var (opened, _) = await WaitAsync("Chrome 列印視窗", 30, s => ShopeeBatchScreens.IsPrintDialog(s.Boxes) ? s : null, shipped: true, part: panel);
        opened.Dispose();
        var reuse = true;
        var ocrDialog = false;
        var expands = 0;
        var lastExpand = Stopwatch.StartNew();
        var dumped = false;
        var actions = 0;
        var printerTries = 0;
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

            // 目的地 first: another printer has its own settings (user, 2026-10-14: Chrome showed
            // the Brother; the set printer is picked from 目的地's list).
            var shownPrinter = ShopeeBatchScreens.PrintDestination(boxes);
            if (shownPrinter.Length > 0 && !ShopeeBatchScreens.SamePrinter(shownPrinter, _printer))
            {
                if (printerTries >= 2)
                    throw new ShopeeBatchStop($"Chrome 列印的目的地是「{shownPrinter}」，換不成設定的「{_printer}」，未列印。", true);
                printerTries++;
                actions++;
                await ChoosePrinterAsync(screen, shownPrinter, panel);
                continue;
            }

            var scaleBox = ShopeeBatchScreens.PrintScaleBox(boxes);
            var scaleValue = ShopeeBatchScreens.PrintRowValue(boxes, "縮放比例");
            // 「顯示更多設定」 reads 「顯示較少設定」 once open, so while it still says 更多 the
            // settings are folded: a click made while the dialog was still loading may have done
            // nothing (user's LOG, 2026-10-14), so it is clicked again, a little apart.
            if (scaleValue is null && ShopeeBatchScreens.MoreSettings(boxes) is { } more && expands < 3 && (expands == 0 || lastExpand.ElapsedMilliseconds > 1500))
            {
                Click(screen, more, "顯示更多設定");
                expands++;
                lastExpand.Restart();
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
            if (scaleValue is null)
            {
                last = "讀不到縮放比例";
                if (!dumped && watch.Elapsed > TimeSpan.FromSeconds(8))
                {
                    dumped = true;
                    _log.Info("shopee-batch", $"print dialog without 縮放比例 ({(ocrDialog ? "OCR" : "page text")}): {string.Join(" | ", boxes.Select(b => ShopeeScreenshot.Compact(b.Text)).Where(t => t.Length is > 0 and <= 20).Take(40))}");
                }
                // Page text may leave the folded rows out; the screen is read instead.
                if (!ocrDialog && watch.Elapsed > TimeSpan.FromSeconds(10)) ocrDialog = true;
                await Task.Delay(600, _token);
                continue;
            }
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
            if (sheets > sheetsAllowed || pages is not null && sheets is null) { await Task.Delay(800, _token); continue; }
            if (ShopeeBatchScreens.PrintButton(boxes) is not { } printButton) { await Task.Delay(600, _token); continue; }
            _log.Info("shopee-batch", $"print dialog set: {last}");
            Click(screen, printButton, "列印");
            return;
        }
    }

    /// <summary>
    /// Opens 目的地's list and clicks the set printer in it (read from the screen: the list is
    /// drawn over the dialog). A printer not in the short list (only under 顯示更多…) stops the
    /// batch: choosing among all printers is left to a person.
    /// </summary>
    private async Task ChoosePrinterAsync(Screen screen, string shown, RectangleF part)
    {
        var value = ShopeeBatchScreens.PrintRowValue(screen.Boxes, "目的地") ?? throw new ShopeeBatchStop("讀不到 Chrome 列印的目的地。", true);
        _log.Info("shopee-batch", $"print destination is {shown}; choosing {_printer}");
        Click(screen, value, "目的地");
        await Task.Delay(700, _token);
        using var list = await ReadAsync(part: part, ocr: true);
        if (ShopeeBatchScreens.PrinterOption(list.Boxes, _printer) is not { } option)
        {
            _inputs++;
            InputSender.Press(NativeMethods.VK_ESCAPE);
            await Task.Delay(400, _token);
            throw new ShopeeBatchStop($"Chrome 列印目的地的清單裡沒有設定的「{_printer}」（目前是「{shown}」），請手動選好印表機再繼續，未列印。", true);
        }
        Click(list, option, $"印表機 {option.Text.Trim()}");
        // Chrome redraws the preview for the new printer.
        await Task.Delay(1500, _token);
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
        // Just outside an OCR box (the text, inside the button) and just inside a page-text box
        // (the whole button, user's PC 2026-10-13: the outside points fell on the grey page).
        var hits = 0;
        foreach (var (x, y) in new[]
                 {
                     (button.Left - 5, button.CenterY), (button.Right + 5, button.CenterY), (button.CenterX, button.Top - 4), (button.CenterX, button.Bottom + 4),
                     (button.Left + 4, button.CenterY), (button.Right - 4, button.CenterY), (button.CenterX, button.Top + 3), (button.CenterX, button.Bottom - 3)
                 })
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
