using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Cloud;

// V3.2-C4b - no local official AMEGO mutation and no hidden HTTP retries.
// This client uses the caller-owned CYID Device and on-demand employee password;
// neither the user password nor the Frozen payload is durably cached here.
// A lost response requires original application-ID readback, not a second POST.
public sealed record CloudAllowanceSummary(string ApplicationId,string Status,
    int Revision,string RequestedGross,string Environment,DateTimeOffset UpdatedAt);
public sealed record CloudAllowanceReceipt(string ApplicationId,string Status,int Revision);
public sealed record CloudAllowanceReservation(string ApplicationId,string LedgerId,
    string AllowanceNumber);
public sealed record CloudAllowanceStatus(string ApplicationId,string Status,int Revision,
    string? AllowanceNumber,string? LedgerStatus);
public sealed record CloudAllowanceOfficialPreview(string ApplicationId,int Revision,
    string OriginalInvoiceNumber,long RequestedGross,int OfficialUploadStatus,
    bool OfficialSummaryComplete,int OfficialAllowanceCount,
    bool OfficialMutationPending,bool OfficialInvoiceCancelled);
public sealed record CloudAllowanceDetail(string ApplicationId,string Status,
    int Revision,string Environment,long RequestedGross,string AllowanceDay,
    string Reason);
public sealed record CloudAllowanceAttempt(string ApplicationId,string LedgerId,
    string AllowanceNumber,string LedgerStatus,bool RecordedExactReadback,
    bool ManuallyUnattributed);
public sealed record CloudAllowanceSendReceipt(string ApplicationId,
    string AllowanceNumber,string LedgerStatus,string IssueKind,
    string ManualDisposition,bool RecordedReadback);

public sealed record CloudAllowanceManualReview(string ReviewId,string LedgerId,
    string LedgerStatus,string Disposition,string? NextLedgerId=null,string? NextAllowanceNumber=null);
public sealed record CloudAllowanceDraft(string ApplicationId,string Environment,
    string OriginalInvoiceNumber,string AllowanceDay,long RequestedGross,
    string UnnumberedTemplateJson,string Reason);
public sealed record CloudAllowanceVoidDraft(string ApplicationId,string Environment,
    string AllowanceNumber,string OriginalInvoiceNumber,string Reason);
public sealed record CloudAllowanceVoidState(string ApplicationId,string Status,int Revision,
    bool Claimed,bool ReadbackOnly);
public sealed record CloudAllowanceVoidManagerPreview(
    string ApplicationId,int Revision,string AllowanceNumber,
    string OriginalInvoiceNumber,string Reason,string RequestHash,
    string OfficialSnapshotHash,DateTimeOffset OfficialCheckedAtUtc);
public sealed record CloudAllowanceVoidApproval(
    string ApplicationId,int Revision,DateTimeOffset ValidUntilUtc);


