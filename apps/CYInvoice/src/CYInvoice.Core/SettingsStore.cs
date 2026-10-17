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
        ClearCloudCompanyStamp(settings);
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
        ClearCloudCompanyStamp(settings);
    }

    public string ProductionAppKey(Settings settings) => Unprotect(settings.ProductionAppKeyEncrypted);

    // Called only after explicit CYID SUPER_ADMIN provisioning and successful
    // DPAPI encryption of both credentials. The marker never grants authority.
    public void StampCloudCompanyProvision(Settings settings, int revision)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (revision < 1 || settings.CloudMode != CloudModes.CloudPreferred ||
            settings.CloudIdentityProvider != "CYID" || !settings.CloudEmployeeAuthorityReady ||
            settings.CloudWorkspaceId.Length == 0 || settings.CloudDeviceId.Length == 0 ||
            !EightDigits(settings.ProductionInvoice) ||
            settings.ProductionAppKeyEncrypted.Length == 0 ||
            settings.MoPasswordEncrypted.Length == 0)
            throw new InvalidOperationException("無法確認已授權的 CYID 公司卡與本機憑證。");
        settings.CloudCompanyAppliedRevision = revision;
        settings.CloudCompanyAppliedWorkspaceId = settings.CloudWorkspaceId;
        settings.CloudCompanyAppliedDeviceId = settings.CloudDeviceId;
        settings.CloudCompanyAppliedSellerInvoice = settings.ProductionInvoice;
    }

    public void ClearCloudCompanyStamp(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudCompanyAppliedRevision = 0;
        settings.CloudCompanyAppliedWorkspaceId = string.Empty;
        settings.CloudCompanyAppliedDeviceId = string.Empty;
        settings.CloudCompanyAppliedSellerInvoice = string.Empty;
    }

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
        if (settings.CloudPendingDeviceJoinTokenEncrypted.Length != 0
            || settings.CloudPendingRecoveryTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有未完成的裝置加入或復原流程，請先處理原有 Pending Token。");

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
        if (settings.CloudPendingBootstrapTokenEncrypted.Length != 0
            || settings.CloudPendingRecoveryTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有未完成的第一台裝置或復原流程，請先處理原有 Pending Token。");

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

    public void SetCloudPendingRecovery(
        Settings settings,
        string baseUrl,
        string workspaceId,
        string identityWorkspaceId,
        string invitationId,
        string deviceDisplayName,
        string deviceToken,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (HasCloudIdentity(settings))
            throw new InvalidOperationException("目前已經有 Cloud Device，災難復原只能從尚未加入的新裝置進行。");
        if (settings.CloudPendingBootstrapTokenEncrypted.Length != 0
            || settings.CloudPendingDeviceJoinTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有其他未完成的裝置加入流程，不能改用災難復原。");
        if (settings.CloudPendingRecoveryTokenEncrypted.Length != 0)
            throw new InvalidOperationException("已有未完成的復原 Token；請先查回原操作，不能建立新的 Token。");

        baseUrl = NormalizeCloudBaseUrl(baseUrl);
        deviceDisplayName = deviceDisplayName.Trim();
        deviceToken = deviceToken.Trim();
        if (!ValidCloudDeviceToken(deviceToken)
            || !ValidRecoveryIdentifier(workspaceId)
            || !ValidRecoveryIdentifier(identityWorkspaceId)
            || !ValidRecoveryInvitationId(invitationId)
            || deviceDisplayName.Length is < 1 or > 120
            || deviceDisplayName.Any(char.IsControl))
            throw new InvalidOperationException("災難復原目標範圍或 Pending Token 無效。");

        settings.CloudPendingRecoveryUrl = baseUrl;
        settings.CloudPendingRecoveryWorkspaceId = workspaceId;
        settings.CloudPendingRecoveryIdentityWorkspaceId = identityWorkspaceId;
        settings.CloudPendingRecoveryInvitationId = invitationId;
        settings.CloudPendingRecoveryDeviceName = deviceDisplayName;
        settings.CloudPendingRecoveryStartedUtc = startedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        settings.CloudPendingRecoveryTokenEncrypted = protector.Protect(Encoding.UTF8.GetBytes(deviceToken));
        Validate(settings);
    }

    public CloudPendingRecoveryState? CloudPendingRecovery(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        if (settings.CloudPendingRecoveryTokenEncrypted.Length == 0) return null;
        try
        {
            var token = Unprotect(settings.CloudPendingRecoveryTokenEncrypted);
            if (!ValidCloudDeviceToken(token)
                || !DateTimeOffset.TryParseExact(settings.CloudPendingRecoveryStartedUtc,
                    "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                    out var startedAt))
                throw new InvalidDataException("Pending Recovery Token 格式無效。");
            return new CloudPendingRecoveryState(
                settings.CloudPendingRecoveryUrl,
                settings.CloudPendingRecoveryWorkspaceId,
                settings.CloudPendingRecoveryIdentityWorkspaceId,
                settings.CloudPendingRecoveryInvitationId,
                settings.CloudPendingRecoveryDeviceName,
                startedAt.ToUniversalTime(),
                token);
        }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException("無法讀取已保護的 Pending Recovery Token；不可另建 Token 重試。", error);
        }
    }

    public void ClearCloudPendingRecovery(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudPendingRecoveryUrl = string.Empty;
        settings.CloudPendingRecoveryWorkspaceId = string.Empty;
        settings.CloudPendingRecoveryIdentityWorkspaceId = string.Empty;
        settings.CloudPendingRecoveryInvitationId = string.Empty;
        settings.CloudPendingRecoveryDeviceName = string.Empty;
        settings.CloudPendingRecoveryStartedUtc = string.Empty;
        settings.CloudPendingRecoveryTokenEncrypted = string.Empty;
    }

    public void ClearCloudIdentity(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CloudWorkspaceId = string.Empty;
        settings.CloudDeviceId = string.Empty;
        settings.CloudDeviceTokenEncrypted = string.Empty;
        ClearCloudCompanyStamp(settings);
        settings.CloudEmployeeAuthorityReady = false;
        settings.CloudIdentityProvider = "BUILT_IN";
        settings.CyIdBindingEncrypted = string.Empty;
        ClearCloudPendingBootstrap(settings);
        ClearCloudPendingDeviceJoin(settings);
        ClearCloudPendingRecovery(settings);
        settings.CloudMode = CloudModes.LocalOnly;
    }

    public void ConfirmCyIdConfiguration(Settings settings, CyIdBinding binding)
    {
        CyIdGateway.ValidateBinding(binding);
        if (binding.BaseUrl != settings.CloudBaseUrl || binding.WorkspaceId != settings.CloudWorkspaceId
            || binding.DeviceId != settings.CloudDeviceId
            || binding.DeviceTokenDigest != CyIdGateway.TokenDigest(CloudDeviceToken(settings)))
            throw new InvalidDataException("CYID 綁定與本機裝置不一致。");
        settings.CyIdBindingEncrypted = protector.Protect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(binding));
        settings.CloudIdentityProvider = "CYID";
        MarkCloudEmployeeAuthorityReady(settings);
    }

    public CyIdBinding CyIdConfiguration(Settings settings)
    {
        if (settings.CloudIdentityProvider != "CYID" || settings.CyIdBindingEncrypted.Length == 0
            || settings.CloudMode != CloudModes.CloudPreferred || !settings.CloudEmployeeAuthorityReady)
            throw new InvalidDataException("CYID 模式缺少已確認的綁定。");
        var binding = System.Text.Json.JsonSerializer.Deserialize<CyIdBinding>(protector.Unprotect(settings.CyIdBindingEncrypted))
            ?? throw new InvalidDataException("CYID 綁定無法讀取。");
        CyIdGateway.ValidateBinding(binding);
        if (binding.BaseUrl != settings.CloudBaseUrl || binding.WorkspaceId != settings.CloudWorkspaceId
            || binding.DeviceId != settings.CloudDeviceId
            || binding.DeviceTokenDigest != CyIdGateway.TokenDigest(CloudDeviceToken(settings)))
            throw new InvalidDataException("CYID 綁定與目前裝置不一致。");
        return binding;
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
        if (settings.CloudCompanyAppliedRevision < 0)
            throw new InvalidDataException("公司卡套用版次無效。");
        if (settings.CloudCompanyAppliedRevision == 0)
        {
            if (settings.CloudCompanyAppliedWorkspaceId.Length != 0 ||
                settings.CloudCompanyAppliedDeviceId.Length != 0 ||
                settings.CloudCompanyAppliedSellerInvoice.Length != 0)
                throw new InvalidDataException("公司卡套用紀錄不完整。");
        }
        else if (settings.CloudMode != CloudModes.CloudPreferred ||
            settings.CloudIdentityProvider != "CYID" ||
            settings.CloudCompanyAppliedWorkspaceId != settings.CloudWorkspaceId ||
            settings.CloudCompanyAppliedDeviceId != settings.CloudDeviceId ||
            settings.CloudCompanyAppliedSellerInvoice != settings.ProductionInvoice ||
            settings.ProductionAppKeyEncrypted.Length == 0 || settings.MoPasswordEncrypted.Length == 0)
            throw new InvalidDataException("公司卡套用來源已改變，必須重新授權。");
        if (settings.CloudEmployeeAuthorityReady && !HasCloudIdentity(settings))
            throw new InvalidDataException("中央帳號主資料狀態缺少 Cloud Device identity。");
        if (settings.CloudMode == CloudModes.CloudTransition
            && (!HasCloudIdentity(settings) || settings.CloudEmployeeAuthorityReady))
            throw new InvalidDataException("Cloud 帳號轉換狀態與 Device identity 不一致。");

        if (settings.CloudIdentityProvider is not "BUILT_IN" and not "CYID")
            throw new InvalidDataException("不支援的 Cloud identity provider。");
        if (settings.CloudIdentityProvider == "CYID" && (!settings.CloudEmployeeAuthorityReady || settings.CyIdBindingEncrypted.Length == 0))
            throw new InvalidDataException("CYID 模式缺少已確認的綁定。");
        ValidateCloudPendingBootstrap(settings);
        ValidateCloudPendingDeviceJoin(settings);
        ValidateCloudPendingRecovery(settings);
        var activePending = (settings.CloudPendingBootstrapTokenEncrypted.Length != 0 ? 1 : 0)
            + (settings.CloudPendingDeviceJoinTokenEncrypted.Length != 0 ? 1 : 0)
            + (settings.CloudPendingRecoveryTokenEncrypted.Length != 0 ? 1 : 0);
        if (activePending > 1)
            throw new InvalidDataException("同時只能有一個 Pending Cloud Device 流程。");
        if (settings.CloudPendingRecoveryTokenEncrypted.Length != 0 && HasCloudIdentity(settings))
            throw new InvalidDataException("已有正式 Cloud Device 時不能保留災難復原 Pending Token。");
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

    private static void ValidateCloudPendingRecovery(Settings settings)
    {
        var fields = new[]
        {
            settings.CloudPendingRecoveryUrl,
            settings.CloudPendingRecoveryWorkspaceId,
            settings.CloudPendingRecoveryIdentityWorkspaceId,
            settings.CloudPendingRecoveryInvitationId,
            settings.CloudPendingRecoveryDeviceName,
            settings.CloudPendingRecoveryStartedUtc,
            settings.CloudPendingRecoveryTokenEncrypted
        };
        var present = fields.Count(value => value.Length != 0);
        if (present == 0) return;
        if (present != fields.Length
            || !ValidCloudBaseUrl(settings.CloudPendingRecoveryUrl)
            || !ValidRecoveryIdentifier(settings.CloudPendingRecoveryWorkspaceId)
            || !ValidRecoveryIdentifier(settings.CloudPendingRecoveryIdentityWorkspaceId)
            || !ValidRecoveryInvitationId(settings.CloudPendingRecoveryInvitationId)
            || settings.CloudPendingRecoveryDeviceName.Trim().Length is < 1 or > 120
            || settings.CloudPendingRecoveryDeviceName.Any(char.IsControl)
            || !DateTimeOffset.TryParseExact(settings.CloudPendingRecoveryStartedUtc,
                "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            throw new InvalidDataException("Pending Recovery 資料不完整或目標範圍無效。");
    }

    private static bool ValidRecoveryIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100
        && value.All(character => char.IsLetterOrDigit(character)
            || character is '_' or '-' or '.');

    private static bool ValidRecoveryInvitationId(string value) =>
        value is not null && value.StartsWith("inv_", StringComparison.Ordinal)
        && Guid.TryParseExact(value.AsSpan(4), "D", out _);

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
