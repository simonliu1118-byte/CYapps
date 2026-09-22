using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed record CloudEmployeeCacheSeed(
    string EmployeeId,
    string EmployeeNo,
    string Name,
    string Email,
    string Role,
    bool Enabled,
    bool EmailVerified,
    string CredentialVerifier,
    int CredentialVersion,
    int Revision);

public sealed record CloudEmployeeCachedAccount(
    string EmployeeId,
    string EmployeeNo,
    string Name,
    string Email,
    string Role,
    bool Enabled,
    bool EmailVerified,
    int CredentialVersion,
    int Revision,
    DateTimeOffset SyncedUtc);

public sealed record CloudEmployeeCacheState(
    string WorkspaceId,
    int WorkspaceRevision,
    DateTimeOffset SyncedUtc);

/// <summary>
/// Local synchronized cache for Cloud Employee authority. This is not a second
/// account authority: in Cloud mode Cloud remains authoritative, while this
/// cache permits action-time authorization during temporary network loss.
/// Credential verifiers are additionally protected by the platform secret
/// protector before being written to SQLite.
/// </summary>
public sealed class CloudEmployeeCacheStore
{
    private const string PasswordAlgorithm = "pbkdf2-sha256";
    private readonly Lock gate = new();
    private readonly string databasePath;
    private readonly ISecretProtector protector;

    public CloudEmployeeCacheStore(string dataDirectory, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(protector);
        databasePath = SqliteBootstrapper.EnsureMigrated(dataDirectory).DatabasePath;
        this.protector = protector;
        EnsureSchema();
    }

