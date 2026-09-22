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
        employeeNo = (employeeNo ?? string.Empty).Trim();
        password ??= string.Empty;
        var delay = EmployeeVoidAuthenticationThrottle.Remaining(employeeNo, now);
        if (delay > TimeSpan.Zero) throw new EmployeeVoidAuthenticationDelayException(delay);

        EmployeeAccount? employee = null;
        try
        {
            employee = repository.AuthenticateEmployee(employeeNo, password);
        }
        catch (InvalidOperationException)
        {
            // Invalid employee-number shape is intentionally indistinguishable from bad credentials.
        }

        if (employee is null)
        {
            EmployeeVoidAuthenticationThrottle.RegisterFailure(employeeNo, now);
            throw new InvalidOperationException("員工編號或密碼錯誤");
        }

        EmployeeVoidAuthenticationThrottle.Reset(employeeNo);
        return employee;
    }

    public static EmployeeAccount AuthenticateManager(
        LocalRepository repository,
        string employeeNo,
        string password)
    {
        EmployeeAccount? employee = null;
        try { employee = repository.AuthenticateEmployee(employeeNo, password ?? string.Empty); }
        catch (InvalidOperationException) { }
        if (employee is null || !EmployeeRoles.CanManageAccounts(employee.Role))
            throw new UnauthorizedAccessException("管理員驗證失敗");
        return employee;
    }
}
