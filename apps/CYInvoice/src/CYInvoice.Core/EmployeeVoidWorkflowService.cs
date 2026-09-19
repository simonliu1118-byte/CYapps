using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public static class PaperInvoiceReceiptStates
{
    public const string NotPrintedOrNotDelivered = "not_printed_or_not_delivered";
    public const string Collected = "collected";
    public const string Uncollected = "uncollected";

    public static bool IsValid(string value) => value is
        NotPrintedOrNotDelivered or Collected or Uncollected;
}

public static class InvoiceVoidIssueTypes
{
    public const string ManualReview = "void_manual_review";
}

public sealed record InvoiceVoidManualReview(
    string RequesterEmployeeNo,
    string Reason,
    DateTimeOffset RequestedUtc);

public sealed record EmployeeVoidWorkflowResult(
    bool ManualReviewRequired,
    InvoiceRecord Record,
    InvoiceVoidResult? VoidResult,
    string Message);

public sealed class EmployeeVoidAuthenticationDelayException(TimeSpan remaining)
    : InvalidOperationException($"驗證失敗次數過多，請 {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} 秒後再試")
{
    public TimeSpan Remaining { get; } = remaining;
}

public sealed class EmployeeVoidWorkflowService
{
    private const string ManualReviewMetadataKey = "cyinvoice_void_manual_review";
    private readonly LocalRepository repository;
    private readonly InvoiceVoidService voidService;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly Func<DateTimeOffset> now;

    public EmployeeVoidWorkflowService(
        LocalRepository repository,
        InvoiceVoidService? voidService = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.voidService = voidService ?? new InvoiceVoidService(repository);
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<EmployeeVoidWorkflowResult> SubmitAsync(
        InvoiceRecord selected,
        string typedInvoiceNumber,
        string employeeNo,
        string password,
        string reason,
        string paperReceiptState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        typedInvoiceNumber = (typedInvoiceNumber ?? string.Empty).Trim();
        reason = (reason ?? string.Empty).Trim();
        ValidateReason(reason);

        var stored = Reload(selected);
        var expectedNumber = stored.InvoiceNumber.Trim();
        if (expectedNumber.Length == 0)
            throw new InvalidOperationException("這筆紀錄沒有發票號碼，無法作廢");
        if (!string.Equals(typedInvoiceNumber, expectedNumber, StringComparison.Ordinal))
            throw new InvalidOperationException("發票號碼不符，請重新確認");

        var employee = AuthenticateEmployee(employeeNo, password);
        var paperInvoice = string.Equals(stored.Delivery, InvoiceService.DeliveryPaper, StringComparison.Ordinal);
        if (paperInvoice)
        {
            paperReceiptState = (paperReceiptState ?? string.Empty).Trim();
            if (!PaperInvoiceReceiptStates.IsValid(paperReceiptState))
                throw new InvalidOperationException("請確認紙本電子發票證明聯狀態");
        }

        if (paperInvoice && paperReceiptState == PaperInvoiceReceiptStates.Uncollected)
            return QueueManualReview(stored, employee.EmployeeNo, reason);

        var result = await voidService.VoidAsync(
            stored,
            CancelReason(employee.EmployeeNo, reason),
            cancellationToken).ConfigureAwait(false);
        return new EmployeeVoidWorkflowResult(
            ManualReviewRequired: false,
            result.Record,
            result,
            result.Message);
    }

    public InvoiceVoidManualReview? ManualReviewFor(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReadManualReview(record);
    }

    public async Task<InvoiceVoidResult> ApproveManualReviewAsync(
        InvoiceSyncIssue issue,
        string actorEmployeeNo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);
        RequireManager(actorEmployeeNo);
        RequireManualReviewIssue(issue);
        var record = FindIssueRecord(issue);
        var review = ReadManualReview(record)
            ?? throw new InvalidOperationException("這筆人工確認已沒有可送出的作廢申請資料");

        var result = await voidService.VoidAsync(
            record,
            CancelReason(review.RequesterEmployeeNo, review.Reason),
            cancellationToken).ConfigureAwait(false);

        if (result.Outcome is InvoiceVoidOutcome.Confirmed or
            InvoiceVoidOutcome.AlreadyVoided or
            InvoiceVoidOutcome.PendingConfirmation)
        {
            var latest = Reload(result.Record);
            ClearManualReview(latest);
            syncRepository.UpsertMany([latest]);
            issueStore.Resolve(issue.Id, now());
        }

        return result;
    }

