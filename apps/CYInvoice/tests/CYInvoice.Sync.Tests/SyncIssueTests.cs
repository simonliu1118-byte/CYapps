using System.Runtime.CompilerServices;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Sync.Tests;

internal static class SyncIssueTests
{
    [ModuleInitializer]
    internal static void RunAsPartOfSyncTests()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("query issue forces retry and resolves after success", QueryFailureForcesRetryAndResolvesAsync),
            ("invoice list issue resolves after later success", InvoiceListFailureResolvesAsync),
            ("ambiguous local match records issue and skips update", AmbiguousMatchIsRecordedAsync),
            ("unknown test record not found stays unresolved until confirmed", UnknownNotFoundResolvesAfterConfirmationAsync),
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                test.Run().GetAwaiter().GetResult();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                failures.Add($"{test.Name}: {error.Message}");
                Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
            }
        }

        if (failures.Count != 0)
            throw new InvalidOperationException("sync issue tests failed: " + string.Join(" | ", failures));
    }

    private static async Task QueryFailureForcesRetryAndResolvesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(Record("query-retry", "QA12345678", "66091800111111", buyer: "舊買受人"));

        var gateway = new FakeGateway
        {
            QueryException = new InvalidOperationException("forced query failure"),
            QueryResponse = Query("QA12345678", "66091800111111", "100", "新買受人"),
        };
        gateway.Page = ListResponse([Remote("QA12345678", "66091800111111", "100", buyer: "新買受人")]);
        var service = Sync(repository, gateway);

        var first = await service.SyncRecentAsync();
        True(first.Problems.Count != 0);
        Equal(1, gateway.QueryCalls);
        var store = new InvoiceSyncIssueStore(repository.DataDirectory);
        var firstIssues = store.Unresolved("prod|12345675");
        Equal(1, firstIssues.Count(issue => issue.IssueType == InvoiceSyncIssueTypes.QueryFailed));

        gateway.QueryException = null;
        var second = await service.SyncRecentAsync();
        Equal(2, gateway.QueryCalls);
        Equal(0, second.Problems.Count);
        Equal(0, store.Unresolved("prod|12345675").Count(issue => issue.IssueType == InvoiceSyncIssueTypes.QueryFailed));
        Equal("同步商品", repository.Invoices.LoadOrCreate().Single().Items.Single().Description);
    }

    private static async Task InvoiceListFailureResolvesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var gateway = new FakeGateway { ListException = new InvalidOperationException("forced list failure") };
        var service = Sync(repository, gateway);
        var store = new InvoiceSyncIssueStore(repository.DataDirectory);

        try
        {
            await service.SyncRecentAsync();
            throw new InvalidOperationException("expected list failure");
        }
        catch (InvalidOperationException error) when (error.Message == "forced list failure")
        {
        }
        Equal(1, store.Unresolved("prod|12345675").Count(issue => issue.IssueType == InvoiceSyncIssueTypes.InvoiceListFailed));

        gateway.ListException = null;
        gateway.Page = ListResponse([]);
        var result = await service.SyncRecentAsync();
        Equal(0, result.Problems.Count);
        Equal(0, store.Unresolved("prod|12345675").Count(issue => issue.IssueType == InvoiceSyncIssueTypes.InvoiceListFailed));
    }

    private static async Task AmbiguousMatchIsRecordedAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var first = Record("duplicate-a", "QB12345678", "66091800222222", buyer: "本機A");
        var second = Record("duplicate-b", "QB12345678", "66091800222222", buyer: "本機B");
        repository.Invoices.Append(first);
        repository.Invoices.Append(second);

        var gateway = new FakeGateway
        {
            Page = ListResponse([Remote("QB12345678", "66091800222222", "120", buyer: "官方新資料")]),
            QueryResponse = Query("QB12345678", "66091800222222", "120", "官方新資料"),
        };
        var result = await Sync(repository, gateway).SyncRecentAsync();
        True(result.Problems.Any(problem => problem.Contains("本機紀錄", StringComparison.Ordinal)));
        Equal(0, gateway.QueryCalls);
        var stored = repository.Invoices.LoadOrCreate().OrderBy(record => record.Id).ToArray();
        Equal("本機A", stored[0].BuyerName);
        Equal("本機B", stored[1].BuyerName);

        var issues = new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("prod|12345675");
        Equal(1, issues.Count(issue => issue.IssueType == InvoiceSyncIssueTypes.AmbiguousMatch));
    }

    private static async Task UnknownNotFoundResolvesAfterConfirmationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        var record = Record(
            "test-unknown",
            string.Empty,
            "M20260918001",
            buyer: "測試消費者",
            environment: Environments.Test,
            sellerInvoice: AmegoDefaults.TestInvoice);
        record.InvoiceState = InvoiceStates.Unknown;
        repository.Invoices.Append(record);

        var gateway = new FakeGateway { QueryException = new AmegoApiException(71, "查無發票") };
        var service = Sync(repository, gateway);
        var first = await service.SyncRecentAsync();
        True(first.Problems.Count != 0);
        var store = new InvoiceSyncIssueStore(repository.DataDirectory);
        Equal(1, store.Unresolved("test|12345678").Count(issue => issue.IssueType == InvoiceSyncIssueTypes.UnknownRemoteNotFound));

        gateway.QueryException = null;
        gateway.QueryResponse = Query("QC12345678", "M20260918001", "100", "測試消費者");
        var second = await service.SyncRecentAsync();
        Equal(0, second.Problems.Count);
        Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
        Equal(0, store.Unresolved("test|12345678").Count(issue => issue.IssueType == InvoiceSyncIssueTypes.UnknownRemoteNotFound));
    }

    private static InvoiceSyncService Sync(LocalRepository repository, FakeGateway gateway) => new(
        repository,
        (_, _) => gateway,
        () => new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));

    private static void ConfigureProduction(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
    }

    private static void ConfigureTest(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Test;
        repository.Settings.Save(settings);
    }

    private static InvoiceRecord Record(
        string id,
        string invoiceNumber,
        string orderId,
        string buyer,
        string environment = Environments.Production,
        string sellerInvoice = "12345675") => new()
        {
            Id = id,
            SellerInvoice = sellerInvoice,
            Environment = environment,
            RecordOrigin = RecordOrigins.Local,
            Source = InvoiceSourceInference.FromOrderId(orderId),
            OriginalOrderId = orderId,
            OrderId = orderId,
            ApiOrderId = orderId,
            InvoiceNumber = invoiceNumber,
            InvoiceState = InvoiceStates.Opened,
            InvoiceDate = "2026/09/18",
            InvoiceTime = "10:10:10",
            BuyerIdentifier = "0000000000",
            BuyerName = buyer,
            Amount = 100,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成",
            MainRemark = "備註",
            DetailVat = 1,
            Items =
            [
                new InvoiceItem
                {
                    Description = "商品",
                    Quantity = 1,
                    QuantityDecimal = "1",
                    Unit = "個",
                    UnitPrice = 100,
                    UnitPriceDecimal = "100",
                    TaxType = "1",
                    Amount = 100,
                    AmountDecimal = "100",
                },
            ],
        };

    private static InvoiceListItem Remote(
        string invoiceNumber,
        string orderId,
        string total,
        string buyer) => new(
            invoiceNumber, "07", UploadStatuses.Complete, "20260918", "101010",
            "0000000000", buyer, total, "0", total, "備註", "", "", "", "", 0, orderId, 0);

    private static InvoiceListResponse ListResponse(IReadOnlyList<InvoiceListItem> data) =>
        new(0, "", 1, 1, data.Count, data);

    private static QueryResponse Query(string invoiceNumber, string orderId, string total, string buyer) => new(
        0,
        "",
        new QueryResult(
            invoiceNumber, "07", UploadStatuses.Complete, "20260918", "101010",
            "0000000000", buyer, total, "0", total, "", "", "", "", 0,
            orderId, 0, ProductItems(total), 1, true));

    private static JsonElement ProductItems(string amount)
    {
        using var document = JsonDocument.Parse($$"""
            [{"Description":"同步商品","Quantity":1,"Unit":"個","UnitPrice":{{amount}},"Amount":{{amount}},"TaxType":1,"Remark":""}]
            """);
        return document.RootElement.Clone();
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("expected condition to be true");
    }

    private sealed class FakeGateway : IAmegoGateway
    {
        public InvoiceListResponse Page { get; set; } = ListResponse([]);
        public QueryResponse QueryResponse { get; set; } = null!;
        public Exception? ListException { get; set; }
        public Exception? QueryException { get; set; }
        public int QueryCalls { get; private set; }

        public Task<InvoiceListResponse> ListInvoicesAsync(
            DateOnly startDate,
            DateOnly endDate,
            int page = 1,
            int limit = 500,
            CancellationToken cancellationToken = default) =>
            ListException is null ? Task.FromResult(Page) : Task.FromException<InvoiceListResponse>(ListException);

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return QueryException is null ? Task.FromResult(QueryResponse) : Task.FromException<QueryResponse>(QueryException);
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

    private sealed class TestProtector : ISecretProtector
    {
        private const string Prefix = "test-protected:";
        public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
        public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(ciphertext[Prefix.Length..])
            : throw new InvalidDataException("invalid protected test value");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-sync-issue-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
