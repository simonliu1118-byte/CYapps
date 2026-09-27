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
        if (!Directory.Exists(runtime)) return; // normal development output
        if (!SetDllDirectory(runtime))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot initialize the portable Runtime folder.");
        // WPF uses explicit native library lookup for some interop calls. Load the
        // signed publish outputs by absolute path before WPF creates an HWND.
        foreach (var name in new[] {
            "vcruntime140_cor3.dll", "D3DCompiler_47_cor3.dll", "wpfgfx_cor3.dll",
            "PenImc_cor3.dll", "PresentationNative_cor3.dll", "e_sqlite3.dll"
        })
        {
            if (LoadLibraryEx(Path.Combine(runtime, name), IntPtr.Zero, 0x00000008) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot load Runtime/{name}.");
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
}
