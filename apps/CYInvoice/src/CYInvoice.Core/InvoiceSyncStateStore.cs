using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed class InvoiceSyncStateStore
{
    private readonly string databasePath;
    private readonly Lock gate = new();

    public InvoiceSyncStateStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("CYInvoice.db does not exist", databasePath);
    }

    public DateTimeOffset? LastSuccess(string accountKey, string scope)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        scope = Required(scope, nameof(scope));
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT last_success_utc
                FROM sync_state
                WHERE account_key = $account_key AND scope = $scope;
                """;
            command.Parameters.AddWithValue("$account_key", accountKey);
            command.Parameters.AddWithValue("$scope", scope);
            var raw = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                throw new InvalidDataException("CYInvoice.db contains an invalid sync_state timestamp");
            return parsed;
        }
    }

    public void SetLastSuccess(string accountKey, string scope, DateTimeOffset value)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        scope = Required(scope, nameof(scope));
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sync_state (account_key, scope, last_success_utc)
                VALUES ($account_key, $scope, $last_success_utc)
                ON CONFLICT(account_key, scope) DO UPDATE SET
                    last_success_utc = excluded.last_success_utc;
                """;
            command.Parameters.AddWithValue("$account_key", accountKey);
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$last_success_utc", value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidDataException("sync_state update did not affect exactly one row");
        }
    }

    private SqliteConnection Open(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        if (mode != SqliteOpenMode.ReadOnly)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = FULL;";
            command.ExecuteNonQuery();
        }
        return connection;
    }

    private static string Required(string value, string name)
    {
        value = value.Trim();
        return value.Length != 0 ? value : throw new ArgumentException("value is required", name);
    }
}
