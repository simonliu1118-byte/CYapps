using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.SyncCoordinator.Tests;

internal static class AutomaticRetentionTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    public static async Task TestDailyTestScopePrunesExpiredWithoutQueryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        repository.Invoices.Append(TestRecord("expired", "TT00000001", "M20260917001", "2026/09/17"));
        var gateway = new FakeGateway();
        var automatic = Automatic(repository, gateway);

        var result = await automatic.SyncAsync();
        Equal(0, result.Problems.Count);
        Equal(1, gateway.ListCalls);
        Equal(Today, gateway.LastListStart);
        Equal(Today, gateway.LastListEnd);
        Equal(0, gateway.QueryCalls);
        Equal(0, repository.Invoices.LoadOrCreate().Count);
    }

    public static async Task TestDailyProblemsSkipRetentionAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        repository.Invoices.Append(TestRecord("expired", "TT00000001", "M20260917001", "2026/09/17"));
        var today = TestRecord("today", "TT00000002", "M20260918001", "2026/09/18");
        today.InvoiceState = InvoiceStates.Unknown;
        repository.Invoices.Append(today);
        var gateway = new FakeGateway();
        var automatic = Automatic(repository, gateway);

        var result = await automatic.SyncAsync();
        True(result.Problems.Count != 0);
        Equal(1, gateway.ListCalls);
        Equal(Today, gateway.LastListStart);
        Equal(Today, gateway.LastListEnd);
        Equal(1, gateway.QueryCalls);
        var ids = repository.Invoices.LoadOrCreate().Select(record => record.Id).OrderBy(id => id).ToArray();
        SequenceEqual(new[] { "expired", "today" }, ids);
    }

    private static InvoiceAutomaticSyncService Automatic(LocalRepository repository, FakeGateway gateway)
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8));
        var service = new InvoiceSyncService(repository, (_, _) => gateway, () => now);
        return new InvoiceAutomaticSyncService(repository, service, () => now);
    }

    private static void ConfigureTest(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Test;
        repository.Settings.Save(settings);
    }

    private static InvoiceRecord TestRecord(string id, string invoiceNumber, string orderId, string invoiceDate) => new()
    {
        Id = id,
        Environment = Environments.Test,
        Source = "手動",
        RecordOrigin = RecordOrigins.Local,
        OriginalOrderId = orderId,
        OrderId = orderId,
        ApiOrderId = orderId,
        InvoiceNumber = invoiceNumber,
        InvoiceState = InvoiceStates.Opened,
        InvoiceDate = invoiceDate,
        InvoiceTime = "10:00:00",
        SentAt = invoiceDate + " 10:00:00",
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

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("expected condition to be true");
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
    }

    private sealed class FakeGateway : IAmegoGateway
    {
        public int ListCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public DateOnly? LastListStart { get; private set; }
        public DateOnly? LastListEnd { get; private set; }

        public Task<InvoiceListResponse> ListInvoicesAsync(
            DateOnly startDate,
            DateOnly endDate,
            int page = 1,
            int limit = 500,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            LastListStart = startDate;
            LastListEnd = endDate;
            return Task.FromResult(new InvoiceListResponse(0, "", 1, page, 0, []));
        }

        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return Task.FromException<QueryResponse>(new InvalidOperationException("forced test query failure"));
        }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            return Task.FromException<QueryResponse>(new InvalidOperationException("forced test query failure"));
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-auto-retention-tests-" + Guid.NewGuid().ToString("N"));
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
