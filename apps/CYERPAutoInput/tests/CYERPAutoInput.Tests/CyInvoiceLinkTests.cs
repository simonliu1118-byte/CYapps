using System.Text.Json;
using Xunit;

namespace CYERPAutoInput.Tests;

public sealed class CyInvoiceLinkTests
{
    private const string Id = "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10";

    private static MoOrder Order(decimal total = 725, string ban = "", string invoiceNo = "") =>
        new(2, "90000000000001", [new MoOrderLine("A1", 1, 725, 725, "商品")], "全家", "", "", ban, invoiceNo, "", total);

    private static CyInvoiceOrder Result(string status = "issued", string number = "AB12345678", string date = "2026/10/15", string ban = "", long amount = 725) =>
        new("90000000000001", status, number, date, ban, ban.Length > 0 ? "測試股份有限公司" : "消費者", amount, "", "");

    [Fact]
    public void RequestCarriesTheContractFields()
    {
        var json = CyInvoiceLink.Request(Id, @"C:\Temp\未出貨.xls", ["1", "2"], (@"C:\Temp\pdf", 5));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("cyinvoice-autoinput-request/1", root.GetProperty("schema").GetString());
        Assert.Equal(Id, root.GetProperty("requestId").GetString());
        Assert.Equal("mo", root.GetProperty("source").GetString());
        Assert.Equal(@"C:\Temp\未出貨.xls", root.GetProperty("file").GetString());
        Assert.Equal(2, root.GetProperty("orderIds").GetArrayLength());
        Assert.Equal(@"C:\Temp\pdf", root.GetProperty("pdfFolder").GetString());
        Assert.Equal(5, root.GetProperty("pdfStyle").GetInt32());
        Assert.False(JsonDocument.Parse(CyInvoiceLink.Request(Id, @"C:\a.xls", ["1"])).RootElement.TryGetProperty("pdfFolder", out _));
    }

    [Fact]
    public void ParsesTheHandoffExample()
    {
        var json = """
            {"schema":"cyinvoice-autoinput-result/1","requestId":"0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10","cyinvoiceVersion":"2.6.20+0",
             "environment":"test","finishedAt":"2026-10-14T10:00:00+08:00",
             "orders":[{"orderId":"90000000000001","status":"existing","invoiceNumber":"ab12345678","invoiceDate":"2026/10/14",
                        "buyerBan":"","buyerName":"消費者","totalAmount":725,"message":""}]}
            """;
        var result = CyInvoiceLink.ParseResult(json, Id);
        Assert.False(result.IsProduction);
        Assert.Equal("測試環境", result.EnvironmentName);
        var order = Assert.Single(result.Orders);
        Assert.Equal("AB12345678", order.InvoiceNumber);
        Assert.Equal(725, order.TotalAmount);
        Assert.Equal(string.Empty, order.PdfFile);
    }

    [Fact]
    public void RejectsAnotherRequestsResult()
    {
        var json = """{"schema":"cyinvoice-autoinput-result/1","requestId":"11111111-1111-1111-1111-111111111111","environment":"production","orders":[]}""";
        Assert.Throws<InvalidDataException>(() => CyInvoiceLink.ParseResult(json, Id));
        Assert.Throws<InvalidDataException>(() => CyInvoiceLink.ParseResult("""{"schema":"x","requestId":"0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10"}""", Id));
    }

    [Fact]
    public void OnlyACheckedInvoiceMayBeUsed()
    {
        Assert.Null(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result()));
        Assert.Null(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result("existing")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result("unknown")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result("failed")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result("skipped")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result(number: "AB1234")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result(date: "2026-10-15")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order()), Result(amount: 724)));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order(ban: "12345675")), Result()));
        Assert.Null(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order(ban: "12345675")), Result(ban: "12345675")));
        Assert.NotNull(CyInvoiceLink.Mismatch(InvoiceExpectation.Of(Order(invoiceNo: "CD87654321")), Result()));
    }

    [Fact]
    public void FillsTheNameOnlyForACompany()
    {
        var mapping = new MoMapping("230", "C1", "MO", "D", "S", new Dictionary<string, string>());
        var consumer = MoOrderImport.ToImported(Order(), mapping);
        CyInvoiceLink.Fill(consumer, Result());
        Assert.Equal("AB12345678", consumer.InvoiceNo);
        Assert.Equal("2026/10/15", consumer.InvoiceDate);
        Assert.Equal(string.Empty, consumer.InvoiceName);
        Assert.False(CyInvoiceLink.IsPaper(Result()));

        var company = MoOrderImport.ToImported(Order(ban: "12345675"), mapping);
        CyInvoiceLink.Fill(company, Result(ban: "12345675"));
        Assert.Equal("測試股份有限公司", company.InvoiceName);
        Assert.True(CyInvoiceLink.IsPaper(Result(ban: "12345675")));
        // 酷澎 is always paper (CYInvoice's 酷澎 entry).
        Assert.True(CyInvoiceLink.IsPaper(Result(), CyInvoiceLink.Coupang));
    }

    [Fact]
    public void PaperLedgerRemembersPerEnvironment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paper-{Guid.NewGuid():N}.json");
        try
        {
            new PaperInvoiceLedger(path).Record("test", "AB12345678");
            var ledger = new PaperInvoiceLedger(path);
            Assert.True(ledger.Printed("test", "AB12345678"));
            Assert.False(ledger.Printed("production", "AB12345678"));
        }
        finally { File.Delete(path); }
    }
}
