using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace CYERPAutoQuery;

/// <summary>Windows-only, read-only collection. Never opens, closes or signals an ERP process.</summary>
internal static class ErpProcessDiagnosticScanner
{
    public static ErpProcessSnapshot Scan(string configuredExe)
    {
        var notes = new List<string>();
        var found = new List<ErpProcessInfo>();
        var windows = WindowsByPid();
        var parentIds = new Dictionary<int, (int? Parent, string Account)>();
        try
        {
            using var search = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name FROM Win32_Process");
            foreach (ManagementObject obj in search.Get())
            {
                try
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    string account = string.Empty;
                    try
                    {
                        var output = new object[] { string.Empty, string.Empty };
                        if (Convert.ToUInt32(obj.InvokeMethod("GetOwner", output)) == 0)
                            account = output[1] + @"\" + output[0];
                    }
                    catch (Exception) { /* permissions vary by session */ }
                    parentIds[pid] = (Convert.ToInt32(obj["ParentProcessId"]), account);
                }
                finally { obj.Dispose(); }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            notes.Add("WMI 無法讀取部分父行程及執行帳戶：" + ex.GetType().Name);
        }

        var configuredName = Path.GetFileNameWithoutExtension(configuredExe);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    string path = string.Empty;
                    try { path = process.MainModule?.FileName ?? string.Empty; }
                    catch (Exception) { /* access denied for protected/remote-session process */ }
                    var basename = Path.GetFileNameWithoutExtension(path);
                    // Window title alone is not a process identity: the AutoQuery window itself
                    // contains "SMART ERP" and was previously misclassified as ERP.
                    if (process.Id == Environment.ProcessId ||
                        name.Equals("CYERPAutoQuery", StringComparison.OrdinalIgnoreCase)) continue;
                    var isCandidate =
                        name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SMLeaderWorkCenter", StringComparison.OrdinalIgnoreCase) ||
                        (configuredName.Length > 0 && (name.Equals(configuredName, StringComparison.OrdinalIgnoreCase) ||
                            basename.Equals(configuredName, StringComparison.OrdinalIgnoreCase)));
                    if (!isCandidate) continue;
                    int? session = null;
                    DateTimeOffset? started = null;
                    long? memory = null;
                    double? cpu = null;
                    try { session = process.SessionId; } catch (Exception) { }
                    try { started = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero); } catch (Exception) { }
                    try { memory = process.WorkingSet64; } catch (Exception) { }
                    try { cpu = process.TotalProcessorTime.TotalSeconds; } catch (Exception) { }
                    parentIds.TryGetValue(process.Id, out var extra);
                    found.Add(new ErpProcessInfo(process.Id, extra.Parent, session, started, name, path,
                        extra.Account, memory, cpu, windows.TryGetValue(process.Id, out var kinds) ? kinds : []));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process might have exited between enumeration and inspection.
                }
            }
        }
        return new ErpProcessSnapshot(DateTimeOffset.UtcNow, found, notes);
    }

    private static Dictionary<int, IReadOnlyList<string>> WindowsByPid()
    {
        var results = new Dictionary<int, HashSet<string>>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new char[256];
            var count = GetWindowText(hwnd, title, title.Length);
            if (count <= 0) return true;
            var caption = new string(title, 0, count);
            var kind =
                caption.Contains("INVI02", StringComparison.OrdinalIgnoreCase) ? "INVI02" :
                caption.Contains("INVQ01", StringComparison.OrdinalIgnoreCase) ? "INVQ01" :
                caption.Contains("COPI01", StringComparison.OrdinalIgnoreCase) ? "COPI01" :
                caption.Contains("PURI01", StringComparison.OrdinalIgnoreCase) ? "PURI01" :
                caption.Contains("COPR15", StringComparison.OrdinalIgnoreCase) ? "COPR15" :
                caption.Contains("COPR16", StringComparison.OrdinalIgnoreCase) ? "COPR16" :
                caption.Contains("鼎新", StringComparison.OrdinalIgnoreCase) ||
                caption.Contains("SMART ERP", StringComparison.OrdinalIgnoreCase) ||
                caption.Contains("Conductor", StringComparison.OrdinalIgnoreCase) ? "ERP 主畫面候選" : null;
            if (kind is null) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;
            if (!results.TryGetValue((int)pid, out var labels)) results[(int)pid] = labels = new HashSet<string>();
            labels.Add(kind);
            return true;
        }, IntPtr.Zero);
        return results.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.OrderBy(v => v).ToArray());
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, char[] text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
