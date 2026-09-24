using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudHealthResult(
    bool Reachable,
    bool StorageAvailable,
    string ServiceName,
    string CloudVersion,
    string ApiVersion,
    string SchemaVersion,
    string Environment,
    long RoundTripMilliseconds,
    string ErrorCode);

public sealed record CloudOnboardingStatus(bool WorkspaceInitialized, string State);

public sealed record CloudEmailChallenge(
    string ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAfter);

public sealed record CloudDeviceIdentity(
    string WorkspaceId,
    string DeviceId,
    string DeviceDisplayName,
    string DeviceToken);

public sealed record CloudBootstrapAttempt(string DeviceToken)
{
    public static CloudBootstrapAttempt Create() => new(
        $"cydev_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}");
}

public sealed record CloudDeviceJoinAttempt(string DeviceToken)
{
    public static CloudDeviceJoinAttempt Create() => new(
        $"cydev_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}");
}

public sealed record CloudPairingTicket(string Code, DateTimeOffset ExpiresAt);
public sealed record CloudWorkspacePreview(string WorkspaceId, string DisplayName);
public sealed record CloudDirectJoinChallenge(CloudWorkspacePreview Workspace, CloudEmailChallenge Challenge);

public sealed class CloudApiException(string code, string message, HttpStatusCode statusCode, int? retryAfterSeconds = null)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
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
        var timer = Stopwatch.StartNew();
        try
        {
            using var document = await SendAsync(
                HttpMethod.Get,
                "v1/health",
                null,
                false,
                null,
                cancellationToken,
                returnErrorResponse: true);
            timer.Stop();
            var root = document.RootElement;
            var storageAvailable =
                (root.TryGetProperty("storage", out var storage) && storage.GetString() == "ok")
                || (root.TryGetProperty("database", out var legacyDatabase) && legacyDatabase.GetString() == "ok");

            return new CloudHealthResult(
                true,
                storageAvailable,
                ReadString(root, "service"),
                ReadString(root, "cloudVersion"),
                ReadString(root, "apiVersion"),
                ReadString(root, "schemaVersion"),
                ReadString(root, "environment"),
                timer.ElapsedMilliseconds,
                ReadErrorCode(root));
        }
        catch (CloudApiException error)
        {
            timer.Stop();
            return new CloudHealthResult(
                true, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                timer.ElapsedMilliseconds, error.Code);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            timer.Stop();
            return new CloudHealthResult(
                true, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                timer.ElapsedMilliseconds, "INVALID_RESPONSE");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            timer.Stop();
            return new CloudHealthResult(
                false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                timer.ElapsedMilliseconds, "CLOUD_UNREACHABLE");
        }
    }

    public async Task<CloudOnboardingStatus> GetOnboardingStatusAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/onboarding/status", null, false, null, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("onboarding", out var onboarding) || onboarding.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud onboarding response is missing onboarding data.");

        var state = ReadRequiredString(onboarding, "state");
        if (state is not "uninitialized" and not "initialized")
            throw new InvalidDataException("Cloud onboarding state is invalid.");

        var initialized = onboarding.TryGetProperty("workspaceInitialized", out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
        if (initialized != (state == "initialized"))
            throw new InvalidDataException("Cloud onboarding state is inconsistent.");

        return new CloudOnboardingStatus(initialized, state);
    }

    public async Task<CloudEmailChallenge> StartBootstrapEmailChallengeAsync(
        string bootstrapKey,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bootstrapKey))
            throw new ArgumentException("Bootstrap key is required.", nameof(bootstrapKey));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/onboarding/bootstrap-email",
            new { email = email.Trim() },
            false,
            bootstrapKey.Trim(),
            cancellationToken);

        return ReadEmailChallenge(document.RootElement);
    }

    public async Task<CloudDeviceIdentity> BootstrapAsync(
        string bootstrapKey,
        string workspaceDisplayName,
        string deviceDisplayName,
        string clientVersion,
        string emailChallengeId,
        string emailOtp,
        CloudBootstrapAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bootstrapKey))
            throw new ArgumentException("Bootstrap key is required.", nameof(bootstrapKey));
        if (string.IsNullOrWhiteSpace(emailChallengeId))
            throw new ArgumentException("Email challenge ID is required.", nameof(emailChallengeId));
        if (emailOtp is null || emailOtp.Length != 6 || !emailOtp.All(char.IsDigit))
            throw new ArgumentException("Email OTP must contain six digits.", nameof(emailOtp));
        ArgumentNullException.ThrowIfNull(attempt);
        if (!ValidDeviceToken(attempt.DeviceToken))
            throw new ArgumentException("Bootstrap device token is invalid.", nameof(attempt));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/bootstrap",
            new
            {
                workspaceDisplayName,
                deviceDisplayName,
                clientVersion,
                emailChallengeId = emailChallengeId.Trim(),
                emailOtp,
                deviceToken = attempt.DeviceToken
            },
            false,
            bootstrapKey.Trim(),
            cancellationToken);

        return ReadDeviceIdentity(document.RootElement, requireToken: false) with { DeviceToken = attempt.DeviceToken };
    }

    public async Task<CloudDeviceIdentity> GetCurrentDeviceAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/device", null, true, null, cancellationToken);
        return ReadDeviceIdentity(document.RootElement, requireToken: false) with { DeviceToken = deviceToken };
    }

    public async Task<CloudEmailChallenge> StartPairingAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/device-pairings/authorization-email",
            new { },
            true,
            null,
            cancellationToken);
        return ReadEmailChallenge(document.RootElement);
    }

    public async Task<CloudPairingTicket> CreatePairingAsync(
        string emailChallengeId,
        string emailOtp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailChallengeId))
            throw new ArgumentException("Email challenge ID is required.", nameof(emailChallengeId));
        if (emailOtp is null || emailOtp.Length != 6 || !emailOtp.All(char.IsDigit))
            throw new ArgumentException("Email OTP must contain six digits.", nameof(emailOtp));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/device-pairings",
            new { emailChallengeId = emailChallengeId.Trim(), emailOtp },
            true,
            null,
            cancellationToken);
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
        CloudDeviceJoinAttempt attempt,
        CancellationToken cancellationToken = default,
        bool directJoin = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Pairing code is required.", nameof(code));
        ArgumentNullException.ThrowIfNull(attempt);
        if (!ValidDeviceToken(attempt.DeviceToken))
            throw new ArgumentException("Device join token is invalid.", nameof(attempt));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/device-pairings/claim",
            new
            {
                code = code.Trim(),
                deviceDisplayName,
                clientVersion,
                deviceToken = attempt.DeviceToken,
                directJoin
            },
            false,
            null,
            cancellationToken);

        return ReadDeviceIdentity(document.RootElement, requireToken: false) with { DeviceToken = attempt.DeviceToken };
    }

    public async Task<CloudWorkspacePreview> PreviewPairingAsync(string code, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "v1/device-pairings/preview",
            new { code }, false, null, cancellationToken);
        return ReadWorkspacePreview(document.RootElement);
    }

    public async Task<CloudDirectJoinChallenge> StartDirectJoinAsync(
        string workspaceId, string employeeNo, string password, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "v1/direct-join/authorize",
            new { workspaceId, employeeNo, password }, false, null, cancellationToken);
        var root = document.RootElement;
        var workspace = ReadWorkspacePreview(root);
        var challenge = root.GetProperty("challenge");
        return new CloudDirectJoinChallenge(workspace, new CloudEmailChallenge(
            ReadRequiredString(challenge, "challengeId"), ReadRequiredString(challenge, "maskedEmail"),
            DateTimeOffset.Parse(ReadRequiredString(challenge, "expiresAt")),
            DateTimeOffset.Parse(ReadRequiredString(challenge, "resendAfter"))));
    }

    public async Task<CloudDeviceIdentity> ClaimDirectJoinAsync(
        string workspaceId, string employeeNo, string challengeId, string otp,
        string deviceDisplayName, string clientVersion, CloudDeviceJoinAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (!ValidDeviceToken(attempt.DeviceToken)) throw new ArgumentException("Device token is invalid.", nameof(attempt));
        using var document = await SendAsync(HttpMethod.Post, "v1/direct-join/claim",
            new { workspaceId, employeeNo, emailChallengeId = challengeId, emailOtp = otp,
                  deviceDisplayName, clientVersion, deviceToken = attempt.DeviceToken },
            false, null, cancellationToken);
        return ReadDeviceIdentity(document.RootElement, requireToken: false) with { DeviceToken = attempt.DeviceToken };
    }

    private static CloudWorkspacePreview ReadWorkspacePreview(JsonElement root)
    {
        var workspace = root.GetProperty("workspace");
        return new CloudWorkspacePreview(ReadRequiredString(workspace, "workspaceId"),
            ReadRequiredString(workspace, "displayName"));
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        bool authenticateDevice,
        string? bootstrapKey,
        CancellationToken cancellationToken,
        bool returnErrorResponse = false)
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
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        catch (JsonException)
        {
            throw new CloudApiException(
                "CLOUD_INVALID_RESPONSE",
                $"Cloud API returned an invalid response ({(int)response.StatusCode}).",
                response.StatusCode);
        }

        if (response.IsSuccessStatusCode || returnErrorResponse)
            return document;

        var errorCode = ReadErrorCode(document.RootElement);
        var errorMessage = ReadErrorMessage(document.RootElement);
        document.Dispose();
        throw new CloudApiException(
            errorCode.Length == 0 ? "CLOUD_API_ERROR" : errorCode,
            errorMessage.Length == 0 ? $"Cloud API returned {(int)response.StatusCode}." : errorMessage,
            response.StatusCode);
    }

    private static CloudEmailChallenge ReadEmailChallenge(JsonElement root)
    {
        if (!root.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud email challenge response is missing challenge data.");

        var challengeId = ReadRequiredString(challenge, "challengeId");
        var maskedEmail = ReadRequiredString(challenge, "maskedEmail");
        var expiresAt = ReadRequiredDateTimeOffset(challenge, "expiresAt");
        var resendAfter = ReadRequiredDateTimeOffset(challenge, "resendAfter");
        if (resendAfter > expiresAt)
            throw new InvalidDataException("Cloud email challenge timing is inconsistent.");

        return new CloudEmailChallenge(challengeId, maskedEmail, expiresAt, resendAfter);
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

    private static DateTimeOffset ReadRequiredDateTimeOffset(JsonElement element, string name)
    {
        var value = ReadRequiredString(element, name);
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new InvalidDataException($"Cloud response field '{name}' is not a valid timestamp.");
        return parsed;
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

    private static bool ValidDeviceToken(string value)
    {
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
