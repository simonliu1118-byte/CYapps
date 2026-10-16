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
    public void Error(string area, Exception ex) => Write("ERROR", area, $"{ex.GetType().Name}: {ex.Message}");

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
