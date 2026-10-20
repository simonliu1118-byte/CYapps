using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

// V3.2-C4d: one canonical applicant-side owner. A Cloud allowance application
// ID is persisted in Local SQLite BEFORE the first Cloud write. No retry,
// hidden second POST, provider mutation or number allocation occurs here.
public sealed record CloudAllowanceSubmissionResult(
    string ApplicationId, bool AlreadySubmitted, string Status);

internal sealed record PendingCloudAllowanceMarker(
    string ApplicationId, string WorkspaceId, string DeviceId,
    string Environment, string OriginalInvoiceNumber, long RequestedGross,
    string TemplateSha256, DateTimeOffset CreatedAtUtc);

public sealed class CloudAllowanceSubmissionService
{
    private const string PendingKey = "cyinvoice_pending_v32_cloud_allowance";
    private static readonly HttpClient SharedHttp =
        new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly LocalRepository repository;
    private readonly HttpClient http;
    private readonly InvoiceDetailRefreshService refresh;

    public CloudAllowanceSubmissionService(
        LocalRepository repository, HttpClient? http = null,
        InvoiceDetailRefreshService? refresh = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.http = http ?? SharedHttp;
        this.refresh = refresh ?? new InvoiceDetailRefreshService(repository);
    }

    public static bool HasPending(InvoiceRecord invoice) =>
        invoice.ExtensionData?.ContainsKey(PendingKey) == true;

