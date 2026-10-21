using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// PURR18 進貨單明細表 export, rebuilt with fake values in the column order of the user's sample
/// (2026-10-18): titles in row 1 with spaces, one row per line repeating the 單頭 (原幣進貨金額 etc.
/// twice: 單頭 then line), a 「小計:N張」 row last.
/// </summary>
public class PurchaseQueryTests
{
    private static readonly string[] Titles =
    [
        "進貨單號", "單據日期", "發票號碼", "廠商代號", "廠商簡稱", "廠商單號", "發票聯數", "課稅別", "幣別", "匯率",
        "原幣進貨金額", "原幣扣款金額", "原幣稅額", "原幣貨款金額", "原幣金額合計", "本幣稅額", "本幣貨款金額", "本幣金額合計", "原幣總進貨費用", "數量合計",
        "包裝數量合計", "預付採購單號", "預付待抵單號", "沖抵金額", "沖抵稅額", "品    號", "品    名", "規    格", "驗收日期", "庫別",
        "進貨數量", "驗收數量", "計價數量", "驗退數量", "單位", "計價單位", "檢驗狀態", "進貨包裝數量", "驗收包裝數量", "驗退包裝數量",
        "包裝單位", "原幣單位進價", "原幣進貨金額", "原幣扣款金額", "合計金額", "本幣進貨費用", "扣款說明", "原幣未稅金額", "原幣稅額", "本幣未稅金額",
        "本幣稅額", "採購單號", "借入單號", "備注", "結帳", "應付憑單號", "批號", "逾期", "專案代號", "專案說明",
    ];

    private static string[] Line(string no, string date, string supplier, string item, string qty, string price, string amount,
        string orderAmount = "300", string orderTax = "15", string orderTotal = "315", string lineTax = "5") =>
    [
        no, date, "", supplier, "假廠商", "FAKE01", "三聯式發票", "應稅外加", "NTD", "1",
        orderAmount, "0", orderTax, orderAmount, orderTotal, orderTax, orderAmount, orderTotal, "0", "30",
        "0", "-", "-", "0", "0", item, "假品名" + item, "假規格", date, "假倉",
        qty, qty, qty, "0", "個", "個", "免檢", "0", "0", "0",
        "", price, amount, "0", amount, "0", "", amount, lineTax, amount,
        lineTax, "--", "--", "", "N", "--", "", "N", "", "",
    ];

    private static string[] Subtotal(int orders, string total)
    {
        var row = new string[Titles.Length];
        Array.Fill(row, "");
        row[0] = $"小計:{orders}張";
        row[3] = "3筆";
        row[14] = total;
        return row;
    }

    private static IReadOnlyList<(string Name, List<string[]> Rows)> Export(params string[][] rows) => SalesOrderQueryTests.ExportWith(Titles, rows);

