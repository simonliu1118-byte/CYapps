using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

internal static class EmployeeOperationAuthentication
{
    public static EmployeeAccount AuthenticateEmployee(
        LocalRepository repository,
        string employeeNo,
        string password,
        DateTimeOffset now)
    {
        var principal = AuthenticatePrincipal(repository, employeeNo, password, now);
        return principal.ToEmployeeAccount();
    }

    public static EmployeeAccount AuthenticateManager(
        LocalRepository repository,
        string employeeNo,
        string password)
    {
        AppPrincipal? principal = null;
        try { principal = repository.AuthenticatePrincipal(employeeNo, password ?? string.Empty); }
        catch (InvalidOperationException) when (repository.IdentityProvider.Kind == IdentityProviderKind.Local) { }
        if (principal is null || !AppRoles.CanManageAccounts(principal.Role))
            throw new UnauthorizedAccessException("管理員驗證失敗");
        return principal.ToEmployeeAccount();
    }

    // A durable business approval is executed by the local workflow, not by
    // retaining a login. Revoked managers or a changed authority stop writes.
    internal static EmployeeAccount RequireApprovedLocalManager(LocalRepository repository, string employeeNo)
    {
        if (repository.Settings.LoadOrCreate().CloudMode != CloudModes.LocalOnly ||
            repository.IdentityProvider.Kind != IdentityProviderKind.Local)
            throw new UnauthorizedAccessException("原 Local 核准的執行權威已改變");
        var manager = repository.Employees.Find(employeeNo);
        if (manager is null || !manager.Enabled || !EmployeeRoles.CanManageAccounts(manager.Role))
            throw new UnauthorizedAccessException("原核准主管已停用或失去主管權限");
        return manager;
    }

    private static AppPrincipal AuthenticatePrincipal(
        LocalRepository repository,
        string employeeNo,
        string password,
        DateTimeOffset now)
    {
        employeeNo = (employeeNo ?? string.Empty).Trim();
        password ??= string.Empty;
        var delay = EmployeeVoidAuthenticationThrottle.Remaining(employeeNo, now);
        if (delay > TimeSpan.Zero) throw new EmployeeVoidAuthenticationDelayException(delay);

        AppPrincipal? principal = null;
        try
        {
            principal = repository.AuthenticatePrincipal(employeeNo, password);
        }
        catch (InvalidOperationException) when (repository.IdentityProvider.Kind == IdentityProviderKind.Local)
        {
            // Invalid employee-number shape is intentionally indistinguishable from bad credentials.
        }

        if (principal is null)
        {
            EmployeeVoidAuthenticationThrottle.RegisterFailure(employeeNo, now);
            throw new InvalidOperationException("員工編號或密碼錯誤");
        }

        EmployeeVoidAuthenticationThrottle.Reset(employeeNo);
        return principal;
    }
}
