using System.Text.Json;

namespace CYERPAutoInput;

internal sealed class UserSettings
{
    public int Version { get; set; } = 1;
    public bool AdvancedMode { get; set; }
    public bool DiagnosticLogging { get; set; }
    public bool AutoSave { get; set; }
    /// <summary>When on, a blank 庫別 on the first detail row is filled with <see cref="FirstRowWarehouse"/>; ERP carries it to later rows.</summary>
    public bool FirstRowWarehouseEnabled { get; set; }
    public string FirstRowWarehouse { get; set; } = string.Empty;
    /// <summary>Shopee import: 銷貨單別, 客戶代號 and 備註 prefix (real codes stay local, PROJECT_RULES §3).</summary>
    public string ShopeeOrderType { get; set; } = string.Empty;
    public string ShopeeCustomerCode { get; set; } = string.Empty;
    public string ShopeeNotePrefix { get; set; } = "蝦皮訂單";
    /// <summary>MO店+ import: 銷貨單別, 客戶代號, 備註 prefix, 折價券／運費品號 and 物流商→貨運別 (local only).</summary>
    public string MoOrderType { get; set; } = string.Empty;
    public string MoCustomerCode { get; set; } = string.Empty;
    public string MoNotePrefix { get; set; } = "MO店+訂單";
    public string MoDiscountItemCode { get; set; } = string.Empty;
    public string MoShippingItemCode { get; set; } = string.Empty;
    public Dictionary<string, string> MoFreightTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>MO店+ export password, DPAPI-protected for the current Windows user (base64).</summary>
    public string MoPasswordProtected { get; set; } = string.Empty;
    public Dictionary<string, string> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UserSettingsStore
{
    private readonly AppLogger _log;
    public string SettingsPath { get; }

    public UserSettingsStore(AppLogger log)
    {
        _log = log;
        var dir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new UserSettings();
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            loaded.Defaults ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            loaded.Defaults = new Dictionary<string, string>(loaded.Defaults, StringComparer.OrdinalIgnoreCase);
            loaded.MoFreightTypes = new Dictionary<string, string>(loaded.MoFreightTypes ?? [], StringComparer.OrdinalIgnoreCase);
            return loaded;
        }
        catch (Exception ex)
        {
            _log.Error("settings", ex);
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        settings.Version = 1;
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(settings, options) + Environment.NewLine;
        File.WriteAllText(SettingsPath, json);
        _log.Info("settings", $"saved local settings defaults={settings.Defaults.Count}");
    }
}

/// <summary>Windows DPAPI (current user) for the export password kept in settings.json.</summary>
internal static class LocalSecret
{
    public static string Protect(string plain) =>
        plain.Length == 0 ? string.Empty
        : Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(plain), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    /// <summary>The stored secret, or empty when none is stored or it cannot be read on this account.</summary>
    public static string Unprotect(string protectedBase64)
    {
        if (protectedBase64.Length == 0) return string.Empty;
        try
        {
            return System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return string.Empty;
        }
    }
}
