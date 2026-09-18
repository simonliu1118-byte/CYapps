using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

internal static class RetentionTests
{
    public static void ProductionPrunesAllExpiredRows()
    {
        using var temporary = new RetentionTemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new RetentionTestProtector());
        ConfigureProduction(repository);

        repository.Invoices.Append(Record("old-delete", "AA00000001", "M20260630001", InvoiceStates.Opened, "2026/06/30"));
        repository.Invoices.Append(Record("resolved-delete", "AA00000002", "M20260501001", InvoiceStates.Opened, "2026/05/01"));
        repository.Invoices.Append(Record("boundary-keep", "AA00000003", "M20260701001", InvoiceStates.Opened, "2026/07/01"));
        repository.Invoices.Append(Record("unknown-delete", "AA00000004", "M20260101001", InvoiceStates.Unknown, "2026/01/01"));
        repository.Invoices.Append(Record("changing-delete", "AA00000005", "M20260101002", InvoiceStates.Changing, "2026/01/01"));
        repository.Invoices.Append(Record("issue-delete", "AA00000006", "M20260101003", InvoiceStates.Opened, "2026/01/01"));
        repository.Invoices.Append(Record("undated-keep", "AA00000007", "M20260101004", InvoiceStates.Opened, ""));

        InsertSyncIssue(repository, "prod|12345675", "", "M20260101003", resolved: false);
        InsertSyncIssue(repository, "prod|12345675", "AA00000002", "", resolved: true);

        var cacheDirectory = Path.Combine(repository.InvoicePdfCacheDirectory, Environments.Production, "20260630");
        Directory.CreateDirectory(cacheDirectory);
        var staleCache = Path.Combine(cacheDirectory, "AA00000001_style0.pdf");
        File.WriteAllText(staleCache, "stale");

        var result = new InvoiceRetentionService(repository).Prune(new DateOnly(2026, 9, 18));
        Equal(5, result.Deleted);
        Equal(0, result.Problems.Count);
        Equal(false, File.Exists(staleCache));

        var remaining = repository.Invoices.LoadOrCreate().Select(record => record.Id).OrderBy(id => id).ToArray();
        SequenceEqual(new[] { "boundary-keep", "undated-keep" }, remaining);

        using var connection = OpenReadOnly(Path.Combine(repository.DataDirectory, SqliteBootstrapper.DatabaseFileName));
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM invoice_items;";
            Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sync_issues;";
            Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
        }
    }

    public static void TestEnvironmentKeepsTodayOnly()
    {
        using var temporary = new RetentionTemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new RetentionTestProtector());
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Test;
        repository.Settings.Save(settings);

        repository.Invoices.Append(Record("test-old-delete", "TT00000001", "M20260917001", InvoiceStates.Opened, "2026/09/17", Environments.Test));
        repository.Invoices.Append(Record("test-today-keep", "TT00000002", "M20260918001", InvoiceStates.Opened, "2026/09/18", Environments.Test));
        repository.Invoices.Append(Record("test-future-keep", "TT00000003", "M20260919001", InvoiceStates.Opened, "2026/09/19", Environments.Test));
        repository.Invoices.Append(Record("test-unknown-delete", "TT00000004", "M20260916001", InvoiceStates.Unknown, "2026/09/16", Environments.Test));

        var result = new InvoiceRetentionService(repository).Prune(new DateOnly(2026, 9, 18));
        Equal(2, result.Deleted);
        Equal(0, result.Problems.Count);
        var remaining = repository.Invoices.LoadOrCreate().Select(record => record.Id).OrderBy(id => id).ToArray();
        SequenceEqual(new[] { "test-future-keep", "test-today-keep" }, remaining);
    }

    private static InvoiceRecord Record(
        string id,
        string invoiceNumber,
        string orderId,
        string state,
        string invoiceDate,
        string environment = Environments.Production) => new()
        {
            Id = id,
            SellerInvoice = environment == Environments.Test ? "12345678" : "12345675",
            Environment = environment,
            Source = "手動",
            RecordOrigin = RecordOrigins.Local,
            OriginalOrderId = orderId,
            OrderId = orderId,
            ApiOrderId = orderId,
            InvoiceNumber = invoiceNumber,
            InvoiceState = state,
            InvoiceDate = invoiceDate,
            InvoiceTime = "10:00:00",
            SentAt = invoiceDate.Length == 0 ? "unknown" : invoiceDate + " 10:00:00",
            Amount = 100,
            Delivery = "紙本",
            Items =
            [
                new InvoiceItem
                {
                    Description = "測試商品",
                    Quantity = 1,
                    QuantityDecimal = "1",
                    UnitPrice = 100,
                    UnitPriceDecimal = "100",
                    Amount = 100,
                    AmountDecimal = "100",
                    TaxType = "1",
                },
            ],
        };

    private static void ConfigureProduction(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
    }

    private static void InsertSyncIssue(
        LocalRepository repository,
        string accountKey,
        string invoiceNumber,
        string orderId,
        bool resolved)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(repository.DataDirectory, SqliteBootstrapper.DatabaseFileName),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync_issues (
                account_key, invoice_number, order_id, issue_type, message, created_utc, resolved_utc
            ) VALUES (
                $account_key, $invoice_number, $order_id, 'retention-test', 'test issue',
                '2026-09-18T00:00:00.0000000+00:00', $resolved_utc
            );
            """;
        command.Parameters.AddWithValue("$account_key", accountKey);
        command.Parameters.AddWithValue("$invoice_number", invoiceNumber);
        command.Parameters.AddWithValue("$order_id", orderId);
        command.Parameters.AddWithValue("$resolved_utc", resolved ? "2026-09-18T01:00:00.0000000+00:00" : "");
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
    }

    private sealed class RetentionTestProtector : ISecretProtector
    {
        private const string Prefix = "test-protected:";
        public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
        public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(ciphertext[Prefix.Length..])
            : throw new InvalidDataException("invalid protected test value");
    }

    private sealed class RetentionTemporaryDirectory : IDisposable
    {
        public RetentionTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-retention-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
