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
