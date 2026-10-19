using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CYERPAutoQuery;

/// <summary>
/// ERP 自動登入與閒置關閉 (user, 2026-10-19): where SMART ERP is (set like CYERPAutoInput's
/// CYInvoice.exe), its 帳號／密碼／公司, and the idle minutes after which the 建立作業 forms
/// (INVI02／COPI01／PURI01, default 10) and ERP itself (default 30, so it does not hold a seat)
/// are closed. Kept in <c>config/erp.json</c> next to the program (never in Git, kept by
/// updates); the password is encrypted with Windows DPAPI for the current Windows user.
/// </summary>
internal sealed class ErpLoginSettings
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Chihyuan.CYERPAutoQuery.ErpPassword");

    public const string DefaultCompany = "志遠醫療器材行";
    public const int DefaultFormIdleMinutes = 10, DefaultIdleMinutes = 30;

    public string ErpPath { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    public string Company { get; set; } = DefaultCompany;
    /// <summary>Close INVI02／COPI01／PURI01 after this many minutes without a query; 0 never.</summary>
    public int FormIdleMinutes { get; set; } = DefaultFormIdleMinutes;
    /// <summary>Close ERP after this many minutes without a query; 0 never closes it.</summary>
    public int IdleCloseMinutes { get; set; } = DefaultIdleMinutes;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config", "erp.json");

    public bool HasPassword => ProtectedPassword.Length > 0;

    /// <summary>Everything needed to start and log in to ERP without anyone at the PC.</summary>
    public bool CanAutoLogin => ErpPath.Length > 0 && Account.Trim().Length > 0 && HasPassword && Company.Trim().Length > 0;

    /// <summary>Whether ERP, last used at <paramref name="lastUse"/>, has been idle long enough to close.</summary>
    public bool IdleDue(DateTime lastUse, DateTime now) => Due(IdleCloseMinutes, lastUse, now);

    /// <summary>Whether the 建立作業 forms have been idle long enough to close.</summary>
    public bool FormsIdleDue(DateTime lastUse, DateTime now) => Due(FormIdleMinutes, lastUse, now);

    private static bool Due(int minutes, DateTime lastUse, DateTime now) =>
        minutes > 0 && now - lastUse >= TimeSpan.FromMinutes(minutes);

    public void SetPassword(string password) =>
        ProtectedPassword = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    public string ReadPassword() =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), Entropy, DataProtectionScope.CurrentUser));

    public static ErpLoginSettings Load(Action<string> warn)
    {
        try
        {
            if (!File.Exists(FilePath)) return new ErpLoginSettings();
            return JsonSerializer.Deserialize<ErpLoginSettings>(File.ReadAllText(FilePath)) ?? new ErpLoginSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            warn($"settings unreadable: {ex.GetType().Name}");
            return new ErpLoginSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
