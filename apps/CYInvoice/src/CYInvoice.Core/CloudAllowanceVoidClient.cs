using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Cloud;

// V3 DRAFT: CYID-only g0501 cancellation transport. Create's Work Item ID
// MUST be retained before submission. Lost replies require Get/Readback;
// never repeat Create or SendOnce automatically.
public sealed record CloudAllowanceVoidDraft(
    string ItemId, string AllowanceNumber, string OriginalInvoiceNumber, string Reason);
public sealed record CloudAllowanceVoidSummary(
    string ItemId, string Environment, string AllowanceNumber, string OriginalInvoiceNumber,
    string Reason, string ApplicantEmployeeId, string SellerInvoice,
    DateTimeOffset CreatedAt, int Revision, string Status);
public sealed record CloudAllowanceVoidCursor(string CursorCreatedAt, string CursorItemId);
public sealed record CloudAllowanceVoidPage(
    IReadOnlyList<CloudAllowanceVoidSummary> Applications, CloudAllowanceVoidCursor? NextCursor);
public sealed record CloudAllowanceVoidApproval(
    string ApprovalId, string ItemId, int Revision, DateTimeOffset ValidUntilUtc,
    bool CanIssueNow);
public sealed record CloudAllowanceVoidAttempt(
    string ItemId, int Revision, string AllowanceNumber, string Outcome, int? OfficialCode,
    bool CanAutoRetry, bool CanAutoClose, bool MustReadback);
public sealed record CloudAllowanceVoidOfficialReadback(
    string ItemId, int Revision, string AllowanceNumber, string OfficialType,
    int OfficialUploadStatus, bool OfficialCancelled, bool DefinitiveOpen,
    DateTimeOffset CheckedAtUtc, string EvidenceSha256,
    bool CanAutoRetry, bool CanAutoClose);
public sealed record CloudAllowanceVoidClosure(
    string ItemId, int PreviousRevision, int Revision, string AllowanceNumber,
    bool OfficialConfirmed);

