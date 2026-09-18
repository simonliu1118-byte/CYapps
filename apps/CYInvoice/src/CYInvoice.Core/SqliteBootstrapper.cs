using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using Microsoft.Data.Sqlite;

namespace CYInvoice.Core.Storage;

public sealed record SqliteBootstrapResult(
    string DatabasePath,
    bool Created,
    int InvoiceCount,
    int ItemCount,
    int BuyerNameCount);

public static class SqliteBootstrapper
{
    public const string DatabaseFileName = "CYInvoice.db";
    public const int SchemaVersion = 1;

    public static SqliteBootstrapResult EnsureMigrated(string dataDirectory, string productionInvoice = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, DatabaseFileName);
        if (File.Exists(databasePath))
        {
            return ValidateExistingDatabase(databasePath);
        }

        var invoices = ReadLegacyInvoices(Path.Combine(dataDirectory, "invoices.json"));
        var buyerNames = ReadLegacyBuyerNames(Path.Combine(dataDirectory, "buyer_names.json"));
        var temporaryPath = Path.Combine(
            dataDirectory,
            $".{DatabaseFileName}.{Guid.NewGuid():N}.migrating");

        try
        {
            CreateDatabase(temporaryPath, invoices, buyerNames, productionInvoice);
            VerifyMigration(temporaryPath, invoices, buyerNames, productionInvoice);
            File.Move(temporaryPath, databasePath);
            return new SqliteBootstrapResult(
                databasePath,
                Created: true,
                invoices.Count,
                invoices.Sum(record => record.Items.Count),
                buyerNames.Count);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static List<InvoiceRecord> ReadLegacyInvoices(string path)
    {
        if (!File.Exists(path)) return [];
        JsonFile.TryRead<List<InvoiceRecord>>(path, out var records);
        records ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            record.Items ??= [];
            if (string.IsNullOrWhiteSpace(record.OrderId))
                throw new InvalidDataException("invoices.json contains a record without OrderId");
            if (record.Amount < 0)
                throw new InvalidDataException("invoices.json contains a negative invoice amount");
            if (string.IsNullOrWhiteSpace(record.InvoiceState))
                throw new InvalidDataException("invoices.json contains a record without invoice state");
            if (record.Id.Length != 0 && !ids.Add(record.Id))
                throw new InvalidDataException($"invoices.json contains duplicated record ID {record.Id}");
            if (string.IsNullOrWhiteSpace(record.SentAt))
                record.SentAt = LegacySentAt(record);
        }
        return records;
    }

