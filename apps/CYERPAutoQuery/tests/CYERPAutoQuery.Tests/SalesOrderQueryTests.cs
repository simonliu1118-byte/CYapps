using System.IO.Compression;
using System.Text;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// COPR15 銷貨單明細表 export, rebuilt with fake values in the column order of the user's sample
/// (2026-10-16): titles in row 1 with spaces, one row per line repeating the 單頭, a 小計 row last.
/// </summary>
public class SalesOrderQueryTests
{
    private static readonly string[] Titles =
    [
        "單據日期", "銷貨單號", "客戶代號", "客戶簡稱", "通路", "型態", "部  門", "業務員", "幣別", "匯率",
        "交易條件", "課稅別", "件數", "發票聯數", "統一編號", "發票號碼", "發票日期", "付款條件", "付款名稱", "總數量",
        "備    註", "原幣銷貨金額", "原幣銷貨稅額", "本幣銷貨金額", "本幣銷貨稅額", "原幣合計", "本幣合計", "預收訂單單號", "預收待抵單號", "沖抵金額",
        "沖抵稅額", "品    號", "品    名", "規    格", "庫別代號", "庫別名稱", "銷貨數量", "折扣率 %", "包裝數量", "類型",
        "贈/備品量", "贈/備品包裝量", "單位", "包裝單位", "單   價", "金額", "原幣未稅金額", "原幣稅額", "本幣未稅金額", "本幣稅額",
        "訂單單號", "批    號", "結帳", "結帳單號", "備註", "專案代號", "客戶單號", "客戶品號",
    ];

    private static string[] Line(string date, string no, string customer, string item, string qty, string price, string amount,
        string total = "2450", string orderNote = "假單頭備註", string lineNote = "") =>
    [
        date, no, customer, "假診所", "高雄市", "中醫診所", "假部門", "假業務", "NTD", "1",
        "一般", "不計稅", "0", "三聯式發票", "12345678", "", "", "R09", "月結30天", "4",
        orderNote, total, "0", total, "0", total, total, "-", "-", "0",
        "0", item, "假品名" + item, "假規格", "KH01", "假倉庫", qty, "100", "0", "贈品量",
        "0", "0", "包", "", price, amount, amount, "0", amount, "0",
        "--", "", "N", "--", lineNote, "", "", "",
    ];

    private static string[] Subtotal()
    {
        var row = new string[Titles.Length];
        Array.Fill(row, "");
        row[4] = "小計:";
        row[8] = "2張";
        return row;
    }

    private static string Col(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Export(params string[][] rows) => ExportWith(Titles, rows);

    internal static IReadOnlyList<(string Name, List<string[]> Rows)> ExportWith(string[] titles, params string[][] rows)
    {
        string Cell(int col, int row, string value) =>
            $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(value)}</t></is></c>";
        var xml = new StringBuilder("<row r=\"1\">");
        for (var i = 0; i < titles.Length; i++) xml.Append(Cell(i, 1, titles[i]));
        xml.Append("</row>");
        for (var r = 0; r < rows.Length; r++)
        {
            xml.Append($"<row r=\"{2 + r}\">");
            for (var i = 0; i < rows[r].Length; i++) xml.Append(Cell(i, 2 + r, rows[r][i]));
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
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"COPR15202610160000\" sheetId=\"1\" r:id=\"rId1\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
        }
        ms.Position = 0;
        return XlsxReader.ReadSheets(ms);
    }

