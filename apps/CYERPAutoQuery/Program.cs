namespace CYERPAutoQuery;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var logger = new AppLogger();

        var appIdHr = NativeMethods.SetCurrentProcessExplicitAppUserModelID("Chihyuan.CYERPAutoQuery");
        if (appIdHr != 0)
            logger.Warn("app", $"SetCurrentProcessExplicitAppUserModelID failed HRESULT=0x{appIdHr:X8}");

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => logger.Error("ui", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.Error("fatal", ex);
        };

        Application.Run(new MainForm(logger));
        return 0;
    }
}
