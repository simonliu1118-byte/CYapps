using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed record LocalAllowanceVoidResult(string AllowanceNumber, bool OfficialVoidObserved,
    bool AuditRecorded, string Message);
public sealed record LocalAllowanceVoidPreview(string AllowanceNumber, string OriginalInvoiceNumber,
    string BuyerName, string NetAmount, string TaxAmount, string OfficialSnapshotHash, AllowanceQueryItem? OriginalAllowance = null);

public sealed partial class EmployeeAllowanceVoidWorkflowService
{
    private static readonly HttpClient VoidApiHttp = new(new HttpClientHandler { AllowAutoRedirect=false }) {
        Timeout=TimeSpan.FromSeconds(20) };
    public LocalAllowanceVoidApproval? LocalApprovalFor(InvoiceSyncIssue issue) =>
        IsLocalApiMode ? new LocalAllowanceVoidStore(repository.DataDirectory).Find(CurrentAccountKey(), issue.Id) : null;

    private bool IsLocalApiMode => repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
        repository.Settings.LoadOrCreate().CloudMode == CloudModes.LocalOnly;

    private EmployeeAccount RequireLocalApiManager(string employee, string password)
    {
        if (!IsLocalApiMode) throw new InvalidOperationException("此申請須由原交易權威處理");
        return EmployeeOperationAuthentication.AuthenticateManager(repository, employee, password);
    }

    private void RequireNoApiClaim(InvoiceSyncIssue issue)
    {
        var store = new LocalAllowanceVoidStore(repository.DataDirectory);
        // A new local issue ID, including one recreated after a mode change, is
        // not proof that the original allowance number was never submitted.
        var review = ReadReview(FindIssueRecord(issue));
        if (store.Find(issue.AccountKey, issue.Id)?.Claimed == true ||
            (review is not null && store.HasClaimForAllowance(issue.AccountKey, review.AllowanceNumber)))
            throw new InvalidOperationException("原折讓單號已有送出紀錄，不可再次送出、人工結案或取消；只能依原申請查回");
    }

    private (InvoiceRecord Record, InvoiceAllowanceVoidManualReview Review) ApiRequest(InvoiceSyncIssue issue)
    {
        RequireIssue(issue);
        if (issue.AccountKey != CurrentAccountKey() ||
            !issueStore.Unresolved(issue.AccountKey).Any(x => x.Id == issue.Id &&
                x.InvoiceNumber == issue.InvoiceNumber && x.OrderId == issue.OrderId &&
                x.IssueType == InvoiceAllowanceVoidIssueTypes.ManualReview))
            throw new InvalidOperationException("折讓作廢待辦已失效或不屬於目前公司／環境");
        var record = FindIssueRecord(issue);
        ValidateCurrentAccount(record);
        return (record, ReadReview(record) ?? throw new InvalidDataException("折讓作廢申請資料已遺失"));
    }

    private string RequestHash(InvoiceSyncIssue issue)
    {
        var request = ApiRequest(issue);
        var settings = repository.Settings.LoadOrCreate();
        // Hash changes when the local company key changes. The key itself is
        // never put in SQLite approval/audit or returned to the UI.
        var key = settings.Environment == Environments.Test ? AmegoDefaults.TestAppKey :
            repository.Settings.ProductionAppKey(settings);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未設定光貿 App Key");
        return Hash(new { Account=CurrentAccountKey(), issue.Id, request.Record.InvoiceNumber,
            request.Record.BuyerIdentifier,
            request.Review.AllowanceNumber, request.Review.RequesterEmployeeNo,
            request.Review.Reason, request.Review.RequestedUtc, Configuration=Hash(key) });
    }

    private IAmegoAllowanceVoidGateway ApiGateway()
    {
        if (allowanceGateway is not null) return allowanceGateway;
        var settings = repository.Settings.LoadOrCreate();
        var seller = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        var key = settings.Environment == Environments.Test ? AmegoDefaults.TestAppKey : repository.Settings.ProductionAppKey(settings);
        return new AmegoClient(seller, key, VoidApiHttp);
    }

