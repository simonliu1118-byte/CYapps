using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("cloud settings default to local-only with no endpoint", TestSettingsAsync),
    ("cloud client rejects non-HTTPS base URLs", TestHttpsOnlyAsync),
    ("cloud health parses provider-neutral backend status", TestHealthAsync),
    ("cloud health preserves backend storage outage diagnostics", TestStorageOutageAsync),
    ("cloud compatibility rejects non-CYInvoice services", TestCompatibilityAsync),
    ("cloud onboarding status distinguishes initialized backends", TestOnboardingStatusAsync),
    ("cloud bootstrap email challenge preserves bootstrap authorization", TestBootstrapEmailChallengeAsync),
    ("cloud client rejects non-JSON bootstrap responses without exposing response text", TestInvalidBootstrapResponseAsync),
    ("cloud bootstrap reuses caller-owned token and requires email OTP", TestBootstrapAsync),
    ("cloud authenticated device request sends bearer token", TestDeviceAuthenticationAsync),
    ("cloud pairing authorization uses the authenticated Device and Workspace recovery Email", TestPairingAuthorizationAsync),
    ("cloud pairing code requires email authorization OTP", TestPairingAsync),
    ("cloud pairing claim reuses caller-owned token across retries", TestPairingClaimAsync),
    ("central employee reconciliation sends only the existing Local SUPER_ADMIN profile", TestEmployeeReconciliationAsync),
    ("central employee listing parses Workspace roles", TestEmployeeListAsync),
    ("central employee update requires execution-time actor credentials", TestEmployeeAccountUpdateAsync),
    ("central employee Email change requires OTP confirmation", TestEmployeeEmailUpdateAsync),
    ("central enabled and password operations never send plaintext new password", TestEmployeeEnabledAndPasswordAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures.Add($"{test.Name}: {error.Message}");
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"{failures.Count} cloud test(s) failed.");
    return 1;
}

Console.WriteLine("All CYInvoice cloud client tests passed.");
return 0;

