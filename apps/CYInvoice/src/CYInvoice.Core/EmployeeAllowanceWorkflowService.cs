using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceAllowanceIssueTypes
{
    public const string ManualReview = "allowance_manual_review";
}

public sealed record InvoiceAllowanceManualReview(
    string RequesterEmployeeNo,
    string Reason,
    long TaxInclusiveAmount,
    DateTimeOffset RequestedUtc,
    string[] BaselineAllowanceNumbers,
    bool AwaitingConfirmation = false,
    DateTimeOffset? ManualCompletedUtc = null,
    string ConfirmedAllowanceNumber = "",
    string HandlerEmployeeNo = "");

public sealed record EmployeeAllowanceWorkflowResult(
    InvoiceRecord Record,
    InvoiceAllowanceManualReview Review,
    bool AlreadyQueued,
    string Message);

public enum InvoiceAllowanceReconcileOutcome
{
    Confirmed,
    PendingConfirmation,
    Problem,
}

public sealed record InvoiceAllowanceReconcileResult(
    InvoiceAllowanceReconcileOutcome Outcome,
    InvoiceRecord Record,
    string Message);

public sealed class EmployeeAllowanceWorkflowService
{
    private const string ManualReviewMetadataKey = "cyinvoice_allowance_manual_review";
    private const string HandledReviewMetadataKey = "cyinvoice_allowance_handled_review";
    private const string PendingMetadataKey = "cyinvoice_allowance_pending";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceDetailRefreshService detailRefreshService;
    private readonly Func<DateTimeOffset> now;

    public EmployeeAllowanceWorkflowService(
        LocalRepository repository,
        InvoiceDetailRefreshService? detailRefreshService = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        this.now = now ?? (() => DateTimeOffset.Now);
        this.detailRefreshService = detailRefreshService ?? new InvoiceDetailRefreshService(repository, now: this.now);
    }

    public async Task<EmployeeAllowanceWorkflowResult> SubmitAsync(
        InvoiceRecord selected,
        string employeeNo,
        string password,
        string reason,
        long taxInclusiveAmount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length == 0) throw new InvalidOperationException("折讓原因不可空白");
        if (taxInclusiveAmount <= 0) throw new InvalidOperationException("折讓金額必須大於 0");

        var employee = EmployeeOperationAuthentication.AuthenticateEmployee(
            repository, employeeNo, password, now());

        // The fresh query is both the eligibility check and the durable allowance baseline.
        // Existing historical allowances are persisted by InvoiceOfficialState.ApplyQuery before
        // this request is created, so later reconciliation only considers newly appearing numbers.
        var fresh = await detailRefreshService.RefreshAsync(selected, cancellationToken).ConfigureAwait(false);
        ValidateCurrentAccount(fresh);
        ValidateEligible(fresh, taxInclusiveAmount);

        var number = fresh.InvoiceNumber.Trim();
        var orderId = EffectiveOrderId(fresh);
        var accountKey = CurrentAccountKey();
        if (issueStore.HasUnresolved(
                accountKey,
                number,
                orderId,
                InvoiceVoidIssueTypes.ManualReview,
                InvoiceVoidSyncIssueTypes.PendingConfirmation))
            throw new InvalidOperationException("這張發票已有作廢作業待處理，不能提出折讓");

        var existing = ReadManualReview(fresh);
        if (existing is not null)
        {
            return new EmployeeAllowanceWorkflowResult(
                fresh,
                existing,
                AlreadyQueued: true,
                Message: existing.AwaitingConfirmation
                    ? "這張發票的折讓已在等待光貿確認"
                    : "這張發票已有折讓申請等待管理員處理");
        }
        if (issueStore.HasUnresolved(accountKey, number, orderId, InvoiceAllowanceIssueTypes.ManualReview))
            throw new InvalidDataException("折讓人工處理待辦存在，但申請資料遺失，已停止重複建立");

        var baseline = InvoiceAllowanceMetadata.ReadOfficial(fresh)
            .Select(item => item.AllowanceNumber.Trim())
            .Where(value => value.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var review = new InvoiceAllowanceManualReview(
            employee.EmployeeNo,
            reason,
            taxInclusiveAmount,
            now().ToUniversalTime(),
            baseline);
        WriteManualReview(fresh, review);
        syncRepository.UpsertMany([fresh]);
        try
        {
            issueStore.Record(
                accountKey,
                number,
                orderId,
                InvoiceAllowanceIssueTypes.ManualReview,
                "折讓申請等待管理員至光貿網站人工處理。",
                now());
        }
        catch
        {
            ClearManualReview(fresh);
            syncRepository.UpsertMany([fresh]);
            throw;
        }

        return new EmployeeAllowanceWorkflowResult(
            Reload(fresh),
            review,
            AlreadyQueued: false,
            Message: "折讓申請已送交管理員人工處理");
    }