    public void CancelManualReview(InvoiceSyncIssue issue, string actorEmployeeNo)
    {
        ArgumentNullException.ThrowIfNull(issue);
        RequireManager(actorEmployeeNo);
        RequireManualReviewIssue(issue);
        var record = FindIssueRecord(issue);
        if (InvoiceVoidService.HasPendingMarker(record))
            throw new InvalidOperationException("這筆作廢已送出或正在確認中，不能取消退回");
        if (record.InvoiceState == InvoiceStates.Voided)
            throw new InvalidOperationException("這張發票已經作廢，不能取消退回");
        if (ReadManualReview(record) is null)
            throw new InvalidOperationException("這筆人工確認已沒有待處理的作廢申請");

        ClearManualReview(record);
        syncRepository.UpsertMany([record]);
        issueStore.Resolve(issue.Id, now());
    }

    private EmployeeVoidWorkflowResult QueueManualReview(
        InvoiceRecord record,
        string requesterEmployeeNo,
        string reason)
    {
        ValidateCurrentAccount(record);
        if (record.InvoiceState != InvoiceStates.Opened)
            throw new InvalidOperationException("只有目前仍為已開立狀態的發票可以送交人工確認");
        if (record.UploadStatus != UploadStatuses.Complete)
            throw new InvalidOperationException("發票尚未完成上傳，不能送交人工確認");
        if (InvoiceVoidService.HasPendingMarker(record))
            throw new InvalidOperationException("這張發票已有作廢作業待確認");

        if (ReadManualReview(record) is not null)
        {
            return new EmployeeVoidWorkflowResult(
                ManualReviewRequired: true,
                record,
                VoidResult: null,
                Message: "這張發票已在人工確認中，尚未向光貿送出作廢");
        }

        var review = new InvoiceVoidManualReview(requesterEmployeeNo, reason, now().ToUniversalTime());
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[ManualReviewMetadataKey] = JsonSerializer.SerializeToElement(review);
        syncRepository.UpsertMany([record]);

        try
        {
            issueStore.Record(
                CurrentAccountKey(),
                record.InvoiceNumber.Trim(),
                EffectiveOrderId(record),
                InvoiceVoidIssueTypes.ManualReview,
                "紙本電子發票證明聯尚未收回，待管理員確認是否送出作廢。",
                now());
        }
        catch
        {
            ClearManualReview(record);
            syncRepository.UpsertMany([record]);
            throw;
        }

        return new EmployeeVoidWorkflowResult(
            ManualReviewRequired: true,
            Reload(record),
            VoidResult: null,
            Message: "已送交人工確認，尚未向光貿送出作廢");
    }

    private EmployeeAccount AuthenticateEmployee(string employeeNo, string password)
    {
        employeeNo = (employeeNo ?? string.Empty).Trim();
        password ??= string.Empty;
        var delay = EmployeeVoidAuthenticationThrottle.Remaining(employeeNo, now());
        if (delay > TimeSpan.Zero) throw new EmployeeVoidAuthenticationDelayException(delay);

        EmployeeAccount? employee = null;
        try
        {
            employee = repository.Employees.Authenticate(employeeNo, password);
        }
        catch (ArgumentException)
        {
            // Keep the external message identical for an invalid number and an invalid password.
        }

        if (employee is null)
        {
            EmployeeVoidAuthenticationThrottle.RegisterFailure(employeeNo, now());
            throw new InvalidOperationException("員工編號或密碼錯誤");
        }

        EmployeeVoidAuthenticationThrottle.Reset(employeeNo);
        return employee;
    }

