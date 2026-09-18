using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.SyncCoordinator.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("manual sync enforces 30-second cooldown", TestManualCooldownAsync),
            ("startup scheduled and manual share one busy gate", TestBusyGateAsync),
            ("failed manual sync does not create cooldown", TestFailedManualDoesNotCooldownAsync),
            ("automatic sync runs two-period reconciliation once per local day", TestDailyBroadThenRecentAsync),
            ("manual sync stays recent even when daily reconciliation is due", TestManualStaysRecentAsync),
            ("failed daily reconciliation remains due", TestFailedDailyRemainsDueAsync),
            ("test daily scope prunes expired rows without querying them", AutomaticRetentionTests.TestDailyTestScopePrunesExpiredWithoutQueryAsync),
            ("daily reconciliation problems skip retention", AutomaticRetentionTests.TestDailyProblemsSkipRetentionAsync),
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

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} coordinator tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task TestManualCooldownAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway();
        var coordinator = Coordinator(repository, gateway, clock);

        var first = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Completed, first.Status);
        Equal(1, gateway.ListCalls);

        var second = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Cooldown, second.Status);
        True(second.CooldownRemaining > TimeSpan.FromSeconds(29));
        Equal(1, gateway.ListCalls);

        clock.Advance(TimeSpan.FromSeconds(31));
        var third = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Completed, third.Status);
        Equal(2, gateway.ListCalls);
    }

    private static async Task TestBusyGateAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway { BlockFirstList = true };
        var coordinator = Coordinator(repository, gateway, clock);

        var startupTask = coordinator.RunStartupAsync();
        await gateway.FirstListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var scheduledWhileBusy = await coordinator.RunScheduledAsync();
        var manualWhileBusy = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Busy, scheduledWhileBusy.Status);
        Equal(InvoiceSyncRunStatus.Busy, manualWhileBusy.Status);
        Equal(1, gateway.ListCalls);

        gateway.ReleaseFirstList.TrySetResult(true);
        var startup = await startupTask;
        Equal(InvoiceSyncRunStatus.Completed, startup.Status);

        var scheduledAfter = await coordinator.RunScheduledAsync();
        Equal(InvoiceSyncRunStatus.Completed, scheduledAfter.Status);
        Equal(2, gateway.ListCalls);
    }

    private static async Task TestFailedManualDoesNotCooldownAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway { FailNextList = true };
        var coordinator = Coordinator(repository, gateway, clock);

        try
        {
            await coordinator.RunManualAsync();
            throw new InvalidOperationException("expected the first manual sync to fail");
        }
        catch (InvalidOperationException error) when (error.Message == FakeGateway.FailureMessage)
        {
        }

        var retry = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Completed, retry.Status);
        Equal(2, gateway.ListCalls);
    }

    private static async Task TestDailyBroadThenRecentAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway();
        var coordinator = AutomaticCoordinator(repository, gateway, clock);

        var startup = await coordinator.RunStartupAsync();
        Equal(InvoiceSyncRunStatus.Completed, startup.Status);
        Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 18)), gateway.Requests[0]);

        var scheduledSameDay = await coordinator.RunScheduledAsync();
        Equal(InvoiceSyncRunStatus.Completed, scheduledSameDay.Status);
        Equal((new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 18)), gateway.Requests[1]);

        clock.Advance(TimeSpan.FromDays(1));
        var scheduledNextDay = await coordinator.RunScheduledAsync();
        Equal(InvoiceSyncRunStatus.Completed, scheduledNextDay.Status);
        Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 19)), gateway.Requests[2]);
    }

    private static async Task TestManualStaysRecentAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway();
        var coordinator = AutomaticCoordinator(repository, gateway, clock);

        var manual = await coordinator.RunManualAsync();
        Equal(InvoiceSyncRunStatus.Completed, manual.Status);
        Equal((new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 18)), gateway.Requests[0]);

        var scheduled = await coordinator.RunScheduledAsync();
        Equal(InvoiceSyncRunStatus.Completed, scheduled.Status);
        Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 18)), gateway.Requests[1]);
    }

    private static async Task TestFailedDailyRemainsDueAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = LocalRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var clock = new TestClock(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.FromHours(8)));
        var gateway = new FakeGateway { FailNextList = true };
        var coordinator = AutomaticCoordinator(repository, gateway, clock);

        try
        {
            await coordinator.RunStartupAsync();
            throw new InvalidOperationException("expected daily reconciliation to fail");
        }
        catch (InvalidOperationException error) when (error.Message == FakeGateway.FailureMessage)
        {
        }
        Equal((new DateOnly(2025, 11, 1), new DateOnly(2026, 1, 15)), gateway.Requests[0]);

        var retry = await coordinator.RunScheduledAsync();
        Equal(InvoiceSyncRunStatus.Completed, retry.Status);
        Equal((new DateOnly(2025, 11, 1), new DateOnly(2026, 1, 15)), gateway.Requests[1]);
    }

    private static InvoiceSyncCoordinator Coordinator(LocalRepository repository, FakeGateway gateway, TestClock clock)
    {
        var service = new InvoiceSyncService(repository, (_, _) => gateway, clock.Now);
        return new InvoiceSyncCoordinator(service, clock.Now, TimeSpan.FromSeconds(30));
    }

    private static InvoiceSyncCoordinator AutomaticCoordinator(LocalRepository repository, FakeGateway gateway, TestClock clock)
    {
        var service = new InvoiceSyncService(repository, (_, _) => gateway, clock.Now);
        var automatic = new InvoiceAutomaticSyncService(repository, service, clock.Now);
        return new InvoiceSyncCoordinator(service, clock.Now, TimeSpan.FromSeconds(30), automatic);
    }

    private static void ConfigureProduction(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
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

    private sealed class TestClock(DateTimeOffset value)
    {
        private DateTimeOffset current = value;
        public DateTimeOffset Now() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class FakeGateway : IAmegoGateway
    {
        public const string FailureMessage = "forced invoice_list failure";
        private readonly Lock requestsGate = new();
        private readonly List<(DateOnly Start, DateOnly End)> requests = [];
        private int listCalls;
        private int firstListBlocked;

        public bool BlockFirstList { get; set; }
        public bool FailNextList { get; set; }
        public int ListCalls => Volatile.Read(ref listCalls);
        public IReadOnlyList<(DateOnly Start, DateOnly End)> Requests
        {
            get { lock (requestsGate) return requests.ToArray(); }
        }
        public TaskCompletionSource<bool> FirstListStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseFirstList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<InvoiceListResponse> ListInvoicesAsync(
            DateOnly startDate,
            DateOnly endDate,
            int page = 1,
            int limit = 500,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref listCalls);
            lock (requestsGate) requests.Add((startDate, endDate));
            if (FailNextList)
            {
                FailNextList = false;
                throw new InvalidOperationException(FailureMessage);
            }

            if (BlockFirstList && Interlocked.Exchange(ref firstListBlocked, 1) == 0)
            {
                FirstListStarted.TrySetResult(true);
                await ReleaseFirstList.Task.WaitAsync(cancellationToken);
            }

            return new InvoiceListResponse(0, "", 1, page, 0, []);
        }

        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IssueResponse>(new NotSupportedException());
        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            Task.FromException<QueryResponse>(new NotSupportedException());
        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromException<QueryResponse>(new NotSupportedException());
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-sync-coordinator-tests-" + Guid.NewGuid().ToString("N"));
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
