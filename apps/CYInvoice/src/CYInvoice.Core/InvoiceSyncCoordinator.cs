namespace CYInvoice.Core.Invoicing;

public enum InvoiceSyncRunStatus
{
    Completed,
    Busy,
    Cooldown,
}

public sealed record InvoiceSyncRunResult(
    InvoiceSyncRunStatus Status,
    InvoiceSyncResult? Result = null,
    TimeSpan CooldownRemaining = default);

public sealed class InvoiceSyncCoordinator
{
    private readonly InvoiceSyncService service;
    private readonly InvoiceAutomaticSyncService? automaticService;
    private readonly Func<DateTimeOffset> now;
    private readonly TimeSpan manualCooldown;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateGate = new();
    private DateTimeOffset? lastManualCompleted;

    public InvoiceSyncCoordinator(
        InvoiceSyncService service,
        Func<DateTimeOffset>? now = null,
        TimeSpan? manualCooldown = null,
        InvoiceAutomaticSyncService? automaticService = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.automaticService = automaticService;
        this.now = now ?? (() => DateTimeOffset.Now);
        this.manualCooldown = manualCooldown ?? TimeSpan.FromSeconds(30);
        if (this.manualCooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(manualCooldown));
    }

    public TimeSpan ManualCooldownRemaining => CalculateManualCooldownRemaining();

    public Task<InvoiceSyncRunResult> RunStartupAsync(CancellationToken cancellationToken = default) =>
        RunAsync(manual: false, automatic: true, cancellationToken);

    public Task<InvoiceSyncRunResult> RunScheduledAsync(CancellationToken cancellationToken = default) =>
        RunAsync(manual: false, automatic: true, cancellationToken);

    public Task<InvoiceSyncRunResult> RunManualAsync(CancellationToken cancellationToken = default) =>
        RunAsync(manual: true, automatic: false, cancellationToken);

    private async Task<InvoiceSyncRunResult> RunAsync(bool manual, bool automatic, CancellationToken cancellationToken)
    {
        if (manual)
        {
            var remaining = CalculateManualCooldownRemaining();
            if (remaining > TimeSpan.Zero)
                return new InvoiceSyncRunResult(InvoiceSyncRunStatus.Cooldown, CooldownRemaining: remaining);
        }

        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new InvoiceSyncRunResult(InvoiceSyncRunStatus.Busy);

        try
        {
            if (manual)
            {
                var remaining = CalculateManualCooldownRemaining();
                if (remaining > TimeSpan.Zero)
                    return new InvoiceSyncRunResult(InvoiceSyncRunStatus.Cooldown, CooldownRemaining: remaining);
            }

            var result = automatic && automaticService is not null
                ? await automaticService.SyncAsync(cancellationToken).ConfigureAwait(false)
                : await service.SyncRecentAsync(cancellationToken).ConfigureAwait(false);
            if (manual)
            {
                lock (stateGate) lastManualCompleted = now();
            }
            return new InvoiceSyncRunResult(InvoiceSyncRunStatus.Completed, result);
        }
        finally
        {
            gate.Release();
        }
    }

    private TimeSpan CalculateManualCooldownRemaining()
    {
        lock (stateGate)
        {
            if (lastManualCompleted is null) return TimeSpan.Zero;
            var remaining = manualCooldown - (now() - lastManualCompleted.Value);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