    public InvoiceAllowanceManualReview? ManualReviewFor(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReadManualReview(record);
    }

    public InvoiceAllowanceManualReview? HandledReviewFor(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(HandledReviewMetadataKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        try { return value.Deserialize<InvoiceAllowanceManualReview>(); }
        catch (JsonException error) { throw new InvalidDataException("折讓人工處理紀錄格式錯誤", error); }
    }

    public async Task<InvoiceAllowanceReconcileResult> MarkManualCompletedAsync(
        InvoiceSyncIssue issue,
        string actorEmployeeNo,
        string actorPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var manager = EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);
        RequireManualReviewIssue(issue);

        var record = FindIssueRecord(issue);
        var review = ReadManualReview(record)
            ?? throw new InvalidOperationException("這筆人工折讓已沒有可處理的申請資料");
        if (review.AwaitingConfirmation)
            return await ReconcilePendingAsync(record, cancellationToken).ConfigureAwait(false);

        review = review with
        {
            AwaitingConfirmation = true,
            ManualCompletedUtc = now().ToUniversalTime(),
            ConfirmedAllowanceNumber = string.Empty,
            HandlerEmployeeNo = manager.EmployeeNo,
        };
        WriteManualReview(record, review);
        MarkPending(record);
        syncRepository.UpsertMany([record]);
        UpdateIssueMessage(record, "管理員已完成人工折讓，等待光貿回傳新的折讓資料。");

        return await ReconcilePendingAsync(record, cancellationToken).ConfigureAwait(false);
    }

    // Exceptional path only. Normal reconciliation discovers the new allowance number by comparing
    // the request-time baseline with the latest invoice_query allowance array. A manager uses this
    // only when more than one new candidate remains equally plausible.
    public async Task<InvoiceAllowanceReconcileResult> ConfirmCandidateAsync(
        InvoiceSyncIssue issue,
        string allowanceNumber,
        string actorEmployeeNo,
        string actorPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);
        RequireManualReviewIssue(issue);
        allowanceNumber = ValidateAllowanceNumber(allowanceNumber);

        var record = FindIssueRecord(issue);
        var review = ReadManualReview(record)
            ?? throw new InvalidOperationException("這筆人工折讓已沒有可處理的申請資料");
        if (!review.AwaitingConfirmation || !HasPendingMarker(record))
            throw new InvalidOperationException("這筆折讓尚未進入官方確認階段");

        InvoiceRecord fresh;
        try
        {
            fresh = await detailRefreshService.RefreshAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            var message = "折讓狀態回查失敗：" + error.Message;
            UpdateIssueMessage(record, message);
            return new InvoiceAllowanceReconcileResult(
                InvoiceAllowanceReconcileOutcome.Problem,
                Reload(record),
                message);
        }

        review = ReadManualReview(fresh)
            ?? throw new InvalidDataException("官方回查後折讓人工申請資料遺失");
        var candidate = NewAllowances(fresh, review)
            .Where(item => string.Equals(item.AllowanceNumber.Trim(), allowanceNumber, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidate.Length == 0)
            return Problem(fresh, $"折讓單 {allowanceNumber} 不是這次申請後新出現的折讓資料，請重新確認。");
        if (candidate.Length > 1)
            return Problem(fresh, $"光貿回傳多筆相同折讓單號 {allowanceNumber}，無法唯一確認。");
        if (!IsIssuanceType(candidate[0].InvoiceType))
            return Problem(fresh, $"折讓單 {allowanceNumber} 不是折讓開立資料，請重新確認。");

        review = review with { ConfirmedAllowanceNumber = allowanceNumber };
        WriteManualReview(fresh, review);
        syncRepository.UpsertMany([fresh]);
        return ReconcileFresh(fresh, review);
    }

