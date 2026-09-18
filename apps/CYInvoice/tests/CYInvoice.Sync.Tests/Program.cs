using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("source inference follows current OrderID", TestSourceInferenceAsync),
    ("production sync discovers remote invoice and queries details", TestProductionDiscoveryAsync),
    ("production sync overwrites cache and re-infers source", TestProductionOverwriteAsync),
    ("unchanged production summary skips invoice query", TestUnchangedSkipsQueryAsync),
    ("test environment does not discover shared pool invoices", TestTestEnvironmentNoDiscoveryAsync),
    ("remote absence never deletes local cache", TestRemoteAbsenceDoesNotDeleteAsync),
    ("invoice list pagination is exhausted", TestPaginationAsync),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} sync tests passed");
return failures == 0 ? 0 : 1;

static Task TestSourceInferenceAsync()
{
    Equal(InvoiceSources.Mo, InvoiceSourceInference.FromOrderId("66091800123456"));
    Equal(InvoiceSources.Coupang, InvoiceSourceInference.FromOrderId("115102696774269"));
    Equal(InvoiceSources.Digiwin, InvoiceSourceInference.FromOrderId("20260918001"));
    Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001"));
    Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001-R2"));
    Equal(string.Empty, InvoiceSourceInference.FromOrderId("20260230001"));
    Equal(string.Empty, InvoiceSourceInference.FromOrderId("OTHER"));
    return Task.CompletedTask;
}

static async Task TestProductionDiscoveryAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    ConfigureProduction(repository);
    var fake = new FakeGateway
    {
        Pages = { [1] = ListResponse(1, [Remote("AA12345678", "66091800123456", "100")]) },
        QueryResponse = Query("AA12345678", "66091800123456", "100", "測試買受人"),
    };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(1, result.Inserted);
    Equal(1, result.Queried);
    Equal(1, fake.ListCalls);
    Equal(1, fake.QueryCalls);
    var record = repository.Invoices.LoadOrCreate().Single();
    Equal(RecordOrigins.Sync, record.RecordOrigin);
    Equal(InvoiceSources.Mo, record.Source);
    Equal("12345675", record.SellerInvoice);
    Equal("AA12345678", record.InvoiceNumber);
    Equal("66091800123456", record.OrderId);
    Equal("測試買受人", record.BuyerName);
    Equal(100L, record.Amount);
    Equal(1, record.Items.Count);
    Equal("同步商品", record.Items[0].Description);
}

static async Task TestProductionOverwriteAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    ConfigureProduction(repository);
    repository.Invoices.Append(new InvoiceRecord
    {
        Id = "local-old",
        SellerInvoice = "12345675",
        RecordOrigin = RecordOrigins.Local,
        Environment = Environments.Production,
        Source = InvoiceSources.Manual,
        OriginalOrderId = "M20260918001",
        OrderId = "M20260918001",
        ApiOrderId = "M20260918001",
        InvoiceNumber = "BB12345678",
        InvoiceState = InvoiceStates.Opened,
        InvoiceDate = "2026/09/18",
        InvoiceTime = "09:00:00",
        BuyerIdentifier = "0000000000",
        BuyerName = "舊買受人",
        Amount = 80,
        Delivery = InvoiceService.DeliveryPaper,
        UploadStatus = UploadStatuses.Complete,
        UploadStatusText = "完成",
        DetailVat = 1,
        Items = [new InvoiceItem { Description = "舊商品", Quantity = 1, UnitPrice = 80, Amount = 80 }],
    });

    var pdfDir = Path.Combine(repository.InvoicePdfCacheDirectory, Environments.Production, "20260918");
    var previewDir = Path.Combine(repository.InvoicePreviewCacheDirectory, Environments.Production, "20260918");
    Directory.CreateDirectory(pdfDir);
    Directory.CreateDirectory(previewDir);
    var pdf = Path.Combine(pdfDir, "BB12345678_style0.pdf");
    var preview = Path.Combine(previewDir, "BB12345678_style0.page1.png");
    File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
    File.WriteAllBytes(preview, [137, 80, 78, 71, 13, 10, 26, 10]);

    var fake = new FakeGateway
    {
        Pages = { [1] = ListResponse(1, [Remote("BB12345678", "66091800999999", "120", buyer: "新買受人")]) },
        QueryResponse = Query("BB12345678", "66091800999999", "120", "新買受人"),
    };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(1, result.Updated);
    var record = repository.Invoices.LoadOrCreate().Single();
    Equal(RecordOrigins.Local, record.RecordOrigin);
    Equal(InvoiceSources.Mo, record.Source);
    Equal("66091800999999", record.OrderId);
    Equal("新買受人", record.BuyerName);
    Equal(120L, record.Amount);
    Equal("同步商品", record.Items.Single().Description);
    Equal(false, File.Exists(pdf));
    Equal(false, File.Exists(preview));
}

static async Task TestUnchangedSkipsQueryAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    ConfigureProduction(repository);
    repository.Invoices.Append(MatchingRecord("CC12345678", "66091800111111"));
    var fake = new FakeGateway
    {
        Pages = { [1] = ListResponse(1, [Remote("CC12345678", "66091800111111", "100")]) },
        QueryException = new InvalidOperationException("query must not be called"),
    };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(0, result.Queried);
    Equal(0, fake.QueryCalls);
    Equal(1, result.Unchanged);
}

static async Task TestTestEnvironmentNoDiscoveryAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    repository.Invoices.Append(new InvoiceRecord
    {
        Id = "test-local",
        Environment = Environments.Test,
        SellerInvoice = AmegoDefaults.TestInvoice,
        RecordOrigin = RecordOrigins.Local,
        Source = InvoiceSources.Manual,
        OriginalOrderId = "M20260918001",
        OrderId = "M20260918001",
        ApiOrderId = "M20260918001",
        InvoiceNumber = "DD12345678",
        InvoiceState = InvoiceStates.Opened,
        InvoiceDate = "2026/09/18",
        InvoiceTime = "10:00:00",
        BuyerIdentifier = "0000000000",
        BuyerName = "測試消費者",
        Amount = 100,
        Delivery = InvoiceService.DeliveryPaper,
        Items = [new InvoiceItem { Description = "舊商品", Quantity = 1, UnitPrice = 100, Amount = 100 }],
    });
    var fake = new FakeGateway
    {
        ThrowIfListCalled = true,
        QueryResponse = Query("DD12345678", "66091800123456", "100", "測試消費者"),
    };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(0, fake.ListCalls);
    Equal(1, fake.QueryCalls);
    Equal(1, result.Updated);
    var record = repository.Invoices.LoadOrCreate().Single();
    Equal(InvoiceSources.Mo, record.Source);
    Equal(RecordOrigins.Local, record.RecordOrigin);
}

static async Task TestRemoteAbsenceDoesNotDeleteAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    ConfigureProduction(repository);
    repository.Invoices.Append(MatchingRecord("EE12345678", "66091800222222"));
    var fake = new FakeGateway { Pages = { [1] = ListResponse(1, []) } };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(0, result.RemoteCount);
    Equal(1, repository.Invoices.LoadOrCreate().Count);
}

static async Task TestPaginationAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = LocalRepository.Open(temporary.Path, new TestProtector());
    ConfigureProduction(repository);
    var fake = new FakeGateway
    {
        Pages =
        {
            [1] = ListResponse(2, [Remote("FF12345678", "66091800333333", "100")]),
            [2] = ListResponse(2, [Remote("GG12345678", "66091800444444", "100")]),
        },
        QueryByInvoice =
        {
            ["FF12345678"] = Query("FF12345678", "66091800333333", "100", "買受人"),
            ["GG12345678"] = Query("GG12345678", "66091800444444", "100", "買受人"),
        },
    };
    var result = await Sync(repository, fake).SyncRecentAsync();
    Equal(2, fake.ListCalls);
    Equal(2, result.RemoteCount);
    Equal(2, result.Inserted);
}

static InvoiceSyncService Sync(LocalRepository repository, FakeGateway fake) => new(
    repository,
    (_, _) => fake,
    () => new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));

