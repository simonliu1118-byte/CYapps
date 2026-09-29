using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

internal static class RoleVocabularyMigrationTests
{
    public static void LegacyEmployeeAndCloudCacheConstraintsMigrateToUser()
    {
        using var temporary = new EmployeeTemporaryDirectory();
        var bootstrap = SqliteBootstrapper.EnsureMigrated(temporary.Path);

        using (var connection = OpenReadWrite(bootstrap.DatabasePath))
        {
            Execute(connection, LegacyRoleSchemaSql);
        }

        var upgraded = SqliteBootstrapper.EnsureMigrated(temporary.Path);
        Equal(false, upgraded.Created);

        using (var connection = OpenReadWrite(upgraded.DatabasePath))
        {
            Equal("USER", ScalarText(connection, "SELECT role FROM employees WHERE employee_no = '3040';"));
            Equal("USER", ScalarText(connection, "SELECT role FROM cloud_employee_cache WHERE employee_no = '3040';"));
            Equal("workspace-old", ScalarText(connection, "SELECT workspace_id FROM cloud_employee_cache_state WHERE singleton_id = 1;"));
            Equal("USER", ScalarText(connection, "SELECT value FROM schema_info WHERE key = 'employee_role_vocabulary';"));
            Equal("USER", ScalarText(connection, "SELECT value FROM schema_info WHERE key = 'cloud_employee_cache_role_vocabulary';"));

            var employeeSql = ScalarText(connection, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'employees';");
            var cacheSql = ScalarText(connection, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'cloud_employee_cache';");
            Equal(false, employeeSql.Contains("'EMPLOYEE'", StringComparison.OrdinalIgnoreCase));
            Equal(false, cacheSql.Contains("'EMPLOYEE'", StringComparison.OrdinalIgnoreCase));
            Equal(true, employeeSql.Contains("'USER'", StringComparison.Ordinal));
            Equal(true, cacheSql.Contains("'USER'", StringComparison.Ordinal));

            Execute(connection, "INSERT INTO cloud_employee_cache (employee_id, workspace_id, employee_no, name, email, role, enabled, email_verified, credential_verifier_enc, credential_version, revision, synced_utc) VALUES ('employee-user-new', 'workspace-old', '3050', '新使用者', 'new@example.com', 'USER', 1, 1, 'protected-new', 1, 2, '2026-09-29T00:00:00.0000000+00:00');");
            Throws<SqliteException>(() => Execute(connection, "INSERT INTO cloud_employee_cache (employee_id, workspace_id, employee_no, name, email, role, enabled, email_verified, credential_verifier_enc, credential_version, revision, synced_utc) VALUES ('employee-legacy-new', 'workspace-old', '3060', '舊角色', 'legacy@example.com', 'EMPLOYEE', 1, 1, 'protected-legacy', 1, 2, '2026-09-29T00:00:00.0000000+00:00');"));
            Throws<SqliteException>(() => Execute(connection, "UPDATE employees SET role = 'ADMIN' WHERE employee_no = '3015';"));
        }

        var store = new EmployeeStore(temporary.Path);
        Equal(EmployeeRoles.User, store.Find("3040")?.Role);
        var created = store.CreateEmployee("3015", "3070", "升級後使用者", "upgrade@example.com", "Password8", EmployeeRoles.User);
        Equal(EmployeeRoles.User, created.Role);
    }

    private const string LegacyRoleSchemaSql = """
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

        INSERT INTO schema_info (key, value) VALUES ('employee_schema_version', '1');
        INSERT INTO employees (employee_no, name, email, password_hash, role, enabled, recovery_hash, created_utc, updated_utc)
        VALUES
            ('3015', 'Super Admin', 'super@example.com', 'legacy-super-hash', 'SUPER_ADMIN', 1, 'legacy-recovery-hash', '2026-09-29T00:00:00.0000000+00:00', '2026-09-29T00:00:00.0000000+00:00'),
            ('3040', 'Legacy Employee', 'employee@example.com', 'legacy-user-hash', 'EMPLOYEE', 1, '', '2026-09-29T00:00:00.0000000+00:00', '2026-09-29T00:00:00.0000000+00:00');

        CREATE TABLE cloud_employee_cache (
            employee_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            employee_no TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            email TEXT NOT NULL,
            role TEXT NOT NULL CHECK (role IN ('SUPER_ADMIN', 'ADMIN', 'EMPLOYEE')),
            enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
            email_verified INTEGER NOT NULL CHECK (email_verified IN (0, 1)),
            credential_verifier_enc TEXT NOT NULL,
            credential_version INTEGER NOT NULL CHECK (credential_version >= 1),
            revision INTEGER NOT NULL CHECK (revision >= 1),
            synced_utc TEXT NOT NULL,
            CHECK(length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*')
        );

        CREATE TABLE cloud_employee_cache_state (
            singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
            workspace_id TEXT NOT NULL,
            workspace_revision INTEGER NOT NULL CHECK (workspace_revision >= 0),
            synced_utc TEXT NOT NULL
        );

        CREATE INDEX idx_cloud_employee_cache_role
            ON cloud_employee_cache (role, enabled, employee_no);

        INSERT INTO cloud_employee_cache (employee_id, workspace_id, employee_no, name, email, role, enabled, email_verified, credential_verifier_enc, credential_version, revision, synced_utc)
        VALUES ('employee-user-old', 'workspace-old', '3040', 'Legacy Employee', 'employee@example.com', 'EMPLOYEE', 1, 1, 'protected-old', 1, 1, '2026-09-29T00:00:00.0000000+00:00');

        INSERT INTO cloud_employee_cache_state (singleton_id, workspace_id, workspace_revision, synced_utc)
        VALUES (1, 'workspace-old', 7, '2026-09-29T00:00:00.0000000+00:00');
        """;

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

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void Throws<T>(Action action) where T : Exception
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
