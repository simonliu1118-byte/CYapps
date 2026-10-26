using System.Drawing;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>One order of the 我的銷售 list: its number, the 待出貨 text to click and its 物流方式.</summary>
internal sealed record ShopeeListOrder(string OrderSn, OcrBox Status, string Carrier);

/// <summary>The 下載完成 line of Shopee's 預覽列印 page.</summary>
internal sealed record ShopeeAwbDownload(int Done, int Total, int Failed);

/// <summary>
/// What the Shopee batch (user, 2026-10-12) reads on each screen of the seller centre, from
/// the OCR boxes of the browser window: which screen it is, and where to click. Every click
/// target is found by its own text; nothing is clicked by a fixed position, and anything not
/// found stops the batch rather than guessing.
/// </summary>
internal enum ShopeeBatchCarrier { Other, Shopee, SevenEleven, FamilyMart, Hct }

internal static partial class ShopeeBatchScreens
{
    /// <summary>A button the page gives no text (a dialog's ×), as page text reports it.</summary>
    public const string NamelessButton = "[button]";
    /// <summary>A check box without a name (酷澎's select-all, user 2026-10-15).</summary>
    public const string NamelessCheckBox = "[checkbox]";

    private static string C(OcrBox box) => ShopeeScreenshot.Compact(box.Text);

    private static bool Has(IEnumerable<OcrBox> boxes, string text) => boxes.Any(b => C(b).Contains(text, StringComparison.Ordinal));

    private static bool SameRow(OcrBox a, OcrBox b) => Math.Abs(a.CenterY - b.CenterY) <= Math.Max(a.Height, b.Height) * 0.7;

    // ---- 我的銷售 › 待出貨 › 待處理 ----

    // 待處理 is sometimes read 待虚理 (user's screenshots, 2026-10-13), so it does not decide.
    public static bool IsOrderList(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "我的銷售") && Has(boxes, "建議出貨期限");

    /// <summary>The count of 「待處理 (n)」, when read.</summary>
    public static int? PendingCount(IReadOnlyList<OcrBox> boxes) => ChipCount(boxes, "待處理");

    /// <summary>The count of 「建議今天內出貨 (n)」, when read.</summary>
    public static int? TodayCount(IReadOnlyList<OcrBox> boxes) => ChipCount(boxes, "建議今天內出貨");

    private static int? ChipCount(IReadOnlyList<OcrBox> boxes, string label)
    {
        foreach (var box in boxes)
        {
            var match = ChipCountPattern().Match(C(box));
            if (match.Success && match.Groups[1].Value.Replace('虚', '處') == label) return int.Parse(match.Groups[2].Value);
        }
        // Page text gives the label and its count apart: 「待處理」 「(19)」.
        foreach (var box in boxes.Where(b => C(b).Replace('虚', '處') == label))
        {
            var count = boxes.Where(b => b.Left >= box.Right - 2 && b.Left - box.Right < 30 && SameRow(b, box))
                .Select(b => CountPattern().Match(C(b))).FirstOrDefault(m => m.Success);
            if (count is not null) return int.Parse(count.Groups[1].Value);
        }
        return null;
    }

    public static bool NoOrders(IReadOnlyList<OcrBox> boxes) => Has(boxes, "找不到訂單");

