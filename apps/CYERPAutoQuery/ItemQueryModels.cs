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
    private static List<IReadOnlyList<ErpField>> Table(List<string[]> rows, string firstTitle, string what)
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
