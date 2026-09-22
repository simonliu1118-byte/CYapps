using System.Net.Http.Headers;
using System.Text.Json;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Cloud;

public sealed record CloudEmployeeIdentity(
    string EmployeeId,
    string EmployeeNo,
    string Name,
    string Email,
    string Role,
    bool Enabled);

public sealed record CloudEmployeeReconciliation(
    string State,
    CloudEmployeeIdentity? Employee)
{
    public bool CentralRoleReady => State is "owner_created" or "admin_created" or "linked";
}

public sealed class CloudEmployeeClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(4);
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string deviceToken;

    public CloudEmployeeClient(HttpClient httpClient, Uri baseUri, string deviceToken)
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

    [Obsolete("Single-account Local SUPER_ADMIN reconciliation is retired. Use CloudEmployeeTransitionClient for whole-device transition.")]
    public Task<CloudEmployeeReconciliation> ReconcileLocalSuperAdminAsync(
        EmployeeAccount? localSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        _ = localSuperAdmin;
        _ = cancellationToken;
        throw new NotSupportedException(
            "Single-account reconciliation has been retired. Use the whole-device Local → Cloud Employee Transition flow.");
    }

    public async Task<IReadOnlyList<CloudEmployeeIdentity>> ListEmployeesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/employees", cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("employees", out var employees) || employees.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Cloud employee response is missing employees data.");

        var result = new List<CloudEmployeeIdentity>();
        foreach (var element in employees.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Cloud employee response contains invalid employee data.");
            result.Add(ReadEmployee(element));
        }
        return result;
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Request-ID", $"win-{Guid.NewGuid():N}");

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

    private static CloudEmployeeIdentity ReadEmployee(JsonElement employee)
    {
        return new CloudEmployeeIdentity(
            ReadRequiredString(employee, "employeeId"),
            ReadRequiredString(employee, "employeeNo"),
            ReadRequiredString(employee, "name"),
            ReadRequiredString(employee, "email"),
            ReadRequiredString(employee, "role"),
            employee.TryGetProperty("enabled", out var enabled)
                && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                && enabled.GetBoolean());
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
