using System.Text;

namespace CYERPAutoQuery;

/// <summary>
/// One ERP input control read from INVI02. Delphi labels are not windows, so a field is
/// identified by its group (表頭 or the tab caption) and its row/column on screen; which
/// positions become named fields is decided after the first on-site readings.
/// </summary>
internal sealed record ErpFieldValue(string Group, int Row, int Column, string ControlClass, string Value)
{
    /// <summary>1-based position for people: 「第 2 列第 1 欄」.</summary>
    public string Position => $"第 {Row + 1} 列第 {Column + 1} 欄";
}

internal sealed record ItemQueryResult(
    string ItemCode,
    string ItemName,
    string Specification,
    IReadOnlyList<ErpFieldValue> Fields,
    TimeSpan Elapsed)
{
    public const string HeaderGroup = "表頭";

    /// <summary>False when ERP has no such 品號 (the query left the record empty).</summary>
    public bool Found { get; init; } = true;

    public static ItemQueryResult NotFound(string itemCode, TimeSpan elapsed) =>
        new(itemCode, string.Empty, string.Empty, [], elapsed) { Found = false };

    /// <summary>Tab-separated text (with a header line) for pasting into Excel.</summary>
    public string ToTsv()
    {
        var sb = new StringBuilder();
        sb.Append("頁籤\t位置\t類型\t內容\r\n");
        foreach (var f in Fields)
            sb.Append(Clean(f.Group)).Append('\t').Append(f.Position).Append('\t')
              .Append(Clean(f.ControlClass)).Append('\t').Append(Clean(f.Value)).Append("\r\n");
        return sb.ToString();
    }

    private static string Clean(string value) =>
        value.Replace("\t", " ").Replace("\r\n", " / ").Replace('\r', ' ').Replace('\n', ' ');
}
