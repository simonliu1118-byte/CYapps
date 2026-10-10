using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

namespace CYInvoice.Core.Storage;

public sealed record CyIdBinding(string BaseUrl, string WorkspaceId, string DeviceId,
    string IdentityWorkspaceId, string ApplicationId, string ConsumerVersion, string DeviceTokenDigest);

public sealed class CyIdAuthenticationException(string code, HttpStatusCode? statusCode = null) : InvalidOperationException(MessageFor(code))
{
    public string Code { get; } = code;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    private static string MessageFor(string code) => code switch
    {
        "ACCESS_DENIED" => "此帳號目前沒有 CYInvoice 使用權限，請由 CY Web 管理員確認。",
        "LOGIN_RATE_LIMITED" => "驗證次數過多，請稍後再試。",
        "DEVICE_INVALID" => "目前裝置已失效，請重新確認裝置授權。",
        "IDENTITY_UNAVAILABLE" => "目前無法確認 CYID 權限，請稍後再試。",
        _ => "CYID 驗證失敗，請確認員工帳號、裝置及 Workspace 設定。",
    };
}

public static class CyIdGateway
{
    // Contract revisions describe transport, not a new Device/Workspace authority.
    public static bool SameAuthorityScope(CyIdBinding first, CyIdBinding second) =>
        first with { ConsumerVersion = second.ConsumerVersion } == second;

    public static string TokenDigest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static async Task<CyIdBinding?> DiscoverAsync(HttpClient http, Uri baseUrl, string deviceToken,
        string workspaceId, string deviceId, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(baseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "v1/identity-provider"));
        request.Headers.Authorization = new("Bearer", deviceToken);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        // A pre-CYID Built-in Worker has no discovery route. Other errors never mean Local.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        using var json = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        if (Text(root, "workspaceId") != workspaceId || Text(root, "deviceId") != deviceId)
            throw new InvalidDataException("CYID 裝置／Workspace 回應不一致。");
        var provider = Text(root, "provider");
        if (provider == "BUILT_IN") return null;
        if (provider != "CYID") throw new InvalidDataException("不支援的身分來源。");
        var binding = new CyIdBinding(baseUrl.AbsoluteUri, workspaceId, deviceId,
            Text(root, "identityWorkspaceId"), Text(root, "applicationId"), Text(root, "consumerVersion"), TokenDigest(deviceToken));
        ValidateBinding(binding);
        return binding;
    }

    public static async Task<AppPrincipal> AuthenticateAsync(HttpClient http, CyIdBinding binding,
        string token, IdentityAuthenticationRequest credentials, CancellationToken cancellationToken)
    {
        ValidateBinding(binding);
        if (TokenDigest(token) != binding.DeviceTokenDigest) throw new InvalidDataException("CYID 裝置憑證已變更。");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(binding.BaseUrl), "v1/cyid/authenticate"));
        request.Headers.Authorization = new("Bearer", token);
        request.Content = JsonContent.Create(new { employeeNo = credentials.EmployeeNo, password = credentials.Password });
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        using var json = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        if (Text(root, "provider") != "CYID" || Text(root, "workspaceId") != binding.WorkspaceId
            || Text(root, "deviceId") != binding.DeviceId || Text(root, "identityWorkspaceId") != binding.IdentityWorkspaceId
            || Text(root, "applicationId") != binding.ApplicationId || Text(root, "consumerVersion") is not ("1.0.2" or "1.0.3")
            || root.TryGetProperty("session", out _) || root.TryGetProperty("firstLogin", out _))
            throw new InvalidDataException("CYID 驗證回應與已確認綁定不一致。");
        var p = root.GetProperty("principal");
        var role = AppRoles.Parse(Text(p, "workspaceRole"));
        var employeeId = Text(p, "employeeId");
        var employeeNo = Text(p, "employeeNo");
        var name = Text(p, "displayName");
        var isIdentityAdmin = p.GetProperty("isIdentityAdmin").GetBoolean();
        var emailVerified = p.GetProperty("emailVerified").GetBoolean();
        var credentialVersion = p.GetProperty("credentialVersion").GetInt32();
        var revision = p.GetProperty("employeeRevision").GetInt32();
        if (Text(p, "workspaceId") != binding.IdentityWorkspaceId || employeeId.Length is < 1 or > 100
            || employeeNo != credentials.EmployeeNo || name.Trim().Length is < 1 or > 120
            || p.GetProperty("isWorkspaceSuperAdmin").GetBoolean() != (role == AppRole.SuperAdmin)
            || (isIdentityAdmin && role != AppRole.Admin) || credentialVersion < 1 || revision < 1)
            throw new InvalidDataException("CYID principal 格式無效。");
        return new AppPrincipal(employeeId, employeeNo, name, string.Empty, role, true,
            IdentityProviderKind.CyId, revision) {
            WorkspaceId = binding.IdentityWorkspaceId, CredentialVersion = credentialVersion,
            IsIdentityAdmin = isIdentityAdmin, EmailVerified = emailVerified,
        };
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)); }
        catch (JsonException) {
            if ((int)response.StatusCode is >= 500 and <= 599)
                throw new CyIdAuthenticationException("IDENTITY_UNAVAILABLE", response.StatusCode);
            throw new InvalidDataException("CYID 回應格式無效。");
        }
        if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var code = "IDENTITY_UNAVAILABLE";
            if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var value))
                code = value.GetString() ?? code;
            json.Dispose();
            throw new CyIdAuthenticationException(code, response.StatusCode);
        }
        return json;
    }

    private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString()
        ?? throw new InvalidDataException("CYID 回應欄位無效。");

    public static void ValidateBinding(CyIdBinding binding)
    {
        ValidateEndpoint(new Uri(binding.BaseUrl, UriKind.Absolute));
        if (binding.WorkspaceId.Length is < 1 or > 80 || binding.DeviceId.Length is < 1 or > 80
            || binding.IdentityWorkspaceId.Length is < 1 or > 80 || binding.ApplicationId.Length is < 2 or > 64
            || binding.ApplicationId.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not '_' and not '-')
            || binding.ConsumerVersion is not ("1.0.2" or "1.0.3") || binding.DeviceTokenDigest.Length != 64)
            throw new InvalidDataException("CYID 綁定格式無效。");
    }

    private static void ValidateEndpoint(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("CYID gateway 必須使用 HTTPS。");
    }
}

