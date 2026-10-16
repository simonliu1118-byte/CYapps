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
    // Column letters as in ERP's real export; values are fake.
    private static readonly (string Col, string Title, string Value, bool Text)[] HeadColumns =
    [
        ("A", "品號", "", true), ("B", "品名", "假品名(10入)", true), ("C", "規格", "10入/測試", true),
        ("E", "單位", "包", true), ("F", "庫存數量", "12", false), ("H", "單位成本", "9.8765", false),
        ("I", "標準售價", "100", false), ("J", "售價定價一", "90", false), ("L", " 會計", "1", true),
        ("P", " 商品", "X01", true), ("Q", "料件分類三名稱", "測試分類", true), ("Z", "商品描述", "第一行\n第二行", true),
        ("AH", "條碼編號", "0000000000000", true), ("AK", "批號管理", "N:不需要", true), ("AL", "有效天數", "0", false),
        ("AM", "生效日期", "46182", false), ("BG", "主供應商", "S001", true), ("BH", "主供應商名稱", "假供應商", true),
        ("BW", "標準進價", "50", false), ("CE", "售價定價二", "85", false), ("CF", "售價定價三", "80", false),
        ("CG", "售價定價四", "0", false), ("CH", "售價定價五", "0", false), ("CI", "售價定價六", "0", false),
    ];

    internal static byte[] FakeExport(bool withDetail, string code = "T0001", int headerRows = 1, string? dropHeaderColumn = null)
    {
        var strings = new List<string>();
        int S(string v) { var i = strings.IndexOf(v); if (i >= 0) return i; strings.Add(v); return strings.Count - 1; }

        string Cell(string col, int row, string value, bool text) => text
            ? $"<c r=\"{col}{row}\" t=\"s\"><v>{S(value)}</v></c>"
            : $"<c r=\"{col}{row}\"><v>{value}</v></c>";

        string Sheet(IEnumerable<string> rows) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
            string.Concat(rows) + "</sheetData></worksheet>";

        var columns = HeadColumns.Where(c => c.Title.Trim() != dropHeaderColumn).ToArray();
        var head = new List<string>
        {
            $"<row r=\"1\">{Cell("E", 1, "品號資料建立作業", true)}</row>",
            "<row r=\"3\">" + string.Concat(columns.Select(c => Cell(c.Col, 3, c.Title, true))) + "</row>"
        };
        for (var i = 0; i < headerRows; i++)
        {
            var r = 4 + i;
            head.Add($"<row r=\"{r}\">" + string.Concat(columns.Select(c => Cell(c.Col, r, c.Title == "品號" ? code : c.Value, c.Text))) + "</row>");
        }
        string Wh(int r, string id, string name, string qty, string lastIn) =>
            $"<row r=\"{r}\">{Cell("A", r, id, true)}{Cell("B", r, name, true)}{Cell("C", r, "1:存貨倉", true)}{Cell("D", r, "A-01", true)}" +
            $"{Cell("E", r, "5", false)}{Cell("G", r, "10", false)}{Cell("H", r, qty, false)}{Cell("I", r, "123.45", false)}" +
            $"{Cell("J", r, "46182", false)}" + (lastIn.Length > 0 ? Cell("K", r, lastIn, false) : "") + $"{Cell("L", r, "46309", false)}</row>";
        var detail = new[]
        {
            $"<row r=\"1\">{Cell("E", 1, "品號資料建立作業", true)}</row>",
            $"<row r=\"3\">{Cell("A", 3, "庫別", true)}{Cell("B", 3, "庫別名稱", true)}{Cell("C", 3, "庫性", true)}{Cell("D", 3, "儲位", true)}" +
            $"{Cell("E", 3, "安全存量", true)}{Cell("G", 3, "經濟批量", true)}{Cell("H", 3, "庫存數量", true)}{Cell("I", 3, "庫存金額", true)}" +
            $"{Cell("J", 3, "上次盤點日", true)}{Cell("K", 3, "最近入庫日", true)}{Cell("L", 3, "最近出庫日", true)}</row>",
            Wh(4, "W01", "測試倉", "12", "46303"),
            Wh(5, "W02", "第二倉", "0", "")
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
    public void KeepsOnlyCatalogFieldsInCatalogOrder()
    {
        var (fields, _) = Invi02Export.Parse(Read(FakeExport(withDetail: true)), "T0001");
        Assert.Equal(ItemFieldCatalog.Header.Select(c => c.Label), fields.Select(f => f.Name));
        var map = fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("T0001", map["品號"]);
        Assert.Equal("X01", map["商品分類"]);       // ERP column " 商品" (leading space)
        Assert.Equal("測試分類", map["商品分類名稱"]);
        Assert.Equal("第一行\n第二行", map["商品描述"]);
        Assert.Equal("85", map["售價定價二"]);
        Assert.False(map.ContainsKey("單位成本"));   // not in the catalog: dropped
        Assert.False(map.ContainsKey("生效日期"));
    }

    [Fact]
    public void KeepsOnlyCatalogWarehouseFields()
    {
        var (_, warehouses) = Invi02Export.Parse(Read(FakeExport(withDetail: true)), "T0001");
        Assert.Equal(2, warehouses.Count);
        Assert.Equal(ItemFieldCatalog.Warehouse.Select(c => c.Label), warehouses[0].Select(f => f.Name));
        Assert.Equal("W01", warehouses[0].Single(f => f.Name == "庫別").Value);
        Assert.Equal("A-01", warehouses[0].Single(f => f.Name == "儲位").Value);
        Assert.Equal("2026/06/09", warehouses[0].Single(f => f.Name == "上次盤點日").Value);
        Assert.Equal("2026/10/08", warehouses[0].Single(f => f.Name == "最近入庫日").Value);
        Assert.Equal("", warehouses[1].Single(f => f.Name == "最近入庫日").Value);
        Assert.DoesNotContain(warehouses[0], f => f.Name == "庫存金額");
    }

    [Fact]
    public void MissingCatalogColumnStopsTheQuery()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Invi02Export.Parse(Read(FakeExport(withDetail: true, dropHeaderColumn: "條碼編號")), "T0001"));
        Assert.Contains("條碼編號", ex.Message);
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
    public void ItemCodeCaseMustMatch() =>
        Assert.Throws<InvalidDataException>(() => Invi02Export.Parse(Read(FakeExport(withDetail: true)), "t0001"));

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
