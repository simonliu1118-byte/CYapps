using System.Reflection;
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

        var appIdHr = NativeMethods.SetCurrentProcessExplicitAppUserModelID("Chihyuan.CYERPAutoInput");
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

        var form = new MainForm(logger);
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CYERPAutoInput.Auto.ico");
            if (iconStream is not null)
            {
                using var embeddedIcon = new Icon(iconStream);
                form.Icon = (Icon)embeddedIcon.Clone();
                logger.Info("app", "window/taskbar icon loaded from embedded canonical Auto.ico");
            }
            else
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                logger.Warn("app", "embedded canonical icon missing; used executable associated icon fallback");
            }
        }
        catch (Exception ex)
        {
            logger.Warn("app", $"window icon load skipped: {ex.Message}");
        }
        CyVisualTheme.Apply(form);
        Build10UiPatch.Apply(form);
        Application.Run(form);
        return 0;
    }
}