public sealed class CloudAllowanceClient
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string deviceToken;
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(10);
    private const string Segment="v1/allowances/";
    private static readonly Regex IdPattern=new(@"^[A-Za-z0-9_-]{8,100}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex DevicePattern=new(@"^cydev_[0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant);

    public CloudAllowanceClient(HttpClient http,Uri endpoint,string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if(!endpoint.IsAbsoluteUri||endpoint.Scheme!=Uri.UriSchemeHttps||
            endpoint.UserInfo.Length!=0||endpoint.Query.Length!=0||
            endpoint.Fragment.Length!=0)
            throw new ArgumentException("Allowance Cloud API requires HTTPS.",nameof(endpoint));
        if(deviceToken is null||!DevicePattern.IsMatch(deviceToken))
            throw new ArgumentException("Invalid Cloud Device token.",nameof(deviceToken));
        this.http=http;
        this.endpoint=new Uri(endpoint.AbsoluteUri.TrimEnd('/')+"/",UriKind.Absolute);
        this.deviceToken=deviceToken.ToLowerInvariant();
    }
    private static void ValidateId(string id)
    {
        if(id is null||!IdPattern.IsMatch(id))
            throw new ArgumentException("Allowance application ID is invalid.",nameof(id));
    }
    private static void Credentials(string employeeNo,string password)
    {
        if(employeeNo is null||!Regex.IsMatch(employeeNo,@"^\d{4}$")||
            password is null||password.Length is <1 or >200)
            throw new ArgumentException("Employee credentials are invalid.");
    }
    private static int RequiredRevision(JsonElement data)
    {
        if(!data.TryGetProperty("revision",out var rev)||
           rev.ValueKind!=JsonValueKind.Number||!rev.TryGetInt32(out int number)||
           number<1)throw new InvalidDataException("Cloud allowance revision is invalid.");
        return number;
    }
    private static string Required(JsonElement data,string field)
    {
        if(!data.TryGetProperty(field,out var v)||v.ValueKind!=JsonValueKind.String||
            string.IsNullOrWhiteSpace(v.GetString()))
            throw new InvalidDataException("Cloud allowance field missing: "+field);
        return v.GetString()!;
    }
    private static void EnsureStatus(string status)
    {
        if(status is not ("open" or "review" or "approved" or "rejected" or "withdrawn"))
            throw new InvalidDataException("Unknown Cloud allowance status.");
    }
    private async Task<JsonDocument> SendAsync(string action,object payload,
        CancellationToken cancellationToken)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint,Segment+action)){
            Content=JsonContent.Create(payload)};
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",deviceToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("X-Request-ID","win-"+Guid.NewGuid().ToString("N"));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        // The provided HttpClient MUST have redirects disabled; a redirect can
        // otherwise disclose Employee login material to a different endpoint.
        using var res=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        // Bound even malicious error responses; never print raw sensitive JSON.
        await res.Content.LoadIntoBufferAsync(65536,timeout.Token).ConfigureAwait(false);
        JsonDocument document;
        try{
            await using var stream=await res.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            document=await JsonDocument.ParseAsync(stream,cancellationToken:timeout.Token)
                .ConfigureAwait(false);
        }catch(JsonException ex){
            throw new InvalidDataException("Cloud allowance JSON response is invalid.",ex);
        }
        var root=document.RootElement;
        if(res.IsSuccessStatusCode&&root.ValueKind==JsonValueKind.Object&&
            root.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True)
            return document;
        var code=root.ValueKind==JsonValueKind.Object&&
            root.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.Object&&
            e.TryGetProperty("code",out var c)&&c.ValueKind==JsonValueKind.String
            ?c.GetString()??"ALLOWANCE_RESULT_UNKNOWN":"ALLOWANCE_RESULT_UNKNOWN";
        document.Dispose();
        throw new CloudApiException(code,
            "Cloud allowance result is not confirmed. Read back the original application ID.",
            res.StatusCode);
    }
    private static CloudAllowanceReceipt Receipt(JsonElement root,string expectedId,
        string expectedStatus,int expectedRevision)
    {
        if(Required(root,"applicationId")!=expectedId||
           Required(root,"status")!=expectedStatus||
           RequiredRevision(root)!=expectedRevision)
            throw new InvalidDataException("Cloud allowance receipt does not match original state.");
        return new CloudAllowanceReceipt(expectedId,expectedStatus,expectedRevision);
    }
    // g0501 cross-device application and status only. No Cloud mutation sender
    // is exposed by the Windows client. Keep the application ID on ambiguous
    // responses and read it back rather than generating a second application.
    private static CloudAllowanceVoidState ReadVoidState(JsonElement root,string id,
        bool requireOpen)
    {
        if(Required(root,"applicationId")!=id ||
           !root.TryGetProperty("claimed",out var claim) ||
           claim.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
           !root.TryGetProperty("readbackOnly",out var only) ||
           only.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
           !root.TryGetProperty("officiallyConfirmed",out var official) ||
           official.ValueKind!=JsonValueKind.False ||
           !root.TryGetProperty("officialActionPerformed",out var external) ||
           external.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud g0501 status cannot imply official success.");
        var status=Required(root,"status");
        var revision=RequiredRevision(root);
        var claimed=claim.GetBoolean();
        if(status is not ("open" or "approved") || revision!=(status=="open"?1:2) ||
           only.GetBoolean()!=claimed || (requireOpen&&(status!="open"||claimed)))
            throw new InvalidDataException("Cloud g0501 application state is inconsistent.");
        return new CloudAllowanceVoidState(id,status,revision,claimed,claimed);
    }

    // Stable across offline restarts and Windows Devices: the same original
    // allowance gets one predictable, non-PII request ID per CYID workspace
    // and environment. NEVER create a random replacement after a lost reply.
    // Server still enforces original-number uniqueness and employee ACLs.
    public static string OriginalVoidApplicationId(
        string workspaceId,string environment,string allowanceNumber)
    {
        if(workspaceId is null||
           !Regex.IsMatch(workspaceId,@"^ws_[A-Za-z0-9_-]{3,100}$")||
           environment is not ("test" or "production")||
           allowanceNumber is null||
           !Regex.IsMatch(allowanceNumber,@"^[A-Z0-9-]{1,16}$"))
            throw new ArgumentException("Invalid original Cloud g0501 identity.");
        var canonical="CYInvoice/g0501/application/v1\n"+
            workspaceId+"\n"+environment+"\n"+allowanceNumber;
        return "void_"+Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public async Task<CloudAllowanceVoidState> CreateVoidApplicationAsync(
        CloudAllowanceVoidDraft draft,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Credentials(employeeNo,password);
        ValidateId(draft.ApplicationId);
        if(draft.Environment is not ("test" or "production") ||
           !Regex.IsMatch(draft.AllowanceNumber??"",@"^[A-Z0-9-]{1,16}$") ||
           !Regex.IsMatch(draft.OriginalInvoiceNumber??"",@"^[A-Z]{2}[0-9]{8}$") ||
           string.IsNullOrWhiteSpace(draft.Reason)||draft.Reason.Length>500 ||
           draft.Reason!=draft.Reason.Trim()||draft.Reason.Any(char.IsControl))
            throw new ArgumentException("Invalid g0501 application.");
        using var doc=await SendAsync("void-create",new{
            applicationId=draft.ApplicationId,environment=draft.Environment,
            allowanceNumber=draft.AllowanceNumber,
            originalInvoiceNumber=draft.OriginalInvoiceNumber,reason=draft.Reason,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        return ReadVoidState(doc.RootElement,draft.ApplicationId,true);
    }

    // Employee workflow: one original-ID readback before any create.
    // A timeout, revoked Device, ambiguous response or other error must
    // NEVER turn into a second create. Only explicit NOT_FOUND permits it.
    public async Task<CloudAllowanceVoidState> CreateOrReadVoidApplicationAsync(
        CloudAllowanceVoidDraft draft,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        try{
            return await ReadVoidApplicationAsync(draft.ApplicationId,
                employeeNo,password,cancellationToken).ConfigureAwait(false);
        }
        catch(CloudApiException ex) when(ex.Code=="VOID_APPLICATION_NOT_FOUND"){
            return await CreateVoidApplicationAsync(draft,
                employeeNo,password,cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CloudAllowanceVoidState> ReadVoidApplicationAsync(
        string id,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        using var doc=await SendAsync("void-status",new{
            applicationId=id,employeeNo,password},cancellationToken)
            .ConfigureAwait(false);
        return ReadVoidState(doc.RootElement,id,false);
    }

    // Privileged CYID manager operations: official READ-ONLY preview and
    // immutable D1 approval only. Neither method can request a g0501 POST.
    private static string Sha256(JsonElement item,string key)
    {
        var value=Required(item,key);
        if(!Regex.IsMatch(value,@"^[a-f0-9]{64}$"))
            throw new InvalidDataException("Cloud g0501 proof hash invalid.");
        return value;
    }

    public async Task<CloudAllowanceVoidManagerPreview> PreviewVoidForManagerAsync(
        string id,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ValidateId(id);Credentials(employeeNo,password);
        using var doc=await SendAsync("void-preview",new{
            applicationId=id,expectedRevision=1,employeeNo,password
        },cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        if(!root.TryGetProperty("preview",out var preview)||
           preview.ValueKind!=JsonValueKind.Object||
           Required(preview,"applicationId")!=id||
           RequiredRevision(preview)!=1||
           !preview.TryGetProperty("mayPostG0501",out var canSend)||
           canSend.ValueKind!=JsonValueKind.False||
           !preview.TryGetProperty("canApproveWithoutFurtherReview",out var canFastApprove)||
           canFastApprove.ValueKind!=JsonValueKind.False||
           !root.TryGetProperty("officialActionPerformed",out var acted)||
           acted.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud g0501 preview may not imply approval or POST.");
        var number=Required(preview,"allowanceNumber");
        var original=Required(preview,"originalInvoiceNumber");
        var reason=Required(preview,"reason");
        var timestamp=Required(preview,"officialCheckedAtUtc");
        if(!Regex.IsMatch(number,@"^[A-Z0-9-]{1,16}$")||
           !Regex.IsMatch(original,@"^[A-Z]{2}[0-9]{8}$")||
           reason.Length>500||reason.Any(char.IsControl)||
           !DateTimeOffset.TryParse(timestamp,out var checkedAt)||
           checkedAt.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("Cloud g0501 official preview invalid.");
        return new CloudAllowanceVoidManagerPreview(
            id,1,number,original,reason,Sha256(preview,"requestHash"),
            Sha256(preview,"officialSnapshotHash"),checkedAt);
    }

    public async Task<CloudAllowanceVoidApproval> ApproveVoidForManagerAsync(
        CloudAllowanceVoidManagerPreview preview,string confirmation,
        string employeeNo,string password,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ValidateId(preview.ApplicationId);Credentials(employeeNo,password);
        if(preview.Revision!=1||
           !Regex.IsMatch(preview.RequestHash??"", @"^[a-f0-9]{64}$")||
           !Regex.IsMatch(preview.OfficialSnapshotHash??"", @"^[a-f0-9]{64}$")||
           confirmation!="作廢 "+preview.AllowanceNumber)
            throw new ArgumentException("g0501 manager confirmation does not match preview.");
        using var doc=await SendAsync("void-approve",new{
            applicationId=preview.ApplicationId,expectedRevision=1,
            requestHash=preview.RequestHash,
            expectedOfficialSnapshotHash=preview.OfficialSnapshotHash,
            confirmation,employeeNo,password
        },cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        if(Required(root,"applicationId")!=preview.ApplicationId||
           Required(root,"status")!="approved"||RequiredRevision(root)!=2||
           !root.TryGetProperty("mayPostG0501",out var canPost)||
           canPost.ValueKind!=JsonValueKind.False||
           !root.TryGetProperty("requiresFreshOfficialCheck",out var needsRead)||
           needsRead.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("officialActionPerformed",out var acted)||
           acted.ValueKind!=JsonValueKind.False||
           !root.TryGetProperty("officiallyConfirmed",out var success)||
           success.ValueKind!=JsonValueKind.False||
           !DateTimeOffset.TryParse(Required(root,"validUntilUtc"),out var expires)||
           expires.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("Cloud g0501 approval is not a financial send.");
        return new CloudAllowanceVoidApproval(preview.ApplicationId,2,expires);
    }

    public async Task<CloudAllowanceReceipt> CreateAsync(CloudAllowanceDraft draft,
        string employeeNo,string password,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Credentials(employeeNo,password);ValidateId(draft.ApplicationId);
        if(draft.Environment is not ("production" or "test")||
           !Regex.IsMatch(draft.OriginalInvoiceNumber??"", @"^[A-Z]{2}\d{8}$")||
           !Regex.IsMatch(draft.AllowanceDay??"", @"^\d{8}$")||
           draft.RequestedGross<=0||
           draft.UnnumberedTemplateJson is null||
           draft.UnnumberedTemplateJson.Length<2||
           draft.UnnumberedTemplateJson.Length>32768||
           string.IsNullOrWhiteSpace(draft.Reason)||
           draft.Reason!=draft.Reason.Trim()||draft.Reason.Length>500||
           draft.Reason.Any(char.IsControl))
            throw new ArgumentException("Cloud allowance draft is invalid.",nameof(draft));
        using var doc=await SendAsync("create",new{
            applicationId=draft.ApplicationId,environment=draft.Environment,
            originalInvoiceNumber=draft.OriginalInvoiceNumber,
            allowanceDay=draft.AllowanceDay,requestedGross=draft.RequestedGross,
            unnumberedTemplateJson=draft.UnnumberedTemplateJson,
            reason=draft.Reason,employeeNo,password},cancellationToken).ConfigureAwait(false);
        if(!doc.RootElement.TryGetProperty("officialActionPerformed",out var v)||
           v.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud allowance create must not issue an official transaction.");
        return Receipt(doc.RootElement,draft.ApplicationId,"open",1);
    }

    // GET is a CYID/Device scoped POST; only owner or verified manager sees
    // the encrypted reason. Do not expose buyer/PDF/template or provider secrets.
    public async Task<CloudAllowanceDetail> GetAsync(
        string id,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        using var doc=await SendAsync("get",new{applicationId=id,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        if(!root.TryGetProperty("officialActionPerformed",out var performed)||
           performed.ValueKind!=JsonValueKind.False||
           !root.TryGetProperty("application",out var item)||
           item.ValueKind!=JsonValueKind.Object||
           item.EnumerateObject().Count()!=7)
            throw new InvalidDataException("Cloud allowance detail response is invalid.");
        var replyId=Required(item,"applicationId");
        var status=Required(item,"status");
        var environment=Required(item,"environment");
        var day=Required(item,"allowanceDay");
        var reason=Required(item,"reason");
        if(replyId!=id||environment is not ("production" or "test")||
           !Regex.IsMatch(day,@"^\d{8}$")||reason.Length>500||
           reason.Any(char.IsControl))
            throw new InvalidDataException("Cloud allowance detail scope is invalid.");
        EnsureStatus(status);
        if(!item.TryGetProperty("requestedGross",out var requested)||
           requested.ValueKind!=JsonValueKind.Number||
           !requested.TryGetInt64(out var gross)||gross<=0)
            throw new InvalidDataException("Cloud allowance amount is invalid.");
        return new CloudAllowanceDetail(id,status,RequiredRevision(item),
            environment,gross,day,reason);
    }
    public async Task<IReadOnlyList<CloudAllowanceSummary>> ListAsync(
        string employeeNo,string password,string? status=null,int limit=20,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);
        if(limit is <1 or >50||status is not null&&
           status is not ("open" or "review" or "approved" or "rejected" or "withdrawn"))
            throw new ArgumentException("Cloud allowance list filter invalid.");
        var body=new Dictionary<string,object>{{"employeeNo",employeeNo},
            {"password",password},{"limit",limit}};
        if(status is not null)body["status"]=status;
        using var doc=await SendAsync("list",body,cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        if(!root.TryGetProperty("items",out var items)||
           items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>limit)
            throw new InvalidDataException("Cloud allowance list is invalid.");
        var result=new List<CloudAllowanceSummary>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var r in items.EnumerateArray()){
            if(r.ValueKind!=JsonValueKind.Object||r.EnumerateObject().Count()!=6)
                throw new InvalidDataException("Cloud allowance list contains an unsafe field.");
            var id=Required(r,"applicationId");
            var s=Required(r,"status");
            ValidateId(id);EnsureStatus(s);
            if(!seen.Add(id)||r.GetProperty("revision").GetInt32()<1||
               !DateTimeOffset.TryParse(Required(r,"updatedAt"),out var when)||
               Required(r,"environment") is not ("production" or "test"))
                throw new InvalidDataException("Cloud allowance list contains invalid state.");
            var n=Required(r,"requestedGross");
            if(!long.TryParse(n,out var amount)||amount<=0)
                throw new InvalidDataException("Cloud allowance gross is invalid.");
            result.Add(new CloudAllowanceSummary(id,s,r.GetProperty("revision").GetInt32(),
                n,Required(r,"environment"),when));
        }
        return result;
    }
    public async Task<CloudAllowanceReceipt> ReviewAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<1)throw new ArgumentException("Invalid expected revision.");
        using var doc=await SendAsync("review",new{applicationId=id,expectedRevision,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        return Receipt(doc.RootElement,id,"review",expectedRevision+1);
    }
    // Preliminary official read-only metadata is NOT a manager Grant, nor an
    // authorization to call the provider or auto-mark a Ledger confirmed.
    public async Task<CloudAllowanceOfficialPreview> PreviewAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<2)throw new ArgumentException("Invalid review revision.");
        using var doc=await SendAsync("preview",new{applicationId=id,
            expectedRevision,employeeNo,password},cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        if(!root.TryGetProperty("preview",out var preview)||
            preview.ValueKind!=JsonValueKind.Object||
            Required(preview,"applicationId")!=id||
            RequiredRevision(preview)!=expectedRevision||
            !preview.TryGetProperty("canApprove",out var claim)||
            claim.ValueKind!=JsonValueKind.False||
            !preview.TryGetProperty("canAllocateNumber",out var canNumber)||
            canNumber.ValueKind!=JsonValueKind.False||
            !preview.TryGetProperty("requiresIndependentOfficialReconciliation",out var independent)||
            independent.ValueKind!=JsonValueKind.True)
            throw new InvalidDataException("Official preview is not a valid authority grant.");
        var original=Required(preview,"originalInvoiceNumber");
        if(!Regex.IsMatch(original,@"^[A-Z]{2}\d{8}$")||
            !preview.TryGetProperty("requestedGross",out var gross)||
            !gross.TryGetInt64(out var amount)||amount<1||
            !preview.TryGetProperty("officialUploadStatus",out var upload)||
            !upload.TryGetInt32(out var uploadValue)||
            !preview.TryGetProperty("officialSummaryComplete",out var complete)||
            complete.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||
            !preview.TryGetProperty("officialMutationPending",out var pending)||
            pending.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||
            !preview.TryGetProperty("officialInvoiceCancelled",out var cancelled)||
            cancelled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||
            !preview.TryGetProperty("officialAllowanceCount",out var count)||
            !count.TryGetInt32(out var allowanceCount)||allowanceCount<0)
            throw new InvalidDataException("Cloud allowance official preview incomplete.");
        return new CloudAllowanceOfficialPreview(id,expectedRevision,original,amount,
            uploadValue,complete.GetBoolean(),allowanceCount,
            pending.GetBoolean(),cancelled.GetBoolean());
    }
    public async Task<CloudAllowanceReceipt> ApproveAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<2)throw new ArgumentException("Invalid manager revision.");
        using var doc=await SendAsync("approve",new{applicationId=id,expectedRevision,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        if(!doc.RootElement.TryGetProperty("officialActionPerformed",out var b)||
           b.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Manager approval must not issue an official transaction.");
        return Receipt(doc.RootElement,id,"approved",expectedRevision+1);
    }
    public async Task<CloudAllowanceReservation> ReserveAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3)throw new ArgumentException("Invalid approved revision.");
        using var doc=await SendAsync("reserve",new{applicationId=id,
            expectedRevision,employeeNo,password},cancellationToken).ConfigureAwait(false);
        return ReadReservation(doc.RootElement,id);
    }
    public async Task<CloudAllowanceReservation> ReserveAfterCollisionAsync(
        string id,int expectedRevision,string reviewId,string previousNumber,string confirmation,
        string employeeNo,string password,CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3||!Regex.IsMatch(reviewId,@"^[A-Za-z0-9_-]{12,100}$")||
           !Regex.IsMatch(previousNumber,@"^A\d{8}[0-9A-Z]{3}$")||
           confirmation!=$"確認外部占號 {previousNumber} 並預留下一號")
            throw new ArgumentException("Explicit original-number collision review is required.");
        // Never retry this mutation. Inspect the original application / audit
        // after a lost response; a reserved number still requires a separate send.
        using var doc=await SendAsync("reserve-after-collision",new{applicationId=id,
            expectedRevision,reviewId,previousNumber,confirmation,employeeNo,password},
            cancellationToken).ConfigureAwait(false);
        var result=ReadReservation(doc.RootElement,id);
        if(result.AllowanceNumber==previousNumber)
            throw new InvalidDataException("Collision reservation returned the old number.");
        return result;
    }
    private static CloudAllowanceReservation ReadReservation(JsonElement root,string id)
    {
        if(Required(root,"applicationId")!=id||
           Required(root,"status")!="reserved"||
           !root.TryGetProperty("maySendWithoutFreshOfficialCheck",out var b)||
           b.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud allowance reserve response is not a send authorization.");
        var ledger=Required(root,"ledgerId");
        var number=Required(root,"allowanceNumber");
        if(!Regex.IsMatch(ledger,@"^al_[0-9a-f-]{36}$")||
           !Regex.IsMatch(number,@"^A\d{8}[0-9A-Z]{3}$"))
            throw new InvalidDataException("Cloud allowance reserved ID is invalid.");
        return new CloudAllowanceReservation(id,ledger,number);
    }
    private static void RequireFalse(JsonElement root,string key)
    {
        if(!root.TryGetProperty(key,out var prop)||
            prop.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud allowance operation cannot certify a financial transaction.");
    }
    private static bool RequiredBoolean(JsonElement root,string key)
    {
        if(!root.TryGetProperty(key,out var value)||
           value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Cloud allowance evidence flag missing.");
        return value.ValueKind==JsonValueKind.True;
    }
    private static string Ledger(string number)
    {
        if(number is not ("reserved" or "sending" or "occupied_external" or "confirmed"))
            throw new InvalidDataException("Unknown Cloud allowance Ledger status.");
        return number;
    }
    public async Task<CloudAllowanceAttempt> InspectAttemptAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3)throw new ArgumentException("Invalid approved revision.");
        using var doc=await SendAsync("attempt",new{
            applicationId=id,expectedRevision,employeeNo,password},
            cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        RequireFalse(root,"proofOfThisPost");
        RequireFalse(root,"officiallyConfirmed");
        RequireFalse(root,"safeToAutoRetry");
        RequireFalse(root,"canAllocateAnotherNumber");
        if(Required(root,"applicationId")!=id)
            throw new InvalidDataException("Cloud allowance attempt identity changed.");
        var ledger=Required(root,"ledgerId");
        var number=Required(root,"allowanceNumber");
        if(!Regex.IsMatch(ledger,@"^al_[0-9a-f-]{36}$")||
           !Regex.IsMatch(number,@"^A\d{8}[0-9A-Z]{3}$"))
            throw new InvalidDataException("Cloud allowance attempt has malformed Ledger.");
        return new CloudAllowanceAttempt(id,ledger,number,
            Ledger(Required(root,"ledgerStatus")),
            RequiredBoolean(root,"recordedExactReadback"),
            RequiredBoolean(root,"manuallyUnattributed"));
    }
    public async Task<CloudAllowanceSendReceipt> SendOnceAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3)throw new ArgumentException("Invalid approved revision.");
        // This method never retries. A lost HTTP response means that the D1
        // one-shot CAS may already have executed. Inspect the SAME ID only.
        using var doc=await SendAsync("send-once",new{
            applicationId=id,expectedRevision,confirmOnce=true,employeeNo,password},
            cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        RequireFalse(root,"proofOfThisPost");
        RequireFalse(root,"officiallyConfirmed");
        RequireFalse(root,"safeToAutoRetry");
        RequireFalse(root,"canAllocateAnotherNumber");
        if(!RequiredBoolean(root,"manualReviewRequired")||
           Required(root,"applicationId")!=id||
           Required(root,"ledgerStatus") is not ("sending" or "occupied_external"))
            throw new InvalidDataException("Cloud send receipt cannot be trusted.");
        var number=Required(root,"allowanceNumber");
        var kind=Required(root,"issueKind");
        var disposition=Required(root,"manualDisposition");
        if(!Regex.IsMatch(number,@"^A\d{8}[0-9A-Z]{3}$")||
           kind is not ("accepted_unconfirmed" or "confirmed_number_collision" or
             "definite_rejection" or "unknown_result")||
           disposition is not ("full_match_unattributed" or
             "matched_readback_audit_unavailable" or
             "number_collision_requires_investigation" or
             "accepted_without_attributable_readback" or
             "provider_rejected_requires_investigation" or
             "unknown_outcome_requires_investigation"))
            throw new InvalidDataException("Cloud send response has unknown review state.");
        var ledgerStatus=Required(root,"ledgerStatus");
        if(ledgerStatus=="occupied_external"&&
           (kind!="confirmed_number_collision"||disposition!="number_collision_requires_investigation"||
            RequiredBoolean(root,"recordedReadback")))
            throw new InvalidDataException("External collision requires distinct official evidence.");
        return new CloudAllowanceSendReceipt(id,number,ledgerStatus,kind,
            disposition,RequiredBoolean(root,"recordedReadback"));
    }
    private static readonly HashSet<string> TriageCodes=new(StringComparer.Ordinal)
    {
        "full_match_unattributed","unknown_outcome",
        "number_collision_investigation","provider_rejection_investigation"
    };
    // Manual classification is an append-only human annotation; never a
    // success claim, another number grant or permission to send a second POST.
    public async Task<CloudAllowanceManualReview> RecordManualReviewAsync(
        string id,int expectedRevision,string reviewId,string disposition,
        string employeeNo,string password,CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3||!Regex.IsMatch(reviewId,
            @"^[A-Za-z0-9_-]{12,100}$")||!TriageCodes.Contains(disposition))
            throw new ArgumentException("Invalid manual review classification.");
        using var doc=await SendAsync("manual-review",new{
            applicationId=id,expectedRevision,reviewId,disposition,
            confirmReview=true,employeeNo,password},cancellationToken)
            .ConfigureAwait(false);
        var root=doc.RootElement;
        RequireFalse(root,"proofOfThisPost");
        RequireFalse(root,"officiallyConfirmed");
        RequireFalse(root,"safeToAutoRetry");
        RequireFalse(root,"canAllocateAnotherNumber");
        if(Required(root,"applicationId")!=id||
           Required(root,"reviewId")!=reviewId||
           Required(root,"disposition")!=disposition)
            throw new InvalidDataException("Manual review identity changed.");
        var ledger=Required(root,"ledgerId");
        var status=Ledger(Required(root,"ledgerStatus"));
        if(!Regex.IsMatch(ledger,@"^al_[0-9a-f-]{36}$")||
           status is not ("sending" or "occupied_external"))
            throw new InvalidDataException("Manual review ledger is not an attempted transaction.");
        return new CloudAllowanceManualReview(reviewId,ledger,status,disposition);
    }
    public async Task<IReadOnlyList<CloudAllowanceManualReview>> ManualHistoryAsync(
        string id,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        if(expectedRevision<3)throw new ArgumentException("Invalid approved revision.");
        using var doc=await SendAsync("manual-history",new{
            applicationId=id,expectedRevision,employeeNo,password},
            cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        RequireFalse(root,"proofOfThisPost");
        RequireFalse(root,"officiallyConfirmed");
        RequireFalse(root,"safeToAutoRetry");
        RequireFalse(root,"canAllocateAnotherNumber");
        if(Required(root,"applicationId")!=id||
           !root.TryGetProperty("items",out var items)||
           items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()>30)
            throw new InvalidDataException("Cloud manual review history is invalid.");
        var result=new List<CloudAllowanceManualReview>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var r in items.EnumerateArray())
        {
            RequireFalse(r,"proofOfThisPost");
            RequireFalse(r,"officiallyConfirmed");
            RequireFalse(r,"safeToAutoRetry");
            RequireFalse(r,"canAllocateAnotherNumber");
            var review=Required(r,"reviewId");
            var ledger=Required(r,"ledgerId");
            var status=Ledger(Required(r,"ledgerStatus"));
            var disposition=Required(r,"disposition");
            if(!seen.Add(review)||!Regex.IsMatch(review,@"^[A-Za-z0-9_-]{12,100}$")||
               !Regex.IsMatch(ledger,@"^al_[0-9a-f-]{36}$")||
               status is not ("sending" or "occupied_external")||
               (!TriageCodes.Contains(disposition)&&disposition!="external_collision_reserved"))
                throw new InvalidDataException("Cloud manual review history entry is invalid.");
            string? nextId=null,nextNumber=null;
            if(disposition=="external_collision_reserved")
            {
                nextId=Required(r,"nextLedgerId");nextNumber=Required(r,"nextAllowanceNumber");
                if(status!="occupied_external"||nextId==ledger||
                   !Regex.IsMatch(nextId,@"^al_[0-9a-f-]{36}$")||
                   !Regex.IsMatch(nextNumber,@"^A\d{8}[0-9A-Z]{3}$"))
                    throw new InvalidDataException("Collision review has invalid next reservation identity.");
            }
            result.Add(new CloudAllowanceManualReview(review,ledger,status,disposition,nextId,nextNumber));
        }
        return result;
    }
    public async Task<CloudAllowanceStatus> StatusAsync(
        string id,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        Credentials(employeeNo,password);ValidateId(id);
        using var doc=await SendAsync("status",new{applicationId=id,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        var root=doc.RootElement;
        var status=Required(root,"status");EnsureStatus(status);
        if(Required(root,"applicationId")!=id||
           !root.TryGetProperty("officiallyConfirmed",out var official)||
           official.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud allowance status must not fabricate provider confirmation.");
        string? number=null,ledger=null;
        if(root.TryGetProperty("allowanceNumber",out var n)&&n.ValueKind==JsonValueKind.String)
            number=n.GetString();
        if(root.TryGetProperty("ledgerStatus",out var l)&&l.ValueKind==JsonValueKind.String)
            ledger=l.GetString();
        if(number is not null&&!Regex.IsMatch(number,@"^A\d{8}[0-9A-Z]{3}$"))
            throw new InvalidDataException("Cloud allowance number invalid.");
        if(ledger is not null&&ledger is not
           ("reserved" or "sending" or "occupied_external" or "confirmed"))
            throw new InvalidDataException("Cloud allowance Ledger state invalid.");
        return new CloudAllowanceStatus(id,status,RequiredRevision(root),number,ledger);
    }
}
