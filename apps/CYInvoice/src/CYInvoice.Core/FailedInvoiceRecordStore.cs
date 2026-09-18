using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed class FailedInvoiceRecordStore
{
    private readonly string databasePath;

    public FailedInvoiceRecordStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
    }

    public int Delete(IEnumerable<InvoiceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var targets = records
            .GroupBy(record => record.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (targets.Length == 0) return 0;

        foreach (var record in targets)
        {
            if (!string.Equals(record.InvoiceState, InvoiceStates.Failed, StringComparison.Ordinal))
                throw new InvalidOperationException("只允許刪除開立失敗的本機紀錄");
            if (string.IsNullOrWhiteSpace(record.Id))
                throw new InvalidDataException("開立失敗紀錄缺少內部識別碼，無法安全刪除");
        }

        using var connection = Open();
        Configure(connection);
        using var transaction = connection.BeginTransaction();
        var deleted = 0;
        foreach (var record in targets)
        {
            var localId = FindFailedLocalId(connection, transaction, record.Id);
            DeleteMatchingIssueArtifacts(connection, transaction, record);

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM invoices WHERE local_id = $local_id AND invoice_state = $failed;";
            delete.Parameters.AddWithValue("$local_id", localId);
            delete.Parameters.AddWithValue("$failed", InvoiceStates.Failed);
            if (delete.ExecuteNonQuery() != 1)
                throw new InvalidDataException($"開立失敗紀錄 {record.Id} 未安全刪除");
            deleted++;
        }
        transaction.Commit();
        return deleted;
    }

    private static long FindFailedLocalId(SqliteConnection connection, SqliteTransaction transaction, string recordId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT local_id, invoice_state FROM invoices WHERE record_id = $record_id ORDER BY local_id;";
        command.Parameters.AddWithValue("$record_id", recordId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"invoice record {recordId} not found");
        var localId = reader.GetInt64(0);
        var state = reader.GetString(1);
        if (reader.Read()) throw new InvalidDataException($"invoice record ID {recordId} is duplicated");
        if (!string.Equals(state, InvoiceStates.Failed, StringComparison.Ordinal))
            throw new InvalidOperationException("只允許刪除開立失敗的本機紀錄");
        return localId;
    }

    private static void DeleteMatchingIssueArtifacts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InvoiceRecord record)
    {
        var sellerInvoice = record.SellerInvoice.Trim();
        if (sellerInvoice.Length == 0) return;
        var accountKey = record.Environment.Trim() + "|" + sellerInvoice;
        var invoiceNumber = record.InvoiceNumber.Trim();
        var orderIds = new[] { record.OriginalOrderId, record.OrderId, record.ApiOrderId }
            .Select(value => value.Trim())
            .Where(value => value.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var orderClauses = new List<string>();
        for (var index = 0; index < orderIds.Length; index++)
        {
            var name = "$order" + index.ToString(CultureInfo.InvariantCulture);
            orderClauses.Add("order_id = " + name);
            command.Parameters.AddWithValue(name, orderIds[index]);
        }
        var identityClauses = new List<string>();
        if (invoiceNumber.Length != 0)
        {
            identityClauses.Add("invoice_number = $invoice_number");
            command.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        }
        identityClauses.AddRange(orderClauses);
        if (identityClauses.Count == 0) return;

        command.CommandText = $"""
            DELETE FROM sync_issues
            WHERE account_key = $account_key
              AND ({string.Join(" OR ", identityClauses)});
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
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
}
