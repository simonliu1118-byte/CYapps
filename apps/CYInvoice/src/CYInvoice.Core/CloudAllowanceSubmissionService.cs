using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

// Uses the existing invoice editor and CYID Work Item APIs. The Work Item ID
// and frozen request are committed to the local invoice BEFORE the first
// network mutation. Lost replies must be read back using the SAME ID.
public sealed record CloudAllowanceSubmissionResult(string ItemId, bool AlreadySubmitted);

internal sealed record PendingCloudAllowance(
    string WorkspaceId, string DeviceId, string Environment,
    CloudAllowanceApplicationDraft Draft);

public sealed class CloudAllowanceSubmissionService
{
    private const string PendingKey = "cyinvoice_pending_cloud_allowance";
    private static readonly HttpClient SharedHttp = new();
    private readonly LocalRepository repository;
    private readonly HttpClient http;
    private readonly InvoiceDetailRefreshService refresh;

    public CloudAllowanceSubmissionService(LocalRepository repository,
        HttpClient? http = null, InvoiceDetailRefreshService? refresh = null)
    {
        this.repository=repository??throw new ArgumentNullException(nameof(repository));
        this.http=http??SharedHttp;
        this.refresh=refresh??new InvoiceDetailRefreshService(repository);
    }

    public static bool HasPending(InvoiceRecord invoice) =>
        invoice.ExtensionData?.ContainsKey(PendingKey)==true;

