using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceAutomaticSyncService
{
    internal const string DailyReconcileScope = "daily-two-period";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncService syncService;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceRetentionService retentionService;
    private readonly InvoiceVoidService voidService;
    private readonly EmployeeAllowanceWorkflowService allowanceService;
    private readonly EmployeeAllowanceVoidWorkflowService allowanceVoidService;
    private readonly Func<DateTimeOffset> now;

    public InvoiceAutomaticSyncService(
        LocalRepository repository,
        InvoiceSyncService syncService,
        Func<DateTimeOffset>? now = null,
        InvoiceVoidService? voidService = null,
        EmployeeAllowanceWorkflowService? allowanceService = null,
        EmployeeAllowanceVoidWorkflowService? allowanceVoidService = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        retentionService = new InvoiceRetentionService(repository);
        this.voidService = voidService ?? new InvoiceVoidService(repository, now: now);
        this.now = now ?? (() => DateTimeOffset.Now);
        this.allowanceVoidService = allowanceVoidService ?? new EmployeeAllowanceVoidWorkflowService(repository, now: this.now);
        this.allowanceService = allowanceService ?? new EmployeeAllowanceWorkflowService(repository, now: this.now);
    }

    public async Task<InvoiceSyncResult> SyncRecentAsync(CancellationToken cancellationToken = default)
    {
        InvoiceSyncResult result;
        try { result = await syncService.SyncRecentAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Financial recovery is independent of invoice-list availability.
            await ReconcileApprovedLocalAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        return await ReconcilePendingAfterListAsync(result, cancellationToken).ConfigureAwait(false);
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
            return await SyncRecentAsync(cancellationToken).ConfigureAwait(false);
        }

        var startDate = account.Environment == Environments.Test ? today : TwoPeriodRangeStart(today);
        InvoiceSyncResult result;
        try { result = await syncService.SyncRangeAsync(startDate, today, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await ReconcileApprovedLocalAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        // Only the daily pass calls allowance_list; normal startup/manual
        // sync retains its existing call volume.
        (int Queried, IReadOnlyList<string> Problems) allowances;
        try
        {
            allowances = await syncService.ReconcileRecentAllowanceEventsAsync(today, cancellationToken)
                .ConfigureAwait(false);
            // The durable Upload Issues alert is cleared only after a full successful list pass.
            issueStore.ResolveAccountType(accountKey, InvoiceSyncIssueTypes.AllowanceLimitExceeded, now());
        }
        catch (AllowanceSyncLimitExceededException error)
        {
            // Fail the daily pass (no LastSuccess marker) but keep a visible,
            // deduplicated action item in the existing Upload Issues screen.
            issueStore.Record(accountKey, string.Empty, string.Empty,
                InvoiceSyncIssueTypes.AllowanceLimitExceeded, error.Message, now());
            // Stop this day's list traversal. The existing daily marker avoids
            // starting the same bounded pass on every timer tick; the durable
            // limit issue remains until a complete pass on a later day succeeds.
            allowances = (0, new[] { error.Message });
        }
        result = result with
        {
            Queried = result.Queried + allowances.Queried,
            Problems = result.Problems.Concat(allowances.Problems).ToArray(),
        };
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

        return await ReconcilePendingAfterListAsync(result, cancellationToken).ConfigureAwait(false);
    }

    internal static DateOnly TwoPeriodRangeStart(DateOnly today)
    {
        var currentPeriodStartMonth = ((today.Month - 1) / 2 * 2) + 1;
        var currentPeriodStart = new DateOnly(today.Year, currentPeriodStartMonth, 1);
        return currentPeriodStart.AddMonths(-2);
    }

    private async Task<InvoiceSyncResult> ReconcilePendingAfterListAsync(
        InvoiceSyncResult result,
        CancellationToken cancellationToken)
    {
        var account = CurrentAccount();
        var accountKey = account.Environment + "|" + account.SellerInvoice;
        var problems = result.Problems.ToList();
        problems.AddRange(await ReconcileApprovedLocalAsync(cancellationToken).ConfigureAwait(false));
        ResolveDeprecatedPendingIssues(accountKey, problems);

        var pending = repository.Invoices.LoadOrCreate()
            .Where(record => InvoiceVoidService.HasPendingMarker(record) ||
                             EmployeeAllowanceWorkflowService.HasPendingMarker(record))
            .Where(record => string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            .ToArray();

        foreach (var record in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = record.InvoiceNumber.Trim();
            var orderId = EffectiveOrderId(record);
            var voidPending = InvoiceVoidService.HasPendingMarker(record);
            var allowancePending = EmployeeAllowanceWorkflowService.HasPendingMarker(record);

            if (voidPending && allowancePending)
            {
                problems.Add($"{number}: 同一張發票同時存在作廢與折讓待確認，已停止自動處理");
                continue;
            }

            // All durable pending work uses authoritative single-invoice query after every normal
            // list sync. Invoice date controls list coverage only; it never gates pending checks.
            if (voidPending)
            {
                if (record.InvoiceState == InvoiceStates.Voided)
                {
                    InvoiceVoidService.ClearPendingMarker(record);
                    syncRepository.UpsertMany([record]);
                    ResolveVoidQueryIssue(accountKey, number, orderId);
                    problems.AddRange(InvoiceCacheInvalidator.Invalidate(repository, account.Environment, number));
                    continue;
                }

                try
                {
                    var reconciliation = await voidService.ReconcilePendingAsync(record, cancellationToken).ConfigureAwait(false);
                    if (reconciliation.LocalSaveError is not null)
                        problems.Add($"{number}: 作廢狀態已確認，但本機保存不完整：{reconciliation.LocalSaveError.Message}");

                    if (reconciliation.Outcome == InvoiceVoidOutcome.PendingConfirmation)
                    {
                        // AMEGO explicitly reporting an in-progress void is a normal invoice lifecycle
                        // state. Only an indeterminate/failed authoritative lookup belongs in upload issues.
                        if (!InvoiceVoidService.IsOfficiallyPending(reconciliation.Record))
                        {
                            var message = "作廢狀態回查失敗：" + reconciliation.Message;
                            RecordVoidQueryIssue(accountKey, number, orderId, message);
                            problems.Add($"{number}: {message}");
                        }
                        else
                        {
                            ResolveVoidQueryIssue(accountKey, number, orderId);
                        }
                        continue;
                    }

                    ResolveVoidQueryIssue(accountKey, number, orderId);
                }
                catch (Exception error)
                {
                    var message = "作廢狀態回查失敗：" + error.Message;
                    RecordVoidQueryIssue(accountKey, number, orderId, message);
                    problems.Add($"{number}: {message}");
                }
                continue;
            }

            try
            {
                var reconciliation = await allowanceService.ReconcilePendingAsync(record, cancellationToken).ConfigureAwait(false);
                if (reconciliation.Outcome == InvoiceAllowanceReconcileOutcome.Problem)
                    problems.Add($"{number}: {reconciliation.Message}");
            }
            catch (Exception error)
            {
                problems.Add($"{number}: 折讓狀態回查失敗：{error.Message}");
            }
        }

        return problems.Count == result.Problems.Count
            ? result
            : result with { Problems = problems.ToArray() };
    }

    private async Task<IReadOnlyList<string>> ReconcileApprovedLocalAsync(CancellationToken token)
    {
        if (repository.Settings.LoadOrCreate().CloudMode != CloudModes.LocalOnly ||
            repository.IdentityProvider.Kind != IdentityProviderKind.Local) return [];
        var problems = new List<string>();
        var voidWorkflow = allowanceVoidService;
        var account = CurrentAccount();
        var invoicesByNumber = repository.Invoices.LoadOrCreate()
            .Where(r => r.Environment == account.Environment &&
                (r.SellerInvoice.Length == 0 || r.SellerInvoice == account.SellerInvoice))
            .ToLookup(r => r.InvoiceNumber, StringComparer.Ordinal);
        foreach (var issue in issueStore.Unresolved(account.Environment + "|" + account.SellerInvoice))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (issue.IssueType == InvoiceAllowanceIssueTypes.ManualReview)
                {
                    var matches = invoicesByNumber[issue.InvoiceNumber].ToArray();
                    if (matches.Length == 1 && allowanceService.ManualReviewFor(matches[0])?.LocalApprovedUtc is not null)
                        await allowanceService.ExecuteApprovedLocalDraftAsync(issue, cancellationToken: token).ConfigureAwait(false);
                }
                else if (issue.IssueType == InvoiceAllowanceVoidIssueTypes.ManualReview &&
                    voidWorkflow.LocalApprovalFor(issue) is not null)
                    await voidWorkflow.ExecuteApprovedLocalVoidAsync(issue, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Workflow owns durable issue/evidence. Keep servicing other
                // independent operations after one operation fails.
                problems.Add($"{issue.Id}: {error.Message}");
            }
        }
        return problems;
    }

    private void ResolveDeprecatedPendingIssues(string accountKey, List<string> problems)
    {
        try
        {
            issueStore.ResolveAccountType(accountKey, InvoiceVoidSyncIssueTypes.PendingConfirmation, now());
        }
        catch (Exception error)
        {
            problems.Add("舊版作廢待確認問題狀態清理失敗：" + error.Message);
        }
    }

    private void RecordVoidQueryIssue(string accountKey, string invoiceNumber, string orderId, string message)
    {
        try
        {
            issueStore.Record(
                accountKey,
                invoiceNumber,
                orderId,
                InvoiceSyncIssueTypes.QueryFailed,
                message,
                now());
        }
        catch
        {
        }
    }

    private void ResolveVoidQueryIssue(string accountKey, string invoiceNumber, string orderId)
    {
        try
        {
            issueStore.ResolveMatching(
                accountKey,
                invoiceNumber,
                orderId,
                now(),
                InvoiceSyncIssueTypes.QueryFailed,
                InvoiceVoidSyncIssueTypes.PendingConfirmation);
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