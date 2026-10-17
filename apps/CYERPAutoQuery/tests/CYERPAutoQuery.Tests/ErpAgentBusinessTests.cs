using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>CY Web customer／supplier／銷貨銷退／未收款 jobs and the 標準進價 grant (contract 2026-10-17), fake values only.</summary>
public class ErpAgentBusinessTests
{
    private static ErpAgentJob Claim(string job) =>
        ErpAgentProtocol.ParseClaim($$"""{"ok":true,"data":{"job":{{job}}},"requestId":"r"}""")!;

    [Fact]
    public void ClaimListsEverySupportedQueryType()
    {
        var body = JsonNode.Parse(ErpAgentProtocol.ClaimBody("v", "BROWSE"))!;
        Assert.Equal(["ITEM_NO", "ITEM_NAME", "CUSTOMER_NO", "CUSTOMER_NAME", "SUPPLIER_NO", "SUPPLIER_NAME", "PURCHASES", "SALES", "RECEIVABLE", "ORDER"],
            body["queryTypes"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void ParsesEachJobType()
    {
        var customer = Claim("""{"id":"j1","queryType":"CUSTOMER_NO","customerCode":"CU0001"}""");
        Assert.True(customer.IsBusiness);
        Assert.Equal("CU0001", customer.CustomerCode);
        Assert.Equal("假", Claim("""{"id":"j2","queryType":"CUSTOMER_NAME","keyword":"假"}""").Keyword);
        var supplier = Claim("""{"id":"j9","queryType":"SUPPLIER_NO","supplierCode":"SU0001"}""");
        Assert.True(supplier.IsBusiness);
        Assert.Same(ErpParty.Supplier, supplier.Party);
        Assert.Equal("SU0001", supplier.PartyCode);
        var supplierName = Claim("""{"id":"j10","queryType":"SUPPLIER_NAME","keyword":"假"}""");
        Assert.Equal(("假", ErpParty.Supplier), (supplierName.Keyword, supplierName.Party));
        Assert.Same(ErpParty.Customer, customer.Party);
        Assert.Throws<InvalidDataException>(() => Claim("""{"id":"j11","queryType":"SUPPLIER_NO","customerCode":"SU0001"}"""));
        var sales = Claim("""{"id":"j3","queryType":"SALES","customerCode":"CU0001","dateFrom":"2026-01-01","dateTo":"2026-10-17"}""");
        var query = sales.Sales(new DateOnly(2026, 10, 17));
        Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 17)), (query.From!.Value, query.To!.Value));
        var all = Claim("""{"id":"j4","queryType":"RECEIVABLE","scope":"ALL","code":null}""");
        Assert.Equal(ReceivableQuery.All, all.Receivable());
        Assert.Equal("SP01", Claim("""{"id":"j5","queryType":"RECEIVABLE","scope":"SALESPERSON","code":"SP01"}""").Receivable().Code);
        Assert.Throws<InvalidDataException>(() => Claim("""{"id":"j6","queryType":"SOMETHING"}"""));
        Assert.Throws<InvalidDataException>(() => Claim("""{"id":"j7","queryType":"CUSTOMER_NO"}"""));
        // A bad date is found when the job runs, so it is reported as FAILED instead of being lost.
        Assert.Throws<ArgumentException>(() => Claim("""{"id":"j8","queryType":"SALES","customerCode":"CU0001","dateFrom":"x","dateTo":"2026-10-17"}""").Sales(new DateOnly(2026, 10, 17)));
    }

