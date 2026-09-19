using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.SyncCoordinator.Tests;

internal static class PendingVoidSyncTests
{
    public static async Task RecentListConfirmsVoidAndInvalidatesCacheAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendPendingRecord(repository, "2026/09/19");
        var pdf = CreateCache(repository.InvoicePdfCacheDirectory, Environments.Production, record.InvoiceNumber, ".pdf");
        var preview = CreateCache(repository.InvoicePreviewCacheDirectory, Environments.Production, record.InvoiceNumber, ".png");
        var clock = new TestClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8)));
        var syncGateway = new ListGateway
        {
            Items = [ListItem(cancelDate: 1789790000)],
            Query = Query(cancelDate: 1789790000),
        };
        var voidGateway = new VoidStatusGateway();
        var coordinator = Coordinator(repository, syncGateway, voidGateway, clock);

        var result = await coordinator.RunManualAsync();

        Equal(InvoiceSyncRunStatus.Completed, result.Status);
        var saved = repository.Invoices.LoadOrCreate().Single(item => item.Id == record.Id);
        Equal(InvoiceStates.Voided, saved.InvoiceState);
        Equal(UploadStatuses.Complete, saved.UploadStatus);
        False(InvoiceVoidService.HasPendingMarker(saved));
        Equal(0, voidGateway.QueryCalls);
        Equal(0, voidGateway.VoidCalls);
        False(File.Exists(pdf));
        False(File.Exists(preview));
    }

    public static async Task RecentListKeepsWaitingVoidAndStatus99Async()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendPendingRecord(repository, "2026/09/19");
        var clock = new TestClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8)));
        var syncGateway = new ListGateway
        {
            Items = [ListItem()],
            Query = Query(),
        };
        var voidGateway = new VoidStatusGateway();
        var coordinator = Coordinator(repository, syncGateway, voidGateway, clock);

        var result = await coordinator.RunScheduledAsync();

        Equal(InvoiceSyncRunStatus.Completed, result.Status);
        var saved = repository.Invoices.LoadOrCreate().Single(item => item.Id == record.Id);
        Equal(InvoiceStates.OpenedWaitingVoid, saved.InvoiceState);
        Equal(UploadStatuses.Complete, saved.UploadStatus);
        Equal("完成", saved.UploadStatusText);
        True(InvoiceVoidService.HasPendingMarker(saved));
        Equal(0, voidGateway.QueryCalls);
        Equal(0, voidGateway.VoidCalls);
    }

    public static async Task OldPendingUsesSingleQueryAndNeverResendsAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendPendingRecord(repository, "2026/09/10");
        var clock = new TestClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8)));
        var syncGateway = new ListGateway();
        var voidGateway = new VoidStatusGateway
        {
            Query = Query(),
            Status = new StatusResponse(0, "", [new StatusResult(record.InvoiceNumber, "A0401", UploadStatuses.Complete, "100")]),
        };
        var coordinator = Coordinator(repository, syncGateway, voidGateway, clock);

        var result = await coordinator.RunManualAsync();

        Equal(InvoiceSyncRunStatus.Completed, result.Status);
        var saved = repository.Invoices.LoadOrCreate().Single(item => item.Id == record.Id);
        Equal(InvoiceStates.Opened, saved.InvoiceState);
        Equal(UploadStatuses.Complete, saved.UploadStatus);
        False(InvoiceVoidService.HasPendingMarker(saved));
        Equal(1, voidGateway.QueryCalls);
        Equal(0, voidGateway.VoidCalls);
    }

    public static async Task OldPendingStillWaitingCreatesIssueAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendPendingRecord(repository, "2026/09/10");
        var clock = new TestClock(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8)));
        var syncGateway = new ListGateway();
        var voidGateway = new VoidStatusGateway
        {
            Query = Query(voidPending: true),
            Status = new StatusResponse(0, "", []),
        };
        var coordinator = Coordinator(repository, syncGateway, voidGateway, clock);

        var result = await coordinator.RunManualAsync();

        Equal(InvoiceSyncRunStatus.Completed, result.Status);
        var saved = repository.Invoices.LoadOrCreate().Single(item => item.Id == record.Id);
        Equal(InvoiceStates.OpenedWaitingVoid, saved.InvoiceState);
        Equal(UploadStatuses.Complete, saved.UploadStatus);
        True(InvoiceVoidService.HasPendingMarker(saved));
        Equal(1, voidGateway.QueryCalls);
        Equal(0, voidGateway.VoidCalls);
        var issue = new InvoiceSyncIssueStore(repository.DataDirectory)
            .Unresolved(Environments.Production + "|12345675")
            .Single(item => item.InvoiceNumber == record.InvoiceNumber);
        Equal(InvoiceVoidSyncIssueTypes.PendingConfirmation, issue.IssueType);
    }

    private static InvoiceSyncCoordinator Coordinator(
        LocalRepository repository,
        ListGateway syncGateway,
        VoidStatusGateway voidGateway,
        TestClock clock)
    {
        var sync = new InvoiceSyncService(repository, (_, _) => syncGateway, clock.Now);
        var voidService = new InvoiceVoidService(repository, (_, _) => voidGateway, clock.Now);
        var automatic = new InvoiceAutomaticSyncService(repository, sync, clock.Now, voidService);
        return new InvoiceSyncCoordinator(sync, clock.Now, TimeSpan.FromSeconds(30), automatic);
    }

    private static LocalRepository OpenProduction(string path)
    {
        var repository = LocalRepository.Open(path, new TestProtector());
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
        return repository;
    }

    private static InvoiceRecord AppendPendingRecord(LocalRepository repository, string invoiceDate)
    {
        var record = new InvoiceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SellerInvoice = "12345675",
            Environment = Environments.Production,
            Source = InvoiceSources.Manual,
            RecordOrigin = RecordOrigins.Local,
            OriginalOrderId = "M20260919001",
            OrderId = "M20260919001",
            ApiOrderId = "M20260919001",
            InvoiceNumber = "AA12345678",
            InvoiceState = InvoiceStates.OpenedWaitingVoid,
            Amount = 100,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成",
            InvoiceDate = invoiceDate,
            InvoiceTime = "11:30:00",
            Items = [],
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["cyinvoice_void_pending"] = JsonSerializer.SerializeToElement(true),
            },
        };
        repository.Invoices.Append(record);
        return record;
    }

    private static string CreateCache(string root, string environment, string invoiceNumber, string extension)
    {
        var directory = Path.Combine(root, environment);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, invoiceNumber + "_style1" + extension);
        File.WriteAllText(path, "stale");
        return path;
    }

    private static InvoiceListItem ListItem(long cancelDate = 0) => new(
        "AA12345678", "A0401", UploadStatuses.Complete, "20260919", "113000", "", "消費者",
        "95", "5", "100", "", "", "", "", "", cancelDate, "M20260919001", 1789790000);

    private static QueryResponse Query(long cancelDate = 0, bool voidPending = false) => new(
        0,
        "",
        new QueryResult(
            "AA12345678", "A0401", UploadStatuses.Complete, "20260919", "113000", "", "消費者",
            "95", "5", "100", "", "", "", "", cancelDate, "M20260919001",
            1789790000, default, 1, true, voidPending));

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("expected condition to be true");
    }

    private static void False(bool value)
    {
        if (value) throw new InvalidOperationException("expected condition to be false");
    }

    private sealed class TestClock(DateTimeOffset value)
    {
        private readonly DateTimeOffset current = value;
        public DateTimeOffset Now() => current;
    }

    private sealed class ListGateway : IAmegoGateway
    {
        public IReadOnlyList<InvoiceListItem> Items { get; set; } = [];
        public QueryResponse Query { get; set; } = PendingVoidSyncTests.Query();

        public Task<InvoiceListResponse> ListInvoicesAsync(
            DateOnly startDate,
            DateOnly endDate,
            int page = 1,
            int limit = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InvoiceListResponse(0, "", 1, page, Items.Count, Items));

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult(Query);
        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IssueResponse>(new NotSupportedException());
        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            Task.FromException<QueryResponse>(new NotSupportedException());
        public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StatusResponse(0, "", []));
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BanResponse(0, "", []));
        public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new NotSupportedException());
    }

    private sealed class VoidStatusGateway : IAmegoGateway
    {
        public QueryResponse Query { get; set; } = PendingVoidSyncTests.Query();
        public StatusResponse Status { get; set; } = new(0, "", []);
        public int QueryCalls { get; private set; }
        public int VoidCalls { get; private set; }

        public Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default)
        {
            VoidCalls++;
            return Task.FromResult(new VoidResponse(0, "OK"));
        }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return Task.FromResult(Query);
        }

        public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult(Status);
        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IssueResponse>(new NotSupportedException());
        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            Task.FromException<QueryResponse>(new NotSupportedException());
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BanResponse(0, "", []));
        public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new NotSupportedException());
        public Task<InvoiceListResponse> ListInvoicesAsync(
            DateOnly startDate,
            DateOnly endDate,
            int page = 1,
            int limit = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromException<InvoiceListResponse>(new NotSupportedException());
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-pending-void-sync-tests-" + Guid.NewGuid().ToString("N"));
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
