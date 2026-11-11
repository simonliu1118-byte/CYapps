namespace CYERPAutoQuery;

/// <summary>The ERP steps of 庫存盤點; the Win32 one is <see cref="StocktakeWin32Driver"/>, tests use a fake.</summary>
internal interface IStocktakeErpDriver
{
    /// <summary>INVR11 「選擇盤點底稿編號」 F2 list; INVR11 stays open for <see cref="ReadSheetAsync"/>.</summary>
    Task<IReadOnlyList<StocktakeSheet>> ListSheetsAsync(CancellationToken ct);
    /// <summary>Closes INVR11 after the list and reads.</summary>
    Task CloseReportAsync();
    /// <summary>INVR11 report of one sheet (儲位／批號／盤點數量 shown) exported to Excel.</summary>
    Task<StocktakeDetail> ReadSheetAsync(string sheetNo, CancellationToken ct);
    /// <summary>INVI14: one existing line's 盤點數量 → 儲存.</summary>
    Task WriteExistingAsync(string sheetNo, StocktakeLine line, string quantity, CancellationToken ct);
    /// <summary>INVI06: a new line (品號＋批號＋盤點數量) at the end of the sheet → 儲存.</summary>
    Task AddBatchAsync(string sheetNo, StocktakeWriteRow row, CancellationToken ct);
    /// <summary>INVI06 確認; true when ERP shows the sheet confirmed.</summary>
    Task<bool> ConfirmAsync(string sheetNo, CancellationToken ct);
}

/// <summary>
/// STOCKTAKE_SYNC and STOCKTAKE_WRITE (WO-1.3.0, user 2026-10-29): after the second 確認 in CY Web
/// every differing line is written (INVI14), every added batch inserted (INVI06), the sheet read
/// again from INVR11 and only when everything matches is it confirmed in INVI06.
/// </summary>
internal sealed class StocktakeExecution(IStocktakeErpDriver driver)
{
    public async Task<StocktakeSyncResult> SyncAsync(ErpAgentJob job, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var sheets = await driver.ListSheetsAsync(ct);
            IEnumerable<StocktakeSheet> targets;
            if (job.OnlySheet is { } only)
                targets = [sheets.FirstOrDefault(s => s.SheetNo == only)
                           ?? throw new InvalidDataException($"ERP 的盤點底稿清單沒有 {only}。")];
            else
                targets = sheets.Where(s => !job.SkipSheets.Contains(s.SheetNo));
            var details = new List<StocktakeDetail>();
            foreach (var sheet in targets)
                details.Add(await driver.ReadSheetAsync(sheet.SheetNo, ct));
            return new StocktakeSyncResult(sheets, details, watch.Elapsed);
        }
        finally
        {
            await driver.CloseReportAsync();
        }
    }

    public async Task<StocktakeWriteResult> WriteAsync(ErpAgentJob job, Action<int, int>? progress, CancellationToken ct)
    {
        var sheet = job.SheetNo ?? throw new InvalidDataException("盤點寫回缺底稿編號。");
        var rows = StocktakeQuery.ValidateWrite(sheet, job.StocktakeRows);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<StocktakeWriteRowResult>();

        StocktakeWriteResult Failed(IReadOnlyList<StocktakeMismatch> mismatches) =>
            new(sheet, results.Concat(rows.Skip(results.Count).Select(r => new StocktakeWriteRowResult(r.Key, false, r.Seq, "未執行"))).ToList(),
                false, mismatches, watch.Elapsed);

        var original = await driver.ReadSheetAsync(sheet, ct);
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = rows[i];
            var line = StocktakeQuery.Find(original.Rows, row);
            try
            {
                if (row.Seq is not null && (line is null || line.ItemCode != row.ItemCode || line.Batch != row.Batch))
                    throw new InvalidDataException($"ERP 底稿序號 {row.Seq} 已不是 {row.ItemCode} 批號 {row.Batch}。");
                if (StocktakeQuery.AlreadyApplied(row, line))
                    results.Add(new StocktakeWriteRowResult(row.Key, true, line!.Seq, "ERP 已是此數量"));
                else if (line is not null)
                {
                    // An added batch already on the sheet (an earlier run inserted it) is written in place.
                    await driver.WriteExistingAsync(sheet, line, row.Quantity, ct);
                    results.Add(new StocktakeWriteRowResult(row.Key, true, line.Seq, "已寫入"));
                }
                else
                {
                    await driver.AddBatchAsync(sheet, row, ct);
                    results.Add(new StocktakeWriteRowResult(row.Key, true, null, "已新增"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new StocktakeWriteRowResult(row.Key, false, row.Seq, ex.Message));
                return Failed([]);
            }
            progress?.Invoke(i + 1, rows.Count);
        }

        // INVR11 again: every written line must show its quantity before 確認 (user, 2026-10-29).
        var final = await driver.ReadSheetAsync(sheet, ct);
        for (var i = 0; i < results.Count; i++)
            if (results[i].ErpSeq is null)
                results[i] = StocktakeQuery.Find(final.Rows, rows[i]) is { } added
                    ? results[i] with { ErpSeq = added.Seq }
                    : results[i] with { Ok = false, Message = "INVR11 找不到新增的批號" };
        var mismatches = StocktakeQuery.Verify(rows, final.Rows);
        if (mismatches.Count > 0) return Failed(mismatches);
        var confirmed = await driver.ConfirmAsync(sheet, ct);
        return new StocktakeWriteResult(sheet, results, confirmed, [], watch.Elapsed);
    }
}
