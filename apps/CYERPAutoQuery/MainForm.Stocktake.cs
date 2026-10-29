namespace CYERPAutoQuery;

/// <summary>庫存盤點 Agent jobs (WO-1.3.0): STOCKTAKE_SYNC reads sheets, STOCKTAKE_WRITE writes, checks and confirms one.</summary>
internal sealed partial class MainForm
{
    private async Task<(bool Found, string Body)> RunWebStocktakeAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        var coordinator = new StocktakeExecution(new StocktakeWin32Driver(_service));
        if (job.QueryType == ErpAgentJob.StocktakeSyncType)
        {
            var result = await RunAutomationAsync(cancellationToken,
                (status, ct) => coordinator.SyncAsync(job, ct),
                result =>
                {
                    SetStatus($"ERP：盤點更新完成，明細 {result.Details.Count} 張");
                    return result.Details.Count > 0 ? AutomationOutcome.Done : AutomationOutcome.NotFound;
                });
            return (result.Details.Count > 0, ErpAgentProtocol.StocktakeSyncBody(result, AppVersionInfo.Display));
        }

        var report = WebProgress(job.Id, cancellationToken);
        var write = await RunAutomationAsync(cancellationToken,
            (status, ct) => coordinator.WriteAsync(job,
                (done, total) =>
                {
                    status.Report($"ERP：盤點寫回 {done} / {total} 列");
                    report("STOCK", done, total);
                }, ct),
            result =>
            {
                SetStatus(result.Confirmed
                    ? $"ERP：盤點 {result.SheetNo} 已寫回並確認"
                    : $"ERP：盤點 {result.SheetNo} 未確認，請看失敗列");
                return result.Confirmed ? AutomationOutcome.Done : AutomationOutcome.NotFound;
            });
        return (write.Confirmed, ErpAgentProtocol.StocktakeWriteBody(write, AppVersionInfo.Display));
    }
}
