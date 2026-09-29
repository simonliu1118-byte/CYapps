using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceAllowanceVoidIssueTypes
{
    public const string ManualReview = "allowance_void_manual_review";
}

public sealed record InvoiceAllowanceVoidManualReview(
    string AllowanceNumber,
    string RequesterEmployeeNo,
    string Reason,
    DateTimeOffset RequestedUtc);

public sealed record EmployeeAllowanceVoidWorkflowResult(
    InvoiceRecord Record,
    InvoiceAllowanceVoidManualReview Review,
    bool AlreadyQueued,
    string Message);

public sealed class EmployeeAllowanceVoidWorkflowService
{
    private const string MetadataKey = "cyinvoice_allowance_void_manual_review";
    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceDetailRefreshService detailRefreshService;
    private readonly Func<DateTimeOffset> now;

    public EmployeeAllowanceVoidWorkflowService(
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

    public async Task<EmployeeAllowanceVoidWorkflowResult> SubmitAsync(
        InvoiceRecord selected,
        string allowanceNumber,
        string employeeNo,
        string password,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        allowanceNumber = (allowanceNumber ?? string.Empty).Trim();
        reason = (reason ?? string.Empty).Trim();
        if (allowanceNumber.Length == 0) throw new InvalidOperationException("折讓單號不可空白");
        if (reason.Length == 0) throw new InvalidOperationException("折讓作廢原因不可空白");

        var employee = EmployeeOperationAuthentication.AuthenticateEmployee(
            repository,
            employeeNo,
            password,
            now());

        var fresh = await detailRefreshService.RefreshAsync(selected, cancellationToken).ConfigureAwait(false);
        ValidateCurrentAccount(fresh);
        var matched = InvoiceAllowanceMetadata.ReadOfficial(fresh)
            .Where(item => string.Equals(item.AllowanceNumber.Trim(), allowanceNumber, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matched.Length != 1)
            throw new InvalidOperationException(matched.Length == 0
                ? "目前光貿資料已找不到這張折讓單，請重新開啟發票詳細資訊"
                : "光貿回傳多筆相同折讓單號，已停止建立作廢申請");
        if (matched[0].InvoiceStatus != UploadStatuses.Complete)
            throw new InvalidOperationException("只有已完成的折讓單可以提出作廢申請");

        var existing = ReadReview(fresh);
        if (existing is not null)
        {
            if (!string.Equals(existing.AllowanceNumber, allowanceNumber, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"這張發票已有折讓單 {existing.AllowanceNumber} 的作廢申請等待管理員處理");
            return new EmployeeAllowanceVoidWorkflowResult(
                fresh,
                existing,
                AlreadyQueued: true,
                Message: "這張折讓單已有作廢申請等待管理員處理");
        }

        var accountKey = CurrentAccountKey();
        var invoiceNumber = fresh.InvoiceNumber.Trim();
        var orderId = EffectiveOrderId(fresh);
        if (issueStore.HasUnresolved(accountKey, invoiceNumber, orderId, InvoiceAllowanceVoidIssueTypes.ManualReview))
            throw new InvalidDataException("折讓作廢待辦存在，但申請資料遺失，已停止重複建立");

        var review = new InvoiceAllowanceVoidManualReview(
            allowanceNumber,
            employee.EmployeeNo,
            reason,
            now().ToUniversalTime());
        WriteReview(fresh, review);
        syncRepository.UpsertMany([fresh]);
        try
        {
            issueStore.Record(
                accountKey,
                invoiceNumber,
                orderId,
                InvoiceAllowanceVoidIssueTypes.ManualReview,
                $"折讓單 {allowanceNumber} 作廢申請等待管理員至光貿網站人工處理。",
                now());
        }
        catch
        {
            ClearReview(fresh);
            syncRepository.UpsertMany([fresh]);
            throw;
        }

        return new EmployeeAllowanceVoidWorkflowResult(
            Reload(fresh),
            review,
            AlreadyQueued: false,
            Message: "折讓作廢申請已送交管理員人工處理");
    }

    public InvoiceAllowanceVoidManualReview? ManualReviewFor(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReadReview(record);
    }

    public void MarkManualCompleted(InvoiceSyncIssue issue, string actorEmployeeNo, string actorPassword)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);
        RequireIssue(issue);
        var record = FindIssueRecord(issue);
        _ = ReadReview(record) ?? throw new InvalidOperationException("這筆折讓作廢申請資料已不存在");
        ClearReview(record);
        syncRepository.UpsertMany([record]);
        issueStore.Resolve(issue.Id, now());
    }

    public void CancelManualReview(InvoiceSyncIssue issue, string actorEmployeeNo, string actorPassword)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);
        RequireIssue(issue);
        var record = FindIssueRecord(issue);
        _ = ReadReview(record) ?? throw new InvalidOperationException("這筆折讓作廢申請資料已不存在");
        ClearReview(record);
        syncRepository.UpsertMany([record]);
        issueStore.Resolve(issue.Id, now());
    }

    private static InvoiceAllowanceVoidManualReview? ReadReview(InvoiceRecord record)
    {
        if (record.ExtensionData is null || !record.ExtensionData.TryGetValue(MetadataKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        try { return value.Deserialize<InvoiceAllowanceVoidManualReview>(); }
        catch (JsonException error) { throw new InvalidDataException("折讓作廢申請資料格式損壞", error); }
    }

    private static void WriteReview(InvoiceRecord record, InvoiceAllowanceVoidManualReview review)
    {
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[MetadataKey] = JsonSerializer.SerializeToElement(review);
    }

    private static void ClearReview(InvoiceRecord record)
    {
        record.ExtensionData?.Remove(MetadataKey);
        if (record.ExtensionData is { Count: 0 }) record.ExtensionData = null;
    }

    private void RequireIssue(InvoiceSyncIssue issue)
    {
        if (!string.Equals(issue.IssueType, InvoiceAllowanceVoidIssueTypes.ManualReview, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆待辦不是折讓作廢人工處理");
        if (issue.ResolvedUtc is not null) throw new InvalidOperationException("這筆折讓作廢待辦已經結案");
    }

    private InvoiceRecord FindIssueRecord(InvoiceSyncIssue issue)
    {
        var matches = repository.Invoices.LoadOrCreate()
            .Where(record =>
                (issue.InvoiceNumber.Trim().Length != 0 && string.Equals(record.InvoiceNumber.Trim(), issue.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (issue.OrderId.Trim().Length != 0 && string.Equals(EffectiveOrderId(record), issue.OrderId.Trim(), StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(matches.Length == 0
                ? "本機找不到這筆折讓作廢申請對應的發票"
                : "折讓作廢申請對到多筆本機發票，已停止處理");
        return matches[0];
    }

    private InvoiceRecord Reload(InvoiceRecord record) =>
        repository.Invoices.LoadOrCreate().SingleOrDefault(item => item.Id == record.Id) ?? record;

    private void ValidateCurrentAccount(InvoiceRecord record)
    {
        var settings = repository.Settings.LoadOrCreate();
        if (!string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            throw new InvalidOperationException("這張發票屬於其他環境，請切換到正確環境後再操作");
        var seller = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (record.SellerInvoice.Trim().Length != 0 && !string.Equals(record.SellerInvoice.Trim(), seller, StringComparison.Ordinal))
            throw new InvalidOperationException("這張發票不屬於目前公司帳號");
    }

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var seller = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (seller.Length == 0) throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return settings.Environment + "|" + seller;
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();
}