    // The AMEGO-typed, unnumbered JSON is the only financial payload sent to
    // the Worker. It is NOT persisted as plaintext pending application state.
    public static CloudAllowanceDraft BuildDraft(
        string applicationId, InvoiceRecord invoice, DateOnly allowanceDate,
        long gross, string reason, IReadOnlyList<AllowanceProductItem> edited)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(edited);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                applicationId ?? "", @"^[A-Za-z0-9_-]{8,100}$") ||
            gross <= 0 || gross > int.MaxValue || string.IsNullOrWhiteSpace(reason) ||
            edited.Count is < 1 or > 50)
            throw new InvalidOperationException("折讓草稿編號、金額、原因或商品無效");
        if (edited.Any(x => x.OriginalInvoiceNumber != invoice.InvoiceNumber ||
                x.OriginalInvoiceDate <= 0 || x.Quantity <= 0 ||
                x.TaxType is < 1 or > 3))
            throw new InvalidOperationException("折讓商品與原發票或稅別不一致");
        decimal net = 0m, tax = 0m;
        foreach (var item in edited)
        {
            if (item.OriginalDescription.Length is < 1 or > 256 ||
                (item.TaxType != 1 && item.Tax != 0m))
                throw new InvalidOperationException("折讓商品內容或稅別不一致");
            net = checked(net + item.Amount);
            tax = checked(tax + item.Tax);
        }
        if (net <= 0m || tax < 0m || net + tax != gross)
            throw new InvalidOperationException("折讓金額與各品項總額不一致");

        AllowanceDraftValidator.Validate(edited, invoice.InvoiceNumber,
            invoice.InvoiceDate, gross);
        var payload = new AllowanceIssueRequest
        {
            AllowanceNumber = "",
            AllowanceDate = allowanceDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            AllowanceType = 2,
            BuyerIdentifier = invoice.BuyerIdentifier,
            BuyerName = invoice.BuyerName,
            ProductItem = edited.ToArray(),
            TotalAmount = net,
            TaxAmount = tax
        };
        var environment = invoice.Environment is "production" ? "production" : "test";
        return new CloudAllowanceDraft(applicationId, environment, invoice.InvoiceNumber,
            payload.AllowanceDate, gross, JsonSerializer.Serialize(payload));
    }

    public async Task<CloudAllowanceSubmissionResult> SubmitAsync(
        InvoiceRecord selected, string employeeNo, string password,
        string reason, long gross, IReadOnlyList<AllowanceProductItem> edited,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var settings = repository.Settings.LoadOrCreate();
        if (settings.CloudMode != CloudModes.CloudPreferred ||
            settings.CloudIdentityProvider != "CYID" ||
            !settings.CloudEmployeeAuthorityReady ||
            string.IsNullOrWhiteSpace(settings.CloudWorkspaceId) ||
            string.IsNullOrWhiteSpace(settings.CloudDeviceId) ||
            !Uri.TryCreate(settings.CloudBaseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("CYID Cloud 尚未具備可信的 Device 與 HTTPS 設定");

        var token = repository.Settings.CloudDeviceToken(settings);
        var api = new CloudAllowanceClient(http, endpoint, token);
        var record = repository.Invoices.LoadOrCreate()
            .SingleOrDefault(x => x.Id == selected.Id)
            ?? throw new InvalidOperationException("找不到本機原發票，禁止建立雲端折讓");
        var pending = ReadPending(record);
        if (pending is not null)
        {
            RequirePendingScope(pending, record, settings.CloudWorkspaceId,
                settings.CloudDeviceId, settings.Environment);
            // Re-entry is READBACK ONLY. Even a confirmed 404 does not
            // authorize a new ID or silent reuse of the previous POST.
            try
            {
                var state = await api.StatusAsync(pending.ApplicationId, employeeNo,
                    password, cancellationToken).ConfigureAwait(false);
                return new CloudAllowanceSubmissionResult(pending.ApplicationId,
                    AlreadySubmitted: true, state.Status);
            }
            catch (Exception error) when (error is HttpRequestException or
                   TaskCanceledException or CloudApiException or InvalidDataException)
            {
                throw new InvalidOperationException(
                    "原折讓申請 " + pending.ApplicationId +
                    " 尚無法確認。已保留原 ID，禁止建立新申請或盲目重送；請依原 ID 人工查回。",
                    error);
            }
        }

        // Bounded preflight: at most one employee-scoped list read before
        // attempting the first write. The schema16 / feature-off Worker must
        // reject here, BEFORE a local pending ID is durably locked in.
        await api.ListAsync(employeeNo, password, limit: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var fresh = await refresh.RefreshAsync(record, cancellationToken)
            .ConfigureAwait(false);
        if (fresh.InvoiceNumber != selected.InvoiceNumber ||
            fresh.Environment != settings.Environment ||
            fresh.UploadStatus != UploadStatuses.Complete ||
            fresh.InvoiceState == InvoiceStates.Voided)
            throw new InvalidOperationException("原發票狀態已改變，請重新開啟明細");
        var remaining = InvoiceAllowanceMetadata.RemainingGross(fresh);
        if (FixedDecimal.FromInt64(gross).ScaledValue > remaining.ScaledValue)
            throw new InvalidOperationException("折讓額度超過光貿最新可用餘額");
        if (new EmployeeAllowanceWorkflowService(repository).ManualReviewFor(fresh) is not null)
            throw new InvalidOperationException("原發票仍有舊人工折讓待辦，請先完成覆核");

        var id = "application_" + Guid.NewGuid().ToString("N");
        var draft = BuildDraft(id, fresh, DateOnly.FromDateTime(DateTime.Today),
            gross, reason, edited);
        if (draft.Environment != settings.Environment)
            throw new InvalidOperationException("申請環境與 CYID Cloud 設定不同");
        var checksum = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(draft.UnnumberedTemplateJson))).ToLowerInvariant();
        var marker = new PendingCloudAllowanceMarker(
            id, settings.CloudWorkspaceId, settings.CloudDeviceId,
            draft.Environment, fresh.InvoiceNumber, gross, checksum,
            DateTimeOffset.UtcNow);

        // The applicant must never lose the ID even when Cloud accepted the
        // POST but the reply was lost. The local write must commit first.
        fresh.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (HasPending(fresh))
            throw new InvalidOperationException("原發票已有待查 Cloud 折讓，禁止重複建立");
        fresh.ExtensionData[PendingKey] = JsonSerializer.SerializeToElement(marker);
        new InvoiceSyncRepository(repository.DataDirectory).UpsertMany([fresh],
            cancellationToken: cancellationToken);
        try
        {
            var receipt = await api.CreateAsync(draft, employeeNo, password,
                cancellationToken).ConfigureAwait(false);
            return new CloudAllowanceSubmissionResult(id, AlreadySubmitted: false,
                receipt.Status);
        }
        catch (Exception error) when (error is HttpRequestException or
               TaskCanceledException or CloudApiException or InvalidDataException)
        {
            throw new InvalidOperationException(
                "Cloud 折讓申請 " + id +
                " 回覆不明；原 ID 已在本機保存。請查回這個 ID，切勿再按建立或重送。",
                error);
        }
    }

    private static PendingCloudAllowanceMarker? ReadPending(InvoiceRecord record)
    {
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(PendingKey, out var json))
            return null;
        try
        {
            var marker = json.Deserialize<PendingCloudAllowanceMarker>();
            if (marker is not null &&
                marker.ApplicationId.StartsWith("application_", StringComparison.Ordinal) &&
                marker.TemplateSha256.Length == 64)
                return marker;
        }
        catch (JsonException) { }
        throw new InvalidDataException("原折讓申請的本機 ID 記錄損壞，禁止重複建立");
    }

    private static void RequirePendingScope(
        PendingCloudAllowanceMarker pending, InvoiceRecord invoice,
        string workspace, string device, string environment)
    {
        if (pending.WorkspaceId != workspace ||
            pending.DeviceId != device ||
            pending.Environment != environment ||
            pending.OriginalInvoiceNumber != invoice.InvoiceNumber)
            throw new InvalidOperationException(
                "原 Cloud 折讓申請屬於其他 Workspace／Device／環境，禁止重用");
    }
}
