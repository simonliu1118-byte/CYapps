using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Cloud;

// V3 DRAFT transport only; official invoice confirmation and allowance issue
// are NOT performed here. The caller must persist ItemId before CreateAsync.
// If the response is lost, use GetAsync with that same ID, never a new ID.
public sealed record CloudAllowanceApplicationDraft(
    string ItemId, string OriginalInvoiceNumber, DateOnly AllowanceDate,
    int RequestedGrossAmount, string Reason, AllowanceIssueRequest UnnumberedTemplate);

public sealed record CloudAllowanceApplicationSummary(
    string ItemId, string Environment, string Status, int Revision,
    string OriginalInvoiceNumber, string AllowanceDay, int RequestedGrossAmount,
    string Reason, string ApplicantEmployeeId, string SellerInvoice, DateTimeOffset CreatedAt);

public sealed record CloudAllowanceReviewItem(
    string Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount,
    decimal? Tax, int? TaxType);
public sealed record CloudAllowanceReviewPreview(
    string ItemId, int Revision, string OriginalInvoiceNumber, string AllowanceDay,
    string SellerInvoice, string Environment, int RequestedGrossAmount,
    string Reason, string ApplicantEmployeeId, string TemplateSha256,
    IReadOnlyList<CloudAllowanceReviewItem> Items, string OfficialVerification,
    bool CanApprove);

public sealed record CloudAllowanceItemCheck(
    bool FullyMatched, string Reason, int VerifiedLines);

public sealed record CloudOfficialAllowanceInspection(
    string ItemId, int Revision, string OriginalInvoiceNumber, DateTimeOffset CheckedAtUtc,
    string OfficialSnapshotSha256, int OfficialUploadStatus, bool Cancelled,
    int OfficialInvoiceGross, int RequestedGrossAmount, int ExistingAllowanceCount,
    bool UploadComplete, bool NotCancelled, bool AllowanceSummaryExplicit,
    bool ZeroExistingAllowances, bool InvoiceGrossEnough, bool OfficialProductsPresent,
    bool RequiresPerItemVerification, bool CanApprove, string Status,
    bool OfficialPendingMutation, CloudAllowanceItemCheck ItemCheck);

public sealed record CloudAllowanceNumberReadback(
    string ItemId, int Revision, string AllowanceNumber, string LedgerStatus,
    DateTimeOffset CheckedAtUtc, string OfficialSnapshotSha256,
    string OfficialInvoiceType, int OfficialUploadStatus, bool OfficialCancelled,
    bool ExactOriginalInvoiceMatch, bool GrossMatchesFrozenRequest,
    bool PossibleMatch, bool RequiresManualReconciliation,
    bool SafeToAutoResend, bool SafeToAutoConfirm);

// On-demand comparison is advisory; absence and matches never authorize resend.
public sealed record CloudAllowanceReconciliation(
    string ItemId, int Revision, string AllowanceNumber, string ListReason,
    CloudAllowanceNumberReadback? Readback);

public sealed record CloudAllowanceManagerApproval(
    string ApprovalId,string ItemId,int Revision,string ApprovedByEmployeeId,
    DateTimeOffset ApprovedAt,DateTimeOffset ValidUntilUtc,
    bool CanIssueNow,bool RequiresFinalPreflight);
public sealed record CloudAllowanceReservedNumber(
    string ItemId,int Revision,string AllowanceNumber,int Attempt,string Status,
    bool CanAutoIssue,bool RequiresFinalPreflight);
public sealed record CloudAllowanceNumberStatus(
    string ItemId,int Revision,string AllowanceNumber,int Attempt,string Status,
    bool MustReadbackBeforeAnyNewAttempt,bool CanAutoIssue);
public sealed record CloudAllowanceOneShotAttempt(
    string ItemId,int Revision,string AllowanceNumber,string Result,int? OfficialCode,
    bool SafeToAutoResend,bool SafeToAutoConfirm,bool MustReadbackByOriginalNumber);

