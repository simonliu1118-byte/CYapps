using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudHealthResult(
    bool Reachable,
    bool DatabaseAvailable,
    string CloudVersion,
    string ApiVersion,
    string SchemaVersion,
    string ErrorCode);

public sealed record CloudDeviceIdentity(
    string WorkspaceId,
    string DeviceId,
    string DeviceDisplayName,
    string DeviceToken);

public sealed record CloudPairingTicket(string Code, DateTimeOffset ExpiresAt);

public sealed class CloudApiException(string code, string message, HttpStatusCode statusCode)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class CloudClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(4);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudClient(HttpClient httpClient, Uri baseUri, string deviceToken = "")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo))
            throw new ArgumentException("Cloud API URL must be an absolute HTTPS URL.", nameof(baseUri));
        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("Cloud API URL cannot contain query or fragment components.", nameof(baseUri));

        this.httpClient = httpClient;
        this.baseUri = NormalizeBaseUri(baseUri);
        this.deviceToken = deviceToken.Trim();
    }

    public async Task<CloudHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = await SendAsync(HttpMethod.Get, "v1/health/db", null, false, null, cancellationToken);
            var root = document.RootElement;
            return new CloudHealthResult(
                true,
                root.TryGetProperty("database", out var database) && database.GetString() == "ok",
                ReadString(root, "cloudVersion"),
                ReadString(root, "apiVersion"),
                ReadString(root, "schemaVersion"),
                ReadErrorCode(root));
        }
        catch (CloudApiException error)
        {
            return new CloudHealthResult(true, false, string.Empty, string.Empty, string.Empty, error.Code);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return new CloudHealthResult(false, false, string.Empty, string.Empty, string.Empty, "CLOUD_UNREACHABLE");
        }
    }

    public async Task<CloudDeviceIdentity> BootstrapAsync(
        string bootstrapKey,
        string workspaceDisplayName,
        string deviceDisplayName,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bootstrapKey)) throw new ArgumentException("Bootstrap key is required.", nameof(bootstrapKey));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/bootstrap",
            new
            {
                workspaceDisplayName,
                deviceDisplayName,
                clientVersion
            },
            false,
            bootstrapKey.Trim(),
            cancellationToken);

        return ReadDeviceIdentity(document.RootElement, requireToken: true);
    }

    public async Task<CloudDeviceIdentity> GetCurrentDeviceAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/device", null, true, null, cancellationToken);
        return ReadDeviceIdentity(document.RootElement, requireToken: false) with { DeviceToken = deviceToken };
    }

    public async Task<CloudPairingTicket> CreatePairingAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "v1/device-pairings", new { }, true, null, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("pairing", out var pairing))
            throw new InvalidDataException("Cloud pairing response is missing pairing data.");

        var code = ReadRequiredString(pairing, "code");
        var expiresAtText = ReadRequiredString(pairing, "expiresAt");
        if (!DateTimeOffset.TryParse(expiresAtText, out var expiresAt))
            throw new InvalidDataException("Cloud pairing expiry is invalid.");
        return new CloudPairingTicket(code, expiresAt);
    }

    public async Task<CloudDeviceIdentity> ClaimPairingAsync(
        string code,
        string deviceDisplayName,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/device-pairings/claim",
            new
            {
                code = code.Trim(),
                deviceDisplayName,
                clientVersion
            },
            false,
            null,
            cancellationToken);

        return ReadDeviceIdentity(document.RootElement, requireToken: true);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticateDevice,
        string? bootstrapKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");

        if (authenticateDevice)
        {
            if (deviceToken.Length == 0)
                throw new InvalidOperationException("Cloud device token is not configured.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        }

        if (!string.IsNullOrEmpty(bootstrapKey))
            request.Headers.TryAddWithoutValidation("X-Bootstrap-Key", bootstrapKey);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

        if (response.IsSuccessStatusCode)
            return document;

        var errorCode = ReadErrorCode(document.RootElement);
        var errorMessage = ReadErrorMessage(document.RootElement);
        document.Dispose();
        throw new CloudApiException(
            errorCode.Length == 0 ? "CLOUD_API_ERROR" : errorCode,
            errorMessage.Length == 0 ? $"Cloud API returned {(int)response.StatusCode}." : errorMessage,
            response.StatusCode);
    }

    private static CloudDeviceIdentity ReadDeviceIdentity(JsonElement root, bool requireToken)
    {
        if (!root.TryGetProperty("workspace", out var workspace) || !root.TryGetProperty("device", out var device))
            throw new InvalidDataException("Cloud response is missing workspace/device data.");

        var workspaceId = ReadRequiredString(workspace, "workspaceId");
        var deviceId = ReadRequiredString(device, "deviceId");
        var displayName = ReadRequiredString(device, "displayName");
        var token = ReadString(device, "token");
        if (requireToken && token.Length == 0)
            throw new InvalidDataException("Cloud response is missing the one-time device token.");

        return new CloudDeviceIdentity(workspaceId, deviceId, displayName, token);
    }

    private static string ReadErrorCode(JsonElement root)
    {
        return root.TryGetProperty("error", out var error) ? ReadString(error, "code") : string.Empty;
    }

    private static string ReadErrorMessage(JsonElement root)
    {
        return root.TryGetProperty("error", out var error) ? ReadString(error, "message") : string.Empty;
    }

    private static string ReadRequiredString(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        if (value.Length == 0) throw new InvalidDataException($"Cloud response field '{name}' is missing.");
        return value;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static Uri NormalizeBaseUri(Uri value)
    {
        var text = value.AbsoluteUri;
        if (!text.EndsWith('/', StringComparison.Ordinal)) text += "/";
        return new Uri(text, UriKind.Absolute);
    }
}
