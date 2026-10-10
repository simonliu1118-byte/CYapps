using CYInvoice.Core.Cloud;

namespace CYInvoice.Core.Storage;

public enum CloudExitDisposition
{
    ReadyForRevoke,
    WorkspaceDisabled,
    DeviceAlreadyRevoked,
    LastActiveDevice,
}

public sealed record CloudExitAssessment(
    CloudExitDisposition Disposition,
    string DeviceId,
    string DeviceDisplayName,
    int ActiveDeviceCount,
    string WorkspaceStatus);

public enum LocalResetKind
{
    Local,
    BuiltInCloud,
}

public sealed record LocalResetExecutionRequest(
    LocalResetKind Kind,
    string EmployeeNo = "",
    string Password = "");

public enum LocalResetRecoveryDisposition
{
    None,
    Completed,
    AbortedSafely,
    Blocked,
}

public sealed record LocalResetRecoveryResult(
    LocalResetRecoveryDisposition Disposition,
    string Message)
{
    public static LocalResetRecoveryResult None { get; } =
        new(LocalResetRecoveryDisposition.None, string.Empty);
}

public sealed class LocalResetBlockedException(string message) : InvalidOperationException(message);
public sealed class LocalResetRecoveryRequiredException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public static class LocalResetCoordinator
{
    private const int MarkerVersion = 1;
    private const string MarkerFileName = ".cyinvoice-local-reset.pending.json";
    private const string KindLocal = "local";
    private const string KindBuiltInCloud = "built_in_cloud";
    private const string KindRevokedDevice = "revoked_device";
    public const string PackageManifestFileName = "package-files.json";
    private const string PhaseCloudExitPending = "cloud_exit_pending";
    private const string PhaseWipeAuthorized = "wipe_authorized";

    private sealed class ResetMarker
    {
        public int Version { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string WorkspaceId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string StartedAtUtc { get; set; } = string.Empty;
    }

