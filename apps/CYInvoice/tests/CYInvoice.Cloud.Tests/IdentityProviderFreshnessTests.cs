using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class IdentityProviderFreshnessTests
{
    public static async Task RunAsync()
    {
        await OnlineSnapshotMakesNewEmployeeImmediatelyUsableAsync();
        await CurrentRolePasswordAndEnabledStateWinAsync();
        await ExplicitAuthorityRefreshUpdatesProtectedCacheAsync();
        await TransportFailureUsesTrustedCacheAsync();
        await TimeoutUsesTrustedCacheAsync();
        await CloudApiFailureDoesNotUseStaleCacheAsync();
        await WrongWorkspaceDoesNotUseStaleCacheAsync();
        await MalformedSnapshotDoesNotUseStaleCacheAsync();
        await CallerCancellationDoesNotUseStaleCacheAsync();
    }

    private static async Task OnlineSnapshotMakesNewEmployeeImmediatelyUsableAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OldPass2", 1, 2));
        context.Handler.EnqueueSnapshot(
            12,
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OldPass2", 1, 2),
            Seed("emp_new", "0004", "New User", "new@example.test", EmployeeRoles.User, "NewPass4", 1, 1));

        var principal = await context.Provider.AuthenticateAsync(
            new IdentityAuthenticationRequest("0004", "NewPass4"));

        NotNull(principal, "new current-authority Employee should authenticate immediately");
        Equal("emp_new", principal!.StableEmployeeId, "new Employee stable ID");
        Equal(12, context.Cache.LoadState()?.WorkspaceRevision ?? -1, "fresh snapshot revision should be cached");
        True(context.Cache.LoadAll().Any(item => item.EmployeeNo == "0004"),
            "freshly-created Employee should be written to protected cache");
    }

    private static async Task CurrentRolePasswordAndEnabledStateWinAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OldPass2", 1, 2));

        context.Handler.EnqueueSnapshot(
            20,
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.Admin, "NewPass2", 2, 9));
        var promoted = await context.Provider.AuthenticateAsync(
            new IdentityAuthenticationRequest("0002", "NewPass2"));
        NotNull(promoted, "new password should authenticate against current Cloud authority");
        Equal(AppRole.Admin, promoted!.Role, "current Cloud role should replace stale cached role");
        Equal(9, promoted.AuthorityRevision, "current Employee revision should be returned");

        context.Handler.EnqueueSnapshot(
            21,
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.Admin, "NewPass2", 2, 10));
        True(await context.Provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "OldPass2")) is null,
            "old password must stop working on the next online authentication");

        context.Handler.EnqueueSnapshot(
            22,
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.Admin, "NewPass2", 2, 11, enabled: false));
        True(await context.Provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "NewPass2")) is null,
            "disabled current authority must reject authentication immediately");
        var cached = context.Cache.LoadAll().Single(item => item.EmployeeNo == "0002");
        True(!cached.Enabled, "disabled current authority should replace the stale enabled cache row");
    }

    private static async Task ExplicitAuthorityRefreshUpdatesProtectedCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OldPass2", 1, 2));
        context.Handler.EnqueueSnapshot(
            30,
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.Admin, "NewPass2", 3, 15));

        await context.Provider.RefreshAuthorityAsync();

        Equal(30, context.Cache.LoadState()?.WorkspaceRevision ?? -1,
            "background/provider refresh should persist current Workspace revision");
        var cached = context.Cache.LoadAll().Single(item => item.EmployeeNo == "0002");
        Equal(EmployeeRoles.Admin, cached.Role, "explicit authority refresh should update cached role");
        Equal(3, cached.CredentialVersion, "explicit authority refresh should update credential version");
        Equal(15, cached.Revision, "explicit authority refresh should update Employee revision");
    }

    private static async Task TransportFailureUsesTrustedCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        context.Handler.EnqueueFailure(new HttpRequestException("synthetic network outage"));

        var principal = await context.Provider.AuthenticateAsync(
            new IdentityAuthenticationRequest("0002", "OfflinePass2"));

        NotNull(principal, "genuine transport outage should allow trusted protected-cache authentication");
        Equal(AppRole.User, principal!.Role, "offline fallback role should come from the same Cloud authority cache");
    }

    private static async Task TimeoutUsesTrustedCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        context.Handler.EnqueueFailure(new TaskCanceledException("synthetic request timeout"));

        var principal = await context.Provider.AuthenticateAsync(
            new IdentityAuthenticationRequest("0002", "OfflinePass2"));

        NotNull(principal, "provider timeout should allow trusted protected-cache authentication");
    }

    private static async Task CloudApiFailureDoesNotUseStaleCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        context.Handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent("""{"ok":false,"error":{"code":"STORAGE_UNAVAILABLE","message":"Storage unavailable."}}"""),
        });

        await ThrowsAsync<CloudApiException>(
            () => context.Provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "OfflinePass2")),
            "Cloud API response must fail closed instead of using stale cache");
    }

    private static async Task WrongWorkspaceDoesNotUseStaleCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        context.Handler.EnqueueSnapshot(
            40,
            new[] { Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2) },
            workspaceId: "ws_other");

        await ThrowsAsync<InvalidDataException>(
            () => context.Provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "OfflinePass2")),
            "wrong-Workspace snapshot must fail closed");
    }

    private static async Task MalformedSnapshotDoesNotUseStaleCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        context.Handler.EnqueueRawSnapshot("""
            {"ok":true,"employeeSnapshot":{"workspaceId":"ws_test","workspaceRevision":41,"employees":[{"employeeId":"emp_user","employeeNo":"0002","name":"Existing User","email":"existing@example.test","role":"USER","enabled":true,"emailVerified":true,"credentialVerifier":"invalid","credentialAlgorithm":"unsupported","credentialVersion":2,"revision":3}]}}
            """);

        await ThrowsAsync<InvalidDataException>(
            () => context.Provider.AuthenticateAsync(new IdentityAuthenticationRequest("0002", "OfflinePass2")),
            "malformed current authority must fail closed");
    }

    private static async Task CallerCancellationDoesNotUseStaleCacheAsync()
    {
        using var context = FreshnessContext.Create(
            Seed("emp_user", "0002", "Existing User", "existing@example.test", EmployeeRoles.User, "OfflinePass2", 1, 2));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await ThrowsAsync<OperationCanceledException>(
            () => context.Provider.AuthenticateAsync(
                new IdentityAuthenticationRequest("0002", "OfflinePass2"),
                cancellation.Token),
            "caller cancellation must propagate instead of falling back to cache");
    }

    private static CloudEmployeeCacheSeed Seed(
        string employeeId,
        string employeeNo,
        string name,
        string email,
        string role,
        string password,
        int credentialVersion,
        int revision,
        bool enabled = true) =>
        new(
            employeeId,
            employeeNo,
            name,
            email,
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

    private static StringContent JsonContent(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static async Task ThrowsAsync<TException>(Func<Task> action, string description)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
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

    private sealed class FreshnessContext : IDisposable
    {
        private FreshnessContext(
            string directory,
            SettingsStore settings,
            CloudEmployeeCacheStore cache,
            FreshnessHandler handler,
            HttpClient httpClient,
            BuiltInCloudIdentityProvider provider)
        {
            Directory = directory;
            Settings = settings;
            Cache = cache;
            Handler = handler;
            HttpClient = httpClient;
            Provider = provider;
        }

        public string Directory { get; }
        public SettingsStore Settings { get; }
        public CloudEmployeeCacheStore Cache { get; }
        public FreshnessHandler Handler { get; }
        public HttpClient HttpClient { get; }
        public BuiltInCloudIdentityProvider Provider { get; }

        public static FreshnessContext Create(params CloudEmployeeCacheSeed[] staleEmployees)
        {
            var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.IdentityFreshness.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var protector = new TestProtector();
            var settingsStore = new SettingsStore(directory, protector);
            var settings = settingsStore.LoadOrCreate();
            settings.CloudBaseUrl = "https://cloud.example.test/";
            settings.CloudWorkspaceId = "ws_test";
            settings.CloudDeviceId = "dev_test";
            settings.CloudEmployeeAuthorityReady = true;
            settings.CloudMode = CloudModes.CloudPreferred;
            settingsStore.SetCloudDeviceToken(settings,
                "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            settingsStore.Save(settings);

            var cache = new CloudEmployeeCacheStore(directory, protector);
            cache.ReplaceSnapshot(
                "ws_test",
                1,
                new[]
                {
                    Seed("emp_super", "0001", "Cloud Admin", "admin@example.test", EmployeeRoles.SuperAdmin, "AdminPass1", 1, 1),
                }.Concat(staleEmployees).ToArray());

            var handler = new FreshnessHandler();
            var httpClient = new HttpClient(handler);
            var provider = new BuiltInCloudIdentityProvider(settingsStore, cache, httpClient);
            return new FreshnessContext(directory, settingsStore, cache, handler, httpClient, provider);
        }

        public void Dispose()
        {
            HttpClient.Dispose();
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class FreshnessHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses = new();

        public void EnqueueSnapshot(int workspaceRevision, params CloudEmployeeCacheSeed[] employees) =>
            EnqueueSnapshot(workspaceRevision, employees, "ws_test");

        public void EnqueueSnapshot(
            int workspaceRevision,
            IReadOnlyList<CloudEmployeeCacheSeed> employees,
            string workspaceId)
        {
            var payload = JsonSerializer.Serialize(new
            {
                ok = true,
                employeeSnapshot = new
                {
                    workspaceId,
                    workspaceRevision,
                    employees = employees.Select(employee => new
                    {
                        employeeId = employee.EmployeeId,
                        employeeNo = employee.EmployeeNo,
                        name = employee.Name,
                        email = employee.Email,
                        role = employee.Role,
                        enabled = employee.Enabled,
                        emailVerified = employee.EmailVerified,
                        credentialVerifier = employee.CredentialVerifier,
                        credentialAlgorithm = "pbkdf2-sha256",
                        credentialVersion = employee.CredentialVersion,
                        revision = employee.Revision,
                    }).ToArray(),
                },
            });
            EnqueueRawSnapshot(payload);
        }

        public void EnqueueRawSnapshot(string json) =>
            responses.Enqueue((request, _) =>
            {
                ValidateRequest(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent(json),
                });
            });

        public void EnqueueResponse(HttpResponseMessage response) =>
            responses.Enqueue((request, _) =>
            {
                ValidateRequest(request);
                return Task.FromResult(response);
            });

        public void EnqueueFailure(Exception error) =>
            responses.Enqueue((request, _) =>
            {
                ValidateRequest(request);
                return Task.FromException<HttpResponseMessage>(error);
            });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (responses.Count == 0)
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException("No synthetic Cloud response was queued."));
            return responses.Dequeue()(request, cancellationToken);
        }

        private static void ValidateRequest(HttpRequestMessage request)
        {
            Equal(HttpMethod.Get, request.Method, "authority refresh method");
            Equal("/v1/employee-authority/snapshot", request.RequestUri?.AbsolutePath ?? string.Empty,
                "authority refresh endpoint");
            Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty,
                "authority refresh authorization scheme");
        }
    }
}
