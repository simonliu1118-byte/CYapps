namespace CYERPAutoQuery;

/// <summary>
/// One SMART ERP 建立作業 the query drives the same way (user, 2026-10-16: 客戶資料 in COPI01
/// works like 品號 in INVI02): F6 → 一般選項 code = value, or 進階設定 name condition → 確定 →
/// a detail tab → 轉出至 Excel (or 資料瀏覽 → 轉出全部 for a name search). Pure data.
/// </summary>
/// <param name="ExportTab">The tab shown before 轉出至 Excel (資料瀏覽 would export without detail).</param>
/// <param name="HeaderRows">Input rows above the tabs: INVI02 has 品號／品名／規格, COPI01／PURI01 代號＋簡稱／全名.</param>
/// <param name="CodeName">What the 一般選項 code is called, for messages.</param>
/// <param name="HasDetailSheet">The export also has a 單身資料 sheet whose fields are all selected.</param>
internal sealed record ErpForm(
    string Code,
    string Title,
    string FormClass,
    string ExportTab,
    TabStrip Tabs,
    int HeaderRows,
    string CodeName,
    bool HasDetailSheet)
{
    public const string BrowseTab = "資料瀏覽";

    public static readonly ErpForm Invi02 = new(
        Invi02Layout.FormCode, Invi02Layout.FormTitle, Invi02Layout.FormClass, Invi02Layout.ExportTab,
        Invi02Layout.Invi02Tabs, HeaderRows: 3, CodeName: "品號", HasDetailSheet: true);

    // COPI01 tabs (user's screenshot 2026-10-16): fixed width, centers about 99 px apart, the first
    // about 72 px right of the page control; OCR is the fallback when this is off.
    public static readonly ErpForm Copi01 = new(
        "COPI01", "客戶資料建立作業", "TfrmCopi01", "基本資料",
        new TabStrip(["基本資料", "基本資料(二)", "地址資料", "交易資料(一)", "交易資料(二)", "客戶描述", BrowseTab],
            Origin: 72 - 99 / 2.0, Pitch: 99),
        HeaderRows: 2, CodeName: "客戶代號", HasDetailSheet: false);

    // PURI01 (user's screenshots 2026-10-17): the same tab style as COPI01, so the same geometry;
    // header 廠商代號＋簡稱, then 公司全名.
    public static readonly ErpForm Puri01 = new(
        "PURI01", "供應廠商資料建立作業", "TfrmPuri01", "基本資料",
        new TabStrip(["基本資料", "基本資料(二)", "交易資料", "地址資料", "廠商描述", BrowseTab],
            Origin: 72 - 99 / 2.0, Pitch: 99),
        HeaderRows: 2, CodeName: "廠商代號", HasDetailSheet: false);

    /// <summary>The form's own window: 「客戶資料建立作業(COPI01)[公司]」.</summary>
    public bool IsTitle(string title) =>
        title.Contains(Title, StringComparison.Ordinal) && title.Contains(Code, StringComparison.OrdinalIgnoreCase);
}