    public static CloudAllowanceApplicationDraft BuildDraft(
        string itemId, InvoiceRecord invoice, DateOnly allowanceDate,
        long gross, string reason, IReadOnlyList<AllowanceProductItem> edited)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(edited);
        if(gross<=0||gross>int.MaxValue||string.IsNullOrWhiteSpace(reason)||
           edited.Count is <1 or >50)
            throw new InvalidOperationException("折讓總額、原因或品項無效");
        if(edited.Any(x=>x.OriginalInvoiceNumber!=invoice.InvoiceNumber||
                         x.OriginalInvoiceDate<=0||
                         x.Quantity<=0||x.TaxType is <1 or >3))
            throw new InvalidOperationException("折讓品項原發票或課稅資料無效");
        decimal net=0m,tax=0m;
        foreach(var item in edited)
        {
            if(item.OriginalDescription.Length is <1 or >256||
               (item.TaxType!=1&&item.Tax!=0m))
                throw new InvalidOperationException("折讓品項單價、小計或稅別不一致");
            net=checked(net+item.Amount);
            tax=checked(tax+item.Tax);
        }
        if(net<=0m||tax<0m||net+tax!=gross)
            throw new InvalidOperationException("折讓明細加總與含稅申請金額不一致");
        var request=new AllowanceIssueRequest
        {
            AllowanceNumber="", AllowanceDate=allowanceDate.ToString("yyyyMMdd",
                CultureInfo.InvariantCulture),AllowanceType=2,
            BuyerIdentifier=invoice.BuyerIdentifier,BuyerName=invoice.BuyerName,
            ProductItem=edited.ToArray(),TotalAmount=net,TaxAmount=tax
        };
        return new CloudAllowanceApplicationDraft(itemId,invoice.InvoiceNumber,
            allowanceDate,checked((int)gross),reason.Trim(),request);
    }

    public async Task<CloudAllowanceSubmissionResult> SubmitAsync(
        InvoiceRecord selected, string employeeNo, string password,
        string reason, long gross, IReadOnlyList<AllowanceProductItem> edited,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var settings=repository.Settings.LoadOrCreate();
        if(settings.CloudMode!=CloudModes.CloudPreferred||
           settings.CloudIdentityProvider!="CYID"||
           !settings.CloudEmployeeAuthorityReady||
           settings.CloudWorkspaceId.Length==0||settings.CloudDeviceId.Length==0)
            throw new InvalidOperationException("CYID Cloud 尚未就緒，不能建立雲端折讓申請");
        if(!Uri.TryCreate(settings.CloudBaseUrl,UriKind.Absolute,out var endpoint)||
           endpoint.Scheme!=Uri.UriSchemeHttps)
            throw new InvalidOperationException("CYID Cloud 端點未設定或不是 HTTPS");
        var token=repository.Settings.CloudDeviceToken(settings);
        var application=new CloudAllowanceApplicationClient(http,endpoint,token);
        var record=Current(selected.Id);
        var pending=ReadPending(record);
        if(pending is not null)
        {
            VerifyScope(pending,settings,record);
            var existing=await application.GetAsync(pending.Draft.ItemId,
                employeeNo,password,cancellationToken).ConfigureAwait(false);
            if(existing is not null)
            {
                VerifyExisting(existing,pending.Draft,settings);
                if(existing.Status!="closed")
                    return new CloudAllowanceSubmissionResult(
                        pending.Draft.ItemId,AlreadySubmitted:true);
                // Only a server-confirmed closed Work Item permits a fresh
                // application. An unknown status must never be discarded.
            }
            else
            {
                // A confirmed 404 is NOT license to allocate another Work
                // Item ID. Retry the exact frozen request using the same ID.
                await CreatePersistedAsync(application,pending.Draft,
                    employeeNo,password,cancellationToken)
                    .ConfigureAwait(false);
                return new CloudAllowanceSubmissionResult(
                    pending.Draft.ItemId,AlreadySubmitted:true);
            }
        }
        var fresh=await refresh.RefreshAsync(selected,cancellationToken)
            .ConfigureAwait(false);
        if(fresh.InvoiceNumber!=selected.InvoiceNumber||
           fresh.Environment!=settings.Environment||
           fresh.UploadStatus!=UploadStatuses.Complete||
           fresh.InvoiceState==InvoiceStates.Voided)
            throw new InvalidOperationException("原發票狀態已變更，請重新開啟詳細畫面");
        var remaining=InvoiceAllowanceMetadata.RemainingGross(fresh);
        if(FixedDecimal.FromInt64(gross).ScaledValue>remaining.ScaledValue)
            throw new InvalidOperationException(
                $"本次折讓超過已扣除歷史折讓的剩餘額 {remaining} 元");
        var draft=BuildDraft(CloudAllowanceApplicationClient.CreateItemId(),
            fresh,DateOnly.FromDateTime(DateTime.Today),gross,reason,edited);
        var frozen=new PendingCloudAllowance(settings.CloudWorkspaceId,
            settings.CloudDeviceId,settings.Environment,draft);
        // Refresh must preserve the invoice extension and all existing data.
        // A failed local write MUST NOT attempt any network creation.
        fresh.ExtensionData??=new Dictionary<string,JsonElement>(
            StringComparer.Ordinal);
        fresh.ExtensionData[PendingKey]=JsonSerializer.SerializeToElement(frozen);
        new InvoiceSyncRepository(repository.DataDirectory).UpsertMany([fresh]);
        await CreatePersistedAsync(application,draft,
            employeeNo,password,cancellationToken).ConfigureAwait(false);
        return new CloudAllowanceSubmissionResult(draft.ItemId,
            AlreadySubmitted:false);
    }

    // The only employee who may retract the still-unapproved request is
    // the original CYID applicant. A manager grant or numbering ends this
    // option forever; withdrawal never calls g0501 or g0401.
    public async Task<CloudAllowanceWithdrawalReceipt> WithdrawAsync(
        InvoiceRecord selected,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var settings=repository.Settings.LoadOrCreate();
        if(settings.CloudMode!=CloudModes.CloudPreferred||
           settings.CloudIdentityProvider!="CYID"||
           !settings.CloudEmployeeAuthorityReady)
            throw new InvalidOperationException("只有 CYID Cloud 折讓申請可由原申請人撤銷");
        if(!Uri.TryCreate(settings.CloudBaseUrl,UriKind.Absolute,out var endpoint)||
           endpoint.Scheme!=Uri.UriSchemeHttps)
            throw new InvalidOperationException("CYID Cloud 尚未完成 HTTPS 設定");
        var record=Current(selected.Id);
        var pending=ReadPending(record)
            ?? throw new InvalidOperationException("本機沒有待處理的 CYID 折讓申請");
        VerifyScope(pending,settings,record);
        var client=new CloudAllowanceApplicationClient(http,endpoint,
            repository.Settings.CloudDeviceToken(settings));
        // Read latest revision; do not trust a cached open/review status.
        var current=await client.GetAsync(pending.Draft.ItemId,employeeNo,password,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "無法查回原折讓申請，不能換號撤銷或重送");
        VerifyExisting(current,pending.Draft,settings);
        if(current.Withdrawn)
        {
            RemovePending(record);
            return new CloudAllowanceWithdrawalReceipt(
                current.ItemId,current.Revision,current.WithdrawnAt
                    ?? throw new InvalidDataException("撤銷紀錄缺少伺服端確認時間"));
        }
        if(current.Status is not ("open" or "review"))
            throw new InvalidOperationException(
                "折讓已結案，不能用員工撤銷功能變更官方處理結果");
        // A timeout is ambiguous; the caller must GET the same Item ID.
        // Never reissue a withdraw request automatically.
        var result=await client.WithdrawAsync(pending.Draft.ItemId,
            current.Revision,employeeNo,password,cancellationToken)
            .ConfigureAwait(false);
        RemovePending(record);
        return result;
    }

    private void RemovePending(InvoiceRecord record)
    {
        if(record.ExtensionData is null)return;
        record.ExtensionData.Remove(PendingKey);
        new InvoiceSyncRepository(repository.DataDirectory).UpsertMany([record]);
    }

    private static async Task CreatePersistedAsync(
        CloudAllowanceApplicationClient application,
        CloudAllowanceApplicationDraft draft,
        string employeeNo,string password,CancellationToken cancellationToken)
    {
        // No automatic retry; ambiguous outcomes remain attached to the
        // persisted ID and are resolved only through Get on the next attempt.
        try
        {
            await application.CreateAsync(draft,employeeNo,password,
                cancellationToken).ConfigureAwait(false);
        }
        catch(Exception error) when(error is HttpRequestException or
            TaskCanceledException or InvalidDataException or CloudApiException)
        {
            throw new InvalidOperationException(
                "CYID 申請結果未確認。已保留 Work Item "+draft.ItemId+
                "，請使用原號查回，不要重新建立。",error);
        }
    }

    private InvoiceRecord Current(string id) =>
        repository.Invoices.LoadOrCreate().SingleOrDefault(x=>x.Id==id)
        ?? throw new InvalidOperationException("本機找不到原發票，不能建立折讓申請");

    private static PendingCloudAllowance? ReadPending(InvoiceRecord record)
    {
        if(record.ExtensionData is null||
           !record.ExtensionData.TryGetValue(PendingKey,out var raw))return null;
        try
        {
            return raw.Deserialize<PendingCloudAllowance>()
                ?? throw new InvalidDataException("CYID 折讓待處理 ID 遺失");
        }
        catch(JsonException error)
        {
            throw new InvalidDataException("CYID 折讓待處理資料已損毀，不能重複申請",error);
        }
    }

    private static void VerifyScope(PendingCloudAllowance pending,
        Settings settings,InvoiceRecord record)
    {
        if(pending.WorkspaceId!=settings.CloudWorkspaceId||
           pending.DeviceId!=settings.CloudDeviceId||
           pending.Environment!=settings.Environment||
           pending.Draft.OriginalInvoiceNumber!=record.InvoiceNumber)
            throw new InvalidOperationException(
                "先前待處理的折讓屬於不同裝置／Workspace，請先人工核對");
    }

    private static void VerifyExisting(CloudAllowanceApplicationSummary existing,
        CloudAllowanceApplicationDraft draft,Settings settings)
    {
        if(existing.ItemId!=draft.ItemId||
           existing.OriginalInvoiceNumber!=draft.OriginalInvoiceNumber||
           existing.RequestedGrossAmount!=draft.RequestedGrossAmount||
           existing.AllowanceDay!=draft.AllowanceDate.ToString("yyyyMMdd",
               CultureInfo.InvariantCulture)||
           existing.Environment!=(settings.Environment==Environments.Production?
               "production":"test"))
            throw new InvalidDataException("CYID 原申請與本機凍結內容不一致，不能重建");
    }
}
