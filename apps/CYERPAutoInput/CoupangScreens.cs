using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>
/// 酷澎 Wing 我的訂單 lists (user's screenshots, 2026-10-15: 正在配送 › 配送中清單 「共 1 筆」
/// 「已選 0 筆」, 「下載Excel」 with a download icon, a select-all box left of the 訂單編號
/// header). Pure, unit tested.
/// </summary>
internal static class CoupangScreens
{
    private static string C(OcrBox b) => ShopeeScreenshot.Compact(b.Text);

    /// <summary>「下載Excel」 (an icon or spaces around it are allowed).</summary>
    public static OcrBox? DownloadExcel(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b).Contains("下載Excel", StringComparison.OrdinalIgnoreCase) && C(b).Length <= 14)
            .OrderBy(b => b.Top).FirstOrDefault();

    /// <summary>The list's 訂單編號 column header (the topmost one under 「下載Excel」).</summary>
    public static OcrBox? OrderHeader(IReadOnlyList<OcrBox> boxes, OcrBox download) =>
        boxes.Where(b => C(b) == "訂單編號" && b.Top > download.Bottom).OrderBy(b => b.Top).FirstOrDefault();

    /// <summary>
    /// The select-all box: an unnamed check box on the header's row left of 訂單編號; when the
    /// page gives none, where it sits in the sample (78 px left of the header's text).
    /// </summary>
    public static OcrBox SelectAll(IReadOnlyList<OcrBox> boxes, OcrBox header)
    {
        var mid = header.Top + header.Height / 2;
        return boxes.Where(b => C(b) == ShopeeBatchScreens.NamelessCheckBox && b.Right <= header.Left && b.Top <= mid + 12 && b.Bottom >= mid - 12)
                   .OrderByDescending(b => b.Left).FirstOrDefault()
               ?? new OcrBox("全選", header.Left - 78 - 8, mid - 8, 16, 16);
    }

    /// <summary>「共 n 筆」 and 「已選 m 筆」 (pieces may be split); null when not read.</summary>
    public static (int? Total, int? Selected) Counts(IReadOnlyList<OcrBox> boxes)
    {
        var text = string.Concat(boxes.OrderBy(b => b.Top / 12).ThenBy(b => b.Left).Select(C));
        static int? Read(string t, string pattern) => Regex.Match(t, pattern) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
        return (Read(text, @"共(\d+)筆"), Read(text, @"已選(\d+)筆"));
    }

    /// <summary>
    /// A 訂單狀態 card (訂單確認／配送準備／配送指示／正在配送／配送完成, user's screenshots
    /// 2026-10-19): its caption and the count shown large to its right; null when not read.
    /// </summary>
    public static (OcrBox Label, int Count)? StatusCard(IReadOnlyList<OcrBox> boxes, string name)
    {
        var label = boxes.Where(b => C(b).TrimEnd('?', '？', 'ⓘ') == name).OrderBy(b => b.Top).FirstOrDefault();
        if (label is null) return null;
        var mid = label.Top + label.Height / 2;
        var count = boxes.Where(b => Regex.IsMatch(C(b), @"^\d{1,4}$") && b.Left > label.Right && b.Left - label.Right < 340 &&
                                     b.Top <= mid + 30 && b.Bottom >= mid - 30)
            .OrderBy(b => b.Left).FirstOrDefault();
        return count is null ? null : (label, int.Parse(C(count)));
    }

    /// <summary>The 訂單編號 column header under a list title (「待確認訂單清單」, 「待安排配送清單」…).</summary>
    public static OcrBox? ListHeader(IReadOnlyList<OcrBox> boxes, string title)
    {
        var heading = boxes.Where(b => C(b).StartsWith(title, StringComparison.Ordinal)).OrderBy(b => b.Top).FirstOrDefault();
        return heading is null ? null : boxes.Where(b => C(b) == "訂單編號" && b.Top > heading.Bottom).OrderBy(b => b.Top).FirstOrDefault();
    }

    private static readonly Regex OrderNumber = new(@"^[•·‧\-]?(\d{12,16})$", RegexOptions.CultureInvariant);

    /// <summary>The 訂單編號 shown in the list's column, top to bottom.</summary>
    public static List<string> ListedOrders(IReadOnlyList<OcrBox> boxes, OcrBox header) =>
        boxes.Where(b => BelowHeader(b, header) && b.CenterX > header.Left - 80 && b.CenterX < header.Right + 80 && OrderNumber.IsMatch(C(b)))
            .OrderBy(b => b.Top).Select(b => OrderNumber.Match(C(b)).Groups[1].Value).Distinct().ToList();

    /// <summary>
    /// The 「確認訂單」 button at the bottom right. The hint left of it, 「點擊確認訂單後…」, carries its
    /// own 「確認訂單」 piece on the same row (user LOG 2026-10-19: that one was clicked), so the
    /// rightmost is the button.
    /// </summary>
    public static OcrBox? ConfirmOrdersButton(IReadOnlyList<OcrBox> boxes) =>
        boxes.Where(b => C(b) == "確認訂單").OrderByDescending(b => b.Right).FirstOrDefault();

    /// <summary>A dialog under <paramref name="title"/>: the boxes below it within its width.</summary>
    private static List<OcrBox> Dialog(IReadOnlyList<OcrBox> boxes, OcrBox title) =>
        boxes.Where(b => b.Top > title.Bottom && b.Top < title.Bottom + 420 && b.Left >= title.Left - 40 && b.Right <= title.Left + 520).ToList();

    /// <summary>
    /// 「確認接受訂單」 (after 確認訂單): how many orders it names (「您想接受以下n個訂單…」), the
    /// 訂單編號 it lists, and its 確認 button; null when not shown.
    /// </summary>
    public static (int? Count, List<string> Orders, OcrBox Confirm)? AcceptDialog(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.FirstOrDefault(b => C(b) == "確認接受訂單");
        if (title is null) return null;
        var inside = Dialog(boxes, title);
        var confirm = inside.Where(b => C(b) == "確認").OrderByDescending(b => b.Top).FirstOrDefault();
        if (confirm is null) return null;
        var text = string.Concat(inside.OrderBy(b => b.Top / 12).ThenBy(b => b.Left).Select(C));
        int? count = Regex.Match(text, @"接受以下(\d+)個訂單") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
        var orders = inside.Where(b => OrderNumber.IsMatch(C(b))).OrderBy(b => b.Top).Select(b => OrderNumber.Match(C(b)).Groups[1].Value).Distinct().ToList();
        return (count, orders, confirm);
    }

    /// <summary>The 確認 of 「配送處理完成。」 (shown after accepting); null when not shown.</summary>
    public static OcrBox? DoneDialogConfirm(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.FirstOrDefault(b => C(b).StartsWith("配送處理完成", StringComparison.Ordinal));
        return title is null ? null : Dialog(boxes, title).Where(b => C(b) == "確認").OrderByDescending(b => b.Top).FirstOrDefault();
    }

    /// <summary>
    /// A column's text in the list rows (配送通路 「宅配 - 新竹物流」…), compacted, top to bottom,
    /// one per row of <paramref name="header"/>'s 訂單編號.
    /// </summary>
    public static List<string> ColumnValues(IReadOnlyList<OcrBox> boxes, OcrBox header, string column)
    {
        var mid = header.Top + header.Height / 2;
        var head = boxes.FirstOrDefault(b => C(b).StartsWith(column, StringComparison.Ordinal) && b.Top <= mid + 24 && b.Bottom >= mid - 24);
        // Not above the bottom bar: its drop-down shows a channel too.
        var floor = boxes.Where(b => C(b) == "批次安排配送" || C(b) == "批次列印寄件單").Select(b => b.Top - 20).DefaultIfEmpty(int.MaxValue).Min();
        var cells = boxes.Where(b => BelowHeader(b, header) && b.Top < floor && Channel(C(b)) is not null).ToList();
        // In the column when its header is read (user LOG 2026-10-19: it was not), else any
        // channel-looking cell of the rows (「宅配 - 新竹物流」, 「便利商店 - 7-11」…).
        var inColumn = head is null ? [] : cells.Where(b => Math.Abs(b.CenterX - head.CenterX) <= 90).ToList();
        return (inColumn.Count > 0 ? inColumn : cells).OrderBy(b => b.Top).Select(b => Channel(C(b))!).ToList();
    }

    /// <summary>
    /// In a row under the header row. Wing's header cell is as tall as its row, so the first data
    /// row starts exactly at its bottom (user LOG 2026-10-19: header 611–668, row at 668).
    /// </summary>
    private static bool BelowHeader(OcrBox b, OcrBox header) => b.Top >= header.Bottom - 2 && b.CenterY > header.Bottom;

    /// <summary>A 配送通路／配送方式 name as the drop-down shows it (「宅配-新竹物流」), dashes made plain; null when not one.</summary>
    public static string? Channel(string compact)
    {
        var plain = compact.Replace('－', '-').Replace('–', '-').Replace('—', '-');
        return Regex.IsMatch(plain, @"^(宅配|便利商店|超商)-\S{1,12}$") ? plain : null;
    }

    /// <summary>
    /// 配送準備's bottom bar (user, 2026-10-19): 「批次安排配送」 and, left of it, the 配送方式 drop-down
    /// (its shown option, 「請選擇配送方式」 when none); null when not read.
    /// </summary>
    public static (OcrBox Select, OcrBox Arrange)? ShipBar(IReadOnlyList<OcrBox> boxes)
    {
        var arrange = boxes.Where(b => C(b) == "批次安排配送").OrderByDescending(b => b.Top).FirstOrDefault();
        if (arrange is null) return null;
        var mid = arrange.Top + arrange.Height / 2;
        var select = boxes.Where(b => b.Right <= arrange.Left + 4 && arrange.Left - b.Right < 120 && b.Top <= mid + 14 && b.Bottom >= mid - 14 &&
                                      (C(b) == "請選擇配送方式" || Channel(C(b)) is not null))
            .OrderByDescending(b => b.Left).FirstOrDefault();
        return select is null ? null : (select, arrange);
    }

    /// <summary>
    /// 「請確認發票開立需求」 (after 批次安排配送, not every time; user, 2026-10-19): its 「不用再提醒我」
    /// (the check box, else the label) and 確認; null when not shown.
    /// </summary>
    public static (OcrBox DontRemind, OcrBox Confirm)? InvoiceReminder(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.FirstOrDefault(b => C(b).Contains("請確認發票開立需求", StringComparison.Ordinal));
        if (title is null) return null;
        var inside = boxes.Where(b => b.Top > title.Bottom && b.Top < title.Bottom + 320 && b.Left >= title.Left - 60).ToList();
        var label = inside.FirstOrDefault(b => C(b) == "不用再提醒我");
        var confirm = inside.Where(b => C(b) == "確認").OrderByDescending(b => b.Top).FirstOrDefault();
        if (label is null || confirm is null) return null;
        var mid = label.Top + label.Height / 2;
        var box = inside.Where(b => C(b) == ShopeeBatchScreens.NamelessCheckBox && b.Right <= label.Left + 4 && label.Left - b.Right < 40 && b.Top <= mid + 12 && b.Bottom >= mid - 12)
            .OrderByDescending(b => b.Left).FirstOrDefault();
        return (box ?? label, confirm);
    }

    /// <summary>
    /// 「安排出貨」 after 批次安排配送 (user, 2026-10-19: 配送方式, 取貨日期, 取貨地址 with 編輯, 備註,
    /// 取消／提交): the 配送方式 shown, whether 取貨地址 holds <paramref name="pickupAddress"/>
    /// (spaces ignored; null when the dialog has no 取貨地址) and 提交; null when not shown.
    /// </summary>
    public static (string Method, bool? AddressMatches, OcrBox Submit)? ArrangeDialog(IReadOnlyList<OcrBox> boxes, string pickupAddress)
    {
        var submit = boxes.Where(b => C(b) == "提交").OrderByDescending(b => b.Top).FirstOrDefault();
        if (submit is null) return null;
        // The topmost: the rows' own 「安排出貨」 buttons behind the dialog sit lower.
        var title = boxes.Where(b => C(b) == "安排出貨" && b.Bottom < submit.Top && submit.Top - b.Bottom < 520 && b.Left < submit.Left)
            .OrderBy(b => b.Top).FirstOrDefault();
        if (title is null) return null;
        // 取貨地址 is two lines (address, then name and phone) around its label; 配送方式 one.
        string RowValue(string name, int reach)
        {
            var label = boxes.FirstOrDefault(b => C(b) == name && b.Top > title.Bottom && b.Bottom < submit.Top);
            if (label is null) return string.Empty;
            var mid = label.Top + label.Height / 2;
            return string.Concat(boxes.Where(b => b.Left > label.Right + 20 && b.Top < mid + reach && b.Bottom > mid - reach && b.Right < submit.Right + 10)
                .OrderBy(b => b.Top).ThenBy(b => b.Left).Select(C).Where(t => t != "編輯"));
        }
        var address = RowValue("取貨地址", 30);
        var wanted = ShopeeScreenshot.Compact(pickupAddress);
        bool? matches = address.Length == 0 ? null : wanted.Length >= 6 && address.Contains(wanted, StringComparison.Ordinal);
        return (RowValue("配送方式", 10), matches, submit);
    }

    /// <summary>
    /// 「取貨詳情」 after 提交 (user, 2026-10-19): the 訂單編號 it lists (each once: the cell and its
    /// link both read), those whose 狀態 reads 成功 (not at once: it was still loading a second after
    /// 提交, user LOG 2026-10-19), its 配送方式, and 列印寄件單／關閉; null when not shown.
    /// </summary>
    public static (List<string> Orders, List<string> Succeeded, string Method, OcrBox Print, OcrBox Close)? PickupDetails(IReadOnlyList<OcrBox> boxes)
    {
        var title = boxes.FirstOrDefault(b => C(b) == "取貨詳情");
        if (title is null) return null;
        var below = boxes.Where(b => b.Top > title.Bottom && b.Top < title.Bottom + 700).ToList();
        var print = below.Where(b => C(b) == "列印寄件單").OrderByDescending(b => b.Top).FirstOrDefault();
        var close = below.Where(b => C(b) == "關閉").OrderByDescending(b => b.Top).FirstOrDefault();
        if (print is null || close is null) return null;
        // Only the dialog: the list behind it has its own 訂單編號 further left.
        below = below.Where(b => b.Left >= title.Left - 40 && b.Right <= print.Right + 40).ToList();
        var rows = below.Where(b => b.Bottom < print.Top && OrderNumber.IsMatch(C(b))).OrderBy(b => b.Top).ToList();
        string Number(OcrBox b) => OrderNumber.Match(C(b)).Groups[1].Value;
        var orders = rows.Select(Number).Distinct().ToList();
        var succeeded = rows.Where(r => below.Any(b => C(b).Contains("成功", StringComparison.Ordinal) && b.Left > r.Right && b.Top < r.Bottom + 16 && b.Bottom > r.Top - 16))
            .Select(Number).Distinct().ToList();
        var methodLabel = below.FirstOrDefault(b => C(b) == "配送方式");
        var method = methodLabel is null ? string.Empty
            : below.Where(b => b.Top >= methodLabel.Bottom - 2 && b.Top < methodLabel.Bottom + 40 && Math.Abs(b.Left - methodLabel.Left) < 40)
                .Select(b => Channel(C(b))).FirstOrDefault(c => c is not null) ?? string.Empty;
        return (orders, succeeded, method, print, close);
    }
}
