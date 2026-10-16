using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed record ArchivedFailedInvoice(InvoiceRecord Record,DateTimeOffset ArchivedUtc);

public sealed class FailedInvoiceRecordStore
{
    private readonly string databasePath;

    public FailedInvoiceRecordStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);
    }

    public int Archive(IEnumerable<InvoiceRecord> records)
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
        EnsureArchiveSchema(connection,transaction);
        // Take the authoritative preimage from the active SQLite transaction,
        // not the potentially stale WinForms row passed by the caller.
        var preimage = InvoiceStore.LoadRecords(connection,transaction)
            .GroupBy(r=>r.Id,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var archived = 0;
        foreach (var record in targets)
        {
            var localId = FindFailedLocalId(connection, transaction, record.Id);
            if (!preimage.TryGetValue(record.Id,out var stored) || stored.Length!=1 ||
                stored[0].InvoiceState!=InvoiceStates.Failed)
                throw new InvalidDataException("開立失敗資料快照已變更，未執行封存");
            var authoritative=stored[0];
            using (var insert=connection.CreateCommand())
            {
                insert.Transaction=transaction;
                insert.CommandText = """
                    INSERT INTO archived_failed_invoices
                    (record_id,seller_invoice,environment,archived_utc,record_json)
                    VALUES ($id,$seller,$environment,$utc,$snapshot);
                    """;
                insert.Parameters.AddWithValue("$id",authoritative.Id);
                insert.Parameters.AddWithValue("$seller",authoritative.SellerInvoice);
                insert.Parameters.AddWithValue("$environment",authoritative.Environment);
                insert.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$snapshot",JsonSerializer.Serialize(authoritative));
                if(insert.ExecuteNonQuery()!=1)
                    throw new InvalidDataException("封存未保留完整失敗紀錄");
            }
            ResolveMatchingIssueArtifacts(connection,transaction,authoritative);
            using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM invoices WHERE local_id=$id AND invoice_state=$failed;";
            remove.Parameters.AddWithValue("$id",localId);
            remove.Parameters.AddWithValue("$failed",InvoiceStates.Failed);
            if(remove.ExecuteNonQuery()!=1)
                throw new InvalidDataException($"開立失敗紀錄 {authoritative.Id} 無法安全移出一般清單");
            archived++;
        }
        transaction.Commit();
        return archived;
    }

    public IReadOnlyList<ArchivedFailedInvoice> AllArchived(string environment,string sellerInvoice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(sellerInvoice);
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=databasePath,Mode=SqliteOpenMode.ReadOnly,
            Cache=SqliteCacheMode.Private,Pooling=false
        }.ToString());
        connection.Open();
        using(var exists=connection.CreateCommand()){
            exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='archived_failed_invoices';";
            if(Convert.ToInt64(exists.ExecuteScalar(),CultureInfo.InvariantCulture)==0)
                return [];
        }
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT record_json,archived_utc FROM archived_failed_invoices
            WHERE environment=$environment AND seller_invoice=$seller
            ORDER BY archived_utc DESC,record_id DESC;
            """;
        command.Parameters.AddWithValue("$environment",environment);
        command.Parameters.AddWithValue("$seller",sellerInvoice);
        using var reader=command.ExecuteReader();
        var rows=new List<ArchivedFailedInvoice>();
        while(reader.Read()){
            var record=JsonSerializer.Deserialize<InvoiceRecord>(reader.GetString(0))
                ?? throw new InvalidDataException("封存的失敗發票快照無法讀取");
            var at=DateTimeOffset.Parse(reader.GetString(1),CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            rows.Add(new(record,at));
        }
        return rows;
    }

    private static void EnsureArchiveSchema(SqliteConnection connection,SqliteTransaction transaction)
    {
        using var command=connection.CreateCommand();
        command.Transaction=transaction;
        // One archive table in the same CYInvoice.db. No extra database,
        // remote API, or migration of existing live invoice rows.
        command.CommandText="""
            CREATE TABLE IF NOT EXISTS archived_failed_invoices(
                record_id TEXT PRIMARY KEY,
                seller_invoice TEXT NOT NULL,
                environment TEXT NOT NULL,
                archived_utc TEXT NOT NULL,
                record_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_archived_failed_account
                ON archived_failed_invoices(environment,seller_invoice,archived_utc DESC);
            CREATE TRIGGER IF NOT EXISTS archived_failed_no_reinsert
            BEFORE INSERT ON invoices
            WHEN EXISTS(SELECT 1 FROM archived_failed_invoices
                WHERE record_id=NEW.record_id)
            BEGIN SELECT RAISE(ABORT,'ARCHIVED_FAILED_CANNOT_REINSERT'); END;
            CREATE TRIGGER IF NOT EXISTS archived_failed_immutable_update
            BEFORE UPDATE ON archived_failed_invoices
            BEGIN SELECT RAISE(ABORT,'ARCHIVED_FAILED_IMMUTABLE'); END;
            CREATE TRIGGER IF NOT EXISTS archived_failed_immutable_delete
            BEFORE DELETE ON archived_failed_invoices
            BEGIN SELECT RAISE(ABORT,'ARCHIVED_FAILED_IMMUTABLE'); END;
            """;
        command.ExecuteNonQuery();
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

    private static void ResolveMatchingIssueArtifacts(
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
            UPDATE sync_issues
            SET resolved_utc=$resolved_utc
            WHERE account_key=$account_key
              AND TRIM(resolved_utc)=''
              AND issue_type IN (
                'invoice_list_failed','query_failed','remote_not_found',
                'unknown_remote_not_found','reconciliation_failed',
                'ambiguous_match','local_write_failed'
              )
              AND ({string.Join(" OR ", identityClauses)});
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$resolved_utc", DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
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
