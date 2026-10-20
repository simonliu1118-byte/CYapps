using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CYERPAutoQuery;

/// <summary>
/// The CY Web connection of this PC: base URL, Agent token and whether the Agent runs. Kept in
/// <c>config/agent.json</c> next to the program (never in Git); the token is encrypted with
/// Windows DPAPI for the current Windows user, so the file is useless on another account or PC.
/// </summary>
internal sealed class ErpAgentSettings
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Chihyuan.CYERPAutoQuery.AgentToken");

    public string BaseUrl { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "config", "agent.json");

    public bool HasToken => ProtectedToken.Length > 0;

    public bool IsComplete => Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && HasToken;

    public void SetToken(string token) =>
        ProtectedToken = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), Entropy, DataProtectionScope.CurrentUser));

    public string ReadToken() =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), Entropy, DataProtectionScope.CurrentUser));

    public static ErpAgentSettings Load(AppLogger log)
    {
        try
        {
            if (!File.Exists(FilePath)) return new ErpAgentSettings();
            return JsonSerializer.Deserialize<ErpAgentSettings>(File.ReadAllText(FilePath)) ?? new ErpAgentSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warn("agent", $"agent settings unreadable: {ex.GetType().Name}");
            return new ErpAgentSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