// A subordinate, device-bound offline cache, never a CYID verifier export or server
// credential/session authority. The entire entry (including role) is platform protected.
public sealed class CyIdOfflineCache(string dataDirectory, ISecretProtector protector)
{
    internal SemaphoreSlim AuthorityGate { get; } = new(1, 1);
    private sealed record Entry(CyIdBinding Binding, AppPrincipal Principal, string Salt, string Hash);
    private readonly Lock gate = new();
    private readonly string path = Path.Combine(dataDirectory, "cyid_offline_cache.json");
    private const int Iterations = 210_000;

    private Dictionary<string, string> Load() => JsonFile.TryRead<Dictionary<string, string>>(path, out var value)
        ? value! : new(StringComparer.Ordinal);

    public object[] AuthorityChecks(CyIdBinding binding)
    {
        lock (gate) return Load().Values.Select(encrypted =>
        {
            var entry = JsonSerializer.Deserialize<Entry>(protector.Unprotect(encrypted))
                ?? throw new InvalidDataException("CYID 快取無效。");
            if (!CyIdGateway.SameAuthorityScope(entry.Binding, binding)) throw new InvalidDataException("CYID 快取綁定不一致。");
            var p = entry.Principal;
            return (object)new { employeeId = p.StableEmployeeId, credentialVersion = p.CredentialVersion,
                employeeRevision = p.AuthorityRevision, workspaceRole = AppRoles.ToValue(p.Role),
                isIdentityAdmin = p.IsIdentityAdmin, emailVerified = p.EmailVerified };
        }).ToArray();
    }

    public void InvalidateEmployees(IReadOnlySet<string> employeeIds)
    {
        lock (gate)
        {
            var entries = Load();
            foreach (var key in entries.Keys.ToArray())
            {
                var entry = JsonSerializer.Deserialize<Entry>(protector.Unprotect(entries[key]))
                    ?? throw new InvalidDataException("CYID 快取無效。");
                if (employeeIds.Contains(entry.Principal.StableEmployeeId)) entries.Remove(key);
            }
            JsonFile.Write(path, entries);
        }
    }

