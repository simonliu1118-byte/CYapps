using System.Drawing;

namespace CYERPAutoQuery;

/// <summary>
/// Pure rules about the SMART ERP INVI02 window and its 設定查詢條件 dialog
/// (no Win32 calls, so they are unit tested on any OS).
/// </summary>
internal static class Invi02Layout
{
    public const string FormTitle = "品號資料建立作業";
    public const string FormCode = "INVI02";
    public const string FormClass = "TfrmInvi02";
    public const string QueryDialogTitle = "設定查詢條件";
    public const string GeneralQueryTab = "一般選項";
    /// <summary>The 設定查詢條件 tab with the free condition box, used for the 品名 search.</summary>
    public const string AdvancedQueryTab = "進階設定";

    /// <summary>
    /// The tab shown before 轉出至 Excel: from 資料瀏覽 the export has no 單身資料 (庫別), from
    /// any other tab it has; 基本資料1 is used (user, 2026-10-16).
    /// </summary>
    public const string ExportTab = "基本資料1";

    /// <summary>The tab ERP shows after a query (資料瀏覽, the result list).</summary>
    public const string BrowseTab = "資料瀏覽";

    // INVI02 TcxPageControl tabs have one fixed width whatever the caption length. Measured
    // from the user's screenshots (2026-10-16, three captures identical): tab i is centered
    // 56 + 79 * i px right of the page control's left edge.
    public static readonly TabStrip Invi02Tabs = new(
        ["基本資料1", "基本資料2", "採購生管1", "採購生管2", "售價", "成本", "標準包裝", "屬性類別頁", "產品圖示", "資料瀏覽"],
        Origin: 56 - 79 / 2.0, Pitch: 79);

    // 設定查詢條件 dialog tabs (user's screenshot 2026-10-16): 一般選項 then 進階設定.
    public static readonly TabStrip QueryDialogTabs = new([GeneralQueryTab, AdvancedQueryTab], Origin: 50 - 66 / 2.0, Pitch: 66);

    // 轉出至 Excel 匯出設定: 匯出欄位設定 tabs 單頭 then 單身, measured from the user's
    // screenshot (2026-10-16): centers about 60 and 150 px right of the page control.
    public static readonly TabStrip ExportFieldTabs = new(["單頭", "單身"], Origin: 60 - 90 / 2.0, Pitch: 90);

