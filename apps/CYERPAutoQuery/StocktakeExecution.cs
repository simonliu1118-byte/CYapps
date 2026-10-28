namespace CYERPAutoQuery;

/// <summary>
/// The sole orchestration owner for stocktake Agent jobs. A Win32 implementation
/// must provide fail-closed operations; this coordinator never guesses ERP
/// control positions or confirms an unverified sheet.
/// </summary>
internal interface IStocktakeErpDriver
{
    Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct);
    Task<StocktakeDetail> ReadSheetAsync(string sheetNo,CancellationToken ct);
    /// <summary>Must verify sheet, seq, item, batch and current mode before any edit.</summary>
    Task<StocktakeWriteRowResult> WriteExistingAsync(
        string sheetNo,StocktakeWriteRow row,CancellationToken ct);
    /// <summary>Must verify sheet and batch uniqueness before inserting.</summary>
    Task<StocktakeWriteRowResult> AddBatchAsync(
        string sheetNo,StocktakeWriteRow row,CancellationToken ct);
    /// <summary>Must read the exact sheet after clicking INVI06 confirm.</summary>
    Task<bool> ConfirmAsync(string sheetNo,CancellationToken ct);
    /// <summary>Cancel an uncommitted edit on failure; never cancel a confirmed document.</summary>
    Task CancelUncommittedEditAsync(CancellationToken ct);
}

internal sealed class StocktakeExecution(IStocktakeErpDriver driver)
{
    public async Task<StocktakeSyncResult> SyncAsync(ErpAgentJob job,CancellationToken ct)
    {
        if(job.QueryType!=ErpAgentJob.StocktakeSyncType)
            throw new InvalidOperationException("不是盤點同步工作。");
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var sheets=await driver.ListSheetsAsync(ct);
        if(sheets.Count>StocktakeQuery.MaxSheets ||
            sheets.Select(x=>x.SheetNo).Distinct(StringComparer.Ordinal).Count()!=sheets.Count)
            throw new InvalidDataException("ERP 清單超過上限或底稿編號重複。");
        var skips=job.SkipSheets.ToHashSet(StringComparer.Ordinal);
        var targets=job.OnlySheet is null
            ? sheets.Where(x=>!skips.Contains(x.SheetNo)).ToArray()
            : sheets.Where(x=>x.SheetNo==job.OnlySheet).ToArray();
        if(job.OnlySheet is not null && targets.Length!=1)
            throw new InvalidDataException("ERP F2 清單找不到要重新整理的底稿。");
        var details=new List<StocktakeDetail>(targets.Length);
        foreach(var sheet in targets)
        {
            ct.ThrowIfCancellationRequested();
            var detail=await driver.ReadSheetAsync(sheet.SheetNo,ct);
            if(detail.SheetNo!=sheet.SheetNo || detail.Rows.Count>StocktakeQuery.MaxRowsPerSheet)
                throw new InvalidDataException("ERP 讀出的底稿編號或明細列數不正確。");
            details.Add(detail);
        }
        return new StocktakeSyncResult(sheets,details,watch.Elapsed);
    }

