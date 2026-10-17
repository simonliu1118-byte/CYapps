using System.Reflection;

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

        var form = new MainForm(logger);
        ApplyIcon(form, logger);
        Application.Run(form);
        return 0;
    }

    /// <summary>Window and taskbar icon from the embedded canonical AutoQ.ico (same as CYERPAutoInput).</summary>
    private static void ApplyIcon(Form form, AppLogger logger)
    {
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CYERPAutoQuery.AutoQ.ico");
            if (iconStream is null)
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                logger.Warn("app", "embedded canonical icon missing; used executable associated icon fallback");
                return;
            }
            using var copy = new MemoryStream();
            iconStream.CopyTo(copy);
            var iconBytes = copy.ToArray();
            using var embeddedIcon = new Icon(new MemoryStream(iconBytes));
            form.Icon = (Icon)embeddedIcon.Clone();
            form.HandleCreated += (_, _) => WindowIcons.Apply(form.Handle, iconBytes, logger);
        }
        catch (Exception ex)
        {
            logger.Warn("app", $"window icon load skipped: {ex.Message}");
        }
    }
}

/// <summary>
/// Sets a window's big (taskbar, Alt+Tab) and small (title bar) icons explicitly (copied from
/// CYERPAutoInput Program.cs).
/// </summary>
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

