using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal static class ApplicationIcon
{
    private static readonly Lazy<Icon> Current = new(() =>
    {
        using var stream = typeof(ApplicationIcon).Assembly.GetManifestResourceStream("CYInvoice.AppIcon")
            ?? throw new InvalidOperationException("CYInvoice 內嵌視窗圖示不存在");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    });

    public static Icon Load() => (Icon)Current.Value.Clone();
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var smokeTest = args.Contains("--startup-smoke-test", StringComparer.Ordinal);
        try
        {
            ApplicationConfiguration.Initialize();
            if (smokeTest)
            {
                using var form = new MainForm(startupSmokeTest: true);
                form.Show();
                form.PerformLayout();
                Application.DoEvents();
                form.VerifySmokeLayout();
                form.Close();

                var repository = LocalRepository.Open(AppContext.BaseDirectory, new DpapiSecretProtector());
                using var syncIssues = new SyncIssuesForm(repository);
                syncIssues.Show();
                syncIssues.PerformLayout();
                Application.DoEvents();
                syncIssues.VerifySmokeLayout();
                syncIssues.Close();
                return;
            }
            Application.Run(new MainForm());
        }
        catch (Exception error)
        {
            WriteStartupError(error);
            if (smokeTest)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
                return;
            }
            MessageBox.Show(
                $"CYInvoice 啟動失敗。\n\n錯誤原因：{error.Message}\n\n詳細紀錄已寫入程式旁的 Logs\\startup-error.log。",
                "CYInvoice 啟動失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void WriteStartupError(Exception error)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            var message = $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "startup-error.log"), message);
        }
        catch (Exception)
        {
            // 啟動錯誤已發生；寫入診斷檔失敗時不可再遮蔽原始錯誤提示。
        }
    }
}