    public static async Task<CloudExitAssessment> AssessBuiltInCloudAsync(
        LocalRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var settings = repository.Settings.LoadOrCreate();
        var identity = ReadCloudIdentity(repository, settings);

        using var httpClient = new HttpClient();
        var endpoint = new Uri(identity.BaseUrl, UriKind.Absolute);
        var selfClient = new CloudDeviceSelfStatusClient(httpClient, endpoint, identity.Token);
        var self = await selfClient.GetAsync(cancellationToken).ConfigureAwait(false);
        ValidateIdentity(self, identity.WorkspaceId, identity.DeviceId);

        if (self.WorkspaceStatus == "disabled")
        {
            return new CloudExitAssessment(
                CloudExitDisposition.WorkspaceDisabled,
                self.DeviceId,
                string.Empty,
                0,
                self.WorkspaceStatus);
        }

        if (self.Status == "revoked")
        {
            return new CloudExitAssessment(
                CloudExitDisposition.DeviceAlreadyRevoked,
                self.DeviceId,
                string.Empty,
                0,
                self.WorkspaceStatus);
        }

        var lifecycle = new CloudDeviceLifecycleClient(httpClient, endpoint, identity.Token);
        var devices = await lifecycle.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(devices.CurrentDeviceId, identity.DeviceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud Device inventory does not match the local Device identity.");

        var current = devices.Devices.SingleOrDefault(device => device.Current)
            ?? throw new InvalidDataException("Cloud Device inventory is missing the current Device.");
        var disposition = devices.ActiveDeviceCount <= 1
            ? CloudExitDisposition.LastActiveDevice
            : CloudExitDisposition.ReadyForRevoke;
        return new CloudExitAssessment(
            disposition,
            current.DeviceId,
            current.DisplayName,
            devices.ActiveDeviceCount,
            self.WorkspaceStatus);
    }

    public static async Task ExecuteAsync(
        string baseDirectory,
        ISecretProtector protector,
        LocalResetExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(request);
        baseDirectory = Path.GetFullPath(baseDirectory);

        if (request.Kind == LocalResetKind.Local)
        {
            var marker = NewMarker(KindLocal, PhaseWipeAuthorized);
            SaveMarker(baseDirectory, marker);
            WipeLocalState(baseDirectory);
            return;
        }

        var repository = LocalRepository.Open(baseDirectory, protector);
        var settings = repository.Settings.LoadOrCreate();
        var identity = ReadCloudIdentity(repository, settings);
        var cloudMarker = NewMarker(
            KindBuiltInCloud,
            PhaseCloudExitPending,
            identity.BaseUrl,
            identity.WorkspaceId,
            identity.DeviceId);
        SaveMarker(baseDirectory, cloudMarker);

        using var httpClient = new HttpClient();
        var endpoint = new Uri(identity.BaseUrl, UriKind.Absolute);
        var selfClient = new CloudDeviceSelfStatusClient(httpClient, endpoint, identity.Token);
        var self = await selfClient.GetAsync(cancellationToken).ConfigureAwait(false);
        ValidateIdentity(self, identity.WorkspaceId, identity.DeviceId);

        if (CanWipe(self))
        {
            AuthorizeAndWipe(baseDirectory, cloudMarker);
            return;
        }

        if (request.EmployeeNo.Length == 0 || request.Password.Length == 0)
        {
            DeleteMarker(baseDirectory);
            throw new LocalResetBlockedException("目前 Device 仍在使用中，必須由超級管理員重新驗證後才能退出 Built-in Cloud。");
        }

        var lifecycle = new CloudDeviceLifecycleClient(httpClient, endpoint, identity.Token);
        try
        {
            var result = await lifecycle.RevokeAsync(
                    identity.DeviceId,
                    request.EmployeeNo,
                    request.Password,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(result.Device.DeviceId, identity.DeviceId, StringComparison.Ordinal)
                || result.Device.Status != "revoked")
                throw new InvalidDataException("Cloud did not confirm revocation of the current Device.");

            AuthorizeAndWipe(baseDirectory, cloudMarker);
        }
        catch (CloudApiException error) when (error.Code == "LAST_ACTIVE_DEVICE")
        {
            DeleteMarker(baseDirectory);
            throw new LocalResetBlockedException(
                "這是 Built-in Cloud Workspace 最後一台使用中的裝置。若要保留 Workspace，請先加入另一台 Device；若不再使用 Workspace，請先在中央管理端手動停用 Workspace，再回來重試。");
        }
        catch (CloudApiException error) when (error.Code is "SUPER_ADMIN_AUTH_FAILED" or "INVALID_EMPLOYEE_CREDENTIALS")
        {
            DeleteMarker(baseDirectory);
            throw;
        }
        catch (Exception error) when (error is not LocalResetBlockedException)
        {
            // Once a revoke request may have reached Cloud, its result is intentionally treated as
            // ambiguous. Keep the marker and let a later startup verify the token's terminal state.
            throw new LocalResetRecoveryRequiredException(
                "雲端撤銷結果目前無法安全確認。本機資料與 Device Token 均已保留；下次啟動時會先向 Cloud 確認狀態。",
                error);
        }
    }

    public static async Task<LocalResetRecoveryResult> RecoverPendingAsync(
        string baseDirectory,
        ISecretProtector protector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(protector);
        baseDirectory = Path.GetFullPath(baseDirectory);
        var marker = LoadMarker(baseDirectory);
        if (marker is null) return LocalResetRecoveryResult.None;

        ValidateMarker(marker);
        if (marker.Phase == PhaseWipeAuthorized)
        {
            WipeLocalState(baseDirectory);
            return new LocalResetRecoveryResult(
                LocalResetRecoveryDisposition.Completed,
                "先前已確認的本機重設已完成。");
        }

        if (marker.Kind != KindBuiltInCloud || marker.Phase != PhaseCloudExitPending)
        {
            return new LocalResetRecoveryResult(
                LocalResetRecoveryDisposition.Blocked,
                "偵測到無法辨識的 CYInvoice 重設狀態。為避免誤刪資料，程式不會繼續啟動。");
        }

        try
        {
            var repository = LocalRepository.Open(baseDirectory, protector);
            var settings = repository.Settings.LoadOrCreate();
            var identity = ReadCloudIdentity(repository, settings);
            ValidateMarkerIdentity(marker, identity.BaseUrl, identity.WorkspaceId, identity.DeviceId);

            using var httpClient = new HttpClient();
            var selfClient = new CloudDeviceSelfStatusClient(
                httpClient,
                new Uri(identity.BaseUrl, UriKind.Absolute),
                identity.Token);
            var self = await selfClient.GetAsync(cancellationToken).ConfigureAwait(false);
            ValidateIdentity(self, identity.WorkspaceId, identity.DeviceId);

            if (CanWipe(self))
            {
                marker.Phase = PhaseWipeAuthorized;
                SaveMarker(baseDirectory, marker);
                WipeLocalState(baseDirectory);
                return new LocalResetRecoveryResult(
                    LocalResetRecoveryDisposition.Completed,
                    "雲端已確認 Device 撤銷或 Workspace 停用；本機重設已安全完成。");
            }

            DeleteMarker(baseDirectory);
            return new LocalResetRecoveryResult(
                LocalResetRecoveryDisposition.AbortedSafely,
                "Cloud 仍確認目前 Device 與 Workspace 都在使用中；先前未完成的重設已取消，本機資料完整保留。");
        }
        catch (Exception error)
        {
            return new LocalResetRecoveryResult(
                LocalResetRecoveryDisposition.Blocked,
                "先前的雲端退出仍無法安全確認。CYInvoice 不會刪除本機資料，也不會在狀態不明時繼續啟動。\n\n" + error.Message);
        }
    }

    public static bool HasPendingReset(string baseDirectory) =>
        File.Exists(MarkerPath(Path.GetFullPath(baseDirectory)));

    public static bool IsRevokedDeviceResetPending(string baseDirectory)
    {
        var marker = LoadMarker(Path.GetFullPath(baseDirectory));
        if (marker is null) return false;
        ValidateMarker(marker);
        return marker.Kind == KindRevokedDevice;
    }

    // This is called only with an authenticated self-status for the locally bound Device.
    // It authorizes recovery before any UI shutdown, without persisting credentials.
    public static void PrepareRevokedDeviceReset(string baseDirectory, Settings current, CloudDeviceSelfStatus self)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(self);
        baseDirectory = Path.GetFullPath(baseDirectory);
        ValidateIdentity(self, current.CloudWorkspaceId, current.CloudDeviceId);
        if (current.CloudMode == CloudModes.LocalOnly || self.Status != "revoked")
            throw new LocalResetBlockedException("只有明確撤銷目前雲端裝置才能自動清除。");
        var existing = LoadMarker(baseDirectory);
        if (existing is not null)
        {
            ValidateMarker(existing);
            if (existing.Kind == KindRevokedDevice)
            {
                ValidateMarkerIdentity(existing, current.CloudBaseUrl, self.WorkspaceId, self.DeviceId);
                return;
            }
            throw new LocalResetBlockedException("已有本機重設尚未完成，不能覆寫清除標記。");
        }
        SaveMarker(baseDirectory, NewMarker(KindRevokedDevice, PhaseWipeAuthorized,
            current.CloudBaseUrl, self.WorkspaceId, self.DeviceId));
    }

