using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CYEnvelope.Launcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var root = AppContext.BaseDirectory;
            var executable = Path.Combine(root, "Runtime", "CYEnvelope.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Runtime/CYEnvelope.exe is missing.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = root
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var app = Process.Start(start) ?? throw new Win32Exception("Cannot start CYEnvelope.");
            app.WaitForExit();
            return app.ExitCode;
        }
        catch (Exception error)
        {
            MessageBox(IntPtr.Zero, $"CYEnvelope 無法啟動。請保留 Runtime 資料夾。\n\n{error.Message}",
                "CYEnvelope", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