static void ConfigureProduction(LocalRepository repository)
{
    var settings = repository.Settings.LoadOrCreate();
    settings.Environment = Environments.Production;
    settings.ProductionInvoice = "12345675";
    repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
    repository.Settings.Save(settings);
}

static InvoiceRecord MatchingRecord(string invoiceNumber, string orderId) => new()
{
    Id = "match-" + invoiceNumber,
    SellerInvoice = "12345675",
    Environment = Environments.Production,
    RecordOrigin = RecordOrigins.Local,
    Source = InvoiceSources.Mo,
    OriginalOrderId = orderId,
    OrderId = orderId,
    ApiOrderId = orderId,
    InvoiceNumber = invoiceNumber,
    InvoiceState = InvoiceStates.Opened,
    InvoiceDate = "2026/09/18",
    InvoiceTime = "10:10:10",
    BuyerIdentifier = "0000000000",
    BuyerName = "買受人",
    Amount = 100,
    Delivery = InvoiceService.DeliveryPaper,
    UploadStatus = UploadStatuses.Complete,
    UploadStatusText = "完成",
    MainRemark = "備註",
    DetailVat = 1,
    Items = [new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 100, Amount = 100 }],
};

static InvoiceListItem Remote(
    string invoiceNumber,
    string orderId,
    string total,
    int status = UploadStatuses.Complete,
    string buyer = "買受人") => new(
        invoiceNumber,
        "07",
        status,
        "20260918",
        "101010",
        "0000000000",
        buyer,
        total,
        "0",
        total,
        "備註",
        "",
        "",
        "",
        "",
        0,
        orderId,
        0);

static InvoiceListResponse ListResponse(int pageTotal, IReadOnlyList<InvoiceListItem> data) =>
    new(0, "", pageTotal, 1, data.Count, data);

static QueryResponse Query(string invoiceNumber, string orderId, string total, string buyer) => new(
    0,
    "",
    new QueryResult(
        invoiceNumber,
        "07",
        UploadStatuses.Complete,
        "20260918",
        "101010",
        "0000000000",
        buyer,
        total,
        "0",
        total,
        "",
        "",
        "",
        "",
        0,
        orderId,
        0,
        ProductItems(),
        1,
        true));

static JsonElement ProductItems()
{
    using var document = JsonDocument.Parse("""
        [{"Description":"同步商品","Quantity":1,"Unit":"個","UnitPrice":100,"Amount":100,"TaxType":1,"Remark":""}]
        """);
    return document.RootElement.Clone();
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

sealed class FakeGateway : IAmegoGateway
{
    public Dictionary<int, InvoiceListResponse> Pages { get; } = [];
    public Dictionary<string, QueryResponse> QueryByInvoice { get; } = new(StringComparer.OrdinalIgnoreCase);
    public QueryResponse QueryResponse { get; set; } = null!;
    public Exception? QueryException { get; set; }
    public bool ThrowIfListCalled { get; set; }
    public int ListCalls { get; private set; }
    public int QueryCalls { get; private set; }

    public Task<InvoiceListResponse> ListInvoicesAsync(DateOnly startDate, DateOnly endDate, int page = 1, int limit = 500, CancellationToken cancellationToken = default)
    {
        if (ThrowIfListCalled) throw new InvalidOperationException("shared test pool must not be listed");
        ListCalls++;
        return Task.FromResult(Pages.TryGetValue(page, out var response) ? response : ListResponse(page, []));
    }

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        if (QueryException is not null) return Task.FromException<QueryResponse>(QueryException);
        if (QueryByInvoice.TryGetValue(number, out var response)) return Task.FromResult(response);
        return Task.FromResult(QueryResponse);
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return QueryException is null ? Task.FromResult(QueryResponse) : Task.FromException<QueryResponse>(QueryException);
    }

    public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<IssueResponse>(new NotSupportedException());
    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StatusResponse(0, "", []));
    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BanResponse(0, "", []));
    public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
        Task.FromException<byte[]>(new NotSupportedException());
}

sealed class TestProtector : ISecretProtector
{
    private const string Prefix = "test-protected:";
    public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
    public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
        ? Convert.FromBase64String(ciphertext[Prefix.Length..])
        : throw new InvalidDataException("invalid protected test value");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
