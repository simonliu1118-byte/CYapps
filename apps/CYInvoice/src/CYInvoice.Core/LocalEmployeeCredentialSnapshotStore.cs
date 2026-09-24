using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

/// <summary>
/// Reads only the existing one-way password verifiers needed for the one-time
/// Local -> Cloud Employee authority cutover. Password plaintext and Local
/// SUPER_ADMIN recovery hashes are never exposed by this reader.
/// </summary>
public sealed record LocalEmployeeCredentialSnapshot(string EmployeeNo, string PasswordVerifier);

public sealed class LocalEmployeeCredentialSnapshotStore
{
    private readonly string databasePath;

    public LocalEmployeeCredentialSnapshotStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("Data directory is required.", nameof(dataDirectory));
        databasePath = SqliteBootstrapper.EnsureMigrated(dataDirectory).DatabasePath;
    }

    public IReadOnlyList<LocalEmployeeCredentialSnapshot> LoadAll()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT employee_no, password_hash
            FROM employees
            ORDER BY employee_no;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<LocalEmployeeCredentialSnapshot>();
        while (reader.Read())
        {
            var employeeNo = reader.GetString(0);
            var verifier = reader.GetString(1);
            if (!ValidEmployeeNo(employeeNo) || !ValidPasswordVerifier(verifier))
                throw new InvalidDataException($"員工 {employeeNo} 的本機登入憑證格式無法安全轉換至 Cloud。");
            result.Add(new LocalEmployeeCredentialSnapshot(employeeNo, verifier));
        }
        return result;
    }

    public string GetRequiredVerifier(string employeeNo)
    {
        employeeNo = employeeNo?.Trim() ?? string.Empty;
        if (!ValidEmployeeNo(employeeNo)) throw new InvalidOperationException("員工編號必須為 4 碼數字");
        var match = LoadAll().SingleOrDefault(item => item.EmployeeNo == employeeNo);
        return match?.PasswordVerifier
            ?? throw new InvalidOperationException($"找不到員工 {employeeNo} 的本機登入憑證。");
    }

    private static bool ValidEmployeeNo(string value) =>
        value.Length == 4 && value.All(character => character is >= '0' and <= '9');

    private static bool ValidPasswordVerifier(string value)
    {
        var parts = value.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 100_000 || iterations > 2_000_000) return false;
        return IsHex(parts[2], minimumLength: 32, maximumLength: 128)
            && parts[2].Length % 2 == 0
            && IsHex(parts[3], minimumLength: 64, maximumLength: 64);
    }

    private static bool IsHex(string value, int minimumLength, int maximumLength) =>
        value.Length >= minimumLength
        && value.Length <= maximumLength
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