    private static (string BaseUrl, string WorkspaceId, string DeviceId, string Token) ReadCloudIdentity(
        LocalRepository repository,
        Settings settings)
    {
        var baseUrl = settings.CloudBaseUrl.Trim();
        var workspaceId = settings.CloudWorkspaceId.Trim().ToLowerInvariant();
        var deviceId = settings.CloudDeviceId.Trim().ToLowerInvariant();
        if (baseUrl.Length == 0 || workspaceId.Length == 0 || deviceId.Length == 0
            || settings.CloudDeviceTokenEncrypted.Length == 0)
            throw new LocalResetBlockedException("這台電腦的 Cloud identity 不完整，為避免遺失復原能力，不能執行破壞性重設。");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new LocalResetBlockedException("Cloud API 網址無效，不能安全確認退出狀態。");
        var token = repository.Settings.CloudDeviceToken(settings);
        if (token.Length == 0)
            throw new LocalResetBlockedException("目前無法讀取 Cloud Device Token，不能安全確認退出狀態。");
        return (NormalizeBaseUrl(endpoint), workspaceId, deviceId, token);
    }

    private static void ValidateIdentity(CloudDeviceSelfStatus self, string workspaceId, string deviceId)
    {
        if (!string.Equals(self.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || !string.Equals(self.DeviceId, deviceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud self-status identity does not match the local Workspace/Device identity.");
    }

    private static bool CanWipe(CloudDeviceSelfStatus self) =>
        self.WorkspaceStatus == "disabled" || self.Status == "revoked";

    private static ResetMarker NewMarker(
        string kind,
        string phase,
        string baseUrl = "",
        string workspaceId = "",
        string deviceId = "") => new()
    {
        Version = MarkerVersion,
        Kind = kind,
        Phase = phase,
        BaseUrl = baseUrl,
        WorkspaceId = workspaceId,
        DeviceId = deviceId,
        StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
    };

    private static void AuthorizeAndWipe(string baseDirectory, ResetMarker marker)
    {
        marker.Phase = PhaseWipeAuthorized;
        SaveMarker(baseDirectory, marker);
        WipeLocalState(baseDirectory);
    }

    private static void WipeLocalState(string baseDirectory)
    {
        if (LoadMarker(baseDirectory)?.Kind == KindRevokedDevice)
        {
            WipePortableRuntime(baseDirectory);
            return;
        }
        DeleteDirectoryIfExists(Path.Combine(baseDirectory, "Data"));
        DeleteDirectoryIfExists(Path.Combine(baseDirectory, "Cache"));
        DeleteMarker(baseDirectory);
    }

    private static void WipePortableRuntime(string baseDirectory)
    {
        // The package records original files at build time. Runtime directories must
        // never be in this manifest; a missing/invalid manifest blocks destructive work.
        if (!JsonFile.TryRead<string[]>(Path.Combine(baseDirectory, PackageManifestFileName), out var files)
            || files is null || files.Length is < 1 or > 4096)
            throw new LocalResetBlockedException("發行檔案清單缺失，無法安全完成裝置清除。");
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PackageManifestFileName, MarkerFileName };
        foreach (var raw in files)
        {
            var relative = raw.Replace('\\', '/');
            if (relative.Length == 0 || Path.IsPathRooted(relative)
                || relative.Split('/').Any(p => p is "" or "." or ".." || p.Contains(':'))
                || new[] { "Data", "Cache", "Logs" }.Any(d => relative.Equals(d, StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("發行檔案清單包含無效路徑。");
            keep.Add(relative);
        }
        if (!keep.Contains("CYInvoice.exe")) throw new InvalidDataException("發行檔案清單未包含程式。");
        CleanDirectory(baseDirectory, "", keep);
        // Keep this marker until every runtime path was removed successfully.
        DeleteMarker(baseDirectory);
    }

    private static void CleanDirectory(string directory, string prefix, HashSet<string> keep)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).ToArray())
        {
            var relative = prefix + Path.GetFileName(path);
            var attributes = File.GetAttributes(path);
            if (keep.Contains(relative))
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("發行檔案不得是連結。");
                continue;
            }
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory && (attributes & FileAttributes.ReparsePoint) == 0
                && keep.Any(p => p.StartsWith(relative + "/", StringComparison.OrdinalIgnoreCase)))
                CleanDirectory(path, relative + "/", keep);
            else if (isDirectory)
                Directory.Delete(path, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
            else
                File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path)) return;
        Directory.Delete(path, recursive: true);
    }

