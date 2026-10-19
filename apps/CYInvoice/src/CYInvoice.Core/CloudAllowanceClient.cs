using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
public sealed record CloudAllowanceDraft(string ApplicationId,string Environment,
    string OriginalInvoiceNumber,string AllowanceDay,long RequestedGross,
    string UnnumberedTemplateJson);

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
           draft.UnnumberedTemplateJson.Length>32768)
            throw new ArgumentException("Cloud allowance draft is invalid.",nameof(draft));
        using var doc=await SendAsync("create",new{
            applicationId=draft.ApplicationId,environment=draft.Environment,
            originalInvoiceNumber=draft.OriginalInvoiceNumber,
            allowanceDay=draft.AllowanceDay,requestedGross=draft.RequestedGross,
            unnumberedTemplateJson=draft.UnnumberedTemplateJson,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
        if(!doc.RootElement.TryGetProperty("officialActionPerformed",out var v)||
           v.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud allowance create must not issue an official transaction.");
        return Receipt(doc.RootElement,draft.ApplicationId,"open",1);
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
        var root=doc.RootElement;
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
