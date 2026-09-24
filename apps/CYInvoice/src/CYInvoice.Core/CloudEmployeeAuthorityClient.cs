using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeAuthorityStatus(
    string DeviceId,
    string WorkspaceId,
    string State,
    string TransitionSnapshotHash,
    int TransitionItemCount,
    int UnresolvedCount,
    int InvalidCentralEmployeeCount,
    bool ReadyForCutover,
    DateTimeOffset? CompletedAt,
    int WorkspaceRevision);

public sealed record CloudEmployeeAuthoritySnapshot(
    string WorkspaceId,
    int WorkspaceRevision,
    IReadOnlyList<CloudEmployeeCacheSeed> Employees);

public sealed class CloudEmployeeAuthorityClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeAuthorityClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    public async Task<CloudEmployeeAuthorityStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/employee-authority/status", null, cancellationToken);
        return ReadStatus(document.RootElement);
    }

    public async Task<CloudEmployeeAuthoritySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/employee-authority/snapshot", null, cancellationToken);
        if (!document.RootElement.TryGetProperty("employeeSnapshot", out var snapshot) || snapshot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Employee authority response is missing employeeSnapshot.");
        if (!snapshot.TryGetProperty("employees", out var employeesElement) || employeesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cloud Employee authority snapshot is missing Employees.");

        var employees = new List<CloudEmployeeCacheSeed>();
        foreach (var employee in employeesElement.EnumerateArray())
        {
            var algorithm = ReadRequiredString(employee, "credentialAlgorithm");
            if (!string.Equals(algorithm, "pbkdf2-sha256", StringComparison.Ordinal))
                throw new InvalidDataException("Cloud Employee credential algorithm is not supported.");
            employees.Add(new CloudEmployeeCacheSeed(
                ReadRequiredString(employee, "employeeId"),
                ReadRequiredString(employee, "employeeNo"),
                ReadRequiredString(employee, "name"),
                ReadRequiredString(employee, "email"),
                ReadRequiredString(employee, "role"),
                ReadRequiredBoolean(employee, "enabled"),
                ReadRequiredBoolean(employee, "emailVerified"),
                ReadRequiredString(employee, "credentialVerifier"),
                ReadRequiredInt32(employee, "credentialVersion"),
                ReadRequiredInt32(employee, "revision")));
        }
        if (employees.Count == 0) throw new InvalidDataException("Cloud Employee authority snapshot is empty.");
        return new CloudEmployeeAuthoritySnapshot(
            ReadRequiredString(snapshot, "workspaceId"),
            ReadRequiredInt32(snapshot, "workspaceRevision"),
            employees);
    }

    public async Task<CloudEmployeeAuthorityStatus> CutoverAsync(
        string snapshotHash,
        CancellationToken cancellationToken = default)
    {
        ValidateSnapshotHash(snapshotHash);
        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/employee-authority/cutover",
            new { snapshotHash },
            cancellationToken);
        return ReadStatus(document.RootElement);
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

    private static CloudEmployeeAuthorityStatus ReadStatus(JsonElement root)
    {
        if (!root.TryGetProperty("authority", out var authority) || authority.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud Employee authority response is missing authority status.");
        var snapshot = ReadOptionalString(authority, "transitionSnapshotHash");
        if (snapshot.Length != 0) ValidateSnapshotHash(snapshot);
        return new CloudEmployeeAuthorityStatus(
            ReadRequiredString(authority, "deviceId"),
            ReadRequiredString(authority, "workspaceId"),
            ReadRequiredString(authority, "state"),
            snapshot,
            ReadRequiredInt32(authority, "transitionItemCount"),
            ReadRequiredInt32(authority, "unresolvedCount"),
            ReadRequiredInt32(authority, "invalidCentralEmployeeCount"),
            ReadRequiredBoolean(authority, "readyForCutover"),
            ReadOptionalDateTimeOffset(authority, "completedAt"),
            ReadRequiredInt32(authority, "workspaceRevision"));
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

    private static DateTimeOffset? ReadOptionalDateTimeOffset(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), out var result))
            throw new InvalidDataException($"Cloud response field '{name}' is invalid.");
        return result;
    }

    private static void ValidateSnapshotHash(string value)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Transition snapshot hash is invalid.", nameof(value));
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
