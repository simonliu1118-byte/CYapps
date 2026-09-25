using System.Text.Json;

namespace CYERPAutoInput;

internal sealed class UserSettings
{
    public int Version { get; set; } = 1;
    public bool AdvancedMode { get; set; }
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
