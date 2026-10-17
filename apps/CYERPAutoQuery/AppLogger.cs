namespace CYERPAutoQuery;

internal sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public string LogDirectory { get; }
    public string LogPath { get; }

    /// <summary>
    /// When false (default), actual ERP content (item codes, field values) is written as
    /// its length only. Logs stay local and are never uploaded.
    /// </summary>
    public bool Diagnostic { get; set; }

    public AppLogger()
    {
        var baseDir = AppContext.BaseDirectory;
        LogDirectory = Path.Combine(baseDir, "logs");
        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
            LogDirectory = Path.Combine(Path.GetTempPath(), "CYERPAutoQuery", "logs");
            Directory.CreateDirectory(LogDirectory);
        }

        LogPath = Path.Combine(LogDirectory, $"CYERPAutoQuery_{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        Info("app", $"{AppVersionInfo.Display} starting");
    }

    public string Value(string? value) =>
        Diagnostic ? value ?? string.Empty : $"<{value?.Length ?? 0} chars>";

    public void Info(string area, string message) => Write("INFO", area, message);
    public void Warn(string area, string message) => Write("WARN", area, message);
    // The place in the code is logged too (V0.5.0 LOG: a bare IndexOutOfRangeException could not
    // be traced). Stack frames name methods and line numbers only, no business data.
    public void Error(string area, Exception ex) =>
        Write("ERROR", area, $"{ex.GetType().Name}: {ex.Message} at {Where(ex)}" +
                             (ex.InnerException is { } inner ? $" inner={inner.GetType().Name} at {Where(inner)}" : string.Empty));

    /// <summary>The innermost few stack frames of <paramref name="ex"/>, on one line.</summary>
    public static string Where(Exception ex) =>
        string.Join(" < ", (ex.StackTrace ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .Select(f => f.StartsWith("at ", StringComparison.Ordinal) ? f[3..] : f));

    private void Write(string level, string area, string message)
    {
        var safe = message.Replace('\r', ' ').Replace('\n', ' ');
        lock (_gate)
            _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {area}: {safe}");
    }

    public void Dispose()
    {
        lock (_gate)
            _writer.Dispose();
    }
}
