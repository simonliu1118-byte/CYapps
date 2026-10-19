using System.Text.Json;
using System.Text.Json.Serialization;

namespace CYERPAutoInput;

internal sealed class UserSettings
{
    public int Version { get; set; } = 1;
    public bool AdvancedMode { get; set; }
    public bool AutoSave { get; set; }
    /// <summary>After a verified auto-save, ERP 列印 → 直接列印 (user, 2026-10-11).</summary>
    public bool AutoPrint { get; set; }
    /// <summary>A document CY saved clears the form for the next order (user, 2026-10-12).</summary>
    public bool ClearAfterSave { get; set; } = true;
    /// <summary>
    /// 蝦皮全自動 (batch, user 2026-10-12): Chrome's print dialog must show this printer and
    /// scale before the label is printed; otherwise the batch stops. Blank until ticked in
    /// 設定 › 印表機 among this PC's printers (user, 2026-10-15: no built-in name).
    /// </summary>
    public string ShopeeLabelPrinter { get; set; } = string.Empty;
    public int ShopeeLabelScale { get; set; } = 110;
    /// <summary>
    /// A4 印表機 (user, 2026-10-14: the Brother) for MO店+ 新竹物流 寄件單 and 到廠取貨總表, printed
    /// after a prompt to load that paper. Blank = not set.
    /// </summary>
    public string A4Printer { get; set; } = string.Empty;
    /// <summary>
    /// CYInvoice.exe, chosen with a file picker (user, 2026-10-14: never typed); blank = not set.
    /// MO店+ 發票 goes through its AutoInput interface (README「MO店+ 發票（CYInvoice）」).
    /// </summary>
    public string CyInvoicePath { get; set; } = string.Empty;
    /// <summary>紙本發票 PDF style (CYInvoice's company styles; user, 2026-10-15: chosen beforehand), printed on the A4 printer.</summary>
    public int PaperInvoiceStyle { get; set; }
    /// <summary>7-ELEVEN 交貨便服務單 print at 100 % (user, 2026-10-13).</summary>
    public int SevenLabelScale { get; set; } = 100;
    /// <summary>全家 FamilyMart寄取貨單: 頁面 自訂 2, 縮放比例 165 % (user, 2026-10-13).</summary>
    public int FamilyLabelScale { get; set; } = 165;
    public string FamilyLabelPages { get; set; } = "2";
    /// <summary>新竹物流 (安排取件) labels print at 95 % (user, 2026-10-14).</summary>
    public int HctLabelScale { get; set; } = 95;
    /// <summary>酷澎 新竹物流 寄件單 on the thermal printer, Chrome 縮放 (user, 2026-10-19: 22 %).</summary>
    public int CoupangHctLabelScale { get; set; } = 22;
    /// <summary>
    /// The 安排取件 dialog's 快遞向你取件的地址 must contain this (spaces ignored) before 確認 is
    /// pressed (user, 2026-10-14). Kept here, never in the source; blank stops 新竹物流 orders.
    /// </summary>
    public string HctPickupAddress { get; set; } = string.Empty;
    /// <summary>
    /// 設定 › 發送訊息 (user, 2026-10-14): per platform, rules of words looked for in the buyer's
    /// note and the message sent for them. 蝦皮 is used by 全自動 (聊聊, before the order is
    /// shipped as usual); MO店+ and 酷澎 are kept for later.
    /// </summary>
    public List<ChatRule> ShopeeChatRules { get; set; } = [];
    public List<ChatRule> MoChatRules { get; set; } = [];
    public List<ChatRule> CoupangChatRules { get; set; } = [];
    /// <summary>設定 › 滿件贈送 (user, 2026-10-14): free quantity into 贈/備品量 for imported orders.</summary>
    public List<GiftRule> GiftRules { get; set; } = [];

    // Build 25 kept one 蝦皮 rule as two strings; read once and moved into ShopeeChatRules.
    private string _legacyChatKeywords = "發票,收據,統編,統一編號,抬頭";
    private string _legacyChatMessage = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopeeChatKeywords { get => null; set { if (!string.IsNullOrWhiteSpace(value)) _legacyChatKeywords = value; } }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopeeChatMessage { get => null; set { if (!string.IsNullOrWhiteSpace(value)) _legacyChatMessage = value; } }

    internal void MigrateLegacyChat()
    {
        if (ShopeeChatRules.Count == 0 && _legacyChatMessage.Trim().Length > 0)
            ShopeeChatRules.Add(new ChatRule { Keywords = _legacyChatKeywords, Message = _legacyChatMessage });
        _legacyChatMessage = string.Empty;
    }
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
    /// <summary>酷澎 import: 銷貨單別, 客戶代號 and 備註 prefix (local only).</summary>
    public string CoupangOrderType { get; set; } = string.Empty;
    public string CoupangCustomerCode { get; set; } = string.Empty;
    public string CoupangNotePrefix { get; set; } = "酷澎訂單";
    /// <summary>Shared by every platform: 折價券／運費品號 and carrier → ERP 貨運別 (local only).</summary>
    public string DiscountItemCode { get; set; } = string.Empty;
    public string ShippingItemCode { get; set; } = string.Empty;
    /// <summary>促銷組合品號: one line for the struck-out promotion discounts (Shopee screenshots).</summary>
    public string PromotionItemCode { get; set; } = string.Empty;
    public Dictionary<string, string> FreightTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Build 14–19 kept these under MO店+; read once and moved to the shared settings.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MoDiscountItemCode { get => null; set { if (DiscountItemCode.Length == 0 && !string.IsNullOrWhiteSpace(value)) DiscountItemCode = value; } }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MoShippingItemCode { get => null; set { if (ShippingItemCode.Length == 0 && !string.IsNullOrWhiteSpace(value)) ShippingItemCode = value; } }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? MoFreightTypes
    {
        get => null;
        set { if (value is not null) foreach (var p in value) FreightTypes.TryAdd(p.Key, p.Value); }
    }
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
            loaded.FreightTypes = new Dictionary<string, string>(loaded.FreightTypes ?? [], StringComparer.OrdinalIgnoreCase);
            loaded.ShopeeChatRules ??= [];
            loaded.MoChatRules ??= [];
            loaded.CoupangChatRules ??= [];
            loaded.GiftRules ??= [];
            loaded.MigrateLegacyChat();
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
