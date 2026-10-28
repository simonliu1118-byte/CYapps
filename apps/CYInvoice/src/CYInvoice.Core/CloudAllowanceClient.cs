using System.Net;
using CYInvoice.Core.Storage;
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
public sealed record CloudAllowanceSummary(string ApplicationId,string Status,int Revision,string RequestedGross,string Environment,DateTimeOffset UpdatedAt);
public sealed record CloudAllowanceReceipt(string ApplicationId,string Status,int Revision);
public sealed record CloudAllowanceStatus(string ApplicationId,string Status,int Revision,
    string? AllowanceNumber,string? LedgerStatus)
{
    public string ExecutionState { get; init; } = "processing";
}
public sealed record CloudAllowanceDraft(string ApplicationId,string Environment,
    string OriginalInvoiceNumber,string AllowanceDay,long RequestedGross,
    string UnnumberedTemplateJson,string Reason);
public sealed record CloudAllowanceVoidDraft(string ApplicationId,string Environment,
    string AllowanceNumber,string OriginalInvoiceNumber,string Reason);
public sealed record CloudAllowanceVoidState(string ApplicationId,string Status,int Revision,
    bool Claimed,bool ReadbackOnly);
public sealed record CloudAllowanceOperationDetail(string ApplicationId, string Operation, string Status,
    int Revision, IReadOnlyList<KeyValuePair<string,string>> Rows, string RequestHash, string OfficialSnapshotHash, bool CanWithdraw = false);
public sealed record CloudAllowanceExecution(string ApplicationId, string ExecutionState, string ErrorCode);

