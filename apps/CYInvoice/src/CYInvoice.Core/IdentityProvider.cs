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

public sealed class IdentityProviderRuntime(
    SettingsStore settings,
    IIdentityProvider local,
    IIdentityProvider cyId)
{
    private readonly SettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IIdentityProvider local = local ?? throw new ArgumentNullException(nameof(local));
    private readonly IIdentityProvider cyId = cyId ?? throw new ArgumentNullException(nameof(cyId));

    public IIdentityProvider Current
    {
        get
        {
            var current = settings.LoadOrCreate();
            if (current.CloudMode == CloudModes.LocalOnly) return local;
            // V3 has exactly one supported Cloud identity authority: CYID.
            // Legacy Built-in or incomplete local-to-cloud transition must not
            // silently fall back to Local credentials or create a second authority.
            if (current.CloudMode != CloudModes.CloudPreferred ||
                current.CloudIdentityProvider != "CYID" ||
                !current.CloudEmployeeAuthorityReady)
                throw new InvalidOperationException(
                    "CYInvoice V3 雲端模式只支援已完成綁定的 CYID。舊 Built-in Cloud／帳號轉換狀態不能登入，請使用正式 CYID 加入流程。");
            return cyId ?? throw new InvalidOperationException("CYID Provider 尚未設定。");
        }
    }
}
