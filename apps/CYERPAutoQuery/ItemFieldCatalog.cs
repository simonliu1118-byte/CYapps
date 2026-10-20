namespace CYERPAutoQuery;

/// <summary>One field CYERPAutoQuery delivers: ERP's export column title and the label shown.</summary>
internal sealed record CatalogField(string Column, string Label);

/// <summary>
/// The fields a query delivers (user, 2026-10-16), in display order. The single source for the
/// local window and, later, the Web whitelist; ERP export columns not listed here are dropped
/// right after parsing (costs and the like never leave the parser).
/// </summary>
internal static class ItemFieldCatalog
{
    /// <summary>單頭資料 columns.</summary>
    public static readonly IReadOnlyList<CatalogField> Header =
    [
        new("品號", "品號"),
        new("品名", "品名"),
        new("規格", "規格"),
        new("單位", "單位"),
        new("庫存數量", "庫存數量"),
        // 商品分類 is ERP's 商品 code (料件分類三) with its name.
        new("商品", "商品分類"),
        new("料件分類三名稱", "商品分類名稱"),
        new("商品描述", "商品描述"),
        new("條碼編號", "條碼編號"),
        new("批號管理", "批號管理"),
        new("有效天數", "有效天數"),
        new("主供應商", "主供應商"),
        new("主供應商名稱", "主供應商名稱"),
        new("標準進價", "標準進價"),
        new("標準售價", "標準售價"),
        new("售價定價一", "售價定價一"),
        new("售價定價二", "售價定價二"),
        new("售價定價三", "售價定價三"),
        new("售價定價四", "售價定價四"),
        new("售價定價五", "售價定價五"),
        new("售價定價六", "售價定價六"),
    ];

    /// <summary>單身資料 (庫別) columns.</summary>
    public static readonly IReadOnlyList<CatalogField> Warehouse =
    [
        new("庫別", "庫別"),
        new("庫別名稱", "庫別名稱"),
        new("儲位", "儲位"),
        new("安全存量", "安全存量"),
        new("經濟批量", "經濟批量"),
        new("上次盤點日", "上次盤點日"),
        new("最近入庫日", "最近入庫日"),
        new("最近出庫日", "最近出庫日"),
    ];

    /// <summary>
    /// The columns of the 品名 search list (one row per matching item); the user picks a row
    /// and that item is then queried by 品號 for the full data.
    /// </summary>
    public static readonly IReadOnlyList<CatalogField> SearchList =
    [
        new("品號", "品號"),
        new("品名", "品名"),
        new("規格", "規格"),
        new("單位", "單位"),
        new("庫存數量", "庫存數量"),
        new("標準售價", "標準售價"),
    ];

    /// <summary>INVQ01 查各批庫存量 columns (user, 2026-10-16).</summary>
    public static readonly IReadOnlyList<CatalogField> Batch =
    [
        new("批號", "批號"),
        new("庫存數量", "庫存數量"),
        new("入庫日", "入庫日"),
        new("有效日期", "有效日期"),
    ];

    /// <summary>
    /// INVQ01 查歷史異動 columns. 單位成本 and 異動金額 are costs and are left out, the same as
    /// INVI02's cost columns.
    /// </summary>
    public static readonly IReadOnlyList<CatalogField> History =
    [
        new("日期", "日期"),
        new("單別-單號-序號", "單別-單號-序號"),
        new("庫別", "庫別"),
        new("庫別名稱", "庫別名稱"),
        new("入出別", "入出別"),
        new("異動數量", "異動數量"),
        new("備註", "備註"),
    ];

    /// <summary>
    /// The catalog's fields of one exported row, in catalog order and labelled. A missing
    /// column means the export did not include it (全選 failed or ERP changed); that stops
    /// the query instead of silently returning less.
    /// </summary>
    public static IReadOnlyList<ErpField> Select(IReadOnlyList<ErpField> row, IReadOnlyList<CatalogField> catalog, string what)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in row) byName.TryAdd(f.Name, f.Value);
        var missing = catalog.Where(c => !byName.ContainsKey(c.Column)).Select(c => c.Column).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"ERP 匯出的{what}缺少欄位：{string.Join("、", missing)}。");
        return catalog.Select(c => new ErpField(c.Label, byName[c.Column])).ToList();
    }
}