    private EmployeeAccount RequireManager(string actorEmployeeNo)
    {
        EmployeeAccount? actor;
        try { actor = repository.Employees.Find(actorEmployeeNo); }
        catch (ArgumentException) { actor = null; }
        if (actor is null || !actor.Enabled || !EmployeeRoles.CanManageAccounts(actor.Role))
            throw new UnauthorizedAccessException("只有管理員或超級管理員可以處理人工確認");
        return actor;
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
        if (matches.Length == 0) throw new InvalidOperationException("本機找不到這筆人工確認對應的發票");
        if (matches.Length > 1) throw new InvalidDataException("人工確認對到多筆本機發票，已停止自動處理");
        return matches[0];
    }

    private void RequireManualReviewIssue(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null)
            throw new InvalidOperationException("這筆人工確認已經處理完成");
        if (!string.Equals(issue.IssueType, InvoiceVoidIssueTypes.ManualReview, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆上傳問題不是作廢人工確認");
        if (!string.Equals(issue.AccountKey, CurrentAccountKey(), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("這筆人工確認屬於其他公司或環境，已停止處理");
    }

    private void ValidateCurrentAccount(InvoiceRecord record)
    {
        var account = CurrentAccount();
        if (!string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他環境，已停止作廢");
        if (record.SellerInvoice.Trim().Length != 0 &&
            !string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他公司統編，已停止作廢");
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

    private static string CancelReason(string employeeNo, string reason)
    {
        var value = employeeNo.Trim() + " " + reason.Trim();
        if (value.EnumerateRunes().Count() > 20)
            throw new InvalidOperationException("員工編號與作廢原因合計超過光貿允許長度");
        return value;
    }

    private static void ValidateReason(string reason)
    {
        var length = reason.EnumerateRunes().Count();
        if (length == 0) throw new InvalidOperationException("作廢原因不可空白");
        if (length > 15) throw new InvalidOperationException("作廢原因最多 15 字");
    }

    private static InvoiceVoidManualReview? ReadManualReview(InvoiceRecord record)
    {
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(ManualReviewMetadataKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        try
        {
            var review = value.Deserialize<InvoiceVoidManualReview>();
            if (review is null || review.RequesterEmployeeNo.Trim().Length == 0 || review.Reason.Trim().Length == 0)
                throw new InvalidDataException("作廢人工確認資料不完整");
            return review;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("作廢人工確認資料格式錯誤", error);
        }
    }

    private static void ClearManualReview(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(ManualReviewMetadataKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }

    private sealed record Account(string Environment, string SellerInvoice);
}

internal static class EmployeeVoidAuthenticationThrottle
{
    private sealed record FailureState(int Count, DateTimeOffset NextAllowed);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FailureState> Failures = new(StringComparer.Ordinal);

    public static TimeSpan Remaining(string employeeNo, DateTimeOffset now)
    {
        var key = Key(employeeNo);
        lock (Gate)
        {
            if (!Failures.TryGetValue(key, out var state)) return TimeSpan.Zero;
            var remaining = state.NextAllowed - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public static void RegisterFailure(string employeeNo, DateTimeOffset now)
    {
        var key = Key(employeeNo);
        lock (Gate)
        {
            var count = Failures.TryGetValue(key, out var previous) ? previous.Count + 1 : 1;
            var delay = count switch
            {
                <= 2 => TimeSpan.Zero,
                3 => TimeSpan.FromSeconds(5),
                4 => TimeSpan.FromSeconds(15),
                _ => TimeSpan.FromSeconds(60),
            };
            Failures[key] = new FailureState(count, now + delay);
        }
    }

    public static void Reset(string employeeNo)
    {
        lock (Gate) Failures.Remove(Key(employeeNo));
    }

    private static string Key(string employeeNo)
    {
        employeeNo = (employeeNo ?? string.Empty).Trim();
        return employeeNo.Length == 0 ? "<blank>" : employeeNo;
    }
}
