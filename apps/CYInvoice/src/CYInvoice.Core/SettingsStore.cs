using System.Globalization;
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

    public void SetCloudDeviceToken(Settings settings, string token)
    {
        ArgumentNullException.ThrowIfNull(settings);
        token = token.Trim();
        settings.CloudDeviceTokenEncrypted = token.Length == 0
            ? string.Empty
            : protector.Protect(Encoding.UTF8.GetBytes(token));
    }

    public string CloudDeviceToken(Settings settings) => Unprotect(settings.CloudDeviceTokenEncrypted);

    public void MarkCloudEmployeeTransition(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!HasCloudIdentity(settings))
            throw new InvalidOperationException("Cloud Device identity 尚未完成，不能開始帳號轉換。");
        settings.CloudEmployeeAuthorityReady = false;
        settings.CloudMode = CloudModes.CloudTransition;
    }

    public void MarkCloudEmployeeAuthorityReady(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!HasCloudIdentity(settings))
            throw new InvalidOperationException("Cloud Device identity 尚未完成，不能切換中央帳號主資料。");
        settings.CloudEmployeeAuthorityReady = true;
        settings.CloudMode = CloudModes.CloudPreferred;
    }

    public void SetCloudPendingBootstrap(
        Settings settings,
        string baseUrl,
        string workspaceDisplayName,
        string deviceDisplayName,
        string deviceToken,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CloudPendingDeviceJoinTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有未完成的裝置加入流程，請先完成或清除後再建立第一個 Workspace。");

        baseUrl = NormalizeCloudBaseUrl(baseUrl);
        workspaceDisplayName = workspaceDisplayName.Trim();
        deviceDisplayName = deviceDisplayName.Trim();
        deviceToken = deviceToken.Trim();

        if (workspaceDisplayName.Length is < 1 or > 120)
            throw new InvalidOperationException("Workspace 名稱長度必須為 1 到 120 個字元。");
        if (deviceDisplayName.Length is < 1 or > 120)
            throw new InvalidOperationException("裝置名稱長度必須為 1 到 120 個字元。");
        if (!ValidCloudDeviceToken(deviceToken))
            throw new InvalidOperationException("Pending Device Token 格式無效。");

        settings.CloudPendingBootstrapUrl = baseUrl;
        settings.CloudPendingBootstrapWorkspaceName = workspaceDisplayName;
        settings.CloudPendingBootstrapDeviceName = deviceDisplayName;
        settings.CloudPendingBootstrapStartedUtc = startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        settings.CloudPendingBootstrapTokenEncrypted = protector.Protect(Encoding.UTF8.GetBytes(deviceToken));
        Validate(settings);
    }

    public CloudPendingBootstrapState? CloudPendingBootstrap(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        if (settings.CloudPendingBootstrapTokenEncrypted.Length == 0) return null;

        try
        {
            var token = Unprotect(settings.CloudPendingBootstrapTokenEncrypted);
            if (!ValidCloudDeviceToken(token))
                throw new InvalidDataException("Pending Device Token 解密後格式無效。");
            if (!DateTimeOffset.TryParseExact(
                    settings.CloudPendingBootstrapStartedUtc,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var startedAtUtc))
                throw new InvalidDataException("Pending Cloud bootstrap 時間格式無效。");

            return new CloudPendingBootstrapState(
                settings.CloudPendingBootstrapUrl,
                settings.CloudPendingBootstrapWorkspaceName,
                settings.CloudPendingBootstrapDeviceName,
                startedAtUtc.ToUniversalTime(),
                token);
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException("目前無法讀取待完成的雲端裝置初始化資料。", error);
        }
    }

    public void ClearCloudPendingBootstrap(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudPendingBootstrapUrl = string.Empty;
        settings.CloudPendingBootstrapWorkspaceName = string.Empty;
        settings.CloudPendingBootstrapDeviceName = string.Empty;
        settings.CloudPendingBootstrapStartedUtc = string.Empty;
        settings.CloudPendingBootstrapTokenEncrypted = string.Empty;
    }

    public void SetCloudPendingDeviceJoin(
        Settings settings,
        string baseUrl,
        string deviceDisplayName,
        string deviceToken,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CloudPendingBootstrapTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有未完成的第一台裝置初始化流程，請先完成或確認無法復原後再加入既有 Workspace。");

        baseUrl = NormalizeCloudBaseUrl(baseUrl);
        deviceDisplayName = deviceDisplayName.Trim();
        deviceToken = deviceToken.Trim();

        if (deviceDisplayName.Length is < 1 or > 120)
            throw new InvalidOperationException("裝置名稱長度必須為 1 到 120 個字元。");
        if (!ValidCloudDeviceToken(deviceToken))
            throw new InvalidOperationException("Pending Device Join Token 格式無效。");

        settings.CloudPendingDeviceJoinUrl = baseUrl;
        settings.CloudPendingDeviceJoinDeviceName = deviceDisplayName;
        settings.CloudPendingDeviceJoinStartedUtc = startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        settings.CloudPendingDeviceJoinTokenEncrypted = protector.Protect(Encoding.UTF8.GetBytes(deviceToken));
        Validate(settings);
    }

    public CloudPendingDeviceJoinState? CloudPendingDeviceJoin(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        if (settings.CloudPendingDeviceJoinTokenEncrypted.Length == 0) return null;

        try
        {
            var token = Unprotect(settings.CloudPendingDeviceJoinTokenEncrypted);
            if (!ValidCloudDeviceToken(token))
                throw new InvalidDataException("Pending Device Join Token 解密後格式無效。");
            if (!DateTimeOffset.TryParseExact(
                    settings.CloudPendingDeviceJoinStartedUtc,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var startedAtUtc))
                throw new InvalidDataException("Pending Device Join 時間格式無效。");

            return new CloudPendingDeviceJoinState(
                settings.CloudPendingDeviceJoinUrl,
                settings.CloudPendingDeviceJoinDeviceName,
                startedAtUtc.ToUniversalTime(),
                token);
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException("目前無法讀取待完成的雲端裝置加入資料。", error);
        }
    }

    public void ClearCloudPendingDeviceJoin(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudPendingDeviceJoinUrl = string.Empty;
        settings.CloudPendingDeviceJoinDeviceName = string.Empty;
        settings.CloudPendingDeviceJoinStartedUtc = string.Empty;
        settings.CloudPendingDeviceJoinTokenEncrypted = string.Empty;
    }

    public void ClearCloudIdentity(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudWorkspaceId = string.Empty;
        settings.CloudDeviceId = string.Empty;
        settings.CloudDeviceTokenEncrypted = string.Empty;
        settings.CloudEmployeeAuthorityReady = false;
        ClearCloudPendingBootstrap(settings);
        ClearCloudPendingDeviceJoin(settings);
        settings.CloudMode = CloudModes.LocalOnly;
    }

    private string Unprotect(string value) => value.Length == 0 ? string.Empty : Encoding.UTF8.GetString(protector.Unprotect(value));

    private static void Validate(Settings settings)
    {
        if (settings.Environment is not Environments.Test and not Environments.Production)
            throw new InvalidDataException($"unknown environment {settings.Environment}");
        if (settings.ProductionInvoice.Length != 0 && !EightDigits(settings.ProductionInvoice))
            throw new InvalidDataException("正式公司統編必須為 8 碼");
        if (settings.CloudMode is not CloudModes.LocalOnly
            and not CloudModes.CloudTransition
            and not CloudModes.CloudPreferred)
            throw new InvalidDataException($"unknown cloud mode {settings.CloudMode}");
        if (settings.CloudBaseUrl.Length != 0 && !ValidCloudBaseUrl(settings.CloudBaseUrl))
            throw new InvalidDataException("Cloud API URL 必須是有效的 HTTPS 網址");
        if (settings.CloudWorkspaceId.Length > 80 || settings.CloudDeviceId.Length > 80)
            throw new InvalidDataException("Cloud workspace/device ID 格式無效");
        if (settings.CloudEmployeeAuthorityReady && !HasCloudIdentity(settings))
            throw new InvalidDataException("中央帳號主資料狀態缺少 Cloud Device identity。");
        if (settings.CloudMode == CloudModes.CloudTransition
            && (!HasCloudIdentity(settings) || settings.CloudEmployeeAuthorityReady))
            throw new InvalidDataException("Cloud 帳號轉換狀態與 Device identity 不一致。");
        if (settings.CloudMode == CloudModes.CloudPreferred
            && HasCloudIdentity(settings)
            && !settings.CloudEmployeeAuthorityReady)
            throw new InvalidDataException("Cloud Device 尚未完成中央帳號主資料切換。");

        ValidateCloudPendingBootstrap(settings);
        ValidateCloudPendingDeviceJoin(settings);
        if (settings.CloudPendingBootstrapTokenEncrypted.Length != 0
            && settings.CloudPendingDeviceJoinTokenEncrypted.Length != 0)
            throw new InvalidDataException("Pending Cloud bootstrap 與 Device Join 不可同時存在。");
    }

    private static void ValidateCloudPendingBootstrap(Settings settings)
    {
        var values = new[]
        {
            settings.CloudPendingBootstrapUrl,
            settings.CloudPendingBootstrapWorkspaceName,
            settings.CloudPendingBootstrapDeviceName,
            settings.CloudPendingBootstrapStartedUtc,
            settings.CloudPendingBootstrapTokenEncrypted
        };
        var present = values.Count(value => value.Length != 0);
        if (present == 0) return;
        if (present != values.Length)
            throw new InvalidDataException("Pending Cloud bootstrap 資料不完整。");
        if (!ValidCloudBaseUrl(settings.CloudPendingBootstrapUrl))
            throw new InvalidDataException("Pending Cloud bootstrap API URL 無效。");
        if (settings.CloudPendingBootstrapWorkspaceName.Trim().Length is < 1 or > 120)
            throw new InvalidDataException("Pending Cloud bootstrap Workspace 名稱無效。");
        if (settings.CloudPendingBootstrapDeviceName.Trim().Length is < 1 or > 120)
            throw new InvalidDataException("Pending Cloud bootstrap 裝置名稱無效。");
        if (!DateTimeOffset.TryParseExact(
                settings.CloudPendingBootstrapStartedUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
            throw new InvalidDataException("Pending Cloud bootstrap 時間格式無效。");
    }

    private static void ValidateCloudPendingDeviceJoin(Settings settings)
    {
        var values = new[]
        {
            settings.CloudPendingDeviceJoinUrl,
            settings.CloudPendingDeviceJoinDeviceName,
            settings.CloudPendingDeviceJoinStartedUtc,
            settings.CloudPendingDeviceJoinTokenEncrypted
        };
        var present = values.Count(value => value.Length != 0);
        if (present == 0) return;
        if (present != values.Length)
            throw new InvalidDataException("Pending Device Join 資料不完整。");
        if (!ValidCloudBaseUrl(settings.CloudPendingDeviceJoinUrl))
            throw new InvalidDataException("Pending Device Join API URL 無效。");
        if (settings.CloudPendingDeviceJoinDeviceName.Trim().Length is < 1 or > 120)
            throw new InvalidDataException("Pending Device Join 裝置名稱無效。");
        if (!DateTimeOffset.TryParseExact(
                settings.CloudPendingDeviceJoinStartedUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
            throw new InvalidDataException("Pending Device Join 時間格式無效。");
    }

    private static string NormalizeCloudBaseUrl(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Cloud API URL 必須是有效的 HTTPS 網址。");

        var normalized = uri.AbsoluteUri;
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private static bool ValidCloudBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool HasCloudIdentity(Settings settings) =>
        settings.CloudWorkspaceId.Length != 0
        && settings.CloudDeviceId.Length != 0
        && settings.CloudDeviceTokenEncrypted.Length != 0;

    private static bool ValidCloudDeviceToken(string value)
    {
        if (!value.StartsWith("cydev_", StringComparison.Ordinal) || value.Length != 70) return false;
        return value.AsSpan(6).ToString().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool EightDigits(string value) => value.Length == 8 && value.All(character => character is >= '0' and <= '9');
}
