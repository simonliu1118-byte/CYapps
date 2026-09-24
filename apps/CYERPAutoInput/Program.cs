using System.Runtime.Versioning;

namespace CYERPAutoInput;

internal static class Program
{
    [STAThread]
    [SupportedOSPlatform("windows10.0.19041.0")]
    private static int Main(string[] args)
    {
        using var logger = new AppLogger();

        if (args.Any(a => a.Equals("--vision-self-test", StringComparison.OrdinalIgnoreCase)))
            return VisionSelfTest.RunAsync(logger).GetAwaiter().GetResult();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => logger.Error("ui", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.Error("fatal", ex);
        };

        var form = new MainForm(logger)
        {
            Text = "CYERPAutoInput V0.1.0 Build 2 — SMART ERP 自動輸入工具"
        };
        Application.Run(form);
        return 0;
    }
}
