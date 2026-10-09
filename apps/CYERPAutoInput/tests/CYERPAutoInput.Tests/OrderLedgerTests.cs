using Xunit;

namespace CYERPAutoInput.Tests;

public class OrderLedgerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cyerp-ledger-" + Guid.NewGuid().ToString("N"));
    private string LedgerPath => Path.Combine(_dir, "order_ledger.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Remembers_outcomes_across_instances()
    {
        new OrderLedger(LedgerPath).Record("MO店+:90000000000001", LedgerStatus.Entered, "T01-20260101001");
        var entry = new OrderLedger(LedgerPath).Find("MO店+:90000000000001");
        Assert.NotNull(entry);
        Assert.Equal(LedgerStatus.Entered, entry.Status);
        Assert.Equal("T01-20260101001", entry.Document);
        Assert.Null(new OrderLedger(LedgerPath).Find("蝦皮:90000000000001"));
    }

    [Fact]
    public void A_saved_order_is_not_downgraded_by_a_later_unsaved_run()
    {
        var ledger = new OrderLedger(LedgerPath);
        ledger.Record("蝦皮:A1", LedgerStatus.HandedOff, "");
        ledger.Record("蝦皮:A1", LedgerStatus.Saved, "T01-20260101002");
        ledger.Record("蝦皮:A1", LedgerStatus.Entered, "T01-20260101003");
        var entry = new OrderLedger(LedgerPath).Find("蝦皮:A1")!;
        Assert.Equal(LedgerStatus.Saved, entry.Status);
        Assert.Equal("T01-20260101002", entry.Document);
        Assert.Equal("已儲存過", entry.ShortText);
    }

    [Fact]
    public void A_corrupt_ledger_is_reported_instead_of_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(LedgerPath, "{ not json");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => new OrderLedger(LedgerPath));
    }
}
