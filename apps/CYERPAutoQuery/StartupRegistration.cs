using Microsoft.Win32;

namespace CYERPAutoQuery;

/// <summary>
/// 開機自動開啟 (user, 2026-10-19): the program in the current Windows user's Run key. Updates
/// replace the files in place, so the path stays valid.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CYERPAutoQuery";

    private static string Command => $"\"{Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CYERPAutoQuery.exe")}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>A Run entry pointing at another copy of the program is moved here.</summary>
    public static void Refresh()
    {
        if (IsEnabled()) Set(true);
    }
}
