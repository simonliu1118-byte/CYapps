using System.Net;
using System.Text;
using CYInvoice.Core.Cloud;

internal static class CloudDeviceSelfStatusClientTests
{
    public static async Task RunAsync()
    {
        await ParsesActiveDeviceAndWorkspaceAsync();
        await ParsesRevokedDeviceAndDisabledWorkspaceAsync();
        await RejectsInvalidWorkspaceStateAsync();
    }

    private static async Task ParsesActiveDeviceAndWorkspaceAsync()
    {
        var token = Token('a');
        var handler = new ScriptedHandler(request =>
        {
            Equal(HttpMethod.Get, request.Method, "self-status method");
            Equal("/v1/devices/self-status", request.RequestUri?.AbsolutePath, "self-status path");
            Equal("Bearer", request.Headers.Authorization?.Scheme, "self-status auth scheme");
            Equal(token, request.Headers.Authorization?.Parameter, "self-status bearer token");
            return Json(HttpStatusCode.OK,
                """
                {
                  "ok": true,
                  "deviceId": "dev_test",
                  "workspaceId": "ws_test",
                  "status": "active",
                  "workspaceStatus": "active",
                  "revokedAt": ""
                }
                """);
        });
        using var http = new HttpClient(handler);
        var client = new CloudDeviceSelfStatusClient(http, new Uri("https://cloud.example.test/"), token);

        var result = await client.GetAsync();
        Equal("dev_test", result.DeviceId, "active device id");
        Equal("ws_test", result.WorkspaceId, "active workspace id");
        Equal("active", result.Status, "active device state");
        Equal("active", result.WorkspaceStatus, "active workspace state");
        True(result.RevokedAt is null, "active device must not have revokedAt");
    }

    private static async Task ParsesRevokedDeviceAndDisabledWorkspaceAsync()
    {
        var token = Token('b');
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK,
            """
            {
              "ok": true,
              "deviceId": "dev_retired",
              "workspaceId": "ws_disabled",
              "status": "revoked",
              "workspaceStatus": "disabled",
              "revokedAt": "2026-09-29T04:00:00Z"
            }
            """));
        using var http = new HttpClient(handler);
        var client = new CloudDeviceSelfStatusClient(http, new Uri("https://cloud.example.test/"), token);

        var result = await client.GetAsync();
        Equal("revoked", result.Status, "revoked device state");
        Equal("disabled", result.WorkspaceStatus, "disabled workspace state");
        True(result.RevokedAt is not null, "revoked device must include revokedAt");
    }

    private static async Task RejectsInvalidWorkspaceStateAsync()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK,
            """
            {
              "ok": true,
              "deviceId": "dev_test",
              "workspaceId": "ws_test",
              "status": "active",
              "workspaceStatus": "deleted",
              "revokedAt": ""
            }
            """));
        using var http = new HttpClient(handler);
        var client = new CloudDeviceSelfStatusClient(http, new Uri("https://cloud.example.test/"), Token('c'));

        await ThrowsAsync<InvalidDataException>(() => client.GetAsync(),
            "unknown Workspace lifecycle state must fail closed");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string Token(char value) => $"cydev_{new string(value, 64)}";

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
    }

    private static void True(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
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

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
