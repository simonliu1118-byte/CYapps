using System.Globalization;

namespace CYERPAutoInput;

/// <summary>
/// Daily logs older than <see cref="KeepDays"/> days are removed when nothing went wrong that day
/// (user, 2026-11-26). A day is kept when the program could not go on by itself: an error, a
/// batch that stopped, a wait that timed out, or Esc pressed (a person stepping in). ERP's stock
/// warning, printer problems and the program's own retries that went through are not trouble.
/// </summary>
internal static class LogRetention
{
    public const int KeepDays = 7;

    private static readonly string[] TroubleMarks =
    [
        "[ERROR]",
        "-batch: stopped:",
        "-batch: print only stopped:",
        "timeout waiting for",
        "Esc pressed",
        "outcome=Stopped",
        "outcome=Cancelled"
    ];

    /// <summary>Whether a log line says the program could not go on by itself.</summary>
    public static bool IsTrouble(string line) => TroubleMarks.Any(m => line.Contains(m, StringComparison.Ordinal));

    /// <summary>The day of a CYERPAutoInput_yyyyMMdd.log file, or null for any other name.</summary>
    public static DateTime? DayOf(string fileName)
    {
        const string prefix = "CYERPAutoInput_";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return null;
        var stamp = fileName[prefix.Length..^4];
        return DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
    }

    /// <summary>Removes the old logs with no trouble in them; returns how many were removed.</summary>
    public static int Prune(string directory, DateTime today, Action<string> warn)
    {
        if (!Directory.Exists(directory)) return 0;
        var removed = 0;
        foreach (var path in Directory.GetFiles(directory, "CYERPAutoInput_*.log"))
        {
            if (DayOf(Path.GetFileName(path)) is not { } day || day > today.Date.AddDays(-KeepDays)) continue;
            try
            {
                bool trouble;
                using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    trouble = false;
                    while (!trouble && reader.ReadLine() is { } line) trouble = IsTrouble(line);
                }
                if (trouble) continue;
                File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warn($"old log left: {Path.GetFileName(path)} {ex.Message}");
            }
        }
        return removed;
    }
}