    public void Remember(CyIdBinding binding, AppPrincipal principal, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var entry = new Entry(binding, principal, Convert.ToHexString(salt), Convert.ToHexString(hash));
        lock (gate) {
            var entries = Load();
            entries[principal.EmployeeNo] = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(entry));
            JsonFile.Write(path, entries);
        }
    }

    public AppPrincipal? Authenticate(CyIdBinding binding, IdentityAuthenticationRequest request)
    {
        lock (gate) {
            if (!Load().TryGetValue(request.EmployeeNo, out var encrypted)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(protector.Unprotect(encrypted))
                ?? throw new InvalidDataException("CYID 離線快取格式無效。");
            if (!CyIdGateway.SameAuthorityScope(entry.Binding, binding) || entry.Principal.ProviderKind != IdentityProviderKind.CyId
                || entry.Principal.WorkspaceId != binding.IdentityWorkspaceId || entry.Principal.EmployeeNo != request.EmployeeNo
                || !entry.Principal.Enabled) throw new InvalidDataException("CYID 離線快取不屬於目前裝置／Workspace。");
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(request.Password), Convert.FromHexString(entry.Salt),
                Iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(entry.Hash)) ? entry.Principal : null;
        }
    }

    public void Forget(string? employeeNo = null)
    {
        lock (gate) {
            if (employeeNo is null) { if (File.Exists(path)) File.Delete(path); return; }
            var entries = Load();
            entries.Remove(employeeNo);
            JsonFile.Write(path, entries);
        }
    }
}

public sealed class CyIdIdentityProvider(SettingsStore settings, CyIdOfflineCache offline, HttpClient? http = null,
    Func<CancellationToken, Task>? requireAmegoForFallback = null) : IIdentityProvider
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    public IdentityProviderKind Kind => IdentityProviderKind.CyId;
    public bool OwnsAccountManagement => false;

    public async Task<AppPrincipal?> AuthenticateAsync(IdentityAuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        await offline.AuthorityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await AuthenticateCoreAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { offline.AuthorityGate.Release(); }
    }

    private async Task<AppPrincipal?> AuthenticateCoreAsync(IdentityAuthenticationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var count = request.Password.EnumerateRunes().Count();
        if (request.EmployeeNo.Length != 4 || !request.EmployeeNo.All(char.IsAsciiDigit) || count is < 8 or > 16) return null;
        var current = settings.LoadOrCreate();
        var binding = settings.CyIdConfiguration(current);
        var token = settings.CloudDeviceToken(current);
        try {
            var principal = await CyIdGateway.AuthenticateAsync(http ?? SharedHttpClient, binding, token, request, cancellationToken)
                .ConfigureAwait(false);
            offline.Remember(binding, principal, request.Password);
            return principal;
        }
        catch (HttpRequestException) { return await FallbackAsync(binding, request, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return await FallbackAsync(binding, request, cancellationToken).ConfigureAwait(false);
        }
        catch (CyIdAuthenticationException error) when ((int?)error.StatusCode is >= 500 and <= 599
            && error.Code == "IDENTITY_UNAVAILABLE") {
            return await FallbackAsync(binding, request, cancellationToken).ConfigureAwait(false);
        }
        catch (CyIdAuthenticationException error) {
            if (error.Code is "DEVICE_INVALID" or "IDENTITY_WORKSPACE_MISMATCH" or "IDENTITY_PROVIDER_MISMATCH") offline.Forget();
            else if (error.Code is "LOGIN_FAILED" or "AUTH_INVALID" or "ACCESS_DENIED") offline.Forget(request.EmployeeNo);
            if (error.Code is "LOGIN_FAILED" or "AUTH_INVALID") return null;
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { offline.Forget(request.EmployeeNo); throw; }
    }

    private async Task<AppPrincipal?> FallbackAsync(CyIdBinding binding, IdentityAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        if (requireAmegoForFallback is not null)
            await requireAmegoForFallback(cancellationToken).ConfigureAwait(false);
        return offline.Authenticate(binding, request);
    }

    public Task<AppPrincipal?> RefreshPrincipalAsync(string employeeNo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An employee number is not a CYID Session. Execution-time credentials are required.
        throw new CyIdAuthenticationException("AUTH_REQUIRED");
    }
}