    public static bool IsInvi02Title(string title) =>
        title.Contains(FormTitle, StringComparison.Ordinal) &&
        title.Contains(FormCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>The SMART ERP main window: 「鼎新電腦Conductor [user]」 (user's screenshot 2026-10-16).</summary>
    public static bool IsErpMainTitle(string title) =>
        title.Contains("Conductor", StringComparison.OrdinalIgnoreCase) && !IsInvi02Title(title);

    /// <summary>SMART ERP's main form class; its caption is 「DSForm」, not Conductor (V0.4.0 Build 3 LOG, 2026-10-16).</summary>
    public const string ErpMainFormClass = "SmartERP_iSM_MainMenu";

    /// <summary>Not a message box and not Delphi's hidden TApplication window (same caption, no size).</summary>
    public static bool IsErpMainClass(string className) =>
        !IsMessageDialogClass(className) && !className.Equals("TApplication", StringComparison.OrdinalIgnoreCase);

    public static bool IsQueryDialogTitle(string title) =>
        title.Contains(QueryDialogTitle, StringComparison.Ordinal);

    /// <summary>ERP message boxes may reuse the caption of the form that raised them.</summary>
    /// <summary>ERP's 「Leader作業元件」 box after 登入 when every licence is taken: 「人數已滿!!」 (user, 2026-10-27).</summary>
    public static bool IsSeatsFullMessage(string text) =>
        NormalizeCaption(text).Contains("人數已滿", StringComparison.Ordinal);

    /// <summary>
    /// ERP's 「Leader作業元件中心」 after being idle (user's screenshots, 2026-10-27): 「使用者因閒置而
    /// 登出授權，請問是否重新登入授權?」 (answered Yes) or 「…無法開啟作業程式!」 (answered OK).
    /// </summary>
    public static bool IsIdleLogoutMessage(string text) =>
        NormalizeCaption(text).Contains("登出授權", StringComparison.Ordinal);

    /// <summary>The idle-logout box that asks whether to log the licence in again (Yes／No).</summary>
    public static bool IsRelicenseQuestion(string text) =>
        NormalizeCaption(text).Contains("重新登入授權", StringComparison.Ordinal);

    public static bool IsMessageDialogClass(string className) =>
        className is "#32770" or "TMessageForm";

    /// <summary>A Windows file path such as the 儲存檔案 default 「C:\Users\…\INVI02_2.XLSX」.</summary>
    public static bool LooksLikeFilePath(string text) =>
        text.Length >= 4 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '\\';

    public static string NormalizeCaption(string value) =>
        value.Trim().Replace(" ", string.Empty).Replace("&", string.Empty)
            .Replace("（", "(").Replace("）", ")").ToUpperInvariant();

    public static bool IsInputLike(string className)
    {
        var s = className.ToUpperInvariant();
        return s.Contains("EDIT") || s.Contains("COMBO") || s.Contains("MEMO") || s.Contains("MASK") ||
               s.Contains("SPIN") || s.Contains("DATE") || s.Contains("LOOKUP") || IsCheckBox(className);
    }

    public static bool IsCheckBox(string className) =>
        className.Contains("CHECKBOX", StringComparison.OrdinalIgnoreCase);

    public static bool IsCombo(string className) =>
        className.Contains("COMBO", StringComparison.OrdinalIgnoreCase);

    public static bool IsEditBox(string className) =>
        className.Contains("EDIT", StringComparison.OrdinalIgnoreCase) && !IsCombo(className);

    /// <summary>
    /// Groups controls into visual rows (vertical centers within <paramref name="tolerance"/>),
    /// top to bottom, each row left to right.
    /// </summary>
    public static List<List<T>> BuildRows<T>(IEnumerable<T> items, Func<T, Rectangle> rect, int tolerance = 9)
    {
        var sorted = items
            .OrderBy(i => CenterY(rect(i)))
            .ThenBy(i => rect(i).Left)
            .ToList();
        var rows = new List<List<T>>();
        var centers = new List<int>();
        foreach (var item in sorted)
        {
            var cy = CenterY(rect(item));
            var index = centers.FindIndex(c => Math.Abs(cy - c) <= tolerance);
            if (index < 0)
            {
                rows.Add([item]);
                centers.Add(cy);
            }
            else rows[index].Add(item);
        }
        foreach (var row in rows) row.Sort((a, b) => rect(a).Left.CompareTo(rect(b).Left));
        return rows;
    }

    private static int CenterY(Rectangle r) => (r.Top + r.Bottom) / 2;
}

/// <summary>
/// A DevExpress tab strip. Tabs either all have the same width (Origin + Pitch) or, when
/// their width follows the caption, each has a measured center (<paramref name="Centers"/>).
/// The measured geometry is the primary path; when clicking there does not show the tab, the
/// caller locates the caption by OCR instead (user decision 2026-10-16).
/// </summary>
/// <param name="Origin">Left edge of the first tab, px right of the page control's left edge.</param>
/// <param name="Centers">Per-tab center, px right of the page control's left edge (overrides Origin/Pitch).</param>
internal sealed record TabStrip(string[] Order, double Origin, double Pitch, double[]? Centers = null)
{
    private const int StripCenterY = 12;

    public int IndexOf(string tab) => Array.IndexOf(Order, tab);

    /// <summary>Click point of <paramref name="tab"/>, or null when unknown or outside the page.</summary>
    public Point? PointFor(Rectangle page, string tab)
    {
        var index = IndexOf(tab);
        if (index < 0) return null;
        double center;
        if (Centers is not null)
        {
            if (index >= Centers.Length) return null;
            center = Centers[index];
        }
        else
        {
            if (Pitch < 8) return null;
            center = Origin + Pitch * (index + 0.5);
        }
        var x = (int)Math.Round(center);
        if (x <= 2 || x >= page.Width - 4) return null;
        return new Point(page.Left + x, page.Top + StripCenterY);
    }
}

/// <summary>What the read-only status probe sees of INVI02.</summary>
internal enum Invi02Status { NotOpen, Multiple, Browse, Editing, DialogOpen, Unknown }

internal static class ItemCodeRules
{
    public const int MaxLength = 40;

    /// <summary>
    /// Accepts one ERP 品號: trimmed, 1..40 characters, ASCII letters, digits and 「-」 (user
    /// 2026-10-21: codes such as K1807003-1 exist), starting with a letter or digit; no spaces,
    /// other symbols, full-width or Chinese characters.
    /// </summary>
    public static bool TryNormalize(string? input, out string code, out string error, string what = "品號")
    {
        code = (input ?? string.Empty).Trim();
        error = string.Empty;
        if (code.Length == 0)
        {
            error = $"請輸入{what}。";
            return false;
        }
        if (code.Length > MaxLength)
        {
            error = $"{what}長度超過 {MaxLength} 字。";
            return false;
        }
        // SMART ERP codes are letters, digits and 「-」 (user, 2026-10-16／10-21); anything else is
        // refused before ERP is touched.
        if (!char.IsAsciiLetterOrDigit(code[0]) || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            error = $"{what}只能輸入英文字母、數字與「-」。";
            return false;
        }
        return true;
    }

    /// <summary>
    /// SMART ERP item codes are case-sensitive (user, 2026-10-16: a lower-case c00154 does not
    /// find C00154), so only surrounding spaces are ignored.
    /// </summary>
    public static bool SameCode(string? shown, string requested) =>
        string.Equals((shown ?? string.Empty).Trim(), requested.Trim(), StringComparison.Ordinal);
}