public sealed class CloudAllowanceVoidClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string token;

    public CloudAllowanceVoidClient(HttpClient http, Uri endpoint, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0)
            throw new ArgumentException("A trusted HTTPS CYID endpoint is required.", nameof(endpoint));
        if (deviceToken is null || !Regex.IsMatch(deviceToken,
            @"^cydev_[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid CYID Device bearer.", nameof(deviceToken));
        this.http = http;
        this.endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        token = deviceToken.ToLowerInvariant();
    }

    public static string CreateItemId() => "wi_" + Guid.NewGuid().ToString("N");

    public async Task<string> CreateAsync(CloudAllowanceVoidDraft draft,
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        VerifyId(draft.ItemId);
        if (!ValidAllowanceNumber(draft.AllowanceNumber) ||
            !Regex.IsMatch(draft.OriginalInvoiceNumber ?? "", @"^[A-Z]{2}[0-9]{8}$") ||
            string.IsNullOrWhiteSpace(draft.Reason) || draft.Reason.Trim() != draft.Reason ||
            draft.Reason.Length > 500 || draft.Reason.Any(char.IsControl))
            throw new ArgumentException("Invalid original cancellation request.", nameof(draft));
        Credentials(employeeNo, password);
        using var doc = await SendAsync("create", new {
            itemId = draft.ItemId, allowanceNumber = draft.AllowanceNumber,
            originalInvoiceNumber = draft.OriginalInvoiceNumber, reason = draft.Reason,
            employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (Required(root, "itemId") != draft.ItemId ||
            Required(root, "status") != "open" || Int(root, "revision") != 1)
            throw new InvalidDataException("Unconfirmed allowance cancellation creation.");
        return draft.ItemId;
    }

    public async Task<CloudAllowanceVoidSummary?> GetAsync(string itemId,
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        VerifyId(itemId);
        Credentials(employeeNo, password);
        try
        {
            using var doc = await SendAsync("get", new {itemId, employeeNo, password},
                cancellationToken).ConfigureAwait(false);
            var item = ParseSummary(Object(doc.RootElement, "application"));
            if (item.ItemId != itemId)
                throw new InvalidDataException("Cancellation lookup changed the Work Item.");
            return item;
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound &&
                                           ex.Code == "NOT_FOUND")
        {
            return null;
        }
    }

    public async Task<CloudAllowanceVoidPage> ListForManagerAsync(int limit,
        CloudAllowanceVoidCursor? cursor, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        Credentials(employeeNo, password);
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor is not null && (!DateTimeOffset.TryParse(cursor.CursorCreatedAt, out _) ||
                                    !ValidId(cursor.CursorItemId)))
            throw new ArgumentException("Invalid cloud pagination cursor.", nameof(cursor));
        object request = cursor is null
            ? new {limit, employeeNo, password}
            : new {limit, cursorCreatedAt = cursor.CursorCreatedAt,
                cursorItemId = cursor.CursorItemId, employeeNo, password};
        using var doc = await SendAsync("list", request, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("applications", out var values) ||
            values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > limit ||
            !root.TryGetProperty("nextCursor", out var next))
            throw new InvalidDataException("Invalid cloud cancellation list.");
        var entries = values.EnumerateArray().Select(ParseSummary).ToArray();
        CloudAllowanceVoidCursor? parsed = null;
        if (next.ValueKind == JsonValueKind.Object)
        {
            var createdAt = Required(next, "cursorCreatedAt");
            var id = Required(next, "cursorItemId");
            if (!DateTimeOffset.TryParse(createdAt, out _) || !ValidId(id) ||
                entries.Length == 0 || entries[^1].ItemId != id)
                throw new InvalidDataException("Cancellation cursor is inconsistent.");
            parsed = new CloudAllowanceVoidCursor(createdAt, id);
        }
        else if (next.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Invalid cloud cancellation list cursor.");
        return new CloudAllowanceVoidPage(entries, parsed);
    }

    public async Task<CloudAllowanceVoidApproval> ApproveAsync(string itemId, int revision,
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        using var doc = await ActionAsync("approve", itemId, revision, employeeNo,
            password, cancellationToken).ConfigureAwait(false);
        var r = doc.RootElement;
        var approvalId = Required(r, "approvalId");
        if (Required(r, "itemId") != itemId || Int(r, "revision") != revision ||
            !approvalId.StartsWith("ava_", StringComparison.Ordinal) ||
            !DateTimeOffset.TryParse(Required(r, "validUntilUtc"), out var until) ||
            Flag(r, "canIssueNow"))
            throw new InvalidDataException("Approval must not imply issuance permission.");
        return new CloudAllowanceVoidApproval(approvalId, itemId, revision, until, false);
    }

    // A mutating HTTP operation. Never loop, retry, or create a new ID after
    // any ambiguous response, even if the network timed out before headers.
    public async Task<CloudAllowanceVoidAttempt> SendOnceAsync(string itemId, int revision,
        string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        using var doc = await ActionAsync("send-once", itemId, revision, employeeNo,
            password, cancellationToken).ConfigureAwait(false);
        var r = doc.RootElement;
        var outcome = Required(r, "outcome");
        var number = Required(r, "allowanceNumber");
        if (Required(r, "itemId") != itemId || Int(r, "revision") != revision ||
            !ValidAllowanceNumber(number) ||
            outcome is not ("accepted_unconfirmed" or "definite_rejection_manual" or "unknown_result") ||
            Flag(r, "canAutoRetry") || Flag(r, "canAutoClose") || !Flag(r, "mustReadback"))
            throw new InvalidDataException("g0501 reply cannot authorize resending or closing.");
        int? code = null;
        if (r.TryGetProperty("officialCode", out var v))
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < 0)
                throw new InvalidDataException("Invalid official g0501 response code.");
            code = n;
        }
        if ((outcome == "accepted_unconfirmed" && code != 0) ||
            (outcome == "definite_rejection_manual" && (code is null or 0)) ||
            (outcome == "unknown_result" && code is not null))
            throw new InvalidDataException("Inconsistent official g0501 outcome.");
        return new CloudAllowanceVoidAttempt(itemId, revision, number, outcome, code,
            false, false, true);
    }

    public async Task<CloudAllowanceVoidOfficialReadback> ReadbackAsync(string itemId,
        int revision, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        using var doc = await ActionAsync("status-readback", itemId, revision, employeeNo,
            password, cancellationToken).ConfigureAwait(false);
        var r = Object(doc.RootElement, "readback");
        var number = Required(r, "allowanceNumber");
        var type = Required(r, "officialType");
        var hash = Required(r, "evidenceSha256");
        var cancelled = Flag(r, "officialCancelled");
        var stillOpen = Flag(r, "definitiveOpen");
        var upload = Int(r, "officialUploadStatus");
        var expectedCancelled = new[] {"D0501", "B0501", "B0201", "B0202"}.Contains(type) &&
                                upload == 99;
        var expectedOpen = new[] {"D0401", "B0401", "B0101", "B0102"}.Contains(type) &&
                           upload == 99;
        if (Required(r, "itemId") != itemId || Int(r, "revision") != revision ||
            !ValidAllowanceNumber(number) || !Regex.IsMatch(hash, @"^[0-9a-f]{64}$") ||
            !Regex.IsMatch(type, @"^[A-Z0-9_]{1,16}$") ||
            Flag(r, "canAutoRetry") || Flag(r, "canAutoClose") ||
            !DateTimeOffset.TryParse(Required(r, "checkedAtUtc"), out var checkedAt) ||
            cancelled != expectedCancelled || stillOpen != expectedOpen)
            throw new InvalidDataException("Official g0501 readback is inconsistent.");
        return new CloudAllowanceVoidOfficialReadback(itemId, revision, number, type,
            upload, cancelled, stillOpen, checkedAt, hash, false, false);
    }

    // Cloud service rechecks official status under manager authority.
    public async Task<CloudAllowanceVoidClosure> CloseVerifiedAsync(string itemId,
        int revision, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        using var doc = await ActionAsync("close", itemId, revision, employeeNo,
            password, cancellationToken).ConfigureAwait(false);
        var r = doc.RootElement;
        var number = Required(r, "allowanceNumber");
        if (Required(r, "itemId") != itemId || Int(r, "previousRevision") != revision ||
            Int(r, "revision") != revision + 1 || Required(r, "status") != "closed" ||
            !ValidAllowanceNumber(number) || !Flag(r, "officialConfirmed"))
            throw new InvalidDataException("Official cancellation closure is inconsistent.");
        return new CloudAllowanceVoidClosure(itemId, revision, revision + 1, number, true);
    }

    private async Task<JsonDocument> ActionAsync(string action, string itemId,
        int revision, string employeeNo, string password, CancellationToken cancellationToken)
    {
        VerifyId(itemId);
        if (revision < 1 || revision == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(revision));
        Credentials(employeeNo, password);
        return await SendAsync(action, new {itemId, expectedRevision = revision,
            employeeNo, password}, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendAsync(string action, object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(endpoint, "v1/allowance-void-applications/" + action)) {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Request-ID", "win-" + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var reply = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        await using var stream = await reply.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
            .ConfigureAwait(false); }
        catch (JsonException ex) {
            throw new InvalidDataException("Invalid CYID allowance cancellation JSON.", ex);
        }
        var r = doc.RootElement;
        if (reply.IsSuccessStatusCode && r.ValueKind == JsonValueKind.Object &&
            r.TryGetProperty("ok", out var successful) && successful.ValueKind == JsonValueKind.True)
            return doc;
        var error = r.ValueKind == JsonValueKind.Object &&
            r.TryGetProperty("error", out var bodyError) && bodyError.ValueKind == JsonValueKind.Object &&
            bodyError.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() ?? "ALLOWANCE_VOID_UNAVAILABLE" : "ALLOWANCE_VOID_UNAVAILABLE";
        doc.Dispose();
        throw new CloudApiException(error,
            "Cancellation outcome is not confirmed. Read back the original Work Item ID; do not resend.",
            reply.StatusCode);
    }

    private static CloudAllowanceVoidSummary ParseSummary(JsonElement r)
    {
        var id = Required(r, "itemId");
        var env = Required(r, "environment");
        var status = Required(r, "status");
        var number = Required(r, "allowanceNumber");
        var original = Required(r, "originalInvoiceNumber");
        var reason = Required(r, "reason");
        var applicant = Required(r, "applicantEmployeeId");
        var seller = Required(r, "sellerInvoice");
        if (!ValidId(id) || env is not ("test" or "production") ||
            status is not ("open" or "review" or "closed") ||
            !ValidAllowanceNumber(number) ||
            !Regex.IsMatch(original, @"^[A-Z]{2}[0-9]{8}$") ||
            reason.Length is < 1 or > 500 || applicant.Length is < 1 or > 100 ||
            !Regex.IsMatch(seller, @"^[0-9]{8}$") ||
            !DateTimeOffset.TryParse(Required(r, "createdAt"), out var created) ||
            Int(r, "revision") < 1)
            throw new InvalidDataException("Invalid allowance cancellation summary.");
        return new CloudAllowanceVoidSummary(id, env, number, original, reason,
            applicant, seller, created, Int(r, "revision"), status);
    }
    private static JsonElement Object(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing CYID cancellation field: " + key);
        return value;
    }
    private static string Required(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(v.GetString()))
            throw new InvalidDataException("Missing CYID cancellation field: " + key);
        return v.GetString()!;
    }
    private static int Int(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number ||
            !v.TryGetInt32(out var n) || n < 0)
            throw new InvalidDataException("Invalid CYID cancellation number: " + key);
        return n;
    }
    private static bool Flag(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var v) ||
            v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid CYID cancellation flag: " + key);
        return v.GetBoolean();
    }
    private static bool ValidId(string? x) => x is not null &&
        Regex.IsMatch(x, @"^[A-Za-z0-9_-]{10,100}$", RegexOptions.CultureInvariant);
    private static bool ValidAllowanceNumber(string? x) => x is not null &&
        x.Length is >= 1 and <= 16 && x.Trim() == x && !x.Any(char.IsControl);
    private static void VerifyId(string? id)
    {
        if (!ValidId(id)) throw new ArgumentException("Invalid Work Item ID.", nameof(id));
    }
    private static void Credentials(string employeeNo, string password)
    {
        if (employeeNo is null || !Regex.IsMatch(employeeNo, @"^\d{4}$") ||
            password is null || password.EnumerateRunes().Count() is < 8 or > 16)
            throw new ArgumentException("Invalid CYID credentials.");
    }
}
