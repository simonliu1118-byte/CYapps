using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    // Compatibility parser only. The current reference backend retires this
    // mutation route with LEGACY_EMPLOYEE_RECONCILIATION_RETIRED; all product
    // flows use CloudEmployeeTransitionClient for whole-device transition.
    public async Task<CloudEmployeeReconciliation> ReconcileLocalSuperAdminAsync(
        EmployeeAccount? localSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        object? local = null;
        if (localSuperAdmin is not null)
        {
            if (localSuperAdmin.Role != EmployeeRoles.SuperAdmin || !localSuperAdmin.Enabled)
                throw new ArgumentException("Local account must be the enabled Local SUPER_ADMIN.", nameof(localSuperAdmin));
            local = new
            {
                employeeNo = localSuperAdmin.EmployeeNo,
                name = localSuperAdmin.Name,
                email = localSuperAdmin.Email,
            };
        }

        using var document = await SendAsync(
            HttpMethod.Post,
            "v1/employees/reconcile-local",
            new { localSuperAdmin = local },
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("reconciliation", out var reconciliation) || reconciliation.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cloud employee reconciliation response is missing reconciliation data.");

        var state = ReadRequiredString(reconciliation, "state");
        CloudEmployeeIdentity? employee = null;
        if (reconciliation.TryGetProperty("employee", out var employeeElement) && employeeElement.ValueKind == JsonValueKind.Object)
            employee = ReadEmployee(employeeElement);
        return new CloudEmployeeReconciliation(state, employee);
    }

    public async Task<IReadOnlyList<CloudEmployeeIdentity>> ListEmployeesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get, "v1/employees", null, cancellationToken);
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
