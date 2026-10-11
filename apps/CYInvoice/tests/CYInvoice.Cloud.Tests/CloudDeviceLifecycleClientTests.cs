using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudDeviceLifecycleClientTests
{
    private const string DeviceToken = "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CurrentDeviceId = "dev_11111111-1111-4111-8111-111111111111";
    private const string OtherDeviceId = "dev_22222222-2222-4222-8222-222222222222";

    public static async Task RunAsync()
    {
        await ListDevicesParsesCurrentAndHistoryAsync();
        await RevokeSendsExecutionTimeSuperAdminCredentialAsync();
        await LastActiveDeviceFailureIsPreservedAsync();
        await InconsistentListFailsClosedAsync();
        await UsageReportsOwnVersionAndServerTimeAsync();
        await RenameSendsExecutionTimeCredentialAsync();
        await MetadataMismatchFailsClosedAsync();
    }

    private static async Task ListDevicesParsesCurrentAndHistoryAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Get, request.Method, "Device list method");
            Equal("/v1/devices", request.RequestUri?.AbsolutePath, "Device list path");
            Equal("Bearer", request.Headers.Authorization?.Scheme, "Device list authorization scheme");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter, "Device list token");
            return Json(HttpStatusCode.OK, new
            {
                currentDeviceId = CurrentDeviceId,
                activeDeviceCount = 2,
                devices = new object[]
                {
                    Device(CurrentDeviceId, "A 機", "active", current: true),
                    Device(OtherDeviceId, "B 機", "active", current: false),
                    Device("dev_33333333-3333-4333-8333-333333333333", "舊機", "revoked", current: false,
                        revokedAt: "2026-09-29T01:30:00+00:00"),
                }
            });
        });
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var result = await client.GetDevicesAsync();

        Equal(CurrentDeviceId, result.CurrentDeviceId, "current Device ID");
        Equal(2, result.ActiveDeviceCount, "active Device count");
        Equal(3, result.Devices.Count, "Device history count");
        True(result.Devices[0].Current, "current Device marker");
        Equal("revoked", result.Devices[2].Status, "revoked history status");
        True(result.Devices[2].RevokedAt is not null, "revoked timestamp");
    }

    private static async Task RevokeSendsExecutionTimeSuperAdminCredentialAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "Device revoke method");
            Equal("/v1/devices/revoke", request.RequestUri?.AbsolutePath, "Device revoke path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter, "Device revoke token");
            var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
            Equal(OtherDeviceId, body.GetProperty("targetDeviceId").GetString(), "target Device ID");
            Equal("0001", body.GetProperty("employeeNo").GetString(), "SUPER_ADMIN employee number");
            Equal("SuperPass1", body.GetProperty("password").GetString(), "execution-time password");
            return Json(HttpStatusCode.OK, new
            {
                device = Device(OtherDeviceId, "B 機", "revoked", current: false,
                    revokedAt: "2026-09-29T02:00:00+00:00"),
                alreadyRevoked = false,
            });
        });
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var result = await client.RevokeAsync(OtherDeviceId, "0001", "SuperPass1");

        Equal(OtherDeviceId, result.Device.DeviceId, "revoked Device ID");
        Equal("revoked", result.Device.Status, "revoked status");
        True(!result.AlreadyRevoked, "first revoke is not idempotent replay");
        True(result.Device.RevokedAt is not null, "revoke timestamp required");
    }

    private static async Task LastActiveDeviceFailureIsPreservedAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Error(
            HttpStatusCode.Conflict,
            "LAST_ACTIVE_DEVICE",
            "The last active Device cannot be revoked."));
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);

        try
        {
            await client.RevokeAsync(CurrentDeviceId, "0001", "SuperPass1");
        }
        catch (CloudApiException error)
        {
            Equal("LAST_ACTIVE_DEVICE", error.Code, "last active Device error code");
            Equal(HttpStatusCode.Conflict, error.StatusCode, "last active Device HTTP status");
            return;
        }
        throw new InvalidOperationException("LAST_ACTIVE_DEVICE must remain distinguishable to the Windows UI.");
    }

    private static async Task InconsistentListFailsClosedAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Json(HttpStatusCode.OK, new
        {
            currentDeviceId = CurrentDeviceId,
            activeDeviceCount = 2,
            devices = new object[]
            {
                Device(CurrentDeviceId, "A 機", "active", current: false),
                Device(OtherDeviceId, "B 機", "active", current: false),
            }
        }));
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);

        await ThrowsAsync<InvalidDataException>(
            () => client.GetDevicesAsync(),
            "Device list without a current marker must fail closed");
    }

    private static async Task UsageReportsOwnVersionAndServerTimeAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "usage method");
            Equal("/v1/devices/usage", request.RequestUri?.AbsolutePath, "usage path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter, "usage token");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal("2.6.14 Build 1", body.RootElement.GetProperty("clientVersion").GetString(), "usage version");
            True(!body.RootElement.TryGetProperty("targetDeviceId", out _), "usage cannot select another device");
            True(!body.RootElement.TryGetProperty("lastSeenAt", out _), "usage cannot supply server time");
            return Json(HttpStatusCode.OK, new { device = Device(CurrentDeviceId, "A 機", "active", true,
                clientVersion: "2.6.14 Build 1", lastSeenAt: "2026-10-09T14:00:00Z") });
        });
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var result = await client.ReportUsageAsync(CurrentDeviceId, "2.6.14 Build 1");
        True(result.LastSeenAt is not null, "usage requires confirmed server time");
        await ThrowsAsync<ArgumentException>(() => client.ReportUsageAsync(CurrentDeviceId, "bad\nversion"), "invalid version rejected");
    }

    private static async Task RenameSendsExecutionTimeCredentialAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal("/v1/devices/rename", request.RequestUri?.AbsolutePath, "rename path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter, "rename token");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(OtherDeviceId, body.RootElement.GetProperty("targetDeviceId").GetString(), "rename target");
            Equal("新裝置", body.RootElement.GetProperty("displayName").GetString(), "trimmed name");
            Equal("0001", body.RootElement.GetProperty("employeeNo").GetString(), "rename SUPER_ADMIN");
            Equal("SyntheticPass1", body.RootElement.GetProperty("password").GetString(), "rename execution password");
            return Json(HttpStatusCode.OK, new { device = Device(OtherDeviceId, "新裝置", "active", false) });
        });
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        Equal("新裝置", (await client.RenameAsync(OtherDeviceId, " 新裝置 ", "0001", "SyntheticPass1")).DisplayName, "renamed name");
    }

    private static async Task MetadataMismatchFailsClosedAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Json(HttpStatusCode.OK, new { device = Device(OtherDeviceId, "B 機", "active", true,
            clientVersion: "2.6.14", lastSeenAt: "2026-10-09T14:00:00Z") }));
        handler.Enqueue(_ => Json(HttpStatusCode.OK, new { device = Device(CurrentDeviceId, "A 機", "active", true,
            clientVersion: "2.6.14") }));
        handler.Enqueue(_ => Json(HttpStatusCode.OK, new { device = Device(OtherDeviceId, "Old Name", "active", false) }));
        using var http = new HttpClient(handler);
        var client = new CloudDeviceLifecycleClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        await ThrowsAsync<InvalidDataException>(() => client.ReportUsageAsync(CurrentDeviceId, "2.6.14"), "wrong device usage rejected");
        await ThrowsAsync<InvalidDataException>(() => client.ReportUsageAsync(CurrentDeviceId, "2.6.14"), "missing last-use time rejected");
        await ThrowsAsync<InvalidDataException>(() => client.RenameAsync(OtherDeviceId, "New Name", "0001", "SyntheticPass1"), "wrong renamed name rejected");
    }

    private static object Device(
        string id,
        string name,
        string status,
        bool current,
        string revokedAt = "",
        string clientVersion = "2.6.9",
        string lastSeenAt = "") => new
        {
            deviceId = id,
            displayName = name,
            status,
            clientVersion,
            pairedAt = "2026-09-28T12:00:00+00:00",
            lastSeenAt,
            createdAt = "2026-09-28T12:00:00+00:00",
            revokedAt,
            current,
        };

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) =>
        Json(status, new { error = new { code, message } });

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
}
