using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

public class StocktakeTests
{
    private const string Id="4a5c95df-4b19-4070-9911-82a66f26aacc";
    private static string Claim(string payload) =>
        $$"""{"ok":true,"data":{"job":{{payload}}},"requestId":"test"}""";

    [Fact]
    public void ClaimOnlySheetPreservesSkipAndAdvertisesBothTypes()
    {
        var job=ErpAgentProtocol.ParseClaim(Claim(
            $$$"""{"id":"{{{Id}}}","queryType":"STOCKTAKE_SYNC","expiresAt":"x","skipSheets":["990001"],"onlySheet":"990001"}"""))!;
        Assert.Equal(ErpAgentJob.StocktakeSyncType,job.QueryType);
        Assert.Equal("990001",job.OnlySheet);
        Assert.Single(job.SkipSheets);
        var advertised=JsonNode.Parse(ErpAgentProtocol.ClaimBody("V1.3.0","BROWSE"))!["queryTypes"]!.AsArray()
            .Select(x=>x!.GetValue<string>()).ToArray();
        // Feature is intentionally not claimed until the Win32 driver has been
        // implemented and verified against the real ERP UI.
        Assert.DoesNotContain("STOCKTAKE_SYNC",advertised);
        Assert.DoesNotContain("STOCKTAKE_WRITE",advertised);
    }

