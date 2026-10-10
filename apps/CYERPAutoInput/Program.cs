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

        var snapshotIndex = Array.FindIndex(args, a => a.Equals("--ui-snapshot", StringComparison.OrdinalIgnoreCase));
        if (snapshotIndex >= 0)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var folder = snapshotIndex + 1 < args.Length ? args[snapshotIndex + 1] : "ui-snapshots";
            return UiSnapshot.Run(logger, folder);
        }

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
                using var copy = new MemoryStream();
                iconStream.CopyTo(copy);
                var iconBytes = copy.ToArray();
                using var embeddedIcon = new Icon(new MemoryStream(iconBytes));
                form.Icon = (Icon)embeddedIcon.Clone();
                // The taskbar button showed a generic icon while the title bar was right
                // (user, 2026-10-10): give the window explicit big and small icons at the
                // system sizes once its handle exists.
                form.HandleCreated += (_, _) => WindowIcons.Apply(form.Handle, iconBytes, logger);
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
        Application.Run(form);
        return 0;
    }
}

/// <summary>Sets a window's big (taskbar, Alt+Tab) and small (title bar) icons explicitly.</summary>
internal static class WindowIcons
{
    private const uint WmSetIcon = 0x0080;
    private static readonly List<Icon> Alive = []; // icon handles must outlive the window

    public static void Apply(nint hwnd, byte[] icoBytes, AppLogger log)
    {
        try
        {
            var big = new Icon(new MemoryStream(icoBytes), SystemInformation.IconSize);
            var small = new Icon(new MemoryStream(icoBytes), SystemInformation.SmallIconSize);
            Alive.Add(big);
            Alive.Add(small);
            NativeMethods.SendMessage(hwnd, WmSetIcon, 1, big.Handle);
            NativeMethods.SendMessage(hwnd, WmSetIcon, 0, small.Handle);
            log.Info("app", $"WM_SETICON big={big.Width} small={small.Width}");
        }
        catch (Exception ex)
        {
            log.Warn("app", $"WM_SETICON skipped: {ex.GetType().Name}");
        }
    }
}
