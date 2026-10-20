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
        await ReviewedCollisionReallocationIsAtomicAndReadbackOnlyAsync();
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
        Check(first.Outcome == LocalAllowanceSendOutcome.FullMatchUnattributed,
            "even complete readback is not proof that this POST created the allowance");
        Check(ledger.FindLatest(Seller, Environment, Work)!.Status == "sending",
            "exact readback must NOT promote the ledger to confirmed");
        Check(ReadbackAuditCount(temp.Path)==1,
            "one immutable exact-readback evidence entry must be persisted");
        Check(AuditHashLength(temp.Path)==64,"snapshot hash, not raw buyer, is recorded");
        AssertReadbackAuditImmutable(temp.Path);
        Expect<InvalidOperationException>(()=>
            ledger.MarkOfficiallyConfirmed(Seller,Environment,saved));
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
        Check(verified.Outcome == LocalAllowanceSendOutcome.FullMatchUnattributed,
            "readback of the original number is still not attributable to this POST");
        Check(ledger.FindLatest(Seller,Environment,Work)!.Status=="sending",
            "repeated exact readback cannot silently confirm");
        Check(ReadbackAuditCount(temp.Path)==1,
            "only the full exact match can append a readback audit");
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

    private static async Task ReviewedCollisionReallocationIsAtomicAndReadbackOnlyAsync()
    {
        using var temp=new TemporaryDirectory("b2-collision-reviewed");
        SqliteBootstrapper.EnsureMigrated(temp.Path);
        var ledger=new LocalAllowanceNumberStore(temp.Path);
        var previous=Reserve(ledger);
        var fake=new StubAllowanceGateway{
            SendException=new AmegoApiException(
                AmegoAllowanceErrorCodes.ExistingIssuedAllowanceNumber,"occupied")
        };
        var managerValid=true;
        var checks=0;
        var service=new LocalAllowanceOneShotService(ledger,fake,(_,_)=>{
            checks++;
            if(!managerValid)throw new UnauthorizedAccessException("revoked");
            return Task.CompletedTask;
        });
        var original=await service.SendFirstAsync(Seller,Environment,previous);
        Check(original.Outcome==LocalAllowanceSendOutcome.ConfirmedNumberCollision,
            "first API confirmed number collision");
        const string ack="確認外部占號並重新配號";
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,"yes"));
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        fake.Official=ValidOfficial(previous);
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        Check(ledger.FindLatest(Seller,Environment,Work)!.AllowanceNumber==
            previous.AllowanceNumber,"matching provider records never authorize a new number");
        // A different buyer alone is not enough without another original invoice.
        fake.Official=ValidOfficial(previous) with {BuyerIdentifier="87654321"};
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        // Likewise, a different invoice with the same buyer remains ambiguous.
        fake.Official=ExternalOfficial(previous) with {BuyerIdentifier="12345678"};
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        fake.Official=ExternalOfficial(previous);
        managerValid=false;
        await ExpectAsync<UnauthorizedAccessException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        managerValid=true;
        var next=await service.ReserveAfterExternalCollisionAsync(
            Seller,Environment,previous,ack);
        Check(next.AllowanceNumber=="A20261019002"&&next.Sequence==2&&
            next.Status=="reserved","a new immutable reservation is not an immediate send");
        Check(fake.SendCount==1,"collision review must never re-POST g0401");
        Check(ledger.FindLatest(Seller,Environment,Work)==next,
            "the same work item remains the only owner of attempts");
        Check(CollisionReviewCount(temp.Path)==1,
            "reservation and manager collision proof must commit together");
        using(var db=OpenAudit(temp.Path))
        {
            using var query=db.CreateCommand();
            query.CommandText="SELECT previous_number,new_number,length(official_snapshot_sha256) "+
                "FROM allowance_local_collision_reviews";
            using var reader=query.ExecuteReader();
            Check(reader.Read() && reader.GetString(0)==previous.AllowanceNumber&&
                reader.GetString(1)==next.AllowanceNumber && reader.GetInt32(2)==64,
                "audit must bind old/new numbers and official fingerprint");
        }
        await ExpectAsync<InvalidOperationException>(()=>
            service.ReserveAfterExternalCollisionAsync(Seller,Environment,previous,ack));
        Check(CollisionReviewCount(temp.Path)==1,"cannot replay collision approval");
        Check(fake.SendCount==1,"manual reallocation never posts the new number");
        using(var db=OpenAudit(temp.Path))
        {
            using var update=db.CreateCommand();
            update.CommandText="UPDATE allowance_local_collision_reviews SET new_number='A20261019099'";
            Expect<SqliteException>(()=>update.ExecuteNonQuery());
            using var delete=db.CreateCommand();
            delete.CommandText="DELETE FROM allowance_local_collision_reviews";
            Expect<SqliteException>(()=>delete.ExecuteNonQuery());
        }
        // The latest reserved intent is immutable and can be recovered.
        var fresh=new LocalAllowanceNumberStore(temp.Path);
        Check(fresh.FindLatest(Seller,Environment,Work)==next,
            "the reviewed next number must survive restart");
        Check(checks>=2,"manager verification must be fresh for manual review");
    }

    private static AllowanceQueryItem ExternalOfficial(LocalAllowanceReservation prev)
    {
        using var json=JsonDocument.Parse("""
            [{"original_invoice_number":"ZZ99887766","original_invoice_date":20261018,
              "original_description":"EXTERNAL ITEM","quantity":"1","unit_price":"5",
              "amount":"5","tax":"0","tax_type":1}]
            """);
        return ValidOfficial(prev) with {
            BuyerIdentifier="87654321",ProductItems=json.RootElement.Clone()
        };
    }

    private static int CollisionReviewCount(string directory)
    {
        using var db=OpenAudit(directory);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM allowance_local_collision_reviews";
        return Convert.ToInt32(cmd.ExecuteScalar());
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
