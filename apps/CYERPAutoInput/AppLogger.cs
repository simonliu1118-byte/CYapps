namespace CYERPAutoInput;

internal sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public string LogDirectory { get; }
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
        Info("app", "CYERPAutoInput V0.1.0 Build 23 starting (C# rewrite, PaddleOCR PP-OCRv5 mobile recognition)");
    }

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
