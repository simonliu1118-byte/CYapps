using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYInvoice.Core.Cloud;

// V3 coordination transport only. Does not call AMEGO or replace local invoice workflows.
// The Work Item D1 table remains an unapproved draft; the client is not wired to UI.
public sealed record CloudWorkItemDraft(
    string ItemId, string Environment, string Kind, string ReferenceHash);

public sealed record CloudWorkItem(
    string ItemId, string WorkspaceId, string Environment, string Kind,
    string ReferenceHash, string Status, int Revision);

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

    public async Task<CloudWorkItem> CreateAsync(
        CloudWorkItemDraft draft, string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateCredentials(employeeNo, password);
        ValidateDraft(draft);
        using var response = await SendAsync("create", new {
            itemId = draft.ItemId, environment = draft.Environment, kind = draft.Kind,
            referenceHash = draft.ReferenceHash, employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var item = ReadItem(response.RootElement, draft.ItemId);
        if (item.Environment != draft.Environment || item.Kind != draft.Kind ||
            item.ReferenceHash != draft.ReferenceHash || item.Status != "open" || item.Revision != 1)
            throw new InvalidDataException("Work Item create result disagrees with request.");
        return item;
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

    public async Task<CloudWorkItem> TransitionAsync(
        CloudWorkItem current, string to, string employeeNo, string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ValidateId(current.ItemId);
        ValidateCredentials(employeeNo, password);
        if (current.WorkspaceId != workspaceId || current.Revision < 1 ||
            !((current.Status == "open" && to == "review") ||
              (current.Status == "review" && to == "closed")))
            throw new ArgumentException("Invalid Work Item state transition.", nameof(to));
        using var response = await SendAsync("transition", new {
            itemId = current.ItemId, expectedRevision = current.Revision, from = current.Status,
            to, employeeNo, password
        }, cancellationToken).ConfigureAwait(false);
        var item = ReadItem(response.RootElement, current.ItemId);
        if (item.Revision != current.Revision + 1 || item.Status != to ||
            item.Environment != current.Environment || item.Kind != current.Kind ||
            item.ReferenceHash != current.ReferenceHash)
            throw new InvalidDataException("Work Item transition result is inconsistent.");
        return item;
    }

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
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Work Item reply is missing item.");
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
            kind is not ("VOID" or "ALLOWANCE" or "ALLOWANCE_VOID") ||
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

    private static void ValidateDraft(CloudWorkItemDraft draft)
    {
        ValidateId(draft.ItemId);
        if (draft.Environment is not ("production" or "test") ||
            draft.Kind is not ("VOID" or "ALLOWANCE_VOID") ||
            draft.ReferenceHash is null ||
            !Regex.IsMatch(draft.ReferenceHash, @"^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Work Item draft is invalid.", nameof(draft));
    }

    private static void ValidateCredentials(string employeeNo, string password)
    {
        if (employeeNo is null || !Regex.IsMatch(employeeNo, @"^\d{4}$", RegexOptions.CultureInvariant) ||
            password is null || password.Length is < 1 or > 200)
            throw new ArgumentException("Manager credentials are invalid.");
    }
}
