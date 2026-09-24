using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeTransitionActionResult(
    string State,
    CloudEmployeeTransitionIdentity Employee);

public sealed class CloudEmployeeTransitionActionClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeTransitionActionClient(HttpClient httpClient, Uri baseUri, string deviceToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("Cloud API URL must be an absolute HTTPS URL without credentials, query, or fragment.", nameof(baseUri));
        if (!ValidDeviceToken(deviceToken))
            throw new ArgumentException("Cloud device token is invalid.", nameof(deviceToken));

        this.httpClient = httpClient;
        this.baseUri = NormalizeBaseUri(baseUri);
        this.deviceToken = deviceToken.Trim();
    }

    public async Task<CloudEmailChallenge> StartEmailVerificationAsync(
        string employeeNo,
        string snapshotHash,
        CancellationToken cancellationToken = default)
    {
        ValidateEmployeeNo(employeeNo);
        ValidateSnapshotHash(snapshotHash);
        using var document = await SendAsync(
            "v1/employee-transition/email-challenge",
            new { employeeNo, snapshotHash },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud transition response is missing Email challenge data.");
        return new CloudEmailChallenge(
            ReadRequiredString(challenge, "challengeId"),
            ReadRequiredString(challenge, "maskedEmail"),
            ReadRequiredDateTimeOffset(challenge, "expiresAt"),
            ReadRequiredDateTimeOffset(challenge, "resendAfter"));
    }

    public Task<CloudEmployeeTransitionActionResult> CompleteBootstrapOwnerAsync(
        string employeeNo,
        string snapshotHash,
        string credentialVerifier,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(
            "v1/employee-transition/bootstrap-owner/complete",
            new { employeeNo, snapshotHash, credentialVerifier },
            cancellationToken);

    public Task<CloudEmployeeTransitionActionResult> VerifyEmailAsync(
        string employeeNo,
        string snapshotHash,
        string challengeId,
        string otp,
        string? credentialVerifier,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(
            "v1/employee-transition/email-verify",
            new { employeeNo, snapshotHash, challengeId, otp, credentialVerifier },
            cancellationToken);

    public Task<CloudEmployeeTransitionActionResult> CompleteExistingCredentialAsync(
        string employeeNo,
        string snapshotHash,
        string credentialVerifier,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(
            "v1/employee-transition/credential/complete",
            new { employeeNo, snapshotHash, credentialVerifier },
            cancellationToken);

    private async Task<CloudEmployeeTransitionActionResult> CompleteAsync(
        string relativePath,
        object payload,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(relativePath, payload, cancellationToken);
        if (!document.RootElement.TryGetProperty("transitionItem", out var item) || item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud transition response is missing transition item data.");
        if (!item.TryGetProperty("employee", out var employee) || employee.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud transition response is missing Employee data.");
        return new CloudEmployeeTransitionActionResult(
            ReadRequiredString(item, "state"),
            ReadIdentity(employee));
    }

    private async Task<JsonDocument> SendAsync(
        string relativePath,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(payload);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultRequestTimeout);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        if (response.IsSuccessStatusCode) return document;

        var code = ReadErrorCode(document.RootElement);
        var message = ReadErrorMessage(document.RootElement);
        document.Dispose();
        throw new CloudApiException(
            code.Length == 0 ? "CLOUD_API_ERROR" : code,
            message.Length == 0 ? $"Cloud API returned {(int)response.StatusCode}." : message,
            response.StatusCode);
    }

    private static CloudEmployeeTransitionIdentity ReadIdentity(JsonElement value) => new(
        ReadRequiredString(value, "employeeId"),
        ReadRequiredString(value, "employeeNo"),
        ReadRequiredString(value, "name"),
        ReadRequiredString(value, "email"),
        ReadRequiredString(value, "role"),
        ReadRequiredBoolean(value, "enabled"),
        ReadRequiredBoolean(value, "emailVerified"),
        ReadRequiredBoolean(value, "credentialReady"),
        ReadRequiredInt32(value, "credentialVersion"),
        ReadRequiredInt32(value, "revision"));

    private static void ValidateEmployeeNo(string value)
    {
        if (value is null || value.Length != 4 || value.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("Employee No must be four digits.", nameof(value));
    }

    private static void ValidateSnapshotHash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Transition snapshot hash is invalid.", nameof(value));
    }

    private static string ReadErrorCode(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? ReadString(error, "code") : string.Empty;

    private static string ReadErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? ReadString(error, "message") : string.Empty;

    private static string ReadRequiredString(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        if (value.Length == 0) throw new InvalidDataException($"Cloud response field '{name}' is missing.");
        return value;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadRequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return value.GetBoolean();
    }

    private static int ReadRequiredInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return result;
    }

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement element, string name)
    {
        var raw = ReadRequiredString(element, name);
        if (!DateTimeOffset.TryParse(raw, out var value))
            throw new InvalidDataException($"Cloud response field '{name}' is invalid.");
        return value;
    }

    private static bool ValidDeviceToken(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("cydev_", StringComparison.Ordinal) || value.Length != 70) return false;
        return value.AsSpan(6).ToString().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static Uri NormalizeBaseUri(Uri value)
    {
        var text = value.AbsoluteUri;
        if (!text.EndsWith("/", StringComparison.Ordinal)) text += "/";
        return new Uri(text, UriKind.Absolute);
    }
}