    public void CancelManualReview(
        InvoiceSyncIssue issue,
        string actorEmployeeNo,
        string actorPassword)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);
        RequireManualReviewIssue(issue);
        var record = FindIssueRecord(issue);
        var review = ReadManualReview(record)
            ?? throw new InvalidOperationException("這筆人工折讓已沒有待處理的申請資料");
        if (review.AwaitingConfirmation || HasPendingMarker(record))
            throw new InvalidOperationException("這筆折讓已標記人工操作完成並等待光貿確認，不能取消退回");

        ClearManualReview(record);
        ClearPending(record);
        syncRepository.UpsertMany([record]);
        issueStore.Resolve(issue.Id, now());
    }

    public async Task<InvoiceAllowanceReconcileResult> ReconcilePendingAsync(
        InvoiceRecord selected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var record = Reload(selected);
        ValidateCurrentAccount(record);
        var review = ReadManualReview(record)
            ?? throw new InvalidOperationException("折讓 pending 缺少人工申請資料");
        if (!review.AwaitingConfirmation || !HasPendingMarker(record))
            throw new InvalidOperationException("這筆折讓尚未進入官方確認階段");

        InvoiceRecord fresh;
        try
        {
            fresh = await detailRefreshService.RefreshAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            var message = "折讓狀態回查失敗：" + error.Message;
            UpdateIssueMessage(record, message);
            return new InvoiceAllowanceReconcileResult(
                InvoiceAllowanceReconcileOutcome.Problem,
                Reload(record),
                message);
        }

        review = ReadManualReview(fresh)
            ?? throw new InvalidDataException("官方回查後折讓人工申請資料遺失");
        return ReconcileFresh(fresh, review);
    }

    internal static bool HasPendingMarker(InvoiceRecord record)
    {
        if (record.ExtensionData is null || !record.ExtensionData.TryGetValue(PendingMetadataKey, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }

    private InvoiceAllowanceReconcileResult ReconcileFresh(
        InvoiceRecord fresh,
        InvoiceAllowanceManualReview review)
    {
        var newAllowances = NewAllowances(fresh, review);
        if (review.ConfirmedAllowanceNumber.Trim().Length != 0)
        {
            var selected = newAllowances
                .Where(item => string.Equals(
                    item.AllowanceNumber.Trim(),
                    review.ConfirmedAllowanceNumber.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (selected.Length == 0)
                return Problem(fresh, $"已確認的折讓單 {review.ConfirmedAllowanceNumber} 不在本次申請後新增的折讓資料中。");
            if (selected.Length > 1)
                return Problem(fresh, $"光貿回傳多筆相同折讓單號 {review.ConfirmedAllowanceNumber}，無法唯一確認。");
            return EvaluateCandidate(fresh, review, selected[0]);
        }

        var issuance = newAllowances.Where(item => IsIssuanceType(item.InvoiceType)).ToArray();
        if (issuance.Length == 0)
        {
            if (newAllowances.Count == 0)
                return Pending(fresh, "尚未在光貿查到本次申請後新增的折讓資料，等待下次確認。");
            return Problem(fresh, "已查到新的折讓資料，但沒有折讓開立資料，請管理員確認光貿操作結果。");
        }

        // If any newly appearing issuance record is still in flight, wait until the result set is
        // stable before auto-selecting a number. This avoids choosing one completed record while a
        // second same-request candidate is still being processed.
        if (issuance.Any(item => IsPendingStatus(item.InvoiceStatus)))
        {
            var numbers = CandidateNumbers(issuance);
            return Pending(fresh, $"已查到新的折讓資料{numbers}，仍有資料在光貿處理中，等待下次確認。");
        }

        var matching = new List<InvoiceAllowanceResult>();
        var amountErrors = new List<string>();
        foreach (var allowance in issuance)
        {
            if (!TryTaxInclusiveAmount(allowance, out var amount, out var error))
            {
                amountErrors.Add($"{allowance.AllowanceNumber}: {error}");
                continue;
            }
            if (amount == FixedDecimal.FromInt64(review.TaxInclusiveAmount))
                matching.Add(allowance);
        }

        if (matching.Count == 0)
        {
            var details = amountErrors.Count == 0
                ? CandidateNumbers(issuance)
                : "（" + string.Join("；", amountErrors) + "）";
            return Problem(
                fresh,
                $"已查到本次申請後新增的折讓資料{details}，但沒有任何一筆含稅金額符合申請金額 {review.TaxInclusiveAmount}，請管理員確認。");
        }
        if (matching.Count > 1)
        {
            return Problem(
                fresh,
                $"查到多筆新折讓都符合申請金額：{string.Join("、", matching.Select(item => item.AllowanceNumber))}。請管理員確認本次折讓單號。");
        }

        return EvaluateCandidate(fresh, review, matching[0]);
    }

    private InvoiceAllowanceReconcileResult EvaluateCandidate(
        InvoiceRecord fresh,
        InvoiceAllowanceManualReview review,
        InvoiceAllowanceResult allowance)
    {
        var number = allowance.AllowanceNumber.Trim();
        if (!IsIssuanceType(allowance.InvoiceType))
            return Problem(fresh, $"折讓單 {number} 的官方類型為 {allowance.InvoiceType}，不是折讓開立資料。");
        if (IsPendingStatus(allowance.InvoiceStatus))
            return Pending(fresh, $"折讓單 {number} 仍在光貿處理中。");
        if (allowance.InvoiceStatus == UploadStatuses.Error)
            return Problem(fresh, $"折讓單 {number} 官方狀態為錯誤，請至光貿確認。");
        if (allowance.InvoiceStatus != UploadStatuses.Complete)
            return Problem(fresh, $"折讓單 {number} 回傳未知狀態 {allowance.InvoiceStatus}，不能自動結案。");
        if (!TryTaxInclusiveAmount(allowance, out var officialAmount, out var amountError))
            return Problem(fresh, $"折讓單 {number} 官方金額格式無法辨識：{amountError}");
        if (officialAmount != FixedDecimal.FromInt64(review.TaxInclusiveAmount))
        {
            return Problem(
                fresh,
                $"折讓單 {number} 官方含稅金額 {officialAmount} 與申請金額 {review.TaxInclusiveAmount} 不符。");
        }

        ClearPending(fresh);
        fresh.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        fresh.ExtensionData[HandledReviewMetadataKey] = JsonSerializer.SerializeToElement(
            review with { ConfirmedAllowanceNumber = number });
        ClearManualReview(fresh);
        syncRepository.UpsertMany([fresh]);
        issueStore.ResolveMatching(
            CurrentAccountKey(),
            fresh.InvoiceNumber.Trim(),
            EffectiveOrderId(fresh),
            now(),
            InvoiceAllowanceIssueTypes.ManualReview);
        return new InvoiceAllowanceReconcileResult(
            InvoiceAllowanceReconcileOutcome.Confirmed,
            Reload(fresh),
            $"光貿已確認折讓單 {number} 完成");
    }

    private static IReadOnlyList<InvoiceAllowanceResult> NewAllowances(
        InvoiceRecord record,
        InvoiceAllowanceManualReview review)
    {
        var baseline = new HashSet<string>(
            review.BaselineAllowanceNumbers ?? [],
            StringComparer.OrdinalIgnoreCase);
        return InvoiceAllowanceMetadata.ReadOfficial(record)
            .Where(item => item.AllowanceNumber.Trim().Length != 0)
            .Where(item => !baseline.Contains(item.AllowanceNumber.Trim()))
            .ToArray();
    }

    private InvoiceAllowanceReconcileResult Pending(InvoiceRecord record, string message)
    {
        UpdateIssueMessage(record, message);
        return new InvoiceAllowanceReconcileResult(
            InvoiceAllowanceReconcileOutcome.PendingConfirmation,
            Reload(record),
            message);
    }

    private InvoiceAllowanceReconcileResult Problem(InvoiceRecord record, string message)
    {
        UpdateIssueMessage(record, message);
        return new InvoiceAllowanceReconcileResult(
            InvoiceAllowanceReconcileOutcome.Problem,
            Reload(record),
            message);
    }

    private void ValidateEligible(InvoiceRecord record, long taxInclusiveAmount)
    {
        if (record.InvoiceNumber.Trim().Length == 0)
            throw new InvalidOperationException("這筆紀錄沒有發票號碼，不能提出折讓");
        if (record.InvoiceState != InvoiceStates.Opened)
            throw new InvalidOperationException("只有目前仍為已開立狀態的發票可以提出折讓");
        if (record.UploadStatus != UploadStatuses.Complete)
            throw new InvalidOperationException("發票尚未完成上傳，不能提出折讓");
        if (InvoiceVoidService.HasPendingMarker(record))
            throw new InvalidOperationException("這張發票已有作廢作業待確認，不能提出折讓");
        if (taxInclusiveAmount > record.Amount)
            throw new InvalidOperationException("折讓金額不可大於原發票含稅金額");
    }

    private void ValidateCurrentAccount(InvoiceRecord record)
    {
        var account = CurrentAccount();
        if (!string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他環境，已停止折讓作業");
        if (record.SellerInvoice.Trim().Length != 0 &&
            !string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他公司統編，已停止折讓作業");
    }

    private void RequireManualReviewIssue(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null)
            throw new InvalidOperationException("這筆折讓人工處理已經完成");
        if (!string.Equals(issue.IssueType, InvoiceAllowanceIssueTypes.ManualReview, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆上傳問題不是折讓人工處理");
        if (!string.Equals(issue.AccountKey, CurrentAccountKey(), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("這筆折讓申請屬於其他公司或環境，已停止處理");
    }

    private InvoiceRecord FindIssueRecord(InvoiceSyncIssue issue)
    {
        var account = CurrentAccount();
        var matches = repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            .Where(record =>
                (issue.InvoiceNumber.Trim().Length != 0 &&
                 string.Equals(record.InvoiceNumber.Trim(), issue.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (issue.OrderId.Trim().Length != 0 &&
                 string.Equals(EffectiveOrderId(record), issue.OrderId.Trim(), StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length == 0) throw new InvalidOperationException("本機找不到這筆折讓申請對應的發票");
        if (matches.Length > 1) throw new InvalidDataException("折讓申請對到多筆本機發票，已停止自動處理");
        return matches[0];
    }

    private void UpdateIssueMessage(InvoiceRecord record, string message)
    {
        issueStore.Record(
            CurrentAccountKey(),
            record.InvoiceNumber.Trim(),
            EffectiveOrderId(record),
            InvoiceAllowanceIssueTypes.ManualReview,
            message,
            now());
    }

    private static bool TryTaxInclusiveAmount(
        InvoiceAllowanceResult allowance,
        out FixedDecimal amount,
        out string error)
    {
        try
        {
            amount = FixedDecimal.Add(
                FixedDecimal.Parse(allowance.TotalAmount),
                FixedDecimal.Parse(allowance.TaxAmount));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            amount = default;
            error = exception.Message;
            return false;
        }
    }

    private static string CandidateNumbers(IEnumerable<InvoiceAllowanceResult> values)
    {
        var numbers = values.Select(item => item.AllowanceNumber.Trim())
            .Where(value => value.Length != 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return numbers.Length == 0 ? string.Empty : "（" + string.Join("、", numbers) + "）";
    }

    private static string ValidateAllowanceNumber(string value)
    {
        value = (value ?? string.Empty).Trim();
        var length = value.EnumerateRunes().Count();
        if (length == 0) throw new InvalidOperationException("折讓單號不可空白");
        if (length > 16) throw new InvalidOperationException("折讓單號不可超過 16 字");
        return value;
    }

    private static bool IsIssuanceType(string value) => value.Trim().ToUpperInvariant() is
        "D0401" or "B0401" or "B0101" or "B0102";

    private static bool IsPendingStatus(int status) => status is
        UploadStatuses.Pending or UploadStatuses.Uploading or UploadStatuses.Uploaded or
        UploadStatuses.Processing or UploadStatuses.Confirming;

    private static InvoiceAllowanceManualReview? ReadManualReview(InvoiceRecord record)
    {
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(ManualReviewMetadataKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        try
        {
            var review = value.Deserialize<InvoiceAllowanceManualReview>();
            if (review is null || review.RequesterEmployeeNo.Trim().Length == 0 ||
                review.Reason.Trim().Length == 0 || review.TaxInclusiveAmount <= 0 ||
                review.BaselineAllowanceNumbers is null)
                throw new InvalidDataException("折讓人工處理資料不完整");
            if (!review.AwaitingConfirmation && review.ConfirmedAllowanceNumber.Trim().Length != 0)
                throw new InvalidDataException("尚未進入 pending 的折讓申請不應有確認單號");
            return review;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("折讓人工處理資料格式錯誤", error);
        }
    }

    private static void WriteManualReview(InvoiceRecord record, InvoiceAllowanceManualReview review)
    {
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[ManualReviewMetadataKey] = JsonSerializer.SerializeToElement(review);
    }

    private static void ClearManualReview(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(ManualReviewMetadataKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }

    private static void MarkPending(InvoiceRecord record)
    {
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[PendingMetadataKey] = JsonSerializer.SerializeToElement(true);
    }

    private static void ClearPending(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(PendingMetadataKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }

    private InvoiceRecord Reload(InvoiceRecord record) =>
        repository.Invoices.LoadOrCreate().SingleOrDefault(item => item.Id == record.Id)
        ?? throw new InvalidOperationException("本機找不到這筆發票紀錄，請重新整理清單");

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

    private string CurrentAccountKey()
    {
        var account = CurrentAccount();
        return account.Environment + "|" + account.SellerInvoice;
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private sealed record Account(string Environment, string SellerInvoice);
}
