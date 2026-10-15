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
}
