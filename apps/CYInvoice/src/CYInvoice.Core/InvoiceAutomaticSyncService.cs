using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceAutomaticSyncService
{
    internal const string DailyReconcileScope = "daily-two-period";
    private const string VoidPendingIssueType = "作廢結果待確認";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncService syncService;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceRetentionService retentionService;
    private readonly InvoiceVoidService voidService;
    private readonly Func<DateTimeOffset> now;

    public InvoiceAutomaticSyncService(
        LocalRepository repository,
        InvoiceSyncService syncService,
        Func<DateTimeOffset>? now = null,
        InvoiceVoidService? voidService = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        retentionService = new InvoiceRetentionService(repository);
        this.voidService = voidService ?? new InvoiceVoidService(repository, now: now);
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceSyncResult> SyncRecentAsync(CancellationToken cancellationToken = default)
    {
        var result = await syncService.SyncRecentAsync(cancellationToken).ConfigureAwait(false);
        return await ReconcilePendingVoidsAsync(result, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InvoiceSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var current = now();
        var today = DateOnly.FromDateTime(current.DateTime);
        var account = CurrentAccount();
        var accountKey = account.Environment + "|" + account.SellerInvoice;
        var lastSuccess = stateStore.LastSuccess(accountKey, DailyReconcileScope);
        InvoiceSyncResult result;

        if (lastSuccess is not null &&
            DateOnly.FromDateTime(lastSuccess.Value.ToOffset(current.Offset).DateTime) == today)
        {
            result = await syncService.SyncRecentAsync(cancellationToken).ConfigureAwait(false);
            return await ReconcilePendingVoidsAsync(result, cancellationToken).ConfigureAwait(false);
        }

        var startDate = account.Environment == Environments.Test ? today : TwoPeriodRangeStart(today);
        result = await syncService.SyncRangeAsync(startDate, today, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        stateStore.SetLastSuccess(accountKey, DailyReconcileScope, now());

        if (result.Problems.Count == 0)
        {
            try
            {
                var retention = retentionService.Prune(today);
                if (retention.Problems.Count != 0)
                {
                    result = result with
                    {
                        Problems = retention.Problems
                            .Select(problem => "本機舊資料快取清理：" + problem)
                            .ToArray(),
                    };
                }
            }
            catch (Exception error)
            {
                result = result with
                {
                    Problems = [$"本機舊資料清理失敗：{error.Message}"],
                };
            }
        }

        return await ReconcilePendingVoidsAsync(result, cancellationToken).ConfigureAwait(false);
    }

    internal static DateOnly TwoPeriodRangeStart(DateOnly today)
    {
        var currentPeriodStartMonth = ((today.Month - 1) / 2 * 2) + 1;
        var currentPeriodStart = new DateOnly(today.Year, currentPeriodStartMonth, 1);
        return currentPeriodStart.AddMonths(-2);
    }

    private async Task<InvoiceSyncResult> ReconcilePendingVoidsAsync(
        InvoiceSyncResult result,
        CancellationToken cancellationToken)
    {
        var account = CurrentAccount();
        var accountKey = account.Environment + "|" + account.SellerInvoice;
        var problems = result.Problems.ToList();
        var pending = repository.Invoices.LoadOrCreate()
            .Where(record => InvoiceVoidService.HasPendingMarker(record))
            .Where(record => string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            .ToArray();

        foreach (var record in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = record.InvoiceNumber.Trim();
            var orderId = EffectiveOrderId(record);
            try
            {
                var reconciliation = await voidService.ReconcilePendingAsync(record, cancellationToken).ConfigureAwait(false);
                if (reconciliation.LocalSaveError is not null)
                    problems.Add($"{number}: 作廢狀態已確認，但本機保存不完整：{reconciliation.LocalSaveError.Message}");

                if (reconciliation.Outcome == InvoiceVoidOutcome.PendingConfirmation)
                {
                    TryRecordVoidPending(accountKey, number, orderId, reconciliation.Message);
                    continue;
                }

                TryResolveVoidPending(accountKey, number, orderId);
                if (reconciliation.Outcome is InvoiceVoidOutcome.Confirmed or InvoiceVoidOutcome.AlreadyVoided)
                {
                    problems.AddRange(InvoiceCacheInvalidator.Invalidate(repository, account.Environment, number));
                }
            }
            catch (Exception error)
            {
                var message = "作廢狀態回查失敗：" + error.Message;
                TryRecordVoidPending(accountKey, number, orderId, message);
                problems.Add($"{number}: {message}");
            }
        }

        return problems.Count == result.Problems.Count
            ? result
            : result with { Problems = problems.ToArray() };
    }

    private void TryRecordVoidPending(string accountKey, string invoiceNumber, string orderId, string message)
    {
        try
        {
            issueStore.Record(accountKey, invoiceNumber, orderId, VoidPendingIssueType, message, now());
        }
        catch
        {
        }
    }

    private void TryResolveVoidPending(string accountKey, string invoiceNumber, string orderId)
    {
        try
        {
            issueStore.ResolveMatching(accountKey, invoiceNumber, orderId, now(), VoidPendingIssueType);
        }
        catch
        {
        }
    }

    private Account CurrentAccount()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0)
            throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return new Account(settings.Environment, sellerInvoice);
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private sealed record Account(string Environment, string SellerInvoice);
}
