using System.IO.Compression;
using System.Text;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// COPI01 customer query: rules and the export, rebuilt with fake values in the column order of
/// the user's samples (2026-10-16): sheet 單頭資料, titles in row 3, 郵遞區號 four times
/// (登記、發票、送貨、文件).
/// </summary>
public class CustomerQueryTests
{
    private static readonly string[] Titles =
    [
        "客戶代號", "客戶簡稱", "客戶全名", "負責人", "版次", "連絡人", "TEL_NO", "FAX NO", "統一編號", "行動電話",
        "部門別", "業務人員", "郵遞區號", "登記地址(一)", "郵遞區號", "發票地址(一)", "郵遞區號", "送貨地址(一)",
        "郵遞區號", "文件地址(一)", "付款條件", "付款條件名稱", "通路別", "通路簡稱", "型態別", "型態簡稱",
        "發票聯數", "課稅別", "客戶描述",
    ];

    private static string[] Values(string code, string shortName, string fullName) =>
    [
        code, shortName, fullName, "假負責人", "0000", "假連絡人", "(07)000-0000", "", "12345678", "0900000000",
        "SA_X", "CY0000", "800001", "假登記地址", "800002", "假發票地址", "800003", "假送貨地址",
        "800004", "假文件地址", "R09", "月結30天", "KHH", "高雄市", "AC1", "中醫診所",
        "1:二聯式", "9:不計稅", "第一行\n第二行",
    ];

    private static string Col(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    internal static byte[] FakeCopi01(params string[][] rows)
    {
        string Cell(int col, int row, string value) =>
            $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(value)}</t></is></c>";
        var xml = new StringBuilder($"<row r=\"1\">{Cell(4, 1, "客戶資料建立作業")}</row><row r=\"3\">");
        for (var i = 0; i < Titles.Length; i++) xml.Append(Cell(i, 3, Titles[i]));
        xml.Append("</row>");
        for (var r = 0; r < rows.Length; r++)
        {
            xml.Append($"<row r=\"{4 + r}\">");
            for (var i = 0; i < rows[r].Length; i++) xml.Append(Cell(i, 4 + r, rows[r][i]));
            xml.Append("</row>");
        }
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Put("xl/worksheets/sheet1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + xml + "</sheetData></worksheet>");
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"單頭資料\" sheetId=\"1\" r:id=\"rId3\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
        }
        return ms.ToArray();
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Read(byte[] bytes) => XlsxReader.ReadSheets(new MemoryStream(bytes));

    [Fact]
    public void ParsesOneCustomerInCatalogOrder()
    {
        var fields = CustomerExport.Parse(Read(FakeCopi01(Values("AC0001", "假診所", "假診所全名"))), "AC0001");
        Assert.Equal(CustomerFieldCatalog.Fields.Select(f => f.Label), fields.Select(f => f.Name));
        var map = fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("假診所", map["客戶簡稱"]);
        Assert.Equal("(07)000-0000", map["TEL"]);
        // Each address takes its own 郵遞區號: 登記 the 1st, 送貨 the 3rd.
        Assert.Equal("800001 假登記地址", map["登記地址"]);
        Assert.Equal("800003 假送貨地址", map["送貨地址"]);
        Assert.Equal("R09 月結30天", map["付款條件"]);
        Assert.Equal("KHH 高雄市", map["通路別"]);
        Assert.Equal("AC1 中醫診所", map["型態別"]);
        Assert.Equal("第一行\n第二行", map["客戶描述"]);
        Assert.Equal("", map["FAX"]);
    }

    [Fact]
    public void RefusesAnotherCustomerOrSeveral()
    {
        Assert.Throws<InvalidDataException>(() => CustomerExport.Parse(Read(FakeCopi01(Values("AC0002", "A", "B"))), "AC0001"));
        Assert.Throws<InvalidDataException>(() => CustomerExport.Parse(Read(FakeCopi01(Values("ac0001", "A", "B"))), "AC0001"));
        Assert.Throws<InvalidDataException>(() => CustomerExport.Parse(
            Read(FakeCopi01(Values("AC0001", "A", "B"), Values("AC0002", "C", "D"))), "AC0001"));
    }

    [Fact]
    public void ListAcceptsShortOrFullNameMatch()
    {
        var list = CustomerExport.ParseList(Read(FakeCopi01(
            Values("AC0001", "佳德中醫", "佳德中醫診所"),
            Values("AC0002", "某診所", "佳德聯合診所"))), "佳德");
        Assert.Equal(2, list.Count);
        Assert.Equal(CustomerFieldCatalog.SearchList.Select(f => f.Label), list[0].Select(f => f.Name));
        Assert.Equal("AC0002", ItemSearchResult.Value(list[1], "客戶代號"));
    }

    [Fact]
    public void ListRefusedWhenConditionWasIgnored()
    {
        Assert.Throws<InvalidDataException>(() => CustomerExport.ParseList(Read(FakeCopi01(
            Values("AC0001", "佳德中醫", "佳德中醫診所"),
            Values("AC0003", "別家", "別家診所"))), "佳德"));
    }

    [Fact]
    public void ConditionSearchesShortAndFullNameWithOr()
    {
        var condition = CustomerRules.NameCondition("佳德");
        Assert.Equal("(COPMA.MA002 like N'%佳德%')\r\n  OR (COPMA.MA003 like N'%佳德%')", condition);
        // ERP may lay the lines out differently; only the words count.
        Assert.True(ItemNameRules.SameCondition("(COPMA.MA002 like N'%佳德%')\n OR (COPMA.MA003 like N'%佳德%')", condition));
    }

    [Fact]
    public void CustomerCodeIsLettersAndDigits()
    {
        Assert.True(ItemCodeRules.TryNormalize(" AC0007 ", out var code, out _, CustomerRules.CodeName));
        Assert.Equal("AC0007", code);
        Assert.False(ItemCodeRules.TryNormalize("AC-0007", out _, out var error, CustomerRules.CodeName));
        Assert.Equal("客戶代號只能輸入英文字母與數字。", error);
        Assert.False(ItemCodeRules.TryNormalize("", out _, out error, CustomerRules.CodeName));
        Assert.Equal("請輸入客戶代號。", error);
    }

    [Fact]
    public void Copi01FormIsRecognisedByTitle()
    {
        Assert.True(ErpForm.Copi01.IsTitle("客戶資料建立作業(COPI01)[假公司]"));
        Assert.False(ErpForm.Copi01.IsTitle("品號資料建立作業(INVI02)[假公司]"));
        Assert.True(ErpForm.Invi02.IsTitle("品號資料建立作業(INVI02)[假公司]"));
        Assert.NotNull(ErpForm.Copi01.Tabs.PointFor(new System.Drawing.Rectangle(0, 0, 1800, 600), "基本資料"));
    }
}