    private static ResetMarker? LoadMarker(string baseDirectory)
    {
        var path = MarkerPath(baseDirectory);
        if (!File.Exists(path)) return null;
        if (!JsonFile.TryRead<ResetMarker>(path, out var marker))
            throw new InvalidDataException("CYInvoice reset marker cannot be read safely.");
        return marker ?? throw new InvalidDataException("CYInvoice reset marker is empty.");
    }

    private static void SaveMarker(string baseDirectory, ResetMarker marker) =>
        JsonFile.Write(MarkerPath(baseDirectory), marker);

    private static void DeleteMarker(string baseDirectory)
    {
        var path = MarkerPath(baseDirectory);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string MarkerPath(string baseDirectory) =>
        Path.Combine(baseDirectory, MarkerFileName);

    private static void ValidateMarker(ResetMarker marker)
    {
        if (marker.Version != MarkerVersion
            || marker.Kind is not (KindLocal or KindBuiltInCloud or KindRevokedDevice)
            || marker.Phase is not (PhaseCloudExitPending or PhaseWipeAuthorized)
            || marker.Kind == KindRevokedDevice && marker.Phase != PhaseWipeAuthorized
            || !DateTimeOffset.TryParse(marker.StartedAtUtc, out _))
            throw new InvalidDataException("CYInvoice reset marker is invalid.");
    }

    private static void ValidateMarkerIdentity(
        ResetMarker marker,
        string baseUrl,
        string workspaceId,
        string deviceId)
    {
        if (!SameEndpoint(marker.BaseUrl, baseUrl)
            || !string.Equals(marker.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || !string.Equals(marker.DeviceId, deviceId, StringComparison.Ordinal))
            throw new InvalidDataException("Pending reset identity no longer matches the local Cloud identity.");
    }

    private static bool SameEndpoint(string left, string right) =>
        Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
        && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
        && string.Equals(NormalizeBaseUrl(leftUri), NormalizeBaseUrl(rightUri), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeBaseUrl(Uri endpoint)
    {
        var value = endpoint.AbsoluteUri;
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }
}
