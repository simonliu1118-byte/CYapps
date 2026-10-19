using System.Net.Http;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class LocalAllowanceOneShotTests
{
    public static async Task RunAsync()
    {
        await AcceptedNeedsExactOfficialReadbackAsync();
        await TimeoutIsNeverAutomaticallyRetriedAsync();
        await CollisionOnlyRecordsOriginalNumberAsync();
        await PreflightFailureNeverSendsAsync();
    }

    private static async Task AcceptedNeedsExactOfficialReadbackAsync()
    {
        using var temp = new TemporaryDirectory("b2c-good");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        var official = ValidOfficial(saved);
        var fake = new StubAllowanceGateway { Official = official };
        var checkedCount = 0;
        var service = new LocalAllowanceOneShotService(ledger, fake, (_, _) =>
        {
            checkedCount++;
            return Task.CompletedTask;
        });
        var first = await service.SendFirstAsync(Seller, Environment, saved);
        Check(first.Outcome == LocalAllowanceSendOutcome.OfficiallyConfirmed,
            "code 0 alone is not enough: exact official readback must confirm");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "confirmed",
            "confirmed status must be committed");
        Check(fake.SendCount == 1 && fake.QueryCount == 1 && checkedCount == 1,
            "one preflight, one g0401, one official query");
        await ExpectAsync<InvalidOperationException>(() =>
            service.SendFirstAsync(Seller, Environment, saved));
        Check(fake.SendCount == 1, "a confirmed send must not be repeated");
    }

    private static async Task TimeoutIsNeverAutomaticallyRetriedAsync()
    {
        using var temp = new TemporaryDirectory("b2c-timeout");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        var fake = new StubAllowanceGateway { SendException = new TaskCanceledException("timeout") };
        var service = new LocalAllowanceOneShotService(ledger, fake, (_, _) => Task.CompletedTask);
        var first = await service.SendFirstAsync(Seller, Environment, saved);
        Check(first.Outcome == LocalAllowanceSendOutcome.PendingOfficialReadback,
            "timeout stays uncertain");
        Check(new LocalAllowanceNumberStore(temp.Path).FindLatest(Seller, Environment, Work)!.Status == "sending",
            "sending must survive restart");
        await ExpectAsync<InvalidOperationException>(() =>
            service.SendFirstAsync(Seller, Environment, saved));
        Check(fake.SendCount == 1, "never resend after a network timeout");
        fake.SendException = null;
        fake.Official = ValidOfficial(saved) with { BuyerIdentifier = "OTHER" };
        var mismatch = await service.ReconcileAsync(Seller, Environment, saved);
        Check(mismatch.Outcome == LocalAllowanceSendOutcome.PendingOfficialReadback,
            "another buyer's allowance cannot confirm our frozen request");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "sending",
            "mismatch never clears sending");
        fake.Official = ValidOfficial(saved);
        var verified = await service.ReconcileAsync(Seller, Environment, saved);
        Check(verified.Outcome == LocalAllowanceSendOutcome.OfficiallyConfirmed,
            "original number readback can complete without another send");
        Check(fake.SendCount == 1 && fake.QueryCount == 2,
            "readback only, no second write");
    }

    private static async Task CollisionOnlyRecordsOriginalNumberAsync()
    {
        using var temp = new TemporaryDirectory("b2c-collision");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        var fake = new StubAllowanceGateway
        {
            SendException = new AmegoApiException(
                AmegoAllowanceErrorCodes.ExistingIssuedAllowanceNumber, "occupied")
        };
        var service = new LocalAllowanceOneShotService(ledger, fake, (_, _) => Task.CompletedTask);
        var result = await service.SendFirstAsync(Seller, Environment, saved);
        Check(result.Outcome == LocalAllowanceSendOutcome.ConfirmedNumberCollision,
            "only explicit official rejection records collision");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "occupied_external",
            "the rejected number cannot be recycled");
        await ExpectAsync<InvalidOperationException>(() =>
            service.SendFirstAsync(Seller, Environment, saved));
        Check(fake.SendCount == 1, "no automatic increment or second POST");
    }

    private static async Task PreflightFailureNeverSendsAsync()
    {
        using var temp = new TemporaryDirectory("b2c-no-auth");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        var fake = new StubAllowanceGateway();
        var service = new LocalAllowanceOneShotService(ledger, fake, (_, _) =>
            Task.FromException(new UnauthorizedAccessException("manager revoked")));
        await ExpectAsync<UnauthorizedAccessException>(() =>
            service.SendFirstAsync(Seller, Environment, saved));
        Check(fake.SendCount == 0 && ledger.FindLatest(Seller, Environment, Work)!.Status == "reserved",
            "failed auth/preflight must not touch official g0401");
    }

    private static LocalAllowanceReservation Reserve(LocalAllowanceNumberStore ledger) =>
        ledger.Reserve(Seller, Environment, Work, new DateOnly(2026, 10, 19),
            new AllowanceIssueRequest
            {
                AllowanceDate = "20261019",
                AllowanceType = 2,
                BuyerIdentifier = "12345678",
                BuyerName = "Synthetic",
                ProductItem = [new AllowanceProductItem
                {
                    OriginalInvoiceNumber = "AA12345678",
                    OriginalInvoiceDate = 20261018,
                    OriginalDescription = "Synthetic item",
                    Quantity = 1m, UnitPrice = 100m, Amount = 100m, Tax = 5m, TaxType = 1,
                }],
                TotalAmount = 100m,
                TaxAmount = 5m,
            }, "2000", new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.FromHours(8)));

    private static AllowanceQueryItem ValidOfficial(LocalAllowanceReservation saved)
    {
        using var json = JsonDocument.Parse("""
            [{"original_invoice_number":"AA12345678","original_invoice_date":20261018,
              "original_description":"Synthetic item","quantity":"1","unit_price":"100",
              "amount":"100","tax":"5","tax_type":1}]
            """);
        return new AllowanceQueryItem(saved.AllowanceNumber, "D0401",
            UploadStatuses.Complete, "20261019", 2, "12345678", "Synthetic",
            "5", "100", 0, json.RootElement.Clone());
    }

    private const string Seller = "SYNTHETIC-SELLER";
    private const string Environment = "test";
    private const string Work = "synthetic-work-001";

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class StubAllowanceGateway : IAmegoAllowanceGateway
    {
        public int SendCount { get; private set; }
        public int QueryCount { get; private set; }
        public Exception? SendException { get; set; }
        public AllowanceQueryItem? Official { get; set; }
        public Task<AllowanceIssueResponse> IssueAllowanceOnceAsync(
            AllowanceIssueRequest request, CancellationToken ct = default)
        {
            SendCount++;
            if (SendException is not null)
                return Task.FromException<AllowanceIssueResponse>(SendException);
            return Task.FromResult(new AllowanceIssueResponse(0, ""));
        }
        public Task<AllowanceQueryResponse> QueryAllowanceAsync(
            string number, CancellationToken ct = default)
        {
            QueryCount++;
            return Official is null
                ? Task.FromException<AllowanceQueryResponse>(new AmegoApiException(71, "not found"))
                : Task.FromResult(new AllowanceQueryResponse(0, "", Official));
        }
    }
}
