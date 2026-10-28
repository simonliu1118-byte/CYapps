using System.Net.Http;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

internal static class LocalAllowanceOneShotTests
{
    public static async Task RunAsync()
    {
        await AcceptedNeedsExactOfficialReadbackAsync();
        await TimeoutIsNeverAutomaticallyRetriedAsync();
        await CollisionOnlyRecordsOriginalNumberAsync();
        await DefinitiveRejectionAdvancesWithoutManualReviewAsync();
        await PreflightFailureNeverSendsAsync();
        await AutoAdvanceOnlyOnExplicitRejectionAsync();
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
            "even complete readback is not proof that this POST created the allowance");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "confirmed",
            "full original-number content proof completes the operation");
        Check(ReadbackAuditCount(temp.Path)==1,
            "one immutable exact-readback evidence entry must be persisted");
        Check(AuditHashLength(temp.Path)==64,"snapshot hash, not raw buyer, is recorded");
        AssertReadbackAuditImmutable(temp.Path);

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
        var fake = new StubAllowanceGateway { SendException = new TaskCanceledException("SECRET_PAYLOAD_DO_NOT_EXPORT") };
        var events = new List<(string Stage, Exception? Error)>();
        var service = new LocalAllowanceOneShotService(ledger, fake, (_, _) => Task.CompletedTask,
            (stage, code, error) => {
                events.Add((stage, error));
                IssueDiagnosticLog.Record(temp.Path, 999, stage, "allowance_manual_review", code, error);
            });
        var first = await service.SendFirstAsync(Seller, Environment, saved);
        Check(first.Outcome == LocalAllowanceSendOutcome.Problem,
            "timeout stays uncertain");
        Check(events.Any(e => e.Stage == "response_unknown" && e.Error is TaskCanceledException),
            "the original exception reaches the existing diagnostic owner");
        var log = File.ReadAllText(Path.Combine(IssueDiagnosticLog.LogDirectory(temp.Path), "issue-999.jsonl"));
        Check(log.Contains("TaskCanceledException") && log.Contains("SendFirstAsync"), "exception type and stack are persisted");
        Check(!log.Contains("SECRET_PAYLOAD_DO_NOT_EXPORT"), "raw exception payload is not exported");
        Check(new LocalAllowanceNumberStore(temp.Path).FindLatest(Seller, Environment, Work)!.Status == "sending",
            "sending must survive restart");
        await ExpectAsync<InvalidOperationException>(() =>
            service.SendFirstAsync(Seller, Environment, saved));
        Check(fake.SendCount == 1, "never resend after a network timeout");
        fake.SendException = null;
        fake.Official = ValidOfficial(saved) with { BuyerIdentifier = "OTHER" };
        var mismatch = await service.ReconcileAsync(Seller, Environment, saved);
        Check(mismatch.Outcome == LocalAllowanceSendOutcome.Problem,
            "another buyer's allowance cannot confirm our frozen request");
        Check(events.Any(e => e.Stage == "readback_buyer_mismatch"), "safe mismatch category survives without buyer values");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "sending",
            "mismatch never clears sending");
        fake.Official = ValidOfficial(saved);
        var verified = await service.ReconcileAsync(Seller, Environment, saved);
        Check(verified.Outcome == LocalAllowanceSendOutcome.OfficiallyConfirmed,
            "readback of the original number is still not attributable to this POST");
        Check(ledger.FindLatest(Seller,Environment,Work)!.Status=="confirmed",
            "full official evidence confirms without another POST");
        Check(ReadbackAuditCount(temp.Path)==1,
            "only the full exact match can append a readback audit");
        Check(fake.SendCount == 1 && fake.QueryCount == 3,
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

    private static async Task DefinitiveRejectionAdvancesWithoutManualReviewAsync()
    {
        using var temp = new TemporaryDirectory("rejection-evidence");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        var fake = new StubAllowanceGateway {
            SendException = new AmegoApiException(4040161, "definite occupied")
        };
        var authValid = true;
        var service = new LocalAllowanceOneShotService(ledger, fake, (_,_) => {
            if (!authValid) throw new UnauthorizedAccessException("revoked");
            return Task.CompletedTask;
        });
        var first = await service.SendFirstAsync(Seller, Environment, saved);
        Check(first.Outcome == LocalAllowanceSendOutcome.ConfirmedNumberCollision,
            "Only explicit provider rejection can advance the number");
        Check(RejectionEvidenceCount(temp.Path) == 1,
            "provider code persisted atomically with occupied status");
        authValid = false;
        await ExpectAsync<UnauthorizedAccessException>(() =>
            service.ReserveAfterConfirmedRejectionAsync(Seller, Environment, saved));
        Check(ledger.FindLatest(Seller, Environment, Work) == saved with { Status = "occupied_external" },
            "revocation prevents further reservation");
        authValid = true;
        var next = await service.ReserveAfterConfirmedRejectionAsync(
            Seller, Environment, saved);
        Check(next.AllowanceNumber == "A20261019002" && next.Status == "reserved",
            "same approved request automatically allocates the next number");
        Check(fake.SendCount == 1,
            "reallocation never calls g0401 until the next safe send");
        await ExpectAsync<InvalidDataException>(() =>
            service.ReserveAfterConfirmedRejectionAsync(Seller, Environment, saved));
        using (var db = OpenAudit(temp.Path))
        {
            using var query = db.CreateCommand();
            query.CommandText = "SELECT allowance_number,provider_code FROM allowance_local_number_rejections";
            using var reader = query.ExecuteReader();
            Check(reader.Read() && reader.GetString(0) == saved.AllowanceNumber &&
                reader.GetInt32(1) == 4040161,
                "evidence contains the actual AMEGO rejection code, not an invented official hash");
            reader.Close();
            using var update = db.CreateCommand();
            update.CommandText = "UPDATE allowance_local_number_rejections SET provider_code=4040162";
            Expect<SqliteException>(() => update.ExecuteNonQuery());
            using var delete = db.CreateCommand();
            delete.CommandText = "DELETE FROM allowance_local_number_rejections";
            Expect<SqliteException>(() => delete.ExecuteNonQuery());
        }
        var reopened = new LocalAllowanceNumberStore(temp.Path);
        Check(reopened.FindLatest(Seller, Environment, Work) == next,
            "new reserved number and original provider evidence survive restart");
    }

    private static int RejectionEvidenceCount(string directory)
    {
        using var db = OpenAudit(directory);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM allowance_local_number_rejections";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static async Task AutoAdvanceOnlyOnExplicitRejectionAsync()
    {
        using var temp = new TemporaryDirectory("local-auto-number");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger = new LocalAllowanceNumberStore(temp.Path);
        var saved = Reserve(ledger);
        // More collisions than a conventional 3-retry cap: there is no
        // retry-count limit for definitive provider number rejections.
        var fake = new StubAllowanceGateway { RejectionsRemaining = 6 };
        var seen = new List<string>();
        var service = new LocalAllowanceOneShotService(ledger, fake,
            (_, _) => Task.CompletedTask);
        var result = await service.SendWithAutomaticNumberAdvanceAsync(
            Seller, Environment, saved, seen.Add);
        Check(fake.SendCount == 7 && fake.QueryCount == 1,
            "six explicit occupied replies advance without manual approval");
        Check(result.Outcome == LocalAllowanceSendOutcome.Problem,
            "a missing official query remains uncertain rather than success");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "sending",
            "last non-collision attempt is locked against replay");
        Check(RejectionEvidenceCount(temp.Path) == 6 &&
              seen.Any(x => x.Contains("A20261019007", StringComparison.Ordinal)),
            "all six old and new numbers are retained with safe progress");
        await ExpectAsync<InvalidOperationException>(() =>
            service.SendFirstAsync(Seller, Environment,
                ledger.FindLatest(Seller, Environment, Work)!));
        Check(fake.SendCount == 7, "a prior unknown response cannot POST again");

        using var other = new TemporaryDirectory("local-auto-timeout");
        SqliteBootstrapper.EnsureMigrated(other.Path);
        var secondLedger = new LocalAllowanceNumberStore(other.Path);
        var second = Reserve(secondLedger);
        var uncertain = new StubAllowanceGateway {
            SendException = new TaskCanceledException("unconfirmed POST")
        };
        var safe = new LocalAllowanceOneShotService(secondLedger, uncertain,
            (_, _) => Task.CompletedTask);
        var stop = await safe.SendWithAutomaticNumberAdvanceAsync(Seller,
            Environment, second);
        Check(stop.Outcome == LocalAllowanceSendOutcome.Problem &&
            uncertain.SendCount == 1 &&
            secondLedger.FindLatest(Seller, Environment, Work)!.Status == "sending",
            "timeout never triggers a new number or retry");
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

    private static SqliteConnection OpenAudit(string directory)
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=Path.Combine(directory,SqliteBootstrapper.DatabaseFileName),
            Pooling=false,Cache=SqliteCacheMode.Private}.ToString());
        connection.Open();
        return connection;
    }
    private static int ReadbackAuditCount(string directory)
    {
        using var db=OpenAudit(directory);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM allowance_local_readback_audit";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    private static int AuditHashLength(string directory)
    {
        using var db=OpenAudit(directory);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT length(snapshot_sha256) FROM allowance_local_readback_audit LIMIT 1";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    private static void AssertReadbackAuditImmutable(string directory)
    {
        using var db=OpenAudit(directory);
        using var update=db.CreateCommand();
        update.CommandText="UPDATE allowance_local_readback_audit SET result_kind='full_match_unattributed'";
        Expect<SqliteException>(()=>update.ExecuteNonQuery());
        using var delete=db.CreateCommand();
        delete.CommandText="DELETE FROM allowance_local_readback_audit";
        Expect<SqliteException>(()=>delete.ExecuteNonQuery());
    }
    private static void Expect<T>(Action action) where T:Exception
    {
        try{action();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
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
        public int RejectionsRemaining { get; set; }
        public AllowanceQueryItem? Official { get; set; }
        public Task<AllowanceIssueResponse> IssueAllowanceOnceAsync(
            AllowanceIssueRequest request, CancellationToken ct = default)
        {
            SendCount++;
            if (RejectionsRemaining > 0)
            {
                RejectionsRemaining--;
                return Task.FromException<AllowanceIssueResponse>(
                    new AmegoApiException(4040161, "definite occupied number"));
            }
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
