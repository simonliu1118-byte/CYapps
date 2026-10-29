namespace CYERPAutoQuery;

/// <summary>
/// Stocktake Agent integration. The actual ERP adapter must be explicitly
/// supplied after Windows UI acceptance. Never advertise these job types
/// merely because the coordinator and network protocol are present.
/// </summary>
internal sealed partial class MainForm
{
    private IStocktakeErpDriver? _stocktakeDriver;

    private async Task<(bool Found, string Body)> RunWebStocktakeAsync(
        ErpAgentJob job, CancellationToken cancellationToken)
    {
        if (job.QueryType is not (ErpAgentJob.StocktakeSyncType or ErpAgentJob.StocktakeWriteType))
            throw new NotSupportedException("不是盤點 Agent 工作。");
        var driver = _stocktakeDriver ??
            throw new NotSupportedException("盤點 ERP 操作尚未通過 Windows 實機驗收，禁止執行。");
        var coordinator = new StocktakeExecution(driver);
        if (job.QueryType == ErpAgentJob.StocktakeSyncType)
        {
            var result = await RunAutomationAsync(cancellationToken,
                (status, ct) => coordinator.SyncAsync(job, ct),
                result =>
                {
                    SetStatus($"ERP：盤點更新完成，明細 {result.Details.Count} 張");
                    return result.Details.Count > 0 ? AutomationOutcome.Done : AutomationOutcome.NotFound;
                });
            return (result.Details.Count > 0,
                ErpAgentProtocol.StocktakeSyncBody(result, AppVersionInfo.Display, job.OnlySheet));
        }

        var report = WebProgress(job.Id, cancellationToken);
        var write = await RunAutomationAsync(cancellationToken,
            (status, ct) => coordinator.WriteAsync(job,
                (done, total) =>
                {
                    status.Report($"ERP：盤點寫回 已寫入 {done} / {total} 列");
                    report("STOCK", done, total);
                }, ct),
            result =>
            {
                SetStatus(result.Confirmed
                    ? $"ERP：盤點 {result.SheetNo} 已核對並確認"
                    : $"ERP：盤點 {result.SheetNo} 未確認，請檢查失敗列");
                return result.Confirmed ? AutomationOutcome.Done : AutomationOutcome.NotFound;
            });
        return (write.Confirmed,
            ErpAgentProtocol.StocktakeWriteBody(write, job.StocktakeRows, AppVersionInfo.Display));
    }
}
