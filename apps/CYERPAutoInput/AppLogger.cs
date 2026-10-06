namespace CYERPAutoInput;

internal sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public string LogDirectory { get; }

    /// <summary>
    /// When false (default), actual ERP / order content (item codes, sales order
    /// numbers, OCR text read from ERP screens) is written as its length only.
    /// Users enable it in Settings for on-site troubleshooting; logs stay local.
    /// </summary>
    public bool Diagnostic { get; set; }
    public string LogPath { get; }

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
            LogDirectory = Path.Combine(Path.GetTempPath(), "CYERPAutoInput", "logs");
            Directory.CreateDirectory(LogDirectory);
        }

        LogPath = Path.Combine(LogDirectory, $"CYERPAutoInput_{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        Info("app", $"{AppVersionInfo.Display} starting (C# rewrite, PaddleOCR PP-OCRv5 mobile recognition)");
    }

    public string Value(string? value) =>
        Diagnostic ? value ?? string.Empty : $"<{value?.Length ?? 0} chars>";

    public void Info(string area, string message) => Write("INFO", area, message);
    public void Warn(string area, string message) => Write("WARN", area, message);
    public void Error(string area, Exception ex) => Write("ERROR", area, $"{ex.GetType().Name}: {ex.Message}");
    public void Error(string area, string message) => Write("ERROR", area, message);

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
