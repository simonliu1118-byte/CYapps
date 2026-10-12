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
internal static partial class ShopeeBatchScreens
{
    private static string C(OcrBox box) => ShopeeScreenshot.Compact(box.Text);

    private static bool Has(IEnumerable<OcrBox> boxes, string text) => boxes.Any(b => C(b).Contains(text, StringComparison.Ordinal));

    private static bool SameRow(OcrBox a, OcrBox b) => Math.Abs(a.CenterY - b.CenterY) <= Math.Max(a.Height, b.Height) * 0.7;

    // ---- 我的銷售 › 待出貨 › 待處理 ----

    public static bool IsOrderList(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "我的銷售") && Has(boxes, "待處理") && Has(boxes, "建議出貨期限");

    /// <summary>The count of 「待處理 (n)」, when read.</summary>
    public static int? PendingCount(IReadOnlyList<OcrBox> boxes) => ChipCount(boxes, "待處理");

    /// <summary>The count of 「建議今天內出貨 (n)」, when read.</summary>
    public static int? TodayCount(IReadOnlyList<OcrBox> boxes) => ChipCount(boxes, "建議今天內出貨");

    private static int? ChipCount(IReadOnlyList<OcrBox> boxes, string label)
    {
        foreach (var box in boxes)
        {
            var match = ChipCountPattern().Match(C(box));
            if (match.Success && match.Groups[1].Value == label) return int.Parse(match.Groups[2].Value);
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
    public static ShopeeListOrder? FirstOrder(IReadOnlyList<OcrBox> boxes)
    {
        var header = boxes.Where(b => C(b) == "買家實付金額").OrderByDescending(b => b.Top).FirstOrDefault();
        if (header is null) return null;
        var status = boxes.Where(b => C(b) == "待出貨" && b.Top > header.Bottom).OrderBy(b => b.Top).FirstOrDefault();
        if (status is null) return null;
        var number = boxes.Where(b => b.Bottom <= status.Top + 2 && b.Top > header.Bottom && C(b).Contains("訂單編號", StringComparison.Ordinal))
            .OrderByDescending(b => b.Top).FirstOrDefault();
        var orderSn = number is null ? string.Empty : OrderSnIn(C(number));
        if (orderSn.Length == 0) return null;

        var carrierHeader = boxes.FirstOrDefault(b => C(b) == "物流方式" && SameRow(b, header));
        var actionHeader = boxes.FirstOrDefault(b => C(b) == "操作" && SameRow(b, header));
        var carrier = string.Empty;
        if (carrierHeader is not null)
        {
            var right = actionHeader?.Left - 8 ?? int.MaxValue;
            carrier = string.Concat(boxes
                .Where(b => b.Left >= carrierHeader.Left - 24 && b.Left < right &&
                            b.Top >= status.Top - status.Height && b.Top <= status.Bottom + status.Height * 2)
                .OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
        }
        return new ShopeeListOrder(orderSn, status, carrier);
    }

    /// <summary>The three carriers the batch ships (user, 2026-10-12): 蝦皮店到店 (and its 隔日到貨) and 蝦皮店到宅.</summary>
    public static bool SupportedCarrier(string carrier)
    {
        var text = ShopeeScreenshot.Compact(carrier);
        return text.Contains("蝦皮店到店", StringComparison.Ordinal) || text.Contains("蝦皮店到宅", StringComparison.Ordinal);
    }

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
            var guard = Rectangle.Inflate(new Rectangle(cancel.Left, cancel.Top, cancel.Width, cancel.Height), 12, 12);
            if (guard.IntersectsWith(new Rectangle(button.Left, button.Top, button.Width, button.Height))) return null;
            if (SameRow(cancel, button) && button.Left < cancel.Right) return null;
        }
        return button;
    }

    // ---- 查看寄件編號 ----

    public static OcrBox? TrackingDialogTitle(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b) == "查看寄件編號");

    public static bool TrackingRequested(IReadOnlyList<OcrBox> boxes) => Has(boxes, "寄件編號申請中");

    /// <summary>The TW number the dialog shows once assigned (spaces dropped).</summary>
    public static string DialogTracking(IReadOnlyList<OcrBox> boxes) =>
        boxes.Select(b => TrackingIn(C(b).ToUpperInvariant())).FirstOrDefault(t => t.Length > 0) ?? string.Empty;

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

    public static bool IsAwbPage(IReadOnlyList<OcrBox> boxes) => Has(boxes, "預覽列印");

    public static bool AwbDownloading(IReadOnlyList<OcrBox> boxes) => Has(boxes, "下載出貨文件中");

