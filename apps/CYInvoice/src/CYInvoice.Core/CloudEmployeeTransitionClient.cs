using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeTransitionIdentity(
    string EmployeeId,
    string EmployeeNo,
    string Name,
    string Email,
    string Role,
    bool Enabled,
    bool EmailVerified,
    bool CredentialReady,
    int CredentialVersion,
    int Revision);

public sealed record CloudEmployeeTransitionItem(
    string LocalEmployeeNo,
    string LocalName,
    string LocalEmail,
    string LocalRole,
    bool LocalEnabled,
    string SuggestedCloudRole,
    string State,
    string MatchKind,
    CloudEmployeeTransitionIdentity? MatchedEmployee,
    CloudEmployeeTransitionIdentity? EmployeeNoMatch,
    CloudEmployeeTransitionIdentity? EmailMatch);

public sealed record CloudEmployeeTransitionInspection(
    string DeviceId,
    string AuthorityState,
    string SnapshotHash,
    int LocalEmployeeCount,
    int UnresolvedCount,
    int ConflictCount,
    bool ReadyForCutover,
    IReadOnlyList<CloudEmployeeTransitionItem> Items);

public sealed class CloudEmployeeTransitionClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(6);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeTransitionClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<CloudEmployeeTransitionInspection> InspectAsync(
        IReadOnlyList<EmployeeAccount> localEmployees,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localEmployees);
        if (localEmployees.Count is < 1 or > 500)
            throw new ArgumentException("One to 500 Local Employees are required for transition inspection.", nameof(localEmployees));

        var payload = new
        {
            localEmployees = localEmployees.Select(employee => new
            {
                employeeNo = employee.EmployeeNo,
                name = employee.Name,
                email = employee.Email,
                role = employee.Role,
                enabled = employee.Enabled,
            }).ToArray(),
        };

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/employee-transition/inspect",
            payload,
            cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("transition", out var transition) || transition.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud employee transition response is missing transition data.");
        if (!transition.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cloud employee transition response is missing items.");

        var items = new List<CloudEmployeeTransitionItem>();
        foreach (var item in itemsElement.EnumerateArray())
            items.Add(ReadItem(item));

        return new CloudEmployeeTransitionInspection(
            ReadRequiredString(transition, "deviceId"),
            ReadRequiredString(transition, "authorityState"),
            ReadRequiredHash(transition, "snapshotHash"),
            ReadRequiredInt32(transition, "localEmployeeCount"),
            ReadRequiredInt32(transition, "unresolvedCount"),
            ReadRequiredInt32(transition, "conflictCount"),
            ReadRequiredBoolean(transition, "readyForCutover"),
            items);
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
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

    private static CloudEmployeeTransitionItem ReadItem(JsonElement item)
    {
        if (!item.TryGetProperty("local", out var local) || local.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud employee transition item is missing Local Employee data.");

        return new CloudEmployeeTransitionItem(
            ReadRequiredString(local, "employeeNo"),
            ReadRequiredString(local, "name"),
            ReadRequiredString(local, "email"),
            ReadRequiredString(local, "role"),
            ReadRequiredBoolean(local, "enabled"),
            ReadRequiredString(item, "suggestedCloudRole"),
            ReadRequiredString(item, "state"),
            ReadRequiredString(item, "matchKind"),
            ReadOptionalIdentity(item, "matchedEmployee"),
            ReadOptionalIdentity(item, "employeeNoMatch"),
            ReadOptionalIdentity(item, "emailMatch"));
    }

    private static CloudEmployeeTransitionIdentity? ReadOptionalIdentity(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Cloud employee transition field '{property}' is invalid.");
        return new CloudEmployeeTransitionIdentity(
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

    private static string ReadRequiredHash(JsonElement element, string name)
    {
        var value = ReadRequiredString(element, name);
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException($"Cloud response field '{name}' is invalid.");
        return value;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadRequiredInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return result;
    }

    private static bool ReadRequiredBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Cloud response field '{name}' is missing or invalid.");
        return value.GetBoolean();
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