    private static readonly SalesQuery Query = new("AC0001", new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 16));

    [Fact]
    public void GroupsLinesIntoOrdersNewestFirst()
    {
        var orders = SalesExport.Parse(Export(
            Line("2026/09/01", "901A-20260901001", "AC0001", "X00004", "1", "500", "500", total: "500"),
            Line("2026/10/14", "901A-20261014002", "AC0001", "X00001", "1", "2000", "2000", lineNote: "假單身備註"),
            Line("2026/10/14", "901A-20261014002", "AC0001", "X00002", "1", "150", "150"),
            Line("2026/10/14", "901A-20261014002", "AC0001", "X00003", "2", "150", "300"),
            Subtotal()), SalesReport.Sales, Query);

        Assert.Equal(["901A-20261014002", "901A-20260901001"], orders.Select(o => o.OrderNo));
        var order = orders[0];
        Assert.Equal(new DateOnly(2026, 10, 14), order.Date);
        Assert.Equal(2450m, order.Total);
        Assert.Equal(4m, order.TotalQuantity);
        Assert.Equal("假單頭備註", order.Note);
        Assert.Equal("假部門", order.Department);
        Assert.Equal("月結30天", order.PaymentTerm);
        Assert.Equal([1, 2, 3], order.Lines.Select(l => l.Seq));
        Assert.Equal(["X00001", "X00002", "X00003"], order.Lines.Select(l => l.ItemCode));
        Assert.Equal(2m, order.Lines[2].Quantity);
        Assert.Equal(150m, order.Lines[2].UnitPrice);
        Assert.Equal(300m, order.Lines[2].Amount);
        Assert.Equal("KH01 假倉庫", order.Lines[0].Warehouse);
        Assert.Equal("假單身備註", order.Lines[0].Note);
        Assert.Equal("", order.Lines[1].Note);
    }

    [Fact]
    public void OnlySubtotalMeansNoOrders()
    {
        Assert.Empty(SalesExport.Parse(Export(Subtotal()), SalesReport.Sales, Query));
        Assert.Empty(SalesExport.Parse(Export(), SalesReport.Sales, Query));
    }

    [Fact]
    public void RefusesAnotherCustomerOrDateOutsideRange()
    {
        Assert.Throws<InvalidDataException>(() => SalesExport.Parse(Export(
            Line("2026/10/14", "901A-20261014002", "AC0002", "X00001", "1", "2000", "2000")), SalesReport.Sales, Query));
        Assert.Throws<InvalidDataException>(() => SalesExport.Parse(Export(
            Line("2025/12/31", "901A-20251231001", "AC0001", "X00001", "1", "2000", "2000")), SalesReport.Sales, Query));
    }

    [Fact]
    public void ResolveChecksCodeAndDates()
    {
        var today = new DateOnly(2026, 10, 16);
        Assert.Equal("AC0001", SalesQuery.Resolve(" AC0001 ", null, null, today).CustomerCode);
        Assert.Throws<ArgumentException>(() => SalesQuery.Resolve("AC-1", null, null, today));
        Assert.Throws<ArgumentException>(() => SalesQuery.Resolve("AC0001", today, today.AddDays(-1), today));
        Assert.Throws<ArgumentException>(() => SalesQuery.Resolve("AC0001", today.AddDays(1), null, today));
    }

    [Fact]
    public void ReportsAreRecognisedByTitle()
    {
        Assert.True(SalesReport.Sales.IsTitle("銷貨單明細表[假公司]"));
        Assert.True(SalesReport.Sales.IsTitle("銷貨單明細表(COPR15)[A]"));
        Assert.False(SalesReport.Sales.IsTitle("客戶資料建立作業(COPI01)[假公司]"));
        Assert.True(SalesReport.Returns.IsTitle("銷退單明細表(COPR16)[A]"));
    }

    [Fact]
    public void ErpExportMessages()
    {
        const string failed = "匯出失敗Cannot create file \"C:\\My Documents\\COPR1520261016000009202610160001.xlsx\". 系統找不到指定的路徑。";
        Assert.True(SalesReportLayout.IsMissingFolder(failed));
        Assert.True(SalesReportLayout.IsExportFailed(failed));
        Assert.Equal(@"C:\My Documents\COPR1520261016000009202610160001.xlsx", SalesReportLayout.PathIn(failed));
        const string done = "匯出成功: C:\\My Documents\\ COPR1520261016000009202610160001.xlsx";
        Assert.False(SalesReportLayout.IsExportFailed(done));
        Assert.True(SalesReportLayout.IsReportFile("COPR1520261016000009202610160001.xlsx", SalesReport.Sales.Form));
        Assert.False(SalesReportLayout.IsReportFile("COPR1620261016000009202610160001.xlsx", SalesReport.Sales.Form));
    }

    // COPR16 銷退單明細表 (user's sample 2026-10-17): other column names, no 金額 (原幣未稅金額 instead).
    private static readonly string[] ReturnTitles =
    [
        "單據日期", "銷退單號", "客戶代號", "客戶簡稱", "通路", "型態", "部門", "業務員", "幣別", "彙率",
        "課稅別", "交易條件", "發票聯數", "統一編號", "發票號碼", "總數量", "備注", "總包裝數量", "原幣銷退金額", "原幣銷退稅額",
        "本幣銷退金額", "本幣銷退稅額", "原幣合計", "本幣合計", "品號", "品名", "規格", "庫別代號", "庫別名稱", "銷退數量",
        "單位", "包裝數量", "包裝單位", "銷退贈品量", "贈品包裝量", "單價", "原幣未稅金額", "原幣稅額", "本幣未稅金額", "本幣稅額",
        "原銷貨單", "原訂貨單", "結帳", "結帳單號", "批號", "備注", "專案代號", "專案說明", "銷退原因代號", "原因說明",
    ];

    private static string[] ReturnLine(string date, string no, string item, string qty, string price, string amount, string source, string reasonCode, string reason) =>
    [
        date, no, "AC0001", "假診所", "高雄市", "中醫診所", "假部門", "假業務", "NTD", "1",
        "不計稅", "一般", "免用統一發票", "12345678", "", "3", "", "0", "1500", "0",
        "1500", "0", "1500", "1500", item, "假品名", "假規格", "KH03", "假倉庫", qty,
        "袋", "0", "", "0", "0", price, amount, "0", amount, "0",
        source, "--", "Y", "--", "LOT0001", "", "", "", reasonCode, reason,
    ];

    [Fact]
    public void ParsesReturns()
    {
        var orders = SalesExport.Parse(ExportWith(ReturnTitles,
            ReturnLine("2022/08/24", "902A-20220824001", "X00005", "2", "500", "1000", "--", "004", "換貨"),
            ReturnLine("2022/08/24", "902A-20220824001", "X00006", "1", "500", "500", "901A-20220801001-0002", "004", "換貨")),
            SalesReport.Returns, new SalesQuery("AC0001", null, null));
        var order = Assert.Single(orders);
        Assert.Equal("銷退", order.Kind);
        Assert.Equal(1500m, order.Total);
        Assert.Equal("004 換貨", order.Reason);
        Assert.Equal(1000m, order.Lines[0].Amount);
        Assert.Equal(2m, order.Lines[0].Quantity);
        Assert.Equal("", order.Lines[0].SourceOrder);
        Assert.Equal("901A-20220801001-0002", order.Lines[1].SourceOrder);
        Assert.Equal("LOT0001", order.Lines[0].Batch);
    }

    [Fact]
    public void MissingPriceColumnHintsPrintPrice()
    {
        var titles = Titles.Select(t => t == "單   價" ? "其他" : t).ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => SalesExport.Parse(ExportWith(titles,
            Line("2026/10/14", "901A-20261014002", "AC0001", "X00001", "1", "2000", "2000")), SalesReport.Sales, Query));
        Assert.Contains("列印單價金額", ex.Message);
    }

    [Fact]
    public void OneOrderByNumber()
    {
        var query = SalesQuery.ForOrder(" 901a-20261014002 ".Replace("901a", "231a"));
        Assert.Equal("231A-20261014002", query.OrderNo);
        Assert.Equal("231A20261014002", query.OrderKeys);
        Assert.Same(SalesReport.Sales, SalesReport.ForOrder("232A-20260804003"));
        Assert.Same(SalesReport.Returns, SalesReport.ForOrder("241A-20260910001"));
        Assert.Null(SalesReport.ForOrder("631A-20260422002"));
        Assert.Throws<ArgumentException>(() => SalesQuery.ForOrder("631A-20260422002"));
        Assert.Throws<ArgumentException>(() => SalesQuery.ForOrder("232A20260804003"));
        Assert.Equal("選擇銷貨單號", SalesReport.Sales.OrderGroup);

        var order = SalesQuery.ForOrder("901A-20261014002".Replace("901A", "232A"));
        var parsed = SalesExport.Parse(Export(
            Line("2026/10/14", "232A-20261014002", "AC0001", "X00001", "1", "2000", "2000"),
            Subtotal()), SalesReport.Sales, order);
        Assert.Single(parsed);
        Assert.Throws<InvalidDataException>(() => SalesExport.Parse(Export(
            Line("2026/10/14", "232A-20261014003", "AC0001", "X00001", "1", "2000", "2000")), SalesReport.Sales, order));
    }

    [Fact]
    public void NoDataNoticeIsRecognisedOnlyByItsWords()
    {
        Assert.True(SalesReportLayout.IsNoData("查無資料"));
        Assert.True(SalesReportLayout.IsNoData("無 符 合 條 件 的 資 料 !"));
        Assert.True(SalesReportLayout.IsNoData("No Data"));
        Assert.False(SalesReportLayout.IsNoData("銷退單明細表[假公司]"));
        Assert.False(SalesReportLayout.IsNoData("匯出失敗"));
        Assert.False(SalesReportLayout.IsNoData(""));
    }
}