    /// <summary>「已成功下載1個包裹中的1個包裹的出貨文件。0個包裹下載失敗」, when read.</summary>
    public static ShopeeAwbDownload? AwbDownloaded(IReadOnlyList<OcrBox> boxes)
    {
        var text = string.Concat(boxes.Where(b => C(b).Contains("包裹", StringComparison.Ordinal)).OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C));
        if (!text.Contains("下載完成", StringComparison.Ordinal) && !Has(boxes, "下載完成")) return null;
        var done = AwbDonePattern().Match(text);
        var failed = AwbFailedPattern().Match(text);
        if (!done.Success || !failed.Success) return null;
        return new ShopeeAwbDownload(int.Parse(done.Groups[2].Value), int.Parse(done.Groups[1].Value), int.Parse(failed.Groups[1].Value));
    }

    public static OcrBox? PrintDocumentsButton(IReadOnlyList<OcrBox> boxes) => boxes.FirstOrDefault(b => C(b) == "列印出貨文件");

    /// <summary>The 蝦皮訂單編號 printed on the label.</summary>
    public static string LabelOrderSn(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b).Contains("訂單編號", StringComparison.Ordinal)).Select(b => OrderSnIn(C(b))).FirstOrDefault(s => s.Length > 0) ?? string.Empty;

    /// <summary>The 寄件編號 printed on the label.</summary>
    public static string LabelTracking(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b).Contains("寄件編號", StringComparison.Ordinal)).Select(b => TrackingIn(C(b).ToUpperInvariant())).FirstOrDefault(t => t.Length > 0) ?? string.Empty;

    // ---- Chrome 列印 ----

    public static bool IsPrintDialog(IReadOnlyList<OcrBox> boxes) => Has(boxes, "目的地") && Has(boxes, "縮放比例");

    /// <summary>The printer named beside 目的地.</summary>
    public static string PrintDestination(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "目的地");
        if (label is null) return string.Empty;
        return string.Concat(boxes.Where(b => b.Left > label.Right && SameRow(b, label)).OrderBy(b => b.Left).Select(b => b.Text.Trim()));
    }

    /// <summary>The number under 縮放比例's 自訂 (null when the scale is not 自訂 or not read).</summary>
    public static int? PrintScale(IReadOnlyList<OcrBox> boxes)
    {
        var label = boxes.FirstOrDefault(b => C(b) == "縮放比例");
        if (label is null) return null;
        var number = boxes.Where(b => b.Left > label.Right && b.Top >= label.Top - 4 && b.Top <= label.Bottom + label.Height * 4 && ScalePattern().IsMatch(C(b)))
            .OrderBy(b => b.Top).FirstOrDefault();
        return number is null ? null : int.Parse(C(number));
    }

    /// <summary>The dialog's 列印 button: the 列印 with 取消 beside it on its right (not the dialog's title).</summary>
    public static OcrBox? PrintButton(IReadOnlyList<OcrBox> boxes)
    {
        var cancel = boxes.Where(b => C(b) == "取消").OrderByDescending(b => b.Top).FirstOrDefault();
        if (cancel is null) return null;
        return boxes.FirstOrDefault(b => C(b) == "列印" && SameRow(b, cancel) && b.Right < cancel.Left);
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

    internal static string TrackingIn(string text)
    {
        var match = TrackingInPattern().Match(text);
        return match.Success ? ShopeeScreenshot.TrackingDigits(match.Value) : string.Empty;
    }

    /// <summary>Two tracking numbers read from different screens, the same?</summary>
    public static bool SameTracking(string a, string b)
    {
        static string N(string s) => ShopeeScreenshot.TrackingDigits(new string(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()));
        return N(a).Length > 0 && N(a) == N(b);
    }

    [GeneratedRegex(@"^(待處理|建議今天內出貨|建議明天內出貨)[\(（](\d+)[\)）]")]
    private static partial Regex ChipCountPattern();

    [GeneratedRegex(@"(?<![0-9A-Z])[0-9OIL]{6}[0-9A-Z]{8}(?![0-9A-Z])")]
    private static partial Regex OrderSnInPattern();

    [GeneratedRegex(@"TW[0-9OIL]{12}[0-9A-Z](?![0-9A-Z])")]
    private static partial Regex TrackingInPattern();

    [GeneratedRegex(@"^包\S\d*[:：]")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"(\d+)個包裹中的(\d+)個")]
    private static partial Regex AwbDonePattern();

    [GeneratedRegex(@"(\d+)個包裹下載失敗")]
    private static partial Regex AwbFailedPattern();

    [GeneratedRegex(@"^\d{2,3}$")]
    private static partial Regex ScalePattern();
}
