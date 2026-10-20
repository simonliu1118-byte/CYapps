using System.IO.Compression;
using System.Text;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// ACRR18 未結案應收明細表 export, rebuilt with fake values in the column order of the user's
/// samples (2026-10-17): one row per 憑證 repeating its 結帳單, 小計 after each customer, 合計 last.
/// </summary>
public class ReceivableQueryTests
{
    private static readonly string[] Titles =
    [
        "客戶代號", "客戶名稱", "日期", "結帳單號", "業務員代號", "業務員名稱", "發票號碼", "幣別", "原幣應收帳款", "原幣應收稅額",
        "原幣未收帳款", "匯率", "憑證號碼", "憑證日期", "原幣未稅金額", "原幣稅額", "本幣未稅金額", "本幣稅額",
    ];

    private static string[] Row(string customer, string bill, string sales, string unpaid, string voucher, string amount) =>
    [
        customer, "假客戶" + customer, "2026/09/30", bill, sales, "假業務", "", "NTD", unpaid, "0",
        unpaid, "1", voucher, "2026/09/10", amount, "0", amount, "0",
    ];

    private static string[] Sum(string label, string unpaid)
    {
        var row = new string[Titles.Length];
        Array.Fill(row, "");
        row[4] = label;
        row[6] = "NTD";
        row[10] = unpaid;
        return row;
    }

    private static string Col(int index)
    {
        var name = string.Empty;
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Export(params string[][] rows)
    {
        string Cell(int col, int row, string value) =>
            $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(value)}</t></is></c>";
        var xml = new StringBuilder("<row r=\"1\">");
        for (var i = 0; i < Titles.Length; i++) xml.Append(Cell(i, 1, Titles[i]));
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
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"ACRR18202610170000\" sheetId=\"1\" r:id=\"rId1\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
        }
        ms.Position = 0;
        return XlsxReader.ReadSheets(ms);
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> TwoCustomers(string total = "1200") => Export(
        Row("CU0001", "901A-20260930001", "SP01", "1000", "902A-20260901001", "1100"),
        Row("CU0001", "901A-20260930001", "SP01", "1000", "903A-20260910001", "-100"),
        Sum("小計:", "1000"),
        Row("CU0002", "901A-20260930002", "SP02", "200", "902A-20260905001", "200"),
        Sum("小計:", "200"),
        Sum("合計", total));

    [Fact]
    public void GroupsVouchersIntoBillsAndChecksTheTotal()
    {
        var bills = ReceivableExport.Parse(TwoCustomers(), ReceivableQuery.All);
        Assert.Equal(["901A-20260930001", "901A-20260930002"], bills.Select(b => b.BillNo));
        var first = bills[0];
        Assert.Equal(1000m, first.Unpaid);
        Assert.Equal(new DateOnly(2026, 9, 30), first.Date);
        Assert.Equal(["902A-20260901001", "903A-20260910001"], first.Vouchers.Select(v => v.VoucherNo));
        Assert.Equal(-100m, first.Vouchers[1].Amount);
        var result = new ReceivableResult(ReceivableQuery.All, bills, TimeSpan.Zero);
        Assert.Equal(1200m, result.TotalUnpaid);
        Assert.Equal(2, result.CustomerCount);
    }

    [Fact]
    public void RefusesWhenBillsDoNotAddUpToTheTotal()
    {
        Assert.Throws<InvalidDataException>(() => ReceivableExport.Parse(TwoCustomers(total: "999"), ReceivableQuery.All));
    }

    [Fact]
    public void OneCustomerOrSalespersonMustMatchEveryRow()
    {
        var customer = new ReceivableQuery(ReceivableScope.Customer, "CU0001");
        Assert.Throws<InvalidDataException>(() => ReceivableExport.Parse(TwoCustomers(), customer));
        Assert.Single(ReceivableExport.Parse(Export(
            Row("CU0001", "901A-20260930001", "SP01", "1000", "902A-20260901001", "1000"), Sum("小計:", "1000"), Sum("合計", "1000")), customer));

        var salesperson = new ReceivableQuery(ReceivableScope.Salesperson, "SP02");
        Assert.Throws<InvalidDataException>(() => ReceivableExport.Parse(TwoCustomers(), salesperson));
    }

    [Fact]
    public void NothingToCollectIsEmpty()
    {
        Assert.Empty(ReceivableExport.Parse(Export(Sum("合計", "0")), ReceivableQuery.All));
        Assert.Empty(ReceivableExport.Parse(Export(), ReceivableQuery.All));
    }

    [Fact]
    public void ResolveChecksTheCode()
    {
        Assert.Equal(ReceivableQuery.All, ReceivableQuery.Resolve(ReceivableScope.All, "anything"));
        Assert.Equal("CU0001", ReceivableQuery.Resolve(ReceivableScope.Customer, " CU0001 ").Code);
        Assert.Throws<ArgumentException>(() => ReceivableQuery.Resolve(ReceivableScope.Salesperson, "SP-1"));
        Assert.Throws<ArgumentException>(() => ReceivableQuery.Resolve(ReceivableScope.Customer, ""));
    }

    [Fact]
    public void ReportIsRecognisedByTitle()
    {
        Assert.True(ReceivableLayout.Report.IsTitle("未結案應收明細表[假公司]"));
        Assert.True(ReceivableLayout.Report.IsTitle("未結案應收明細表(ACRR18)[A]"));
        Assert.False(ReceivableLayout.Report.IsTitle("銷貨單明細表(COPR15)[A]"));
    }
}