static Task TestSettingsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.Cloud.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SettingsStore(directory, new TestProtector());
        var settings = store.LoadOrCreate();
        Equal(CloudModes.LocalOnly, settings.CloudMode, "default cloud mode");
        Equal(string.Empty, settings.CloudBaseUrl, "public client must not ship with a cloud endpoint");
        Equal(string.Empty, settings.CloudWorkspaceId, "default workspace identity");
        Equal(string.Empty, settings.CloudDeviceId, "default device identity");
        Equal(string.Empty, settings.CloudPendingBootstrapTokenEncrypted, "default pending bootstrap token");
        Equal(string.Empty, settings.CloudPendingDeviceJoinTokenEncrypted, "default pending Device Join token");
        True(store.CloudPendingBootstrap(settings) is null, "default settings must not invent pending bootstrap state");
        True(store.CloudPendingDeviceJoin(settings) is null, "default settings must not invent pending Device Join state");

        settings.CloudMode = CloudModes.CloudPreferred;
        settings.CloudBaseUrl = "https://cloud.example.test/";
        store.Save(settings);

        var configured = store.LoadOrCreate();
        Equal(CloudModes.CloudPreferred, configured.CloudMode, "cloud mode can be selected before device registration");
        Equal("https://cloud.example.test/", configured.CloudBaseUrl, "user-configured cloud endpoint");
        Equal(string.Empty, configured.CloudDeviceId, "cloud endpoint configuration does not invent a device identity");

        var pendingToken = "cydev_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var pendingStarted = DateTimeOffset.Parse("2026-09-21T05:45:00+00:00");
        store.SetCloudPendingBootstrap(
            configured,
            "https://cloud.example.test",
            "Chihyuan",
            "高雄-A機",
            pendingToken,
            pendingStarted);
        True(configured.CloudPendingBootstrapTokenEncrypted != pendingToken,
            "pending bootstrap token must not be stored as plaintext");
        store.Save(configured);

        var pendingReloadedSettings = store.LoadOrCreate();
        var pending = store.CloudPendingBootstrap(pendingReloadedSettings)
            ?? throw new InvalidOperationException("pending bootstrap state should survive restart");
        Equal("https://cloud.example.test/", pending.BaseUrl, "pending bootstrap endpoint");
        Equal("Chihyuan", pending.WorkspaceDisplayName, "pending workspace name");
        Equal("高雄-A機", pending.DeviceDisplayName, "pending device name");
        Equal(pendingStarted, pending.StartedAtUtc, "pending bootstrap start time");
        Equal(pendingToken, pending.DeviceToken, "protected pending token round-trip");

        pendingReloadedSettings.CloudWorkspaceId = "ws_test";
        pendingReloadedSettings.CloudDeviceId = "dev_test";
        store.SetCloudDeviceToken(pendingReloadedSettings, "cydev_secret");
        True(pendingReloadedSettings.CloudDeviceTokenEncrypted != "cydev_secret", "device token must not be stored as plaintext");
        store.Save(pendingReloadedSettings);

        var reloaded = store.LoadOrCreate();
        Equal(CloudModes.CloudPreferred, reloaded.CloudMode, "persisted cloud mode");
        Equal("cydev_secret", store.CloudDeviceToken(reloaded), "protected cloud token round-trip");
        True(store.CloudPendingBootstrap(reloaded) is not null, "pending state remains until onboarding is explicitly finalized or cleared");

        store.ClearCloudIdentity(reloaded);
        store.Save(reloaded);
        var cleared = store.LoadOrCreate();
        Equal(CloudModes.LocalOnly, cleared.CloudMode, "clearing cloud identity returns to local-only");
        Equal(string.Empty, cleared.CloudWorkspaceId, "cleared workspace identity");
        Equal(string.Empty, cleared.CloudDeviceId, "cleared device identity");
        Equal(string.Empty, cleared.CloudDeviceTokenEncrypted, "cleared device token");
        True(store.CloudPendingBootstrap(cleared) is null, "clearing cloud identity must also clear pending bootstrap state");
        True(store.CloudPendingDeviceJoin(cleared) is null, "clearing cloud identity must also clear pending Device Join state");

        var joinToken = "cydev_dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        var joinStarted = DateTimeOffset.Parse("2026-09-21T07:30:00+00:00");
        store.SetCloudPendingDeviceJoin(
            cleared,
            "https://cloud.example.test",
            "高雄-B機",
            joinToken,
            joinStarted);
        True(cleared.CloudPendingDeviceJoinTokenEncrypted != joinToken,
            "pending Device Join token must not be stored as plaintext");
        store.Save(cleared);

        var joinReloadedSettings = store.LoadOrCreate();
        var join = store.CloudPendingDeviceJoin(joinReloadedSettings)
            ?? throw new InvalidOperationException("pending Device Join state should survive restart");
        Equal("https://cloud.example.test/", join.BaseUrl, "pending Device Join endpoint");
        Equal("高雄-B機", join.DeviceDisplayName, "pending Device Join device name");
        Equal(joinStarted, join.StartedAtUtc, "pending Device Join start time");
        Equal(joinToken, join.DeviceToken, "protected pending Device Join token round-trip");

        store.ClearCloudIdentity(joinReloadedSettings);
        store.Save(joinReloadedSettings);
        True(store.CloudPendingDeviceJoin(store.LoadOrCreate()) is null,
            "clearing cloud identity must clear pending Device Join token");

        var incomplete = store.LoadOrCreate();
        incomplete.CloudPendingBootstrapUrl = "https://cloud.example.test/";
        Throws<InvalidDataException>(() => store.Save(incomplete));
        incomplete.CloudPendingBootstrapUrl = string.Empty;
        incomplete.CloudPendingDeviceJoinUrl = "https://cloud.example.test/";
        Throws<InvalidDataException>(() => store.Save(incomplete));
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestHttpsOnlyAsync()
{
    using var http = new HttpClient(new QueueHandler());
    Throws<ArgumentException>(() => new CloudClient(http, new Uri("http://cloud.example.test/")));
    return Task.CompletedTask;
}

static async Task TestHealthAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK,
        """{"ok":true,"service":"cyinvoice-cloud","cloudVersion":"0.8.1","apiVersion":"1","schemaVersion":"7","environment":"test","storage":"ok"}"""));
    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    var health = await client.CheckHealthAsync();
    True(health.Reachable, "health should be reachable");
    True(health.StorageAvailable, "backend storage should be available");
    Equal("cyinvoice-cloud", health.ServiceName, "service name");
    Equal("0.8.1", health.CloudVersion, "cloud version");
    Equal("1", health.ApiVersion, "api version");
    Equal("7", health.SchemaVersion, "schema version");
    Equal(string.Empty, CloudCompatibility.Problem(health), "compatible service should have no compatibility problem");
}

