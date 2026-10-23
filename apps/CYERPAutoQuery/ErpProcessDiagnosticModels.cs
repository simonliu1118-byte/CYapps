using System.Globalization;
using System.Text;

namespace CYERPAutoQuery;

/// <summary>One observed process. This is diagnostic data only; it never grants permission to terminate a process.</summary>
internal sealed record ErpProcessInfo(
    int Pid,
    int? ParentPid,
    int? SessionId,
    DateTimeOffset? StartedUtc,
    string ProcessName,
    string ExecutablePath,
    string Account,
    long? WorkingSetBytes,
    double? CpuSeconds,
    IReadOnlyList<string> WindowKinds)
{
    // A PID may be recycled after the original process exits.
    public string Identity => Pid.ToString(CultureInfo.InvariantCulture) + "/" +
                              (StartedUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "?");
}

internal sealed record ErpProcessSnapshot(DateTimeOffset At, IReadOnlyList<ErpProcessInfo> Processes, IReadOnlyList<string> Notes);

internal sealed record ErpProcessChange(DateTimeOffset At, string Event, int Pid, string Name, string Detail);

/// <summary>Pure snapshot comparison and redacted text export, usable in cross-platform tests.</summary>
internal static class ErpProcessDiagnosticReport
{
    public static IReadOnlyList<ErpProcessChange> Compare(ErpProcessSnapshot previous, ErpProcessSnapshot current)
    {
        var old = previous.Processes.ToDictionary(p => p.Identity, StringComparer.Ordinal);
        var now = current.Processes.ToDictionary(p => p.Identity, StringComparer.Ordinal);
        var events = new List<ErpProcessChange>();

        foreach (var process in previous.Processes.Where(p => !now.ContainsKey(p.Identity)))
            events.Add(new ErpProcessChange(current.At, "結束／不再可見", process.Pid, process.ProcessName, "原工作階段 " + (process.SessionId?.ToString() ?? "未知")));
        foreach (var process in current.Processes.Where(p => !old.ContainsKey(p.Identity)))
            events.Add(new ErpProcessChange(current.At, "出現", process.Pid, process.ProcessName, "工作階段 " + (process.SessionId?.ToString() ?? "未知")));
        foreach (var process in current.Processes.Where(p => old.ContainsKey(p.Identity)))
        {
            var before = old[process.Identity];
            if (!before.WindowKinds.OrderBy(x => x).SequenceEqual(process.WindowKinds.OrderBy(x => x)))
                events.Add(new ErpProcessChange(current.At, "視窗變更", process.Pid, process.ProcessName,
                    "視窗類型：" + string.Join("、", process.WindowKinds)));
        }
        return events;
    }

    /// <summary>
    /// This file intentionally contains no account names, full paths, raw window titles,
    /// command lines, passwords or tokens. Alias numbers correlate paths/accounts within one report.
    /// </summary>
    public static string Format(ErpProcessSnapshot? latest, IReadOnlyList<ErpProcessChange> events, int samples)
    {
        var b = new StringBuilder();
        b.AppendLine("CYERPAutoQuery ERP 行程診斷（唯讀）");
        b.AppendLine("報告時間（UTC）：" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        b.AppendLine("採樣次數：" + samples.ToString(CultureInfo.InvariantCulture));
        b.AppendLine("此報告已遮蔽帳戶、完整路徑與原始視窗標題；不得作為自動終止行程的依據。");
        b.AppendLine();
        if (latest is null)
        {
            b.AppendLine("尚無掃描資料。");
            return b.ToString();
        }

        b.AppendLine("最近掃描（UTC）：" + latest.At.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        b.AppendLine("偵測到的 ERP 候選行程：" + latest.Processes.Count.ToString(CultureInfo.InvariantCulture));
        // Aliases are assigned in stable ordinal order, never exposing their originals in the report.
        var paths = latest.Processes.Select(p => p.ExecutablePath).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var accounts = latest.Processes.Select(p => p.Account).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var p in latest.Processes.OrderBy(p => p.SessionId).ThenBy(p => p.Pid))
        {
            var path = p.ExecutablePath.Length == 0 ? "無法讀取" : "路徑#" + (paths.FindIndex(s => string.Equals(s, p.ExecutablePath, StringComparison.OrdinalIgnoreCase)) + 1);
            var account = p.Account.Length == 0 ? "無法讀取" : "帳戶#" + (accounts.FindIndex(s => string.Equals(s, p.Account, StringComparison.OrdinalIgnoreCase)) + 1);
            b.AppendLine($"- {Clean(p.ProcessName)} | PID={p.Pid} PPID={p.ParentPid?.ToString() ?? "?"} Session={p.SessionId?.ToString() ?? "?"}");
            b.AppendLine($"  啟動(UTC)={p.StartedUtc?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "?"} | {path} | {account}");
            b.AppendLine($"  記憶體MiB={(p.WorkingSetBytes is { } bytes ? (bytes / 1048576d).ToString("F1", CultureInfo.InvariantCulture) : "?")} | CPU累計秒={p.CpuSeconds?.ToString("F1", CultureInfo.InvariantCulture) ?? "?"}");
            b.AppendLine("  視窗類型=" + (p.WindowKinds.Count == 0 ? "無可讀取視窗" : string.Join("、", p.WindowKinds.Select(Clean))));
        }

        b.AppendLine();
        b.AppendLine("掃描註記：");
        foreach (var note in latest.Notes) b.AppendLine("- " + Clean(note));
        if (latest.Notes.Count == 0) b.AppendLine("- 無");
        b.AppendLine();
        b.AppendLine("觀察到的變化：");
        if (events.Count == 0) b.AppendLine("- 尚無變化（不代表未曾在兩次採樣之間啟動或結束）。");
        foreach (var change in events)
            b.AppendLine($"- {change.At.ToUniversalTime():HH:mm:ss} [{Clean(change.Event)}] {Clean(change.Name)} PID={change.Pid}: {Clean(change.Detail)}");
        return b.ToString();
    }

    private static string Clean(string value) =>
        new(value.Where(c => !char.IsControl(c) || c == '\t').Take(120).ToArray());
}
