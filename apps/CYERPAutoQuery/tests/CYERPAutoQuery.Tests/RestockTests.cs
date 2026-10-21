using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>進貨輔助 (user 2026-10-20), fake values only.</summary>
public class RestockTests
{
    private static SalesOrder Order(string kind, string no, DateOnly date, params (string Item, decimal Qty, decimal Gift)[] lines) =>
        new(kind, no, date, "CU0001", "假客戶", "", "", "", "", "", lines.Sum(l => l.Qty), 0m, 0m, 0m, "", "",
            lines.Select((l, i) => new SalesLine(i + 1, l.Item, "假品名" + l.Item, "", "", l.Qty, "包", 0m, 0m, "", l.Gift, "", "", "")).ToList());

    private static readonly DateOnly D12 = new(2026, 10, 12), D13 = new(2026, 10, 13);

    [Fact]
    public void SumsSoldAndReturnedByItemAndDay()
    {
        var items = Restock.Summarize(
        [
            Order("銷貨", "901A-1", D12, ("X00002", 3m, 0m), ("X00001", 1m, 0m)),
            Order("銷貨", "901A-2", D12, ("X00002", 2m, 1m)),
            Order("銷退", "902A-1", D13, ("X00002", 1m, 0m)),
            Order("銷貨", "901A-3", D13, ("X00001", 4m, 0m)),
        ]);
        Assert.Equal(["X00001", "X00002"], items.Select(i => i.ItemCode));
        var x2 = items[1];
        Assert.Equal(("假品名X00002", "包"), (x2.ItemName, x2.Unit));
        // 贈／備品量 counts as 銷貨 (user 2026-10-21), and is kept apart as well.
        Assert.Equal([new RestockDay(D12, 6m, 0m, 1m), new RestockDay(D13, 0m, 1m)], x2.Days);
        Assert.Equal((6m, 1m, 1m), (x2.Sold, x2.Returned, x2.SoldGift));
        Assert.Equal((5m, 0m), (items[0].Sold, items[0].Returned));
    }

    [Fact]
    public void ResolveNeedsBothDatesWithinAMonth()
    {
        var today = new DateOnly(2026, 10, 20);
        Assert.Equal(new RestockQuery(D12, today), RestockQuery.Resolve(D12, today, today));
        Assert.Throws<ArgumentException>(() => RestockQuery.Resolve(null, today, today));
        Assert.Throws<ArgumentException>(() => RestockQuery.Resolve(D12, null, today));
        Assert.Throws<ArgumentException>(() => RestockQuery.Resolve(D13, D12, today));
        Assert.Throws<ArgumentException>(() => RestockQuery.Resolve(D12, today.AddDays(1), today));
        Assert.Throws<ArgumentException>(() => RestockQuery.Resolve(new DateOnly(2026, 9, 19), today, today));
        RestockQuery.Resolve(new DateOnly(2026, 9, 20), today, today);
    }

    [Fact]
    public void ClaimAndBodyCarryTheRangeItemsAndStock()
    {
        var job = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j1","queryType":"RESTOCK","dateFrom":"2026-10-12","dateTo":"2026-10-13"}},"requestId":"r"}""")!;
        Assert.Equal("RESTOCK", job.QueryType);
        Assert.True(job.IsBusiness);
        var query = job.Restock(new DateOnly(2026, 10, 20));
        var items = Restock.Summarize([Order("銷貨", "901A-1", D12, ("X00001", 2.5m, 0m)), Order("銷退", "902A-1", D13, ("X00002", 1m, 2m))]);
        items[0] = items[0] with { Stock = "40" };
        items[1] = items[1] with { StockNote = "ERP 查無此品號" };
        var body = JsonNode.Parse(ErpAgentProtocol.RestockBody(job, new RestockResult(query, items, TimeSpan.FromSeconds(2)), "v"))!;
        Assert.Equal("FOUND", body["outcome"]!.GetValue<string>());
        Assert.Equal(["2026-10-12", "2026-10-13"], new[] { "dateFrom", "dateTo" }.Select(k => body[k]!.GetValue<string>()));
        Assert.Equal(2, body["total"]!.GetValue<int>());
        var first = body["items"]![0]!;
        Assert.Equal(["X00001", "2.5", "0", "40"], new[] { "itemCode", "sold", "returned", "stock" }.Select(k => first[k]!.GetValue<string>()));
        Assert.Equal(["2026-10-12", "2.5", "0"], new[] { "date", "sold", "returned" }.Select(k => first["days"]![0]![k]!.GetValue<string>()));
        var second = body["items"]![1]!;
        Assert.Equal(["3", "2"], new[] { "returned", "returnedGift" }.Select(k => second[k]!.GetValue<string>()));
        Assert.Equal(["3", "2"], new[] { "returned", "returnedGift" }.Select(k => second["days"]![0]![k]!.GetValue<string>()));
        Assert.Null(second["stock"]);
        Assert.Equal("ERP 查無此品號", second["stockNote"]!.GetValue<string>());

        var none = JsonNode.Parse(ErpAgentProtocol.RestockBody(job, new RestockResult(query, [], TimeSpan.Zero), "v"))!;
        Assert.Equal("NOT_FOUND", none["outcome"]!.GetValue<string>());
    }

    [Fact]
    public void ABadRangeFailsWhenRun()
    {
        var job = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"j1","queryType":"RESTOCK","dateFrom":"2026-10-12"}},"requestId":"r"}""")!;
        Assert.Throws<ArgumentException>(() => job.Restock(new DateOnly(2026, 10, 20)));
    }

    [Theory]
    [InlineData("1,234.000", "1234")]
    [InlineData(" -2.50 ", "-2.5")]
    [InlineData("0", "0")]
    [InlineData("", null)]
    [InlineData("--", null)]
    public void StockIsSentAsAPlainNumber(string shown, string? sent) => Assert.Equal(sent, Restock.Quantity(shown));

    [Fact]
    public void ProgressIsPostedForTheJob()
    {
        Assert.Equal("api/erp-agent/item-queries/j%201/progress", ErpAgentProtocol.ProgressPath("j 1"));
        var body = JsonNode.Parse(ErpAgentProtocol.ProgressBody("STOCK", 3, 40))!;
        Assert.Equal(("STOCK", 3, 40), (body["stage"]!.GetValue<string>(), body["done"]!.GetValue<int>(), body["total"]!.GetValue<int>()));
    }
}