    public static OcrBox? TodayChip(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b).StartsWith("建議今天內出貨", StringComparison.Ordinal));

    /// <summary>
    /// The 「全部」 of the 建議出貨期限 row, not the one of 訂單狀態 (user, 2026-10-12): the
    /// first 全部 to the right of the 建議出貨期限 label on its row.
    /// </summary>
    public static OcrBox? DeadlineAll(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b).StartsWith("建議出貨期限", StringComparison.Ordinal));
        if (label is null) return null;
        return boxes.Where(b => C(b) == "全部" && b.Left > label.Right && SameRow(b, label))
            .OrderBy(b => b.Left).FirstOrDefault();
    }

    /// <summary>
    /// The first order of the list (user, 2026-10-12: click its 待出貨, the text is large and
    /// clear): the topmost 「待出貨」 below the table header, the 訂單編號 above it and the
    /// 物流方式 column beside it. Null when the header or the number is not read.
    /// </summary>
    public static ShopeeListOrder? FirstOrder(IReadOnlyList<OcrBox> boxes) => Orders(boxes).FirstOrDefault();

    /// <summary>
    /// The list's orders top to bottom (user, 2026-10-13: orders left for a person are passed
    /// over, so the batch needs the next ones too). From the OCR: each 「待出貨」 below the
    /// table header with the 訂單編號 above it; from page text: one link per row.
    /// </summary>
    public static List<ShopeeListOrder> Orders(IReadOnlyList<OcrBox> boxes)
    {
        var orders = new List<ShopeeListOrder>();
        var header = boxes.Where(b => C(b) == "買家實付金額").OrderByDescending(b => b.Top).FirstOrDefault();
        if (header is null) return orders;
        var statuses = boxes.Where(b => C(b) == "待出貨" && b.Top > header.Bottom).OrderBy(b => b.Top).ToList();
        if (statuses.Count == 0) return OrderRows(boxes, header);
        var above = header.Bottom;
        foreach (var status in statuses)
        {
            var number = boxes.Where(b => b.Bottom <= status.Top + 2 && b.Top > above && C(b).Contains("訂單編號", StringComparison.Ordinal))
                .OrderByDescending(b => b.Top).FirstOrDefault();
            above = status.Bottom;
            var orderSn = number is null ? string.Empty : OrderSnIn(C(number));
            if (orderSn.Length > 0) orders.Add(new ShopeeListOrder(orderSn, status, string.Empty));
        }
        return orders;
    }

    /// <summary>
    /// Page text (user's PC, 2026-10-13): each order of the list is one link whose text is the
    /// whole row (cut short on a long row). The click goes to the row's 狀態 column, a little
    /// below its top (where 待出貨 stands); never the row's 安排出貨 button.
    /// </summary>
    private static List<ShopeeListOrder> OrderRows(IReadOnlyList<OcrBox> boxes, OcrBox header)
    {
        var orders = new List<ShopeeListOrder>();
        var statusHeader = boxes.FirstOrDefault(b => C(b) == "狀態" && SameRow(b, header));
        var x = statusHeader?.CenterX ?? header.Right + 120;
        foreach (var row in boxes.Where(b => b.Top > header.Bottom && b.Height >= 60 && C(b).Contains("訂單編號", StringComparison.Ordinal)).OrderBy(b => b.Top))
        {
            // From the row's own text, spaces kept: 「訂單編號 261011… 2 件」 would run on into 2 without them.
            var raw = row.Text;
            var at = raw.IndexOf("訂單編號", StringComparison.Ordinal);
            var orderSn = at >= 0 ? OrderSnIn(raw[at..]) : string.Empty;
            if (orderSn.Length > 0) orders.Add(new ShopeeListOrder(orderSn, new OcrBox("待出貨", x - 20, row.Top + 52, 40, 20), string.Empty));
        }
        return orders;
    }

    /// <summary>
    /// How the batch ships a carrier: 蝦皮店到店 (and its 隔日到貨) and 蝦皮店到宅 print Shopee's own
    /// label (user, 2026-10-12); 7-ELEVEN prints its 交貨便 slip from 7-11's page (2026-10-13).
    /// Anything else is not done by the batch.
    /// </summary>
    public static ShopeeBatchCarrier CarrierKind(string carrier)
    {
        var text = ShopeeScreenshot.Compact(carrier).ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        // 店到家宅配 (user's LOG, 2026-10-14) is Shopee's own home delivery: TW number, Shopee's label.
        if (text.Contains("蝦皮店到店", StringComparison.Ordinal) || text.Contains("蝦皮店到宅", StringComparison.Ordinal) ||
            text.Contains("店到家", StringComparison.Ordinal)) return ShopeeBatchCarrier.Shopee;
        if (text.Contains("7ELEVEN", StringComparison.Ordinal) || text.Contains("711", StringComparison.Ordinal) || text.Contains("統一超商", StringComparison.Ordinal))
            return ShopeeBatchCarrier.SevenEleven;
        if (text.Contains("全家", StringComparison.Ordinal) || text.Contains("FAMILYMART", StringComparison.Ordinal)) return ShopeeBatchCarrier.FamilyMart;
        // 新竹物流 is 安排取件 (user, 2026-10-14): a pickup at the set address, then Shopee's label.
        if (text.Contains("新竹", StringComparison.Ordinal) || text.Contains("HCT", StringComparison.Ordinal)) return ShopeeBatchCarrier.Hct;
        return ShopeeBatchCarrier.Other;
    }

    public static bool SupportedCarrier(string carrier) => CarrierKind(carrier) != ShopeeBatchCarrier.Other;

    // ---- 訂單明細 ----

    public static bool IsOrderDetail(IReadOnlyList<OcrBox> boxes) => Has(boxes, "訂單明細") && Has(boxes, "訂單編號");

    /// <summary>The order number shown under 訂單編號.</summary>
    public static string DetailOrderSn(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "訂單編號");
        var candidates = label is null ? boxes : boxes.Where(b => b.Top >= label.Top && b.Top <= label.Bottom + label.Height * 4);
        return candidates.Select(b => OrderSnIn(C(b))).FirstOrDefault(s => s.Length > 0) ?? string.Empty;
    }

    /// <summary>The carrier after 「包裹1:」 in 運送資訊.</summary>
    public static string DetailCarrier(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => PackagePattern().IsMatch(C(b)));
        if (label is null) return string.Empty;
        var row = boxes.Where(b => b == label || (b.Left > label.Left && SameRow(b, label))).OrderBy(b => b.Left).Select(C);
        return PackagePattern().Replace(string.Concat(row), string.Empty);
    }

    /// <summary>
    /// The orange 安排出貨／安排取件 button of the 待出貨 card (user, 2026-10-12). Never 不成立訂單:
    /// the button must be read whole, below the 待出貨 title, and wholly to the right of
    /// 不成立訂單 with a gap; otherwise null.
    /// </summary>
    public static OcrBox? ShipButton(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.Where(b => C(b).StartsWith("待出貨", StringComparison.Ordinal)).OrderBy(b => b.Top).FirstOrDefault();
        if (title is null) return null;
        var buttons = boxes.Where(b => C(b) is "安排出貨" or "安排取件" && b.Top > title.Top).ToList();
        if (buttons.Count != 1) return null;
        var button = buttons[0];
        foreach (var cancel in boxes.Where(b => C(b).Contains("不成立", StringComparison.Ordinal)))
        {
            // Page text gives whole buttons, 7 px apart (user's PC, 2026-10-13); a few pixels of guard.
            var guard = Rectangle.Inflate(new Rectangle(cancel.Left, cancel.Top, cancel.Width, cancel.Height), 3, 3);
            if (guard.IntersectsWith(new Rectangle(button.Left, button.Top, button.Width, button.Height))) return null;
            if (SameRow(cancel, button) && button.Left < cancel.Right) return null;
        }
        return button;
    }

    // ---- 買家備註 and 聊聊 (user, 2026-10-14) ----

    /// <summary>The 買家備註 card's text (empty when the order has none).</summary>
    public static string BuyerNote(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.FirstOrDefault(b => C(b) == "買家備註");
        if (title is null) return string.Empty;
        return string.Concat(boxes.Where(b => b != title && b.Top >= title.Bottom - 2 && b.Top < title.Bottom + 80 && b.Left >= title.Left - 40)
            .OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
    }

    /// <summary>The buyer card's 聊聊 button: on the row of 關注, right of it.</summary>
    public static OcrBox? ChatButton(IReadOnlyList<OcrBox> boxes)
    {
        var follow = boxes.FirstOrDefault(b => C(b) == "關注");
        if (follow is null) return null;
        return boxes.Where(b => C(b).EndsWith("聊聊", StringComparison.Ordinal) && C(b).Length <= 4 && b.Left > follow.Right && SameRow(b, follow))
            .OrderBy(b => b.Left).FirstOrDefault();
    }

    /// <summary>The chat's empty input (its 輸入文字 placeholder).</summary>
    public static OcrBox? ChatInput(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b) == "輸入文字").OrderByDescending(b => b.Top).FirstOrDefault();

    /// <summary>
    /// The message is in the input: a piece of text at the input's place (it grows upward as the
    /// text wraps) that starts with the message's first characters.
    /// </summary>
    public static bool ChatInputHolds(IReadOnlyList<OcrBox> boxes, OcrBox input, string message)
    {
        var compact = ShopeeScreenshot.Compact(message);
        var wanted = compact[..Math.Min(12, compact.Length)];
        return wanted.Length > 0 && boxes.Any(b => C(b).StartsWith(wanted, StringComparison.Ordinal) &&
            b.Top < input.Bottom + 10 && b.Bottom > input.Top - 120 && Math.Abs(b.Left - input.Left) < 60);
    }

    /// <summary>
    /// The side panel's collapse (») button: right of 網頁版聊聊 on its row. The button has no name
    /// and is not always in the page text (user LOG 2026-10-15); then its place is taken from the
    /// first sample (2026-10-14: 22 px right of 網頁版聊聊, 24 px wide).
    /// </summary>
    public static OcrBox? ChatCollapseButton(IReadOnlyList<OcrBox> boxes)
    {
        var web = boxes.FirstOrDefault(b => C(b).EndsWith("網頁版聊聊", StringComparison.Ordinal));
        if (web is null) return null;
        return boxes.Where(b => b.Left > web.Right && SameRow(b, web) && b.Width <= 48 && b.Height <= 48)
                   .OrderBy(b => b.Left).FirstOrDefault()
               ?? new OcrBox("»", web.Right + 22, web.Top + web.Height / 2 - 12, 24, 24);
    }

    /// <summary>The 聊聊 side panel is open (its header, or the order it is about).</summary>
    public static bool ChatPanelOpen(IReadOnlyList<OcrBox> boxes) =>
        boxes.Any(b => C(b).EndsWith("網頁版聊聊", StringComparison.Ordinal) || C(b) is "您目前正與買家聊聊此訂單" or "您正在聊聊此訂單");

    /// <summary>
    /// A conversation 蝦皮 closed by itself has no input, only 「重新啟用對話」 under 「對話已自動關閉…」
    /// (user LOG 2026-10-15); pressed, the input comes back.
    /// </summary>
    public static OcrBox? ChatReopenButton(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b) == "重新啟用對話").OrderByDescending(b => b.Top).FirstOrDefault();

    // ---- 查看寄件編號 ----

    /// <summary>The number's dialog: 查看寄件編號, or 快遞取件詳情 after a 新竹物流 pickup (user, 2026-10-14).</summary>
    public static OcrBox? TrackingDialogTitle(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b) is "查看寄件編號" or "快遞取件詳情");

    // ---- 安排取件 (新竹物流, user 2026-10-14) ----

    /// <summary>The 安排取件 dialog, loaded: its 快遞向你取件的地址 card is there.</summary>
    public static OcrBox? PickupAddressLabel(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b) == "快遞向你取件的地址");

    /// <summary>
    /// Whether the dialog's pickup address is the one set (spaces ignored): the card under
    /// 快遞向你取件的地址 must contain it.
    /// </summary>
    public static bool PickupAddressMatches(IReadOnlyList<OcrBox> boxes, string address)
    {
        var label = PickupAddressLabel(boxes);
        var wanted = ShopeeScreenshot.Compact(address);
        if (label is null || wanted.Length < 6) return false;
        var card = string.Concat(boxes.Where(b => b.Top > label.Bottom - 2 && b.Top < label.Bottom + 140 && b.Left >= label.Left - 20)
            .OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
        return card.Contains(wanted, StringComparison.Ordinal);
    }

    /// <summary>The dialog's 確認: below the address, on the row of 取消 and right of it.</summary>
    public static OcrBox? PickupConfirmButton(IReadOnlyList<OcrBox> boxes)
    {
        var label = PickupAddressLabel(boxes);
        if (label is null) return null;
        var cancel = boxes.Where(b => C(b) == "取消" && b.Top > label.Bottom).OrderByDescending(b => b.Top).FirstOrDefault();
        if (cancel is null) return null;
        var confirm = boxes.Where(b => C(b) == "確認" && SameRow(b, cancel) && b.Left > cancel.Right).ToList();
        return confirm.Count == 1 ? confirm[0] : null;
    }

    /// <summary>The dialog's 取消 (to leave it untouched when the address is not the set one).</summary>
    public static OcrBox? PickupCancelButton(IReadOnlyList<OcrBox> boxes)
    {
        var label = PickupAddressLabel(boxes);
        return label is null ? null : boxes.Where(b => C(b) == "取消" && b.Top > label.Bottom).OrderByDescending(b => b.Top).FirstOrDefault();
    }

    /// <summary>The dialog's × when the OCR reads it (an X on the title's row, right of it).</summary>
    public static OcrBox? DialogCloseMark(IReadOnlyList<OcrBox> boxes, OcrBox title) =>
        boxes.Where(b => C(b) is "X" or "×" or "x" or NamelessButton && b.Left > title.Right && SameRow(b, title) && b.Width <= 48 && b.Height <= 48)
            .OrderBy(b => b.Left).FirstOrDefault();

    public static bool TrackingRequested(IReadOnlyList<OcrBox> boxes) => Has(boxes, "寄件編號申請中");

    /// <summary>The TW number the dialog shows once assigned (spaces dropped).</summary>
    public static string DialogTracking(IReadOnlyList<OcrBox> boxes)
    {
        var known = boxes.Select(b => TrackingIn(C(b).ToUpperInvariant())).FirstOrDefault(t => t.Length > 0);
        if (known is not null) return known;
        // Other formats (全家, not yet seen): the number line just under the dialog's title.
        var title = TrackingDialogTitle(boxes);
        if (title is null) return string.Empty;
        return boxes.Where(b => b.Top > title.Bottom && b.Top < title.Bottom + title.Height * 4)
            .Select(b => C(b).ToUpperInvariant())
            .FirstOrDefault(t => OtherTrackingPattern().IsMatch(t) && t.Count(char.IsAsciiDigit) >= 8) ?? string.Empty;
    }

    /// <summary>
    /// 全家 asks first (user, 2026-10-13): 「申請寄件編號」 at the dialog's bottom right. The dialog's
    /// title says the same (user's LOG, 2026-10-14: the title was clicked), so only the one on
    /// the row of 取消, right of it, is the button.
    /// </summary>
    public static OcrBox? ApplyTrackingButton(IReadOnlyList<OcrBox> boxes)
    {
        var named = boxes.Where(b => C(b) == "申請寄件編號").ToList();
        // The 申請寄件編號 dialog (寄件人真實姓名): its button sits right of 取消.
        var beside = Beside(boxes, named);
        if (beside.Count == 1) return beside[0];
        // In 查看寄件編號 (2026-10-13 screenshot) the button is the only one so named, below the title.
        var title = TrackingDialogTitle(boxes);
        return beside.Count == 0 && title is not null && named.Count == 1 && named[0].Top > title.Bottom ? named[0] : null;
    }

    /// <summary>The 申請寄件編號 dialog's own button (right of 取消) is shown.</summary>
    public static bool ApplyBesideCancel(IReadOnlyList<OcrBox> boxes) =>
        Beside(boxes, boxes.Where(b => C(b) == "申請寄件編號").ToList()).Count == 1;

    private static List<OcrBox> Beside(IReadOnlyList<OcrBox> boxes, List<OcrBox> named) =>
        named.Where(b => boxes.Any(c => C(c) == "取消" && SameRow(c, b) && c.Right <= b.Left)).ToList();

    public static OcrBox? PrintLabelButton(IReadOnlyList<OcrBox> boxes) => boxes.FirstOrDefault(b => C(b) == "列印寄件單");

    /// <summary>
    /// The dialog's × (user, 2026-10-12: close 查看寄件編號 there): the only ink right of the
    /// title inside the white dialog, as a point of the image; null when it is not clear.
    /// </summary>
    /// <param name="brightness">0–255 of the image pixel at (x, y).</param>
    public static Point? CloseCross(Func<int, int, int> brightness, int width, int height, OcrBox title)
    {
        // The dialog's right edge: along a row just above the title (clear of the ×), the
        // white dialog meets the dimmed page.
        var probeY = Math.Max(0, title.Top - 6);
        var edge = -1;
        for (int x = title.Right + 20, dark = 0; x < width; x++)
        {
            dark = brightness(x, probeY) < 235 ? dark + 1 : 0;
            if (dark >= 5) { edge = x - 5; break; }
        }
        if (edge < 0) return null;
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        var y0 = Math.Max(0, title.CenterY - title.Height);
        var y1 = Math.Min(height - 1, title.CenterY + title.Height);
        for (var y = y0; y <= y1; y++)
            for (var x = title.Right + 10; x < edge - 2; x++)
            {
                if (brightness(x, y) >= 200) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        if (right < 0 || right - left > 40 || bottom - top > 40 || right - left < 4) return null;
        return new Point((left + right) / 2, (top + bottom) / 2);
    }

    // ---- 預覽列印 (seller.shopee.tw/awbprint) ----

    public static bool IsAwbPage(IReadOnlyList<OcrBox> boxes) => Has(boxes, "預覽列印") || Has(boxes, "列印出貨文件") || Has(boxes, "下載出貨文件中");

    public static bool AwbDownloading(IReadOnlyList<OcrBox> boxes) => Has(boxes, "下載出貨文件中");

    /// <summary>「已成功下載1個包裹中的1個包裹的出貨文件。0個包裹下載失敗」, when read.</summary>
    public static ShopeeAwbDownload? AwbDownloaded(IReadOnlyList<OcrBox> boxes)
    {
        var text = string.Concat(boxes.Where(b => C(b).Contains("個包", StringComparison.Ordinal)).OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
        if (!text.Contains("下載完成", StringComparison.Ordinal) && !Has(boxes, "下載完成")) return null;
        var done = AwbDonePattern().Match(text);
        var failed = AwbFailedPattern().Match(text);
        if (!done.Success || !failed.Success) return null;
        return new ShopeeAwbDownload(int.Parse(done.Groups[2].Value), int.Parse(done.Groups[1].Value), int.Parse(failed.Groups[1].Value));
    }

    // Page text prefixes the button's icon (「 列印出貨文件」).
    public static OcrBox? PrintDocumentsButton(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b).EndsWith("列印出貨文件", StringComparison.Ordinal) && C(b).Length <= 8);

    /// <summary>
    /// The label is drawn: Chrome's PDF viewer shows its page (「第 1 頁」) or the label's own
    /// words are there (寄件編號／訂單編號).
    /// </summary>
    public static bool LabelShown(IReadOnlyList<OcrBox> boxes) =>
        LabelPageBox(boxes) is not null || boxes.Any(b => C(b).Contains("寄件編號", StringComparison.Ordinal) || C(b).Contains("訂單編號", StringComparison.Ordinal));

    /// <summary>The PDF viewer's page (「第 1 頁」), the label's area on screen.</summary>
    public static OcrBox? LabelPageBox(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => PageNumberPattern().IsMatch(C(b)) && b.Width >= 100 && b.Height >= 100).OrderBy(b => b.Top).FirstOrDefault();

    /// <summary>Two readings of one order number; OCR may read O／Q for 0 and I／L for 1.</summary>
    public static bool SameOrderSn(string a, string b)
    {
        static string N(string s) => s.ToUpperInvariant().Replace('O', '0').Replace('Q', '0').Replace('I', '1').Replace('L', '1');
        return a.Length == 14 && N(a) == N(b);
    }

    /// <summary>
    /// The 蝦皮訂單編號 printed on the label. The number may be its own piece of text after the
    /// label or on the next line (user's LOG, 2026-10-14: 隔日到貨 labels were never read with
    /// the label and number taken as one piece), so the label's row and the row under it count.
    /// </summary>
    public static string LabelOrderSn(IReadOnlyList<OcrBox> boxes) =>
        LabelValue(boxes, "訂單編號", OrderSnIn);

    /// <summary>The 寄件編號 printed on the label (same reading as <see cref="LabelOrderSn"/>).</summary>
    public static string LabelTracking(IReadOnlyList<OcrBox> boxes) =>
        LabelValue(boxes, "寄件編號", t => TrackingIn(t.ToUpperInvariant()));

    private static string LabelValue(IReadOnlyList<OcrBox> boxes, string label, Func<string, string> parse)
    {
        foreach (var box in boxes.Where(b => C(b).Contains(label, StringComparison.Ordinal)))
        {
            var own = parse(C(box)[C(box).IndexOf(label, StringComparison.Ordinal)..]);
            if (own.Length > 0) return own;
            var row = string.Concat(boxes.Where(b => b == box || (b.Left >= box.Left && SameRow(b, box))).OrderBy(b => b.Left).Select(C));
            var inRow = parse(row[Math.Max(0, row.IndexOf(label, StringComparison.Ordinal))..]);
            if (inRow.Length > 0) return inRow;
            var below = boxes.Where(b => b.Top >= box.Bottom - 2 && b.Top - box.Bottom < box.Height * 2 && b.Right > box.Left && b.Left < box.Right + box.Width)
                .OrderBy(b => b.Top).ThenBy(b => b.Left).FirstOrDefault();
            if (below is not null && parse(C(below)) is { Length: > 0 } next) return next;
        }
        return string.Empty;
    }

    // ---- 7-ELEVEN 交貨便服務單 (epayment.7-11.com.tw) ----

    /// <summary>The 交貨便 slip 7-11's page shows after 列印寄件單 (user, 2026-10-13).</summary>
    public static bool IsSevenLabelPage(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "交貨便服務代碼") || Has(boxes, "統一超商") && Has(boxes, "寄貨訂單編號");

    /// <summary>The 交貨便服務代碼 on the slip (the dialog's 寄件編號).</summary>
    public static string SevenLabelCode(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b).Contains("服務代碼", StringComparison.Ordinal)).Select(b => TrackingIn(C(b).ToUpperInvariant())).FirstOrDefault(t => t.Length > 0) ?? string.Empty;

    // ---- 全家 FamilyMart寄取貨單 (api.sls.shopee.tw) ----

    public static bool IsFamilyLabelPage(IReadOnlyList<OcrBox> boxes) =>
        boxes.Any(b => C(b).ToUpperInvariant().Contains("FAMILYMART", StringComparison.Ordinal) || C(b).Contains("寄取貨單", StringComparison.Ordinal));

    /// <summary>The order's number anywhere, read loosely (OCR's O／Q／0, I／L／1): 新竹物流 labels print it bare.</summary>
    public static bool ShowsOrderSnLoosely(IReadOnlyList<OcrBox> boxes, string orderSn) =>
        boxes.Any(b => SameOrderSn(OrderSnIn(C(b)), orderSn));

    /// <summary>The order's number anywhere on the page (全家 prints it on the line under 蝦皮訂單編號).</summary>
    public static bool ShowsOrderSn(IReadOnlyList<OcrBox> boxes, string orderSn) => boxes.Any(b => OrderSnIn(C(b)) == orderSn);

    /// <summary>A number (spaces ignored) anywhere on the page.</summary>
    public static bool ShowsNumber(IReadOnlyList<OcrBox> boxes, string number) =>
        number.Length > 0 && boxes.Any(b => C(b).ToUpperInvariant().Contains(number, StringComparison.Ordinal));

    // ---- Chrome 列印 ----

    /// <summary>「N 張紙」 at the dialog's top right.</summary>
    public static int? SheetCount(IReadOnlyList<OcrBox> boxes)
    {
        foreach (var box in boxes)
        {
            var match = SheetPattern().Match(C(box));
            if (match.Success) return int.Parse(match.Groups[1].Value);
        }
        return null;
    }

    /// <summary>The value shown beside a print setting's label (頁面 → 全部／自訂, 縮放比例 → 自訂…).</summary>
    public static OcrBox? PrintRowValue(IReadOnlyList<OcrBox> boxes, string label)
    {
        var name = boxes.FirstOrDefault(b => C(b) == label);
        return name is null ? null : boxes.Where(b => b.Left > name.Right + 20 && SameRow(b, name)).OrderBy(b => b.Left).FirstOrDefault();
    }

    /// <summary>The number box under 縮放比例's 自訂.</summary>
    public static OcrBox? PrintScaleBox(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "縮放比例");
        if (label is null) return null;
        return boxes.Where(b => b.Left > label.Right && b.Top >= label.Top - 4 && b.Top <= label.Bottom + label.Height * 4 && ScalePattern().IsMatch(C(b)))
            .OrderBy(b => b.Top).FirstOrDefault();
    }

    /// <summary>The page list typed under 頁面's 自訂 (例如 2), when shown.</summary>
    public static OcrBox? PrintPagesBox(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "頁面");
        var copies = boxes.FirstOrDefault(b => C(b) is "份數" or "份数");
        if (label is null) return null;
        var bottom = copies?.Top ?? label.Bottom + label.Height * 4;
        return boxes.Where(b => b.Left > label.Right + 20 && b.Top > label.Bottom && b.Bottom <= bottom && !SameRow(b, label))
            .OrderBy(b => b.Top).FirstOrDefault();
    }

    /// <summary>
    /// Chrome's print dialog; 縮放比例 shows only once 顯示更多設定 is open (user, 2026-10-13),
    /// so the dialog is known by 目的地 with either.
    /// </summary>
    public static bool IsPrintDialog(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "目的地") && (Has(boxes, "縮放比例") || MoreSettings(boxes) is not null || SaveButton(boxes) is not null);

    /// <summary>「顯示更多設定」, clicked only while 縮放比例 is not shown (a second click folds it again).</summary>
    public static OcrBox? MoreSettings(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b).Contains("更多設定", StringComparison.Ordinal));

    /// <summary>The printer named beside 目的地.</summary>
    public static string PrintDestination(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "目的地");
        if (label is null) return string.Empty;
        return string.Concat(boxes.Where(b => b.Left > label.Right && SameRow(b, label)).OrderBy(b => b.Left).Select(b => b.Text.Trim()));
    }

    /// <summary>
    /// In 目的地's opened list (user, 2026-10-14: Brother…, GG-D410 Printer, Xprinter…, 另存為 PDF,
    /// 顯示更多…), the option naming the set printer: under the 目的地 row, not the row itself.
    /// </summary>
    public static OcrBox? PrinterOption(IReadOnlyList<OcrBox> boxes, string wanted)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "目的地");
        if (label is null) return null;
        return boxes.Where(b => b.Top > label.Top && !SameRow(b, label) && SamePrinter(b.Text, wanted))
            .OrderBy(b => b.Top).FirstOrDefault();
    }

    /// <summary>The number under 縮放比例's 自訂 (null when the scale is not 自訂 or not read).</summary>
    public static int? PrintScale(IReadOnlyList<OcrBox> boxes) => PrintScaleBox(boxes) is { } number ? int.Parse(C(number)) : null;

    /// <summary>The dialog's 列印 button: the 列印 with 取消 beside it on its right (not the dialog's title).</summary>
    public static OcrBox? PrintButton(IReadOnlyList<OcrBox> boxes)
    {
        var cancel = boxes.Where(b => C(b) == "取消").OrderByDescending(b => b.Top).FirstOrDefault();
        if (cancel is null) return null;
        return boxes.FirstOrDefault(b => C(b) == "列印" && SameRow(b, cancel) && b.Right < cancel.Left);
    }

    /// <summary>
    /// 「儲存」 left of the bottom 取消: the print button while 目的地 is 另存為 PDF, whose panel has
    /// neither 縮放比例 nor 更多設定 (user's screenshot and LOG, 2026-10-20).
    /// </summary>
    public static OcrBox? SaveButton(IReadOnlyList<OcrBox> boxes)
    {
        var cancel = boxes.Where(b => C(b) == "取消").OrderByDescending(b => b.Top).FirstOrDefault();
        if (cancel is null) return null;
        return boxes.FirstOrDefault(b => C(b) == "儲存" && SameRow(b, cancel) && b.Right < cancel.Left);
    }

    /// <summary>Whether the printer shown is the one set (spaces and case ignored, either way round).</summary>
    public static bool SamePrinter(string shown, string wanted)
    {
        static string N(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
        var a = N(shown);
        var b = N(wanted);
        return Math.Min(a.Length, b.Length) >= 4 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    // ---- numbers ----

    internal static string OrderSnIn(string text)
    {
        var match = OrderSnInPattern().Match(text.ToUpperInvariant());
        return match.Success ? ShopeeScreenshot.OrderSnDateDigits(match.Value) : string.Empty;
    }

    /// <summary>A Shopee TW number, or a 7-11 交貨便 code (a letter and 11 digits, user 2026-10-13: G55 152 432 330).</summary>
    internal static string TrackingIn(string text)
    {
        var match = TrackingInPattern().Match(text);
        if (match.Success) return ShopeeScreenshot.TrackingDigits(match.Value);
        var seven = SevenCodePattern().Match(text);
        return seven.Success ? seven.Value[0] + seven.Value[1..].Replace('O', '0').Replace('I', '1').Replace('L', '1') : string.Empty;
    }

    /// <summary>
    /// Two readings of one tracking number, the same? A TW number's last character may be a
    /// letter or a digit, and the OCR reads its O and 0 either way (user's screenshots,
    /// 2026-10-13: 「…53O」 on one screen, 「…530」 on the next), so they count as one there.
    /// </summary>
    public static bool SameTracking(string a, string b)
    {
        static string N(string s)
        {
            var compact = new string(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            if (SevenCodePattern().IsMatch(compact) && compact.Length == 12) return TrackingIn(compact);
            var t = ShopeeScreenshot.TrackingDigits(compact);
            return t.StartsWith("TW", StringComparison.Ordinal) && t.Length > 2 && t[^1] == 'O' ? t[..^1] + "0" : t;
        }
        return N(a).Length > 0 && N(a) == N(b);
    }

    [GeneratedRegex(@"^(待[處虚]理|建議今天內出貨|建議明天內出貨)[\(（](\d+)[\)）]")]
    private static partial Regex ChipCountPattern();

    [GeneratedRegex(@"(?<![0-9A-Z])[0-9OIL]{6}[0-9A-Z]{8}(?![0-9A-Z])")]
    private static partial Regex OrderSnInPattern();

    [GeneratedRegex(@"TW[0-9OIL]{12}[0-9A-Z](?![0-9A-Z])")]
    private static partial Regex TrackingInPattern();

    [GeneratedRegex(@"(?<![0-9A-Z])[A-Z][0-9OIL]{11}(?![0-9A-Z])")]
    private static partial Regex SevenCodePattern();

    [GeneratedRegex(@"^包\S\d*[:：]")]
    private static partial Regex PackagePattern();

    // 包裹 is often read 包里 (user's screenshots, 2026-10-13).
    [GeneratedRegex(@"(\d+)個包[裹里裏]中的(\d+)個")]
    private static partial Regex AwbDonePattern();

    [GeneratedRegex(@"(\d+)個包[裹里裏]下載失敗")]
    private static partial Regex AwbFailedPattern();

    [GeneratedRegex(@"^[\(（](\d+)[\)）]$")]
    private static partial Regex CountPattern();

    [GeneratedRegex(@"^第\d+頁$")]
    private static partial Regex PageNumberPattern();

    [GeneratedRegex(@"(\d+)張[紙纸]")]
    private static partial Regex SheetPattern();

    [GeneratedRegex(@"^[0-9A-Z]{8,20}$")]
    private static partial Regex OtherTrackingPattern();

    [GeneratedRegex(@"^\d{2,3}$")]
    private static partial Regex ScalePattern();

    /// <summary>
    /// The red message Shopee shows at the top after 安排出貨 when it refuses, e.g. 「買家門市關轉，
    /// 請待買家重選完門市後，再重新申請寄件編號。(error:10514)」 (user's screenshot, 2026-10-26): the
    /// line with 「error:數字」 and the lines just above and below it, centred with it.
    /// </summary>
    public static string? ShipError(IReadOnlyList<OcrBox> boxes)
    {
        var error = boxes.FirstOrDefault(b => ErrorCodePattern().IsMatch(C(b)));
        if (error is null) return null;
        return string.Concat(boxes
            .Where(b => b == error || (Math.Abs(b.CenterY - error.CenterY) <= 35 && Math.Abs(b.CenterX - error.CenterX) <= 350))
            .OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
    }

    /// <summary>
    /// An item whose 規格 leaves the choice to the buyer's note, e.g. 「規格: 任選3罐(下單請備註指定的味道)」
    /// (user's screenshot, 2026-10-26): the 規格 line, or null. Such orders go to a person before shipping.
    /// </summary>
    public static string? BuyerChoiceSpec(IReadOnlyList<OcrBox> boxes) =>
        boxes.Select(C).FirstOrDefault(t => t.StartsWith("規格", StringComparison.Ordinal) &&
            (t.Contains("任選", StringComparison.Ordinal) || t.Contains("請備註", StringComparison.Ordinal)));

    /// <summary>The buyer's store closed (error 10514): nothing to do until the buyer picks another one.</summary>
    public static bool StoreClosed(string error) =>
        error.Contains("門市關轉", StringComparison.Ordinal) || error.Contains("10514", StringComparison.Ordinal);

    [GeneratedRegex(@"error[:：]\d{3,}", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorCodePattern();
}