    private static Dictionary<string, string> ReadLegacyBuyerNames(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        JsonFile.TryRead<Dictionary<string, string>>(path, out var names);
        names ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in names)
        {
            if (item.Key.Length != 8 ||
                !item.Key.All(character => character is >= '0' and <= '9') ||
                string.IsNullOrWhiteSpace(item.Value))
            {
                throw new InvalidDataException("buyer_names.json contains an invalid entry");
            }
        }
        return new Dictionary<string, string>(names, StringComparer.Ordinal);
    }

    private static void CreateDatabase(
        string path,
        IReadOnlyList<InvoiceRecord> invoices,
        IReadOnlyDictionary<string, string> buyerNames,
        string productionInvoice)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWriteCreate);
        ConfigureWritableConnection(connection);
        using var transaction = connection.BeginTransaction();
        CreateSchema(connection, transaction);

        foreach (var record in invoices)
        {
            var invoiceLocalId = InsertInvoice(connection, transaction, record, productionInvoice);
            for (var index = 0; index < record.Items.Count; index++)
            {
                InsertItem(connection, transaction, invoiceLocalId, index, record.Items[index]);
            }
        }

        foreach (var item in buyerNames.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO buyer_names (ban, name) VALUES ($ban, $name);";
            command.Parameters.AddWithValue("$ban", item.Key);
            command.Parameters.AddWithValue("$name", item.Value);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO schema_info (key, value) VALUES
                    ('schema_version', $schema_version),
                    ('created_utc', $created_utc),
                    ('legacy_invoices_imported', $invoice_count),
                    ('legacy_buyer_names_imported', $buyer_name_count);
                """;
            command.Parameters.AddWithValue("$schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$created_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$invoice_count", invoices.Count.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$buyer_name_count", buyerNames.Count.ToString(CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE invoices (
                local_id INTEGER PRIMARY KEY AUTOINCREMENT,
                record_id TEXT NOT NULL DEFAULT '',
                seller_invoice TEXT NOT NULL DEFAULT '',
                environment TEXT NOT NULL DEFAULT '',
                source TEXT NOT NULL DEFAULT '',
                record_origin TEXT NOT NULL DEFAULT 'local',
                original_order_id TEXT NOT NULL DEFAULT '',
                order_id TEXT NOT NULL DEFAULT '',
                api_order_id TEXT NOT NULL DEFAULT '',
                invoice_number TEXT NOT NULL DEFAULT '',
                invoice_state TEXT NOT NULL DEFAULT '',
                carrier_type TEXT NOT NULL DEFAULT '',
                carrier_id1 TEXT NOT NULL DEFAULT '',
                carrier_id2 TEXT NOT NULL DEFAULT '',
                npo_ban TEXT NOT NULL DEFAULT '',
                amount INTEGER NOT NULL DEFAULT 0,
                delivery TEXT NOT NULL DEFAULT '',
                upload_status INTEGER NOT NULL DEFAULT 0,
                upload_status_text TEXT NOT NULL DEFAULT '',
                error_message TEXT NOT NULL DEFAULT '',
                sent_at TEXT NOT NULL DEFAULT '',
                invoice_date TEXT NOT NULL DEFAULT '',
                invoice_time TEXT NOT NULL DEFAULT '',
                last_checked TEXT NOT NULL DEFAULT '',
                buyer_identifier TEXT NOT NULL DEFAULT '',
                buyer_name TEXT NOT NULL DEFAULT '',
                main_remark TEXT NOT NULL DEFAULT '',
                detail_vat INTEGER NOT NULL DEFAULT 0,
                buyer_name_needs_memory INTEGER NOT NULL DEFAULT 0,
                extra_json TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX idx_invoices_account_date
                ON invoices (seller_invoice, environment, invoice_date);
            CREATE INDEX idx_invoices_invoice_number
                ON invoices (seller_invoice, environment, invoice_number);
            CREATE INDEX idx_invoices_order_id
                ON invoices (seller_invoice, environment, order_id);

            CREATE TABLE invoice_items (
                local_id INTEGER PRIMARY KEY AUTOINCREMENT,
                invoice_local_id INTEGER NOT NULL,
                line_no INTEGER NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                quantity_value TEXT NOT NULL DEFAULT '',
                quantity_decimal TEXT NOT NULL DEFAULT '',
                unit TEXT NOT NULL DEFAULT '',
                unit_price INTEGER NOT NULL DEFAULT 0,
                unit_price_decimal TEXT NOT NULL DEFAULT '',
                tax_type TEXT NOT NULL DEFAULT '',
                amount INTEGER NOT NULL DEFAULT 0,
                amount_decimal TEXT NOT NULL DEFAULT '',
                remark TEXT NOT NULL DEFAULT '',
                allow_subtotal_rounding INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (invoice_local_id) REFERENCES invoices(local_id) ON DELETE CASCADE
            );

            CREATE INDEX idx_invoice_items_invoice
                ON invoice_items (invoice_local_id, line_no);

            CREATE TABLE buyer_names (
                ban TEXT PRIMARY KEY,
                name TEXT NOT NULL
            );

            CREATE TABLE sync_state (
                account_key TEXT NOT NULL,
                scope TEXT NOT NULL,
                last_success_utc TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (account_key, scope)
            );

            CREATE TABLE sync_issues (
                local_id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_key TEXT NOT NULL DEFAULT '',
                invoice_number TEXT NOT NULL DEFAULT '',
                order_id TEXT NOT NULL DEFAULT '',
                issue_type TEXT NOT NULL DEFAULT '',
                message TEXT NOT NULL DEFAULT '',
                created_utc TEXT NOT NULL DEFAULT '',
                resolved_utc TEXT NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();
    }

    private static long InsertInvoice(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InvoiceRecord record,
        string productionInvoice)
    {
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
                $record_id, $seller_invoice, $environment, $source, 'local',
                $original_order_id, $order_id, $api_order_id, $invoice_number, $invoice_state,
                $carrier_type, $carrier_id1, $carrier_id2, $npo_ban, $amount, $delivery,
                $upload_status, $upload_status_text, $error_message, $sent_at, $invoice_date,
                $invoice_time, $last_checked, $buyer_identifier, $buyer_name, $main_remark,
                $detail_vat, $buyer_name_needs_memory, $extra_json
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$record_id", record.Id);
        command.Parameters.AddWithValue("$seller_invoice", SellerInvoiceFor(record.Environment, productionInvoice));
        command.Parameters.AddWithValue("$environment", record.Environment);
        command.Parameters.AddWithValue("$source", record.Source);
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

    private static void InsertItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long invoiceLocalId,
        int index,
        InvoiceItem item)
    {
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

    private static void VerifyMigration(
        string path,
        IReadOnlyList<InvoiceRecord> invoices,
        IReadOnlyDictionary<string, string> buyerNames,
        string productionInvoice)
    {
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        VerifyIntegrity(connection);
        VerifySchemaVersion(connection);

        if (Count(connection, "invoices") != invoices.Count)
            throw new InvalidDataException("SQLite migration invoice count mismatch");
        if (Count(connection, "invoice_items") != invoices.Sum(record => record.Items.Count))
            throw new InvalidDataException("SQLite migration item count mismatch");
        if (Count(connection, "buyer_names") != buyerNames.Count)
            throw new InvalidDataException("SQLite migration buyer-name count mismatch");

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT record_id, seller_invoice, environment, source, original_order_id,
                       order_id, api_order_id, invoice_number, invoice_state, amount,
                       buyer_identifier, buyer_name
                FROM invoices
                ORDER BY local_id;
                """;
            using var reader = command.ExecuteReader();
            for (var index = 0; index < invoices.Count; index++)
            {
                if (!reader.Read()) throw new InvalidDataException("SQLite migration invoice read-back is incomplete");
                var expected = invoices[index];
                RequireEqual(expected.Id, reader.GetString(0), "record_id");
                RequireEqual(SellerInvoiceFor(expected.Environment, productionInvoice), reader.GetString(1), "seller_invoice");
                RequireEqual(expected.Environment, reader.GetString(2), "environment");
                RequireEqual(expected.Source, reader.GetString(3), "source");
                RequireEqual(expected.OriginalOrderId, reader.GetString(4), "original_order_id");
                RequireEqual(expected.OrderId, reader.GetString(5), "order_id");
                RequireEqual(expected.ApiOrderId, reader.GetString(6), "api_order_id");
                RequireEqual(expected.InvoiceNumber, reader.GetString(7), "invoice_number");
                RequireEqual(expected.InvoiceState, reader.GetString(8), "invoice_state");
                if (reader.GetInt64(9) != expected.Amount)
                    throw new InvalidDataException("SQLite migration amount mismatch");
                RequireEqual(expected.BuyerIdentifier, reader.GetString(10), "buyer_identifier");
                RequireEqual(expected.BuyerName, reader.GetString(11), "buyer_name");
            }
            if (reader.Read()) throw new InvalidDataException("SQLite migration invoice read-back has extra rows");
        }

        var expectedItems = invoices.SelectMany(record => record.Items).ToArray();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT description, quantity_value, quantity_decimal, unit, unit_price,
                       unit_price_decimal, tax_type, amount, amount_decimal, remark,
                       allow_subtotal_rounding
                FROM invoice_items
                ORDER BY invoice_local_id, line_no;
                """;
            using var reader = command.ExecuteReader();
            for (var index = 0; index < expectedItems.Length; index++)
            {
                if (!reader.Read()) throw new InvalidDataException("SQLite migration item read-back is incomplete");
                var expected = expectedItems[index];
                RequireEqual(expected.Description, reader.GetString(0), "item description");
                RequireEqual(expected.Quantity.ToString("R", CultureInfo.InvariantCulture), reader.GetString(1), "item quantity");
                RequireEqual(expected.QuantityDecimal, reader.GetString(2), "item quantity decimal");
                RequireEqual(expected.Unit, reader.GetString(3), "item unit");
                if (reader.GetInt64(4) != expected.UnitPrice)
                    throw new InvalidDataException("SQLite migration item unit price mismatch");
                RequireEqual(expected.UnitPriceDecimal, reader.GetString(5), "item unit price decimal");
                RequireEqual(expected.TaxType, reader.GetString(6), "item tax type");
                if (reader.GetInt64(7) != expected.Amount)
                    throw new InvalidDataException("SQLite migration item amount mismatch");
                RequireEqual(expected.AmountDecimal, reader.GetString(8), "item amount decimal");
                RequireEqual(expected.Remark, reader.GetString(9), "item remark");
                if (reader.GetInt64(10) != (expected.AllowSubtotalRounding ? 1 : 0))
                    throw new InvalidDataException("SQLite migration item rounding flag mismatch");
            }
            if (reader.Read()) throw new InvalidDataException("SQLite migration item read-back has extra rows");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ban, name FROM buyer_names ORDER BY ban;";
            using var reader = command.ExecuteReader();
            foreach (var expected in buyerNames.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!reader.Read()) throw new InvalidDataException("SQLite migration buyer-name read-back is incomplete");
                RequireEqual(expected.Key, reader.GetString(0), "buyer-name ban");
                RequireEqual(expected.Value, reader.GetString(1), "buyer-name value");
            }
            if (reader.Read()) throw new InvalidDataException("SQLite migration buyer-name read-back has extra rows");
        }
    }

    private static SqliteBootstrapResult ValidateExistingDatabase(string path)
    {
        try
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            VerifyIntegrity(connection);
            VerifySchemaVersion(connection);
            return new SqliteBootstrapResult(
                path,
                Created: false,
                checked((int)Count(connection, "invoices")),
                checked((int)Count(connection, "invoice_items")),
                checked((int)Count(connection, "buyer_names")));
        }
        catch (SqliteException error)
        {
            throw new InvalidDataException("Existing CYInvoice.db failed validation and was left unchanged", error);
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
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

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CYInvoice.db failed SQLite quick_check");
    }

    private static void VerifySchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var raw = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported CYInvoice.db schema version: {raw}");
        }
    }

    private static long Count(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string SellerInvoiceFor(string environment, string productionInvoice) =>
        environment.Trim() switch
        {
            Environments.Test => AmegoDefaults.TestInvoice,
            Environments.Production => productionInvoice.Trim(),
            _ => string.Empty,
        };

    private static string LegacySentAt(InvoiceRecord record)
    {
        var date = record.InvoiceDate.Trim();
        var time = record.InvoiceTime.Trim();
        if (date.Length != 0 && time.Length != 0) return date + " " + time;
        if (date.Length != 0) return date;
        return record.LastChecked.Trim();
    }

    private static void RequireEqual(string expected, string actual, string field)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"SQLite migration {field} mismatch");
    }
}
