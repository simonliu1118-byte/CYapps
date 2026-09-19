using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

internal static class EmployeeStoreTests
{
    public static void AdditiveSchemaPreservesExistingInvoiceData()
    {
        using var temporary = new EmployeeTemporaryDirectory();
        var legacyPath = Path.Combine(temporary.Path, "invoices.json");
        var records = new List<InvoiceRecord>
        {
            new()
            {
                Id = "before-employee-schema",
                Source = "手動",
                OriginalOrderId = "M20260919001",
                OrderId = "M20260919001",
                ApiOrderId = "M20260919001",
                Environment = Environments.Test,
                InvoiceNumber = "AA87654321",
                InvoiceState = InvoiceStates.Opened,
                Amount = 100,
                Delivery = "紙本",
                SentAt = "2026/09/19 05:00:00",
                InvoiceDate = "2026/09/19",
                InvoiceTime = "05:00:00",
                Items =
                [
                    new InvoiceItem
                    {
                        Description = "測試商品",
                        Quantity = 1,
                        QuantityDecimal = "1",
                        UnitPrice = 100,
                        UnitPriceDecimal = "100",
                        TaxType = "1",
                        Amount = 100,
                        AmountDecimal = "100",
                    },
                ],
            },
        };
        File.WriteAllText(
            legacyPath,
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            new UTF8Encoding(false));

        var baseResult = SqliteBootstrapper.EnsureMigrated(temporary.Path);
        EmployeeEqual(1, baseResult.InvoiceCount);
        var store = new EmployeeStore(temporary.Path);
        EmployeeEqual(false, store.HasEmployees());

        using var connection = OpenReadOnly(baseResult.DatabasePath);
        EmployeeEqual("1", ScalarText(connection, "SELECT value FROM schema_info WHERE key = 'schema_version';"));
        EmployeeEqual("1", ScalarText(connection, "SELECT value FROM schema_info WHERE key = 'employee_schema_version';"));
        EmployeeEqual(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM invoices;"));
        EmployeeEqual("before-employee-schema", ScalarText(connection, "SELECT record_id FROM invoices LIMIT 1;"));
        EmployeeEqual(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM invoice_items;"));
    }

    public static void SuperAdminRecoveryIsOneTimeAndSecretsAreHashed()
    {
        using var temporary = new EmployeeTemporaryDirectory();
        var store = new EmployeeStore(temporary.Path);
        var setup = store.CreateFirstSuperAdmin("3015", "Simon", "simon@example.com", "InitialPass1");

        EmployeeEqual(EmployeeRoles.SuperAdmin, setup.Employee.Role);
        EmployeeEqual(true, setup.Employee.Enabled);
        EmployeeEqual(true, setup.RecoveryCode.StartsWith("CYR-", StringComparison.Ordinal));
        EmployeeEqual(28, setup.RecoveryCode.Length);
        EmployeeEqual("3015", store.Authenticate("3015", "InitialPass1")?.EmployeeNo);
        EmployeeEqual<EmployeeAccount?>(null, store.Authenticate("3015", "WrongPass1"));

        var databasePath = Path.Combine(temporary.Path, SqliteBootstrapper.DatabaseFileName);
        using (var connection = OpenReadOnly(databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT password_hash, recovery_hash FROM employees WHERE employee_no = '3015';";
            using var reader = command.ExecuteReader();
            EmployeeEqual(true, reader.Read());
            var passwordHash = reader.GetString(0);
            var recoveryHash = reader.GetString(1);
            EmployeeEqual(true, passwordHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal));
            EmployeeEqual(true, recoveryHash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal));
            EmployeeEqual(false, passwordHash.Contains("InitialPass1", StringComparison.Ordinal));
            EmployeeEqual(false, recoveryHash.Contains(setup.RecoveryCode, StringComparison.Ordinal));
        }

        var replacementCode = store.ResetSuperAdminPasswordWithRecoveryCode(
            "3015",
            setup.RecoveryCode,
            "ReplacePass1");
        EmployeeEqual<EmployeeAccount?>(null, store.Authenticate("3015", "InitialPass1"));
        EmployeeEqual("3015", store.Authenticate("3015", "ReplacePass1")?.EmployeeNo);
        EmployeeThrows<InvalidOperationException>(() =>
            store.ResetSuperAdminPasswordWithRecoveryCode("3015", setup.RecoveryCode, "MustNotWork1"));

        var secondReplacement = store.ResetSuperAdminPasswordWithRecoveryCode(
            "3015",
            replacementCode,
            "ThirdPass1");
        EmployeeEqual(true, secondReplacement.StartsWith("CYR-", StringComparison.Ordinal));
        EmployeeEqual("3015", store.Authenticate("3015", "ThirdPass1")?.EmployeeNo);
    }

    public static void RoleRulesProtectSuperAdminAndAllowAdminPeerManagement()
    {
        using var temporary = new EmployeeTemporaryDirectory();
        var store = new EmployeeStore(temporary.Path);
        store.CreateFirstSuperAdmin("3015", "Simon", "simon@example.com", "SuperPass1");
        store.CreateEmployee("3015", "3020", "管理員甲", "admin-a@example.com", "AdminPass1", EmployeeRoles.Admin);
        store.CreateEmployee("3015", "3030", "管理員乙", "admin-b@example.com", "AdminPass2", EmployeeRoles.Admin);
        store.CreateEmployee("3015", "3040", "一般員工", "employee@example.com", "Employee1", EmployeeRoles.Employee);

        store.SetRole("3020", "3030", EmployeeRoles.Employee);
        EmployeeEqual(EmployeeRoles.Employee, store.Find("3030")?.Role);
        EmployeeThrows<InvalidOperationException>(() => store.SetRole("3020", "3020", EmployeeRoles.Employee));
        EmployeeThrows<InvalidOperationException>(() => store.SetRole("3020", "3015", EmployeeRoles.Employee));
        EmployeeThrows<InvalidOperationException>(() => store.SetEnabled("3020", "3015", false));
        EmployeeThrows<InvalidOperationException>(() => store.DeleteEmployee("3020", "3015"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.ResetPasswordByAdministrator("3020", "3015", "ShouldNotWork1"));

        store.SetEnabled("3020", "3040", false);
        EmployeeEqual<EmployeeAccount?>(null, store.Authenticate("3040", "Employee1"));
        store.SetEnabled("3020", "3040", true);
        EmployeeEqual("3040", store.Authenticate("3040", "Employee1")?.EmployeeNo);

        var databasePath = Path.Combine(temporary.Path, SqliteBootstrapper.DatabaseFileName);
        using var connection = OpenReadWrite(databasePath);
        EmployeeThrows<SqliteException>(() => Execute(connection,
            "UPDATE employees SET role = 'ADMIN' WHERE employee_no = '3015';"));
        EmployeeThrows<SqliteException>(() => Execute(connection,
            "UPDATE employees SET enabled = 0 WHERE employee_no = '3015';"));
        EmployeeThrows<SqliteException>(() => Execute(connection,
            "DELETE FROM employees WHERE employee_no = '3015';"));
        EmployeeEqual(EmployeeRoles.SuperAdmin, store.Find("3015")?.Role);
        EmployeeEqual(true, store.Find("3015")?.Enabled);
    }

    public static void EmployeeIdentityConstraintsAreEnforced()
    {
        using var temporary = new EmployeeTemporaryDirectory();
        var store = new EmployeeStore(temporary.Path);
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateFirstSuperAdmin("3015", "Simon", "", "SuperPass1"));
        store.CreateFirstSuperAdmin("3015", "Simon", "simon@example.com", "SuperPass1");

        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateFirstSuperAdmin("9999", "第二超管", "second@example.com", "Password8"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "123", "錯誤編號", "bad@example.com", "Password8"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "ABCD", "錯誤編號", "bad@example.com", "Password8"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "3050", "", "employee@example.com", "Password8"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "3050", "員工", "", "Password8"));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "3050", "員工", "employee@example.com", "short7", EmployeeRoles.Employee));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "3050", "員工", "employee@example.com", "Invalid-1", EmployeeRoles.Employee));
        EmployeeThrows<InvalidOperationException>(() =>
            store.CreateEmployee("3015", "3050", "員工", "employee@example.com", "Password8", EmployeeRoles.SuperAdmin));
    }

    public static void SettingsModelHasNoLegacyManagementPasswordFields()
    {
        var names = typeof(Settings).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        EmployeeEqual(false, names.Contains("AdminPasswordSet"));
        EmployeeEqual(false, names.Contains("PasswordSalt"));
        EmployeeEqual(false, names.Contains("PasswordHash"));
        var methods = typeof(SettingsStore).GetMethods().Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        EmployeeEqual(false, methods.Contains("SetAdminPassword"));
        EmployeeEqual(false, methods.Contains("CheckAdminPassword"));
        EmployeeEqual(false, methods.Contains("RetireLegacyAdminPassword"));
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenReadWrite(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static long ScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EmployeeEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void EmployeeThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"expected {typeof(T).Name}");
    }
}

internal sealed class EmployeeTemporaryDirectory : IDisposable
{
    public EmployeeTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CYInvoiceEmployeeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
