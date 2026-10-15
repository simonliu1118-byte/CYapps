using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudWorkItemClientTests
{
    private const string Token = "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Workspace = "ws_synthetic";
    private const string ItemId = "wi_synthetic_request_01";
    private static readonly string ReferenceHash = new('b', 64);

    public static async Task RunAsync()
    {
        RejectInvalidTransport();
        await RejectUnfrozenAllowanceAsync();
        await CreateReadAndTransitionAsync();
        await MissingAndConflictingWorkAsync();
        await RejectUntrustedResponseAsync();
    }

    private static void RejectInvalidTransport()
    {
        using var http = new HttpClient(new QueueHandler());
        Throws<ArgumentException>(() => new CloudWorkItemClient(
            http, new Uri("http://cloud.example.test/"), Token, Workspace));
        Throws<ArgumentException>(() => new CloudWorkItemClient(
            http, new Uri("https://cloud.example.test/?token=secret"), Token, Workspace));
        Throws<ArgumentException>(() => new CloudWorkItemClient(
            http, new Uri("https://cloud.example.test/"), "bad-token", Workspace));
    }

    private static async Task RejectUnfrozenAllowanceAsync()
    {
        var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        await ThrowsAsync<ArgumentException>(() => client.CreateAsync(
            new CloudWorkItemDraft(ItemId, "test", "ALLOWANCE", ReferenceHash),
            "0001", "SyntheticPass12"));
        Equal(0, handler.PendingCount, "unfrozen ALLOWANCE must use dedicated application API");
    }

    private static async Task CreateReadAndTransitionAsync()
    {
        var handler = new QueueHandler();
        var draft = new CloudWorkItemDraft(ItemId, "test", "VOID", ReferenceHash);
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "create");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(ItemId, body.RootElement.GetProperty("itemId").GetString(), "create id");
            Equal("test", body.RootElement.GetProperty("environment").GetString(), "create environment");
            Equal("VOID", body.RootElement.GetProperty("kind").GetString(), "create kind");
            Equal(ReferenceHash, body.RootElement.GetProperty("referenceHash").GetString(), "create reference hash");
            Equal("0001", body.RootElement.GetProperty("employeeNo").GetString(), "create manager");
            Equal("synthetic-pass", body.RootElement.GetProperty("password").GetString(), "create credential");
            return Json(HttpStatusCode.OK, Result("open", 1));
        });
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "get");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(ItemId, body.RootElement.GetProperty("itemId").GetString(), "get id");
            return Json(HttpStatusCode.OK, Result("open", 1));
        });
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "transition");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal("open", body.RootElement.GetProperty("from").GetString(), "transition from");
            Equal("review", body.RootElement.GetProperty("to").GetString(), "transition to");
            Equal(1, body.RootElement.GetProperty("expectedRevision").GetInt32(), "CAS revision");
            return Json(HttpStatusCode.OK, Result("review", 2));
        });

        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        var created = await client.CreateAsync(draft, "0001", "synthetic-pass");
        Equal("open", created.Status, "created status");
        Equal(1, created.Revision, "created revision");
        var loaded = await client.GetAsync(ItemId, "0001", "synthetic-pass");
        Equal(created, loaded, "get result");
        var moved = await client.TransitionAsync(created, "review", "0001", "synthetic-pass");
        Equal(2, moved.Revision, "transition revision");
        Equal("review", moved.Status, "transition result");
        await ThrowsAsync<ArgumentException>(async () => { await client.TransitionAsync(created, "closed", "0001", "synthetic-pass"); });
        Equal(0, handler.PendingCount, "all script requests consumed");
    }

    private static async Task MissingAndConflictingWorkAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "get");
            return Json(HttpStatusCode.NotFound, new { ok = false,
                error = new { code = "NOT_FOUND" } });
        });
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "create");
            return Json(HttpStatusCode.Conflict, new { ok = false,
                error = new { code = "WORK_ITEM_CONFLICT" } });
        });
        handler.Enqueue(request =>
        {
            ValidateRequest(request, "transition");
            return Json(HttpStatusCode.ServiceUnavailable, new { ok = false,
                error = new { code = "WORK_ITEM_RESULT_UNKNOWN" } });
        });
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        var missing = await client.GetAsync(ItemId, "0001", "synthetic-pass");
        True(missing is null, "only explicit not-found is null");
        var draft = new CloudWorkItemDraft(ItemId, "test", "VOID", ReferenceHash);
        await ThrowsAsync<CloudApiException>(async () =>
        {
            await client.CreateAsync(draft, "0001", "synthetic-pass");
        }, "WORK_ITEM_CONFLICT");
        await ThrowsAsync<CloudApiException>(async () =>
        {
            await client.TransitionAsync(new CloudWorkItem(ItemId, Workspace, "test",
                "VOID", ReferenceHash, "open", 1), "review", "0001", "synthetic-pass");
        }, "WORK_ITEM_RESULT_UNKNOWN");
        Equal(0, handler.PendingCount, "no hidden retries on conflict or unknown");
    }

    private static async Task RejectUntrustedResponseAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Json(HttpStatusCode.OK, Result("open", 1, workspace: "ws_other")));
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        await ThrowsAsync<InvalidDataException>(async () =>
        {
            await client.GetAsync(ItemId, "0001", "synthetic-pass");
        });
    }

    private static void ValidateRequest(HttpRequestMessage request, string action)
    {
        Equal(HttpMethod.Post, request.Method, "method");
        Equal("/v1/work-items/" + action, request.RequestUri!.AbsolutePath, "path");
        Equal(string.Empty, request.RequestUri.Query, "password is never a query parameter");
        Equal("Bearer", request.Headers.Authorization?.Scheme, "auth scheme");
        Equal(Token, request.Headers.Authorization?.Parameter, "device bearer");
    }

    private static object Result(string status, int revision, string workspace = Workspace) => new
    {
        ok = true, item = new {
            work_item_id = ItemId, workspace_id = workspace, environment = "test",
            kind = "VOID", reference_hash = ReferenceHash, status, revision
        }
    };

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static async Task ThrowsAsync<T>(Func<Task> action, string expectedCode = "")
        where T : Exception
    {
        try { await action(); }
        catch (T error)
        {
            if (error is CloudApiException api && expectedCode.Length != 0 &&
                api.Code != expectedCode)
                throw new InvalidOperationException("Wrong Cloud error code.");
            return;
        }
        throw new InvalidOperationException("Expected exception " + typeof(T).Name);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected exception " + typeof(T).Name);
    }

    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(context + " did not match.");
    }

    private static void True(bool condition, string context)
    {
        if (!condition) throw new InvalidOperationException(context);
    }
}