static async Task TestStorageOutageAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(_ => JsonResponse(HttpStatusCode.ServiceUnavailable,
        """{"ok":false,"service":"cyinvoice-cloud","cloudVersion":"0.8.1","apiVersion":"1","schemaVersion":"7","environment":"test","storage":"unavailable","error":{"code":"STORAGE_UNAVAILABLE","message":"Backend storage health check failed."}}"""));
    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    var health = await client.CheckHealthAsync();
    True(health.Reachable, "cloud API should still be reachable during a D1 outage");
    True(!health.StorageAvailable, "storage outage must not be reported as healthy");
    Equal("cyinvoice-cloud", health.ServiceName, "storage outage should preserve service identity");
    Equal("1", health.ApiVersion, "storage outage should preserve API version");
    Equal("7", health.SchemaVersion, "storage outage should preserve schema version");
    Equal("STORAGE_UNAVAILABLE", health.ErrorCode, "storage outage error code");
    True(CloudCompatibility.Problem(health).Contains("後端儲存服務尚未就緒", StringComparison.Ordinal),
        "storage outage should report backend storage instead of a wrong-service error");
}

static Task TestCompatibilityAsync()
{
    var health = new CloudHealthResult(
        true,
        true,
        "other-service",
        "1.0.0",
        CloudCompatibility.ApiVersion,
        CloudCompatibility.SchemaVersion,
        "test",
        12,
        string.Empty);
    True(CloudCompatibility.Problem(health).Contains("不是相容的 CYInvoice Cloud API", StringComparison.Ordinal),
        "non-CYInvoice service must be rejected");
    return Task.CompletedTask;
}

static async Task TestOnboardingStatusAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Get, request.Method, "onboarding status method");
        Equal("/v1/onboarding/status", request.RequestUri?.AbsolutePath ?? string.Empty, "onboarding status path");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"onboarding":{"state":"uninitialized","workspaceInitialized":false}}""");
    });
    handler.Enqueue(_ => JsonResponse(HttpStatusCode.OK,
        """{"ok":true,"onboarding":{"state":"initialized","workspaceInitialized":true}}"""));

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    var first = await client.GetOnboardingStatusAsync();
    Equal("uninitialized", first.State, "uninitialized state");
    True(!first.WorkspaceInitialized, "new backend must report no workspace");

    var second = await client.GetOnboardingStatusAsync();
    Equal("initialized", second.State, "initialized state");
    True(second.WorkspaceInitialized, "existing backend must report a workspace");
}

static async Task TestBootstrapEmailChallengeAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "bootstrap email challenge method");
        Equal("/v1/onboarding/bootstrap-email", request.RequestUri?.AbsolutePath ?? string.Empty,
            "bootstrap email challenge path");
        True(request.Headers.TryGetValues("X-Bootstrap-Key", out var values), "bootstrap email header should exist");
        Equal("temporary-bootstrap", values!.Single(), "bootstrap email header value");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        Equal("owner@example.test", body.RootElement.GetProperty("email").GetString() ?? string.Empty,
            "existing Local SUPER_ADMIN email is forwarded without a separate user-entered address");
        return JsonResponse(HttpStatusCode.Created,
            """{"ok":true,"challenge":{"challengeId":"otp_12345678-1234-1234-1234-123456789abc","maskedEmail":"ow***@example.test","expiresAt":"2026-09-21T07:10:00Z","resendAfter":"2026-09-21T07:01:00Z"}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));
    var challenge = await client.StartBootstrapEmailChallengeAsync("temporary-bootstrap", "owner@example.test");
    Equal("otp_12345678-1234-1234-1234-123456789abc", challenge.ChallengeId, "challenge ID");
    Equal("ow***@example.test", challenge.MaskedEmail, "masked email");
    Equal(DateTimeOffset.Parse("2026-09-21T07:10:00Z"), challenge.ExpiresAt, "OTP expiry");
    Equal(DateTimeOffset.Parse("2026-09-21T07:01:00Z"), challenge.ResendAfter, "OTP resend time");
}

static async Task TestInvalidBootstrapResponseAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("error code: 1101", Encoding.UTF8, "text/plain")
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    try
    {
        await client.StartBootstrapEmailChallengeAsync("temporary-bootstrap", "owner@example.test");
    }
    catch (CloudApiException error)
    {
        Equal("CLOUD_INVALID_RESPONSE", error.Code, "invalid Cloud response error code");
        Equal(HttpStatusCode.InternalServerError, error.StatusCode, "invalid Cloud response HTTP status");
        True(!error.Message.Contains("1101", StringComparison.Ordinal),
            "raw provider response text must not be exposed to the UI");
        return;
    }

    throw new InvalidOperationException("Expected CloudApiException for a non-JSON Cloud response.");
}

static async Task TestBootstrapAsync()
{
    var attempt = new CloudBootstrapAttempt(
        "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    const string challengeId = "otp_12345678-1234-1234-1234-123456789abc";
    const string otp = "123456";
    var handler = new QueueHandler();

    for (var index = 0; index < 2; index++)
    {
        var status = index == 0 ? HttpStatusCode.Created : HttpStatusCode.OK;
        handler.Enqueue(request =>
        {
            True(request.Headers.TryGetValues("X-Bootstrap-Key", out var values), "bootstrap header should exist");
            Equal("temporary-bootstrap", values!.Single(), "bootstrap header value");
            Equal(HttpMethod.Post, request.Method, "bootstrap method");
            Equal("/v1/bootstrap", request.RequestUri?.AbsolutePath ?? string.Empty, "bootstrap path");

            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("CYInvoice", body.RootElement.GetProperty("workspaceDisplayName").GetString() ?? string.Empty, "bootstrap workspace name");
            Equal("A機", body.RootElement.GetProperty("deviceDisplayName").GetString() ?? string.Empty, "bootstrap device name");
            Equal(challengeId, body.RootElement.GetProperty("emailChallengeId").GetString() ?? string.Empty, "bootstrap email challenge ID");
            Equal(otp, body.RootElement.GetProperty("emailOtp").GetString() ?? string.Empty, "bootstrap OTP");
            True(!body.RootElement.TryGetProperty("deviceId", out _), "bootstrap device ID must remain cloud-owned");
            Equal(attempt.DeviceToken, body.RootElement.GetProperty("deviceToken").GetString() ?? string.Empty, "bootstrap retry device token");

            return JsonResponse(status,
                """{"ok":true,"workspace":{"workspaceId":"ws_1","displayName":"CYInvoice"},"device":{"deviceId":"dev_cloud_generated","displayName":"A機"}}""");
        });
    }

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));
    var first = await client.BootstrapAsync("temporary-bootstrap", "CYInvoice", "A機", "2.6.4", challengeId, otp, attempt);
    var retry = await client.BootstrapAsync("temporary-bootstrap", "CYInvoice", "A機", "2.6.4", challengeId, otp, attempt);

    Equal("ws_1", first.WorkspaceId, "bootstrap workspace ID");
    Equal("dev_cloud_generated", first.DeviceId, "bootstrap device ID is returned by Cloud");
    Equal(attempt.DeviceToken, first.DeviceToken, "bootstrap token remains caller-owned");
    Equal(first.WorkspaceId, retry.WorkspaceId, "retry workspace ID");
    Equal(first.DeviceId, retry.DeviceId, "retry device ID");
    Equal(first.DeviceToken, retry.DeviceToken, "retry must reuse the same device token");

    var generated = CloudBootstrapAttempt.Create();
    True(generated.DeviceToken.StartsWith("cydev_", StringComparison.Ordinal) && generated.DeviceToken.Length == 70,
        "generated bootstrap device token format");
}

static async Task TestDeviceAuthenticationAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty, "authorization scheme");
        Equal("cydev_test_token", request.Headers.Authorization?.Parameter ?? string.Empty, "authorization token");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"workspace":{"workspaceId":"ws_1"},"device":{"deviceId":"dev_1","displayName":"A機"}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"), "cydev_test_token");
    var identity = await client.GetCurrentDeviceAsync();
    Equal("ws_1", identity.WorkspaceId, "workspace ID");
    Equal("dev_1", identity.DeviceId, "device ID");
    Equal("cydev_test_token", identity.DeviceToken, "local device token remains available to caller");
}

