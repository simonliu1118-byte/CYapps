using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudCompanySettingsClientTests
{
    private const string Token =
        "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Endpoint = "https://cyinvoice.example.test/";
    public static async Task RunAsync()
    {
        using (var http = new HttpClient(new QueueHandler()))
        {
            Reject<ArgumentException>(() => new CloudCompanySettingsClient(
                http, new Uri("http://cyinvoice.example.test/"), Token));
            Reject<ArgumentException>(() => new CloudCompanySettingsClient(
                http, new Uri(Endpoint + "?apikey=secret"), Token));
            Reject<ArgumentException>(() => new CloudCompanySettingsClient(
                http, new Uri(Endpoint), "invalid"));
        }

        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Check(request, "get");
            return Json(HttpStatusCode.OK, new { ok = true, company = (object?)null });
        });
        handler.Enqueue(request =>
        {
            Check(request, "update");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var r = body.RootElement;
            Equal("12345678", r.GetProperty("sellerInvoice").GetString(), "seller is exact");
            Equal(0, r.GetProperty("expectedRevision").GetInt32(), "first revision");
            Equal("replace", r.GetProperty("amegoAppKey").GetProperty("action").GetString(), "app key action");
            Equal("SYNTHETIC_NONSECRET_KEY", r.GetProperty("amegoAppKey").GetProperty("value").GetString(), "app key value");
            Equal("keep", r.GetProperty("moExcelPassword").GetProperty("action").GetString(), "MO keep action");
            True(!r.TryGetProperty("workspaceId", out _), "business Workspace is never client-owned");
            True(!r.TryGetProperty("role", out _), "role is CYID-owned");
            return Json(HttpStatusCode.OK, Reply(1, false));
        });
        handler.Enqueue(request =>
        {
            Check(request, "get");
            return Json(HttpStatusCode.OK, Reply(1, false));
        });
        handler.Enqueue(request =>
        {
            Check(request, "update");
            return Json(HttpStatusCode.Conflict,
                new { ok = false, error = new { code = "COMPANY_CONFLICT_READBACK_REQUIRED" } });
        });

        using var service = new HttpClient(handler);
        var client = new CloudCompanySettingsClient(service, new Uri(Endpoint), Token);
        Equal<CloudCompanySettingsStatus?>(null, await client.GetAsync("0001", "SyntheticPass12"), "no card");
        var created = await client.UpdateAsync(
            new CloudCompanySettingsEdit(0, "12345678",
                CloudCompanySecretChange.Replace("SYNTHETIC_NONSECRET_KEY"),
                CloudCompanySecretChange.Keep()), "0001", "SyntheticPass12");
        Equal(1, created.Revision, "new revision");
        True(created.HasAmegoAppKey, "app key configured");
        True(!created.HasMoExcelPassword, "MO remains unset");
        Equal(created, await client.GetAsync("0001", "SyntheticPass12"), "safe readback");
        try
        {
            await client.UpdateAsync(new CloudCompanySettingsEdit(1, "12345678",
                CloudCompanySecretChange.Keep(), CloudCompanySecretChange.Clear()),
                "0001", "SyntheticPass12");
            throw new InvalidOperationException("Expected revision conflict");
        }
        catch (CloudApiException ex) when (ex.Code == "COMPANY_CONFLICT_READBACK_REQUIRED")
        {
            // Caller must GET the current revision; no automatic write retry.
        }
        Equal(0, handler.PendingCount, "all expected requests consumed without extra POST");
        await RejectAsync<ArgumentException>(() => client.UpdateAsync(
            new CloudCompanySettingsEdit(1, "12345678", CloudCompanySecretChange.Replace(""),
                CloudCompanySecretChange.Keep()), "0001", "SyntheticPass12"));
        await RejectAsync<ArgumentException>(() => client.GetAsync("0001", "short"));
        await RejectAsync<ArgumentException>(() => client.UpdateAsync(
            new CloudCompanySettingsEdit(1, "not-invoice",
                CloudCompanySecretChange.Keep(), CloudCompanySecretChange.Keep()),
            "0001", "SyntheticPass12"));
        Equal(0, handler.PendingCount, "invalid arguments must not send requests");
    }

    private static void Check(HttpRequestMessage request, string action)
    {
        Equal(HttpMethod.Post, request.Method, "method");
        Equal("/v1/company-settings/" + action, request.RequestUri?.AbsolutePath, "route");
        Equal(string.Empty, request.RequestUri?.Query, "no query credentials");
        Equal("Bearer", request.Headers.Authorization?.Scheme, "auth scheme");
        Equal(Token, request.Headers.Authorization?.Parameter, "device identity");
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Equal("0001", payload.RootElement.GetProperty("employeeNo").GetString(), "CYID employee");
        Equal("SyntheticPass12", payload.RootElement.GetProperty("password").GetString(), "CYID password");
    }
    private static object Reply(int revision, bool hasMo) => new
    {
        ok = true, company = new {
            sellerInvoice = "12345678", revision, hasAmegoAppKey = true, hasMoExcelPassword = hasMo
        }
    };
    private static HttpResponseMessage Json(HttpStatusCode code, object body) => new(code)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };
    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(context + " mismatch");
    }
    private static void True(bool value, string context)
    {
        if (!value) throw new InvalidOperationException(context);
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
