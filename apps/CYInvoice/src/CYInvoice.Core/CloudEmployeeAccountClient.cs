using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeUpdateProposal(
    string TargetEmployeeNo,
    string Name,
    string Email,
    string Role);

public sealed record CloudEmployeeUpdateChallenge(
    string ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAfter);

public sealed record CloudEmployeeUpdateStart(
    bool VerificationRequired,
    CloudEmployeeUpdateChallenge? Challenge,
    CloudEmployeeTransitionIdentity? Employee);

public sealed record CloudEmployeePasswordRecoveryChallenge(
    string ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAfter);

public sealed class CloudEmployeeAccountClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeAccountClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<CloudEmployeeUpdateStart> StartUpdateAsync(
        string actorEmployeeNo,
        string actorPassword,
        CloudEmployeeUpdateProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        ValidateUpdateProposal(proposal);
        using var document = await SendAsync(
            "v1/employees/update/challenge",
            UpdatePayload(actorEmployeeNo, actorPassword, proposal),
            cancellationToken);
        var root = document.RootElement;
        var verificationRequired = ReadRequiredBoolean(root, "verificationRequired");
        if (!verificationRequired)
        {
            if (!root.TryGetProperty("employee", out var employee) || employee.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Updated Cloud Employee is missing from the response.");
            return new CloudEmployeeUpdateStart(false, null, ReadIdentity(employee));
        }

        if (!root.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Employee update challenge is missing.");
        return new CloudEmployeeUpdateStart(
            true,
            new CloudEmployeeUpdateChallenge(
                ReadRequiredString(challenge, "challengeId"),
                ReadRequiredString(challenge, "maskedEmail"),
                ReadRequiredDateTimeOffset(challenge, "expiresAt"),
                ReadRequiredDateTimeOffset(challenge, "resendAfter")),
            null);
    }

    public async Task<CloudEmployeeTransitionIdentity> ConfirmUpdateAsync(
        string actorEmployeeNo,
        string actorPassword,
        CloudEmployeeUpdateProposal proposal,
        string challengeId,
        string otp,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        ValidateUpdateProposal(proposal);
        if (string.IsNullOrWhiteSpace(challengeId) || !challengeId.Trim().StartsWith("otp_", StringComparison.Ordinal))
            throw new ArgumentException("Challenge ID is invalid.", nameof(challengeId));
        otp = otp?.Trim() ?? string.Empty;
        if (otp.Length != 6 || !otp.All(char.IsAsciiDigit))
            throw new ArgumentException("OTP must be six digits.", nameof(otp));
        var payload = new
        {
            actorEmployeeNo = actorEmployeeNo.Trim(),
            actorPassword,
            targetEmployeeNo = proposal.TargetEmployeeNo.Trim(),
            name = proposal.Name.Trim(),
            email = proposal.Email.Trim(),
            role = proposal.Role.Trim(),
            challengeId = challengeId.Trim(),
            otp,
        };
        using var document = await SendAsync("v1/employees/update/confirm", payload, cancellationToken);
        if (!document.RootElement.TryGetProperty("employee", out var employee) || employee.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Updated Cloud Employee is missing from the response.");
        return ReadIdentity(employee);
    }

    public async Task<CloudEmployeeTransitionIdentity> SetEnabledAsync(
        string actorEmployeeNo,
        string actorPassword,
        string targetEmployeeNo,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        ValidateEmployeeNo(targetEmployeeNo, nameof(targetEmployeeNo));
        using var document = await SendAsync(
            "v1/employees/enabled",
            new
            {
                actorEmployeeNo = actorEmployeeNo.Trim(),
                actorPassword,
                targetEmployeeNo = targetEmployeeNo.Trim(),
                enabled,
            },
            cancellationToken);
        return ReadEmployeeResponse(document);
    }

    public async Task<CloudEmployeeTransitionIdentity> SetPasswordAsync(
        string actorEmployeeNo,
        string actorPassword,
        string targetEmployeeNo,
        string credentialVerifier,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        ValidateEmployeeNo(targetEmployeeNo, nameof(targetEmployeeNo));
        if (string.IsNullOrWhiteSpace(credentialVerifier) || !credentialVerifier.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal))
            throw new ArgumentException("Cloud Employee credential verifier is invalid.", nameof(credentialVerifier));
        using var document = await SendAsync(
            "v1/employees/password",
            new
            {
                actorEmployeeNo = actorEmployeeNo.Trim(),
                actorPassword,
                targetEmployeeNo = targetEmployeeNo.Trim(),
                credentialVerifier = credentialVerifier.Trim(),
            },
            cancellationToken);
        return ReadEmployeeResponse(document);
    }

    public async Task<CloudEmployeePasswordRecoveryChallenge> StartPasswordRecoveryAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        ValidateEmployeeNo(employeeNo, nameof(employeeNo));
        using var document = await SendAsync(
            "v1/employees/password-recovery/challenge",
            new { employeeNo = employeeNo.Trim() }, cancellationToken);
        if (!document.RootElement.TryGetProperty("challenge", out var challenge) ||
            challenge.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud password recovery challenge is missing.");
        return new CloudEmployeePasswordRecoveryChallenge(
            ReadRequiredString(challenge, "challengeId"),
            ReadRequiredString(challenge, "maskedEmail"),
            ReadRequiredDateTimeOffset(challenge, "expiresAt"),
            ReadRequiredDateTimeOffset(challenge, "resendAfter"));
    }

    public async Task ConfirmPasswordRecoveryAsync(
        string employeeNo,
        string challengeId,
        string otp,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidateEmployeeNo(employeeNo, nameof(employeeNo));
        if (string.IsNullOrWhiteSpace(challengeId) ||
            !challengeId.Trim().StartsWith("otp_", StringComparison.Ordinal))
            throw new ArgumentException("Challenge ID is invalid.", nameof(challengeId));
        otp = otp?.Trim() ?? string.Empty;
        if (otp.Length != 6 || !otp.All(char.IsAsciiDigit))
            throw new ArgumentException("OTP must be six digits.", nameof(otp));
        var credentialVerifier = CloudEmployeeCredentialVerifier.Create(newPassword);
        using var document = await SendAsync(
            "v1/employees/password-recovery/confirm",
            new { employeeNo = employeeNo.Trim(), challengeId = challengeId.Trim(), otp, credentialVerifier },
            cancellationToken);
        if (!ReadRequiredBoolean(document.RootElement, "passwordReset"))
            throw new InvalidDataException("Cloud password recovery confirmation is incomplete.");
    }

    private static object UpdatePayload(string actorEmployeeNo, string actorPassword, CloudEmployeeUpdateProposal proposal) => new
    {
        actorEmployeeNo = actorEmployeeNo.Trim(),
        actorPassword,
        targetEmployeeNo = proposal.TargetEmployeeNo.Trim(),
        name = proposal.Name.Trim(),
        email = proposal.Email.Trim(),
        role = proposal.Role.Trim(),
    };

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

    private static CloudEmployeeTransitionIdentity ReadEmployeeResponse(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("employee", out var employee) || employee.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Employee is missing from the response.");
        return ReadIdentity(employee);
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

    private static void ValidateActor(string employeeNo, string password)
    {
        ValidateEmployeeNo(employeeNo, nameof(employeeNo));
        if (string.IsNullOrEmpty(password) || password.Length > 200)
            throw new ArgumentException("Actor password is required.", nameof(password));
    }

    private static void ValidateUpdateProposal(CloudEmployeeUpdateProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ValidateEmployeeNo(proposal.TargetEmployeeNo, nameof(proposal));
        if (string.IsNullOrWhiteSpace(proposal.Name) || string.IsNullOrWhiteSpace(proposal.Email))
            throw new ArgumentException("Employee name and Email are required.", nameof(proposal));
        if (proposal.Role is not ("SUPER_ADMIN" or "ADMIN" or "EMPLOYEE"))
            throw new ArgumentException("Cloud Employee role is invalid.", nameof(proposal));
    }

    private static void ValidateEmployeeNo(string employeeNo, string parameterName)
    {
        employeeNo = employeeNo?.Trim() ?? string.Empty;
        if (employeeNo.Length != 4 || !employeeNo.All(char.IsAsciiDigit))
            throw new ArgumentException("Employee No must be four digits.", parameterName);
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
