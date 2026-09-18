using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public static class InvoiceSyncIssueTypes
{
    public const string InvoiceListFailed = "invoice_list_failed";
    public const string QueryFailed = "query_failed";
    public const string RemoteNotFound = "remote_not_found";
    public const string UnknownRemoteNotFound = "unknown_remote_not_found";
    public const string ReconciliationFailed = "reconciliation_failed";
    public const string AmbiguousMatch = "ambiguous_match";
    public const string LocalWriteFailed = "local_write_failed";
}

public sealed record InvoiceSyncIssue(
    long Id,
    string AccountKey,
    string InvoiceNumber,
    string OrderId,
    string IssueType,
    string Message,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ResolvedUtc);

public sealed class InvoiceSyncIssueStore
{
    private readonly string databasePath;

    public InvoiceSyncIssueStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
    }

    public IReadOnlyList<InvoiceSyncIssue> Unresolved(string accountKey)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        using var connection = Open(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT local_id, account_key, invoice_number, order_id, issue_type,
                   message, created_utc, resolved_utc
            FROM sync_issues
            WHERE account_key = $account_key AND TRIM(resolved_utc) = ''
            ORDER BY local_id;
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        return Read(command);
    }

    public bool HasUnresolved(
        string accountKey,
        string invoiceNumber,
        string orderId,
        params string[] issueTypes)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        invoiceNumber = invoiceNumber.Trim();
        orderId = orderId.Trim();
        if (issueTypes.Length == 0) return false;
        var types = issueTypes.Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.Ordinal).ToArray();
        if (types.Length == 0) return false;

        using var connection = Open(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        var typeParameters = AddTypes(command, types);
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM sync_issues
            WHERE account_key = $account_key
              AND TRIM(resolved_utc) = ''
              AND issue_type IN ({typeParameters})
              AND (
                    ($invoice_number <> '' AND invoice_number = $invoice_number)
                 OR ($order_id <> '' AND order_id = $order_id)
              );
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        command.Parameters.AddWithValue("$order_id", orderId);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    public long Record(
        string accountKey,
        string invoiceNumber,
        string orderId,
        string issueType,
        string message,
        DateTimeOffset createdUtc)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        issueType = Required(issueType, nameof(issueType));
        invoiceNumber = invoiceNumber.Trim();
        orderId = orderId.Trim();
        message = message.Trim();
        if (message.Length == 0) message = issueType;

        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var transaction = connection.BeginTransaction();

        long? existingId = null;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT local_id
                FROM sync_issues
                WHERE account_key = $account_key
                  AND invoice_number = $invoice_number
                  AND order_id = $order_id
                  AND issue_type = $issue_type
                  AND TRIM(resolved_utc) = ''
                ORDER BY local_id;
                """;
            find.Parameters.AddWithValue("$account_key", accountKey);
            find.Parameters.AddWithValue("$invoice_number", invoiceNumber);
            find.Parameters.AddWithValue("$order_id", orderId);
            find.Parameters.AddWithValue("$issue_type", issueType);
            using var reader = find.ExecuteReader();
            if (reader.Read()) existingId = reader.GetInt64(0);
            if (reader.Read()) throw new InvalidDataException("sync_issues contains duplicate unresolved issue identity");
        }

        if (existingId is not null)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE sync_issues SET message = $message WHERE local_id = $id;";
            update.Parameters.AddWithValue("$message", message);
            update.Parameters.AddWithValue("$id", existingId.Value);
            if (update.ExecuteNonQuery() != 1) throw new InvalidDataException("sync issue update did not affect exactly one row");
            transaction.Commit();
            return existingId.Value;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO sync_issues (
                account_key, invoice_number, order_id, issue_type, message, created_utc, resolved_utc
            ) VALUES (
                $account_key, $invoice_number, $order_id, $issue_type, $message, $created_utc, ''
            );
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$account_key", accountKey);
        insert.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        insert.Parameters.AddWithValue("$order_id", orderId);
        insert.Parameters.AddWithValue("$issue_type", issueType);
        insert.Parameters.AddWithValue("$message", message);
        insert.Parameters.AddWithValue("$created_utc", createdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        transaction.Commit();
        return id;
    }

    public int ResolveMatching(
        string accountKey,
        string invoiceNumber,
        string orderId,
        DateTimeOffset resolvedUtc,
        params string[] issueTypes)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        invoiceNumber = invoiceNumber.Trim();
        orderId = orderId.Trim();
        var types = issueTypes.Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.Ordinal).ToArray();
        if (types.Length == 0 || (invoiceNumber.Length == 0 && orderId.Length == 0)) return 0;

        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var command = connection.CreateCommand();
        var typeParameters = AddTypes(command, types);
        command.CommandText = $"""
            UPDATE sync_issues
            SET resolved_utc = $resolved_utc
            WHERE account_key = $account_key
              AND TRIM(resolved_utc) = ''
              AND issue_type IN ({typeParameters})
              AND (
                    ($invoice_number <> '' AND invoice_number = $invoice_number)
                 OR ($order_id <> '' AND order_id = $order_id)
              );
            """;
        command.Parameters.AddWithValue("$resolved_utc", resolvedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        command.Parameters.AddWithValue("$order_id", orderId);
        return command.ExecuteNonQuery();
    }

    public int ResolveAccountType(string accountKey, string issueType, DateTimeOffset resolvedUtc)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        issueType = Required(issueType, nameof(issueType));
        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sync_issues
            SET resolved_utc = $resolved_utc
            WHERE account_key = $account_key
              AND issue_type = $issue_type
              AND TRIM(resolved_utc) = '';
            """;
        command.Parameters.AddWithValue("$resolved_utc", resolvedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$issue_type", issueType);
        return command.ExecuteNonQuery();
    }

    public void Resolve(long id, DateTimeOffset resolvedUtc)
    {
        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sync_issues SET resolved_utc = $resolved_utc WHERE local_id = $id;";
        command.Parameters.AddWithValue("$resolved_utc", resolvedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"sync issue {id} not found");
    }

    public void Delete(long id)
    {
        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sync_issues WHERE local_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"sync issue {id} not found");
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
        return connection;
    }

    private static void Configure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = FULL;
            """;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<InvoiceSyncIssue> Read(SqliteCommand command)
    {
        var issues = new List<InvoiceSyncIssue>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var createdText = reader.GetString(6);
            if (!DateTimeOffset.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                throw new InvalidDataException("sync_issues contains invalid created_utc");
            DateTimeOffset? resolved = null;
            var resolvedText = reader.GetString(7).Trim();
            if (resolvedText.Length != 0)
            {
                if (!DateTimeOffset.TryParse(resolvedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    throw new InvalidDataException("sync_issues contains invalid resolved_utc");
                resolved = parsed;
            }
            issues.Add(new InvoiceSyncIssue(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), created, resolved));
        }
        return issues;
    }

    private static string AddTypes(SqliteCommand command, IReadOnlyList<string> types)
    {
        var names = new string[types.Count];
        for (var index = 0; index < types.Count; index++)
        {
            names[index] = "$type" + index.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(names[index], types[index]);
        }
        return string.Join(", ", names);
    }

    private static string Required(string value, string parameterName)
    {
        value = value.Trim();
        if (value.Length == 0) throw new ArgumentException("value is required", parameterName);
        return value;
    }
}
