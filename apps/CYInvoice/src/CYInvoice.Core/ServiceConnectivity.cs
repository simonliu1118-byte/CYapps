using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
using System.Net.Http.Json;
using System.Text.Json;

namespace CYInvoice.Core.Storage;

public sealed record ServiceConnectionState(bool AmegoAvailable, bool CloudAvailable, bool CloudRequired,
    string AmegoProblem = "", string CloudProblem = "", bool CloudRejected = false)
{
    public bool BlockAll => CloudRejected || (!AmegoAvailable && (!CloudRequired || !CloudAvailable));
    public bool UsesExistingFallback => CloudRequired && AmegoAvailable && !CloudAvailable && !CloudRejected;
}

public sealed class ServiceConnectionException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

// Service reachability is not Employee authorization. Providers retain their
// original action-time authority checks and their existing protected fallback.
public sealed class ServiceConnectivity(SettingsStore settings, HttpClient? http = null, Action? forgetRejectedAuthority = null,
    CyIdOfflineCache? cyIdOffline = null, CloudEmployeeCacheStore? builtInCache = null, string? baseDirectory = null,
    Func<long>? monotonicMilliseconds = null)
{
    private long Now => (monotonicMilliseconds ?? (() => Environment.TickCount64))();
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private long nextAuthorityRefresh;
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly HttpClient client = http ?? SharedHttp;
    public ServiceConnectionState? Current { get; private set; }

    public async Task RequireAmegoAsync(CancellationToken cancellationToken = default)
    {
        if (Current is { CloudRejected: true })
            throw new ServiceConnectionException("雲端已拒絕目前裝置或綁定，不能使用單機備援。請確認授權後重新檢查。", null);
        try { await new AmegoConnectivityProbe(client).CheckAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            throw new ServiceConnectionException("目前無法連線光貿，已停止開票、查詢及光貿相關操作。請恢復連線後重試。", error);
        }
    }

    public async Task<ServiceConnectionState> CheckAsync(CancellationToken cancellationToken = default)
    {
        await checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await CheckCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { checkGate.Release(); }
    }

    private async Task<ServiceConnectionState> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var current = settings.LoadOrCreate();
        var cloudRequired = current.CloudMode != CloudModes.LocalOnly;
        var ame = CheckServiceAsync(() => new AmegoConnectivityProbe(client).CheckAsync(cancellationToken), cancellationToken);
        var cloud = cloudRequired
            ? CheckServiceAsync(() => CheckCloudAsync(current, cancellationToken), cancellationToken)
            : Task.FromResult((Problem: "", Rejected: false));
        await Task.WhenAll(ame, cloud).ConfigureAwait(false);
        // A known denial cannot turn into a fallback grant on the next outage.
        // Only a successful check of the same Device/binding clears this state.
        var rejected = cloudRequired && (cloud.Result.Rejected
            || Current is { CloudRejected: true } && cloud.Result.Problem.Length != 0);
        if (cloud.Result.Rejected) forgetRejectedAuthority?.Invoke();
        if (cloud.Result.Problem.Length != 0) nextAuthorityRefresh = default;
        return Current = new(ame.Result.Problem.Length == 0, cloudRequired && cloud.Result.Problem.Length == 0,
            cloudRequired, ame.Result.Problem, cloud.Result.Problem, rejected);
    }

    private async Task CheckCloudAsync(Settings current, CancellationToken cancellationToken)
    {
        var synchronize = current.CloudMode == CloudModes.CloudPreferred && current.CloudEmployeeAuthorityReady
            && Now >= nextAuthorityRefresh;
        if (synchronize && cyIdOffline is not null) await cyIdOffline.AuthorityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var binding = current.CloudIdentityProvider == "CYID" ? settings.CyIdConfiguration(current) : null;
            var authorities = synchronize && binding is not null && cyIdOffline is not null ? cyIdOffline.AuthorityChecks(binding) : [];
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(current.CloudBaseUrl), "v1/runtime/sync"));
            request.Headers.Authorization = new("Bearer", settings.CloudDeviceToken(current));
            request.Content = JsonContent.Create(new { synchronize, authorities });
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new CloudApiException("RUNTIME_SYNC_UNSUPPORTED",
                    "雲端版本尚未提供裝置／權限合併同步。請先更新 Cloud，再測試新同步功能。", response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new CloudApiException("RUNTIME_SYNC_FAILED", "無法確認雲端狀態。", response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var latest = settings.LoadOrCreate();
            if (latest.CloudWorkspaceId != current.CloudWorkspaceId || latest.CloudDeviceId != current.CloudDeviceId
                || latest.CloudMode != current.CloudMode || latest.CloudIdentityProvider != current.CloudIdentityProvider
                || latest.CloudBaseUrl != current.CloudBaseUrl || settings.CloudDeviceToken(latest) != settings.CloudDeviceToken(current))
                throw new InvalidDataException("同步期間裝置／Workspace 綁定已變更，請重新確認。");
            var root = json.RootElement;
            if (!root.GetProperty("ok").GetBoolean()) throw new InvalidDataException("同步回應無效。");
            var d = root.GetProperty("device");
            var self = CloudDeviceSelfStatusClient.ReadStatus(d);
            if (self.WorkspaceId != current.CloudWorkspaceId || self.DeviceId != current.CloudDeviceId)
                throw new InvalidDataException("同步裝置／Workspace 不一致。");
            if (self.Status == "revoked")
            {
                if (baseDirectory is not null) LocalResetCoordinator.PrepareRevokedDeviceReset(baseDirectory, current, self);
                throw new CloudApiException("DEVICE_REVOKED", "目前裝置已撤銷。", System.Net.HttpStatusCode.Forbidden);
            }
            if (self.Status != "active" || self.WorkspaceStatus != "active")
                throw new InvalidDataException("目前裝置或 Workspace 已停用。");
            if (root.TryGetProperty("permissions", out var permissions) && !permissions.GetProperty("ok").GetBoolean())
            {
                var code = permissions.GetProperty("code").GetString();
                if (code == "IDENTITY_UNAVAILABLE")
                {
                    nextAuthorityRefresh = default;
                    throw new CloudApiException(code, "雲端驗證暫時無法連線。", System.Net.HttpStatusCode.ServiceUnavailable);
                }
                throw new InvalidDataException("中央權限已拒絕：" + code);
            }
            var remote = root.GetProperty("binding");
            if (binding is null && remote.GetProperty("provider").GetString() == "CYID")
            {
                var discovered = new CyIdBinding(new Uri(current.CloudBaseUrl).AbsoluteUri, current.CloudWorkspaceId,
                    current.CloudDeviceId, remote.GetProperty("identityWorkspaceId").GetString()!,
                    remote.GetProperty("applicationId").GetString()!, remote.GetProperty("consumerVersion").GetString()!,
                    CyIdGateway.TokenDigest(settings.CloudDeviceToken(current)));
                settings.ConfirmCyIdConfiguration(current, discovered);
                settings.Save(current);
                builtInCache?.Clear();
                binding = discovered;
            }
            if (binding is not null && (remote.GetProperty("provider").GetString() != "CYID"
                || remote.GetProperty("identityWorkspaceId").GetString() != binding.IdentityWorkspaceId
                || remote.GetProperty("applicationId").GetString() != binding.ApplicationId
                || remote.GetProperty("workspaceId").GetString() != binding.WorkspaceId))
                throw new InvalidDataException("CYID 綁定不一致。");
            if (binding is null && (remote.GetProperty("provider").GetString() != "BUILT_IN"
                || remote.GetProperty("workspaceId").GetString() != current.CloudWorkspaceId))
                throw new InvalidDataException("雲端綁定不一致。");
            if (binding is not null)
            {
                var updated = binding with { ConsumerVersion = remote.GetProperty("consumerVersion").GetString()! };
                CyIdGateway.ValidateBinding(updated);
                if (updated != binding)
                {
                    settings.ConfirmCyIdConfiguration(current, updated);
                    settings.Save(current);
                    binding = updated;
                }
            }
            if (synchronize)
            {
                if (!root.TryGetProperty("permissions", out permissions)) throw new InvalidDataException("權限同步結果缺失。");
                if (binding is not null && cyIdOffline is not null)
                    cyIdOffline.InvalidateEmployees(permissions.GetProperty("invalidated").EnumerateArray()
                        .Select(id => id.GetString() ?? throw new InvalidDataException("權限失效清單無效。")).ToHashSet(StringComparer.Ordinal));
                else if (remote.GetProperty("provider").GetString() == "BUILT_IN" && builtInCache is not null)
                {
                    var snapshot = CloudEmployeeAuthorityClient.ReadSnapshot(permissions);
                    if (snapshot.WorkspaceId != current.CloudWorkspaceId) throw new InvalidDataException("權限 Workspace 不一致。");
                    builtInCache.ReplaceSnapshot(snapshot.WorkspaceId, snapshot.WorkspaceRevision, snapshot.Employees);
                }
                nextAuthorityRefresh = Now + 60_000;
            }
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException
            && error is not CloudApiException && error is not CyIdAuthenticationException)
        {
            throw new InvalidDataException("雲端同步回應或快取格式無效。", error);
        }
        finally { if (synchronize && cyIdOffline is not null) cyIdOffline.AuthorityGate.Release(); }
    }

    private static async Task<(string Problem, bool Rejected)> CheckServiceAsync(Func<Task> check, CancellationToken cancellationToken)
    {
        try { await check().ConfigureAwait(false); return ("", false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { return (error.Message, error is InvalidDataException or System.Text.Json.JsonException or UriFormatException
            || error is CloudApiException cloud && (cloud.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                || cloud.Code is "DEVICE_REVOKED" or "DEVICE_INVALID" or "ACCESS_DENIED" or "UNAUTHORIZED")
            || error is CyIdAuthenticationException identity && identity.Code is "DEVICE_INVALID" or "IDENTITY_WORKSPACE_MISMATCH" or "IDENTITY_PROVIDER_MISMATCH" or "ACCESS_DENIED"); }
    }
}
