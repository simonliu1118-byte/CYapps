using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

var tests = new (string Name, Action Run)[]
{
    ("legacy JSON migrates without modifying source files", TestLegacyMigration),
    ("corrupt legacy JSON never creates SQLite database", TestCorruptLegacyJson),
    ("corrupt existing SQLite database is never overwritten", TestCorruptExistingDatabase),
    ("empty data directory creates an empty valid database", TestEmptyMigration),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} SQLite migration tests passed");
return failures == 0 ? 0 : 1;

static void TestLegacyMigration()
{
    using var temporary = new TemporaryDirectory();
    var invoicesPath = Path.Combine(temporary.Path, "invoices.json");
    var buyerNamesPath = Path.Combine(temporary.Path, "buyer_names.json");

    var records = new List<InvoiceRecord>
    {
        new()
        {
            Id = "prod-record",
            Source = "MO店+",
            OriginalOrderId = "660000000001",
            OrderId = "660000000001",
            ApiOrderId = "660000000001",
            Environment = Environments.Production,
            InvoiceNumber = "AA12345678",
            InvoiceState = InvoiceStates.Opened,
            Amount = 190,
            Delivery = "紙本",
            UploadStatus = 99,
            UploadStatusText = "完成",
            InvoiceDate = "2026/09/18",
            InvoiceTime = "10:20:30",
            BuyerIdentifier = "12345675",
            BuyerName = "測試公司",
            MainRemark = "保留備註",
            DetailVat = 1,
            Items =
            [
                new InvoiceItem
                {
                    Description = "商品A",
                    Quantity = 2,
                    QuantityDecimal = "2",
                    Unit = "個",
                    UnitPrice = 100,
                    UnitPriceDecimal = "100",
                    TaxType = "1",
                    Amount = 200,
                    AmountDecimal = "200",
                },
                new InvoiceItem
                {
                    Description = "折扣",
                    Quantity = 1,
                    QuantityDecimal = "1",
                    UnitPrice = -10,
                    UnitPriceDecimal = "-10",
                    TaxType = "1",
                    Amount = -10,
                    AmountDecimal = "-10",
                    Remark = "折扣明細",
                },
            ],
        },
        new()
        {
            Id = "test-record",
            Source = "手動",
            OriginalOrderId = "M20260918001",
            OrderId = "M20260918001",
            Environment = Environments.Test,
            InvoiceNumber = "BB12345678",
            InvoiceState = InvoiceStates.Voided,
            Amount = 99,
            Delivery = "紙本",
            SentAt = "2026/09/18 11:00:00",
            InvoiceDate = "2026/09/18",
            InvoiceTime = "11:00:00",
            BuyerName = "消費者",
            Items =
            [
                new InvoiceItem
                {
                    Description = "商品B",
                    Quantity = 1.5,
                    QuantityDecimal = "1.5000000",
                    UnitPrice = 66,
                    UnitPriceDecimal = "66",
                    TaxType = "1",
                    Amount = 99,
                    AmountDecimal = "99",
                    AllowSubtotalRounding = true,
                },
            ],
        },
    };
    var buyerNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["12345675"] = "測試公司",
    };

    WriteJson(invoicesPath, records);
    WriteJson(buyerNamesPath, buyerNames);
    var originalInvoices = File.ReadAllBytes(invoicesPath);
    var originalBuyerNames = File.ReadAllBytes(buyerNamesPath);

    var result = SqliteBootstrapper.EnsureMigrated(temporary.Path, "12345675");
    Equal(true, result.Created);
    Equal(2, result.InvoiceCount);
    Equal(3, result.ItemCount);
    Equal(1, result.BuyerNameCount);
    Equal(true, File.Exists(result.DatabasePath));
    SequenceEqual(originalInvoices, File.ReadAllBytes(invoicesPath));
    SequenceEqual(originalBuyerNames, File.ReadAllBytes(buyerNamesPath));

    using (var connection = OpenReadOnly(result.DatabasePath))
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT record_id, seller_invoice, environment, record_origin, source, invoice_number, amount
            FROM invoices
            ORDER BY local_id;
            """;
        using var reader = command.ExecuteReader();
        Equal(true, reader.Read());
        Equal("prod-record", reader.GetString(0));
        Equal("12345675", reader.GetString(1));
        Equal(Environments.Production, reader.GetString(2));
        Equal("local", reader.GetString(3));
        Equal("MO店+", reader.GetString(4));
        Equal("AA12345678", reader.GetString(5));
        Equal(190L, reader.GetInt64(6));

        Equal(true, reader.Read());
        Equal("test-record", reader.GetString(0));
        Equal("12345678", reader.GetString(1));
        Equal(Environments.Test, reader.GetString(2));
        Equal("local", reader.GetString(3));
        Equal("手動", reader.GetString(4));
        Equal(false, reader.Read());
    }

    var second = SqliteBootstrapper.EnsureMigrated(temporary.Path, "99999999");
    Equal(false, second.Created);
    Equal(2, second.InvoiceCount);
    Equal(3, second.ItemCount);
    Equal(1, second.BuyerNameCount);
    SequenceEqual(originalInvoices, File.ReadAllBytes(invoicesPath));
    SequenceEqual(originalBuyerNames, File.ReadAllBytes(buyerNamesPath));
}

static void TestCorruptLegacyJson()
{
    using var temporary = new TemporaryDirectory();
    var invoicesPath = Path.Combine(temporary.Path, "invoices.json");
    var original = Encoding.UTF8.GetBytes("{ this is not valid json");
    File.WriteAllBytes(invoicesPath, original);

    Throws<InvalidDataException>(() => SqliteBootstrapper.EnsureMigrated(temporary.Path, "12345675"));
    Equal(false, File.Exists(Path.Combine(temporary.Path, SqliteBootstrapper.DatabaseFileName)));
    SequenceEqual(original, File.ReadAllBytes(invoicesPath));
    Equal(0, Directory.GetFiles(temporary.Path, "*.migrating", SearchOption.TopDirectoryOnly).Length);
}

static void TestCorruptExistingDatabase()
{
    using var temporary = new TemporaryDirectory();
    var databasePath = Path.Combine(temporary.Path, SqliteBootstrapper.DatabaseFileName);
    var original = Encoding.UTF8.GetBytes("not a sqlite database");
    File.WriteAllBytes(databasePath, original);

    Throws<InvalidDataException>(() => SqliteBootstrapper.EnsureMigrated(temporary.Path, "12345675"));
    SequenceEqual(original, File.ReadAllBytes(databasePath));
}

static void TestEmptyMigration()
{
    using var temporary = new TemporaryDirectory();
    var result = SqliteBootstrapper.EnsureMigrated(temporary.Path, "12345675");
    Equal(true, result.Created);
    Equal(0, result.InvoiceCount);
    Equal(0, result.ItemCount);
    Equal(0, result.BuyerNameCount);

    var second = SqliteBootstrapper.EnsureMigrated(temporary.Path, "12345675");
    Equal(false, second.Created);
    Equal(0, second.InvoiceCount);
}

static void WriteJson<T>(string path, T value)
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine, new UTF8Encoding(false));
}

static SqliteConnection OpenReadOnly(string path)
{
    var builder = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
    };
    var connection = new SqliteConnection(builder.ToString());
    connection.Open();
    return connection;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

static void SequenceEqual(byte[] expected, byte[] actual)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException("file bytes changed unexpectedly");
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CYInvoiceSqliteTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