static async Task TestPairingAuthorizationAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "pairing authorization method");
        Equal("/v1/device-pairings/authorization-email", request.RequestUri?.AbsolutePath ?? string.Empty,
            "pairing authorization path");
        Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty, "pairing authorization bearer scheme");
        Equal("cydev_test_token", request.Headers.Authorization?.Parameter ?? string.Empty,
            "pairing authorization Device token");
        return JsonResponse(HttpStatusCode.Created,
            """{"ok":true,"challenge":{"challengeId":"otp_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","maskedEmail":"ow***@example.test","expiresAt":"2026-09-21T08:10:00Z","resendAfter":"2026-09-21T08:01:00Z"}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"), "cydev_test_token");
    var challenge = await client.StartPairingAuthorizationAsync();
    Equal("otp_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", challenge.ChallengeId, "pairing authorization challenge ID");
    Equal("ow***@example.test", challenge.MaskedEmail, "pairing authorization masked Email");
}

static async Task TestPairingAsync()
{
    const string challengeId = "otp_aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    const string otp = "654321";
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "pairing creation method");
        Equal("/v1/device-pairings", request.RequestUri?.AbsolutePath ?? string.Empty, "pairing creation path");
        Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty, "pairing creation bearer scheme");
        Equal("cydev_test_token", request.Headers.Authorization?.Parameter ?? string.Empty, "pairing creation Device token");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        Equal(challengeId, body.RootElement.GetProperty("emailChallengeId").GetString() ?? string.Empty,
            "pairing authorization challenge ID");
        Equal(otp, body.RootElement.GetProperty("emailOtp").GetString() ?? string.Empty,
            "pairing authorization OTP");
        return JsonResponse(HttpStatusCode.Created,
            """{"ok":true,"pairing":{"code":"0123456789abcdefabcd","expiresAt":"2026-09-21T08:20:00.000Z"}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"), "cydev_test_token");
    var pairing = await client.CreatePairingAsync(challengeId, otp);
    Equal("0123456789abcdefabcd", pairing.Code, "pairing code");
    Equal(DateTimeOffset.Parse("2026-09-21T08:20:00.000Z"), pairing.ExpiresAt, "pairing expiry");
}

static async Task TestPairingClaimAsync()
{
    var attempt = new CloudDeviceJoinAttempt(
        "cydev_cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
    var handler = new QueueHandler();

    for (var index = 0; index < 2; index++)
    {
        var status = index == 0 ? HttpStatusCode.Created : HttpStatusCode.OK;
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "claim method");
            Equal("/v1/device-pairings/claim", request.RequestUri?.AbsolutePath ?? string.Empty, "claim path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0123456789abcdefabcd", body.RootElement.GetProperty("code").GetString() ?? string.Empty, "pairing code");
            Equal("B機", body.RootElement.GetProperty("deviceDisplayName").GetString() ?? string.Empty, "pairing device name");
            Equal(attempt.DeviceToken, body.RootElement.GetProperty("deviceToken").GetString() ?? string.Empty,
                "pairing retry must send the same caller-owned token");
            True(!body.RootElement.TryGetProperty("deviceId", out _), "pairing Device ID must remain cloud-owned");
            return JsonResponse(status,
                """{"ok":true,"workspace":{"workspaceId":"ws_1"},"device":{"deviceId":"dev_2","displayName":"B機"}}""");
        });
    }

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));
    var first = await client.ClaimPairingAsync("0123456789abcdefabcd", "B機", "2.6.4", attempt);
    var retry = await client.ClaimPairingAsync("0123456789abcdefabcd", "B機", "2.6.4", attempt);

    Equal("ws_1", first.WorkspaceId, "claim workspace ID");
    Equal("dev_2", first.DeviceId, "claim Device ID is returned by Cloud");
    Equal(attempt.DeviceToken, first.DeviceToken, "claim token remains caller-owned");
    Equal(first.WorkspaceId, retry.WorkspaceId, "retry workspace ID");
    Equal(first.DeviceId, retry.DeviceId, "retry device ID");
    Equal(first.DeviceToken, retry.DeviceToken, "retry must reuse the same Device Token");

    var generated = CloudDeviceJoinAttempt.Create();
    True(generated.DeviceToken.StartsWith("cydev_", StringComparison.Ordinal) && generated.DeviceToken.Length == 70,
        "generated Device Join token format");
}

