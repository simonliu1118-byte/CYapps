using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

internal static class RoleVocabularySchemaMigration
{
    public static void Ensure(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();

        MigrateEmployeesIfNeeded(connection, transaction);
        MigrateCloudEmployeeCacheIfNeeded(connection, transaction);

        transaction.Commit();
    }

    private static void MigrateEmployeesIfNeeded(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = TableSql(connection, transaction, "employees");
        if (sql is null || !ContainsLegacyEmployeeRole(sql)) return;

        Execute(connection, transaction, """
            CREATE TABLE employees_user_role_migration (
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
                CHECK(role IN ('SUPER_ADMIN', 'ADMIN', 'USER')),
                CHECK(enabled IN (0, 1)),
                CHECK(role <> 'SUPER_ADMIN' OR enabled = 1),
                CHECK((role = 'SUPER_ADMIN' AND length(recovery_hash) > 0) OR
                      (role <> 'SUPER_ADMIN' AND recovery_hash = ''))
            );

            INSERT INTO employees_user_role_migration (
                employee_no, name, email, password_hash, role, enabled,
                recovery_hash, created_utc, updated_utc
            )
            SELECT employee_no,
                   name,
                   email,
                   password_hash,
                   CASE role WHEN 'EMPLOYEE' THEN 'USER' ELSE role END,
                   enabled,
                   recovery_hash,
                   created_utc,
                   updated_utc
              FROM employees;

            DROP TABLE employees;
            ALTER TABLE employees_user_role_migration RENAME TO employees;

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
            """);

        UpsertMarker(connection, transaction, "employee_role_vocabulary", "USER");
    }

    private static void MigrateCloudEmployeeCacheIfNeeded(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = TableSql(connection, transaction, "cloud_employee_cache");
        if (sql is null || !ContainsLegacyEmployeeRole(sql)) return;

        Execute(connection, transaction, """
            CREATE TABLE cloud_employee_cache_user_role_migration (
                employee_id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                employee_no TEXT NOT NULL UNIQUE,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                role TEXT NOT NULL CHECK (role IN ('SUPER_ADMIN', 'ADMIN', 'USER')),
                enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
                email_verified INTEGER NOT NULL CHECK (email_verified IN (0, 1)),
                credential_verifier_enc TEXT NOT NULL,
                credential_version INTEGER NOT NULL CHECK (credential_version >= 1),
                revision INTEGER NOT NULL CHECK (revision >= 1),
                synced_utc TEXT NOT NULL,
                CHECK(length(employee_no) = 4 AND employee_no NOT GLOB '*[^0-9]*')
            );

            INSERT INTO cloud_employee_cache_user_role_migration (
                employee_id, workspace_id, employee_no, name, email, role,
                enabled, email_verified, credential_verifier_enc,
                credential_version, revision, synced_utc
            )
            SELECT employee_id,
                   workspace_id,
                   employee_no,
                   name,
                   email,
                   CASE role WHEN 'EMPLOYEE' THEN 'USER' ELSE role END,
                   enabled,
                   email_verified,
                   credential_verifier_enc,
                   credential_version,
                   revision,
                   synced_utc
              FROM cloud_employee_cache;

            DROP TABLE cloud_employee_cache;
            ALTER TABLE cloud_employee_cache_user_role_migration RENAME TO cloud_employee_cache;

            CREATE INDEX idx_cloud_employee_cache_role
                ON cloud_employee_cache (role, enabled, employee_no);
            """);

        UpsertMarker(connection, transaction, "cloud_employee_cache_role_vocabulary", "USER");
    }

    private static bool ContainsLegacyEmployeeRole(string sql) =>
        sql.Contains("'EMPLOYEE'", StringComparison.OrdinalIgnoreCase)
        || sql.Contains("\"EMPLOYEE\"", StringComparison.OrdinalIgnoreCase);

    private static string? TableSql(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() as string;
    }

    private static void UpsertMarker(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        if (TableSql(connection, transaction, "schema_info") is null) return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_info (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var configure = connection.CreateCommand();
        configure.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            """;
        configure.ExecuteNonQuery();
        return connection;
    }
}
