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
    CyId,
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
    public string WorkspaceId { get; init; } = string.Empty;
    public int CredentialVersion { get; init; }
    public bool IsIdentityAdmin { get; init; }
    public bool EmailVerified { get; init; }

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
    private readonly CloudEmployeeCacheStore employees;
    private readonly ICloudEmployeeAuthoritySnapshotSource authority;

    public BuiltInCloudIdentityProvider(
        CloudEmployeeCacheStore employees,
        ICloudEmployeeAuthoritySnapshotSource authority)
    {
        this.employees = employees ?? throw new ArgumentNullException(nameof(employees));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public IdentityProviderKind Kind => IdentityProviderKind.BuiltInCloud;
    public bool OwnsAccountManagement => true;

    public async Task<AppPrincipal?> AuthenticateAsync(
        IdentityAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshCacheFromCurrentAuthorityAsync(cancellationToken).ConfigureAwait(false);
        return ToPrincipal(employees.Authenticate(request.EmployeeNo, request.Password));
    }

    public async Task<AppPrincipal?> RefreshPrincipalAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RefreshCacheFromCurrentAuthorityAsync(cancellationToken).ConfigureAwait(false);
        var normalized = (employeeNo ?? string.Empty).Trim();
        var account = employees.LoadAll().FirstOrDefault(candidate =>
            string.Equals(candidate.EmployeeNo, normalized, StringComparison.Ordinal));
        return ToPrincipal(account);
    }

    private async Task RefreshCacheFromCurrentAuthorityAsync(CancellationToken cancellationToken)
    {
        var offlineFallback = false;
        try
        {
            var snapshot = await authority.GetCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
            employees.ReplaceSnapshot(snapshot.WorkspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
            return;
        }
        catch (HttpRequestException)
        {
            // A genuine transport failure is the approved Offline boundary.
            offlineFallback = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The Cloud request timed out rather than being cancelled by the caller.
            offlineFallback = true;
        }

        if (!offlineFallback) return;
        var state = employees.LoadState();
        var currentWorkspaceId = authority.CurrentWorkspaceId;
        if (state is null
            || !string.Equals(state.WorkspaceId, currentWorkspaceId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "目前無法連線 Cloud，且本機沒有屬於目前 Workspace 的可信 Employee 離線快取。");
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
    IIdentityProvider builtInCloud,
    IIdentityProvider? cyId = null)
{
    private readonly SettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IIdentityProvider local = local ?? throw new ArgumentNullException(nameof(local));
    private readonly IIdentityProvider builtInCloud = builtInCloud ?? throw new ArgumentNullException(nameof(builtInCloud));

    public IIdentityProvider Current
    {
        get
        {
            var current = settings.LoadOrCreate();
            if (current.CloudMode == CloudModes.LocalOnly) return local;
            if (current.CloudIdentityProvider == "CYID")
                return cyId ?? throw new InvalidOperationException("CYID Provider 尚未設定。");
            return current.CloudMode == CloudModes.CloudPreferred && current.CloudEmployeeAuthorityReady
                ? builtInCloud : local;
        }
    }
}
