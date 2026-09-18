using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed class BuyerNameStore
{
    private readonly Lock gate = new();
    private readonly string databasePath;

    public BuyerNameStore(string dataDirectory)
    {
        databasePath = SqliteBootstrapper.EnsureMigrated(dataDirectory).DatabasePath;
    }

    public IReadOnlyDictionary<string, string> LoadOrCreate()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ban, name FROM buyer_names ORDER BY ban;";
            using var reader = command.ExecuteReader();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read()) names.Add(reader.GetString(0), reader.GetString(1));
            Validate(names);
            return names;
        }
    }

    public bool TryLookup(string ban, out string name)
    {
        ban = ban.Trim();
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM buyer_names WHERE ban = $ban;";
            command.Parameters.AddWithValue("$ban", ban);
            var value = command.ExecuteScalar();
            if (value is null)
            {
                name = string.Empty;
                return false;
            }
            name = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("CYInvoice.db contains an invalid buyer name");
            return true;
        }
    }

    public bool RememberAfterSuccessfulInvoice(
        string ban,
        bool lookupSucceeded,
        string apiName,
        string manualName,
        bool invoiceSucceeded)
    {
        ban = ban.Trim();
        manualName = manualName.Trim();
        if (!lookupSucceeded || apiName.Trim().Length != 0 || !invoiceSucceeded || manualName.Length == 0) return false;
        if (ban.Length != 8 || !ban.All(character => character is >= '0' and <= '9'))
            throw new InvalidDataException("公司統編必須為 8 碼");

        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using (var existing = connection.CreateCommand())
            {
                existing.CommandText = "SELECT name FROM buyer_names WHERE ban = $ban;";
                existing.Parameters.AddWithValue("$ban", ban);
                var value = Convert.ToString(existing.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(value, manualName, StringComparison.Ordinal)) return false;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO buyer_names (ban, name) VALUES ($ban, $name)
                ON CONFLICT(ban) DO UPDATE SET name = excluded.name;
                """;
            command.Parameters.AddWithValue("$ban", ban);
            command.Parameters.AddWithValue("$name", manualName);
            command.ExecuteNonQuery();
            return true;
        }
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

    private static void Validate(IReadOnlyDictionary<string, string> names)
    {
        if (names.Any(item => item.Key.Length != 8 ||
                              !item.Key.All(character => character is >= '0' and <= '9') ||
                              string.IsNullOrWhiteSpace(item.Value)))
        {
            throw new InvalidDataException("CYInvoice.db contains an invalid buyer-name entry");
        }
    }
}
