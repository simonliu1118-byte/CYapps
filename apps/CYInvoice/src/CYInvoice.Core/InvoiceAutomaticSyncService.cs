using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceAutomaticSyncService
{
    internal const string DailyReconcileScope = "daily-two-period";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncService syncService;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly InvoiceRetentionService retentionService;
    private readonly Func<DateTimeOffset> now;

    public InvoiceAutomaticSyncService(
        LocalRepository repository,
        InvoiceSyncService syncService,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        retentionService = new InvoiceRetentionService(repository);
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var current = now();
        var today = DateOnly.FromDateTime(current.DateTime);
        var account = CurrentAccount();
        var accountKey = account.Environment + "|" + account.SellerInvoice;
        var lastSuccess = stateStore.LastSuccess(accountKey, DailyReconcileScope);

        if (lastSuccess is not null &&
            DateOnly.FromDateTime(lastSuccess.Value.ToOffset(current.Offset).DateTime) == today)
        {
            return await syncService.SyncRecentAsync(cancellationToken).ConfigureAwait(false);
        }

        var startDate = account.Environment == Environments.Test ? today : TwoPeriodRangeStart(today);
        var result = await syncService.SyncRangeAsync(startDate, today, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        stateStore.SetLastSuccess(accountKey, DailyReconcileScope, now());

        if (result.Problems.Count != 0) return result;

        try
        {
            var retention = retentionService.Prune(today);
            if (retention.Problems.Count == 0) return result;
            return result with
            {
                Problems = retention.Problems
                    .Select(problem => "本機舊資料快取清理：" + problem)
                    .ToArray(),
            };
        }
        catch (Exception error)
        {
            return result with
            {
                Problems = [$"本機舊資料清理失敗：{error.Message}"],
            };
        }
    }

    internal static DateOnly TwoPeriodRangeStart(DateOnly today)
    {
        var currentPeriodStartMonth = ((today.Month - 1) / 2 * 2) + 1;
        var currentPeriodStart = new DateOnly(today.Year, currentPeriodStartMonth, 1);
        return currentPeriodStart.AddMonths(-2);
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

    private sealed record Account(string Environment, string SellerInvoice);
}
