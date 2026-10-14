using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>The three ways MO店+ orders ship (user, 2026-10-14); anything else is left for a person.</summary>
internal enum MoShipKind { Hct, Seven, Family, Other }

/// <summary>A 配送方式 button of 批次出貨 with its count, e.g. 「全家店出(1)」.</summary>
internal sealed record MoShipMethod(string Name, int Count, MoShipKind Kind, OcrBox Box);

/// <summary>The newest row of 批次出貨記錄.</summary>
internal sealed record MoShipRecord(string Time, string Method, int Orders, int Shipped, string Status, OcrBox? PrintButton);

/// <summary>
/// What the MO店+ 店+管理系統 pages show (user's screenshots, 2026-10-14), read from their text:
/// E001訂單管理 with 我的訂單 and 批次出貨, the 批次出貨 page, its 批次出貨記錄 and the dialogs on
/// the way. Pure: no Windows, unit tested.
/// </summary>
internal static partial class MoBatchScreens
{
    private static string C(OcrBox box) => ShopeeScreenshot.Compact(box.Text);
    private static bool Has(IEnumerable<OcrBox> boxes, string text) => boxes.Any(b => C(b).Contains(text, StringComparison.Ordinal));
    private static bool SameRow(OcrBox a, OcrBox b) => Math.Abs(a.CenterY - b.CenterY) <= Math.Max(a.Height, b.Height) * 0.7;

    /// <summary>Signed in: the header's 登出 is there; without it the page is the login (or not MO店+).</summary>
    public static bool SignedIn(IReadOnlyList<OcrBox> boxes) => Has(boxes, "店+管理系統") && Has(boxes, "登出");

    public static OcrBox? Text(IReadOnlyList<OcrBox> boxes, string text) =>
        boxes.Where(b => C(b) == text).OrderBy(b => b.Top).FirstOrDefault();

    /// <summary>The order menu entry 「E001訂單管理」 (shown once 訂單管理 opens).</summary>
    public static OcrBox? OrderManagement(IReadOnlyList<OcrBox> boxes) =>
        boxes.FirstOrDefault(b => C(b).EndsWith("訂單管理", StringComparison.Ordinal) && C(b).StartsWith("E0", StringComparison.Ordinal) && b.Height < 60);

    /// <summary>On E001訂單管理: the left menu with 我的訂單 and 批次出貨.</summary>
    public static bool IsOrderManagement(IReadOnlyList<OcrBox> boxes) =>
        Text(boxes, "我的訂單") is not null && boxes.Any(b => C(b) == "批次出貨") && boxes.Any(b => C(b) == "退貨/退款");

    /// <summary>A tab with its count, 「未出貨(2)」 → 2, 「未出貨」 → 0; null when the tab is not shown.</summary>
    public static (OcrBox Box, int Count)? Tab(IReadOnlyList<OcrBox> boxes, string name)
    {
        foreach (var box in boxes.OrderBy(b => b.Top))
        {
            var text = C(box);
            if (text == name) return (box, 0);
            var m = CountPattern().Match(text);
            if (m.Success && m.Groups[1].Value == name) return (box, int.Parse(m.Groups[2].Value));
        }
        return null;
    }

    /// <summary>
    /// A filter button beside its row's label (處理提醒 › 全部, 配送方式 › 全部): right of the label
    /// and within a row's height of it (the 配送方式 label sits between two rows of buttons).
    /// </summary>
    public static OcrBox? Filter(IReadOnlyList<OcrBox> boxes, string label, string button)
    {
        var name = Text(boxes, label);
        if (name is null) return null;
        return boxes.Where(b => b.Left > name.Right && Math.Abs(b.CenterY - name.CenterY) <= Math.Max(name.Height, b.Height) * 1.6 && C(b) == button)
            .OrderBy(b => b.Left).FirstOrDefault();
    }

    /// <summary>The 配送方式 buttons of 批次出貨 that have orders (a count after the name).</summary>
    public static List<MoShipMethod> ShipMethods(IReadOnlyList<OcrBox> boxes)
    {
        var label = Text(boxes, "配送方式");
        var methods = new List<MoShipMethod>();
        if (label is null) return methods;
        var row = boxes.Where(b => b.Left > label.Right && Math.Abs(b.CenterY - label.CenterY) <= label.Height * 3).OrderBy(b => b.Left).ToList();
        foreach (var box in row)
        {
            var text = C(box);
            // The count may be a piece of its own (「全家店出」「(1)」).
            if (!CountPattern().IsMatch(text) && row.FirstOrDefault(b => b.Left >= box.Right - 2 && b.Left - box.Right < 12 && SameRow(b, box) && PiecePattern().IsMatch(C(b))) is { } piece)
                text += C(piece);
            var m = CountPattern().Match(text);
            if (!m.Success || m.Groups[1].Value.Length == 0) continue;
            var count = int.Parse(m.Groups[2].Value);
            if (count > 0) methods.Add(new MoShipMethod(m.Groups[1].Value, count, Kind(m.Groups[1].Value), box));
        }
        return methods;
    }

    /// <summary>
    /// 第三方物流-新竹貨運 → 新竹, 7-11店出 → 7-11, 全家店出 → 全家 (user, 2026-10-14: the only
    /// three in use); 倉出, 冷凍 and the others are left for a person.
    /// </summary>
    public static MoShipKind Kind(string method)
    {
        if (method.Contains("冷凍", StringComparison.Ordinal) || method.Contains("倉出", StringComparison.Ordinal)) return MoShipKind.Other;
        if (method.Contains("新竹", StringComparison.Ordinal)) return MoShipKind.Hct;
        if (method.Contains("7-11", StringComparison.Ordinal) && method.Contains("店", StringComparison.Ordinal)) return MoShipKind.Seven;
        if (method.Contains("全家", StringComparison.Ordinal) && method.Contains("店", StringComparison.Ordinal)) return MoShipKind.Family;
        return MoShipKind.Other;
    }