    private async Task<AllowanceQueryItem> InspectVoidTargetAsync(InvoiceSyncIssue issue, IAmegoAllowanceVoidGateway gateway,
        CancellationToken token)
    {
        var request = ApiRequest(issue);
        var number = request.Review.AllowanceNumber;
        AmegoClient.ValidateOfficialAllowanceNumber(number);
        var result = await gateway.QueryAllowanceAsync(number, token).ConfigureAwait(false);
        var item = result.Data;
        if (result.Code != 0 || item is null || item.AllowanceNumber != number ||
            item.InvoiceType is not ("D0401" or "D0501") || item.InvoiceStatus != UploadStatuses.Complete ||
            item.AllowanceType != 2 ||
            string.IsNullOrWhiteSpace(item.BuyerIdentifier) ||
            item.BuyerIdentifier != request.Record.BuyerIdentifier ||
            item.ProductItems.ValueKind != JsonValueKind.Array || item.ProductItems.GetArrayLength() is < 1 or > 9999)
            throw new InvalidOperationException("官方原號不是目前買方已完成、尚未作廢的賣方折讓單");
        foreach (var line in item.ProductItems.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object ||
                !line.TryGetProperty("original_invoice_number", out var original) ||
                original.ValueKind != JsonValueKind.String || original.GetString() != request.Record.InvoiceNumber)
                throw new InvalidOperationException("折讓原發票不明或含其他發票，禁止由此申請整張作廢");
        }
        var status = await gateway.ReadAllowanceVoidStatusAsync(number, token).ConfigureAwait(false);
        if (status.AllowanceNumber != number || (!status.OfficiallyVoided &&
            (status.Type != "D0401" || status.Status != UploadStatuses.Complete || item.InvoiceType != "D0401" || item.CancelDate != 0)))
            throw new InvalidOperationException("官方折讓上傳狀態已變更，禁止作廢");
        return item;
    }

    public async Task<LocalAllowanceVoidPreview> PreviewLocalVoidAsync(InvoiceSyncIssue issue,
        string employee, string password, IAmegoAllowanceVoidGateway? gateway = null,
        CancellationToken cancellationToken = default)
    {
        RequireLocalApiManager(employee, password);
        RequireNoApiClaim(issue);
        var before = RequestHash(issue);
        var item = await InspectVoidTargetAsync(issue, gateway ?? ApiGateway(), cancellationToken).ConfigureAwait(false);
        RequireLocalApiManager(employee, password);
        if (RequestHash(issue) != before) throw new InvalidOperationException("作廢申請或公司設定已變更");
        return new(item.AllowanceNumber, ApiRequest(issue).Record.InvoiceNumber,
            item.BuyerName, item.TotalAmount, item.TaxAmount, Hash(item), item);
    }

    // One manager confirmation executes the existing verified approval and
    // durable claim within the same active operation. No second UI password,
    // text challenge or delayed approval window is required.
    public async Task<LocalAllowanceVoidResult> ApproveAndExecuteLocalVoidAsync(
        InvoiceSyncIssue issue, string employee, string password,
        string reviewedOfficialHash, IAmegoAllowanceVoidGateway? gateway = null,
        CancellationToken cancellationToken = default)
    {
        var api = gateway ?? ApiGateway();
        var approval = await ApproveLocalVoidAsync(issue, employee, password,
            reviewedOfficialHash, api, cancellationToken).ConfigureAwait(false);
        return await ExecuteApprovedLocalVoidAsync(issue, api, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalAllowanceVoidResult> ExecuteApprovedLocalVoidAsync(
        InvoiceSyncIssue issue, IAmegoAllowanceVoidGateway? gateway = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = new LocalAllowanceVoidStore(repository.DataDirectory);
        var approval = store.Find(CurrentAccountKey(), issue.Id)
            ?? throw new InvalidOperationException("缺少原主管核准");
        var api = gateway ?? ApiGateway();
        try
        {
            EmployeeOperationAuthentication.RequireApprovedLocalManager(repository, approval.ManagerEmployeeNo);
            if (RequestHash(issue) != approval.RequestHash)
                throw new InvalidOperationException("原核准內容或公司設定已改變");
            // Always inspect the original official number before any retry.
            var status = await api.ReadAllowanceVoidStatusAsync(approval.AllowanceNumber, cancellationToken).ConfigureAwait(false);
            if (status.AllowanceNumber != approval.AllowanceNumber)
                throw new InvalidDataException("官方查回的折讓號碼不符");
            if (status.OfficiallyVoided)
            {
                if (!approval.Claimed) store.Claim(approval, now());
                store.Observe(approval, "official_void_observed_unattributed", Hash(status), approval.ManagerEmployeeNo, now());
                CompleteLocalVoidIssue(issue);
                return new(approval.AllowanceNumber, true, true, "官方確認原折讓已作廢，已自動結案");
            }
            if (status.Type != "D0401" || status.Status != UploadStatuses.Complete)
            {
                var waiting = InvoiceWorkQueue.IsPendingUpload(status.Status);
                SetLocalVoidProblem(issue, !waiting, waiting ? "官方處理中，系統會自動追蹤" : "官方狀態不符，已停止送出");
                return new(approval.AllowanceNumber, false, true, waiting ? "官方處理中" : "官方狀態異常，請查看上傳問題");
            }
            var target = await InspectVoidTargetAsync(issue, api, cancellationToken).ConfigureAwait(false);
            if (Hash(target) != approval.OfficialHash) throw new InvalidOperationException("原折讓內容已異於主管核准，已停止送出");
            EmployeeOperationAuthentication.RequireApprovedLocalManager(repository, approval.ManagerEmployeeNo);
            if (RequestHash(issue) != approval.RequestHash)
                throw new InvalidOperationException("核准內容已改變");
            if (!approval.Claimed) store.Claim(approval, now());
            var attempt = store.StartAttempt(approval, now(), !approval.Claimed);
            if (attempt == 0)
            {
                var count = store.AttemptCount(approval);
                var problem = count is 0 or >= 3 || store.LastProviderCode(approval) is not (null or 0 or 4050132 or 4050141);
                var message = problem ? "原號仍未作廢；歷史送出缺少嘗試證據、官方明確拒絕或已達安全重試上限，請匯出問題診斷" : "等待官方結果與下一次安全查證";
                SetLocalVoidProblem(issue, problem, message);
                return new(approval.AllowanceNumber, false, true, message);
            }
            IssueDiagnosticLog.Record(repository.DataDirectory, issue.Id, "send_started", InvoiceAllowanceVoidIssueTypes.ManualReview, attempt: attempt, responseReceived: false);
            int? code = null;
            try
            {
                var response = await api.VoidAllowanceOnceAsync(approval.AllowanceNumber, cancellationToken).ConfigureAwait(false);
                code = response.Code;
            }
            catch (AmegoApiException error) { code = error.Code; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException)
            {
                IssueDiagnosticLog.Record(repository.DataDirectory, issue.Id, "response_unknown", InvoiceAllowanceVoidIssueTypes.ManualReview, error: error, attempt: attempt, responseReceived: false);
            }
            if (code is not null) store.RecordResponse(approval, attempt, code);
            IssueDiagnosticLog.Record(repository.DataDirectory, issue.Id, "response_observed", InvoiceAllowanceVoidIssueTypes.ManualReview, code, attempt: attempt, responseReceived: code is not null);
            store.Observe(approval, code == 0 ? "accepted_unconfirmed" : code is null ? "unknown_result" : "rejected_unconfirmed", "", approval.ManagerEmployeeNo, now());
            var after = await api.ReadAllowanceVoidStatusAsync(approval.AllowanceNumber, cancellationToken).ConfigureAwait(false);
            if (after.AllowanceNumber != approval.AllowanceNumber) throw new InvalidDataException("作廢原號不符");
            if (after.OfficiallyVoided)
            {
                store.Observe(approval, "official_void_observed_unattributed", Hash(after), approval.ManagerEmployeeNo, now());
                CompleteLocalVoidIssue(issue);
                return new(approval.AllowanceNumber, true, true, "原折讓已由官方確認作廢");
            }
            var normalWait = code is 0 or 4050141 || InvoiceWorkQueue.IsPendingUpload(after.Status);
            var messageAfter = normalWait ? "官方尚在處理作廢，系統會自動查回" : "作廢結果查證仍未確認；原號及嘗試證據已保存，系統會依安全條件查回／有限重試";
            SetLocalVoidProblem(issue, !normalWait, messageAfter);
            return new(approval.AllowanceNumber, false, true, messageAfter);
        }
        catch (Exception error)
        {
            SetLocalVoidProblem(issue, true, "折讓作廢自動處理未完成：" + error.Message);
            IssueDiagnosticLog.Record(repository.DataDirectory, issue.Id, "execution_failed", InvoiceAllowanceVoidIssueTypes.ManualReview, error: error);
            throw;
        }
    }

    private void CompleteLocalVoidIssue(InvoiceSyncIssue issue)
    {
        var record = FindIssueRecord(issue);
        if (ReadReview(record) is { } review)
        {
            record.ExtensionData!["cyinvoice_allowance_void_handled_" + issue.Id] = JsonSerializer.SerializeToElement(review);
            ClearReview(record);
            syncRepository.UpsertMany([record], completedIssue: issue);
        }
        else issueStore.Resolve(issue.Id, now());
    }

    private void SetLocalVoidProblem(InvoiceSyncIssue issue, bool problem, string message)
    {
        var current = issueStore.All(issue.AccountKey).SingleOrDefault(i => i.Id == issue.Id);
        if (current?.ResolvedUtc is not null) return;
        var record = FindIssueRecord(issue);
        var review = ReadReview(record);
        if (review is null) { issueStore.UpdateMessage(issue.Id, message); return; }
        record.ExtensionData![MetadataKey] = JsonSerializer.SerializeToElement(review with { ConfirmationProblem = problem });
        syncRepository.UpsertMany([record]);
        issueStore.UpdateMessage(issue.Id, message);
    }

    public async Task<LocalAllowanceVoidApproval> ApproveLocalVoidAsync(InvoiceSyncIssue issue,
        string employee, string password, string reviewedOfficialHash, IAmegoAllowanceVoidGateway? gateway = null,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireLocalApiManager(employee, password);
        RequireNoApiClaim(issue);
        var requestHash = RequestHash(issue);
        var officialHash = Hash(await InspectVoidTargetAsync(issue, gateway ?? ApiGateway(), cancellationToken).ConfigureAwait(false));
        if (officialHash != reviewedOfficialHash) throw new InvalidOperationException("官方折讓明細已變更，請重新預覽覆核");
        RequireLocalApiManager(employee, password);
        if (RequestHash(issue) != requestHash) throw new InvalidOperationException("作廢申請或公司設定已變更");
        cancellationToken.ThrowIfCancellationRequested();
        var approval = new LocalAllowanceVoidApproval(Guid.NewGuid().ToString("N"), issue.Id,
            CurrentAccountKey(), ApiRequest(issue).Review.AllowanceNumber, requestHash,
            officialHash, manager.EmployeeNo, now().ToUniversalTime(), false, "");
        new LocalAllowanceVoidStore(repository.DataDirectory).Approve(approval, now());
        return approval;
    }

    public async Task<LocalAllowanceVoidResult> ReadbackLocalVoidAsync(InvoiceSyncIssue issue,
        string employee, string password, IAmegoAllowanceVoidGateway? gateway = null,
        CancellationToken cancellationToken = default)
    {
        var manager = RequireLocalApiManager(employee, password);
        if (issue.AccountKey != CurrentAccountKey()) throw new InvalidOperationException("作廢待辦公司／環境不符");
        var store = new LocalAllowanceVoidStore(repository.DataDirectory);
        var approval = store.Find(CurrentAccountKey(), issue.Id);
        if (approval is null || !approval.Claimed) throw new InvalidOperationException("原號尚未進入 API 送出狀態");
        var status = await (gateway ?? ApiGateway()).ReadAllowanceVoidStatusAsync(
            approval.AllowanceNumber, cancellationToken).ConfigureAwait(false);
        RequireLocalApiManager(employee, password);
        if (CurrentAccountKey() != approval.AccountKey || status.AllowanceNumber != approval.AllowanceNumber)
            throw new InvalidOperationException("官方查回的折讓單號或目前公司已改變");
        var observed = status.OfficiallyVoided;
        store.Observe(approval, observed ? "official_void_observed_unattributed" : "official_void_unconfirmed", Hash(status), manager.EmployeeNo, now());
        if (observed) CompleteLocalVoidIssue(issue);
        return new(approval.AllowanceNumber, observed, true, observed
            ? "官方確認原號目前已作廢，原待辦已結案；仍保留本次送出、核准及官方查回歷史。"
            : "官方尚未確認原號已作廢，保留送出紀錄並等待下次原號查回，不得盲目重送。");
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
