using System.IO.Compression;
using System.Text;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// ERP's INVI02 export rebuilt with fake values (the real samples hold costs and stay out of
/// this public repository): same package layout (directory entries, no docProps), sheet
/// names, title in E1, column titles in row 3 (some with a leading space), data in row 4,
/// dates as serial numbers and a multi-line 商品描述.
/// </summary>
public class Invi02ExportTests
{
    internal static byte[] FakeExport(bool withDetail, string code = "T0001", int headerRows = 1)
    {
        var strings = new List<string>();
        int S(string v) { var i = strings.IndexOf(v); if (i >= 0) return i; strings.Add(v); return strings.Count - 1; }

        string Cell(string col, int row, string value, bool text) => text
            ? $"<c r=\"{col}{row}\" t=\"s\"><v>{S(value)}</v></c>"
            : $"<c r=\"{col}{row}\"><v>{value}</v></c>";

        string Sheet(IEnumerable<string> rows) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
            string.Concat(rows) + "</sheetData></worksheet>";

        var headTitles = new[] { ("A", "品號"), ("B", "品名"), ("C", "規格"), ("L", " 會計"), ("Z", "商品描述"), ("AM", "生效日期"), ("AQ", "品號屬性") };
        var head = new List<string>
        {
            $"<row r=\"1\">{Cell("E", 1, "品號資料建立作業", true)}</row>",
            "<row r=\"3\">" + string.Concat(headTitles.Select(t => Cell(t.Item1, 3, t.Item2, true))) + "</row>"
        };
        for (var i = 0; i < headerRows; i++)
        {
            var r = 4 + i;
            head.Add($"<row r=\"{r}\">{Cell("A", r, code, true)}{Cell("B", r, "假品名(10入)", true)}{Cell("C", r, "10入/測試", true)}" +
                     $"{Cell("L", r, "1", true)}{Cell("Z", r, "第一行\n第二行", true)}{Cell("AM", r, "46182", false)}{Cell("AQ", r, "P:採購件", true)}</row>");
        }
        var detail = new[]
        {
            $"<row r=\"1\">{Cell("E", 1, "品號資料建立作業", true)}</row>",
            $"<row r=\"3\">{Cell("A", 3, "庫別", true)}{Cell("B", 3, "庫別名稱", true)}{Cell("H", 3, "庫存數量", true)}{Cell("K", 3, "最近入庫日", true)}</row>",
            $"<row r=\"4\">{Cell("A", 4, "W01", true)}{Cell("B", 4, "測試倉", true)}{Cell("H", 4, "12", false)}{Cell("K", 4, "46303", false)}</row>",
            $"<row r=\"5\">{Cell("A", 5, "W02", true)}{Cell("B", 5, "第二倉", true)}{Cell("H", 5, "0", false)}</row>"
        };

        var sheets = withDetail ? new[] { ("單頭資料", Sheet(head)), ("單身資料", Sheet(detail)) } : new[] { ("Sheet1", Sheet(head)) };
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            zip.CreateEntry("xl/");
            zip.CreateEntry("xl/worksheets/");
            for (var i = 0; i < sheets.Length; i++) Put($"xl/worksheets/sheet{i + 1}.xml", sheets[i].Item2);
            Put("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                string.Concat(sheets.Select((s, i) => $"<sheet name=\"{s.Item1}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 3}\" />")) + "</sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 3}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\" />")) +
                "</Relationships>");
            Put("xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(strings.Select(v => $"<si><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(v)}</t></si>")) + "</sst>");
        }
        return ms.ToArray();
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Read(byte[] bytes) =>
        XlsxReader.ReadSheets(new MemoryStream(bytes));

    [Fact]
    public void ReadsBothSheetsByName()
    {
        var sheets = Read(FakeExport(withDetail: true));
        Assert.Equal(["單頭資料", "單身資料"], sheets.Select(s => s.Name));
    }

    [Fact]
    public void ParsesNamedHeaderFields()
    {
        var (fields, _) = Invi02Export.Parse(Read(FakeExport(withDetail: true)), "T0001");
        var map = fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("T0001", map["品號"]);
        Assert.Equal("假品名(10入)", map["品名"]);
        Assert.Equal("1", map["會計"]); // title had a leading space
        Assert.Equal("第一行\n第二行", map["商品描述"]);
        Assert.Equal("2026/06/09", map["生效日期"]);
        Assert.Equal("P:採購件", map["品號屬性"]);
        Assert.Equal("品號", fields[0].Name);
    }

    [Fact]
    public void ParsesWarehouseRows()
    {
        var (_, warehouses) = Invi02Export.Parse(Read(FakeExport(withDetail: true)), "t0001");
        Assert.Equal(2, warehouses.Count);
        Assert.Equal("W01", warehouses[0].Single(f => f.Name == "庫別").Value);
        Assert.Equal("2026/10/08", warehouses[0].Single(f => f.Name == "最近入庫日").Value);
        Assert.Equal("", warehouses[1].Single(f => f.Name == "最近入庫日").Value);
    }

    [Fact]
    public void ExportWithoutDetailSheetIsRejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Invi02Export.Parse(Read(FakeExport(withDetail: false)), "T0001"));
        Assert.Contains("單身資料", ex.Message);
    }

    [Fact]
    public void OtherItemCodeIsRejected() =>
        Assert.Throws<InvalidDataException>(() => Invi02Export.Parse(Read(FakeExport(withDetail: true, code: "T0002")), "T0001"));

    [Fact]
    public void MoreThanOneHeaderRowIsRejected() =>
        Assert.Throws<InvalidDataException>(() => Invi02Export.Parse(Read(FakeExport(withDetail: true, headerRows: 2)), "T0001"));

    [Theory]
    [InlineData("上次盤點日", "46182", "2026/06/09")]
    [InlineData("失效日期", "46309", "2026/10/14")]
    [InlineData("有效天數", "46182", "46182")]
    [InlineData("最近入庫日", "", "")]
    [InlineData("最近入庫日", "2026/10/08", "2026/10/08")]
    public void DateColumns(string title, string raw, string expected) =>
        Assert.Equal(expected, Invi02Export.FormatValue(title, raw));
}