public sealed record CloudAllowanceApplicationCursor(string CreatedAt, string ItemId);
public sealed record CloudAllowanceApplicationPage(
    IReadOnlyList<CloudAllowanceApplicationSummary> Applications,
    CloudAllowanceApplicationCursor? NextCursor);

public sealed class CloudAllowanceApplicationClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string token;

    public CloudAllowanceApplicationClient(HttpClient http, Uri endpoint, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0)
            throw new ArgumentException("Allowance work requires a trusted HTTPS endpoint.", nameof(endpoint));
        if (deviceToken is null ||
            !Regex.IsMatch(deviceToken, @"^cydev_[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid allowance Device identity.", nameof(deviceToken));
        this.http = http;
        this.endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        token = deviceToken.ToLowerInvariant();
    }

    public static string CreateItemId() => "wi_" + Guid.NewGuid().ToString("N");

    public async Task<string> CreateAsync(
        CloudAllowanceApplicationDraft draft, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        VerifyDraft(draft);
        Credentials(employeeNo, password);
        var json = JsonSerializer.Serialize(draft.UnnumberedTemplate);
        using var response = await SendAsync("create", new {
            itemId = draft.ItemId,
            originalInvoiceNumber = draft.OriginalInvoiceNumber,
            allowanceDay = draft.AllowanceDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
            requestedGrossAmount = draft.RequestedGrossAmount,
            reason = draft.Reason,
            unnumberedTemplateJson = json,
            employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        if (!root.TryGetProperty("itemId", out var id) ||
            id.ValueKind != JsonValueKind.String || id.GetString() != draft.ItemId ||
            !root.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() != "open" ||
            !root.TryGetProperty("revision", out var rev) ||
            rev.ValueKind != JsonValueKind.Number || !rev.TryGetInt32(out var version) || version != 1)
            throw new InvalidDataException("Allowance application creation reply is inconsistent.");
        return draft.ItemId;
    }

    public async Task<CloudAllowanceApplicationSummary?> GetAsync(
        string itemId, string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        VerifyId(itemId);
        Credentials(employeeNo, password);
        try
        {
            using var response = await SendAsync("get", new {itemId, employeeNo, password},
                cancellationToken).ConfigureAwait(false);
            var root = response.RootElement;
            if (!root.TryGetProperty("application", out var application))
                throw new InvalidDataException("Allowance application readback is missing.");
            var result = Parse(application);
            if (result.ItemId != itemId)
                throw new InvalidDataException("Allowance application readback returned a different ID.");
            return result;
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex.Code == "NOT_FOUND")
        {
            return null;
        }
    }

    // Read-only frozen Work Item preview. This DOES NOT query AMEGO, create
    // an approval grant, reserve a number, or submit a g0401 request.
    public async Task<CloudAllowanceReviewPreview> PreviewForManagerAsync(
        string itemId, int expectedRevision, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        VerifyId(itemId);
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        Credentials(employeeNo, password);
        using var result = await SendAsync("review-preview", new {
            itemId, expectedRevision, employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var root = result.RootElement;
        if (!root.TryGetProperty("review", out var preview) ||
            preview.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing manager review preview.");
        var id = Required(preview, "itemId");
        var invoice = Required(preview, "originalInvoiceNumber");
        var day = Required(preview, "allowanceDay");
        var seller = Required(preview, "sellerInvoice");
        var env = Required(preview, "environment");
        var reason = Required(preview, "reason");
        var applicant = Required(preview, "applicantEmployeeId");
        var template = Required(preview, "templateSha256");
        var verification = Required(preview, "officialVerification");
        if (id != itemId || !ValidInvoice(invoice) || !Regex.IsMatch(day, @"^[0-9]{8}$") ||
            !Regex.IsMatch(seller, @"^[0-9]{8}$") || env is not ("production" or "test") ||
            reason.Length is < 1 or > 500 || applicant.Length is < 1 or > 100 ||
            !Regex.IsMatch(template, @"^[0-9a-f]{64}$") ||
            verification != "NOT_PERFORMED" ||
            !preview.TryGetProperty("canApprove", out var canApprove) ||
            canApprove.ValueKind != JsonValueKind.False ||
            !preview.TryGetProperty("revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetInt32(out var ver) || ver != expectedRevision ||
            !preview.TryGetProperty("requestedGrossAmount", out var gross) ||
            gross.ValueKind != JsonValueKind.Number ||
            !gross.TryGetInt32(out var amount) || amount <= 0 ||
            !preview.TryGetProperty("items", out var products) ||
            products.ValueKind != JsonValueKind.Array ||
            products.GetArrayLength() is < 1 or > 50)
            throw new InvalidDataException("Manager review preview is unsafe or inconsistent.");
        var items = products.EnumerateArray().Select(item => {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("description", out var description) ||
                description.ValueKind != JsonValueKind.String ||
                (description.GetString()?.Length ?? 0) > 300)
                throw new InvalidDataException("Invalid manager review item.");
            return new CloudAllowanceReviewItem(description.GetString()!,
                PreviewAmount(item,"quantity"),PreviewAmount(item,"unitPrice"),
                PreviewAmount(item,"amount"),PreviewAmount(item,"tax"),
                item.TryGetProperty("taxType", out var type) &&
                type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var n)
                  ? n : null);
        }).ToArray();
        return new CloudAllowanceReviewPreview(id,ver,invoice,day,seller,env,amount,
            reason,applicant,template,items,verification,false);
    }

    private static decimal? PreviewAmount(JsonElement item,string property)
    {
        if (!item.TryGetProperty(property,out var value) || value.ValueKind==JsonValueKind.Null)
            return null;
        if (value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind==JsonValueKind.String &&
            decimal.TryParse(value.GetString(),
                System.Globalization.NumberStyles.AllowLeadingSign |
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,out var parsed))
            return parsed;
        throw new InvalidDataException("Invalid numeric field in manager review preview.");
    }

    // Official AMEGO READ-ONLY query through the authorized Worker. This method
    // never approves, allocates, issues, cancels or retries any allowance.
    public async Task<CloudOfficialAllowanceInspection> InspectOfficialForManagerAsync(
        string itemId, int expectedRevision, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        VerifyId(itemId);
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        Credentials(employeeNo, password);
        using var result = await SendAsync("official-inspection", new {
            itemId, expectedRevision, employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var root = result.RootElement;
        if (!root.TryGetProperty("inspection", out var item) ||
            item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Official inspection response is missing.");
        var id = Required(item, "itemId");
        var invoice = Required(item, "originalInvoiceNumber");
        var digest = Required(item, "officialSnapshotSha256");
        var status = Required(item, "status");
        var checkedAt = Required(item, "checkedAtUtc");
        if (id != itemId || !ValidInvoice(invoice) ||
            !Regex.IsMatch(digest, @"^[a-f0-9]{64}$") ||
            status != "OFFICIAL_READ_ONLY_NOT_APPROVED" ||
            !DateTimeOffset.TryParse(checkedAt, out var instant) ||
            !item.TryGetProperty("revision", out var rev) ||
            rev.ValueKind != JsonValueKind.Number ||
            !rev.TryGetInt32(out var actualRev) || actualRev != expectedRevision)
            throw new InvalidDataException("Official inspection identity is inconsistent.");
        static int Integer(JsonElement value,string name)
        {
            if (!value.TryGetProperty(name,out var x) || x.ValueKind!=JsonValueKind.Number ||
                !x.TryGetInt32(out var n) || n<0)
                throw new InvalidDataException("Invalid official inspection number: "+name);
            return n;
        }
        static bool Boolean(JsonElement value,string name)
        {
            if (!value.TryGetProperty(name,out var x) ||
                x.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Invalid official inspection flag: "+name);
            return x.GetBoolean();
        }
        var upload=Integer(item,"officialUploadStatus");
        var gross=Integer(item,"officialInvoiceGross");
        var requested=Integer(item,"requestedGrossAmount");
        var existing=Integer(item,"existingAllowanceCount");
        var cancelled=Boolean(item,"cancelled");
        var complete=Boolean(item,"uploadComplete");
        var notCancelled=Boolean(item,"notCancelled");
        var summary=Boolean(item,"allowanceSummaryExplicit");
        var noExisting=Boolean(item,"zeroExistingAllowances");
        var enough=Boolean(item,"invoiceGrossEnough");
        var products=Boolean(item,"officialProductsPresent");
        var needsItems=Boolean(item,"requiresPerItemVerification");
        var canApprove=Boolean(item,"canApprove");
        var pending=Boolean(item,"officialPendingMutation");
        if (!item.TryGetProperty("itemCheck",out var rawCheck) ||
            rawCheck.ValueKind!=JsonValueKind.Object ||
            !rawCheck.TryGetProperty("reason",out var reason) ||
            reason.ValueKind!=JsonValueKind.String ||
            !Regex.IsMatch(reason.GetString()??"", @"^[A-Z][A-Z0-9_]{0,100}$") ||
            !rawCheck.TryGetProperty("verifiedLines",out var lines) ||
            lines.ValueKind!=JsonValueKind.Number ||
            !lines.TryGetInt32(out var verifiedCount) ||
            verifiedCount<0 || verifiedCount>50)
            throw new InvalidDataException("Official item verification status is invalid.");
        var matched=Boolean(rawCheck,"fullyMatched");
        if (matched && (pending || cancelled || existing!=0 || !summary ||
                        upload!=99 || verifiedCount==0 ||
                        reason.GetString()!="SOURCE_ITEMS_MATCHED_NO_PRIOR_ALLOWANCE") ||
            !matched && verifiedCount!=0)
            throw new InvalidDataException("Official item verification cannot override safety fences.");
        if (requested==0 || canApprove || !needsItems ||
            complete!=(upload==99) || notCancelled==cancelled ||
            noExisting!=(summary && existing==0) ||
            enough!=(gross>=requested))
            throw new InvalidDataException("Official inspection must remain non-authorizing.");
        return new CloudOfficialAllowanceInspection(id,actualRev,invoice,instant,digest,
            upload,cancelled,gross,requested,existing,complete,notCancelled,summary,
            noExisting,enough,products,needsItems,false,status,
            pending,new CloudAllowanceItemCheck(matched,reason.GetString()!,verifiedCount));
    }

    // The Worker owns the original D1 allowance number; callers cannot
    // supply another number. Neither endpoint authorizes any AMEGO mutation.
    public async Task<CloudAllowanceNumberReadback> ReadbackNumberForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var result=await ManagerActionAsync("number-readback",itemId,
            expectedRevision,employeeNo,password,cancellationToken).ConfigureAwait(false);
        return ParseNumberReadback(result.RootElement,itemId,expectedRevision);
    }

    public async Task<CloudAllowanceReconciliation> ReconcileForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var result=await ManagerActionAsync("reconcile",itemId,
            expectedRevision,employeeNo,password,cancellationToken).ConfigureAwait(false);
        var root=result.RootElement;
        var record=RequiredObject(root,"reconciliation");
        var actualId=Required(record,"itemId");
        var number=Required(record,"allowanceNumber");
        var reason=Required(record,"listReason");
        if(actualId!=itemId || Int(record,"expectedRevision")!=expectedRevision ||
            !ValidAllowanceNumber(number) ||
            reason is not ("NOT_SEEN_NOT_PROOF" or
                "OFFICIAL_STATUS_CHANGED_OR_PENDING" or "CONFLICTING_LIST_RESULTS") ||
            !Flag(record,"requiresManualReconciliation") ||
            Flag(record,"safeToAutoResend") || Flag(record,"safeToAutoConfirm"))
            throw new InvalidDataException("Reconciliation cannot authorize an automatic action.");
        if(!root.TryGetProperty("readback",out var evidence))
        {
            if(reason!="NOT_SEEN_NOT_PROOF")
                throw new InvalidDataException("Official list hit has no original-number readback.");
            return new CloudAllowanceReconciliation(itemId,expectedRevision,number,reason,null);
        }
        if(reason=="NOT_SEEN_NOT_PROOF" || evidence.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Official readback contradicts list evidence.");
        var readback=ParseNumberReadback(root,itemId,expectedRevision);
        if(readback.AllowanceNumber!=number)
            throw new InvalidDataException("Reconciliation changed the D1 original number.");
        return new CloudAllowanceReconciliation(itemId,expectedRevision,number,reason,readback);
    }

    private static CloudAllowanceNumberReadback ParseNumberReadback(
        JsonElement root,string itemId,int expectedRevision)
    {
        if(!root.TryGetProperty("readback",out var value) ||
           value.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Missing original-number readback.");
        var id=Required(value,"itemId");
        var number=Required(value,"allowanceNumber");
        var status=Required(value,"ledgerStatus");
        var date=Required(value,"checkedAtUtc");
        var digest=Required(value,"officialSnapshotSha256");
        var invoiceType=Required(value,"officialInvoiceType");
        if(id!=itemId ||
           !Regex.IsMatch(number,@"^A[0-9]{8}(?:[0-9]{3}|[A-Z][0-9A-Z]{2})$")||
           status is not ("sending" or "confirmed")||
           !DateTimeOffset.TryParse(date,out var checkedAt)||
           !Regex.IsMatch(digest,@"^[0-9a-f]{64}$")||
           invoiceType.Length is < 1 or > 10 ||
           !value.TryGetProperty("expectedRevision",out var rev)||
           rev.ValueKind!=JsonValueKind.Number||
           !rev.TryGetInt32(out var actualRev)||actualRev!=expectedRevision||
           !value.TryGetProperty("officialUploadStatus",out var upload)||
           upload.ValueKind!=JsonValueKind.Number ||
           !upload.TryGetInt32(out var uploadStatus)||uploadStatus<0)
            throw new InvalidDataException("Original-number readback has invalid identifiers.");
        static bool Flag(JsonElement element,string name){
            if(!element.TryGetProperty(name,out var v)||
               v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Readback boolean invalid: "+name);
            return v.GetBoolean();
        }
        var cancelled=Flag(value,"officialCancelled");
        var original=Flag(value,"exactOriginalInvoiceMatch");
        var gross=Flag(value,"grossMatchesFrozenRequest");
        var possible=Flag(value,"possibleMatch");
        var manual=Flag(value,"requiresManualReconciliation");
        var resend=Flag(value,"safeToAutoResend");
        var confirm=Flag(value,"safeToAutoConfirm");
        if(resend||confirm||!manual||
           (possible&&(!original||!gross||cancelled||uploadStatus!=99)))
            throw new InvalidDataException("Readback cannot authorize an automatic AMEGO action.");
        return new CloudAllowanceNumberReadback(
            id,actualRev,number,status,checkedAt,digest,invoiceType,uploadStatus,
            cancelled,original,gross,possible,manual,false,false);
    }

    public async Task<CloudAllowanceManagerApproval> ApproveForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var root=await ManagerActionAsync("approve",itemId,expectedRevision,employeeNo,
            password,cancellationToken).ConfigureAwait(false);
        var grant=RequiredObject(root.RootElement,"approval");
        if(Required(grant,"itemId")!=itemId||Int(grant,"revision")!=expectedRevision||
           Flag(grant,"canIssueNow")||!Flag(grant,"requiresFinalPreflight"))
            throw new InvalidDataException("CYID manager approval must not imply issuance.");
        var approvalId=Required(grant,"approvalId");
        var employee=Required(grant,"approvedByEmployeeId");
        if(!approvalId.StartsWith("apr_",StringComparison.Ordinal)||
           employee.Length>100||
           !DateTimeOffset.TryParse(Required(grant,"approvedAt"),out var approved)||
           !DateTimeOffset.TryParse(Required(grant,"validUntilUtc"),out var expires)||
           expires<=approved)
            throw new InvalidDataException("Manager approval receipt is inconsistent.");
        return new CloudAllowanceManagerApproval(
            approvalId,itemId,expectedRevision,employee,approved,expires,false,true);
    }

    public async Task<CloudAllowanceReservedNumber> ReserveForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var root=await ManagerActionAsync("reserve",itemId,expectedRevision,employeeNo,
            password,cancellationToken).ConfigureAwait(false);
        var number=RequiredObject(root.RootElement,"number");
        var reserved=Required(number,"allowanceNumber");
        if(Required(number,"itemId")!=itemId||
           Int(number,"revision")!=expectedRevision||
           !ValidAllowanceNumber(reserved)||Int(number,"attempt")<1||
           Required(number,"status")!="reserved"||
           Flag(number,"canAutoIssue")||!Flag(number,"requiresFinalPreflight"))
            throw new InvalidDataException("Cloud numbering receipt is not safe.");
        return new CloudAllowanceReservedNumber(itemId,expectedRevision,reserved,
            Int(number,"attempt"),"reserved",false,true);
    }

    public async Task<CloudAllowanceNumberStatus> GetNumberStatusForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var root=await ManagerActionAsync("number-status",itemId,expectedRevision,
            employeeNo,password,cancellationToken).ConfigureAwait(false);
        var number=RequiredObject(root.RootElement,"number");
        var status=Required(number,"status");
        var allowance=Required(number,"allowanceNumber");
        var ambiguous=Flag(number,"mustReadbackBeforeAnyNewAttempt");
        if(Required(number,"itemId")!=itemId||Int(number,"revision")!=expectedRevision||
           !ValidAllowanceNumber(allowance)||Int(number,"attempt")<1||
           status is not ("reserved" or "sending" or "confirmed" or "occupied_external")||
           Flag(number,"canAutoIssue")||ambiguous!=(status=="sending"))
            throw new InvalidDataException("Cloud number status cannot enable automatic resend.");
        return new CloudAllowanceNumberStatus(itemId,expectedRevision,allowance,
            Int(number,"attempt"),status,ambiguous,false);
    }

    // Mutating and strictly ONE attempt. Caller must request explicit human
    // confirmation and must never retry after timeout or ambiguous response.
    public async Task<CloudAllowanceOneShotAttempt> SendOnceForManagerAsync(
        string itemId,int expectedRevision,string employeeNo,string password,
        CancellationToken cancellationToken=default)
    {
        using var root=await ManagerActionAsync("send-once",itemId,expectedRevision,employeeNo,
            password,cancellationToken).ConfigureAwait(false);
        var attempt=RequiredObject(root.RootElement,"attempt");
        var kind=Required(attempt,"result");
        var allowance=Required(attempt,"allowanceNumber");
        var code=attempt.TryGetProperty("officialCode",out var prop)&&
            prop.ValueKind==JsonValueKind.Number&&prop.TryGetInt32(out var n)?n:(int?)null;
        if(Required(attempt,"itemId")!=itemId||Int(attempt,"revision")!=expectedRevision||
           !ValidAllowanceNumber(allowance)||
           kind is not ("ACCEPTED_PENDING_OFFICIAL" or "DEFINITE_NUMBER_COLLISION" or
             "DEFINITE_REJECTION_MANUAL" or "UNKNOWN_REQUIRES_ORIGINAL_NUMBER_READBACK")||
           Flag(attempt,"safeToAutoResend")||Flag(attempt,"safeToAutoConfirm")||
           !Flag(attempt,"mustReadbackByOriginalNumber"))
            throw new InvalidDataException("Cloud first-send reply is inconsistent.");
        return new CloudAllowanceOneShotAttempt(itemId,expectedRevision,allowance,kind,
            code,false,false,true);
    }

    private async Task<JsonDocument> ManagerActionAsync(string action,string itemId,
        int revision,string employeeNo,string password,CancellationToken cancellationToken)
    {
        VerifyId(itemId);
        if(revision<1)throw new ArgumentOutOfRangeException(nameof(revision));
        Credentials(employeeNo,password);
        return await SendAsync(action,new{itemId,expectedRevision=revision,
            employeeNo,password},cancellationToken).ConfigureAwait(false);
    }
    private static JsonElement RequiredObject(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var x)||x.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Missing Cloud manager result: "+name);
        return x;
    }
    private static int Int(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var x)||x.ValueKind!=JsonValueKind.Number||
           !x.TryGetInt32(out var n)||n<0)
            throw new InvalidDataException("Cloud manager result integer invalid: "+name);
        return n;
    }
    private static bool Flag(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var x)||
           x.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Cloud manager result flag invalid: "+name);
        return x.GetBoolean();
    }
    private static bool ValidAllowanceNumber(string value) =>
        Regex.IsMatch(value,@"^A[0-9]{8}(?:[0-9]{3}|[A-Z][0-9A-Z]{2})$",
            RegexOptions.CultureInvariant);

    public async Task<CloudAllowanceApplicationPage> ListForManagerAsync(
        int limit, CloudAllowanceApplicationCursor? cursor,
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        Credentials(employeeNo, password);
        if (limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor is not null && (!ValidDate(cursor.CreatedAt) || !ValidId(cursor.ItemId)))
            throw new ArgumentException("Invalid allowance list cursor.", nameof(cursor));
        object body = cursor is null ? new {limit, employeeNo, password} : new {
            limit, cursorCreatedAt = cursor.CreatedAt, cursorItemId = cursor.ItemId, employeeNo, password
        };
        using var response = await SendAsync("list", body, cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        if (!root.TryGetProperty("applications", out var rows) || rows.ValueKind != JsonValueKind.Array ||
            rows.GetArrayLength() > limit ||
            !root.TryGetProperty("nextCursor", out var next))
            throw new InvalidDataException("Allowance application list reply is inconsistent.");
        var items = rows.EnumerateArray().Select(Parse).ToArray();
        CloudAllowanceApplicationCursor? nextCursor = null;
        if (next.ValueKind == JsonValueKind.Object)
        {
            var created = Required(next, "cursorCreatedAt");
            var itemId = Required(next, "cursorItemId");
            if (!ValidDate(created) || !ValidId(itemId) || items.Length == 0 ||
                items[^1].ItemId != itemId)
                throw new InvalidDataException("Allowance application list cursor is inconsistent.");
            nextCursor = new CloudAllowanceApplicationCursor(created, itemId);
        }
        else if (next.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Allowance application list cursor type is invalid.");
        return new CloudAllowanceApplicationPage(items, nextCursor);
    }

    private async Task<JsonDocument> SendAsync(
        string action, object body, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri(endpoint, "v1/allowance-applications/" + action)) {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("X-Request-ID", "win-" + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        // Never auto-retry create on 409, ambiguous HTTP or timeout.
        using var result = await http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await result.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
            .ConfigureAwait(false); }
        catch (JsonException ex) { throw new InvalidDataException("Invalid allowance application JSON reply.", ex); }
        var root = doc.RootElement;
        if (result.IsSuccessStatusCode && root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            return doc;
        var errorCode = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() ?? "ALLOWANCE_APPLICATION_UNAVAILABLE" : "ALLOWANCE_APPLICATION_UNAVAILABLE";
        doc.Dispose();
        throw new CloudApiException(errorCode,
            "Allowance application result is not confirmed; use the same Work Item ID to read back.",
            result.StatusCode);
    }

    private static CloudAllowanceApplicationSummary Parse(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Allowance application record is invalid.");
        var id=Required(obj,"itemId");
        var env=Required(obj,"environment");
        var state=Required(obj,"status");
        var invoice=Required(obj,"originalInvoiceNumber");
        var day=Required(obj,"allowanceDay");
        var reason=Required(obj,"reason");
        var applicant=Required(obj,"applicantEmployeeId");
        var seller=Required(obj,"sellerInvoice");
        var created=Required(obj,"createdAt");
        if (!ValidId(id) || env is not ("production" or "test") ||
            state is not ("open" or "review" or "closed") ||
            !ValidInvoice(invoice) || !Regex.IsMatch(day, @"^\d{8}$") ||
            reason.Length is < 1 or > 500 || applicant.Length>100 ||
            !Regex.IsMatch(seller, @"^\d{8}$") ||
            !DateTimeOffset.TryParse(created, out var timestamp) ||
            !obj.TryGetProperty("revision", out var rev) ||
            rev.ValueKind != JsonValueKind.Number || !rev.TryGetInt32(out var version) || version < 1 ||
            !obj.TryGetProperty("requestedGrossAmount", out var gross) ||
            gross.ValueKind != JsonValueKind.Number || !gross.TryGetInt32(out var amount) || amount < 1)
            throw new InvalidDataException("Allowance application record fails validation.");
        return new CloudAllowanceApplicationSummary(id,env,state,version,invoice,day,amount,
            reason,applicant,seller,timestamp);
    }
    private static string Required(JsonElement obj,string key)
    {
        if (!obj.TryGetProperty(key,out var el) || el.ValueKind!=JsonValueKind.String ||
            string.IsNullOrEmpty(el.GetString()))
            throw new InvalidDataException("Allowance application field missing: "+key);
        return el.GetString()!;
    }
    private static void VerifyDraft(CloudAllowanceApplicationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!ValidId(draft.ItemId) || !ValidInvoice(draft.OriginalInvoiceNumber) ||
            draft.RequestedGrossAmount <= 0 || string.IsNullOrWhiteSpace(draft.Reason) ||
            draft.Reason.Length>500 || draft.UnnumberedTemplate is null ||
            !string.IsNullOrEmpty(draft.UnnumberedTemplate.AllowanceNumber) ||
            draft.UnnumberedTemplate.AllowanceDate != draft.AllowanceDate.ToString("yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture) ||
            draft.UnnumberedTemplate.ProductItem is null ||
            draft.UnnumberedTemplate.ProductItem.Count is < 1 or > 50 ||
            draft.UnnumberedTemplate.ProductItem.Any(p => p.OriginalInvoiceNumber!=draft.OriginalInvoiceNumber))
            throw new ArgumentException("Allowance template is not a frozen unnumbered request.",nameof(draft));
    }
    private static void VerifyId(string id)
    {
        if (!ValidId(id))throw new ArgumentException("Invalid Work Item ID.",nameof(id));
    }
    private static bool ValidId(string? v) => v is not null &&
        Regex.IsMatch(v,@"^[A-Za-z0-9_-]{10,100}$",RegexOptions.CultureInvariant);
    private static bool ValidInvoice(string? v)=>v is not null &&
        Regex.IsMatch(v,@"^[A-Z]{2}[0-9]{8}$",RegexOptions.CultureInvariant);
    private static bool ValidDate(string s)=>!string.IsNullOrEmpty(s) &&
        DateTimeOffset.TryParse(s,out _);
    private static void Credentials(string employeeNo,string password)
    {
        if (employeeNo is null || !Regex.IsMatch(employeeNo,@"^\d{4}$")||
            password is null || password.EnumerateRunes().Count() is < 8 or > 16)
            throw new ArgumentException("CYID credentials are invalid.");
    }
}
