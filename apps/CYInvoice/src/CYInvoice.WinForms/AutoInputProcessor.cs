using CYInvoice.Core;
using CYInvoice.Core.AutoInput;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Mo;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// This is the only AutoInput dispatcher; it reuses the normal importers and InvoiceService.
// It never bypasses AMEGO reconciliation, makes a separate database writer, or shows UI.
internal sealed class AutoInputProcessor(LocalRepository repository, InvoiceService service)
{
    private sealed record Order(
        string Id, long Amount, string BuyerBan,
        Func<CancellationToken, Task<IssueResult>> Issue);

    public async Task<AutoInputResult> ExecuteAsync(
        AutoInputRequest request, bool issue, CancellationToken cancellationToken = default)
    {
        if (LocalResetCoordinator.HasPendingReset(AppContext.BaseDirectory) ||
            LocalResetCoordinator.IsRevokedDeviceResetPending(AppContext.BaseDirectory))
            throw new InvalidOperationException("CYInvoice 裝置或資料正等待重設，不能處理 AutoInput 請求");

        var settings = repository.Settings.LoadOrCreate();
        var environment = settings.Environment;
        if (environment is not (Environments.Production or Environments.Test))
            throw new InvalidOperationException("CYInvoice 環境設定無效");

        IReadOnlyList<Order> imported = request.Source switch
        {
            "mo" => (await PlatformImportReader.ReadMoOrderExportAsync(
                request.File, repository.Settings.MoPassword(settings), cancellationToken).ConfigureAwait(false))
                .Select(order => new Order(order.OrderId, order.TotalAmount, order.BuyerBan,
                    token => service.IssueMoAsync(order, token))).ToArray(),
            "coupang" => (await PlatformImportReader.ReadCoupangExportAsync(
                request.File, cancellationToken).ConfigureAwait(false))
                .Select(order => new Order(order.OrderId, order.TotalAmount, order.BuyerBan,
                    token => service.IssueCoupangAsync(order, token))).ToArray(),
            _ => throw new InvalidDataException("不支援的 AutoInput 來源")
        };
        if (imported.Count > AutoInputContract.MaxOrderCount ||
            imported.Any(order => string.IsNullOrWhiteSpace(order.Id)) ||
            imported.GroupBy(order => order.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("匯入訂單過多、訂單編號空白或重複");

        var source = request.Source == "mo" ? InvoiceSources.Mo : InvoiceSources.Coupang;
        var selected = request.OrderIds is null
            ? imported.Select(order => order.Id).ToArray() : request.OrderIds.ToArray();
        var lookup = imported.ToDictionary(order => order.Id, StringComparer.Ordinal);
        var results = new List<AutoInputOrderResult>(selected.Length);
        foreach (var orderId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!lookup.TryGetValue(orderId, out var order))
            {
                results.Add(new(orderId, "skipped", Message: "匯入檔找不到指定訂單"));
                continue;
            }
            if (repository.Settings.LoadOrCreate().Environment != environment ||
                LocalResetCoordinator.HasPendingReset(AppContext.BaseDirectory) ||
                LocalResetCoordinator.IsRevokedDeviceResetPending(AppContext.BaseDirectory))
            {
                results.Add(new(orderId, "unknown", Message: "處理期間環境或裝置狀態已變更，已停止"));
                break;
            }

            var existing = AutoInputRecordDecision.Classify(
                repository.Invoices.LoadOrCreate(), source, order.Id, order.Amount, order.BuyerBan, environment);
            if (existing is not null)
            {
                results.Add(!issue && existing.Status == "failed"
                    ? existing with { Status = "unknown" } : existing);
                continue;
            }
            if (!issue)
            {
                results.Add(new(orderId, "skipped", Message: "本機尚無這張訂單的發票紀錄"));
                continue;
            }
            try
            {
                // Same matrix as interactive issue controls. The service independently gates AMEGO.
                var state = await repository.Connections.CheckAsync(cancellationToken).ConfigureAwait(false);
                if (!state.AmegoAvailable || state.BlockAll)
                {
                    results.Add(new(orderId, "unknown", Message: "服務連線或裝置權限不足，未送出"));
                    continue;
                }
                var issued = await order.Issue(cancellationToken).ConfigureAwait(false);
                var record = issued.Record;
                if (record.Environment != environment || record.InvoiceState != InvoiceStates.Opened ||
                    record.InvoiceNumber.Trim().Length == 0 || record.InvoiceDate.Trim().Length == 0 ||
                    record.ErrorMessage.Trim().Length != 0 || record.Amount != order.Amount)
                    results.Add(new(orderId, "unknown", Message: "開立回覆尚未完成嚴格確認，請在 CYInvoice 回查"));
                else
                    results.Add(new(orderId, "issued", record.InvoiceNumber, record.InvoiceDate,
                        record.BuyerIdentifier, record.BuyerName, record.Amount));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (UnknownInvoiceResultException)
            {
                results.Add(new(orderId, "unknown", Message: "光貿開立結果不明，禁止重送"));
            }
            catch (LocalPersistenceException)
            {
                results.Add(new(orderId, "unknown", Message: "光貿可能已開立但本機保存失敗，禁止重送"));
            }
            catch (Exception)
            {
                // A transport error may have occurred AFTER AMEGO accepted an issue request.
                // Never claim a safe retry based solely on exception type.
                results.Add(new(orderId, "unknown", Message: "開立／核對失敗，需在 CYInvoice 人工確認，禁止直接重送"));
            }
        }
        // The worker must never disguise skipped orders after a mid-batch abort.
        foreach (var remaining in selected.Skip(results.Count))
            results.Add(new(remaining, "skipped", Message: "批次已停止，尚未處理"));

        return new AutoInputResult(
            AutoInputContract.ResultSchema, request.RequestId,
            ApplicationVersion.Read() + "+0",
            environment == Environments.Production ? "production" : "test",
            DateTimeOffset.Now.ToString("O"), results);
    }
}
