using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

internal sealed class InvoiceSyncRepository(string dataDirectory)
{
    private readonly string databasePath = Path.Combine(dataDirectory, SqliteBootstrapper.DatabaseFileName);

    public void UpsertMany(IEnumerable<InvoiceRecord> records)
    {
        var materialized = records.ToList();
        if (materialized.Count == 0) return;
        if (materialized.Any(record => string.IsNullOrWhiteSpace(record.Id)))
            throw new InvalidDataException("sync record ID is required");

        using var connection = Open();
        Configure(connection);
        using var transaction = connection.BeginTransaction();
        foreach (var record in materialized)
        {
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
        }
        transaction.Commit();
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
