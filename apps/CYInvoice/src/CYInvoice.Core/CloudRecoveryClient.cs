using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

/// <summary>
/// V3 disaster-recovery transport for a NEW, unregistered device.
/// Deliberately never sends an old Device token / CYWEB core Session.
/// The API is not enabled until CYInvoice's dedicated Cloud recovery routes
/// and the matching CYWEB private issuer have passed integrated acceptance.
/// </summary>
public sealed record CloudRecoveryPreview(
    string WorkspaceId,
    string WorkspaceDisplayName,
    string IdentityWorkspaceId,
    string ApplicationId,
    string InvitationId,
    DateTimeOffset ExpiresAt);

public sealed record CloudRecoveryClaim(
    string WorkspaceId,
    string DeviceId,
    string DeviceDisplayName,
    bool Recovered,
    string DeviceToken);

public sealed class CloudRecoveryClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;

    public CloudRecoveryClient(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps
            || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0
            || baseUri.Fragment.Length != 0)
            throw new ArgumentException("Recovery requires an HTTPS Cloud API without credentials or query.", nameof(baseUri));
        this.httpClient = httpClient;
        this.baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public async Task<CloudRecoveryPreview> PreviewAsync(
        string code,
        string employeeNo,
        string password,
        CancellationToken cancellationToken = default)
    {
        VerifyCredentials(code, employeeNo, password);
        using var document = await SendAsync("v1/device-recovery/preview",
            new { code = code.ToLowerInvariant(), employeeNo, password }, cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var workspace = RequiredObject(root, "workspace");
        var identity = RequiredObject(root, "identity");
        var invitation = RequiredObject(root, "invitation");
        var application = RequiredString(identity, "applicationId");
        if (application != "CYINVOICE")
            throw new InvalidDataException("Recovery provider is not CYInvoice.");
        var expires = RequiredString(invitation, "expiresAt");
        if (!DateTimeOffset.TryParse(expires, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Recovery invitation has expired.");
        return new CloudRecoveryPreview(
            Scope(RequiredString(workspace, "workspaceId")),
            RequiredString(workspace, "displayName"),
            Scope(RequiredString(identity, "identityWorkspaceId")),
            application,
            Invitation(RequiredString(invitation, "invitationId")),
            expiresAt);
    }

    /// <summary>
    /// The caller MUST persist pending into DPAPI-backed SettingsStore and
    /// Save() BEFORE invoking ClaimAsync. Lost responses must use the same
    /// pending DeviceToken; no automatic retry, no implicit reissue.
    /// </summary>
    public async Task<CloudRecoveryClaim> ClaimAsync(
        CloudRecoveryPreview preview,
        CYInvoice.Core.Storage.CloudPendingRecoveryState pending,
        string code,
        string employeeNo,
        string password,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(pending);
        VerifyCredentials(code, employeeNo, password);
        if (!Uri.TryCreate(pending.BaseUrl, UriKind.Absolute, out var savedEndpoint)
            || !Uri.Compare(savedEndpoint, baseUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase).Equals(0)
            || pending.WorkspaceId != preview.WorkspaceId
            || pending.IdentityWorkspaceId != preview.IdentityWorkspaceId
            || pending.InvitationId != preview.InvitationId
            || preview.ApplicationId != "CYINVOICE")
            throw new InvalidOperationException("Pending Recovery Token belongs to a different Cloud/Workspace/invitation.");
        if (!ValidToken(pending.DeviceToken))
            throw new InvalidDataException("Pending Recovery Device Token is invalid.");
        if (string.IsNullOrWhiteSpace(clientVersion) || clientVersion.Length > 64
            || clientVersion.Any(char.IsControl))
            throw new ArgumentException("Client version is invalid.", nameof(clientVersion));

        using var document = await SendAsync("v1/device-recovery/claim", new {
            code = code.ToLowerInvariant(), employeeNo, password,
            deviceDisplayName = pending.DeviceDisplayName,
            clientVersion = clientVersion.Trim(), deviceToken = pending.DeviceToken,
        }, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var workspace = RequiredObject(root, "workspace");
        var device = RequiredObject(root, "device");
        var returnedWorkspace = Scope(RequiredString(workspace, "workspaceId"));
        if (returnedWorkspace != preview.WorkspaceId)
            throw new InvalidDataException("Recovery claim returned a different Workspace.");
        var displayName = RequiredString(device, "displayName");
        if (displayName != pending.DeviceDisplayName)
            throw new InvalidDataException("Recovery claim returned a different Device name.");
        if (!root.TryGetProperty("recovered", out var recovered)
            || recovered.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Recovery claim is missing result state.");
        var deviceId = RequiredString(device, "deviceId");
        if (!deviceId.StartsWith("dev_", StringComparison.Ordinal)
            || !Guid.TryParseExact(deviceId.AsSpan(4), "D", out _))
            throw new InvalidDataException("Recovery claim returned an invalid Device ID.");
        return new CloudRecoveryClaim(returnedWorkspace, deviceId, displayName,
            recovered.GetBoolean(), pending.DeviceToken);
    }

    private async Task<JsonDocument> SendAsync(string path, object body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path)) {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");
        // No Authorization bearer: this is the only operation usable when ALL
        // original Device Tokens have been lost. CYID credentials are scoped
        // per-operation and sent only in the POST JSON over HTTPS.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(RequestTimeout);
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        JsonDocument doc;
        try {
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        } catch (JsonException) {
            throw new CloudApiException("CLOUD_INVALID_RESPONSE",
                $"Recovery API returned an invalid response ({(int)response.StatusCode}).", response.StatusCode);
        }
        if (response.IsSuccessStatusCode) return doc;
        var root = doc.RootElement;
        var error = root.TryGetProperty("error", out var errorObj) && errorObj.ValueKind == JsonValueKind.Object
            ? ReadString(errorObj, "code") : string.Empty;
        doc.Dispose();
        throw new CloudApiException(error.Length == 0 ? "RECOVERY_FAILED" : error,
            $"Recovery API rejected the request ({(int)response.StatusCode}).", response.StatusCode);
    }

    private static JsonElement RequiredObject(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw new InvalidDataException($"Recovery response missing {name}.");

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static string RequiredString(JsonElement obj, string name) {
        var value = ReadString(obj, name);
        return value.Length != 0 ? value : throw new InvalidDataException($"Recovery response missing {name}.");
    }

    private static string Scope(string value) =>
        value.Length is >= 1 and <= 100 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
            ? value : throw new InvalidDataException("Invalid Recovery workspace scope.");

    private static string Invitation(string value) =>
        value.StartsWith("inv_", StringComparison.Ordinal)
        && Guid.TryParseExact(value.AsSpan(4), "D", out _)
            ? value : throw new InvalidDataException("Invalid Recovery invitation ID.");

    private static bool ValidToken(string value) =>
        value.Length == 70 && value.StartsWith("cydev_", StringComparison.Ordinal)
        && value.AsSpan(6).ToString().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void VerifyCredentials(string code, string employeeNo, string password)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 40 ||
            !code.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            throw new ArgumentException("Recovery invitation code must be 40 hexadecimal characters.", nameof(code));
        if (employeeNo is null || employeeNo.Length != 4
            || !employeeNo.All(c => c is >= '0' and <= '9'))
            throw new ArgumentException("Employee number must be four digits.", nameof(employeeNo));
        if (password is null || password.Length is < 8 or > 16)
            throw new ArgumentException("CYID recovery confirmation is invalid.", nameof(password));
    }
}
