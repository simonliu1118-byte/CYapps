using System.Drawing;
using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

public class StocktakeTests
{
    private const string Id = "4a5c95df-4b19-4070-9911-82a66f26aacc";
    private static string Claim(string payload) => $$"""{"ok":true,"data":{"job":{{payload}}},"requestId":"test"}""";
    private static OcrToken T(string text, int x, int y, int w = 50, int h = 14) => new(text, new Rectangle(x, y, w, h));

    [Fact]
    public void ClaimsParseAndBothTypesAreAdvertised()
    {
        var sync = ErpAgentProtocol.ParseClaim(Claim(
            $$$"""{"id":"{{{Id}}}","queryType":"STOCKTAKE_SYNC","skipSheets":["990001"],"onlySheet":"990002"}"""))!;
        Assert.Equal(["990001"], sync.SkipSheets);
        Assert.Equal("990002", sync.OnlySheet);

        var write = ErpAgentProtocol.ParseClaim(Claim(
            $$$"""{"id":"{{{Id}}}","queryType":"STOCKTAKE_WRITE","sheetNo":"990001","rows":[{"key":"00001","seq":"00001","itemCode":"X0001","batch":"B1","quantity":"-3.25"},{"key":"N0001","seq":null,"itemCode":"X0001","batch":"B9","quantity":"2"}]}"""))!;
        Assert.Equal("990001", write.SheetNo);
        Assert.Equal("-3.25", write.StocktakeRows[0].Quantity);
        Assert.Null(write.StocktakeRows[1].Seq);

        var advertised = JsonNode.Parse(ErpAgentProtocol.ClaimBody("V1.3.0", "BROWSE"))!["queryTypes"]!.AsArray()
            .Select(x => x!.GetValue<string>()).ToArray();
        Assert.Contains("STOCKTAKE_SYNC", advertised);
        Assert.Contains("STOCKTAKE_WRITE", advertised);
    }

    [Fact]
    public void QuantityIsAPlainDecimalString()
    {
        Assert.Equal("1234.5", StocktakeQuery.Quantity("1,234.50"));
        Assert.Equal("0", StocktakeQuery.Quantity(""));
        Assert.Equal("-12", StocktakeQuery.Quantity("-12"));
        Assert.True(StocktakeQuery.SameQuantity("10", "10.0"));
        Assert.Throws<InvalidDataException>(() => StocktakeQuery.Quantity("1e3"));
    }

    [Fact]
    public void F2GridAndFooterAreReadFromOcrTokens()
    {
        // Screenshot 105: 底稿編號 and 盤點日期 cells, the filter row above, 「1 / 4」 in the footer.
        IReadOnlyList<OcrToken> tokens =
        [
            T("底稿編號", 240, 165), T(">=", 400, 165, 20),
            T("990202", 80, 240), T("2025/02/17", 180, 241, 70),
            T("990102", 80, 262), T("2026/01/09", 180, 262, 70),
            T("990901", 80, 284), T("2026/09/02", 180, 285, 70),
            T("1 / 4", 125, 485, 40), T("INVTE-02", 240, 485, 60)
        ];
        var sheets = StocktakeQuery.ParseF2(tokens);
        Assert.Equal(["990202", "990102", "990901"], sheets.Select(s => s.Sheet.SheetNo));
        Assert.Equal(new DateOnly(2026, 1, 9), sheets[1].Sheet.Date);
        Assert.Equal(262, sheets[1].Rect.Top);
        Assert.Equal(4, StocktakeQuery.ParseF2Total(tokens));
        Assert.Null(StocktakeQuery.ParseF2Total([T("990202", 80, 240), T("2025/02/17", 180, 241, 70)]));
    }

    [Fact]
    public void GridHeaderSeparatesBatchFromBatchCheckAndFindsTheNewRow()
    {
        // Screenshot 111: … 批號盤點 批號 盤點數量 …
        IReadOnlyList<OcrToken> header = [T("序號", 50, 5, 30), T("品號", 125, 5, 30), T("批號盤點", 850, 5, 60), T("批號", 930, 5, 30), T("盤點數量", 990, 5, 60)];
        var x = StocktakeQuery.GridColumns(header, "序號", "品號", "批號", "盤點數量");
        Assert.Equal(945, x["批號"]);
        Assert.Equal(1020, x["盤點數量"]);

        IReadOnlyList<OcrToken> grid = [T("00046A00671", 45, 100, 120), T("00047", 45, 122, 40), T("A00671", 90, 122), T("00048", 45, 144, 40)];
        var seqs = StocktakeQuery.GridSeqs(grid, x["序號"]);
        Assert.Equal(["00046", "00047", "00048"], seqs.Select(s => s.Seq));
        Assert.Equal(151, seqs[^1].Y);

        IReadOnlyList<OcrToken> typed = [T("00048", 45, 144, 40), T("A00671", 90, 144), T("揚隆美容針", 200, 144, 80), T("20261201", 910, 144, 55), T("5", 1045, 144, 8), T("0", 1100, 144, 8)];
        Assert.Contains("A00671", StocktakeQuery.RowText(typed, 151));
        Assert.Equal("5", StocktakeQuery.CellText(typed, 151, x["盤點數量"] + 28, 40));
    }

    [Fact]
    public void ExportUsesErpQuantityAndSkipsBlankLines()
    {
        var rows = new List<string[]>
        {
            new[] { "志遠測試" },
            new[] { "底稿編號", "底稿序號", "品號", "品名", "規格", "庫別名稱", "儲位", "單位", "批號", "盤點數量", "初盤數量" },
            new[] { "990001", "00001", "X0001", "測試商品", "60入", "假倉", "A-01", "盒", "A1", "2", "" },
            new[] { "990001", "00002", "X0001", "測試商品", "60入", "假倉", "A-01", "盒", "A2", "-1", "" },
            new[] { "", "", "", "", "", "", "", "", "", "", "" }
        };
        var parsed = StocktakeQuery.ParseExport("990001", [("Sheet1", rows)]);
        Assert.Equal(["2", "-1"], parsed.Rows.Select(r => r.BookQty));
        Assert.Throws<InvalidDataException>(() => StocktakeQuery.ParseExport("990002", [("Sheet1", rows)]));
        Assert.Throws<InvalidDataException>(() => StocktakeQuery.ParseExport("990001", [("Sheet1", [rows[2]])]));
    }

    [Fact]
    public void WriteBodyCarriesResultsAndMismatches()
    {
        var result = new StocktakeWriteResult("990001",
            [new StocktakeWriteRowResult("00001", true, "00001", "已寫入")], false,
            [new StocktakeMismatch("00001", "00001", "X0001", "B1", "3", "2")], TimeSpan.FromSeconds(1));
        var body = JsonNode.Parse(ErpAgentProtocol.StocktakeWriteBody(result, "V1.3.0"))!;
        Assert.Equal("FAILED", body["outcome"]!.GetValue<string>());
        Assert.Equal("2", body["mismatches"]![0]!["actual"]!.GetValue<string>());

        var empty = new StocktakeSyncResult([new StocktakeSheet("990001", new DateOnly(2026, 10, 29))], [], TimeSpan.Zero);
        Assert.Equal("NOT_FOUND", JsonNode.Parse(ErpAgentProtocol.StocktakeSyncBody(empty, "V1.3.0"))!["outcome"]!.GetValue<string>());
    }
}
