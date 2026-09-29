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
            UiControls.InstallGlobalEnterNavigation();
            if (smokeTest)
            {
                RunStartupSmokeTest();
                return;
            }

            if (!RecoverPendingReset()) return;

            Application.Run(new MainForm());
            RunScheduledResetAfterShutdown();
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

    private static bool RecoverPendingReset()
    {
        var result = LocalResetCoordinator
            .RecoverPendingAsync(AppContext.BaseDirectory, new DpapiSecretProtector())
            .GetAwaiter()
            .GetResult();

        switch (result.Disposition)
        {
            case LocalResetRecoveryDisposition.None:
                return true;
            case LocalResetRecoveryDisposition.Completed:
                return true;
            case LocalResetRecoveryDisposition.AbortedSafely:
                MessageBox.Show(
                    result.Message,
                    "本機重設已取消",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return true;
            case LocalResetRecoveryDisposition.Blocked:
                MessageBox.Show(
                    result.Message,
                    "本機重設待確認",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            default:
                throw new InvalidOperationException("未知的本機重設復原狀態。");
        }
    }

    private static void RunScheduledResetAfterShutdown()
    {
        if (!LocalResetApplication.TryTake(out var request) || request is null) return;
        try
        {
            LocalResetCoordinator
                .ExecuteAsync(AppContext.BaseDirectory, new DpapiSecretProtector(), request)
                .GetAwaiter()
                .GetResult();
            Application.Restart();
        }
        catch (Exception error)
        {
            WriteStartupError(error);
            var recoveryPending = LocalResetCoordinator.HasPendingReset(AppContext.BaseDirectory);
            var title = recoveryPending ? "雲端退出待確認" : "無法完成本機重設";
            var message = recoveryPending
                ? error.Message + "\n\n本機資料尚未刪除。請稍後重新開啟 CYInvoice，程式會先確認 Cloud 狀態後再決定是否繼續。"
                : error.Message + "\n\n本機資料已保留，CYInvoice 將重新開啟。";
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (!recoveryPending) Application.Restart();
        }
    }

    private static void RunStartupSmokeTest()
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

        using var diagnostics = new SystemDiagnosticsForm(repository, startupSmokeTest: true);
        diagnostics.Show();
        diagnostics.PerformLayout();
        Application.DoEvents();
        diagnostics.VerifySmokeLayout();
        diagnostics.Close();

        using var settings = new SettingsForm(repository);
        settings.Show();
        settings.PerformLayout();
        Application.DoEvents();
        settings.VerifySmokeLayout();
        settings.Close();

        using var cloudSetup = new CloudSetupForm(repository);
        cloudSetup.Show();
        cloudSetup.PerformLayout();
        Application.DoEvents();
        cloudSetup.VerifySmokeLayout();
        cloudSetup.Close();

        SmokeCloudDeviceForms();
    }

    private static void SmokeCloudDeviceForms()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CYInvoice.CloudUiSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var repository = LocalRepository.Open(temporaryRoot, new DpapiSecretProtector());
            var settings = repository.Settings.LoadOrCreate();

            using var firstRun = new FirstRunModeForm();
            firstRun.Show();
            firstRun.PerformLayout();
            Application.DoEvents();
            firstRun.Close();

            using var directJoin = new CloudDirectJoinForm(repository);
            directJoin.Show();
            directJoin.PerformLayout();
            Application.DoEvents();
            directJoin.VerifySmokeLayout();
            directJoin.Close();

            // Device Join deliberately prompts for Local ADMIN/SUPER_ADMIN on Shown.
            // The startup smoke validates static layout without displaying the form so CI
            // never bypasses or blocks on the real runtime authorization gate.
            using var join = new CloudJoinWorkspaceForm(repository, settings, "https://cloud.example.test/");
            join.PerformLayout();
            join.VerifySmokeLayout();

            // Device Management now loads the trusted-Device inventory from Cloud on Shown.
            // Smoke validation must remain deterministic and offline, so validate the complete
            // static layout without firing Shown or any real network request.
            var token = $"cydev_{new string('a', 64)}";
            using var management = new CloudDeviceManagementForm("https://cloud.example.test/", token);
            management.PerformLayout();
            management.VerifySmokeLayout();
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
            }
            catch (Exception)
            {
                // Smoke cleanup failure must not hide the actual UI validation result.
            }
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
