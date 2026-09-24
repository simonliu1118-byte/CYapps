using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeTransitionConflict(
    string TransitionItemId,
    string SourceDeviceId,
    string SourceDeviceName,
    string LocalEmployeeNo,
    string LocalName,
    string LocalEmail,
    string LocalRole,
    bool LocalEnabled,
    string MatchKind,
    CloudEmployeeTransitionIdentity? EmployeeNoMatch,
    CloudEmployeeTransitionIdentity? EmailMatch)
{
    public IReadOnlyList<CloudEmployeeTransitionIdentity> Candidates =>
        new[] { EmployeeNoMatch, EmailMatch }
            .Where(value => value is not null)
            .Cast<CloudEmployeeTransitionIdentity>()
            .GroupBy(value => value.EmployeeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
}

public sealed class CloudEmployeeTransitionConflictClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeTransitionConflictClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/employee-transition/conflicts/count", null, cancellationToken);
        return ReadRequiredInt32(document.RootElement, "conflictCount");
    }

    public async Task<IReadOnlyList<CloudEmployeeTransitionConflict>> ListAsync(
        string actorEmployeeNo,
        string actorPassword,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/employee-transition/conflicts/list",
            new { actorEmployeeNo = actorEmployeeNo.Trim(), actorPassword },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("conflicts", out var conflicts) || conflicts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cloud conflict response is missing conflicts.");
        var result = new List<CloudEmployeeTransitionConflict>();
        foreach (var item in conflicts.EnumerateArray()) result.Add(ReadConflict(item));
        return result;
    }

    public async Task<CloudEmployeeTransitionIdentity> ResolveAsync(
        string actorEmployeeNo,
        string actorPassword,
        string transitionItemId,
        string targetEmployeeId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorEmployeeNo, actorPassword);
        if (string.IsNullOrWhiteSpace(transitionItemId) || !transitionItemId.Trim().StartsWith("eti_", StringComparison.Ordinal))
            throw new ArgumentException("Transition item ID is invalid.", nameof(transitionItemId));
        if (string.IsNullOrWhiteSpace(targetEmployeeId) || !targetEmployeeId.Trim().StartsWith("emp_", StringComparison.Ordinal))
            throw new ArgumentException("Cloud Employee ID is invalid.", nameof(targetEmployeeId));

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/employee-transition/conflicts/resolve",
            new
            {
                actorEmployeeNo = actorEmployeeNo.Trim(),
                actorPassword,
                transitionItemId = transitionItemId.Trim(),
                targetEmployeeId = targetEmployeeId.Trim(),
            },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("targetEmployee", out var employee) || employee.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud conflict resolution response is missing target Employee.");
        return ReadIdentity(employee);
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

    private static CloudEmployeeTransitionConflict ReadConflict(JsonElement item)
    {
        if (!item.TryGetProperty("local", out var local) || local.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud conflict item is missing Local Employee data.");
        return new CloudEmployeeTransitionConflict(
            ReadRequiredString(item, "transitionItemId"),
            ReadRequiredString(item, "sourceDeviceId"),
            ReadRequiredString(item, "sourceDeviceName"),
            ReadRequiredString(local, "employeeNo"),
            ReadRequiredString(local, "name"),
            ReadRequiredString(local, "email"),
            ReadRequiredString(local, "role"),
            ReadRequiredBoolean(local, "enabled"),
            ReadRequiredString(item, "matchKind"),
            ReadOptionalIdentity(item, "employeeNoMatch"),
            ReadOptionalIdentity(item, "emailMatch"));
    }

    private static CloudEmployeeTransitionIdentity? ReadOptionalIdentity(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Cloud conflict field '{property}' is invalid.");
        return ReadIdentity(value);
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
        if (employeeNo is null || employeeNo.Trim().Length != 4 || !employeeNo.Trim().All(char.IsDigit))
            throw new ArgumentException("Actor Employee No must be four digits.", nameof(employeeNo));
        if (string.IsNullOrEmpty(password) || password.Length > 200)
            throw new ArgumentException("Actor password is required.", nameof(password));
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
