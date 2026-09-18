using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public static class EmployeeRoles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Admin = "ADMIN";
    public const string Employee = "EMPLOYEE";

    public static bool IsValid(string role) => role is SuperAdmin or Admin or Employee;
    public static bool CanManageAccounts(string role) => role is SuperAdmin or Admin;
}

public sealed record EmployeeAccount(
    string EmployeeNo,
    string Name,
    string Email,
    string Role,
    bool Enabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SuperAdminSetupResult(EmployeeAccount Employee, string RecoveryCode);

public sealed class EmployeeStore
{
    private const int EmployeeSchemaVersion = 1;
    private const int PasswordIterations = 210_000;
    private const string PasswordAlgorithm = "pbkdf2-sha256";
    private const string RecoveryAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private readonly Lock gate = new();
    private readonly string databasePath;

    public EmployeeStore(string dataDirectory)
    {
        databasePath = SqliteBootstrapper.EnsureMigrated(dataDirectory).DatabasePath;
        EnsureEmployeeSchema();
    }

    public bool HasEmployees()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            return CountEmployees(connection) != 0;
        }
    }

    public IReadOnlyList<EmployeeAccount> LoadAll()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT employee_no, name, email, role, enabled, created_utc, updated_utc
                FROM employees
                ORDER BY employee_no;
                """;
            using var reader = command.ExecuteReader();
            var result = new List<EmployeeAccount>();
            while (reader.Read()) result.Add(ReadAccount(reader));
            return result;
        }
    }

    public EmployeeAccount? Find(string employeeNo)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            return Find(connection, null, employeeNo);
        }
    }

    public EmployeeAccount? Authenticate(string employeeNo, string password)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        ArgumentNullException.ThrowIfNull(password);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT employee_no, name, email, password_hash, role, enabled, created_utc, updated_utc
                FROM employees
                WHERE employee_no = $employee_no;
                """;
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            if (reader.GetInt64(5) != 1) return null;
            if (!PasswordHash.Verify(password, reader.GetString(3))) return null;
            return ReadAccount(reader, passwordHashColumnPresent: true);
        }
    }

    public SuperAdminSetupResult CreateFirstSuperAdmin(
        string employeeNo,
        string name,
        string email,
        string password)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        name = NormalizeName(name);
        email = NormalizeEmail(email);
        RequirePassword(password);

        var passwordHash = PasswordHash.Create(password);
        var recoveryCode = RecoveryCode.Generate();
        var recoveryHash = PasswordHash.Create(recoveryCode);
        var now = UtcNowText();

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            if (CountEmployees(connection, transaction) != 0)
                throw new InvalidOperationException("第一位超級管理員只能在員工清單為空時建立");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO employees (
                    employee_no, name, email, password_hash, role, enabled,
                    recovery_hash, created_utc, updated_utc
                ) VALUES (
                    $employee_no, $name, $email, $password_hash, 'SUPER_ADMIN', 1,
                    $recovery_hash, $created_utc, $updated_utc
                );
                """;
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$password_hash", passwordHash);
            command.Parameters.AddWithValue("$recovery_hash", recoveryHash);
            command.Parameters.AddWithValue("$created_utc", now);
            command.Parameters.AddWithValue("$updated_utc", now);
            command.ExecuteNonQuery();
            transaction.Commit();

            return new SuperAdminSetupResult(
                new EmployeeAccount(
                    employeeNo,
                    name,
                    email,
                    EmployeeRoles.SuperAdmin,
                    Enabled: true,
                    ParseUtc(now),
                    ParseUtc(now)),
                recoveryCode);
        }
    }

    public EmployeeAccount CreateEmployee(
        string actorEmployeeNo,
        string employeeNo,
        string name,
        string email,
        string password,
        string role = EmployeeRoles.Employee)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        employeeNo = NormalizeEmployeeNo(employeeNo);
        name = NormalizeName(name);
        email = NormalizeEmail(email);
        RequirePassword(password);
        if (role is not EmployeeRoles.Employee and not EmployeeRoles.Admin)
            throw new InvalidOperationException("新增員工只能指定一般員工或管理員權限");

        var now = UtcNowText();
        var passwordHash = PasswordHash.Create(password);
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            RequireManager(connection, transaction, actorEmployeeNo);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO employees (
                    employee_no, name, email, password_hash, role, enabled,
                    recovery_hash, created_utc, updated_utc
                ) VALUES (
                    $employee_no, $name, $email, $password_hash, $role, 1,
                    '', $created_utc, $updated_utc
                );
                """;
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$password_hash", passwordHash);
            command.Parameters.AddWithValue("$role", role);
            command.Parameters.AddWithValue("$created_utc", now);
            command.Parameters.AddWithValue("$updated_utc", now);
            command.ExecuteNonQuery();
            transaction.Commit();
            return Find(employeeNo) ?? throw new InvalidDataException("新增員工後無法讀回資料");
        }
    }

    public void UpdateProfile(string actorEmployeeNo, string targetEmployeeNo, string name, string email)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        targetEmployeeNo = NormalizeEmployeeNo(targetEmployeeNo);
        name = NormalizeName(name);
        email = NormalizeEmail(email);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            var actor = RequireManager(connection, transaction, actorEmployeeNo);
            var target = RequireEmployee(connection, transaction, targetEmployeeNo);
            if (target.Role == EmployeeRoles.SuperAdmin && actor.EmployeeNo != target.EmployeeNo)
                throw new InvalidOperationException("其他管理員不得修改超級管理員資料");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE employees
                SET name = $name, email = $email, updated_utc = $updated_utc
                WHERE employee_no = $employee_no;
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$email", email);
            command.Parameters.AddWithValue("$updated_utc", UtcNowText());
            command.Parameters.AddWithValue("$employee_no", targetEmployeeNo);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public void SetRole(string actorEmployeeNo, string targetEmployeeNo, string role)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        targetEmployeeNo = NormalizeEmployeeNo(targetEmployeeNo);
        if (role is not EmployeeRoles.Employee and not EmployeeRoles.Admin)
            throw new InvalidOperationException("只能設定一般員工或管理員權限");

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            RequireManager(connection, transaction, actorEmployeeNo);
            var target = RequireEmployee(connection, transaction, targetEmployeeNo);
            if (target.Role == EmployeeRoles.SuperAdmin)
                throw new InvalidOperationException("超級管理員權限不可變更");
            if (actorEmployeeNo == targetEmployeeNo && target.Role == EmployeeRoles.Admin && role != EmployeeRoles.Admin)
                throw new InvalidOperationException("管理員不得取消自己的管理員權限");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE employees
                SET role = $role, updated_utc = $updated_utc
                WHERE employee_no = $employee_no;
                """;
            command.Parameters.AddWithValue("$role", role);
            command.Parameters.AddWithValue("$updated_utc", UtcNowText());
            command.Parameters.AddWithValue("$employee_no", targetEmployeeNo);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public void SetEnabled(string actorEmployeeNo, string targetEmployeeNo, bool enabled)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        targetEmployeeNo = NormalizeEmployeeNo(targetEmployeeNo);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            RequireManager(connection, transaction, actorEmployeeNo);
            var target = RequireEmployee(connection, transaction, targetEmployeeNo);
            if (target.Role == EmployeeRoles.SuperAdmin && !enabled)
                throw new InvalidOperationException("超級管理員不可停用");
            if (actorEmployeeNo == targetEmployeeNo && !enabled)
                throw new InvalidOperationException("管理員不得停用自己的帳號");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE employees
                SET enabled = $enabled, updated_utc = $updated_utc
                WHERE employee_no = $employee_no;
                """;
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$updated_utc", UtcNowText());
            command.Parameters.AddWithValue("$employee_no", targetEmployeeNo);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    public void ResetPasswordByAdministrator(
        string actorEmployeeNo,
        string targetEmployeeNo,
        string newPassword)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        targetEmployeeNo = NormalizeEmployeeNo(targetEmployeeNo);
        RequirePassword(newPassword);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            RequireManager(connection, transaction, actorEmployeeNo);
            var target = RequireEmployee(connection, transaction, targetEmployeeNo);
            if (target.Role == EmployeeRoles.SuperAdmin)
                throw new InvalidOperationException("超級管理員密碼只能由本人或復原碼重設");
            SetPasswordHash(connection, transaction, targetEmployeeNo, PasswordHash.Create(newPassword));
            transaction.Commit();
        }
    }

    public void ChangePassword(string employeeNo, string currentPassword, string newPassword)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        ArgumentNullException.ThrowIfNull(currentPassword);
        RequirePassword(newPassword);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            var credentials = ReadCredentials(connection, transaction, employeeNo);
            if (credentials is null || !credentials.Value.Enabled ||
                !PasswordHash.Verify(currentPassword, credentials.Value.PasswordHash))
            {
                throw new InvalidOperationException("員工編號或密碼錯誤");
            }
            SetPasswordHash(connection, transaction, employeeNo, PasswordHash.Create(newPassword));
            transaction.Commit();
        }
    }

    public string ResetSuperAdminPasswordWithRecoveryCode(
        string employeeNo,
        string recoveryCode,
        string newPassword)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        ArgumentNullException.ThrowIfNull(recoveryCode);
        RequirePassword(newPassword);

        var replacementCode = RecoveryCode.Generate();
        var replacementHash = PasswordHash.Create(replacementCode);
        var newPasswordHash = PasswordHash.Create(newPassword);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            var credentials = ReadCredentials(connection, transaction, employeeNo);
            if (credentials is null || credentials.Value.Role != EmployeeRoles.SuperAdmin ||
                !credentials.Value.Enabled || credentials.Value.RecoveryHash.Length == 0 ||
                !PasswordHash.Verify(recoveryCode.Trim(), credentials.Value.RecoveryHash))
            {
                throw new InvalidOperationException("超級管理員復原碼錯誤");
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE employees
                SET password_hash = $password_hash,
                    recovery_hash = $recovery_hash,
                    updated_utc = $updated_utc
                WHERE employee_no = $employee_no AND role = 'SUPER_ADMIN';
                """;
            command.Parameters.AddWithValue("$password_hash", newPasswordHash);
            command.Parameters.AddWithValue("$recovery_hash", replacementHash);
            command.Parameters.AddWithValue("$updated_utc", UtcNowText());
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("超級管理員密碼復原更新失敗");
            transaction.Commit();
            return replacementCode;
        }
    }

    public string RotateSuperAdminRecoveryCode(string employeeNo, string password)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        ArgumentNullException.ThrowIfNull(password);
        var replacementCode = RecoveryCode.Generate();
        var replacementHash = PasswordHash.Create(replacementCode);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            var credentials = ReadCredentials(connection, transaction, employeeNo);
            if (credentials is null || credentials.Value.Role != EmployeeRoles.SuperAdmin ||
                !credentials.Value.Enabled || !PasswordHash.Verify(password, credentials.Value.PasswordHash))
            {
                throw new InvalidOperationException("員工編號或密碼錯誤");
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE employees
                SET recovery_hash = $recovery_hash, updated_utc = $updated_utc
                WHERE employee_no = $employee_no AND role = 'SUPER_ADMIN';
                """;
            command.Parameters.AddWithValue("$recovery_hash", replacementHash);
            command.Parameters.AddWithValue("$updated_utc", UtcNowText());
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("超級管理員復原碼更新失敗");
            transaction.Commit();
            return replacementCode;
        }
    }

    public void DeleteEmployee(string actorEmployeeNo, string targetEmployeeNo)
    {
        actorEmployeeNo = NormalizeEmployeeNo(actorEmployeeNo);
        targetEmployeeNo = NormalizeEmployeeNo(targetEmployeeNo);

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            RequireManager(connection, transaction, actorEmployeeNo);
            var target = RequireEmployee(connection, transaction, targetEmployeeNo);
            if (target.Role == EmployeeRoles.SuperAdmin)
                throw new InvalidOperationException("超級管理員不可刪除");
            if (actorEmployeeNo == targetEmployeeNo)
                throw new InvalidOperationException("管理員不得刪除自己的帳號");

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM employees WHERE employee_no = $employee_no;";
            command.Parameters.AddWithValue("$employee_no", targetEmployeeNo);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private void EnsureEmployeeSchema()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();

            var existingVersion = ReadEmployeeSchemaVersion(connection, transaction);
            if (existingVersion is null)
            {
                if (DatabaseObjectExists(connection, transaction, "table", "employees"))
                    throw new InvalidDataException("CYInvoice.db 已有 employees 資料表但缺少 employee_schema_version，停止自動修復");
                CreateEmployeeSchema(connection, transaction);
                using var versionCommand = connection.CreateCommand();
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = "INSERT INTO schema_info (key, value) VALUES ('employee_schema_version', $version);";
                versionCommand.Parameters.AddWithValue("$version", EmployeeSchemaVersion.ToString(CultureInfo.InvariantCulture));
                versionCommand.ExecuteNonQuery();
            }
            else if (existingVersion != EmployeeSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported employee schema version: {existingVersion}");
            }

            ValidateEmployeeSchema(connection, transaction);
            transaction.Commit();
        }
    }

    private static void CreateEmployeeSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE employees (
                employee_no TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL DEFAULT '',
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                recovery_hash TEXT NOT NULL DEFAULT '',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                CHECK(length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*'),
                CHECK(length(trim(name)) > 0),
                CHECK(length(password_hash) > 0),
                CHECK(role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
                CHECK(enabled IN (0, 1)),
                CHECK(role <> 'SUPER_ADMIN' OR enabled = 1),
                CHECK((role = 'SUPER_ADMIN' AND length(recovery_hash) > 0) OR
                      (role <> 'SUPER_ADMIN' AND recovery_hash = ''))
            );

            CREATE UNIQUE INDEX ux_employees_one_super_admin
                ON employees(role)
                WHERE role = 'SUPER_ADMIN';

            CREATE TRIGGER trg_employees_protect_super_admin_update
            BEFORE UPDATE OF role, enabled ON employees
            WHEN OLD.role = 'SUPER_ADMIN'
                 AND (NEW.role <> 'SUPER_ADMIN' OR NEW.enabled <> 1)
            BEGIN
                SELECT RAISE(ABORT, 'super administrator cannot be demoted or disabled');
            END;

            CREATE TRIGGER trg_employees_protect_super_admin_delete
            BEFORE DELETE ON employees
            WHEN OLD.role = 'SUPER_ADMIN'
            BEGIN
                SELECT RAISE(ABORT, 'super administrator cannot be deleted');
            END;
            """;
        command.ExecuteNonQuery();
    }

    private static void ValidateEmployeeSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var (type, name) in new[]
        {
            ("table", "employees"),
            ("index", "ux_employees_one_super_admin"),
            ("trigger", "trg_employees_protect_super_admin_update"),
            ("trigger", "trg_employees_protect_super_admin_delete"),
        })
        {
            if (!DatabaseObjectExists(connection, transaction, type, name))
                throw new InvalidDataException($"CYInvoice.db employee schema is missing {type} {name}");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM employees WHERE role = 'SUPER_ADMIN';";
        var superAdminCount = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (superAdminCount > 1)
            throw new InvalidDataException("CYInvoice.db contains more than one super administrator");
    }

    private static int? ReadEmployeeSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM schema_info WHERE key = 'employee_schema_version';";
        var raw = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            throw new InvalidDataException($"Invalid employee schema version: {raw}");
        return version;
    }

    private static bool DatabaseObjectExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string type,
        string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = $type AND name = $name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private EmployeeAccount RequireManager(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string employeeNo)
    {
        var account = RequireEmployee(connection, transaction, employeeNo);
        if (!account.Enabled || !EmployeeRoles.CanManageAccounts(account.Role))
            throw new InvalidOperationException("此員工沒有帳戶管理權限");
        return account;
    }

    private EmployeeAccount RequireEmployee(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string employeeNo) =>
        Find(connection, transaction, employeeNo) ?? throw new InvalidOperationException("找不到指定員工");

    private static EmployeeAccount? Find(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string employeeNo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT employee_no, name, email, role, enabled, created_utc, updated_utc
            FROM employees
            WHERE employee_no = $employee_no;
            """;
        command.Parameters.AddWithValue("$employee_no", employeeNo);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAccount(reader) : null;
    }

    private static EmployeeAccount ReadAccount(SqliteDataReader reader, bool passwordHashColumnPresent = false)
    {
        var roleIndex = passwordHashColumnPresent ? 4 : 3;
        var enabledIndex = passwordHashColumnPresent ? 5 : 4;
        var createdIndex = passwordHashColumnPresent ? 6 : 5;
        var updatedIndex = passwordHashColumnPresent ? 7 : 6;
        return new EmployeeAccount(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(roleIndex),
            reader.GetInt64(enabledIndex) == 1,
            ParseUtc(reader.GetString(createdIndex)),
            ParseUtc(reader.GetString(updatedIndex)));
    }

    private static (string PasswordHash, string RecoveryHash, string Role, bool Enabled)? ReadCredentials(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string employeeNo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT password_hash, recovery_hash, role, enabled
            FROM employees
            WHERE employee_no = $employee_no;
            """;
        command.Parameters.AddWithValue("$employee_no", employeeNo);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1);
    }

    private static void SetPasswordHash(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string employeeNo,
        string passwordHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE employees
            SET password_hash = $password_hash, updated_utc = $updated_utc
            WHERE employee_no = $employee_no;
            """;
        command.Parameters.AddWithValue("$password_hash", passwordHash);
        command.Parameters.AddWithValue("$updated_utc", UtcNowText());
        command.Parameters.AddWithValue("$employee_no", employeeNo);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidDataException("員工密碼更新失敗");
    }

    private static long CountEmployees(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM employees;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private SqliteConnection Open(SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void ConfigureWritableConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            """;
        command.ExecuteNonQuery();
    }

    private static string NormalizeEmployeeNo(string employeeNo)
    {
        employeeNo = employeeNo?.Trim() ?? string.Empty;
        if (employeeNo.Length != 4 || !employeeNo.All(character => character is >= '0' and <= '9'))
            throw new InvalidOperationException("員工編號必須為 4 碼數字");
        return employeeNo;
    }

    private static string NormalizeName(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new InvalidOperationException("員工姓名不可空白");
        return name;
    }

    private static string NormalizeEmail(string email) => email?.Trim() ?? string.Empty;

    private static void RequirePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length == 0) throw new InvalidOperationException("密碼不可空白");
    }

    private static string UtcNowText() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new InvalidDataException("CYInvoice.db contains an invalid employee timestamp");
        }
        return parsed;
    }

    private static class PasswordHash
    {
        public static string Create(string value)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(value),
                salt,
                PasswordIterations,
                HashAlgorithmName.SHA256,
                32);
            return string.Join(
                '$',
                PasswordAlgorithm,
                PasswordIterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToHexString(salt).ToLowerInvariant(),
                Convert.ToHexString(hash).ToLowerInvariant());
        }

        public static bool Verify(string value, string encoded)
        {
            var parts = encoded.Split('$');
            if (parts.Length != 4 || parts[0] != PasswordAlgorithm ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
                iterations < 100_000 ||
                !TryHex(parts[2], out var salt) || salt.Length < 16 ||
                !TryHex(parts[3], out var expected) || expected.Length != 32)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(value),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static bool TryHex(string value, out byte[] bytes)
        {
            try
            {
                bytes = Convert.FromHexString(value);
                return true;
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
        }
    }

    private static class RecoveryCode
    {
        public static string Generate()
        {
            Span<char> raw = stackalloc char[20];
            for (var index = 0; index < raw.Length; index++)
                raw[index] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            return $"CYR-{new string(raw[..4])}-{new string(raw[4..8])}-{new string(raw[8..12])}-{new string(raw[12..16])}-{new string(raw[16..20])}";
        }
    }
}
