using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Sync.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("source inference follows current OrderID", TestSourceInferenceAsync),
            ("production sync discovers remote invoice and queries details", TestProductionDiscoveryAsync),
            ("production sync overwrites cache and re-infers source", TestProductionOverwriteAsync),
            ("unchanged production summary skips invoice query", TestUnchangedSkipsQueryAsync),
            ("test environment does not discover shared pool invoices", TestTestEnvironmentNoDiscoveryAsync),
            ("test environment skips definite failed records", TestTestEnvironmentSkipsFailedAsync),
            ("remote absence never deletes local cache", TestRemoteAbsenceDoesNotDeleteAsync),
            ("invoice list pagination is exhausted", TestPaginationAsync),
            ("detail refresh always queries even when official data is unchanged", TestDetailRefreshAlwaysQueriesAsync),
            ("detail refresh overwrites official fields and invalidates cache", TestDetailRefreshOverwriteAsync),
            ("detail refresh failure leaves local cache untouched", TestDetailRefreshFailureLeavesCacheAsync),
            ("detail refresh falls back to OrderID when invoice number is missing", TestDetailRefreshByOrderIdAsync),
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
    }

    private static Task TestSourceInferenceAsync()
    {
        Equal(InvoiceSources.Mo, InvoiceSourceInference.FromOrderId("66091800123456"));
        Equal(InvoiceSources.Coupang, InvoiceSourceInference.FromOrderId("115102696774269"));
        Equal(InvoiceSources.Digiwin, InvoiceSourceInference.FromOrderId("20260918001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001-R2"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("20260230001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("OTHER"));
        return Task.CompletedTask;
    }

    private static async Task TestProductionDiscoveryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var fake = new FakeGateway
        {
            QueryResponse = Query("AA12345678", "66091800123456", "100", "測試買受人"),
        };
        fake.Pages[1] = ListResponse(1, [Remote("AA12345678", "66091800123456", "100")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, result.Inserted);
        Equal(1, result.Queried);
        Equal(1, fake.ListCalls);
        Equal(1, fake.QueryCalls);
        var record = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Sync, record.RecordOrigin);
        Equal(InvoiceSources.Mo, record.Source);
        Equal(InvoiceSourceInference.SyncTag, InvoiceSourceInference.DisplayTag(record));
        Equal("12345675", record.SellerInvoice);
        Equal("AA12345678", record.InvoiceNumber);
        Equal("66091800123456", record.OrderId);
        Equal("測試買受人", record.BuyerName);
        Equal(100L, record.Amount);
        Equal("同步商品", record.Items.Single().Description);
    }

    private static async Task TestProductionOverwriteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(new InvoiceRecord
        {
            Id = "local-old", SellerInvoice = "12345675", RecordOrigin = RecordOrigins.Local,
            Environment = Environments.Production, Source = InvoiceSources.Manual,
            OriginalOrderId = "M20260918001", OrderId = "M20260918001", ApiOrderId = "M20260918001",
            InvoiceNumber = "BB12345678", InvoiceState = InvoiceStates.Opened,
            InvoiceDate = "2026/09/18", InvoiceTime = "09:00:00",
            BuyerIdentifier = "0000000000", BuyerName = "舊買受人", Amount = 80,
            Delivery = InvoiceService.DeliveryPaper, UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成", DetailVat = 1,
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
            QueryResponse = Query("BB12345678", "66091800999999", "120", "新買受人"),
        };
        fake.Pages[1] = ListResponse(1, [Remote("BB12345678", "66091800999999", "120", buyer: "新買受人")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, result.Updated);
        var record = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Local, record.RecordOrigin);
        Equal(InvoiceSources.Mo, record.Source);
        Equal(InvoiceSourceInference.UpdateTag, InvoiceSourceInference.DisplayTag(record));
        Equal("66091800999999", record.OrderId);
        Equal("新買受人", record.BuyerName);
        Equal(120L, record.Amount);
        Equal("同步商品", record.Items.Single().Description);
        Equal(false, File.Exists(pdf));
        Equal(false, File.Exists(preview));
    }

    private static async Task TestUnchangedSkipsQueryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(MatchingRecord("CC12345678", "66091800111111"));
        var fake = new FakeGateway { QueryException = new InvalidOperationException("query must not be called") };
        fake.Pages[1] = ListResponse(1, [Remote("CC12345678", "66091800111111", "100")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(0, result.Queried);
        Equal(0, fake.QueryCalls);
        Equal(1, result.Unchanged);
    }

    private static async Task TestTestEnvironmentNoDiscoveryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        repository.Invoices.Append(new InvoiceRecord
        {
            Id = "test-local", Environment = Environments.Test, SellerInvoice = AmegoDefaults.TestInvoice,
            RecordOrigin = RecordOrigins.Local, Source = InvoiceSources.Manual,
            OriginalOrderId = "M20260918001", OrderId = "M20260918001", ApiOrderId = "M20260918001",
            InvoiceNumber = "DD12345678", InvoiceState = InvoiceStates.Opened,
            InvoiceDate = "2026/09/18", InvoiceTime = "10:00:00",
            BuyerIdentifier = "0000000000", BuyerName = "測試消費者", Amount = 100,
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
        Equal(InvoiceSourceInference.UpdateTag, InvoiceSourceInference.DisplayTag(record));
        Equal(RecordOrigins.Local, record.RecordOrigin);
    }

    private static async Task TestTestEnvironmentSkipsFailedAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Test;
        repository.Settings.Save(settings);
        repository.Invoices.Append(new InvoiceRecord
        {
            Id = "test-failed", Environment = Environments.Test, SellerInvoice = AmegoDefaults.TestInvoice,
            RecordOrigin = RecordOrigins.Local, Source = InvoiceSources.Manual,
            OriginalOrderId = "CUSTOM-FAILED", OrderId = "CUSTOM-FAILED", ApiOrderId = "CUSTOM-FAILED",
            InvoiceState = InvoiceStates.Failed, InvoiceDate = "2026/09/18", InvoiceTime = "10:00:00",
            BuyerName = "測試消費者", Amount = 100, Delivery = InvoiceService.DeliveryPaper,
            ErrorMessage = "光貿明確拒絕開立",
            Items = [new InvoiceItem { Description = "測試商品", Quantity = 1, UnitPrice = 100, Amount = 100 }],
        });
        var fake = new FakeGateway
        {
            ThrowIfListCalled = true,
            QueryException = new AmegoApiException(71, "查無發票"),
        };

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(0, fake.ListCalls);
        Equal(0, fake.QueryCalls);
        Equal(0, result.Queried);
        Equal(0, result.Problems.Count);
        Equal(0, new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("test|12345678").Count);
    }

    private static async Task TestRemoteAbsenceDoesNotDeleteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(MatchingRecord("EE12345678", "66091800222222"));
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(0, result.RemoteCount);
        Equal(1, repository.Invoices.LoadOrCreate().Count);
    }

    private static async Task TestPaginationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(2, [Remote("FF12345678", "66091800333333", "100")]);
        fake.Pages[2] = ListResponse(2, [Remote("GG12345678", "66091800444444", "100")]);
        fake.QueryByInvoice["FF12345678"] = Query("FF12345678", "66091800333333", "100", "買受人");
        fake.QueryByInvoice["GG12345678"] = Query("GG12345678", "66091800444444", "100", "買受人");

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(2, fake.ListCalls);
        Equal(2, result.RemoteCount);
        Equal(2, result.Inserted);
    }

    private static async Task TestDetailRefreshAlwaysQueriesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("HH12345678", "66091800555555");
        repository.Invoices.Append(original);
        var cache = CreatePdfCache(repository, original.InvoiceNumber);
        var fake = new FakeGateway
        {
            QueryResponse = Query(
                "HH12345678", "66091800555555", "100", "買受人",
                description: "商品", buyerIdentifier: "0000000000"),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(1, fake.QueryByInvoiceCalls);
        Equal(0, fake.QueryByOrderCalls);
        Equal("HH12345678", fake.LastInvoiceQuery);
        Equal("HH12345678", fresh.InvoiceNumber);
        Equal(true, File.Exists(cache));
    }

    private static async Task TestDetailRefreshOverwriteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("JJ12345678", "M20260918001");
        original.Source = InvoiceSources.Manual;
        original.OriginalOrderId = "M20260918001";
        repository.Invoices.Append(original);
        var pdf = CreatePdfCache(repository, original.InvoiceNumber);
        var preview = CreatePreviewCache(repository, original.InvoiceNumber);
        var fake = new FakeGateway
        {
            QueryResponse = Query(
                "JJ12345678", "66091800666666", "120", "",
                cancelDate: 1, description: "新商品", buyerIdentifier: ""),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(RecordOrigins.Local, fresh.RecordOrigin);
        Equal("M20260918001", fresh.OriginalOrderId);
        Equal("66091800666666", fresh.OrderId);
        Equal("66091800666666", fresh.ApiOrderId);
        Equal(InvoiceSources.Mo, fresh.Source);
        Equal(InvoiceSourceInference.UpdateTag, InvoiceSourceInference.DisplayTag(fresh));
        Equal(InvoiceStates.Voided, fresh.InvoiceState);
        Equal(string.Empty, fresh.BuyerIdentifier);
        Equal(string.Empty, fresh.BuyerName);
        Equal(120L, fresh.Amount);
        Equal("新商品", fresh.Items.Single().Description);
        Equal(false, File.Exists(pdf));
        Equal(false, File.Exists(preview));
    }

    private static async Task TestDetailRefreshFailureLeavesCacheAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("KK12345678", "66091800777777");
        repository.Invoices.Append(original);
        var fake = new FakeGateway { QueryException = new InvalidOperationException("forced detail query failure") };

        try
        {
            await DetailRefresh(repository, fake).RefreshAsync(original);
            throw new InvalidOperationException("expected detail refresh to fail");
        }
        catch (InvalidOperationException error) when (error.Message == "forced detail query failure")
        {
        }

        var stored = repository.Invoices.LoadOrCreate().Single();
        Equal("KK12345678", stored.InvoiceNumber);
        Equal("66091800777777", stored.OrderId);
        Equal("買受人", stored.BuyerName);
        Equal(100L, stored.Amount);
        Equal("商品", stored.Items.Single().Description);
    }

    private static async Task TestDetailRefreshByOrderIdAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("LL12345678", "M20260918002");
        original.InvoiceNumber = string.Empty;
        original.InvoiceState = InvoiceStates.Unknown;
        repository.Invoices.Append(original);
        var fake = new FakeGateway
        {
            QueryResponse = Query("LL12345678", "M20260918002", "100", "買受人", description: "商品", buyerIdentifier: "0000000000"),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(0, fake.QueryByInvoiceCalls);
        Equal(1, fake.QueryByOrderCalls);
        Equal("M20260918002", fake.LastOrderQuery);
        Equal("LL12345678", fresh.InvoiceNumber);
        Equal(InvoiceStates.Opened, fresh.InvoiceState);
    }

    private static InvoiceSyncService Sync(LocalRepository repository, FakeGateway fake) => new(
        repository,
        (_, _) => fake,
        () => new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));

    private static InvoiceDetailRefreshService DetailRefresh(LocalRepository repository, FakeGateway fake) => new(
        repository,
        (_, _) => fake,
        () => new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));

    private static void ConfigureProduction(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
    }

    private static InvoiceRecord MatchingRecord(string invoiceNumber, string orderId) => new()
    {
        Id = "match-" + (invoiceNumber.Length == 0 ? orderId : invoiceNumber), SellerInvoice = "12345675", Environment = Environments.Production,
        RecordOrigin = RecordOrigins.Local, Source = InvoiceSourceInference.FromOrderId(orderId), OriginalOrderId = orderId,
        OrderId = orderId, ApiOrderId = orderId, InvoiceNumber = invoiceNumber, InvoiceState = InvoiceStates.Opened,
        InvoiceDate = "2026/09/18", InvoiceTime = "10:10:10", BuyerIdentifier = "0000000000",
        BuyerName = "買受人", Amount = 100, Delivery = InvoiceService.DeliveryPaper,
        UploadStatus = UploadStatuses.Complete, UploadStatusText = "完成", MainRemark = "備註", DetailVat = 1,
        Items = [new InvoiceItem
        {
            Description = "商品", Quantity = 1, QuantityDecimal = "1", Unit = "個",
            UnitPrice = 100, UnitPriceDecimal = "100", TaxType = "1",
            Amount = 100, AmountDecimal = "100", Remark = string.Empty,
        }],
    };

    private static InvoiceListItem Remote(
        string invoiceNumber,
        string orderId,
        string total,
        int status = UploadStatuses.Complete,
        string buyer = "買受人") => new(
            invoiceNumber, "07", status, "20260918", "101010", "0000000000", buyer,
            total, "0", total, "備註", "", "", "", "", 0, orderId, 0);

    private static InvoiceListResponse ListResponse(int pageTotal, IReadOnlyList<InvoiceListItem> data) =>
        new(0, "", pageTotal, 1, data.Count, data);

    private static QueryResponse Query(
        string invoiceNumber,
        string orderId,
        string total,
        string buyer,
        long cancelDate = 0,
        string description = "同步商品",
        string buyerIdentifier = "0000000000") => new(
        0,
        "",
        new QueryResult(
            invoiceNumber, "07", UploadStatuses.Complete, "20260918", "101010",
            buyerIdentifier, buyer, total, "0", total, "", "", "", "", cancelDate,
            orderId, 0, ProductItems(description, total), 1, true));

    private static JsonElement ProductItems(string description = "同步商品", string amount = "100")
    {
        using var document = JsonDocument.Parse($$"""
            [{"Description":{{JsonSerializer.Serialize(description)}},"Quantity":1,"Unit":"個","UnitPrice":{{amount}},"Amount":{{amount}},"TaxType":1,"Remark":""}]
            """);
        return document.RootElement.Clone();
    }

    private static string CreatePdfCache(LocalRepository repository, string invoiceNumber)
    {
        var directory = Path.Combine(repository.InvoicePdfCacheDirectory, Environments.Production, "20260918");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, invoiceNumber + "_style0.pdf");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        return path;
    }

    private static string CreatePreviewCache(LocalRepository repository, string invoiceNumber)
    {
        var directory = Path.Combine(repository.InvoicePreviewCacheDirectory, Environments.Production, "20260918");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, invoiceNumber + "_style0.page1.png");
        File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
        return path;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private sealed class FakeGateway : IAmegoGateway
    {
        public Dictionary<int, InvoiceListResponse> Pages { get; } = [];
        public Dictionary<string, QueryResponse> QueryByInvoice { get; } = new(StringComparer.OrdinalIgnoreCase);
        public QueryResponse QueryResponse { get; set; } = null!;
        public Exception? QueryException { get; set; }
        public bool ThrowIfListCalled { get; set; }
        public int ListCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int QueryByInvoiceCalls { get; private set; }
        public int QueryByOrderCalls { get; private set; }
        public string LastInvoiceQuery { get; private set; } = string.Empty;
        public string LastOrderQuery { get; private set; } = string.Empty;

        public Task<InvoiceListResponse> ListInvoicesAsync(DateOnly startDate, DateOnly endDate, int page = 1, int limit = 500, CancellationToken cancellationToken = default)
        {
            if (ThrowIfListCalled) throw new InvalidOperationException("shared test pool must not be listed");
            ListCalls++;
            return Task.FromResult(Pages.TryGetValue(page, out var response)
                ? response
                : new InvoiceListResponse(0, "", page, page, 0, []));
        }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            QueryByInvoiceCalls++;
            LastInvoiceQuery = number;
            if (QueryException is not null) return Task.FromException<QueryResponse>(QueryException);
            if (QueryByInvoice.TryGetValue(number, out var response)) return Task.FromResult(response);
            return Task.FromResult(QueryResponse);
        }

        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            QueryByOrderCalls++;
            LastOrderQuery = orderId;
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-sync-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
