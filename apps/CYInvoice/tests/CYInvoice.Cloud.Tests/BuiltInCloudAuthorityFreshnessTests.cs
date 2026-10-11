using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class BuiltInCloudAuthorityFreshnessTests
{
    public static async Task RunAsync()
    {
        await CurrentAuthorityOverridesStaleCacheAsync();
        await TemporaryServiceFailureUsesExistingFallbackAsync();
        await ReconnectReplacesOfflineAuthorityAsync();
        await ConfiguredSourceRejectsWorkspaceMismatchAsync();
    }

    private static async Task CurrentAuthorityOverridesStaleCacheAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var cache = new CloudEmployeeCacheStore(temporary.Path, new TestProtector());
        cache.ReplaceSnapshot(
            "ws_freshness",
            1,
            new[]
            {
                Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1),
                Seed("emp_user", "0002", "Stale User", EmployeeRoles.User, "OldPass22", 1, 1),
            });

        var current = Snapshot(
            "ws_freshness",
            8,
            Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 2),
            Seed("emp_user", "0002", "Current Admin", EmployeeRoles.Admin, "NewPass22", 2, 6),
            Seed("emp_new", "0003", "New User", EmployeeRoles.User, "NewUser33", 1, 3),
            Seed("emp_disabled", "0004", "Disabled", EmployeeRoles.User, "Disabled44", 1, 4, enabled: false));
        var source = new ScriptedAuthoritySource("ws_freshness", _ => Task.FromResult(current));
        var provider = new BuiltInCloudIdentityProvider(cache, source);

        True(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "OldPass22")) is null,
            "stale password must stop working as soon as current Cloud authority is reachable");
        var promoted = await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "NewPass22"));
        NotNull(promoted, "current Cloud password must authenticate without restart or background sync");
        Equal(AppRole.Admin, promoted!.Role, "current role must replace stale cached role");
        Equal(6, promoted.AuthorityRevision, "current Employee revision must be exposed");

        var added = await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0003", "NewUser33"));
        NotNull(added, "new central Employee must be usable on the next protected operation");
        Equal("emp_new", added!.StableEmployeeId, "new central Employee stable ID");

        True(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0004", "Disabled44")) is null,
            "centrally disabled Employee must be rejected on the next protected operation");
        var disabled = await provider.RefreshPrincipalAsync("0004");
        NotNull(disabled, "disabled current authority metadata should remain inspectable");
        True(!disabled!.Enabled, "current disabled state must replace stale assumptions");

        var state = cache.LoadState() ?? throw new InvalidOperationException("refreshed cache state is missing");
        Equal(8, state.WorkspaceRevision, "successful online refresh must persist the current Workspace revision");
    }

    private static async Task TemporaryServiceFailureUsesExistingFallbackAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var cache = new CloudEmployeeCacheStore(temporary.Path, new TestProtector());
        cache.ReplaceSnapshot(
            "ws_offline",
            3,
            new[]
            {
                Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1),
                Seed("emp_user", "0002", "Offline User", EmployeeRoles.User, "Offline22", 1, 2),
            });

        var source = new ScriptedAuthoritySource(
            "ws_offline",
            _ => throw new HttpRequestException("network unavailable"));
        var provider = new BuiltInCloudIdentityProvider(cache, source);
        NotNull(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Offline22")),
            "genuine transport failure may use the last trusted protected cache");

        source.Handler = _ => throw new OperationCanceledException("request timeout");
        NotNull(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Offline22")),
            "Cloud request timeout may use the last trusted protected cache");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await ThrowsAsync<OperationCanceledException>(
                () => provider.AuthenticateAsync(
                    new IdentityAuthenticationRequest("0002", "Offline22"), cancelled.Token),
                "caller cancellation must propagate rather than being treated as Offline");
        }

        source.Handler = _ => throw new CloudApiException(
            "STORAGE_UNAVAILABLE", "storage unavailable", HttpStatusCode.ServiceUnavailable);
        NotNull(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Offline22")),
            "503 temporary outage must reuse the original protected fallback");

        source.Handler = _ => throw new InvalidDataException("malformed authority snapshot");
        await ThrowsAsync<InvalidDataException>(
            () => provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Offline22")),
            "malformed Cloud authority data must fail closed");

        source.CurrentWorkspaceId = "ws_other";
        source.Handler = _ => throw new HttpRequestException("offline after Workspace changed");
        await ThrowsAsync<InvalidOperationException>(
            () => provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Offline22")),
            "Offline cache from another Workspace must never authenticate current Workspace operations");
        source.CurrentWorkspaceId = "ws_offline";
        source.Handler = _ => throw new CloudApiException("UNAUTHORIZED", "device rejected", HttpStatusCode.Unauthorized);
        await ThrowsAsync<CloudApiException>(() => provider.AuthenticateAsync(new("0002", "Offline22")),
            "known Device rejection must never fall back");
        source.Handler = _ => throw new HttpRequestException("later outage");
        await ThrowsAsync<InvalidOperationException>(() => provider.AuthenticateAsync(new("0002", "Offline22")),
            "a later outage cannot resurrect credentials after known Device rejection");

    }

    private static async Task ReconnectReplacesOfflineAuthorityAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var cache = new CloudEmployeeCacheStore(temporary.Path, new TestProtector());
        cache.ReplaceSnapshot(
            "ws_reconnect",
            2,
            new[]
            {
                Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1),
                Seed("emp_user", "0002", "Before", EmployeeRoles.User, "Before222", 1, 2),
            });

        var source = new ScriptedAuthoritySource("ws_reconnect", _ => throw new HttpRequestException("offline"));
        var provider = new BuiltInCloudIdentityProvider(cache, source);
        var offline = await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Before222"));
        NotNull(offline, "trusted cache must remain usable during a true outage");
        Equal(AppRole.User, offline!.Role, "offline operation uses the last trusted role");

        source.Handler = _ => Task.FromResult(Snapshot(
            "ws_reconnect",
            9,
            Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 2),
            Seed("emp_user", "0002", "After", EmployeeRoles.Admin, "After2222", 2, 9)));

        True(await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "Before222")) is null,
            "reconnect must immediately retire stale cached credentials");
        var current = await provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "After2222"));
        NotNull(current, "reconnect must restore current Cloud authority without restart");
        Equal(AppRole.Admin, current!.Role, "reconnect must restore the current Cloud role");
        Equal(9, current.AuthorityRevision, "reconnect must restore current Employee revision");
    }

    private static async Task ConfiguredSourceRejectsWorkspaceMismatchAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var protector = new TestProtector();
        var settingsStore = new SettingsStore(temporary.Path, protector);
        var settings = settingsStore.LoadOrCreate();
        settings.CloudMode = CloudModes.CloudPreferred;
        settings.CloudEmployeeAuthorityReady = true;
        settings.CloudBaseUrl = "https://cloud.example.test/";
        settings.CloudWorkspaceId = "ws_expected";
        settings.CloudDeviceId = "dev_expected";
        settingsStore.SetCloudDeviceToken(settings,
            "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        settingsStore.Save(settings);

        var verifier = Verifier("Current22");
        var json = JsonSerializer.Serialize(new
        {
            employeeSnapshot = new
            {
                workspaceId = "ws_other",
                workspaceRevision = 5,
                employees = new[]
                {
                    new
                    {
                        employeeId = "emp_super",
                        employeeNo = "0001",
                        name = "Super",
                        email = "super@example.test",
                        role = EmployeeRoles.SuperAdmin,
                        enabled = true,
                        emailVerified = true,
                        credentialVerifier = verifier,
                        credentialAlgorithm = "pbkdf2-sha256",
                        credentialVersion = 1,
                        revision = 1,
                    }
                }
            }
        });
        var handler = new QueueHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var source = new ConfiguredCloudEmployeeAuthoritySnapshotSource(settingsStore, http);

        await ThrowsAsync<InvalidDataException>(
            () => source.GetCurrentSnapshotAsync(),
            "authority snapshot from another Workspace must be rejected");
    }

    internal static CloudEmployeeAuthoritySnapshot Snapshot(
        string workspaceId,
        int workspaceRevision,
        params CloudEmployeeCacheSeed[] employees) =>
        new(workspaceId, workspaceRevision, employees);

    internal static CloudEmployeeCacheSeed Seed(
        string employeeId,
        string employeeNo,
        string name,
        string role,
        string password,
        int credentialVersion,
        int revision,
        bool enabled = true) =>
        new(
            employeeId,
            employeeNo,
            name,
            $"{employeeNo}@example.test",
            role,
            enabled,
            EmailVerified: true,
            Verifier(password),
            credentialVersion,
            revision);

    private static string Verifier(string password)
    {
        var salt = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        const int iterations = 100_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return string.Join(
            '$',
            "pbkdf2-sha256",
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(salt).ToLowerInvariant(),
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string description) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(description);
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
    }

    private static void True(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void NotNull(object? value, string description)
    {
        if (value is null) throw new InvalidOperationException(description);
    }
}

internal sealed class ScriptedAuthoritySource(
    string currentWorkspaceId,
    Func<CancellationToken, Task<CloudEmployeeAuthoritySnapshot>> handler)
    : ICloudEmployeeAuthoritySnapshotSource
{
    public string CurrentWorkspaceId { get; set; } = currentWorkspaceId;
    public Func<CancellationToken, Task<CloudEmployeeAuthoritySnapshot>> Handler { get; set; } = handler;

    public Task<CloudEmployeeAuthoritySnapshot> GetCurrentSnapshotAsync(
        CancellationToken cancellationToken = default) => Handler(cancellationToken);
}

internal sealed class AuthorityTemporaryDirectory : IDisposable
{
    public AuthorityTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CYInvoice.CloudAuthorityFreshness.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}