    public async Task<StocktakeWriteResult> WriteAsync(ErpAgentJob job,
        Action<int,int>? progress,CancellationToken ct)
    {
        if(job.QueryType!=ErpAgentJob.StocktakeWriteType ||
            job.SheetNo is not { } sheet)
            throw new InvalidOperationException("不是盤點寫回工作。");
        var rows=StocktakeQuery.ValidateWrite(sheet,job.StocktakeRows);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var results=new List<StocktakeWriteRowResult>();
        var assigned=new Dictionary<string,string>(StringComparer.Ordinal);
        var mismatches=new List<StocktakeMismatch>();
        var started=false;
        StocktakeWriteResult Failure(string reason)
        {
            var failed=results.ToList();
            foreach(var missing in rows.Skip(failed.Count))
                failed.Add(new StocktakeWriteRowResult(missing.Key,false,null,reason));
            return new StocktakeWriteResult(sheet,failed,false,mismatches,watch.Elapsed);
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            var original=await driver.ReadSheetAsync(sheet,ct);
            if(original.SheetNo!=sheet)
                throw new InvalidDataException("ERP 開啟的底稿與寫回目標不符。");
            var bySeq=original.Rows.ToDictionary(x=>x.Seq,StringComparer.Ordinal);
            // Before a write, assert the identity of EVERY existing row.
            // An injected/changed batch is not grounds to silently overwrite it.
            foreach(var row in rows.Where(x=>x.Seq is not null))
            {
                if(!bySeq.TryGetValue(row.Seq!,out var observed) ||
                    observed.ItemCode!=row.ItemCode || observed.Batch!=row.Batch)
                    throw new InvalidDataException("原盤點底稿序號、品號或批號不符，已停止寫入。");
            }
            started=true;
            for(var i=0;i<rows.Count;i++)
            {
                ct.ThrowIfCancellationRequested();
                var row=rows[i];
                StocktakeWriteRowResult result;
                if(row.Seq is { } seq)
                {
                    var old=bySeq[seq];
                    result=StocktakeQuery.AlreadyApplied(row,old)
                        ? new StocktakeWriteRowResult(row.Key,true,seq,"原數量已一致")
                        : await driver.WriteExistingAsync(sheet,row,ct);
                }
                else
                {
                    // Defense in depth: CY Web permits new batches only for an
                    // item already batch-managed in this exact ERP stocktake.
                    if(!original.Rows.Any(x=>x.ItemCode==row.ItemCode && x.Batch.Length>0))
                        throw new InvalidDataException("此品號在原底稿沒有批號，禁止自行新增批號。");
                    var exists=original.Rows.Where(x=>x.ItemCode==row.ItemCode &&
                        x.Batch==row.Batch).ToArray();
                    if(exists.Length>1)
                        throw new InvalidDataException("ERP 相同品號批號有重複列，不能自動新增。");
                    if(exists.Length==1)
                    {
                        var present=exists[0];
                        assigned[row.Key]=present.Seq;
                        if(StocktakeQuery.AlreadyApplied(row,present))
                            result=new StocktakeWriteRowResult(row.Key,true,present.Seq,"新增批號已存在且數量一致");
                        else
                        {
                            started=true;
                            var updated=await driver.WriteExistingAsync(sheet,row with
                            {
                                Key=present.Seq,Seq=present.Seq
                            },ct);
                            result=new StocktakeWriteRowResult(row.Key,updated.Ok,present.Seq,updated.Message);
                        }
                    }
                    else result=await driver.AddBatchAsync(sheet,row,ct);
                }
                if(result.Ok)
                {
                    if(!StocktakeQuery.IsSeq(result.ErpSeq) ||
                        (row.Seq is not null && row.Seq!=result.ErpSeq))
                        throw new InvalidDataException("ERP 寫入後未返回可驗證底稿序號。");
                    assigned[row.Key]=result.ErpSeq!;
                }
                // Some lines may have already changed in ERP if a later line fails.
                // Never auto-retry or confirm partial writes.
                results.Add(result with {Key=row.Key});
                progress?.Invoke(i+1,rows.Count);
                if(!result.Ok)
                    return Failure("有盤點列寫入失敗，其他列未執行。");
            }
            ct.ThrowIfCancellationRequested();
            var final=await driver.ReadSheetAsync(sheet,ct);
            if(final.SheetNo!=sheet)
                throw new InvalidDataException("INVR11 核對時底稿編號已改變，不能確認。");
            mismatches.AddRange(StocktakeQuery.VerifyEntireSheet(
                original.Rows,final.Rows,rows,assigned));
            if(mismatches.Count>0)
                return Failure("全表核對不一致，不得確認。");
            if(!await driver.ConfirmAsync(sheet,ct))
                return Failure("ERP 未回報盤點單確認完成。");
            started=false; // confirmed: do not send cancel to a committed document
            return new StocktakeWriteResult(sheet,results,true,[],watch.Elapsed);
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception) { return Failure("ERP 操作未完成，請人工核對後再重試。"); }
        finally
        {
            // Driver is responsible for ensuring a confirmed sheet cannot be
            // cancelled or modified here. No "undo" of a committed ERP write.
            if(started)
                await driver.CancelUncommittedEditAsync(CancellationToken.None);
        }
    }
}