static async Task TestEmployeeReconciliationAsync()
{
    const string token = "cydev_eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    var local = new EmployeeAccount(
        "0001",
        "X",
        "owner@example.test",
        EmployeeRoles.SuperAdmin,
        true,
        DateTimeOffset.Parse("2026-09-21T00:00:00Z"),
        DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "employee reconciliation method");
        Equal("/v1/employees/reconcile-local", request.RequestUri?.AbsolutePath ?? string.Empty,
            "employee reconciliation path");
        Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty, "employee reconciliation bearer scheme");
        Equal(token, request.Headers.Authorization?.Parameter ?? string.Empty, "employee reconciliation Device token");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        var candidate = body.RootElement.GetProperty("localSuperAdmin");
        Equal("0001", candidate.GetProperty("employeeNo").GetString() ?? string.Empty, "local SUPER_ADMIN employee number");
        Equal("X", candidate.GetProperty("name").GetString() ?? string.Empty, "local SUPER_ADMIN name");
        Equal("owner@example.test", candidate.GetProperty("email").GetString() ?? string.Empty, "local SUPER_ADMIN email");
        True(!candidate.TryGetProperty("password", out _), "local password must never be sent to central employee reconciliation");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"reconciliation":{"state":"owner_created","employee":{"employeeId":"emp_1","employeeNo":"0001","name":"X","email":"owner@example.test","role":"SUPER_ADMIN","enabled":true}}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudEmployeeClient(http, new Uri("https://cloud.example.test/"), token);
    var result = await client.ReconcileLocalSuperAdminAsync(local);
    Equal("owner_created", result.State, "employee reconciliation state");
    True(result.CentralRoleReady, "central role should be ready after owner creation");
    Equal("SUPER_ADMIN", result.Employee?.Role ?? string.Empty, "central owner role");
    Equal("0001", result.Employee?.EmployeeNo ?? string.Empty, "central owner employee number");
}

static async Task TestEmployeeListAsync()
{
    const string token = "cydev_ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Get, request.Method, "employee list method");
        Equal("/v1/employees", request.RequestUri?.AbsolutePath ?? string.Empty, "employee list path");
        Equal(token, request.Headers.Authorization?.Parameter ?? string.Empty, "employee list Device token");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"employees":[{"employeeId":"emp_x","employeeNo":"0001","name":"X","email":"x@example.test","role":"SUPER_ADMIN","enabled":true},{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"ADMIN","enabled":true}]}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudEmployeeClient(http, new Uri("https://cloud.example.test/"), token);
    var employees = await client.ListEmployeesAsync();
    Equal(2, employees.Count, "central employee count");
    Equal("SUPER_ADMIN", employees[0].Role, "first central employee role");
    Equal("ADMIN", employees[1].Role, "second central employee role");
}

static async Task TestEmployeeAccountUpdateAsync()
{
    const string token = "cydev_1111111111111111111111111111111111111111111111111111111111111111";
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "employee update method");
        Equal("/v1/employees/update/challenge", request.RequestUri?.AbsolutePath ?? string.Empty, "employee update path");
        Equal(token, request.Headers.Authorization?.Parameter ?? string.Empty, "employee update Device token");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        Equal("0001", body.RootElement.GetProperty("actorEmployeeNo").GetString() ?? string.Empty, "employee update actor");
        Equal("actor-password", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "employee update execution-time password");
        Equal("0002", body.RootElement.GetProperty("targetEmployeeNo").GetString() ?? string.Empty, "employee update target");
        Equal("Y Updated", body.RootElement.GetProperty("name").GetString() ?? string.Empty, "employee update name");
        Equal("ADMIN", body.RootElement.GetProperty("role").GetString() ?? string.Empty, "employee update role");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"verificationRequired":false,"employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y Updated","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":2}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudEmployeeAccountClient(http, new Uri("https://cloud.example.test/"), token);
    var result = await client.StartUpdateAsync(
        "0001",
        "actor-password",
        new CloudEmployeeUpdateProposal("0002", "Y Updated", "y@example.test", "ADMIN"));
    True(!result.VerificationRequired, "same Email update should commit without Email OTP");
    Equal("Y Updated", result.Employee?.Name ?? string.Empty, "updated employee name");
    Equal(2, result.Employee?.Revision ?? 0, "updated employee revision");
}

