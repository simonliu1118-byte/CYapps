using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed record StatusUpdate(
    string InvoiceNumber,
    string InvoiceState,
    int UploadStatus,
    string UploadStatusText,
    string ErrorMessage,
    string InvoiceDate,
    string InvoiceTime,
    string LastChecked);

public sealed class InvoiceStore
{
    private readonly Lock gate = new();
    private readonly string dataDirectory;
    private readonly string databasePath;
    private readonly string productionInvoice;

    public InvoiceStore(string dataDirectory, string productionInvoice = "")
    {
        this.dataDirectory = dataDirectory;
        this.productionInvoice = productionInvoice.Trim();
        var bootstrap = SqliteBootstrapper.EnsureMigrated(dataDirectory, this.productionInvoice);
        databasePath = bootstrap.DatabasePath;
        if (bootstrap.Created)
        {
            NormalizeLegacyJsonAfterSuccessfulMigration();
        }
    }

    public IReadOnlyList<InvoiceRecord> LoadOrCreate()
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            return LoadRecords(connection);
        }
    }

    public void Save(IEnumerable<InvoiceRecord> records)
    {
        lock (gate)
        {
            var materialized = records.ToList();
            Validate(materialized);
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "DELETE FROM invoice_items;");
            Execute(connection, transaction, "DELETE FROM invoices;");
            foreach (var record in materialized)
            {
                var localId = InsertInvoice(connection, transaction, record);
                InsertItems(connection, transaction, localId, record.Items);
            }
            transaction.Commit();
        }
    }

    public void Append(InvoiceRecord record)
    {
        lock (gate)
        {
            ValidateRecord(record);
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            if (record.Id.Length != 0)
            {
                using var duplicate = connection.CreateCommand();
                duplicate.Transaction = transaction;
                duplicate.CommandText = "SELECT COUNT(*) FROM invoices WHERE record_id = $id;";
                duplicate.Parameters.AddWithValue("$id", record.Id);
                if (Convert.ToInt64(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException($"invoice record ID {record.Id} already exists");
            }
            var localId = InsertInvoice(connection, transaction, record);
            InsertItems(connection, transaction, localId, record.Items);
            transaction.Commit();
        }
    }

    public void UpdateStatus(string id, StatusUpdate update)
    {
        lock (gate)
        {
            using var connection = Open(SqliteOpenMode.ReadWrite);
            ConfigureWritableConnection(connection);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE invoices
                SET invoice_number = $invoice_number,
                    invoice_state = $invoice_state,
                    upload_status = $upload_status,
                    upload_status_text = $upload_status_text,
                    error_message = $error_message,
                    invoice_date = $invoice_date,
                    invoice_time = $invoice_time,
                    last_checked = $last_checked
                WHERE record_id = $id;
                """;
            command.Parameters.AddWithValue("$invoice_number", update.InvoiceNumber);
            command.Parameters.AddWithValue("$invoice_state", update.InvoiceState);
            command.Parameters.AddWithValue("$upload_status", update.UploadStatus);
            command.Parameters.AddWithValue("$upload_status_text", update.UploadStatusText);
            command.Parameters.AddWithValue("$error_message", update.ErrorMessage);
            command.Parameters.AddWithValue("$invoice_date", update.InvoiceDate);
            command.Parameters.AddWithValue("$invoice_time", update.InvoiceTime);
            command.Parameters.AddWithValue("$last_checked", update.LastChecked);
            command.Parameters.AddWithValue("$id", id);
            var changed = command.ExecuteNonQuery();
            if (changed == 0) throw new KeyNotFoundException($"invoice record {id} not found");
            if (changed != 1) throw new InvalidDataException($"invoice record ID {id} is duplicated");
            transaction.Commit();
        }
    }

    private IReadOnlyList<InvoiceRecord> LoadRecords(SqliteConnection connection)
    {
        var rows = new List<(long LocalId, InvoiceRecord Record)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT local_id, record_id, seller_invoice, environment, source, record_origin,
                       original_order_id, order_id, api_order_id, invoice_number, invoice_state,
                       carrier_type, carrier_id1, carrier_id2, npo_ban, amount, delivery,
                       upload_status, upload_status_text, error_message, sent_at, invoice_date,
                       invoice_time, last_checked, buyer_identifier, buyer_name, main_remark,
                       detail_vat, buyer_name_needs_memory, extra_json
                FROM invoices
                ORDER BY local_id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), new InvoiceRecord
                {
                    Id = reader.GetString(1),
                    SellerInvoice = reader.GetString(2),
                    Environment = reader.GetString(3),
                    Source = reader.GetString(4),
                    RecordOrigin = reader.GetString(5),
                    OriginalOrderId = reader.GetString(6),
                    OrderId = reader.GetString(7),
                    ApiOrderId = reader.GetString(8),
                    InvoiceNumber = reader.GetString(9),
                    InvoiceState = reader.GetString(10),
                    CarrierType = reader.GetString(11),
                    CarrierId1 = reader.GetString(12),
                    CarrierId2 = reader.GetString(13),
                    NpoBan = reader.GetString(14),
                    Amount = reader.GetInt64(15),
                    Delivery = reader.GetString(16),
                    UploadStatus = reader.GetInt32(17),
                    UploadStatusText = reader.GetString(18),
                    ErrorMessage = reader.GetString(19),
                    SentAt = reader.GetString(20),
                    InvoiceDate = reader.GetString(21),
                    InvoiceTime = reader.GetString(22),
                    LastChecked = reader.GetString(23),
                    BuyerIdentifier = reader.GetString(24),
                    BuyerName = reader.GetString(25),
                    MainRemark = reader.GetString(26),
                    DetailVat = reader.GetInt32(27),
                    BuyerNameNeedsMemory = reader.GetInt32(28) != 0,
                    ExtensionData = ParseExtensionData(reader.GetString(29)),
                    Items = [],
                }));
            }
        }

        var byLocalId = rows.ToDictionary(item => item.LocalId, item => item.Record);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT invoice_local_id, description, quantity_value, quantity_decimal,
                       unit, unit_price, unit_price_decimal, tax_type, amount, amount_decimal,
                       remark, allow_subtotal_rounding
                FROM invoice_items
                ORDER BY invoice_local_id, line_no;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var invoiceLocalId = reader.GetInt64(0);
                if (!byLocalId.TryGetValue(invoiceLocalId, out var record))
                    throw new InvalidDataException("invoice_items contains an orphaned invoice reference");
                if (!double.TryParse(reader.GetString(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity))
                    throw new InvalidDataException("invoice_items contains an invalid quantity value");
                record.Items.Add(new InvoiceItem
                {
                    Description = reader.GetString(1),
                    Quantity = quantity,
                    QuantityDecimal = reader.GetString(3),
                    Unit = reader.GetString(4),
                    UnitPrice = reader.GetInt64(5),
                    UnitPriceDecimal = reader.GetString(6),
                    TaxType = reader.GetString(7),
                    Amount = reader.GetInt64(8),
                    AmountDecimal = reader.GetString(9),
                    Remark = reader.GetString(10),
                    AllowSubtotalRounding = reader.GetInt32(11) != 0,
                });
            }
        }

        var records = rows.Select(item => item.Record).ToList();
        Validate(records);
        return records;
    }

    private long InsertInvoice(SqliteConnection connection, SqliteTransaction transaction, InvoiceRecord record)
    {
        ValidateRecord(record);
        NormalizeAccount(record);
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
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long invoiceLocalId,
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
            command.Parameters.AddWithValue("$invoice_local_id", invoiceLocalId);
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

    private void NormalizeAccount(InvoiceRecord record)
    {
        if (record.SellerInvoice.Trim().Length == 0)
        {
            record.SellerInvoice = record.Environment.Trim() switch
            {
                Environments.Test => AmegoDefaults.TestInvoice,
                Environments.Production => productionInvoice,
                _ => string.Empty,
            };
        }
        if (record.RecordOrigin.Trim().Length == 0) record.RecordOrigin = RecordOrigins.Local;
    }

    private void NormalizeLegacyJsonAfterSuccessfulMigration()
    {
        var legacyPath = Path.Combine(dataDirectory, "invoices.json");
        if (!File.Exists(legacyPath)) return;
        JsonFile.TryRead<List<InvoiceRecord>>(legacyPath, out var records);
        records ??= [];
        var changed = false;
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.SentAt))
            {
                record.SentAt = LegacySentAt(record);
                changed |= record.SentAt.Length != 0;
            }
        }
        if (changed) JsonFile.Write(legacyPath, records);
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

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, JsonElement>? ParseExtensionData(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("CYInvoice.db contains invalid extra_json", error);
        }
    }

    private static string LegacySentAt(InvoiceRecord record)
    {
        var date = record.InvoiceDate.Trim();
        var time = record.InvoiceTime.Trim();
        if (date.Length != 0 && time.Length != 0) return date + " " + time;
        if (date.Length != 0) return date;
        return record.LastChecked.Trim();
    }

    private static void Validate(IReadOnlyList<InvoiceRecord> records)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            ValidateRecord(record);
            if (record.Id.Length != 0 && !ids.Add(record.Id))
                throw new InvalidDataException($"invoice record ID {record.Id} is duplicated");
        }
    }

    private static void ValidateRecord(InvoiceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.OrderId)) throw new InvalidDataException("order ID is required");
        if (record.Amount < 0) throw new InvalidDataException("invoice amount cannot be negative");
        if (string.IsNullOrWhiteSpace(record.InvoiceState)) throw new InvalidDataException("invoice state is required");
        record.Items ??= [];
    }
}
