using System.Globalization;

namespace CYERPAutoQuery;

/// <summary>One named value from ERP's INVI02 export (the column title is the name).</summary>
internal sealed record ErpField(string Name, string Value);

/// <summary>
/// One 品號: the <see cref="ItemFieldCatalog"/> fields of ERP's INVI02 export, 單頭 (one
/// row) and 單身 (one row per 庫別), labelled and in catalog order.
/// </summary>
internal sealed record ItemQueryResult(
    string ItemCode,
    IReadOnlyList<ErpField> Fields,
    IReadOnlyList<IReadOnlyList<ErpField>> Warehouses,
    TimeSpan Elapsed)
{
    /// <summary>False when ERP has no such 品號 (the query left the record empty).</summary>
    public bool Found { get; init; } = true;

    /// <summary>INVQ01 查各批庫存量 rows; empty when the item is not batch managed.</summary>
    public IReadOnlyList<IReadOnlyList<ErpField>> Batches { get; init; } = [];

    /// <summary>True when the item is batch managed, so <see cref="Batches"/> was looked up.</summary>
    public bool BatchManaged { get; init; }

    /// <summary>The 查歷史異動 range asked for, or null when history was not asked for.</summary>
    public HistoryRange? HistoryRange { get; init; }

    /// <summary>INVQ01 查歷史異動 rows in ERP's order (oldest first).</summary>
    public IReadOnlyList<IReadOnlyList<ErpField>> History { get; init; } = [];

    public string ItemName => Value("品名");
    public string Specification => Value("規格");

    public string Value(string name) => Fields.FirstOrDefault(f => f.Name == name)?.Value ?? string.Empty;

    public static ItemQueryResult NotFound(string itemCode, TimeSpan elapsed) =>
        new(itemCode, [], [], elapsed) { Found = false };

}

/// <summary>
/// Reads ERP's INVI02 「轉出至 Excel」 workbook (user's samples 2026-10-16): row 1 title,
/// row 3 column titles starting with 品號, data from row 4. Exported from a tab other than
/// 資料瀏覽 it has two sheets, 單頭資料 and 單身資料 (庫別); from 資料瀏覽 only the first.
/// </summary>
internal static class Invi02Export
{
    public const string HeaderSheet = "單頭資料";
    public const string DetailSheet = "單身資料";

    public static (IReadOnlyList<ErpField> Fields, IReadOnlyList<IReadOnlyList<ErpField>> Warehouses) Parse(
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string itemCode)
    {
        if (sheets.Count == 0) throw new InvalidDataException("ERP 匯出的 Excel 沒有工作表。");
        var header = sheets.FirstOrDefault(s => s.Name == HeaderSheet);
        if (header.Rows is null) header = sheets[0];

        var headerRows = Table(header.Rows, "品號", "單頭");
        if (headerRows.Count != 1)
            throw new InvalidDataException($"ERP 匯出的單頭資料應為 1 筆，實際 {headerRows.Count} 筆。");
        var fields = headerRows[0];
        var code = fields.FirstOrDefault(f => f.Name == "品號")?.Value ?? string.Empty;
        if (!ItemCodeRules.SameCode(code, itemCode))
            throw new InvalidDataException($"ERP 匯出的品號「{code}」不是查詢的「{itemCode}」。");

        var detail = sheets.FirstOrDefault(s => s.Name == DetailSheet);
        if (detail.Rows is null)
            throw new InvalidDataException("ERP 匯出的 Excel 沒有「單身資料」（庫別）。請確認匯出前停在「基本資料1」頁籤。");
        var warehouses = Table(detail.Rows, "庫別", "單身")
            .Select(row => ItemFieldCatalog.Select(row, ItemFieldCatalog.Warehouse, "單身資料"))
            .ToList();
        return (ItemFieldCatalog.Select(fields, ItemFieldCatalog.Header, "單頭資料"), warehouses);
    }

