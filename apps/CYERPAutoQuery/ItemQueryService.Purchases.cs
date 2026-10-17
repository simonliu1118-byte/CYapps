using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// 進貨單查詢 (user, 2026-10-18): a supplier's 進貨單 from PURR18 進貨單明細表, driven like COPR15
/// (<see cref="RunReportAsync"/>). 基本選項: 廠商代號 起＝迄＝code (ticking 區間選擇 when needed),
/// 單據日期 as COPR15; 進貨單號 and 進貨庫別 must be empty and 選擇日期 must be 單據日. 進階選項:
/// 確認狀態／結帳狀態 「全部」, and no 「僅列印…」 box ticked. Then 退貨單 from PURR22 退貨單明細表
/// (user, 2026-10-18: it has no 廠商 condition, so the same 單據日期 range is exported whole and
/// the supplier's 退貨單 are kept; with no dates that is every 退貨單, a few hundred rows).
/// </summary>
internal sealed partial class ItemQueryService
{
    private const uint BM_GETCHECK = 0x00F0;

    public Task<PurchaseQueryResult> QueryPurchasesAsync(PurchaseQuery query, IProgress<string> progress, CancellationToken cancellationToken) =>
        GuardErpMessagesAsync(() => QueryPurchasesCoreAsync(query, progress, cancellationToken));

    private async Task<PurchaseQueryResult> QueryPurchasesCoreAsync(PurchaseQuery query, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var report = PurchaseReportLayout.Report;
        var describe = $"廠商 {query.SupplierCode}，{Invq01Layout.ErpDate(query.From)}～{Invq01Layout.ErpDate(query.To)}";
        var sheets = await RunReportAsync(report, describe, async (dialog, ct) =>
        {
            await FillPurchaseBasicAsync(dialog, report, query, ct);
            await FillReportAdvancedAsync(dialog, report, ct);
            EnsureNotChecked(dialog, report, c => c.StartsWith(PurchaseReportLayout.OnlyPrintPrefix, StringComparison.Ordinal));
            await VerifyReportAdvancedAsync(dialog, report, ct);
        }, progress, cancellationToken);
        var orders = PurchaseExport.Parse(sheets, query).ToList();
        _log.Info("purchase", $"PURR18 supplier={query.SupplierCode} orders={orders.Count} lines={orders.Sum(o => o.Lines.Count)}");

        var returns = PurchaseReportLayout.Returns;
        var returnSheets = await RunReportAsync(returns, $"{Invq01Layout.ErpDate(query.From)}～{Invq01Layout.ErpDate(query.To)}，全部廠商", async (dialog, ct) =>
        {
            await ShowReportTabAsync(dialog, returns, SalesReportLayout.BasicTab, ct);
            EnsureGroupEmpty(dialog, returns, PurchaseReportLayout.ReturnOrderGroup);
            await FillReportDatesAsync(dialog, returns, query.Dates, ct);
            await FillOptionalStatusAsync(dialog, returns, ct);
        }, progress, cancellationToken);
        var returned = PurchaseExport.ParseReturns(returnSheets, query);
        _log.Info("purchase", $"PURR22 supplier={query.SupplierCode} returns={returned.Count} ms={watch.ElapsedMilliseconds}");
        orders.AddRange(returned);
        return new PurchaseQueryResult(query, orders.OrderBy(o => o, PurchaseExport.Newest).ToList(), watch.Elapsed);
    }

    /// <summary>
    /// PURR22's 進階選項 was not shown to us: when it has 確認狀態／結帳狀態 drop-downs they are set to
    /// 「全部」 as in PURR18; without them the LOG says so and the defaults stay.
    /// </summary>
    private async Task FillOptionalStatusAsync(nint dialog, ErpReport report, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, SalesReportLayout.AdvancedTab, cancellationToken);
        var sheet = await WaitForTabSheetAsync(dialog, SalesReportLayout.AdvancedTab, AfterActionWaitMs, cancellationToken, visible: true);
        var combos = sheet is null ? 0 : Win32Automation.InputControls(sheet.Handle, visibleOnly: true).Count(c => Invi02Layout.IsCombo(c.ClassName));
        _log.Info("report", $"{report.Code} 進階選項 combos={combos}");
        EnsureNotChecked(dialog, report, c => c.StartsWith(PurchaseReportLayout.OnlyPrintPrefix, StringComparison.Ordinal));
        if (combos < 2)
        {
            LogChildren(dialog, report.Code);
            return;
        }
        await FillReportAdvancedAsync(dialog, report, cancellationToken);
        await VerifyReportAdvancedAsync(dialog, report, cancellationToken);
    }

    private async Task FillPurchaseBasicAsync(nint dialog, ErpReport report, PurchaseQuery query, CancellationToken cancellationToken)
    {
        await ShowReportTabAsync(dialog, report, SalesReportLayout.BasicTab, cancellationToken);
        EnsureGroupEmpty(dialog, report, PurchaseReportLayout.OrderGroup);
        EnsureGroupEmpty(dialog, report, PurchaseReportLayout.WarehouseGroup);
        EnsureNotChecked(dialog, report, c => c == PurchaseReportLayout.AcceptDateRadio);
        var boxes = await OpenRangeAsync(dialog, report, PurchaseReportLayout.SupplierGroup, cancellationToken);
        await SetReportTextAsync(dialog, boxes[0], query.SupplierCode, $"{report.Title}的廠商代號起", cancellationToken);
        await SetReportTextAsync(dialog, boxes[1], query.SupplierCode, $"{report.Title}的廠商代號迄", cancellationToken);
        await FillReportDatesAsync(dialog, report, query.Dates, cancellationToken);
    }

    /// <summary>
    /// A visible check box or radio button whose caption matches must not be checked (it would narrow
    /// the report). Read with BM_GETCHECK; a control that does not answer reads as unchecked, and
    /// every match is logged so a real-machine LOG shows what was read.
    /// </summary>
    private void EnsureNotChecked(nint dialog, ErpReport report, Func<string, bool> caption)
    {
        foreach (var c in Win32Automation.EnumerateChildren(dialog).Where(c => c.Visible && caption(Invi02Layout.NormalizeCaption(c.Text))))
        {
            var state = NativeMethods.QueryNumber(c.Handle, BM_GETCHECK);
            _log.Info("report", $"{report.Code} 「{c.Text}」 class={c.ClassName} checked={state?.ToString() ?? "-"}");
            if (state == 1)
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"{report.Title}的「{Invi02Layout.NormalizeCaption(c.Text)}」被選取，會讓報表少列資料。\n請在 ERP 取消後再查詢。");
        }
    }
}
