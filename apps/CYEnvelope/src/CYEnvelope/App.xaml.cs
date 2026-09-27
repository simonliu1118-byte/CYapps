using System.Windows;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CYEnvelope;

public partial class App : Application
{
    // The portable package keeps native WPF/SQLite dependencies under Runtime.
    // Set the process search path before the first window or database is created.
    public App()
    {
        var runtime = Path.Combine(AppContext.BaseDirectory, "Runtime");
        if (Directory.Exists(runtime) && !SetDllDirectory(runtime))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot initialize the portable Runtime folder.");
    }

    [DllImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
}
