using System.Text;

namespace CYInvoice.Core.Storage;

public sealed class SettingsStore(string dataDirectory, ISecretProtector protector)
{
    private readonly Lock gate = new();
    private readonly string path = Path.Combine(dataDirectory, "settings.json");

    public Settings LoadOrCreate()
    {
        lock (gate)
        {
            if (JsonFile.TryRead<Settings>(path, out var existing))
            {
                Validate(existing!);
                return existing!;
            }
            var settings = new Settings();
            JsonFile.Write(path, settings);
            return settings;
        }
    }

    public void Save(Settings settings)
    {
        lock (gate) { Validate(settings); JsonFile.Write(path, settings); }
    }

    public void SetMoPassword(Settings settings, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("MO店+ Excel 密碼不可空白");
        settings.MoPasswordEncrypted = protector.Protect(Encoding.UTF8.GetBytes(password));
    }

    public string MoPassword(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.MoPasswordEncrypted.Length == 0)
            throw new InvalidOperationException("尚未設定 MO店+ Excel 保護密碼，請由管理員至「設定」輸入後再匯入。");
        try
        {
            var password = Unprotect(settings.MoPasswordEncrypted);
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidDataException("MO店+ Excel 密碼解密後為空白");
            return password;
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "目前無法讀取 MO店+ Excel 保護密碼，請由管理員至「設定」重新輸入後再匯入。",
                error);
        }
    }

    public void SetProductionAppKey(Settings settings, string appKey)
    {
        appKey = appKey.Trim();
        settings.ProductionAppKeyEncrypted = appKey.Length == 0 ? string.Empty : protector.Protect(Encoding.UTF8.GetBytes(appKey));
    }

    public string ProductionAppKey(Settings settings) => Unprotect(settings.ProductionAppKeyEncrypted);

    private string Unprotect(string value) => value.Length == 0 ? string.Empty : Encoding.UTF8.GetString(protector.Unprotect(value));

    private static void Validate(Settings settings)
    {
        if (settings.Environment is not Environments.Test and not Environments.Production)
            throw new InvalidDataException($"unknown environment {settings.Environment}");
        if (settings.ProductionInvoice.Length != 0 && !EightDigits(settings.ProductionInvoice))
            throw new InvalidDataException("正式公司統編必須為 8 碼");
    }

    private static bool EightDigits(string value) => value.Length == 8 && value.All(character => character is >= '0' and <= '9');
}