    public CloudEmployeeCacheState? LoadState()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT workspace_id, workspace_revision, synced_utc
                FROM cloud_employee_cache_state
                WHERE singleton_id = 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new CloudEmployeeCacheState(
                reader.GetString(0),
                reader.GetInt32(1),
                ParseUtc(reader.GetString(2)));
        }
    }

    public IReadOnlyList<CloudEmployeeCachedAccount> LoadAll()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT employee_id, employee_no, name, email, role, enabled,
                       email_verified, credential_version, revision, synced_utc
                FROM cloud_employee_cache
                ORDER BY CASE role WHEN 'SUPER_ADMIN' THEN 0 WHEN 'ADMIN' THEN 1 ELSE 2 END,
                         employee_no;
                """;
            using var reader = command.ExecuteReader();
            var result = new List<CloudEmployeeCachedAccount>();
            while (reader.Read())
            {
                result.Add(new CloudEmployeeCachedAccount(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5) == 1,
                    reader.GetInt64(6) == 1,
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    ParseUtc(reader.GetString(9))));
            }
            return result;
        }
    }

    public CloudEmployeeCachedAccount? Authenticate(string employeeNo, string password)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        ArgumentNullException.ThrowIfNull(password);
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT employee_id, employee_no, name, email, role, enabled,
                       email_verified, credential_verifier_enc, credential_version,
                       revision, synced_utc
                FROM cloud_employee_cache
                WHERE employee_no = $employee_no;
                """;
            command.Parameters.AddWithValue("$employee_no", employeeNo);
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt64(5) != 1) return null;

            var verifier = UnprotectVerifier(reader.GetString(7));
            if (!VerifyPassword(password, verifier)) return null;
            return new CloudEmployeeCachedAccount(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Enabled: true,
                reader.GetInt64(6) == 1,
                reader.GetInt32(8),
                reader.GetInt32(9),
                ParseUtc(reader.GetString(10)));
        }
    }

    public void ReplaceSnapshot(string workspaceId, int workspaceRevision, IReadOnlyList<CloudEmployeeCacheSeed> employees)
    {
        workspaceId = NormalizeIdentifier(workspaceId, 80, "Workspace ID");
        if (workspaceRevision < 0) throw new InvalidOperationException("Workspace Employee revision 不可小於 0。");
        ArgumentNullException.ThrowIfNull(employees);
        if (employees.Count == 0) throw new InvalidOperationException("Cloud Employee 快取不可為空。");

        var normalized = employees.Select(NormalizeSeed).ToArray();
        if (normalized.Select(item => item.EmployeeId).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidOperationException("Cloud Employee ID 不可重複。");
        if (normalized.Select(item => item.EmployeeNo).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidOperationException("Cloud Employee 編號不可重複。");
        if (normalized.Count(item => item.Role == EmployeeRoles.SuperAdmin) != 1)
            throw new InvalidOperationException("Cloud Employee 快取必須且只能有一位 SUPER_ADMIN。");

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();

            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM cloud_employee_cache;";
                clear.ExecuteNonQuery();
            }

            foreach (var employee in normalized)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cloud_employee_cache (
                        employee_id, workspace_id, employee_no, name, email, role,
                        enabled, email_verified, credential_verifier_enc,
                        credential_version, revision, synced_utc
                    ) VALUES (
                        $employee_id, $workspace_id, $employee_no, $name, $email, $role,
                        $enabled, $email_verified, $credential_verifier_enc,
                        $credential_version, $revision, $synced_utc
                    );
                    """;
                insert.Parameters.AddWithValue("$employee_id", employee.EmployeeId);
                insert.Parameters.AddWithValue("$workspace_id", workspaceId);
                insert.Parameters.AddWithValue("$employee_no", employee.EmployeeNo);
                insert.Parameters.AddWithValue("$name", employee.Name);
                insert.Parameters.AddWithValue("$email", employee.Email);
                insert.Parameters.AddWithValue("$role", employee.Role);
                insert.Parameters.AddWithValue("$enabled", employee.Enabled ? 1 : 0);
                insert.Parameters.AddWithValue("$email_verified", employee.EmailVerified ? 1 : 0);
                insert.Parameters.AddWithValue("$credential_verifier_enc", ProtectVerifier(employee.CredentialVerifier));
                insert.Parameters.AddWithValue("$credential_version", employee.CredentialVersion);
                insert.Parameters.AddWithValue("$revision", employee.Revision);
                insert.Parameters.AddWithValue("$synced_utc", now);
                insert.ExecuteNonQuery();
            }

            using (var state = connection.CreateCommand())
            {
                state.Transaction = transaction;
                state.CommandText = """
                    INSERT INTO cloud_employee_cache_state (
                        singleton_id, workspace_id, workspace_revision, synced_utc
                    ) VALUES (1, $workspace_id, $workspace_revision, $synced_utc)
                    ON CONFLICT(singleton_id) DO UPDATE SET
                        workspace_id = excluded.workspace_id,
                        workspace_revision = excluded.workspace_revision,
                        synced_utc = excluded.synced_utc;
                    """;
                state.Parameters.AddWithValue("$workspace_id", workspaceId);
                state.Parameters.AddWithValue("$workspace_revision", workspaceRevision);
                state.Parameters.AddWithValue("$synced_utc", now);
                state.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM cloud_employee_cache; DELETE FROM cloud_employee_cache_state;";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private void EnsureSchema()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS cloud_employee_cache (
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

                CREATE TABLE IF NOT EXISTS cloud_employee_cache_state (
                    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                    workspace_id TEXT NOT NULL,
                    workspace_revision INTEGER NOT NULL CHECK (workspace_revision >= 0),
                    synced_utc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_cloud_employee_cache_role
                    ON cloud_employee_cache (role, enabled, employee_no);
                """;
            command.ExecuteNonQuery();
        }
    }

    private CloudEmployeeCacheSeed NormalizeSeed(CloudEmployeeCacheSeed value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var employeeId = NormalizeIdentifier(value.EmployeeId, 100, "Cloud Employee ID");
        var employeeNo = NormalizeEmployeeNo(value.EmployeeNo);
        var name = value.Name?.Trim() ?? string.Empty;
        var email = value.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var role = value.Role?.Trim().ToUpperInvariant() ?? string.Empty;
        if (name.Length is < 1 or > 120) throw new InvalidOperationException("Cloud Employee 姓名格式無效。");
        if (email.Length is < 3 or > 320 || !email.Contains('@')) throw new InvalidOperationException("Cloud Employee Email 格式無效。");
        if (!EmployeeRoles.IsValid(role)) throw new InvalidOperationException("Cloud Employee Role 格式無效。");
        if (!value.EmailVerified) throw new InvalidOperationException("Cloud Employee Email 尚未驗證，不可進入離線帳號快取。");
        if (value.CredentialVersion < 1 || value.Revision < 1) throw new InvalidOperationException("Cloud Employee 版本資訊格式無效。");
        if (!ValidPasswordVerifier(value.CredentialVerifier)) throw new InvalidOperationException("Cloud Employee 登入憑證格式無效。");
        return value with
        {
            EmployeeId = employeeId,
            EmployeeNo = employeeNo,
            Name = name,
            Email = email,
            Role = role,
        };
    }

    private string ProtectVerifier(string verifier) =>
        protector.Protect(Encoding.UTF8.GetBytes(verifier));

    private string UnprotectVerifier(string encrypted)
    {
        try
        {
            var verifier = Encoding.UTF8.GetString(protector.Unprotect(encrypted));
            if (!ValidPasswordVerifier(verifier)) throw new InvalidDataException("Cloud Employee 離線登入憑證格式無效。");
            return verifier;
        }
        catch (Exception error) when (error is not InvalidDataException)
        {
            throw new InvalidDataException("Cloud Employee 離線登入憑證無法解密。", error);
        }
    }

    private static bool VerifyPassword(string password, string verifier)
    {
        var parts = verifier.Split('$');
        if (parts.Length != 4 || parts[0] != PasswordAlgorithm
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 100_000
            || !TryHex(parts[2], out var salt) || salt.Length < 16
            || !TryHex(parts[3], out var expected) || expected.Length != 32)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool ValidPasswordVerifier(string value)
    {
        var parts = value.Split('$');
        return parts.Length == 4
            && parts[0] == PasswordAlgorithm
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            && iterations is >= 100_000 and <= 2_000_000
            && TryHex(parts[2], out var salt) && salt.Length is >= 16 and <= 64
            && TryHex(parts[3], out var hash) && hash.Length == 32;
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

    private static string NormalizeIdentifier(string value, int maximumLength, string field)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 || value.Length > maximumLength)
            throw new InvalidOperationException($"{field} 格式無效。");
        return value;
    }

    private static string NormalizeEmployeeNo(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length != 4 || !value.All(character => character is >= '0' and <= '9'))
            throw new InvalidOperationException("員工編號必須為 4 碼數字。");
        return value;
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
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE; PRAGMA synchronous = FULL;";
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidDataException("Cloud Employee 快取時間格式無效。");
        return parsed;
    }
}
