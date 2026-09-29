using System.Net.Http.Headers;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudDeviceSelfStatus(
    string DeviceId,
    string WorkspaceId,
    string Status,
    DateTimeOffset? RevokedAt);

public sealed class CloudDeviceSelfStatusClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudDeviceSelfStatusClient(HttpClient httpClient, Uri baseUri, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException(
                "Cloud API URL must be an absolute HTTPS URL without credentials, query, or fragment.",
                nameof(baseUri));
        if (!ValidDeviceToken(deviceToken))
            throw new ArgumentException("Cloud device token is invalid.", nameof(deviceToken));

        this.httpClient = httpClient;
        this.baseUri = NormalizeBaseUri(baseUri);
        this.deviceToken = deviceToken.Trim().ToLowerInvariant();
    }

    public async Task<CloudDeviceSelfStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "v1/devices/self-status"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = ReadErrorCode(document.RootElement);
            var message = ReadErrorMessage(document.RootElement);
            throw new CloudApiException(
                code.Length == 0 ? "CLOUD_API_ERROR" : code,
                message.Length == 0 ? $"Cloud API returned {(int)response.StatusCode}." : message,
                response.StatusCode);
        }

        var root = document.RootElement;
        var deviceId = ReadRequiredString(root, "deviceId");
        var workspaceId = ReadRequiredString(root, "workspaceId");
        var status = ReadRequiredString(root, "status");
        if (status is not "active" and not "revoked")
            throw new InvalidDataException("Cloud Device self status is invalid.");
        var revokedAt = ReadOptionalDateTimeOffset(root, "revokedAt");
        if (status == "revoked" && revokedAt is null)
            throw new InvalidDataException("Revoked Device self status is missing revokedAt.");
        if (status == "active" && revokedAt is not null)
            throw new InvalidDataException("Active Device self status unexpectedly contains revokedAt.");

        return new CloudDeviceSelfStatus(deviceId, workspaceId, status, revokedAt);
    }

    private static string ReadErrorCode(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? ReadOptionalString(error, "code") : string.Empty;

    private static string ReadErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? ReadOptionalString(error, "message") : string.Empty;

    private static string ReadRequiredString(JsonElement element, string name)
    {
        var value = ReadOptionalString(element, name);
        if (value.Length == 0) throw new InvalidDataException($"Cloud response field '{name}' is missing.");
        return value;
    }

    private static string ReadOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static DateTimeOffset? ReadOptionalDateTimeOffset(JsonElement element, string name)
    {
        var value = ReadOptionalString(element, name);
        if (value.Length == 0) return null;
        if (!DateTimeOffset.TryParse(value, out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is invalid.");
        return result;
    }

    private static bool ValidDeviceToken(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length == 70
            && value.StartsWith("cydev_", StringComparison.Ordinal)
            && value.AsSpan(6).ToString().All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static Uri NormalizeBaseUri(Uri value)
    {
        var text = value.AbsoluteUri;
        if (!text.EndsWith("/", StringComparison.Ordinal)) text += "/";
        return new Uri(text, UriKind.Absolute);
    }
}