    /// <summary>「已選 n 個訂單」 above the list, with n.</summary>
    /// <remarks>The page may give it in pieces (「已選」「0」「個訂單」, user's LOG 2026-10-14): the
    /// pieces on its row are read together; the box returned is the first piece.</remarks>
    public static (OcrBox Box, int Count)? Selected(IReadOnlyList<OcrBox> boxes)
    {
        foreach (var box in boxes.Where(b => C(b).StartsWith("已選", StringComparison.Ordinal)))
        {
            var row = C(box) + string.Concat(boxes.Where(b => b != box && SameRow(b, box) && b.Left >= box.Left && b.Left - box.Right < 80)
                .OrderBy(b => b.Left).Take(3).Select(C));
            var m = SelectedPattern().Match(row);
            if (m.Success) return (box, int.Parse(m.Groups[1].Value));
        }
        return null;
    }

    /// <summary>The 批次出貨 button beside 「已選 n 個訂單」 (not the left menu's 批次出貨).</summary>
    public static OcrBox? ShipSelectedButton(IReadOnlyList<OcrBox> boxes)
    {
        if (Selected(boxes) is not { } selected) return null;
        return boxes.Where(b => C(b) == "批次出貨" && b.Left > selected.Box.Left && SameRow(b, selected.Box)).OrderBy(b => b.Left).FirstOrDefault();
    }

    /// <summary>The 批次出貨（共 n 筆訂單） page: n, or null when not on it.</summary>
    public static int? ShipPageCount(IReadOnlyList<OcrBox> boxes)
    {
        foreach (var box in boxes)
        {
            var m = ShipPagePattern().Match(C(box));
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return null;
    }

    /// <summary>The order numbers listed on the 批次出貨 page (「訂單1: 66101400799232」).</summary>
    public static List<string> ShipPageOrders(IReadOnlyList<OcrBox> boxes) =>
        boxes.Select(b => ShipOrderPattern().Match(C(b))).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>「批次出貨中」, the dialog after 確認出貨, with its 關閉.</summary>
    public static OcrBox? ShippingDialogClose(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "批次出貨中") ? boxes.Where(b => C(b) == "關閉").OrderByDescending(b => b.Top).FirstOrDefault() : null;

    /// <summary>The newest row of 批次出貨記錄 (the first under the header 出貨時間).</summary>
    public static MoShipRecord? LatestRecord(IReadOnlyList<OcrBox> boxes)
    {
        var header = Text(boxes, "出貨時間");
        if (header is null) return null;
        var first = boxes.Where(b => b.Top > header.Bottom && TimePattern().IsMatch(C(b))).OrderBy(b => b.Top).FirstOrDefault();
        if (first is null) return null;
        var row = boxes.Where(b => SameRow(b, first) && b.Left > first.Left + first.Width / 2).OrderBy(b => b.Left).ToList();
        var cells = row.Select(C).ToList();
        int Number(int i) => i < cells.Count && int.TryParse(cells[i], out var n) ? n : 0;
        var method = cells.FirstOrDefault() ?? string.Empty;
        var status = cells.FirstOrDefault(c => c.Contains("成功", StringComparison.Ordinal) || c.Contains("失敗", StringComparison.Ordinal) || c.Contains("處理", StringComparison.Ordinal)) ?? string.Empty;
        var print = row.FirstOrDefault(b => C(b) == "列印文件");
        return new MoShipRecord(first.Text.Trim(), method, Number(1), Number(2), status, print);
    }

    /// <summary>The record is the one just made: its 配送方式 is the one shipped and the counts match.</summary>
    public static bool RecordMatches(MoShipRecord record, MoShipKind kind, int orders) =>
        Kind(record.Method) == kind && record.Orders == orders;

    /// <summary>The 注意 dialog of 列印文件: its 列印標籤 / 列印出貨總表 buttons.</summary>
    public static OcrBox? PrintDocumentButton(IReadOnlyList<OcrBox> boxes, string name) =>
        Has(boxes, "列印標籤") ? boxes.FirstOrDefault(b => C(b) == name) : null;

    /// <summary>「匯出提醒」 once its file is ready (已完成), with its 關閉.</summary>
    public static bool ExportDone(IReadOnlyList<OcrBox> boxes) => Has(boxes, "匯出提醒") && Has(boxes, "已完成");

    public static OcrBox? ExportClose(IReadOnlyList<OcrBox> boxes) =>
        Has(boxes, "匯出提醒") ? boxes.Where(b => C(b) == "關閉").OrderByDescending(b => b.Top).FirstOrDefault() : null;

    [GeneratedRegex(@"^(.*?)[(（](\d+)[)）]$")]
    private static partial Regex CountPattern();

    [GeneratedRegex(@"^[(（]\d+[)）]$")]
    private static partial Regex PiecePattern();

    [GeneratedRegex(@"^已選(\d+)個訂單")]
    private static partial Regex SelectedPattern();

    [GeneratedRegex(@"^批次出貨[(（]共(\d+)筆訂單[)）]$")]
    private static partial Regex ShipPagePattern();

    [GeneratedRegex(@"^訂單\d+[:：](\d{14})$")]
    private static partial Regex ShipOrderPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\d{2}:\d{2}:\d{2}$")]
    private static partial Regex TimePattern();
}
