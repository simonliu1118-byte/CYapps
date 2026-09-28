using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudManagedDevice(
    string DeviceId,
    string DisplayName,
    string Status,
    string ClientVersion,
    DateTimeOffset? PairedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    bool Current);

public sealed record CloudManagedDeviceList(
    string CurrentDeviceId,
    int ActiveDeviceCount,
    IReadOnlyList<CloudManagedDevice> Devices);

public sealed record CloudDeviceRevokeResult(
    CloudManagedDevice Device,
    bool AlreadyRevoked);

public sealed class CloudDeviceLifecycleClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudDeviceLifecycleClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<CloudManagedDeviceList> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
                HttpMethod.Get,
                "v1/devices",
                body: null,
                cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var currentDeviceId = ReadRequiredString(root, "currentDeviceId");
        var activeDeviceCount = ReadRequiredInt32(root, "activeDeviceCount");
        if (!root.TryGetProperty("devices", out var devicesElement)
            || devicesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cloud Device response is missing devices.");

        var devices = new List<CloudManagedDevice>();
        foreach (var device in devicesElement.EnumerateArray())
            devices.Add(ReadDevice(device));

        var currentDevices = devices.Where(device => device.Current).ToArray();
        if (currentDevices.Length != 1
            || !string.Equals(currentDevices[0].DeviceId, currentDeviceId, StringComparison.Ordinal)
            || currentDevices[0].Status != "active")
            throw new InvalidDataException("Cloud Device response must identify exactly one current active Device.");
        if (activeDeviceCount < 1
            || activeDeviceCount != devices.Count(device => device.Status == "active"))
            throw new InvalidDataException("Cloud Device active count is inconsistent.");

        return new CloudManagedDeviceList(currentDeviceId, activeDeviceCount, devices);
    }

    public async Task<CloudDeviceRevokeResult> RevokeAsync(
        string targetDeviceId,
        string employeeNo,
        string password,
        CancellationToken cancellationToken = default)
    {
        targetDeviceId = NormalizeDeviceId(targetDeviceId);
        employeeNo = NormalizeEmployeeNo(employeeNo);
        if (password is null || password.Length is < 1 or > 200)
            throw new ArgumentException("Super administrator password is invalid.", nameof(password));

        using var document = await SendAsync(
                HttpMethod.Post,
                "v1/devices/revoke",
                new { targetDeviceId, employeeNo, password },
                cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (!root.TryGetProperty("device", out var deviceElement)
            || deviceElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Device revoke response is missing device.");
        if (!root.TryGetProperty("alreadyRevoked", out var alreadyElement)
            || alreadyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Cloud Device revoke response is missing alreadyRevoked.");
        var device = ReadDevice(deviceElement);
        if (!string.Equals(device.DeviceId, targetDeviceId, StringComparison.Ordinal)
            || device.Status != "revoked"
            || device.RevokedAt is null)
            throw new InvalidDataException("Cloud Device revoke response is inconsistent.");
        return new CloudDeviceRevokeResult(device, alreadyElement.GetBoolean());
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");
        if (body is not null) request.Content = JsonContent.Create(body);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return document;

        var code = ReadErrorCode(document.RootElement);
        var message = ReadErrorMessage(document.RootElement);
        var retryAfter = ReadOptionalInt32(document.RootElement, "retryAfterSeconds");
        document.Dispose();
        throw new CloudApiException(
            code.Length == 0 ? "CLOUD_API_ERROR" : code,
            message.Length == 0 ? $"Cloud API returned {(int)response.StatusCode}." : message,
            response.StatusCode,
            retryAfter);
    }

    private static CloudManagedDevice ReadDevice(JsonElement element)
    {
        var status = ReadRequiredString(element, "status");
        if (status is not "active" and not "revoked")
            throw new InvalidDataException("Cloud Device status is invalid.");
        return new CloudManagedDevice(
            NormalizeDeviceId(ReadRequiredString(element, "deviceId")),
            ReadRequiredString(element, "displayName"),
            status,
            ReadOptionalString(element, "clientVersion"),
            ReadOptionalDateTimeOffset(element, "pairedAt"),
            ReadOptionalDateTimeOffset(element, "lastSeenAt"),
            ReadRequiredDateTimeOffset(element, "createdAt"),
            ReadOptionalDateTimeOffset(element, "revokedAt"),
            ReadRequiredBoolean(element, "current"));
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

    private static int ReadRequiredInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return result;
    }

    private static int? ReadOptionalInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var result)
            ? result
            : null;

    private static bool ReadRequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return value.GetBoolean();
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement element, string name)
    {
        var value = ReadOptionalString(element, name);
        if (value.Length == 0 || !DateTimeOffset.TryParse(value, out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return result;
    }

    private static DateTimeOffset? ReadOptionalDateTimeOffset(JsonElement element, string name)
    {
        var value = ReadOptionalString(element, name);
        if (value.Length == 0) return null;
        if (!DateTimeOffset.TryParse(value, out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is invalid.");
        return result;
    }

    private static string NormalizeDeviceId(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!value.StartsWith("dev_", StringComparison.Ordinal)
            || !Guid.TryParseExact(value[4..], "D", out _))
            throw new ArgumentException("Cloud Device ID is invalid.", nameof(value));
        return value;
    }

    private static string NormalizeEmployeeNo(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length != 4 || !value.All(char.IsDigit))
            throw new ArgumentException("Employee number must be four digits.", nameof(value));
        return value;
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
