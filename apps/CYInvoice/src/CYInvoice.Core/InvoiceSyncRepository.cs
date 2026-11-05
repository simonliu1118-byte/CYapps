using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Invoicing;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

internal sealed class InvoiceSyncRepository(string dataDirectory)
{
    private readonly string databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);

    internal static string Snapshot(IEnumerable<InvoiceRecord> records) =>
        JsonSerializer.Serialize(records.OrderBy(record => record.Id, StringComparer.Ordinal));

    public void UpsertMany(IEnumerable<InvoiceRecord> records, string? repairSnapshot = null, CancellationToken cancellationToken = default, InvoiceSyncIssue? completedIssue = null, bool cancelUnsentAllowance = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var materialized = records.ToList();
        if (materialized.Count == 0) return;
        if (materialized.Any(record => string.IsNullOrWhiteSpace(record.Id)))
            throw new InvalidDataException("sync record ID is required");

        using var connection = Open();
        Configure(connection);
        if (repairSnapshot is not null)
            BackupBeforeRepair(connection, repairSnapshot, cancellationToken);
        using var transaction = connection.BeginTransaction();
        if (repairSnapshot is not null)
            RequireRepairSnapshot(connection, transaction, repairSnapshot);
        if (cancelUnsentAllowance)
        {
            ArgumentNullException.ThrowIfNull(completedIssue);
            using var guard = connection.CreateCommand();
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT COUNT(*) FROM allowance_number_ledger
                WHERE seller_invoice=$seller AND environment=$environment AND work_item_id=$work AND status<>'reserved' AND NOT
                 (status='occupied_external' AND EXISTS(SELECT 1 FROM allowance_local_number_rejections r
                  WHERE r.work_item_id=allowance_number_ledger.work_item_id
                   AND r.seller_invoice=allowance_number_ledger.seller_invoice
                   AND r.environment=allowance_number_ledger.environment
                   AND r.allowance_number=allowance_number_ledger.allowance_number));
                """;
            var account = completedIssue.AccountKey.Split('|', 2);
            if (account.Length != 2) throw new InvalidDataException("折讓公司識別無效");
            guard.Parameters.AddWithValue("$environment", account[0]);
            guard.Parameters.AddWithValue("$seller", account[1]);
            guard.Parameters.AddWithValue("$work", completedIssue.Id.ToString(CultureInfo.InvariantCulture));
            if (Convert.ToInt64(guard.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                throw new InvalidOperationException("折讓已進入送出或結果待查，不能取消；系統會依原號查證");
        }
        foreach (var record in materialized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localId = FindLocalId(connection, transaction, record.Id);
            if (localId is null)
            {
                localId = InsertInvoice(connection, transaction, record);
            }
            else
            {
                UpdateInvoice(connection, transaction, localId.Value, record);
                using var deleteItems = connection.CreateCommand();
                deleteItems.Transaction = transaction;
                deleteItems.CommandText = "DELETE FROM invoice_items WHERE invoice_local_id = $id;";
                deleteItems.Parameters.AddWithValue("$id", localId.Value);
                deleteItems.ExecuteNonQuery();
            }
            InsertItems(connection, transaction, localId.Value, record.Items);
            AppendOfficialAllowanceObservations(connection, transaction, record);
        }
        if (completedIssue is not null)
        {
            using var close = connection.CreateCommand();
            close.Transaction = transaction;
            close.CommandText = "UPDATE sync_issues SET resolved_utc=$time WHERE local_id=$id AND account_key=$account AND issue_type=$type AND TRIM(resolved_utc)=''";
            close.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            close.Parameters.AddWithValue("$id", completedIssue.Id);
            close.Parameters.AddWithValue("$account", completedIssue.AccountKey);
            close.Parameters.AddWithValue("$type", completedIssue.IssueType);
            if (close.ExecuteNonQuery() != 1) throw new InvalidOperationException("原操作已變更，結案保存已回復");
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    internal static void EnsureOfficialAllowanceAudit(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS official_allowance_observations (
                audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                seller_invoice TEXT NOT NULL,
                environment TEXT NOT NULL,
                invoice_number TEXT NOT NULL,
                allowance_number TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                observed_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_official_allowance_observations_invoice
                ON official_allowance_observations(seller_invoice,environment,invoice_number,audit_id);
            CREATE TRIGGER IF NOT EXISTS official_allowance_observations_no_update
            BEFORE UPDATE ON official_allowance_observations
            BEGIN SELECT RAISE(ABORT,'OFFICIAL_ALLOWANCE_AUDIT_APPEND_ONLY'); END;
            CREATE TRIGGER IF NOT EXISTS official_allowance_observations_no_delete
            BEFORE DELETE ON official_allowance_observations
            BEGIN SELECT RAISE(ABORT,'OFFICIAL_ALLOWANCE_AUDIT_APPEND_ONLY'); END;
            """;
        command.ExecuteNonQuery();
    }

    private static void AppendOfficialAllowanceObservations(
        SqliteConnection connection, SqliteTransaction transaction, InvoiceRecord record)
    {
        if (!InvoiceAllowanceMetadata.HasCompleteSummary(record) ||
            record.InvoiceNumber.Trim().Length == 0 ||
            record.SellerInvoice.Trim().Length == 0) return;

        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var allowance in InvoiceAllowanceMetadata.ReadOfficial(record))
        {
            var number = allowance.AllowanceNumber.Trim();
            if (number.Length == 0 || !numbers.Add(number))
                throw new InvalidDataException("官方折讓歷史缺少單號或重複，未儲存不完整稽核");
            var snapshot = JsonSerializer.Serialize(allowance);
            using var last = connection.CreateCommand();
            last.Transaction = transaction;
            last.CommandText = """
                SELECT snapshot_json FROM official_allowance_observations
                WHERE seller_invoice=$seller AND environment=$environment
                  AND invoice_number=$invoice COLLATE NOCASE
                  AND allowance_number=$allowance COLLATE NOCASE
                ORDER BY audit_id DESC LIMIT 1;
                """;
            last.Parameters.AddWithValue("$seller", record.SellerInvoice.Trim());
            last.Parameters.AddWithValue("$environment", record.Environment);
            last.Parameters.AddWithValue("$invoice", record.InvoiceNumber.Trim());
            last.Parameters.AddWithValue("$allowance", number);
            var previous = last.ExecuteScalar() as string;
            if (string.Equals(previous, snapshot, StringComparison.Ordinal)) continue;

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO official_allowance_observations
                    (seller_invoice,environment,invoice_number,allowance_number,snapshot_json,observed_utc)
                VALUES ($seller,$environment,$invoice,$allowance,$snapshot,$time);
                """;
            insert.Parameters.AddWithValue("$seller", record.SellerInvoice.Trim());
            insert.Parameters.AddWithValue("$environment", record.Environment);
            insert.Parameters.AddWithValue("$invoice", record.InvoiceNumber.Trim());
            insert.Parameters.AddWithValue("$allowance", number);
            insert.Parameters.AddWithValue("$snapshot", snapshot);
            insert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidDataException("官方折讓查證歷史無法完整保存");
        }
    }

    private static void RequireRepairSnapshot(SqliteConnection connection, SqliteTransaction transaction, string expected)
    {
        if (Snapshot(InvoiceStore.LoadRecords(connection, transaction)) != expected)
            throw new InvalidDataException("回查期間本機資料已變更，重複紀錄未修復；請重新整理再試");
    }

    private void BackupBeforeRepair(SqliteConnection connection, string expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(dataDirectory, "RepairBackups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "duplicate-" + Guid.NewGuid().ToString("N") + ".db");
        var temporary = path + ".tmp";
        try
        {
            // A read transaction pins the preimage while SQLite copies all tables, including items.
            using (var transaction = connection.BeginTransaction(deferred: true))
            {
                RequireRepairSnapshot(connection, transaction, expected);
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
                }.ToString());
                backup.Open();
                connection.BackupDatabase(backup);
                using var check = backup.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("重複紀錄修復備份完整性檢查失敗");
                check.CommandText = "PRAGMA foreign_key_check;";
                using (var reader = check.ExecuteReader())
                    if (reader.Read()) throw new InvalidDataException("重複紀錄修復備份關聯檢查失敗");
                if (Snapshot(InvoiceStore.LoadRecords(backup)) != expected)
                    throw new InvalidDataException("重複紀錄修復備份與原資料不符");
                transaction.Commit();
            }
            using (var file = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                file.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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

    private static long? FindLocalId(SqliteConnection connection, SqliteTransaction transaction, string recordId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT local_id FROM invoices WHERE record_id = $id ORDER BY local_id;";
        command.Parameters.AddWithValue("$id", recordId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var localId = reader.GetInt64(0);
        if (reader.Read()) throw new InvalidDataException($"invoice record ID {recordId} is duplicated");
        return localId;
    }

    private static long InsertInvoice(SqliteConnection connection, SqliteTransaction transaction, InvoiceRecord record)
    {
        Validate(record);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO invoices (
                record_id, seller_invoice, environment, source, record_origin,
                original_order_id, order_id, api_order_id, invoice_number, invoice_state,
                carrier_type, carrier_id1, carrier_id2, npo_ban, amount, delivery,
                upload_status, upload_status_text, error_message, sent_at, invoice_date,
                invoice_time, last_checked, buyer_identifier, buyer_name, main_remark,
                detail_vat, buyer_name_needs_memory, extra_json
            ) VALUES (
                $record_id, $seller_invoice, $environment, $source, $record_origin,
                $original_order_id, $order_id, $api_order_id, $invoice_number, $invoice_state,
                $carrier_type, $carrier_id1, $carrier_id2, $npo_ban, $amount, $delivery,
                $upload_status, $upload_status_text, $error_message, $sent_at, $invoice_date,
                $invoice_time, $last_checked, $buyer_identifier, $buyer_name, $main_remark,
                $detail_vat, $buyer_name_needs_memory, $extra_json
            );
            SELECT last_insert_rowid();
            """;
        BindInvoice(command, record);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void UpdateInvoice(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long localId,
        InvoiceRecord record)
    {
        Validate(record);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE invoices SET
                record_id = $record_id,
                seller_invoice = $seller_invoice,
                environment = $environment,
                source = $source,
                record_origin = $record_origin,
                original_order_id = $original_order_id,
                order_id = $order_id,
                api_order_id = $api_order_id,
                invoice_number = $invoice_number,
                invoice_state = $invoice_state,
                carrier_type = $carrier_type,
                carrier_id1 = $carrier_id1,
                carrier_id2 = $carrier_id2,
                npo_ban = $npo_ban,
                amount = $amount,
                delivery = $delivery,
                upload_status = $upload_status,
                upload_status_text = $upload_status_text,
                error_message = $error_message,
                sent_at = $sent_at,
                invoice_date = $invoice_date,
                invoice_time = $invoice_time,
                last_checked = $last_checked,
                buyer_identifier = $buyer_identifier,
                buyer_name = $buyer_name,
                main_remark = $main_remark,
                detail_vat = $detail_vat,
                buyer_name_needs_memory = $buyer_name_needs_memory,
                extra_json = $extra_json
            WHERE local_id = $local_id;
            """;
        BindInvoice(command, record);
        command.Parameters.AddWithValue("$local_id", localId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("sync invoice update did not affect exactly one row");
    }

    private static void BindInvoice(SqliteCommand command, InvoiceRecord record)
    {
        command.Parameters.AddWithValue("$record_id", record.Id);
        command.Parameters.AddWithValue("$seller_invoice", record.SellerInvoice);
        command.Parameters.AddWithValue("$environment", record.Environment);
        command.Parameters.AddWithValue("$source", record.Source);
        command.Parameters.AddWithValue("$record_origin", record.RecordOrigin);
        command.Parameters.AddWithValue("$original_order_id", record.OriginalOrderId);
        command.Parameters.AddWithValue("$order_id", record.OrderId);
        command.Parameters.AddWithValue("$api_order_id", record.ApiOrderId);
        command.Parameters.AddWithValue("$invoice_number", record.InvoiceNumber);
        command.Parameters.AddWithValue("$invoice_state", record.InvoiceState);
        command.Parameters.AddWithValue("$carrier_type", record.CarrierType);
        command.Parameters.AddWithValue("$carrier_id1", record.CarrierId1);
        command.Parameters.AddWithValue("$carrier_id2", record.CarrierId2);
        command.Parameters.AddWithValue("$npo_ban", record.NpoBan);
        command.Parameters.AddWithValue("$amount", record.Amount);
        command.Parameters.AddWithValue("$delivery", record.Delivery);
        command.Parameters.AddWithValue("$upload_status", record.UploadStatus);
        command.Parameters.AddWithValue("$upload_status_text", record.UploadStatusText);
        command.Parameters.AddWithValue("$error_message", record.ErrorMessage);
        command.Parameters.AddWithValue("$sent_at", record.SentAt);
        command.Parameters.AddWithValue("$invoice_date", record.InvoiceDate);
        command.Parameters.AddWithValue("$invoice_time", record.InvoiceTime);
        command.Parameters.AddWithValue("$last_checked", record.LastChecked);
        command.Parameters.AddWithValue("$buyer_identifier", record.BuyerIdentifier);
        command.Parameters.AddWithValue("$buyer_name", record.BuyerName);
        command.Parameters.AddWithValue("$main_remark", record.MainRemark);
        command.Parameters.AddWithValue("$detail_vat", record.DetailVat);
        command.Parameters.AddWithValue("$buyer_name_needs_memory", record.BuyerNameNeedsMemory ? 1 : 0);
        command.Parameters.AddWithValue(
            "$extra_json",
            record.ExtensionData is null ? string.Empty : JsonSerializer.Serialize(record.ExtensionData));
    }

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long localId,
        IReadOnlyList<InvoiceItem> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO invoice_items (
                    invoice_local_id, line_no, description, quantity_value, quantity_decimal,
                    unit, unit_price, unit_price_decimal, tax_type, amount, amount_decimal,
                    remark, allow_subtotal_rounding
                ) VALUES (
                    $invoice_local_id, $line_no, $description, $quantity_value, $quantity_decimal,
                    $unit, $unit_price, $unit_price_decimal, $tax_type, $amount, $amount_decimal,
                    $remark, $allow_subtotal_rounding
                );
                """;
            command.Parameters.AddWithValue("$invoice_local_id", localId);
            command.Parameters.AddWithValue("$line_no", index);
            command.Parameters.AddWithValue("$description", item.Description);
            command.Parameters.AddWithValue("$quantity_value", item.Quantity.ToString("R", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$quantity_decimal", item.QuantityDecimal);
            command.Parameters.AddWithValue("$unit", item.Unit);
            command.Parameters.AddWithValue("$unit_price", item.UnitPrice);
            command.Parameters.AddWithValue("$unit_price_decimal", item.UnitPriceDecimal);
            command.Parameters.AddWithValue("$tax_type", item.TaxType);
            command.Parameters.AddWithValue("$amount", item.Amount);
            command.Parameters.AddWithValue("$amount_decimal", item.AmountDecimal);
            command.Parameters.AddWithValue("$remark", item.Remark);
            command.Parameters.AddWithValue("$allow_subtotal_rounding", item.AllowSubtotalRounding ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void Validate(InvoiceRecord record)
    {
        if (record.Id.Trim().Length == 0) throw new InvalidDataException("sync record ID is required");
        if (record.OrderId.Trim().Length == 0) throw new InvalidDataException("sync OrderID is required");
        if (record.InvoiceState.Trim().Length == 0) throw new InvalidDataException("sync invoice state is required");
        if (record.Amount < 0) throw new InvalidDataException("sync invoice amount cannot be negative");
        record.Items ??= [];
    }
}
