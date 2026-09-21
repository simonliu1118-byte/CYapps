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
    ("cloud bootstrap reuses caller-owned device credentials for safe retry", TestBootstrapAsync),
    ("cloud authenticated device request sends bearer token", TestDeviceAuthenticationAsync),
    ("cloud pairing response parses one-time ticket", TestPairingAsync),
    ("cloud pairing claim parses new device identity", TestPairingClaimAsync)
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

        settings.CloudMode = CloudModes.CloudPreferred;
        settings.CloudBaseUrl = "https://cloud.example.test/";
        store.Save(settings);

        var configured = store.LoadOrCreate();
        Equal(CloudModes.CloudPreferred, configured.CloudMode, "cloud mode can be selected before device registration");
        Equal("https://cloud.example.test/", configured.CloudBaseUrl, "user-configured cloud endpoint");
        Equal(string.Empty, configured.CloudDeviceId, "cloud endpoint configuration does not invent a device identity");

        configured.CloudWorkspaceId = "ws_test";
        configured.CloudDeviceId = "dev_test";
        store.SetCloudDeviceToken(configured, "cydev_secret");
        True(configured.CloudDeviceTokenEncrypted != "cydev_secret", "device token must not be stored as plaintext");
        store.Save(configured);

        var reloaded = store.LoadOrCreate();
        Equal(CloudModes.CloudPreferred, reloaded.CloudMode, "persisted cloud mode");
        Equal("cydev_secret", store.CloudDeviceToken(reloaded), "protected cloud token round-trip");
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
        """{"ok":true,"service":"cyinvoice-cloud","cloudVersion":"0.4.0","apiVersion":"1","schemaVersion":"2","environment":"test","storage":"ok"}"""));
    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    var health = await client.CheckHealthAsync();
    True(health.Reachable, "health should be reachable");
    True(health.StorageAvailable, "backend storage should be available");
    Equal("cyinvoice-cloud", health.ServiceName, "service name");
    Equal("0.4.0", health.CloudVersion, "cloud version");
    Equal("1", health.ApiVersion, "api version");
    Equal("2", health.SchemaVersion, "schema version");
    Equal(string.Empty, CloudCompatibility.Problem(health), "compatible service should have no compatibility problem");
}

static async Task TestStorageOutageAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(_ => JsonResponse(HttpStatusCode.ServiceUnavailable,
        """{"ok":false,"service":"cyinvoice-cloud","cloudVersion":"0.4.0","apiVersion":"1","schemaVersion":"2","environment":"test","storage":"unavailable","error":{"code":"STORAGE_UNAVAILABLE","message":"Backend storage health check failed."}}"""));
    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));

    var health = await client.CheckHealthAsync();
    True(health.Reachable, "cloud API should still be reachable during a D1 outage");
    True(!health.StorageAvailable, "storage outage must not be reported as healthy");
    Equal("cyinvoice-cloud", health.ServiceName, "storage outage should preserve service identity");
    Equal("1", health.ApiVersion, "storage outage should preserve API version");
    Equal("2", health.SchemaVersion, "storage outage should preserve schema version");
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

static async Task TestBootstrapAsync()
{
    var attempt = new CloudBootstrapAttempt(
        "dev_11111111-1111-4111-8111-111111111111",
        "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
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
            Equal(attempt.DeviceId, body.RootElement.GetProperty("deviceId").GetString() ?? string.Empty, "bootstrap retry device ID");
            Equal(attempt.DeviceToken, body.RootElement.GetProperty("deviceToken").GetString() ?? string.Empty, "bootstrap retry device token");

            return JsonResponse(status,
                """{"ok":true,"workspace":{"workspaceId":"ws_1","displayName":"CYInvoice"},"device":{"deviceId":"dev_11111111-1111-4111-8111-111111111111","displayName":"A機"}}""");
        });
    }

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));
    var first = await client.BootstrapAsync("temporary-bootstrap", "CYInvoice", "A機", "2.6.3", attempt);
    var retry = await client.BootstrapAsync("temporary-bootstrap", "CYInvoice", "A機", "2.6.3", attempt);

    Equal("ws_1", first.WorkspaceId, "bootstrap workspace ID");
    Equal(attempt.DeviceId, first.DeviceId, "bootstrap device ID");
    Equal(attempt.DeviceToken, first.DeviceToken, "bootstrap token remains caller-owned");
    Equal(first.WorkspaceId, retry.WorkspaceId, "retry workspace ID");
    Equal(first.DeviceId, retry.DeviceId, "retry device ID");
    Equal(first.DeviceToken, retry.DeviceToken, "retry must reuse the same device token");

    var generated = CloudBootstrapAttempt.Create();
    True(generated.DeviceId.StartsWith("dev_", StringComparison.Ordinal), "generated bootstrap device ID prefix");
    True(Guid.TryParseExact(generated.DeviceId[4..], "D", out _), "generated bootstrap device ID UUID");
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

static async Task TestPairingAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(_ => JsonResponse(HttpStatusCode.Created,
        """{"ok":true,"pairing":{"code":"0123456789abcdefabcd","expiresAt":"2026-09-20T10:00:00.000Z"}}"""));

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"), "cydev_test_token");
    var pairing = await client.CreatePairingAsync();
    Equal("0123456789abcdefabcd", pairing.Code, "pairing code");
    Equal(DateTimeOffset.Parse("2026-09-20T10:00:00.000Z"), pairing.ExpiresAt, "pairing expiry");
}

static async Task TestPairingClaimAsync()
{
    var handler = new QueueHandler();
    handler.Enqueue(request =>
    {
        Equal(HttpMethod.Post, request.Method, "claim method");
        Equal("/v1/device-pairings/claim", request.RequestUri?.AbsolutePath ?? string.Empty, "claim path");
        return JsonResponse(HttpStatusCode.Created,
            """{"ok":true,"workspace":{"workspaceId":"ws_1"},"device":{"deviceId":"dev_2","displayName":"B機","token":"cydev_second_token"}}""");
    });

    using var http = new HttpClient(handler);
    var client = new CloudClient(http, new Uri("https://cloud.example.test/"));
    var identity = await client.ClaimPairingAsync("0123456789abcdefabcd", "B機", "2.6.3");
    Equal("ws_1", identity.WorkspaceId, "claim workspace ID");
    Equal("dev_2", identity.DeviceId, "claim device ID");
    Equal("cydev_second_token", identity.DeviceToken, "claim one-time device token");
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