    private static readonly PurchaseQuery Query = new("SU0001", new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 18));

    [Fact]
    public void GroupsLinesIntoOrdersNewestFirst()
    {
        var orders = PurchaseExport.Parse(Export(
            Line("341A-20260212001", "2026/02/12", "SU0001", "X0001", "10", "10", "100"),
            Line("341A-20260212001", "2026/02/12", "SU0001", "X0002", "20", "10", "200"),
            Line("341A-20261016001", "2026/10/16", "SU0001", "X0003", "30", "10", "300", orderTotal: "315"),
            Subtotal(2, "630")), Query);
        Assert.Equal(["341A-20261016001", "341A-20260212001"], orders.Select(o => o.OrderNo));
        var older = orders[1];
        // 單頭 amounts come from the first 原幣進貨金額／原幣稅額, line amounts from the second.
        Assert.Equal((300m, 15m, 315m, 30m), (older.Amount, older.Tax, older.Total, older.TotalQuantity));
        Assert.Equal(("FAKE01", "", "三聯式發票", "應稅外加", "NTD", "N"), (older.SupplierOrderNo, older.InvoiceNo, older.InvoiceType, older.TaxType, older.Currency, older.Settled));
        Assert.Equal([1, 2], older.Lines.Select(l => l.Seq));
        var line = older.Lines[1];
        Assert.Equal(("X0002", 20m, 20m, 0m, 10m, 200m, 5m), (line.ItemCode, line.Quantity, line.AcceptedQuantity, line.ReturnedQuantity, line.UnitPrice, line.Amount, line.Tax));
        Assert.Equal(new DateOnly(2026, 2, 12), line.AcceptDate);
        // 「--」 means none.
        Assert.Equal("", line.PurchaseOrderNo);
    }

    [Fact]
    public void RefusesAnotherSupplierOutOfRangeOrBadSubtotal()
    {
        Assert.Throws<InvalidDataException>(() => PurchaseExport.Parse(Export(
            Line("341A-20260212001", "2026/02/12", "SU0002", "X0001", "10", "10", "100")), Query));
        Assert.Throws<InvalidDataException>(() => PurchaseExport.Parse(Export(
            Line("341A-20251212001", "2025/12/12", "SU0001", "X0001", "10", "10", "100")), Query));
        Assert.Throws<InvalidDataException>(() => PurchaseExport.Parse(Export(
            Line("341A-20260212001", "2026/02/12", "SU0001", "X0001", "10", "10", "100"), Subtotal(2, "315")), Query));
    }

    [Fact]
    public void EmptyReportAndMissingPrice()
    {
        Assert.Empty(PurchaseExport.Parse(Export(), Query));
        var noPrice = Titles.Select(t => t == "原幣單位進價" ? "其他" : t).ToArray();
        var error = Assert.Throws<InvalidDataException>(() => PurchaseExport.Parse(SalesOrderQueryTests.ExportWith(noPrice,
            Line("341A-20260212001", "2026/02/12", "SU0001", "X0001", "10", "10", "100")), Query));
        Assert.Contains("列印單價金額", error.Message);
    }

    [Fact]
    public void QueryNeedsASupplierCodeAndAValidRange()
    {
        var today = new DateOnly(2026, 10, 18);
        Assert.Equal("SU0001", PurchaseQuery.Resolve(" SU0001 ", null, null, today).SupplierCode);
        Assert.Throws<ArgumentException>(() => PurchaseQuery.Resolve("SU_01", null, null, today));
        Assert.Throws<ArgumentException>(() => PurchaseQuery.Resolve("SU0001", today.AddDays(1), null, today));
        Assert.Throws<ArgumentException>(() => PurchaseQuery.Resolve("SU0001", today, today.AddDays(-1), today));
        Assert.True(PurchaseReportLayout.Report.IsTitle("進貨單明細表[假公司]"));
        Assert.True(PurchaseReportLayout.Report.IsTitle("進貨單明細表(PURR18)[假公司]"));
    }

    private static readonly string[] ReturnTitles =
    [
        "單據日期", "退貨單號", "廠商代號", "簡稱", "幣別", "匯率", "聯數", "課稅別", "原幣退貨金額", "原幣退貨稅額",
        "本幣退貨金額", "本幣退貨稅額", "數量合計", "包裝數量合計", "發票號碼", "品號", "品名", "規格", "庫別代號", "庫別名稱",
        "數量", "單位", "包裝數量", "包裝單位", "單價", "原幣未稅金額", "原幣稅額", "本幣未稅金額", "本幣稅額", "備註",
        "原進貨單號", "原採購單號", "結帳", "應付憑單號",
    ];

    private static string[] ReturnLine(string date, string no, string supplier, string amount, string source = "--") =>
    [
        date, no, supplier, "假廠商", "NTD", "1", "三聯式發票", "不計稅", amount, "0",
        amount, "0", "5", "0", "", "X0001", "假品名", "", "KH01", "假倉",
        "5", "個", "0", "", "20", amount, "0", amount, "0", "假退貨原因",
        source, "--", "N", "--",
    ];

    private static string[] ReturnSubtotal(int orders, string amount)
    {
        var row = new string[ReturnTitles.Length];
        Array.Fill(row, "");
        row[1] = $"小計:{orders}張";
        row[2] = "2筆";
        row[8] = amount;
        return row;
    }

    [Fact]
    public void ReturnsKeepTheSupplierAndCheckTheWholeSubtotal()
    {
        var returns = PurchaseExport.ParseReturns(SalesOrderQueryTests.ExportWith(ReturnTitles,
            ReturnLine("2026/03/01", "351A-20260301001", "SU0002", "50"),
            ReturnLine("2026/03/02", "351A-20260302001", "SU0001", "100", "341A-20260212001-0002"),
            ReturnSubtotal(2, "150")), Query);
        var r = Assert.Single(returns);
        Assert.Equal(("退貨", "351A-20260302001", 100m, 100m), (r.Kind, r.OrderNo, r.Amount, r.Total));
        Assert.Equal(("341A-20260212001-0002", "KH01 假倉", 5m, 20m), (r.Lines[0].SourceOrder, r.Lines[0].Warehouse, r.Lines[0].Quantity, r.Lines[0].UnitPrice));
        // A 小計 that does not cover every supplier's 退貨單, or a date outside the range, refuses the export.
        Assert.Throws<InvalidDataException>(() => PurchaseExport.ParseReturns(SalesOrderQueryTests.ExportWith(ReturnTitles,
            ReturnLine("2026/03/02", "351A-20260302001", "SU0001", "100"), ReturnSubtotal(2, "150")), Query));
        Assert.Throws<InvalidDataException>(() => PurchaseExport.ParseReturns(SalesOrderQueryTests.ExportWith(ReturnTitles,
            ReturnLine("2025/03/02", "351A-20250302001", "SU0002", "100")), Query));
        Assert.Empty(PurchaseExport.ParseReturns(SalesOrderQueryTests.ExportWith(ReturnTitles), Query));
    }
}
