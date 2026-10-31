using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal static class EmployeeDisplayText
{
    internal static string Format(LocalRepository? repository, string employeeNo, string? savedName = null)
    {
        employeeNo = employeeNo.Trim();
        if (employeeNo.Length == 0) return "－";
        var name = savedName?.Trim();
        if (string.IsNullOrEmpty(name)) {
            try { name = repository?.KnownEmployeeName(employeeNo)?.Trim(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or InvalidOperationException) { }
        }
        return string.IsNullOrEmpty(name) ? employeeNo + "（姓名未取得）" : employeeNo + " " + name;
    }
}
