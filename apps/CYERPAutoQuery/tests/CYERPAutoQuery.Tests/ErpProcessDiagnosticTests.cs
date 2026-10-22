using Xunit;

namespace CYERPAutoQuery.Tests;

public sealed class ErpProcessDiagnosticTests
{
    private static ErpProcessInfo Process(int pid, DateTimeOffset time, string path = @"C:\Private\erp.exe", string account = @"DOMAIN\worker") =>
        new(pid, 50, 2, time, "MainMenu", path, account, 104857600, 2.5, ["ERP 主畫面候選"]);

    [Fact]
    public void Compare_detects_pid_reuse_as_exit_and_new_process()
    {
        var old = new ErpProcessSnapshot(DateTimeOffset.UtcNow, [Process(100, DateTimeOffset.UtcNow.AddMinutes(-3))], []);
        var next = new ErpProcessSnapshot(DateTimeOffset.UtcNow, [Process(100, DateTimeOffset.UtcNow)], []);
        var events = ErpProcessDiagnosticReport.Compare(old, next);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Event == "結束／不再可見");
        Assert.Contains(events, e => e.Event == "出現");
    }

    [Fact]
    public void Compare_ignores_resource_counter_changes()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var p = Process(150, start);
        var old = new ErpProcessSnapshot(DateTimeOffset.UtcNow, [p], []);
        var next = new ErpProcessSnapshot(DateTimeOffset.UtcNow, [p with { WorkingSetBytes = 1000, CpuSeconds = 15 }], []);
        Assert.Empty(ErpProcessDiagnosticReport.Compare(old, next));
    }

    [Fact]
    public void Export_never_contains_private_path_or_account()
    {
        var time = DateTimeOffset.UtcNow;
        var snapshot = new ErpProcessSnapshot(time, [Process(11, time)], []);
        var report = ErpProcessDiagnosticReport.Format(snapshot, [], 1);
        Assert.Contains("MainMenu", report);
        Assert.Contains("路徑#1", report);
        Assert.Contains("帳戶#1", report);
        Assert.DoesNotContain(@"C:\Private", report);
        Assert.DoesNotContain(@"DOMAIN\worker", report);
    }
}