    [Fact]
    public void ClaimRejectsTooManySkipsAndMalformedWrite()
    {
        string Sync(string data) => Claim($$"""{"id":"{{Id}}","queryType":"STOCKTAKE_SYNC","skipSheets":{{data}},"onlySheet":null}""");
        var skips="["+string.Join(",",Enumerable.Repeat("\"990001\"",501))+"]";
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.ParseClaim(Sync(skips)));
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.ParseClaim(Claim(
            $$$"""{"id":"{{{Id}}}","queryType":"STOCKTAKE_WRITE","sheetNo":"990001","rows":[{"key":"00001","seq":"00001","itemCode":"X0001","batch":"B1","quantity":3}]}""")));
    }

    [Fact]
    public void WriteClaimUsesStringDecimalsAndDistinguishesNewBatch()
    {
        var job=ErpAgentProtocol.ParseClaim(Claim(
            $$$"""{"id":"{{{Id}}}","queryType":"STOCKTAKE_WRITE","sheetNo":"990001","rows":[{"key":"00001","seq":"00001","itemCode":"X0001","batch":"B1","quantity":"-3.25"},{"key":"N0001","seq":null,"itemCode":"X0001","batch":"B9","quantity":"2"}]}"""))!;
        Assert.Equal(2,job.StocktakeRows.Count);
        Assert.Equal("-3.25",job.StocktakeRows[0].Quantity);
        Assert.Null(job.StocktakeRows[1].Seq);
    }

    [Fact]
    public void F2ParsesDatesDeduplicatesPagesAndRejectsOverflow()
    {
        var listed=StocktakeQuery.ParseF2("990001 2026/10/28\n990002 2026/10/29\n990001 2026/10/28");
        Assert.Equal(2,listed.Count);
        Assert.Equal(new DateOnly(2026,10,29),listed[1].Date);
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.ParseF2(
            string.Join("\n",Enumerable.Range(100000,201).Select(x=>$"{x} 2026/10/28"))));
    }

    [Fact]
    public void ParseExportUsesErpCountNotUnderlinedFirstCount()
    {
        var headers=new[]{"底稿編號","底稿序號","品號","品名","規格","庫別名稱","儲位","單位","批號","盤點數量","初盤數量"};
        var rows=new List<string[]>{
            headers,new[]{"990001","00001","X0001","測試商品","60入","假倉","A-01","盒","A1","2","999"},
            new[]{"990001","00002","X0001","測試商品","60入","假倉","A-01","盒","A2","1","0"}
        };
        var parsed=StocktakeQuery.ParseExport("990001",[("Sheet1",rows)]);
        Assert.Equal("2",parsed.Rows[0].BookQty);
        Assert.Equal("1",parsed.Rows[1].BookQty);
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.ParseExport("990002",[("Sheet1",rows)]));
        rows.Add(rows[1]);
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.ParseExport("990001",[("Sheet1",rows)]));
    }

    [Fact]
    public void ReconciliationHandlesIdempotentExistingAndNewBatch()
    {
        var before=new StocktakeLine("00001","X0001","測試品","規格","假倉","A","盒","B1","2");
        var after=new StocktakeLine("00003","X0001","測試品","規格","假倉","B","盒","B9","3");
        var jobs=new[]{
            new StocktakeWriteRow("00001","00001","X0001","B1","2"),
            new StocktakeWriteRow("N0001",null,"X0001","B9","3")
        };
        Assert.True(StocktakeQuery.AlreadyApplied(jobs[0],before));
        Assert.True(StocktakeQuery.AlreadyApplied(jobs[1],after));
        Assert.Empty(StocktakeQuery.Verify(jobs,[before,after],
            new Dictionary<string,string>{{"N0001","00003"}}));
        Assert.Single(StocktakeQuery.Verify(jobs,[before,after with { BookQty="2" }]));
    }

    [Fact]
    public void WholeSheetMustDetectUnexpectedUntouchedBatchChangesAndExtraRows()
    {
        var before=new[]{
            new StocktakeLine("00001","X0001","假品","盒","假倉","A","盒","B1","2"),
            new StocktakeLine("00002","X0001","假品","盒","假倉","A","盒","B2","1")
        };
        var writes=new[]{new StocktakeWriteRow("00001","00001","X0001","B1","3")};
        var after=new[]{before[0] with {BookQty="3"},before[1]};
        Assert.Empty(StocktakeQuery.VerifyEntireSheet(before,after,writes));
        var changed=new[]{after[0],after[1] with {BookQty="9"}};
        Assert.Single(StocktakeQuery.VerifyEntireSheet(before,changed,writes));
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.VerifyEntireSheet(before,
          [..after,new StocktakeLine("00003","X0001","假品","盒","假倉","A","盒","B9","1")],writes));
    }

    [Fact]
    public void WriteCompleteRequiresConfirmedAndAssignedSequence()
    {
        var rows=new[]{new StocktakeWriteRow("N0001",null,"X0001","B9","3")};
        var ok=new StocktakeWriteResult("990001",
            [new StocktakeWriteRowResult("N0001",true,"00003","已寫入")],
            true,[],TimeSpan.FromSeconds(2));
        var body=JsonNode.Parse(ErpAgentProtocol.StocktakeWriteBody(ok,rows,"V1.3.0"))!;
        Assert.Equal("FOUND",body["outcome"]!.GetValue<string>());
        Assert.Equal("00003",body["results"]![0]!["erpSeq"]!.GetValue<string>());
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.StocktakeWriteBody(
            ok with { Results=[new StocktakeWriteRowResult("N0001",true,null,"錯誤")]},rows,"V1.3.0"));
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.StocktakeWriteBody(
            ok with { Mismatches=[new StocktakeMismatch("N0001","00003","X0001","B9","3","2")]},rows,"V1.3.0"));
    }

    [Fact]
    public void SyncCompleteValidatesSubsetAndDoesNotTruncate()
    {
        var valid=new StocktakeSyncResult(
            [new StocktakeSheet("990001",new DateOnly(2026,10,28))],
            [new StocktakeDetail("990001",
              [new StocktakeLine("00001","X0001","測試","規格","假倉","A","盒","B1","1")])],
            TimeSpan.FromSeconds(1));
        var body=JsonNode.Parse(ErpAgentProtocol.StocktakeSyncBody(valid,"V1.3.0",null))!;
        Assert.Equal("FOUND",body["outcome"]!.GetValue<string>());
        Assert.Equal("1",body["details"]![0]!["rows"]![0]!["bookQty"]!.GetValue<string>());
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.StocktakeSyncBody(valid,"V1.3.0","990002"));
    }
    [Fact]
    public void WriteRejectsDuplicateExistingSequenceAndNewBatchBeforeErpMutation()
    {
        var existing=new StocktakeWriteRow("00001","00001","X0001","B1","2");
        var duplicateExisting=existing with {Quantity="3"};
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.ValidateWrite("990001",
            [existing,duplicateExisting]));
        var added=new StocktakeWriteRow("N0001",null,"X0001","B9","2");
        Assert.Throws<InvalidDataException>(()=>StocktakeQuery.ValidateWrite("990001",
            [added,added with {Key="N0002",Quantity="3"}]));
    }

    [Fact]
    public void AlreadyAppliedComparesDecimalValueNotDisplayFormatting()
    {
        var row=new StocktakeWriteRow("00001","00001","X0001","B1","2.00");
        var existing=new StocktakeLine("00001","X0001","測試品","規格","假倉","A","盒","B1","2");
        Assert.True(StocktakeQuery.AlreadyApplied(row,existing));
    }

    [Fact]
    public void CompleteRejectsTwoRowsSharingOneErpSequence()
    {
        var rows=new[]{
            new StocktakeWriteRow("N0001",null,"X0001","B9","3"),
            new StocktakeWriteRow("N0002",null,"X0001","B8","4")
        };
        var result=new StocktakeWriteResult("990001",[
            new StocktakeWriteRowResult("N0001",true,"00003","已新增"),
            new StocktakeWriteRowResult("N0002",true,"00003","已新增")
        ],true,[],TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(()=>ErpAgentProtocol.StocktakeWriteBody(result,rows,"V1.3.0"));
    }

}
