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
    string BuyerName, string NetAmount, string TaxAmount, string OfficialSnapshotHash);

public sealed partial class EmployeeAllowanceVoidWorkflowService
{
    private static readonly HttpClient VoidApiHttp = new(new HttpClientHandler { AllowAutoRedirect=false }) {
        Timeout=TimeSpan.FromSeconds(20) };
    public LocalAllowanceVoidApproval? LocalApprovalFor(InvoiceSyncIssue issue) =>
        IsLocalApiMode ? new LocalAllowanceVoidStore(repository.DataDirectory).Find(CurrentAccountKey(), issue.Id) : null;

    // A local identity toggle is not an ownership handoff. Once the current
    // settings retain a Cloud Device/workspace or CYID authority, Local SQLite
    // cannot prove that a different Device has not claimed the same g0501.
    // This is a conservative local-side fence, NOT global mutual exclusion.
    private bool IsLocalApiMode
    {
        get
        {
            var settings = repository.Settings.LoadOrCreate();
            return repository.IdentityProvider.Kind == IdentityProviderKind.Local &&
                settings.CloudMode == CloudModes.LocalOnly &&
                !settings.CloudVoidLocalSendBlocked &&
                settings.CloudWorkspaceId.Length == 0 &&
                settings.CloudDeviceId.Length == 0 &&
                settings.CloudIdentityProvider != "CYID" &&
                !settings.CloudEmployeeAuthorityReady &&
                settings.CloudCompanyAppliedRevision == 0;
        }
    }

    private EmployeeAccount RequireLocalApiManager(string employee, string password)
    {
        if (!IsLocalApiMode) throw new InvalidOperationException(
            "本機仍有 Cloud／CYID 裝置或公司授權紀錄，無法證明折讓作廢只有單一送出權威；禁止由 Local 送出或核准，請依原申請 ID 查回並完成受控交接。");
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
            request.Record.BuyerIdentifier, request.Review, Configuration=Hash(key) });
    }

    private IAmegoAllowanceVoidGateway ApiGateway()
    {
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
            item.InvoiceType != "D0401" || item.InvoiceStatus != UploadStatuses.Complete ||
            item.AllowanceType != 2 || item.CancelDate != 0 ||
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
        if (status.AllowanceNumber != number || status.Type != "D0401" || status.Status != UploadStatuses.Complete)
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
            item.BuyerName, item.TotalAmount, item.TaxAmount, Hash(item));
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
        cancellationToken.ThrowIfCancellationRequested();
        var sent = await SendLocalVoidOnceAsync(issue, employee, password,
            "作廢 " + approval.AllowanceNumber, api, cancellationToken).ConfigureAwait(false);
        try
        {
            // A code=0 by itself is only an ACK. The official source must
            // separately show that the original number is actually void.
            return await ReadbackLocalVoidAsync(issue, employee, password,
                api, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The financial attempt was already claimed: never turn a failed
            // readback into another provider POST or a false "not sent".
            return sent;
        }
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

    public async Task<LocalAllowanceVoidResult> SendLocalVoidOnceAsync(InvoiceSyncIssue issue,
        string employee, string password, string typedConfirmation,
        IAmegoAllowanceVoidGateway? gateway = null, CancellationToken cancellationToken = default)
    {
        var manager = RequireLocalApiManager(employee, password);
        var store = new LocalAllowanceVoidStore(repository.DataDirectory);
        var approval = store.Find(CurrentAccountKey(), issue.Id)
            ?? throw new InvalidOperationException("請先由管理員核准折讓作廢");
        // The legacy ExpiresUtc/text-confirmation fields are retained for
        // historical compatibility, not for new runtime authorization.
        // Every call still re-authenticates the current manager and rechecks
        // the frozen request, original official number and durable send claim.
        if (approval.Claimed || approval.ManagerEmployeeNo != manager.EmployeeNo ||
            RequestHash(issue) != approval.RequestHash)
            throw new InvalidOperationException("原號已送出、主管或作廢申請已變更；只能查回原號");
        var api = gateway ?? ApiGateway();
        if (Hash(await InspectVoidTargetAsync(issue, api, cancellationToken).ConfigureAwait(false)) != approval.OfficialHash)
            throw new InvalidOperationException("官方明細不同於核准時，請重新覆核");
        RequireLocalApiManager(employee, password);
        if (RequestHash(issue) != approval.RequestHash) throw new InvalidOperationException("作廢申請或公司設定已變更");
        cancellationToken.ThrowIfCancellationRequested();
        store.Claim(approval, now()); // Durable unique claim before ANY g0501.
        var kind = "unknown_result";
        try
        {
            var response = await api.VoidAllowanceOnceAsync(approval.AllowanceNumber, cancellationToken).ConfigureAwait(false);
            kind = response.Code == 0 ? "accepted_unconfirmed" : "rejected_unconfirmed";
        }
        catch (AmegoApiException) { kind = "rejected_unconfirmed"; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException)
        { /* A claimed attempt is never released, even if no response arrives. */ }
        var recorded = true;
        try { store.Observe(approval, kind, "", manager.EmployeeNo, now()); }
        catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException) { recorded=false; }
        return new(approval.AllowanceNumber, false, recorded,
            "已保存原號送出紀錄；結果尚待官方查回，不得重送或換號。" + (recorded ? "" : " 結果稽核未寫入，原送出鎖仍保留。"));
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
        if (observed)
            new InvoiceSyncIssueStore(repository.DataDirectory).Resolve(issue.Id, now());
        return new(approval.AllowanceNumber, observed, true, observed
            ? "官方確認原號目前已作廢，原待辦已結案；仍保留本次送出、核准及官方查回歷史。"
            : "官方尚未確認原號已作廢，保留送出紀錄並等待下次原號查回，不得盲目重送。");
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
