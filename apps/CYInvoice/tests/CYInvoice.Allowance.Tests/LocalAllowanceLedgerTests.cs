using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class LocalAllowanceLedgerTests
{
    public static void Run()
    {
        using var temporary = new TemporaryDirectory("ledger-b1");
        SqliteBootstrapper.EnsureMigrated(temporary.Path);
        var ledger = new LocalAllowanceNumberStore(temporary.Path);
        var date = new DateOnly(2026, 10, 19);
        var approved = new DateTimeOffset(2026, 10, 19, 14, 35, 0, TimeSpan.FromHours(8));
        var template = new AllowanceIssueRequest
        {
            AllowanceDate = "20261019",
            AllowanceType = 2,
            BuyerIdentifier = "12345678",
            ProductItem = [new AllowanceProductItem
            {
                OriginalInvoiceNumber = "AA12345678",
                OriginalInvoiceDate = 20261019,
                OriginalDescription = "合成品項",
                Quantity = 1m,
                UnitPrice = 100m,
                Amount = 100m,
                Tax = 5m,
                TaxType = 1
            }],
            TotalAmount = 100m,
            TaxAmount = 5m
        };
        const string seller = "SYNTHETIC-SELLER";
        const string environment = "test";
        const string workId = "synthetic-work-001";
        var reserved = ledger.Reserve(seller, environment, workId, date, template, "2000", approved);
        Check(reserved.AllowanceNumber == "A20261019001", "first number must be 001");
        Check(reserved.Status == "reserved" && reserved.Sequence == 1, "reservation status");
        Check(reserved.ApproverEmployeeNo == "2000" &&
              reserved.ApprovedAtUtc == approved.ToUniversalTime(), "approval snapshot");
        using (var json = JsonDocument.Parse(reserved.PayloadJson))
        {
            Check(json.RootElement.GetProperty("AllowanceNumber").GetString() == reserved.AllowanceNumber,
                "frozen payload must include the allocated number");
        }

        var reopened = new LocalAllowanceNumberStore(temporary.Path);
        var again = reopened.Reserve(seller, environment, workId, date, template, "2000", approved);
        Check(again == reserved, "reopen must return same immutable reserved intent");
        Expect<InvalidOperationException>(() =>
            reopened.Reserve(seller, environment, workId, date, template, "9999", approved));
        Expect<InvalidOperationException>(() =>
            reopened.Reserve(seller, environment, workId, date, template,
                "2000", approved.AddMinutes(1)));
        Expect<InvalidOperationException>(() =>
            reopened.Reserve(seller, environment, workId, date,
                new AllowanceIssueRequest
                {
                    AllowanceDate = "20261019", AllowanceType = 2,
                    BuyerIdentifier = "DIFFERENT",
                    ProductItem = template.ProductItem,
                    TotalAmount = 100m, TaxAmount = 5m
                }, "2000", approved));
        Expect<InvalidOperationException>(() =>
            ledger.MarkOfficiallyConfirmed(seller, environment, reserved));
        ledger.MarkSending(seller, environment, reserved);
        Check(reopened.FindLatest(seller, environment, workId)?.Status == "sending",
            "sending must survive process restart");
        Expect<InvalidOperationException>(() => ledger.MarkSending(seller, environment, reserved));
        Expect<InvalidOperationException>(() =>
            ledger.Reserve(seller, environment, workId, date, template, "2000", approved));
        Expect<ArgumentException>(() =>
            ledger.MarkExternalCollision(seller, environment, reserved, 4040163));
        Expect<InvalidOperationException>(() =>
            ledger.MarkOfficiallyConfirmed(seller, environment, reserved));
        Check(reopened.FindLatest(seller, environment, workId)?.Status == "sending",
            "matching readback cannot manually force a false confirmed state");

        var second = ledger.Reserve(seller, environment, "synthetic-work-002", date,
            template, "2000", approved);
        Check(second.AllowanceNumber == "A20261019002", "second work item gets a distinct sequence");
        ledger.MarkSending(seller, environment, second);
        ledger.MarkExternalCollision(seller, environment, second, 4040161);
        Check(reopened.FindLatest(seller, environment, second.WorkItemId)?.Status == "occupied_external",
            "explicit collision must persist");
        Expect<InvalidOperationException>(() =>
            reopened.Reserve(seller, environment, second.WorkItemId, date,
                template, "2000", approved));
        Expect<InvalidOperationException>(() => ledger.MarkSending(seller, environment, second));
        Expect<InvalidOperationException>(() =>
            ledger.MarkOfficiallyConfirmed(seller, environment, second));
        Check(AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(4040162),
            "voided-number official rejection also occupies the number");
        Check(!AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied(0),
            "AMEGO code 0 is not a collision");

        var third = ledger.Reserve(seller, environment, "synthetic-work-003", date,
            template, "2000", approved);
        Check(third.AllowanceNumber == "A20261019003",
            "failed and collided numbers are never recycled");
        Check(reopened.FindLatest(seller, "other-environment", workId) is null,
            "environment fence must reject foreign work items");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
