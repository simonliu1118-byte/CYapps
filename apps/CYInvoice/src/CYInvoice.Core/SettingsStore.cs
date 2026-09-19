using System.Security.Cryptography;
using System.Text;

namespace CYInvoice.Core.Storage;

public sealed class SettingsStore(string dataDirectory, ISecretProtector protector)
{
    private const int PasswordIterations = 210_000;
    private const string PasswordPrefix = "pbkdf2-sha256$210000$";
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

    public void SetAdminPassword(Settings settings, string password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (password.Length == 0) throw new InvalidOperationException("管理密碼不可空白");
        settings.PasswordSalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        settings.PasswordHash = PasswordPrefix + Convert.ToHexString(Derive(password, settings.PasswordSalt)).ToLowerInvariant();
        settings.AdminPasswordSet = true;
    }

    public void RetireLegacyAdminPassword(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.PasswordSalt = string.Empty;
        settings.PasswordHash = string.Empty;
        settings.AdminPasswordSet = false;
    }

    public static bool CheckAdminPassword(Settings settings, string password)
    {
        if (!settings.AdminPasswordSet || settings.PasswordSalt.Length == 0 || settings.PasswordHash.Length == 0) return false;
        if (settings.PasswordHash.StartsWith(PasswordPrefix, StringComparison.Ordinal))
        {
            if (!TryHex(settings.PasswordHash[PasswordPrefix.Length..], out var expected) || expected.Length != 32) return false;
            return CryptographicOperations.FixedTimeEquals(Derive(password, settings.PasswordSalt), expected);
        }
        if (!TryHex(settings.PasswordHash, out var legacyExpected)) return false;
        var saltFirst = SHA256.HashData(Encoding.UTF8.GetBytes(settings.PasswordSalt + password));
        var passwordFirst = SHA256.HashData(Encoding.UTF8.GetBytes(password + settings.PasswordSalt));
        return legacyExpected.Length == saltFirst.Length &&
            (CryptographicOperations.FixedTimeEquals(saltFirst, legacyExpected) || CryptographicOperations.FixedTimeEquals(passwordFirst, legacyExpected));
    }

    public bool NeedsInitialSetup(Settings settings) => !settings.AdminPasswordSet || string.IsNullOrWhiteSpace(settings.MoPasswordEncrypted);

    public bool InitialSetupRequired(Settings settings)
    {
        if (NeedsInitialSetup(settings)) return true;
        return string.IsNullOrWhiteSpace(MoPassword(settings));
    }

    public void SetMoPassword(Settings settings, string password)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("MO店+ Excel 密碼不可空白");
        settings.MoPasswordEncrypted = protector.Protect(Encoding.UTF8.GetBytes(password));
    }

    public string MoPassword(Settings settings) => Unprotect(settings.MoPasswordEncrypted);

    public void SetProductionAppKey(Settings settings, string appKey)
    {
        appKey = appKey.Trim();
        settings.ProductionAppKeyEncrypted = appKey.Length == 0 ? string.Empty : protector.Protect(Encoding.UTF8.GetBytes(appKey));
    }

    public string ProductionAppKey(Settings settings) => Unprotect(settings.ProductionAppKeyEncrypted);

    private string Unprotect(string value) => value.Length == 0 ? string.Empty : Encoding.UTF8.GetString(protector.Unprotect(value));

    private static byte[] Derive(string password, string salt) => Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), PasswordIterations, HashAlgorithmName.SHA256, 32);

    private static void Validate(Settings settings)
    {
        if (settings.Environment is not Environments.Test and not Environments.Production)
            throw new InvalidDataException($"unknown environment {settings.Environment}");
        if (settings.ProductionInvoice.Length != 0 && !EightDigits(settings.ProductionInvoice))
            throw new InvalidDataException("正式公司統編必須為 8 碼");
        if (settings.AdminPasswordSet)
        {
            if (!TryHex(settings.PasswordSalt, out var salt) || settings.PasswordSalt.Length < 16 || salt.Length == 0)
                throw new InvalidDataException("invalid password_salt");
            var hashText = settings.PasswordHash.StartsWith(PasswordPrefix, StringComparison.Ordinal)
                ? settings.PasswordHash[PasswordPrefix.Length..] : settings.PasswordHash;
            if (!TryHex(hashText, out var hash) || hash.Length != 32) throw new InvalidDataException("invalid password_hash");
        }
        else if (settings.PasswordSalt.Length != 0 || settings.PasswordHash.Length != 0)
        {
            throw new InvalidDataException("retired management password fields must be empty");
        }
        if (settings.Environment == Environments.Production &&
            (settings.ProductionInvoice.Length == 0 || settings.ProductionAppKeyEncrypted.Length == 0))
            throw new InvalidDataException("正式公司請輸入 8 碼公司統編與 App Key");
    }

    private static bool EightDigits(string value) => value.Length == 8 && value.All(character => character is >= '0' and <= '9');
    private static bool TryHex(string value, out byte[] bytes)
    {
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { bytes = []; return false; }
    }
}
