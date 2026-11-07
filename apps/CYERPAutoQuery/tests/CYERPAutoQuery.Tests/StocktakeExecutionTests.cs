using Xunit;

namespace CYERPAutoQuery.Tests;

public class StocktakeExecutionTests
{
    /// <summary>A fake ERP sheet: writes change it, INVR11 reads it.</summary>
    private sealed class FakeErp : IStocktakeErpDriver
    {
        public List<StocktakeLine> Lines = [];
        public List<string> Calls = [];
        public bool Confirmed;
        public string? FailAdd;

        public Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StocktakeSheet>>([new("990001", new DateOnly(2026, 10, 1)), new("990002", new DateOnly(2026, 10, 2))]);
        public Task<StocktakeDetail> ReadSheetAsync(string sheetNo, CancellationToken ct)
        {
            Calls.Add("read " + sheetNo);
            return Task.FromResult(new StocktakeDetail(sheetNo, Lines.ToList()));
        }
        public Task WriteExistingAsync(string sheetNo, StocktakeLine line, string quantity, CancellationToken ct)
        {
            Calls.Add("write " + line.Seq);
            Lines[Lines.FindIndex(l => l.Seq == line.Seq)] = line with { BookQty = quantity };
            return Task.CompletedTask;
        }
        public Task AddBatchAsync(string sheetNo, StocktakeWriteRow row, CancellationToken ct)
        {
            Calls.Add("add " + row.Batch);
            if (FailAdd == row.Batch) throw new InvalidOperationException("INVI06 按「下」後沒有產生新的一列");
            Lines.Add(new StocktakeLine($"{Lines.Count + 1:00000}", row.ItemCode, "", "", "", "", "", row.Batch, row.Quantity));
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmAsync(string sheetNo, CancellationToken ct)
        {
            Calls.Add("confirm");
            Confirmed = true;
            return Task.FromResult(true);
        }
    }

    private static StocktakeLine Line(string seq, string batch, string qty) => new(seq, "X0001", "測試品", "", "假倉", "", "盒", batch, qty);
    private static ErpAgentJob Write(params StocktakeWriteRow[] rows) =>
        new("job", "") { QueryType = ErpAgentJob.StocktakeWriteType, SheetNo = "990001", StocktakeRows = rows };

    [Fact]
    public async Task SyncReadsUnskippedSheetsOrOnlyTheOneAsked()
    {
        var erp = new FakeErp();
        var run = new StocktakeExecution(erp);
        var all = await run.SyncAsync(new ErpAgentJob("j", "") { QueryType = ErpAgentJob.StocktakeSyncType, SkipSheets = ["990001"] }, default);
        Assert.Equal(["990002"], all.Details.Select(d => d.SheetNo));
        var one = await run.SyncAsync(new ErpAgentJob("j", "") { QueryType = ErpAgentJob.StocktakeSyncType, SkipSheets = ["990001"], OnlySheet = "990001" }, default);
        Assert.Equal(["990001"], one.Details.Select(d => d.SheetNo));
    }

    [Fact]
    public async Task WritesChangedLinesAddsBatchesChecksAndConfirms()
    {
        var erp = new FakeErp { Lines = [Line("00001", "B1", "2"), Line("00002", "B2", "1")] };
        var progress = new List<int>();
        var result = await new StocktakeExecution(erp).WriteAsync(Write(
            new("00001", "00001", "X0001", "B1", "1"),
            new("00002", "00002", "X0001", "B2", "1"),
            new("N0001", null, "X0001", "B9", "4")), (done, _) => progress.Add(done), default);

        Assert.True(result.Confirmed);
        Assert.Equal(["read 990001", "write 00001", "add B9", "read 990001", "confirm"], erp.Calls);
        Assert.Equal("00003", result.Results[2].ErpSeq);
        Assert.Equal([1, 2, 3], progress);
    }

    [Fact]
    public async Task RetryAfterAnAddWritesTheAddedLineInsteadOfAddingAgain()
    {
        var erp = new FakeErp { Lines = [Line("00001", "B1", "2"), Line("00002", "B9", "3")] };
        var result = await new StocktakeExecution(erp).WriteAsync(Write(new StocktakeWriteRow("N0001", null, "X0001", "B9", "4")), null, default);
        Assert.True(result.Confirmed);
        Assert.Equal(["read 990001", "write 00002", "read 990001", "confirm"], erp.Calls);
    }

    [Fact]
    public async Task AFailedLineStopsWithoutConfirming()
    {
        var erp = new FakeErp { Lines = [Line("00001", "B1", "2")], FailAdd = "B9" };
        var result = await new StocktakeExecution(erp).WriteAsync(Write(
            new("N0001", null, "X0001", "B9", "4"),
            new("00001", "00001", "X0001", "B1", "5")), null, default);
        Assert.False(result.Confirmed);
        Assert.False(erp.Confirmed);
        Assert.Contains("新的一列", result.Results[0].Message);
        Assert.Equal("未執行", result.Results[1].Message);
    }

    [Fact]
    public async Task ASequenceNowHoldingAnotherBatchIsNotWritten()
    {
        var erp = new FakeErp { Lines = [Line("00001", "B7", "2")] };
        var result = await new StocktakeExecution(erp).WriteAsync(Write(new StocktakeWriteRow("00001", "00001", "X0001", "B1", "5")), null, default);
        Assert.False(result.Confirmed);
        Assert.DoesNotContain("write 00001", erp.Calls);
    }
}
