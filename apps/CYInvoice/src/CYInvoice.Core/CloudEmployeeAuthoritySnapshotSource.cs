using CYInvoice.Core.Cloud;

namespace CYInvoice.Core.Storage;

public interface ICloudEmployeeAuthoritySnapshotSource
{
    string CurrentWorkspaceId { get; }

    Task<CloudEmployeeAuthoritySnapshot> GetCurrentSnapshotAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the current Built-in Cloud Employee authority from the active
/// CYInvoice Cloud settings. The Device token authenticates the current Device;
/// Employee credentials remain inside the protected authority snapshot/cache
/// path and are never stored as plaintext.
/// </summary>
public sealed class ConfiguredCloudEmployeeAuthoritySnapshotSource : ICloudEmployeeAuthoritySnapshotSource
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly SettingsStore settings;
    private readonly HttpClient httpClient;

    public ConfiguredCloudEmployeeAuthoritySnapshotSource(SettingsStore settings)
        : this(settings, SharedHttpClient)
    {
    }

    public ConfiguredCloudEmployeeAuthoritySnapshotSource(SettingsStore settings, HttpClient httpClient)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string CurrentWorkspaceId
    {
        get
        {
            var current = settings.LoadOrCreate();
            var workspaceId = current.CloudWorkspaceId.Trim();
            if (workspaceId.Length == 0)
                throw new InvalidOperationException("Cloud Workspace identity 不完整。");
            return workspaceId;
        }
    }

    public async Task<CloudEmployeeAuthoritySnapshot> GetCurrentSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = settings.LoadOrCreate();
        if (current.CloudMode != CloudModes.CloudPreferred || !current.CloudEmployeeAuthorityReady)
            throw new InvalidOperationException("Built-in Cloud Employee authority 尚未切換完成。");

        var workspaceId = current.CloudWorkspaceId.Trim();
        if (workspaceId.Length == 0)
            throw new InvalidOperationException("Cloud Workspace identity 不完整。");

        var deviceToken = settings.CloudDeviceToken(current);
        if (deviceToken.Length == 0)
            throw new InvalidOperationException("Cloud Device Token 不存在。");

        var client = new CloudEmployeeAuthorityClient(
            httpClient,
            new Uri(current.CloudBaseUrl, UriKind.Absolute),
            deviceToken);
        var snapshot = await client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(snapshot.WorkspaceId, workspaceId, StringComparison.Ordinal))
            throw new InvalidDataException("Cloud Employee authority snapshot 與目前 Workspace 不一致。");
        return snapshot;
    }
}
