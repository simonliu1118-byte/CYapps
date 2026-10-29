using Xunit;

namespace CYERPAutoQuery.Tests;

public class StocktakeExecutionTests
{
    private static StocktakeLine Row(string seq,string batch,string qty) =>
        new(seq,"X0001","假商品","60入","假倉","A","盒",batch,qty);
    private sealed class FakeDriver : IStocktakeErpDriver
    {
        public List<StocktakeSheet> Sheets=[new("990001",new DateOnly(2026,10,28)),
            new("990002",new DateOnly(2026,10,29))];
        public List<StocktakeLine> Rows=[Row("00001","B1","2"),Row("00002","B2","1")];
        public int Changes,Confirms,Cancels,Reads;
        public bool FaultyUnchanged;
        public bool ReuseWrongSequence;
        public Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StocktakeSheet>>(Sheets);
        public Task<StocktakeDetail> ReadSheetAsync(string sheet,CancellationToken ct)
        {
            Reads++;
            var rows=Rows.ToList();
            if(FaultyUnchanged && Reads>1)
                rows[1]=rows[1] with {BookQty="9"};
            return Task.FromResult(new StocktakeDetail(sheet,rows));
        }
        public Task<StocktakeWriteRowResult> WriteExistingAsync(
            string sheet,StocktakeWriteRow row,CancellationToken ct)
        {
            var i=Rows.FindIndex(x=>x.Seq==row.Seq);
            if(i<0)throw new InvalidDataException("seq missing");
            Changes++;
            Rows[i]=Rows[i] with {BookQty=row.Quantity};
            return Task.FromResult(new StocktakeWriteRowResult(row.Key,true,row.Seq,"已儲存"));
        }
        public Task<StocktakeWriteRowResult> AddBatchAsync(
            string sheet,StocktakeWriteRow row,CancellationToken ct)
        {
            Changes++;
            Rows.Add(Row("00003",row.Batch,row.Quantity));
            return Task.FromResult(new StocktakeWriteRowResult(row.Key,true,
                ReuseWrongSequence?"00002":"00003","已新增"));
        }
        public Task<bool> ConfirmAsync(string sheet,CancellationToken ct)
        {
            Confirms++;
            return Task.FromResult(true);
        }
        public Task CancelUncommittedEditAsync(CancellationToken ct)
        {
            Cancels++;
            return Task.CompletedTask;
        }
    }
    private static ErpAgentJob Write(params StocktakeWriteRow[] rows) =>
        new("synthetic-job","")
        {QueryType=ErpAgentJob.StocktakeWriteType,SheetNo="990001",StocktakeRows=rows};

    [Fact]
    public async Task SyncSkipsKnownAndSingleSheetOverridesSkip()
    {
        var driver=new FakeDriver();
        var service=new StocktakeExecution(driver);
        var list=await service.SyncAsync(new ErpAgentJob("job",""){
            QueryType=ErpAgentJob.StocktakeSyncType,SkipSheets=["990001"]
        },CancellationToken.None);
        Assert.Equal(2,list.Sheets.Count);
        Assert.Single(list.Details);
        Assert.Equal("990002",list.Details[0].SheetNo);
        var selected=await service.SyncAsync(new ErpAgentJob("job",""){
            QueryType=ErpAgentJob.StocktakeSyncType,SkipSheets=["990001"],
            OnlySheet="990001"
        },CancellationToken.None);
        Assert.Single(selected.Details);
        Assert.Equal("990001",selected.Details[0].SheetNo);
        Assert.Equal(0,driver.Changes);
    }
    [Fact]
    public async Task IdempotentRetryAvoidsDuplicateWriteAndNewBatch()
    {
        var driver=new FakeDriver();
        var service=new StocktakeExecution(driver);
        var job=Write(
            new StocktakeWriteRow("00001","00001","X0001","B1","2"),
            new StocktakeWriteRow("N0001",null,"X0001","B9","3"));
        var first=await service.WriteAsync(job,null,CancellationToken.None);
        Assert.True(first.Confirmed);
        Assert.Equal(1,driver.Changes);
        var again=await service.WriteAsync(job,null,CancellationToken.None);
        Assert.True(again.Confirmed);
        Assert.Equal(1,driver.Changes);
        Assert.Equal(2,driver.Confirms);
        Assert.Equal("00003",again.Results[1].ErpSeq);
    }
    [Fact]
    public async Task ExistingBatchWithDifferentCountUsesVerifiedInvi14PathInsteadOfDuplicate()
    {
        var driver=new FakeDriver();
        driver.Rows.Add(Row("00003","B9","1"));
        var service=new StocktakeExecution(driver);
        var result=await service.WriteAsync(Write(
            new StocktakeWriteRow("N0001",null,"X0001","B9","3")),null,CancellationToken.None);
        Assert.True(result.Confirmed);
        Assert.Equal("00003",result.Results[0].ErpSeq);
        Assert.Equal(3,driver.Rows.Count);
        Assert.Equal(1,driver.Changes);
    }

    [Fact]
    public async Task WrongIdentityFailsBeforeAnyModification()
    {
        var driver=new FakeDriver();
        var service=new StocktakeExecution(driver);
        var failed=await service.WriteAsync(
            Write(new StocktakeWriteRow("00001","00001","OTHER","B1","9")),null,CancellationToken.None);
        Assert.False(failed.Confirmed);
        Assert.False(failed.Results[0].Ok);
        Assert.Equal(0,driver.Changes);
        Assert.Equal(0,driver.Confirms);
    }
    [Fact]
    public async Task NewBatchRequiresOriginalBatchManagedItem()
    {
        var driver=new FakeDriver();
        var service=new StocktakeExecution(driver);
        var result=await service.WriteAsync(Write(new StocktakeWriteRow(
            "N0001",null,"UNKNOWN","B9","3")),null,CancellationToken.None);
        Assert.False(result.Confirmed);
        Assert.False(result.Results[0].Ok);
        Assert.Equal(0,driver.Changes);
        Assert.Equal(0,driver.Confirms);
    }

    [Fact]
    public async Task FullSheetMismatchNeverConfirms()
    {
        var driver=new FakeDriver {FaultyUnchanged=true};
        var service=new StocktakeExecution(driver);
        var result=await service.WriteAsync(Write(new StocktakeWriteRow(
            "00001","00001","X0001","B1","3")),null,CancellationToken.None);
        Assert.False(result.Confirmed);
        Assert.Single(result.Mismatches);
        Assert.Equal("00002",result.Mismatches[0].Key);
        Assert.Equal(0,driver.Confirms);
        Assert.Equal(1,driver.Cancels);
    }
    [Fact]
    public async Task NewBatchMustNotReuseUnrelatedErpSequence()
    {
        var driver=new FakeDriver {ReuseWrongSequence=true};
        var service=new StocktakeExecution(driver);
        var result=await service.WriteAsync(Write(
            new StocktakeWriteRow("N0001",null,"X0001","B9","3")),
            null,CancellationToken.None);
        Assert.False(result.Confirmed);
        Assert.Equal(0,driver.Confirms);
        Assert.Equal(1,driver.Cancels);
    }

}