public sealed class CloudAllowanceClient
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string deviceToken;
    private readonly string dataDirectory;
    public string DiagnosticWriteProblem { get; private set; } = "";
    private const string Segment="v1/allowances/";
    private static readonly Regex IdPattern=new(@"^[A-Za-z0-9_-]{8,100}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex DevicePattern=new(@"^cydev_[0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant);

    public CloudAllowanceClient(HttpClient http,Uri endpoint,string deviceToken,string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if(!endpoint.IsAbsoluteUri||endpoint.Scheme!=Uri.UriSchemeHttps||
            endpoint.UserInfo.Length!=0||endpoint.Query.Length!=0||
            endpoint.Fragment.Length!=0)
            throw new ArgumentException("Allowance Cloud API requires HTTPS.",nameof(endpoint));
        if(deviceToken is null||!DevicePattern.IsMatch(deviceToken))
            throw new ArgumentException("Invalid Cloud Device token.",nameof(deviceToken));
        this.dataDirectory=dataDirectory ?? Path.Combine(AppContext.BaseDirectory,"Data");
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
        var requestId="win-"+Guid.NewGuid().ToString("N");
        req.Headers.TryAddWithoutValidation("X-Request-ID",requestId);
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var metadata=JsonSerializer.SerializeToElement(payload);
        var applicationId=metadata.TryGetProperty("applicationId",out var operationId)?operationId.GetString():null;
        bool responseReceived=false;int? httpStatus=null;
        string? serverStage=null, serverCause=null;
        bool? serverDiagnosticsSaved=null;
        CloudOfficialQueryDiagnostic? officialQuery=null;
        void Log(string phase, string? code=null, Exception? error=null, string? status=null, string? executionState=null) {
            if(!IssueDiagnosticLog.RecordCloud(dataDirectory,action,phase,requestId,applicationId,timer.ElapsedMilliseconds,
                httpStatus,responseReceived,code,error,serverStage,status,executionState))
                DiagnosticWriteProblem="程式同目錄 log 寫入失敗，請確認資料夾權限與磁碟空間。";
        }
        Log("request_started");
        try {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(action is "approve" or "void-approve" ? 90 : 30));
        // The provided HttpClient MUST have redirects disabled; a redirect can
        // otherwise disclose Employee login material to a different endpoint.
        using var res=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        responseReceived=true;httpStatus=(int)res.StatusCode;
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
        {
            string? Field(string key)=>root.TryGetProperty(key,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
            Log("response_confirmed",status:Field("status"),executionState:Field("executionState"));
            if(root.TryGetProperty("diagnosticSaved",out var saved)&&saved.ValueKind==JsonValueKind.False)
                DiagnosticWriteProblem += "Cloud 診斷保存失敗；請保留本機 log。";
            return document;
        }
        if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out var diagnostic)&&diagnostic.ValueKind==JsonValueKind.Object&&
            diagnostic.TryGetProperty("stage",out var failedStage)&&failedStage.ValueKind==JsonValueKind.String)
            serverStage=failedStage.GetString();
        if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out var errorDetail)&&errorDetail.ValueKind==JsonValueKind.Object) {
            officialQuery=CloudOfficialQueryDiagnostic.Read(errorDetail);
            if(errorDetail.TryGetProperty("causeCode",out var cause)&&cause.ValueKind==JsonValueKind.String)serverCause=cause.GetString();
            if(errorDetail.TryGetProperty("diagnosticSaved",out var saved)&&saved.ValueKind is JsonValueKind.True or JsonValueKind.False) {
                serverDiagnosticsSaved=saved.GetBoolean();
                if(!serverDiagnosticsSaved.Value)DiagnosticWriteProblem+="Cloud 診斷保存失敗；請保留本機 log。";
            }
        }
        var code=root.ValueKind==JsonValueKind.Object&&
            root.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.Object&&
            e.TryGetProperty("code",out var c)&&c.ValueKind==JsonValueKind.String
            ?c.GetString()??"ALLOWANCE_RESULT_UNKNOWN":"ALLOWANCE_RESULT_UNKNOWN";
        document.Dispose();
        throw new CloudApiException(code,
            code switch {
                "ALLOWANCES_NOT_READY" => "折讓功能尚未啟用。",
                "OFFICIAL_SEND_DISABLED" => "折讓送出功能尚未啟用。",
                "LOGIN_FAILED" or "AUTH_INVALID" => "帳號或密碼錯誤。",
                "ACCESS_DENIED" or "MANAGER_AUTH_REQUIRED" or "MANAGER_REQUIRED" or "ALLOWANCE_APPROVAL_MANAGER_REQUIRED" => "權限不足。",
                "DEVICE_INVALID" => "目前裝置授權已失效，請重新確認連線。",
                "INVALID_REQUEST" => "折讓申請資料不符，請保留問題診斷供查證。",
                _ => "折讓操作未完成（" + (Regex.IsMatch(code,@"^[A-Z0-9_]{3,100}$")?code:"UNKNOWN") + "），原申請已保留，請查看 log 紀錄。"
            },
            res.StatusCode);
        } catch (Exception error) {
            if(officialQuery is not null)error.Data["OfficialQuery"]=officialQuery;
            if(serverCause is not null)error.Data["CloudCauseCode"]=serverCause;
            if(serverDiagnosticsSaved is not null)error.Data["CloudDiagnosticsSaved"]=serverDiagnosticsSaved.Value;
            Log("request_failed",error is CloudApiException cloud?cloud.Code:null,error);
            if(DiagnosticWriteProblem.Length!=0)error.Data["DiagnosticWriteProblem"]=DiagnosticWriteProblem;
            if(serverStage is not null)error.Data["CloudStage"]=serverStage;
            error.Data["CloudRequestId"]=requestId;
            error.Data["ResponseReceived"]=responseReceived;
            if(httpStatus is not null)error.Data["HttpStatus"]=httpStatus.Value;
            throw;
        }
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
           official.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
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
            if(r.ValueKind!=JsonValueKind.Object||r.EnumerateObject().Any(p=>p.Name is not
                ("applicationId" or "status" or "revision" or "requestedGross" or "environment" or "updatedAt" or "executionState")))
                throw new InvalidDataException("Cloud allowance list contains an unsafe field.");
            if(r.TryGetProperty("executionState",out var execution)&&
                (execution.ValueKind!=JsonValueKind.String || execution.GetString() is not ("processing" or "problem" or "completed")))
                throw new InvalidDataException("Cloud allowance execution state is invalid.");
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
    public async Task<CloudAllowanceOperationDetail> OpenOperationAsync(string id, bool isVoid,
        string employeeNo, string password, CancellationToken token = default)
    {
        Credentials(employeeNo, password); ValidateId(id);
        using var doc = await SendAsync("detail", new { applicationId=id, operation=isVoid?"void":"issue", employeeNo,password }, token).ConfigureAwait(false);
        var root=doc.RootElement;
        if (Required(root,"applicationId")!=id || Required(root,"operation")!=(isVoid?"void":"issue"))
            throw new InvalidDataException("Cloud 原申請身分不符");
        var rows=root.GetProperty("rows").EnumerateArray().Select(r => new KeyValuePair<string,string>(
            Required(r,"label"), r.GetProperty("value").GetString() ?? "－")).ToArray();
        return new(id, isVoid?"void":"issue", Required(root,"status"), RequiredRevision(root), rows,
            root.GetProperty("requestHash").GetString() ?? "", root.GetProperty("officialSnapshotHash").GetString() ?? "",
            root.TryGetProperty("canWithdraw", out var cancel) && cancel.ValueKind == JsonValueKind.True);
    }

    public async Task<CloudAllowanceReceipt> WithdrawOperationAsync(CloudAllowanceOperationDetail detail,
        string employeeNo, string password, CancellationToken token = default)
    {
        Credentials(employeeNo,password); ValidateId(detail.ApplicationId);
        if(detail.Operation!="issue" || !detail.CanWithdraw)throw new InvalidOperationException("此申請不能取消退回");
        using var doc=await SendAsync("withdraw",new {applicationId=detail.ApplicationId,
            expectedRevision=detail.Revision,employeeNo,password},token).ConfigureAwait(false);
        return Receipt(doc.RootElement,detail.ApplicationId,"withdrawn",detail.Revision+1);
    }

    public async Task<CloudAllowanceExecution> ApproveOperationAsync(CloudAllowanceOperationDetail detail,
        string employeeNo, string password, CancellationToken token = default)
    {
        Credentials(employeeNo,password); ValidateId(detail.ApplicationId);
        using var doc=detail.Operation=="void"
            ?await SendAsync("void-approve",new {applicationId=detail.ApplicationId,expectedRevision=detail.Revision,
                requestHash=detail.RequestHash,expectedOfficialSnapshotHash=detail.OfficialSnapshotHash,employeeNo,password},token).ConfigureAwait(false)
            :await SendAsync("approve",new {applicationId=detail.ApplicationId,expectedRevision=detail.Revision,employeeNo,password},token).ConfigureAwait(false);
        var root=doc.RootElement;
        if(Required(root,"applicationId")!=detail.ApplicationId)throw new InvalidDataException("核准回覆身分不符");
        var state=Required(root,"executionState");
        if(state is not ("completed" or "processing" or "problem"))throw new InvalidDataException("執行狀態無效");
        return new(detail.ApplicationId,state,root.TryGetProperty("errorCode",out var code)?code.GetString()??"":"");
    }

    public async Task<string> ExportOperationDiagnosticsAsync(string id, bool isVoid,
        string employeeNo,string password,CancellationToken token=default,int maxPages=int.MaxValue)
    {
        if(maxPages<1)throw new ArgumentOutOfRangeException(nameof(maxPages));
        Credentials(employeeNo,password);ValidateId(id);
        var output=new StringBuilder();long cursor=0;var pages=0;
        do
        {
            using var doc=await SendAsync("diagnostics",new{applicationId=id,operation=isVoid?"void":"issue",afterEventId=cursor,employeeNo,password},token).ConfigureAwait(false);
            var root=doc.RootElement;
            if(Required(root,"applicationId")!=id)throw new InvalidDataException("診斷操作身分不符");
            output.AppendLine(JsonSerializer.Serialize(root,new JsonSerializerOptions{WriteIndented=true}));
            if(root.TryGetProperty("events",out var events)&&events.ValueKind==JsonValueKind.Array) {
                foreach(var entry in events.EnumerateArray()) {
                    string? Text(string key)=>entry.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
                    int? Number(string key)=>entry.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out var n)?n:null;
                    var received=entry.TryGetProperty("response_received",out var response)&&response.ValueKind==JsonValueKind.Number
                        ?response.GetInt32()==1:(bool?)null;
                    if(!IssueDiagnosticLog.RecordCloud(dataDirectory,isVoid?"void":"issue","server_event",Text("request_id")??"unknown",id,
                        httpStatus:Number("http_status"),responseReceived:received,code:Text("error_code"),serverStage:Text("stage"),
                        attempt:Number("attempt"),providerCode:Number("provider_code"),serverAtUtc:Text("recorded_at")))
                        DiagnosticWriteProblem="程式同目錄 log 寫入失敗，請確認資料夾權限與磁碟空間。";
                }
            }
            if(!root.GetProperty("hasMore").GetBoolean() || ++pages>=maxPages)break;
            var next=root.GetProperty("nextEventId").GetInt64();
            if(next<=cursor)throw new InvalidDataException("診斷游標未前進，請保留目前紀錄");
            cursor=next;
        }while(true);
        return output.ToString();
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
           official.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
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
        var state=Required(root,"executionState");
        if(state is not ("processing" or "problem" or "completed") ||
           official.GetBoolean() != (state=="completed") || state=="completed" && ledger!="confirmed")
            throw new InvalidDataException("Cloud 官方完成狀態與原 Ledger 不一致");
        return new CloudAllowanceStatus(id,status,RequiredRevision(root),number,ledger) { ExecutionState = state };
    }
}
