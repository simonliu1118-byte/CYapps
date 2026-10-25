using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public static class InvoiceSyncIssueTypes
{
    public const string InvoiceListFailed = "invoice_list_failed";
    public const string AllowanceLimitExceeded = "allowance_limit_exceeded";
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
    private const string DeprecatedVoidPendingConfirmationIssueType = "void_pending_confirmation";
    private readonly string databasePath;
    private readonly string dataDirectory;

    public InvoiceSyncIssueStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = dataDirectory;
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
    }

    public IReadOnlyList<InvoiceSyncIssue> All(string accountKey)
    {
        accountKey = Required(accountKey, nameof(accountKey));
        using var connection = Open(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT local_id, account_key, invoice_number, order_id, issue_type,
                   message, created_utc, resolved_utc
            FROM sync_issues
            WHERE account_key = $account_key
              AND issue_type <> $deprecated_void_pending
            ORDER BY local_id DESC;
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$deprecated_void_pending", DeprecatedVoidPendingConfirmationIssueType);
        return Read(command);
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
            WHERE account_key = $account_key
              AND TRIM(resolved_utc) = ''
              AND issue_type <> $deprecated_void_pending
            ORDER BY local_id;
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$deprecated_void_pending", DeprecatedVoidPendingConfirmationIssueType);
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
        var types = issueTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Where(type => !string.Equals(type, DeprecatedVoidPendingConfirmationIssueType, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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
        DateTimeOffset createdUtc, Exception? error = null)
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
            WriteDiagnostic(connection, existingId.Value, "updated", issueType, message, error);
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
        WriteDiagnostic(connection, id, "created", issueType, message, error);
        return id;
    }

    private void WriteDiagnostic(SqliteConnection db,long id,string stage,string type,string message,Exception? error)
    {
        var saved=false;
        try
        {
            if(error is not null){IssueDiagnosticLog.Record(dataDirectory,id,stage,type,error:error);saved=true;}
            else saved=IssueDiagnosticLog.TryAppend(dataDirectory,id,stage,type,message);
        }
        catch(Exception failure)when(failure is IOException or UnauthorizedAccessException){ }
        if(saved)return;
        using var warning=db.CreateCommand();warning.CommandText="UPDATE sync_issues SET message=$message WHERE local_id=$id";
        warning.Parameters.AddWithValue("$id",id);
        warning.Parameters.AddWithValue("$message",message+"\n診斷寫入失敗，請保留目前訊息並檢查儲存空間／權限。");
        warning.ExecuteNonQuery();
    }

    public void ApplyCloudAllowance(string accountKey, string applicationId, string operation,
        string state, string status, string error, string? previousAccountKey = null)
    {
        var type = "cloud_allowance_" + (operation == "void" ? "void_" : "issue_") +
            (state == "problem" ? "problem" : "processing");
        InvoiceSyncIssue? existing;
        using (var lookup = Open(SqliteOpenMode.ReadOnly))
        using (var query = lookup.CreateCommand())
        {
            query.CommandText = """
                SELECT local_id,account_key,invoice_number,order_id,issue_type,message,created_utc,resolved_utc
                FROM sync_issues WHERE account_key=$account AND order_id=$application
                  AND issue_type IN ($processing,$problem) ORDER BY local_id DESC LIMIT 1
                """;
            var prefix = "cloud_allowance_" + (operation == "void" ? "void_" : "issue_");
            query.Parameters.AddWithValue("$account", accountKey);
            query.Parameters.AddWithValue("$application", applicationId);
            query.Parameters.AddWithValue("$processing", prefix + "processing");
            query.Parameters.AddWithValue("$problem", prefix + "problem");
            existing = Read(query).FirstOrDefault();
        }
        if (state == "completed" || status is "withdrawn" or "rejected")
        {
            if (existing?.ResolvedUtc is null && existing is not null) Resolve(existing.Id, DateTimeOffset.UtcNow);
        }
        else
        {
            var message = status is "open" or "review" ? "等待主管核准" :
                state == "problem" ? "自動處理遇到問題：" + error : "主管已核准，系統自動處理中";
            if (existing is null)
            {
                Record(accountKey, "", applicationId, type, message, DateTimeOffset.UtcNow);
            }
            else
            {
                using var db = Open(SqliteOpenMode.ReadWrite);
                Configure(db);
                using var sql = db.CreateCommand();
                sql.CommandText = "UPDATE sync_issues SET issue_type=$type,message=$message WHERE local_id=$id AND TRIM(resolved_utc)='' AND (issue_type<>$type OR message<>$message)";
                sql.Parameters.AddWithValue("$id", existing.Id);
                sql.Parameters.AddWithValue("$type", type);
                sql.Parameters.AddWithValue("$message", message);
                sql.ExecuteNonQuery();
            }
        }
        // Earlier runtime projections omitted environment. Resolve only this exact
        // Cloud operation in the other current account; retain its historical row.
        if (!string.IsNullOrEmpty(previousAccountKey) && previousAccountKey != accountKey)
        {
            using var staleDb = Open(SqliteOpenMode.ReadWrite);
            Configure(staleDb);
            using var stale = staleDb.CreateCommand();
            stale.CommandText = """
                UPDATE sync_issues SET resolved_utc=$now
                WHERE account_key=$previous AND order_id=$application
                  AND issue_type IN ($processing,$problem) AND TRIM(resolved_utc)=''
                """;
            var prefix = "cloud_allowance_" + (operation == "void" ? "void_" : "issue_");
            stale.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            stale.Parameters.AddWithValue("$previous", previousAccountKey);
            stale.Parameters.AddWithValue("$application", applicationId);
            stale.Parameters.AddWithValue("$processing", prefix + "processing");
            stale.Parameters.AddWithValue("$problem", prefix + "problem");
            stale.ExecuteNonQuery();
        }
    }

    public void UpdateMessage(long issueId, string message)
    {
        using var connection = Open(SqliteOpenMode.ReadWrite);
        Configure(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sync_issues SET message=$message WHERE local_id=$id AND TRIM(resolved_utc)=''";
        if (!IssueDiagnosticLog.TryAppend(dataDirectory, issueId, "updated", "sync_issue", message))
            message += "\n診斷紀錄寫入失敗，請保留目前錯誤內容並檢查儲存空間／權限。";
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$id", issueId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("原問題已結案或不存在，不能更新其他操作");
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
              )
            RETURNING local_id, issue_type;
            """;
        command.Parameters.AddWithValue("$resolved_utc", resolvedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        command.Parameters.AddWithValue("$order_id", orderId);
        return ReadResolvedWithDiagnostics(command);
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
              AND TRIM(resolved_utc) = ''
            RETURNING local_id, issue_type;
            """;
        command.Parameters.AddWithValue("$resolved_utc", resolvedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$issue_type", issueType);
        return ReadResolvedWithDiagnostics(command);
    }

    // The database write succeeds independently of best-effort local logs.
    // RETURNING avoids an extra table scan and records exact changed IDs.
    private int ReadResolvedWithDiagnostics(SqliteCommand command)
    {
        var changed = new List<(long Id, string Type)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                changed.Add((reader.GetInt64(0), reader.GetString(1)));
        foreach (var (id, type) in changed)
            IssueDiagnosticLog.TryAppend(dataDirectory, id, "resolved", type);
        return changed.Count;
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
        IssueDiagnosticLog.TryAppend(dataDirectory, id, "resolved", "sync_issue");
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