    /// <summary>Rows under the title row that starts with <paramref name="firstTitle"/>, as named fields.</summary>
    internal static List<IReadOnlyList<ErpField>> Table(List<string[]> rows, string firstTitle, string what)
    {
        var titleIndex = rows.FindIndex(r => r.Length > 0 && r[0].Trim() == firstTitle);
        if (titleIndex < 0) throw new InvalidDataException($"ERP 匯出的{what}資料找不到「{firstTitle}」欄位標題。");
        var titles = rows[titleIndex].Select(t => t.Trim()).ToArray();

        var result = new List<IReadOnlyList<ErpField>>();
        foreach (var row in rows.Skip(titleIndex + 1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var fields = new List<ErpField>(titles.Length);
            for (var i = 0; i < titles.Length; i++)
            {
                if (titles[i].Length == 0) continue;
                var raw = i < row.Length ? row[i] : string.Empty;
                fields.Add(new ErpField(titles[i], FormatValue(titles[i], raw)));
            }
            result.Add(fields);
        }
        return result;
    }

    /// <summary>
    /// ERP writes dates (上次盤點日, 生效日期 …) as Excel serial numbers; a column whose title
    /// ends with 日 or 日期 and holds a whole number in the date range is shown as yyyy/MM/dd.
    /// </summary>
    internal static string FormatValue(string title, string raw)
    {
        if ((title.EndsWith('日') || title.EndsWith("日期", StringComparison.Ordinal)) &&
            int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) &&
            serial is > 1 and < 2958466)
            return DateTime.FromOADate(serial).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        return raw;
    }
}

/// <summary>
/// Reads ERP's INVQ01 「轉出到Excel」 workbook (user's samples 2026-10-16, INVQ01_5 查各批庫存量
/// and INVQ01_4 查歷史異動): one sheet 查詢資料, row 1 the query name, row 3 column titles
/// (some padded with spaces). Columns from 品號 on describe the item and are filled on the
/// first data row only; the query's own columns start at a known title (批號, 日期) and fill
/// one row each. The item part repeats titles of the detail part (庫存數量), so the detail
/// part is cut out from its first title before the columns are named.
/// </summary>
internal static class Invq01Export
{
    public const string BatchFirstColumn = "批號";
    public const string HistoryFirstColumn = "日期";

    public static IReadOnlyList<IReadOnlyList<ErpField>> ParseBatches(
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string itemCode) =>
        Parse(sheets, itemCode, BatchFirstColumn, ItemFieldCatalog.Batch, "查各批庫存量");

    public static IReadOnlyList<IReadOnlyList<ErpField>> ParseHistory(
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string itemCode) =>
        Parse(sheets, itemCode, HistoryFirstColumn, ItemFieldCatalog.History, "查歷史異動")
            .Select(row => (IReadOnlyList<ErpField>)row.Select(f => f.Name == "單別-單號-序號" ? f with { Value = CompactDocumentNo(f.Value) } : f).ToList())
            .ToList();

    private static IReadOnlyList<IReadOnlyList<ErpField>> Parse(
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string itemCode, string firstDetail,
        IReadOnlyList<CatalogField> catalog, string what)
    {
        if (sheets.Count == 0) throw new InvalidDataException($"ERP 匯出的{what} Excel 沒有工作表。");
        var rows = sheets[0].Rows;
        var titleIndex = rows.FindIndex(r => r.Length > 0 && r[0].Trim() == "品號");
        if (titleIndex < 0) throw new InvalidDataException($"ERP 匯出的{what}找不到「品號」欄位標題。");
        var titles = rows[titleIndex].Select(t => t.Trim()).ToArray();
        var start = Array.IndexOf(titles, firstDetail);
        if (start < 0) throw new InvalidDataException($"ERP 匯出的{what}找不到「{firstDetail}」欄位標題。");

        var first = titleIndex + 1 < rows.Count ? rows[titleIndex + 1] : [];
        var code = first.Length > 0 ? first[0] : string.Empty;
        if (!ItemCodeRules.SameCode(code, itemCode))
            throw new InvalidDataException($"ERP 匯出的{what}品號「{code.Trim()}」不是查詢的「{itemCode}」。");

        var detailTitles = titles.Skip(start).ToArray();
        var detailRows = rows.Skip(titleIndex + 1)
            .Select(r => Enumerable.Range(start, detailTitles.Length).Select(i => i < r.Length ? r[i] : string.Empty).ToArray())
            .ToList();
        var table = Invi02Export.Table([detailTitles, .. detailRows], firstDetail, what);
        return table
            .Select(row => (IReadOnlyList<ErpField>)ItemFieldCatalog.Select(
                row.Select(f => f with { Value = f.Value.Trim() }).ToList(), catalog, what))
            .ToList();
    }

    /// <summary>「341A      -20210622001-0002」 → 「341A-20210622001-0002」 (ERP pads the 單別).</summary>
    internal static string CompactDocumentNo(string value) =>
        string.Join("-", value.Split('-').Select(p => p.Trim()));
}