static async Task TestEmployeeEmailUpdateAsync()
{
    const string token = "cydev_2222222222222222222222222222222222222222222222222222222222222222";
    const string challengeId = "otp_aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb";
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal("/v1/employees/update/challenge", request.RequestUri?.AbsolutePath ?? string.Empty, "Email update challenge path");
        return JsonResponse(HttpStatusCode.Created,
            """{"ok":true,"verificationRequired":true,"challenge":{"challengeId":"otp_aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb","maskedEmail":"ne***@example.test","expiresAt":"2026-09-22T06:10:00Z","resendAfter":"2026-09-22T06:01:00Z"}}""");
    });
    handler.Enqueue(request =>
    {
        Equal("/v1/employees/update/confirm", request.RequestUri?.AbsolutePath ?? string.Empty, "Email update confirm path");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        Equal(challengeId, body.RootElement.GetProperty("challengeId").GetString() ?? string.Empty, "Email update challenge ID");
        Equal("123456", body.RootElement.GetProperty("otp").GetString() ?? string.Empty, "Email update OTP");
        Equal("new@example.test", body.RootElement.GetProperty("email").GetString() ?? string.Empty, "Email update target address");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"verificationRequired":false,"employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"new@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":3}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudEmployeeAccountClient(http, new Uri("https://cloud.example.test/"), token);
    var proposal = new CloudEmployeeUpdateProposal("0002", "Y", "new@example.test", "ADMIN");
    var started = await client.StartUpdateAsync("0001", "actor-password", proposal);
    True(started.VerificationRequired, "changed Email must require OTP");
    Equal(challengeId, started.Challenge?.ChallengeId ?? string.Empty, "Email update challenge parsed");

    var updated = await client.ConfirmUpdateAsync("0001", "actor-password", proposal, challengeId, "123456");
    Equal("new@example.test", updated.Email, "confirmed Email update");
    True(updated.EmailVerified, "confirmed Email remains verified");
}

static async Task TestEmployeeEnabledAndPasswordAsync()
{
    const string token = "cydev_3333333333333333333333333333333333333333333333333333333333333333";
    const string verifier = "pbkdf2-sha256$210000$00112233445566778899aabbccddeeff$0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal("/v1/employees/enabled", request.RequestUri?.AbsolutePath ?? string.Empty, "enabled update path");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        True(!body.RootElement.GetProperty("enabled").GetBoolean(), "enabled update value");
        Equal("actor-password", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "enabled update execution-time password");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"EMPLOYEE","enabled":false,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":4}}""");
    });
    handler.Enqueue(request =>
    {
        Equal("/v1/employees/password", request.RequestUri?.AbsolutePath ?? string.Empty, "password update path");
        var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var body = JsonDocument.Parse(payload);
        Equal(verifier, body.RootElement.GetProperty("credentialVerifier").GetString() ?? string.Empty, "password verifier payload");
        True(!body.RootElement.TryGetProperty("newPassword", out _), "plaintext new password must never be uploaded");
        return JsonResponse(HttpStatusCode.OK,
            """{"ok":true,"employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"EMPLOYEE","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":2,"revision":5}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudEmployeeAccountClient(http, new Uri("https://cloud.example.test/"), token);
    var disabled = await client.SetEnabledAsync("0001", "actor-password", "0002", false);
    True(!disabled.Enabled, "disabled Employee response");

    var passwordUpdated = await client.SetPasswordAsync("0001", "actor-password", "0002", verifier);
    Equal(2, passwordUpdated.CredentialVersion, "password credential version");
    Equal(5, passwordUpdated.Revision, "password update Employee revision");
}

static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
{
    return new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
}

static void True(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class TestProtector : ISecretProtector
{
    public string Protect(ReadOnlySpan<byte> plaintext)
    {
        var bytes = plaintext.ToArray();
        Array.Reverse(bytes);
        return Convert.ToBase64String(bytes);
    }

    public byte[] Unprotect(string ciphertext)
    {
        var bytes = Convert.FromBase64String(ciphertext);
        Array.Reverse(bytes);
        return bytes;
    }
}

sealed class QueueHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) => responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (responses.Count == 0)
            throw new InvalidOperationException("Unexpected HTTP request.");
        return Task.FromResult(responses.Dequeue()(request));
    }
}