    [Fact]
    public void StandardCostOnlyWithTheGrant()
    {
        Assert.False(Claim("""{"id":"j1","itemNo":"T0001","includeCost":false}""").IncludeCost);
        Assert.True(Claim("""{"id":"j2","itemNo":"T0001","includeCost":true}""").IncludeCost);
        Assert.False(Claim("""{"id":"j3","itemNo":"T0001"}""").IncludeCost);
        var result = new ItemQueryResult("T0001", [new ErpField("品號", "T0001"), new ErpField("標準進價", "1")], [], TimeSpan.Zero);
        string Names(bool cost) => string.Join(",", JsonNode.Parse(ErpAgentProtocol.FoundBody(result, "v", cost))!["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()));
        Assert.Equal("品號", Names(false));
        Assert.Equal("品號,標準進價", Names(true));
    }

    [Fact]
    public void SalesBodyEchoesTheJobAndFormatsNumbers()
    {
        var job = Claim("""{"id":"j1","queryType":"SALES","customerCode":"CU0001","dateFrom":"2026-01-01","dateTo":"2026-10-17"}""");
        var line = new SalesLine(1, "X0001", "假品名", "", "W01 假倉", 2m, "包", 4266.667m, 2100.00m, "贈品量", 0m, "", "", "901A-20260101001-0001");
        var order = new SalesOrder("銷退", "902A-20260102001", new DateOnly(2026, 1, 2), "CU0001", "假客戶", "", "", "", "", "",
            2m, 2100m, 0m, 2100m, "", "004 換貨", [line]);
        var body = JsonNode.Parse(ErpAgentProtocol.SalesBody(job, new SalesQueryResult(job.Sales(new DateOnly(2026, 10, 17)), [order], TimeSpan.FromSeconds(3)), "v"))!;
        Assert.Equal("FOUND", body["outcome"]!.GetValue<string>());
        Assert.Equal(["CU0001", "2026-01-01", "2026-10-17"], new[] { "customerCode", "dateFrom", "dateTo" }.Select(k => body[k]!.GetValue<string>()));
        var o = body["orders"]![0]!;
        Assert.Equal(["銷退", "2026-01-02", "2100"], new[] { "kind", "date", "total" }.Select(k => o[k]!.GetValue<string>()));
        Assert.Equal("4266.667", o["lines"]![0]!["unitPrice"]!.GetValue<string>());
        Assert.Equal("901A-20260101001-0001", o["lines"]![0]!["sourceOrder"]!.GetValue<string>());
        Assert.Equal(1, body["total"]!.GetValue<int>());

        var none = JsonNode.Parse(ErpAgentProtocol.SalesBody(job, new SalesQueryResult(job.Sales(new DateOnly(2026, 10, 17)), [], TimeSpan.Zero), "v"))!;
        Assert.Equal("NOT_FOUND", none["outcome"]!.GetValue<string>());
        Assert.Equal("CU0001", none["customerCode"]!.GetValue<string>());
        Assert.Empty(none["orders"]!.AsArray());
    }

    [Fact]
    public void ReceivableAndPartyBodies()
    {
        var all = Claim("""{"id":"j1","queryType":"RECEIVABLE","scope":"ALL","code":null}""");
        var bill = new ReceivableBill("CU0001", "假客戶", new DateOnly(2026, 9, 30), "903A-20260930001", "SP01", "假業務", "", "NTD",
            1000m, 0m, -30.5m, [new ReceivableVoucher("901A-20260901001", null, 1000m, 0m)]);
        var body = JsonNode.Parse(ErpAgentProtocol.ReceivableBody(all, new ReceivableResult(ReceivableQuery.All, [bill], TimeSpan.Zero), "v"))!;
        Assert.Equal("ALL", body["scope"]!.GetValue<string>());
        Assert.Null(body["code"]);
        Assert.Equal("-30.5", body["bills"]![0]!["unpaid"]!.GetValue<string>());
        Assert.Equal("", body["bills"]![0]!["vouchers"]![0]!["date"]!.GetValue<string>());

        var customer = Claim("""{"id":"j2","queryType":"CUSTOMER_NO","customerCode":"CU0009"}""");
        var none = JsonNode.Parse(ErpAgentProtocol.PartyBody(customer, PartyQueryResult.NotFound(ErpParty.Customer, "CU0009", TimeSpan.Zero), "v"))!;
        Assert.Equal("NOT_FOUND", none["outcome"]!.GetValue<string>());
        Assert.Equal("CU0009", none["customerCode"]!.GetValue<string>());
        Assert.Empty(none["fields"]!.AsArray());

        var supplier = Claim("""{"id":"j3","queryType":"SUPPLIER_NO","supplierCode":"SU0001"}""");
        var found = JsonNode.Parse(ErpAgentProtocol.PartyBody(supplier,
            new PartyQueryResult(ErpParty.Supplier, "SU0001", [new ErpField("廠商代號", "SU0001"), new ErpField("廠商簡稱", "假廠商")], TimeSpan.Zero), "v"))!;
        Assert.Equal("FOUND", found["outcome"]!.GetValue<string>());
        Assert.Equal("SU0001", found["supplierCode"]!.GetValue<string>());
        Assert.Null(found["customerCode"]);
        Assert.Equal("假廠商", found["fields"]![1]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void OrderJobEchoesItsNumber()
    {
        var job = Claim("""{"id":"j1","queryType":"ORDER","orderNo":"232A-20260101001"}""");
        Assert.True(job.IsBusiness);
        Assert.Equal("232A-20260101001", job.OrderNo);
        var none = JsonNode.Parse(ErpAgentProtocol.SalesBody(job, new SalesQueryResult(SalesQuery.ForOrder(job.OrderNo!), [], TimeSpan.Zero), "v"))!;
        Assert.Equal("232A-20260101001", none["orderNo"]!.GetValue<string>());
        Assert.Null(none["customerCode"]);
        Assert.Contains("ORDER", JsonNode.Parse(ErpAgentProtocol.ClaimBody("v", "BROWSE"))!["queryTypes"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void PurchaseJobEchoesSupplierAndDates()
    {
        var job = Claim("""{"id":"j1","queryType":"PURCHASES","supplierCode":"SU0001","dateFrom":"2026-01-01","dateTo":"2026-10-18"}""");
        Assert.True(job.IsBusiness);
        var query = job.Purchases(new DateOnly(2026, 10, 18));
        Assert.Equal(("SU0001", new DateOnly(2026, 1, 1)), (query.SupplierCode, query.From!.Value));
        Assert.Throws<InvalidDataException>(() => Claim("""{"id":"j2","queryType":"PURCHASES","customerCode":"SU0001"}"""));
        var line = new PurchaseLine(1, "X0001", "假品名", "", new DateOnly(2026, 2, 12), "假倉", 10m, 10m, 0m, "個", 23m, 230m, 11.5m, "", "", "");
        var order = new PurchaseOrder("進貨", "341A-20260212001", new DateOnly(2026, 2, 12), "SU0001", "假廠商", "FAKE01", "", "三聯式發票", "應稅外加", "NTD",
            10m, 230m, 0m, 11.5m, 241.5m, "N", [line]);
        var body = JsonNode.Parse(ErpAgentProtocol.PurchaseBody(job, new PurchaseQueryResult(query, [order], TimeSpan.Zero), "v"))!;
        Assert.Equal(["FOUND", "SU0001", "2026-01-01", "2026-10-18"], new[] { "outcome", "supplierCode", "dateFrom", "dateTo" }.Select(k => body[k]!.GetValue<string>()));
        var o = body["orders"]![0]!;
        Assert.Equal(["進貨", "2026-02-12", "241.5", "FAKE01"], new[] { "kind", "date", "total", "supplierOrderNo" }.Select(k => o[k]!.GetValue<string>()));
        Assert.Equal(["23", "230", "2026-02-12"], new[] { "unitPrice", "amount", "acceptDate" }.Select(k => o["lines"]![0]![k]!.GetValue<string>()));
        var none = JsonNode.Parse(ErpAgentProtocol.PurchaseBody(job, new PurchaseQueryResult(query, [], TimeSpan.Zero), "v"))!;
        Assert.Equal("NOT_FOUND", none["outcome"]!.GetValue<string>());
        Assert.Empty(none["orders"]!.AsArray());
    }
}
