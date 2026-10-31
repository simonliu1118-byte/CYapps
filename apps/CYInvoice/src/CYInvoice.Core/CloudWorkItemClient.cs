using System.Net;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Cloud;

// V3.1 transport for VOID and UPLOAD_ISSUE only; never invokes AMEGO.
// Work Items are draft until original D1 migration is independently approved.
// Caller MUST supply a redirect-disabled HttpClient (credential-bearing POST).

public sealed record CloudWorkItem(
    string ItemId, string WorkspaceId, string Environment, string Kind,
    string ReferenceHash, string Status, int Revision);

// Privacy-limited list transport: no source hashes, invoice details, employee
// identities or device identifiers are present in a manager's queue response.
public sealed record CloudWorkItemSummary(
    string ItemId, string Environment, string Kind, string? IssueCode,
    string Status, int Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CloudWorkItemCursor(string CreatedAt, string ItemId);
public sealed record CloudWorkItemPage(
    IReadOnlyList<CloudWorkItemSummary> Items, CloudWorkItemCursor? NextCursor);

// These payloads carry only identifiers and hashes, never invoice or buyer data.
public sealed record CloudVoidReviewRequest(string ItemId,string Environment,string ReferenceHash);
public sealed record CloudUploadIssueReport(string ItemId,string Environment,string ReferenceHash,string IssueCode);
public sealed record CloudWorkItemReceipt(string ItemId);

public sealed class CloudWorkItemClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient http;
    private readonly Uri baseUri;
    private readonly string deviceToken;
    private readonly string workspaceId;

    public CloudWorkItemClient(HttpClient http, Uri baseUri, string deviceToken, string workspaceId)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps
            || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
            throw new ArgumentException("Work Item API requires an HTTPS endpoint without URL credentials.", nameof(baseUri));
        if (deviceToken is null || !Regex.IsMatch(deviceToken, @"^cydev_[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Work Item Device token is invalid.", nameof(deviceToken));
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 128 || workspaceId.Any(char.IsControl))
            throw new ArgumentException("Work Item Workspace is invalid.", nameof(workspaceId));
        this.http = http;
        this.baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        this.deviceToken = deviceToken.ToLowerInvariant();
        this.workspaceId = workspaceId;
    }


    // The same caller-selected ID is kept for readback when a reply is lost.
    // Neither HTTP retries nor official AMEGO operations occur in this client.
    public async Task<CloudWorkItemReceipt> SubmitVoidReviewAsync(
        CloudVoidReviewRequest draft,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateCredentials(employeeNo,password);
        ValidateId(draft.ItemId);
        ValidateReference(draft.Environment,draft.ReferenceHash);
        using var document=await SendAsync("create",new {
            itemId=draft.ItemId,environment=draft.Environment,kind="VOID",
            referenceHash=draft.ReferenceHash,employeeNo,password
        },cancellationToken).ConfigureAwait(false);
        return ReadReceipt(document.RootElement,draft.ItemId);
    }

    public async Task<CloudWorkItemReceipt> ReportUploadIssueAsync(
        CloudUploadIssueReport report,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateCredentials(employeeNo,password);
        ValidateId(report.ItemId);
        ValidateReference(report.Environment,report.ReferenceHash);
        if(report.IssueCode is not (
           "invoice_list_failed" or "query_failed" or "remote_not_found" or
           "unknown_remote_not_found" or "reconciliation_failed" or
           "ambiguous_match" or "local_write_failed" or "upload_failed" or
           "duplicate_order_id"))
            throw new ArgumentException("Upload issue code is invalid.",nameof(report));
        using var document=await SendAsync("report-upload-issue",new {
            itemId=report.ItemId,environment=report.Environment,
            referenceHash=report.ReferenceHash,issueCode=report.IssueCode,
            employeeNo,password
        },cancellationToken).ConfigureAwait(false);
        return ReadReceipt(document.RootElement,report.ItemId);
    }

    // This changes a MANUAL review stage only, not an invoice's official
    // provider status. The caller must use the latest GET revision.
    public async Task<CloudWorkItemReceipt> TransitionManualAsync(
        CloudWorkItem current,string to,string employeeNo,string password,
        string? resolutionCode=null,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateCredentials(employeeNo,password);
        ValidateId(current.ItemId);
        ValidateReference(current.Environment,current.ReferenceHash);
        if(current.WorkspaceId!=workspaceId||current.Revision<1||
           current.Kind is not ("VOID" or "UPLOAD_ISSUE")||
           !((current.Status=="open"&&to=="review")||
             (current.Status=="review"&&to=="closed")))
            throw new ArgumentException("Work Item transition is invalid.",nameof(current));
        if(current.Kind=="UPLOAD_ISSUE"&&to=="closed"&&
           resolutionCode is not (
              "resolved_locally" or "checked_official" or "duplicate_report" or
              "not_actionable" or "needs_followup"))
            throw new ArgumentException("Upload issue closure requires a reason.",nameof(resolutionCode));
        if((current.Kind!="UPLOAD_ISSUE"||to!="closed")&&resolutionCode is not null)
            throw new ArgumentException("Reason only belongs to upload issue closure.",nameof(resolutionCode));
        var request=new Dictionary<string,object?>{
            ["itemId"]=current.ItemId,["expectedRevision"]=current.Revision,
            ["from"]=current.Status,["to"]=to,
            ["employeeNo"]=employeeNo,["password"]=password
        };
        if(resolutionCode is not null)request["resolutionCode"]=resolutionCode;
        using var document=await SendAsync("transition",request,cancellationToken).ConfigureAwait(false);
        return ReadReceipt(document.RootElement,current.ItemId);
    }

    private static CloudWorkItemReceipt ReadReceipt(JsonElement root,string itemId)
    {
        // Strongly validate server semantics: recorded is not official success.
        if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count()!=5||
           !root.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("itemId",out var id)||id.ValueKind!=JsonValueKind.String||
           id.GetString()!=itemId||
           !root.TryGetProperty("recorded",out var saved)||saved.ValueKind!=JsonValueKind.True||
           !root.TryGetProperty("officialVerified",out var official)||
           official.ValueKind!=JsonValueKind.False||
           !root.TryGetProperty("providerActionPerformed",out var provider)||
           provider.ValueKind!=JsonValueKind.False)
            throw new InvalidDataException("Cloud Work Item receipt is not a local-only confirmation.");
        return new CloudWorkItemReceipt(itemId);
    }

    private static void ValidateReference(string environment,string referenceHash)
    {
        if(environment is not ("production" or "test")||
           referenceHash is null||
           !Regex.IsMatch(referenceHash,@"^[a-f0-9]{64}$",RegexOptions.CultureInvariant))
            throw new ArgumentException("Work Item local reference is invalid.");
    }


    public async Task<CloudWorkItem?> GetAsync(
        string itemId, string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        ValidateId(itemId);
        ValidateCredentials(employeeNo, password);
        try
        {
            using var response = await SendAsync("get",
                new { itemId, employeeNo, password }, cancellationToken).ConfigureAwait(false);
            return ReadItem(response.RootElement, itemId);
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Code == "NOT_FOUND")
        {
            return null;
        }
    }

    public async Task<CloudWorkItemPage> ListAsync(
        string? employeeNo = null, string? password = null, int limit = 20,
        string? kind = null, string? status = null,
        CloudWorkItemCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        if(employeeNo is not null || password is not null) ValidateCredentials(employeeNo!,password!);
        if(limit is < 1 or > 50 ||
           (kind is not null && kind is not ("VOID" or "UPLOAD_ISSUE")) ||
           (status is not null && status is not ("open" or "review" or "closed")))
            throw new ArgumentException("Work Item list filter is invalid.");
        if(cursor is not null)
        {
            ValidateId(cursor.ItemId);
            if(!ValidTimestamp(cursor.CreatedAt))
                throw new ArgumentException("Work Item cursor is invalid.",nameof(cursor));
        }
        // Work Item scope is exclusively derived from the active Cloud Device;
        // never send a client-selected workspace or an original invoice.
        var query=new Dictionary<string,object?>{ ["limit"]=limit };
        if(employeeNo is not null){query["employeeNo"]=employeeNo;query["password"]=password;}
        if(kind is not null)query["kind"]=kind;
        if(status is not null)query["status"]=status;
        if(cursor is not null)query["cursor"]=new{
            createdAt=cursor.CreatedAt,itemId=cursor.ItemId
        };
        using var response=await SendAsync("list",query,cancellationToken)
            .ConfigureAwait(false);
        var root=response.RootElement;
        if(!root.TryGetProperty("items",out var items) ||
           items.ValueKind!=JsonValueKind.Array ||
           items.GetArrayLength()>limit ||
           !root.TryGetProperty("nextCursor",out var next))
            throw new InvalidDataException("Cloud Work Item list reply is invalid.");
        var parsed=new List<CloudWorkItemSummary>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        string? lastTime=null;
        string? lastId=null;
        foreach(var row in items.EnumerateArray())
        {
            // Fail closed if the service accidentally exposes extra business
            // data or fields beyond the manager's approved seven-field view.
            if(row.ValueKind!=JsonValueKind.Object||
               row.EnumerateObject().Count()!=8)
                throw new InvalidDataException("Work Item list exposed unexpected fields.");
            string Required(string key)
            {
                if(!row.TryGetProperty(key,out var field) ||
                   field.ValueKind!=JsonValueKind.String ||
                   string.IsNullOrWhiteSpace(field.GetString()))
                    throw new InvalidDataException("Work Item list has a missing field.");
                return field.GetString()!;
            }
            var id=Required("work_item_id");
            var environment=Required("environment");
            var itemKind=Required("kind");
            if(!row.TryGetProperty("issue_code",out var code)||
               code.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new InvalidDataException("Missing reported issue type.");
            var issueCode=code.ValueKind==JsonValueKind.Null?null:code.GetString();
            if((itemKind=="UPLOAD_ISSUE" && issueCode is not (
                   "invoice_list_failed" or "query_failed" or "remote_not_found" or
                   "unknown_remote_not_found" or "reconciliation_failed" or "ambiguous_match" or
                   "local_write_failed" or "upload_failed" or "duplicate_order_id"))||
               (itemKind!="UPLOAD_ISSUE" && issueCode is not null))
                throw new InvalidDataException("Work Item type does not match issue reason.");
            var state=Required("status");
            var createdAt=Required("created_at");
            var updatedAt=Required("updated_at");
            if(!row.TryGetProperty("revision",out var revision)||
               revision.ValueKind!=JsonValueKind.Number ||
               !revision.TryGetInt32(out var rev)||rev<1||
               !Regex.IsMatch(id,@"^[a-zA-Z0-9_-]{10,100}$",RegexOptions.CultureInvariant)||
               !seen.Add(id)||
               environment is not ("production" or "test")||
               itemKind is not ("VOID" or "UPLOAD_ISSUE")||
               state is not ("open" or "review" or "closed")||
               (kind is not null && kind!=itemKind)||
               (status is not null && status!=state)||
               !ValidTimestamp(createdAt)||!ValidTimestamp(updatedAt)||
               (lastTime is not null &&
                (string.CompareOrdinal(createdAt,lastTime)>0||
                 (createdAt==lastTime && string.CompareOrdinal(id,lastId)>=0)))||
               (cursor is not null &&
                (string.CompareOrdinal(createdAt,cursor.CreatedAt)>0 ||
                 (createdAt==cursor.CreatedAt &&
                  string.CompareOrdinal(id,cursor.ItemId)>=0))))
                throw new InvalidDataException("Work Item list has inconsistent data.");
            var timestamp=DateTimeOffset.ParseExact(createdAt,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
            var updated=DateTimeOffset.ParseExact(updatedAt,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal);
            parsed.Add(new CloudWorkItemSummary(id,environment,itemKind,issueCode,state,rev,timestamp,updated));
            lastTime=createdAt;lastId=id;
        }
        CloudWorkItemCursor? nextCursor=null;
        if(next.ValueKind==JsonValueKind.Object)
        {
            if(parsed.Count==0 ||next.EnumerateObject().Count()!=2||
               !next.TryGetProperty("createdAt",out var date)||
               date.ValueKind!=JsonValueKind.String||
               !next.TryGetProperty("itemId",out var last)||
               last.ValueKind!=JsonValueKind.String||
               date.GetString()!=lastTime||last.GetString()!=lastId)
                throw new InvalidDataException("Work Item list cursor is inconsistent.");
            nextCursor=new CloudWorkItemCursor(lastTime!,lastId!);
        }
        else if(next.ValueKind!=JsonValueKind.Null)
            throw new InvalidDataException("Work Item list cursor is invalid.");
        return new CloudWorkItemPage(parsed,nextCursor);
    }

    private static bool ValidTimestamp(string value)=>
        Regex.IsMatch(value,@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$",
            RegexOptions.CultureInvariant) &&
        DateTimeOffset.TryParseExact(value,"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out _);

    private async Task<JsonDocument> SendAsync(string action, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri, "v1/work-items/" + action)) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Request-ID", "win-" + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        // Never retry a mutating call: D1 may have committed before an HTTP response is lost.
        using var result = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await result.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid Cloud Work Item JSON reply.", ex);
        }
        var root = document.RootElement;
        if (result.IsSuccessStatusCode && root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return document;
        var code = root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "WORK_ITEM_UNAVAILABLE" : "WORK_ITEM_UNAVAILABLE";
        document.Dispose();
        throw new CloudApiException(code, "Work Item operation was not confirmed; query its status before retry.",
            result.StatusCode);
    }

    private CloudWorkItem ReadItem(JsonElement root, string expectedId)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object ||
            item.EnumerateObject().Count() != 7)
            throw new InvalidDataException("Cloud Work Item reply must have the approved seven fields.");
        string Required(string key)
        {
            if (!item.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Cloud Work Item field is missing: " + key);
            var value = field.GetString();
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Cloud Work Item field is empty: " + key);
            return value;
        }
        var id = Required("work_item_id");
        var workspace = Required("workspace_id");
        var environment = Required("environment");
        var kind = Required("kind");
        var referenceHash = Required("reference_hash");
        var status = Required("status");
        if (id != expectedId || workspace != workspaceId ||
            environment is not ("production" or "test") ||
            kind is not ("VOID" or "UPLOAD_ISSUE") ||
            !Regex.IsMatch(referenceHash, @"^[a-f0-9]{64}$", RegexOptions.CultureInvariant) ||
            status is not ("open" or "review" or "closed") ||
            !item.TryGetProperty("revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetInt32(out var number) || number < 1)
            throw new InvalidDataException("Cloud Work Item reply contains inconsistent data.");
        return new CloudWorkItem(id, workspace, environment, kind, referenceHash, status, number);
    }

    private static void ValidateId(string id)
    {
        if (id is null || !Regex.IsMatch(id, @"^[a-zA-Z0-9_-]{10,100}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Work Item ID is invalid.", nameof(id));
    }

    private static void ValidateCredentials(string employeeNo, string password)
    {
        if (employeeNo is null || !Regex.IsMatch(employeeNo, @"^\d{4}$", RegexOptions.CultureInvariant) ||
            password is null || password.Length is < 1 or > 200)
            throw new ArgumentException("Manager credentials are invalid.");
    }
}
