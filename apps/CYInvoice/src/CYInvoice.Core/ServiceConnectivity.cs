using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;

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
public sealed class ServiceConnectivity(SettingsStore settings, HttpClient? http = null, Action? forgetRejectedAuthority = null)
{
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
        return Current = new(ame.Result.Problem.Length == 0, cloudRequired && cloud.Result.Problem.Length == 0,
            cloudRequired, ame.Result.Problem, cloud.Result.Problem, rejected);
    }

    private async Task CheckCloudAsync(Settings current, CancellationToken cancellationToken)
    {
        var cloud = new CloudClient(client, new Uri(current.CloudBaseUrl), settings.CloudDeviceToken(current));
        var device = await cloud.GetCurrentDeviceAsync(cancellationToken).ConfigureAwait(false);
        if (device.WorkspaceId != current.CloudWorkspaceId || device.DeviceId != current.CloudDeviceId)
            throw new InvalidDataException("雲端裝置／Workspace 回應不一致。");
        var binding = await CyIdGateway.DiscoverAsync(client, new Uri(current.CloudBaseUrl),
            device.DeviceToken, current.CloudWorkspaceId, current.CloudDeviceId, cancellationToken).ConfigureAwait(false);
        if (current.CloudIdentityProvider == "CYID" && binding is null)
            throw new InvalidDataException("伺服器身分來源與已確認 CYID 不一致。");
        if (binding is not null && current.CloudIdentityProvider == "CYID"
            && binding != settings.CyIdConfiguration(current))
            throw new InvalidDataException("CYID 綁定與原裝置不一致。");
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
