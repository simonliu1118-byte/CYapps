using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.SyncCoordinator.Tests;

internal static class DetailRefreshVoidStateTests
{
    private const string PendingMetadataKey = "cyinvoice_void_pending";

    public static async Task QueryWaitSetsWaitingVoidAndKeeps99Async()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendRecord(repository, pending: false);
        var gateway = new QueryGateway(Query(voidPending: true));
        var service = new InvoiceDetailRefreshService(repository, (_, _) => gateway, Clock);

        var refreshed = await service.RefreshAsync(record);

        Equal(InvoiceStates.OpenedWaitingVoid, refreshed.InvoiceState);
        Equal(UploadStatuses.Complete, refreshed.UploadStatus);
        Equal("完成", refreshed.UploadStatusText);
        True(PendingMarker(refreshed));
        Equal(1, gateway.QueryCalls);
    }

    public static async Task LocalPendingSurvivesTemporarilyMissingWaitAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendRecord(repository, pending: true);
        var gateway = new QueryGateway(Query());
        var service = new InvoiceDetailRefreshService(repository, (_, _) => gateway, Clock);

        var refreshed = await service.RefreshAsync(record);

        Equal(InvoiceStates.OpenedWaitingVoid, refreshed.InvoiceState);
        Equal(UploadStatuses.Complete, refreshed.UploadStatus);
        True(PendingMarker(refreshed));
        Equal(1, gateway.QueryCalls);
    }

    public static async Task ConfirmedCancelClearsPendingAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = OpenProduction(temporary.Path);
        var record = AppendRecord(repository, pending: true);
        const long cancelDate = 1789790000;
        var gateway = new QueryGateway(Query(cancelDate: cancelDate));
        var service = new InvoiceDetailRefreshService(repository, (_, _) => gateway, Clock);

        var refreshed = await service.RefreshAsync(record);

        Equal(InvoiceStates.Voided, refreshed.InvoiceState);
        Equal(UploadStatuses.Complete, refreshed.UploadStatus);
        False(PendingMarker(refreshed));
        Equal(cancelDate, InvoiceOfficialMetadata.CancelDate(refreshed));
        Equal(1, gateway.QueryCalls);
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

    private static InvoiceRecord AppendRecord(LocalRepository repository, bool pending)
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
            InvoiceState = pending ? InvoiceStates.OpenedWaitingVoid : InvoiceStates.Opened,
            Amount = 100,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成",
            InvoiceDate = "2026/09/19",
            InvoiceTime = "11:30:00",
            Items = [],
        };
        if (pending) MarkPending(record);
        repository.Invoices.Append(record);
        return record;
    }

    private static void MarkPending(InvoiceRecord record)
    {
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[PendingMetadataKey] = JsonSerializer.SerializeToElement(true);
    }

    private static bool PendingMarker(InvoiceRecord record)
    {
        if (record.ExtensionData is null || !record.ExtensionData.TryGetValue(PendingMetadataKey, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }

    private static QueryResponse Query(long cancelDate = 0, bool voidPending = false)
    {
        using var document = JsonDocument.Parse("[]");
        return new QueryResponse(
            0,
            string.Empty,
            new QueryResult(
                "AA12345678",
                "A0401",
                UploadStatuses.Complete,
                "20260919",
                "113000",
                string.Empty,
                "消費者",
                "95",
                "5",
                "100",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                cancelDate,
                "M20260919001",
                1789790000,
                document.RootElement.Clone(),
                1,
                true,
                voidPending));
    }

    private static DateTimeOffset Clock() =>
        new(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8));

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

    private sealed class QueryGateway(QueryResponse response) : IAmegoGateway
    {
        public int QueryCalls { get; private set; }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return Task.FromResult(response);
        }

        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            QueryByInvoiceNumberAsync(orderId, cancellationToken);

        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IssueResponse>(new NotSupportedException());

        public Task<InvoiceListResponse> ListInvoicesAsync(DateOnly startDate, DateOnly endDate, int page = 1, int limit = 500, CancellationToken cancellationToken = default) =>
            Task.FromException<InvoiceListResponse>(new NotSupportedException());

        public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StatusResponse(0, string.Empty, []));

        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BanResponse(0, string.Empty, []));

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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-detail-void-tests-" + Guid.NewGuid().ToString("N"));
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
