using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceAutomaticSyncService
{
    internal const string DailyReconcileScope = "daily-two-period";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncService syncService;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly Func<DateTimeOffset> now;

    public InvoiceAutomaticSyncService(
        LocalRepository repository,
        InvoiceSyncService syncService,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var current = now();
        var today = DateOnly.FromDateTime(current.DateTime);
        var accountKey = CurrentAccountKey();
        var lastSuccess = stateStore.LastSuccess(accountKey, DailyReconcileScope);

        if (lastSuccess is not null &&
            DateOnly.FromDateTime(lastSuccess.Value.ToOffset(current.Offset).DateTime) == today)
        {
            return await syncService.SyncRecentAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await syncService.SyncRangeAsync(TwoPeriodRangeStart(today), today, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        stateStore.SetLastSuccess(accountKey, DailyReconcileScope, now());
        return result;
    }

    internal static DateOnly TwoPeriodRangeStart(DateOnly today)
    {
        var currentPeriodStartMonth = ((today.Month - 1) / 2 * 2) + 1;
        var currentPeriodStart = new DateOnly(today.Year, currentPeriodStartMonth, 1);
        return currentPeriodStart.AddMonths(-2);
    }

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0)
            throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return settings.Environment + "|" + sellerInvoice;
    }
}
