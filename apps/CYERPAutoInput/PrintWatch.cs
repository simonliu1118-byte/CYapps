using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// After a print is sent (user, 2026-10-27): Windows takes the job as soon as it is spooled, so a
/// printer out of paper went unnoticed. For a while after each print the printer's state and the
/// job in its queue are read; 缺紙／卡紙／錯誤／離線 reported by the driver, or a job that stays
/// unprinted, is told once through <see cref="Problem"/>. A driver that reports nothing (some
/// thermal printers) can only be caught by the stuck job.
/// </summary>
internal static class PrintWatch
{
    /// <summary>What went wrong, raised on a pool thread.</summary>
    public static event Action<string>? Problem;

    /// <summary>Where its own troubles are logged (the main window's log).</summary>
    public static Action<string>? Warn { get; set; }

    /// <param name="document">The job's document name (any job of this program when null, e.g. Chrome's).</param>
    public static void Watch(string printer, string? document, int stuckSeconds = 30)
    {
        _ = Task.Run(async () =>
        {
            try { await WatchAsync(printer, document, stuckSeconds); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Warn?.Invoke($"{ex.GetType().Name}: {ex.Message}"); }
        });
    }

    private static async Task WatchAsync(string printer, string? document, int stuckSeconds)
    {
        if (!OpenPrinter(printer, out var handle, 0)) return;
        try
        {
            var start = DateTime.Now;
            var seen = false;
            while (DateTime.Now - start < TimeSpan.FromSeconds(stuckSeconds + 5))
            {
                await Task.Delay(1500);
                var jobs = Jobs(handle).Where(j => document is null || (j.Document ?? string.Empty).Contains(document, StringComparison.Ordinal)).ToList();
                var problem = Describe(PrinterStatus(handle), jobs.Aggregate(0u, (s, j) => s | j.Status));
                if (problem is not null)
                {
                    Report(printer, problem);
                    return;
                }
                if (jobs.Count > 0) seen = true;
                else if (seen || DateTime.Now - start > TimeSpan.FromSeconds(6)) return; // printed (or gone)
                if (jobs.Count > 0 && DateTime.Now - start > TimeSpan.FromSeconds(stuckSeconds) && jobs.All(j => j.PagesPrinted == 0))
                {
                    Report(printer, "工作卡在列印佇列沒有印出（可能缺紙、離線或卡紙）");
                    return;
                }
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static void Report(string printer, string problem)
    {
        Warn?.Invoke($"printer={printer} {problem}");
        Problem?.Invoke($"印表機「{printer}」：{problem}，請檢查印表機後補印。");
    }

    // Printer (PRINTER_INFO_6) and job (JOB_INFO_1) status bits, winspool.h.
    private const uint PrinterError = 0x2, PrinterPaperJam = 0x8, PrinterPaperOut = 0x10, PrinterPaperProblem = 0x40,
        PrinterOffline = 0x80, PrinterNotAvailable = 0x1000, PrinterNoToner = 0x40000, PrinterUserIntervention = 0x100000,
        PrinterDoorOpen = 0x400000;
    private const uint JobError = 0x2, JobOffline = 0x20, JobPaperOut = 0x40, JobBlocked = 0x200, JobUserIntervention = 0x400;

    /// <summary>What the printer or its job reports as wrong, in words; null when nothing. Pure.</summary>
    public static string? Describe(uint printerStatus, uint jobStatus)
    {
        if ((printerStatus & PrinterPaperOut) != 0 || (jobStatus & JobPaperOut) != 0) return "缺紙";
        if ((printerStatus & PrinterPaperJam) != 0) return "卡紙";
        if ((printerStatus & PrinterDoorOpen) != 0) return "機蓋打開";
        if ((printerStatus & PrinterNoToner) != 0) return "沒有碳粉";
        if ((printerStatus & PrinterPaperProblem) != 0) return "紙張有問題";
        if ((printerStatus & (PrinterOffline | PrinterNotAvailable)) != 0 || (jobStatus & JobOffline) != 0) return "離線";
        if ((printerStatus & (PrinterError | PrinterUserIntervention)) != 0 || (jobStatus & (JobError | JobBlocked | JobUserIntervention)) != 0)
            return "印表機錯誤，需要處理";
        return null;
    }

    private static uint PrinterStatus(nint handle)
    {
        GetPrinter(handle, 6, 0, 0, out var needed);
        if (needed <= 0) return 0;
        var buffer = Marshal.AllocHGlobal(needed);
        try { return GetPrinter(handle, 6, buffer, needed, out _) ? (uint)Marshal.ReadInt32(buffer) : 0; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static List<(string? Document, uint Status, uint PagesPrinted)> Jobs(nint handle)
    {
        var list = new List<(string?, uint, uint)>();
        EnumJobs(handle, 0, 64, 1, 0, 0, out var needed, out _);
        if (needed <= 0) return list;
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumJobs(handle, 0, 64, 1, buffer, needed, out _, out var count)) return list;
            var size = Marshal.SizeOf<JobInfo1>();
            for (var i = 0; i < count; i++)
            {
                var job = Marshal.PtrToStructure<JobInfo1>(buffer + i * size);
                list.Add((job.Document, job.Status, job.PagesPrinted));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JobInfo1
    {
        public uint JobId;
        public string? PrinterName, MachineName, UserName, Document, Datatype, StatusText;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string name, out nint handle, nint defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(nint handle);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetPrinterW")]
    private static extern bool GetPrinter(nint handle, int level, nint buffer, int size, out int needed);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumJobsW")]
    private static extern bool EnumJobs(nint handle, int first, int count, int level, nint buffer, int size, out int needed, out int returned);
}
