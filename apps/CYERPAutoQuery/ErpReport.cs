namespace CYERPAutoQuery;

/// <summary>
/// One SMART ERP 報表 (明細表) driven through its condition window and 即時報表 (user, 2026-10-16/17:
/// COPR15, COPR16 and ACRR18 work the same way). Pure data.
/// </summary>
/// <param name="Tabs">The condition window's tabs, left to right.</param>
/// <param name="Strip">Measured tab centers; OCR is the fallback.</param>
internal sealed record ErpReport(string Code, string Title, string[] Tabs, TabStrip Strip)
{
    /// <summary>「銷貨單明細表[公司]」 (conditions) or 「銷貨單明細表(COPR15)[A]」 (report).</summary>
    public bool IsTitle(string title) => title.Contains(Title, StringComparison.Ordinal);
}
