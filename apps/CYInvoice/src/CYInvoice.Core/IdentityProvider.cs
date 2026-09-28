using CYInvoice.Core.Cloud;

namespace CYInvoice.Core.Storage;

public enum AppRole
{
    User,
    Admin,
    SuperAdmin,
}

public static class AppRoles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Admin = "ADMIN";
    public const string User = "USER";

    public static AppRole Parse(string role) => role switch
    {
        User => AppRole.User,
        Admin => AppRole.Admin,
        SuperAdmin => AppRole.SuperAdmin,
        _ => throw new InvalidDataException($"Unsupported CYInvoice role: {role}"),
    };

    public static string ToValue(AppRole role) => role switch
    {
        AppRole.User => User,
        AppRole.Admin => Admin,
        AppRole.SuperAdmin => SuperAdmin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static bool CanManageAccounts(AppRole role) => role is AppRole.Admin or AppRole.SuperAdmin;
}

public enum IdentityProviderKind
{
    Local,
    BuiltInCloud,
}

public sealed record IdentityAuthenticationRequest(string EmployeeNo, string Password);

public sealed record AppPrincipal(
    string StableEmployeeId,
    string EmployeeNo,
    string DisplayName,
    string Email,
    AppRole Role,
    bool Enabled,
    IdentityProviderKind ProviderKind,
    int AuthorityRevision)
{
    public EmployeeAccount ToEmployeeAccount(DateTimeOffset? timestamp = null)
    {
        var value = timestamp ?? DateTimeOffset.UtcNow;
        return new EmployeeAccount(
            EmployeeNo,
            DisplayName,
            Email,
            AppRoles.ToValue(Role),
            Enabled,
            value,
            value);
    }
}

public interface IIdentityProvider
{
    IdentityProviderKind Kind { get; }
    bool OwnsAccountManagement { get; }

    Task<AppPrincipal?> AuthenticateAsync(
        IdentityAuthenticationRequest request,
        CancellationToken cancellationToken = default);

    Task<AppPrincipal?> RefreshPrincipalAsync(
        string employeeNo,
        CancellationToken cancellationToken = default);

    Task RefreshAuthorityAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalIdentityProvider(EmployeeStore employees) : IIdentityProvider
{
    private readonly EmployeeStore employees = employees ?? throw new ArgumentNullException(nameof(employees));

    public IdentityProviderKind Kind => IdentityProviderKind.Local;
    public bool OwnsAccountManagement => true;

    public Task<AppPrincipal?> AuthenticateAsync(
        IdentityAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToPrincipal(employees.Authenticate(request.EmployeeNo, request.Password)));
    }

    public Task<AppPrincipal?> RefreshPrincipalAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToPrincipal(employees.Find(employeeNo)));
    }

    public Task RefreshAuthorityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static AppPrincipal? ToPrincipal(EmployeeAccount? account)
    {
        if (account is null) return null;
        return new AppPrincipal(
            account.EmployeeNo,
            account.EmployeeNo,
            account.Name,
            account.Email,
            AppRoles.Parse(account.Role),
            account.Enabled,
            IdentityProviderKind.Local,
            AuthorityRevision: 0);
    }
}

public sealed class BuiltInCloudIdentityProvider : IIdentityProvider
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly SettingsStore settings;
    private readonly CloudEmployeeCacheStore employees;
    private readonly HttpClient httpClient;

    public BuiltInCloudIdentityProvider(
        SettingsStore settings,
        CloudEmployeeCacheStore employees,
        HttpClient? httpClient = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.employees = employees ?? throw new ArgumentNullException(nameof(employees));
        this.httpClient = httpClient ?? SharedHttpClient;
    }

    public IdentityProviderKind Kind => IdentityProviderKind.BuiltInCloud;
    public bool OwnsAccountManagement => true;

    public async Task<AppPrincipal?> AuthenticateAsync(
        IdentityAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshForExecutionAsync(cancellationToken).ConfigureAwait(false);
        return ToPrincipal(employees.Authenticate(request.EmployeeNo, request.Password));
    }

    public async Task<AppPrincipal?> RefreshPrincipalAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshForExecutionAsync(cancellationToken).ConfigureAwait(false);
        var normalized = (employeeNo ?? string.Empty).Trim();
        var account = employees.LoadAll().FirstOrDefault(candidate =>
            string.Equals(candidate.EmployeeNo, normalized, StringComparison.Ordinal));
        return ToPrincipal(account);
    }

    public async Task RefreshAuthorityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = settings.LoadOrCreate();
        if (current.CloudMode != CloudModes.CloudPreferred || !current.CloudEmployeeAuthorityReady)
            throw new InvalidOperationException("Built-in Cloud Employee authority is not active.");
        if (current.CloudBaseUrl.Length == 0 || current.CloudWorkspaceId.Length == 0 || current.CloudDeviceId.Length == 0)
            throw new InvalidOperationException("Built-in Cloud Workspace/Device identity is incomplete.");

        var token = settings.CloudDeviceToken(current);
        if (token.Length == 0)
            throw new InvalidOperationException("Built-in Cloud Device Token is unavailable.");

        var client = new CloudEmployeeAuthorityClient(
            httpClient,
            new Uri(current.CloudBaseUrl, UriKind.Absolute),
            token);
        var snapshot = await client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(snapshot.WorkspaceId, current.CloudWorkspaceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud Employee snapshot Workspace identity does not match this device.");

        employees.ReplaceSnapshot(
            snapshot.WorkspaceId,
            snapshot.WorkspaceRevision,
            snapshot.Employees);
    }

    private async Task RefreshForExecutionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAuthorityAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // A transport failure is genuine Offline. Continue with the last trusted
            // protected snapshot from the same Built-in Cloud authority.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The Cloud request timed out independently of caller cancellation.
            // This is the other approved Offline fallback path.
        }
    }

    private static AppPrincipal? ToPrincipal(CloudEmployeeCachedAccount? account)
    {
        if (account is null) return null;
        return new AppPrincipal(
            account.EmployeeId,
            account.EmployeeNo,
            account.Name,
            account.Email,
            AppRoles.Parse(account.Role),
            account.Enabled,
            IdentityProviderKind.BuiltInCloud,
            account.Revision);
    }
}

public sealed class IdentityProviderRuntime(
    SettingsStore settings,
    IIdentityProvider local,
    IIdentityProvider builtInCloud)
{
    private readonly SettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IIdentityProvider local = local ?? throw new ArgumentNullException(nameof(local));
    private readonly IIdentityProvider builtInCloud = builtInCloud ?? throw new ArgumentNullException(nameof(builtInCloud));

    public IIdentityProvider Current
    {
        get
        {
            var current = settings.LoadOrCreate();
            return current.CloudMode == CloudModes.CloudPreferred && current.CloudEmployeeAuthorityReady
                ? builtInCloud
                : local;
        }
    }
}
