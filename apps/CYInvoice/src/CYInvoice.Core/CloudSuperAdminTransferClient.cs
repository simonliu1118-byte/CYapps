using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudSuperAdminTransferChallenge(
    string ChallengeId,
    string MaskedEmail,
    CloudEmployeeTransitionIdentity TargetEmployee,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAfter);

public sealed record CloudSuperAdminTransferResult(
    CloudEmployeeTransitionIdentity FormerSuperAdmin,
    CloudEmployeeTransitionIdentity NewSuperAdmin,
    string RecoveryEmail,
    DateTimeOffset CompletedAt);

public sealed class CloudSuperAdminTransferClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudSuperAdminTransferClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<CloudSuperAdminTransferChallenge> StartAsync(
        string actorEmployeeNo,
        string actorPassword,
        string targetEmployeeNo,
        CancellationToken cancellationToken = default)
    {
        ValidateEmployeeNo(actorEmployeeNo, nameof(actorEmployeeNo));
        ValidateEmployeeNo(targetEmployeeNo, nameof(targetEmployeeNo));
        ValidatePassword(actorPassword);
        using var document = await SendAsync(
            "v1/employees/super-admin-transfer/challenge",
            new
            {
                actorEmployeeNo = actorEmployeeNo.Trim(),
                actorPassword,
                targetEmployeeNo = targetEmployeeNo.Trim(),
            },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud SUPER_ADMIN transfer challenge is missing.");
        if (!challenge.TryGetProperty("targetEmployee", out var target) || target.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud SUPER_ADMIN transfer target Employee is missing.");
        return new CloudSuperAdminTransferChallenge(
            ReadRequiredString(challenge, "challengeId"),
            ReadRequiredString(challenge, "maskedEmail"),
            ReadIdentity(target),
            ReadRequiredDateTimeOffset(challenge, "expiresAt"),
            ReadRequiredDateTimeOffset(challenge, "resendAfter"));
    }

    public async Task<CloudSuperAdminTransferResult> ConfirmAsync(
        string actorEmployeeNo,
        string actorPassword,
        string targetEmployeeNo,
        string challengeId,
        string otp,
        CancellationToken cancellationToken = default)
    {
        ValidateEmployeeNo(actorEmployeeNo, nameof(actorEmployeeNo));
        ValidateEmployeeNo(targetEmployeeNo, nameof(targetEmployeeNo));
        ValidatePassword(actorPassword);
        if (string.IsNullOrWhiteSpace(challengeId) || !challengeId.Trim().StartsWith("otp_", StringComparison.Ordinal))
            throw new ArgumentException("Challenge ID is invalid.", nameof(challengeId));
        otp = otp?.Trim() ?? string.Empty;
        if (otp.Length != 6 || !otp.All(char.IsAsciiDigit))
            throw new ArgumentException("OTP must be six digits.", nameof(otp));

        using var document = await SendAsync(
            "v1/employees/super-admin-transfer/confirm",
            new
            {
                actorEmployeeNo = actorEmployeeNo.Trim(),
                actorPassword,
                targetEmployeeNo = targetEmployeeNo.Trim(),
                challengeId = challengeId.Trim(),
                otp,
            },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("transfer", out var transfer) || transfer.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud SUPER_ADMIN transfer response is missing.");
        if (!transfer.TryGetProperty("formerSuperAdmin", out var former) || former.ValueKind != JsonValueKind.Object
            || !transfer.TryGetProperty("newSuperAdmin", out var successor) || successor.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud SUPER_ADMIN transfer Employee state is missing.");
        return new CloudSuperAdminTransferResult(
            ReadIdentity(former),
            ReadIdentity(successor),
            ReadRequiredString(transfer, "recoveryEmail"),
            ReadRequiredDateTimeOffset(transfer, "completedAt"));
    }

    private async Task<JsonDocument> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(body);

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

    private static void ValidateEmployeeNo(string value, string argumentName)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length != 4 || !value.All(char.IsAsciiDigit))
            throw new ArgumentException("Employee No must be four digits.", argumentName);
    }

    private static void ValidatePassword(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 200)
            throw new ArgumentException("Actor password is required.", nameof(value));
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
        return value.AsSpan(6).ToString().All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static Uri NormalizeBaseUri(Uri value)
    {
        var text = value.AbsoluteUri;
        if (!text.EndsWith("/", StringComparison.Ordinal)) text += "/";
        return new Uri(text, UriKind.Absolute);
    }
}
