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
        await ListPagesAndRejectUnsafeRepliesAsync();
        await DeviceListOmitsCredentialsAsync();
        await RejectUntrustedResponseAsync();
        await IdReadbackIsPureAndRevalidatesErrorsAsync();
        await MutationsAreManualRecordsOnlyAsync();
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

    private static async Task ListPagesAndRejectUnsafeRepliesAsync()
    {
        const string createdFirst="2026-10-16T03:00:00.000Z";
        const string createdSecond="2026-10-16T02:59:59.000Z";
        var secondId="wi_synthetic_request_02";
        object Row(string id,string created)=>new{
            work_item_id=id,environment="test",kind="VOID",issue_code=(string?)null,status="review",
            revision=2,created_at=created,updated_at=created
        };
        var handler=new QueueHandler();
        handler.Enqueue(request=>{
            ValidateRequest(request,"list");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var obj=body.RootElement;
            Equal(1,obj.GetProperty("limit").GetInt32(),"bounded page size");
            Equal("VOID",obj.GetProperty("kind").GetString(),"kind");
            Equal("review",obj.GetProperty("status").GetString(),"status");
            True(!obj.TryGetProperty("cursor",out _),
                "first page must omit absent cursor instead of sending null");
            True(!obj.TryGetProperty("workspaceId",out _),
                "client must not choose another workspace");
            return Json(HttpStatusCode.OK,new{ok=true,
                items=new[]{Row(ItemId,createdFirst)},
                nextCursor=new{createdAt=createdFirst,itemId=ItemId}});
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"list");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(ItemId,body.RootElement.GetProperty("cursor").GetProperty("itemId").GetString(),
                "second page uses original server cursor");
            return Json(HttpStatusCode.OK,new{ok=true,
                items=new[]{Row(secondId,createdSecond)},nextCursor=(object?)null});
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"list");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root=body.RootElement;
            Equal(20,root.GetProperty("limit").GetInt32(),"default page limit");
            Equal("0001",root.GetProperty("employeeNo").GetString(),"CYID manager identity");
            True(!root.TryGetProperty("kind",out _)&&
                 !root.TryGetProperty("status",out _)&&
                 !root.TryGetProperty("cursor",out _),
                 "omitted filters must not arrive as null");
            return Json(HttpStatusCode.OK,new{ok=true,
                items=Array.Empty<object>(),nextCursor=(object?)null});
        });
        handler.Enqueue(_=>Json(HttpStatusCode.OK,new{ok=true,
            items=new object[]{new {
                work_item_id=ItemId,environment="test",kind="VOID",issue_code=(string?)null,
                status="review",revision=2,created_at=createdFirst,
                updated_at=createdFirst,reference_hash=ReferenceHash
            }},nextCursor=(object?)null}));
        handler.Enqueue(_=>Json(HttpStatusCode.OK,new{ok=true,
            items=new[]{Row(ItemId,createdFirst)},nextCursor=new {
                createdAt=createdSecond,itemId=ItemId
            }}));
        using var http=new HttpClient(handler);
        var client=new CloudWorkItemClient(http,new Uri("https://cloud.example.test/"),
            Token,Workspace);
        var page1=await client.ListAsync("0001","synthetic-pass",1,"VOID","review");
        Equal(1,page1.Items.Count,"first page count");
        Equal(ItemId,page1.Items[0].ItemId,"first page ID");
        Equal(2,page1.Items[0].Revision,"first page revision");
        True(page1.NextCursor is not null,"first page must have next cursor");
        var page2=await client.ListAsync("0001","synthetic-pass",1,"VOID","review",
            page1.NextCursor);
        Equal(secondId,page2.Items[0].ItemId,"second page ID");
        True(page2.NextCursor is null,"last page has no cursor");
        await ThrowsAsync<ArgumentException>(()=>client.ListAsync(
            "0001","synthetic-pass",51));
        await ThrowsAsync<ArgumentException>(()=>client.ListAsync(
            "0001","synthetic-pass",1,"UNSUPPORTED_KIND"));
        await ThrowsAsync<ArgumentException>(()=>client.ListAsync(
            "0001","synthetic-pass",1,cursor:new CloudWorkItemCursor(
                "untrusted_time",ItemId)));
        var defaults=await client.ListAsync("0001","synthetic-pass");
        Equal(0,defaults.Items.Count,"default empty pending list");
        await ThrowsAsync<InvalidDataException>(()=>client.ListAsync(
            "0001","synthetic-pass",1));
        await ThrowsAsync<InvalidDataException>(()=>client.ListAsync(
            "0001","synthetic-pass",1));
        Equal(0,handler.PendingCount,"invalid requests never consume scripted responses");
    }

    private static async Task DeviceListOmitsCredentialsAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request => {
            True(request.Headers.Authorization?.Parameter == Token, "device list must retain the original Device token");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            True(!body.RootElement.TryGetProperty("employeeNo", out _) && !body.RootElement.TryGetProperty("password", out _)
                && !body.RootElement.TryGetProperty("workspaceId", out _), "opening the list cannot collect or forward employee credentials");
            return Json(HttpStatusCode.OK, new {ok=true, items=Array.Empty<object>(), nextCursor=(object?)null});
        });
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        Equal(0, (await client.ListAsync()).Items.Count, "authenticated Device reads the privacy-limited pending list");
        Equal(0, handler.PendingCount, "device list never retries or sends an extra login");
    }

    private static async Task RejectUntrustedResponseAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => Json(HttpStatusCode.OK, Result("open", 1, workspace: "ws_other")));
        handler.Enqueue(_ => Json(HttpStatusCode.OK, new { ok=true,
            item=new {work_item_id=ItemId,workspace_id=Workspace,environment="test",
                kind="VOID",reference_hash=ReferenceHash,status="open",revision=1,
                actor_id="synthetic_actor"} }));
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"), Token, Workspace);
        await ThrowsAsync<InvalidDataException>(async () =>
        {
            await client.GetAsync(ItemId, "0001", "synthetic-pass");
        });
        await ThrowsAsync<InvalidDataException>(async () =>
        {
            await client.GetAsync(ItemId, "0001", "synthetic-pass");
        });
        Equal(0,handler.PendingCount,"unexpected actor field rejected, never retried");
    }

    private static async Task IdReadbackIsPureAndRevalidatesErrorsAsync()
    {
        // A mutating reply may be lost after D1 commits. A manual ID readback
        // must use ONLY the manager GET contract, including archived items.
        var handler = new QueueHandler();
        handler.Enqueue(request => {
            ValidateRequest(request, "get");
            using var payload = JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var body = payload.RootElement;
            Equal(ItemId, body.GetProperty("itemId").GetString(), "exact readback ID");
            True(!body.TryGetProperty("expectedRevision", out _) &&
                 !body.TryGetProperty("to", out _) &&
                 !body.TryGetProperty("referenceHash", out _),
                 "readback cannot mutate, replay or expose source");
            return Json(HttpStatusCode.OK, Result("closed", 3));
        });
        handler.Enqueue(request => {
            ValidateRequest(request, "get");
            return Json(HttpStatusCode.NotFound, new { ok=false, error=new { code="NOT_FOUND" } });
        });
        handler.Enqueue(request => {
            ValidateRequest(request, "get");
            return Json(HttpStatusCode.Forbidden, new { ok=false, error=new { code="MANAGER_AUTH_REQUIRED" } });
        });
        using var http = new HttpClient(handler);
        var client = new CloudWorkItemClient(http, new Uri("https://cloud.example.test/"),
            Token, Workspace);
        var archived = await client.GetAsync(ItemId, "0001", "synthetic-pass");
        True(archived is { Status: "closed", Revision: 3 },
            "archived item remains visible by explicit ID readback");
        True(await client.GetAsync(ItemId, "0001", "synthetic-pass") is null,
            "404 means no item, not a successful mutation");
        await ThrowsAsync<CloudApiException>(() =>
            client.GetAsync(ItemId, "0001", "synthetic-pass"),
            "MANAGER_AUTH_REQUIRED");
        Equal(0, handler.PendingCount, "ambiguous result must never trigger automatic retries");
    }

    private static async Task MutationsAreManualRecordsOnlyAsync()
    {
        object Receipt(string id,bool official=false)=>new {
            ok=true,itemId=id,recorded=true,officialVerified=official,providerActionPerformed=false
        };
        var handler=new QueueHandler();
        handler.Enqueue(request=>{
            ValidateRequest(request,"create");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root=body.RootElement;
            Equal("VOID",root.GetProperty("kind").GetString(),"create only paper-void manual item");
            Equal("test",root.GetProperty("environment").GetString(),"environment");
            Equal(ReferenceHash,root.GetProperty("referenceHash").GetString(),"hashed local reference");
            True(!root.TryGetProperty("workspaceId",out _)&&
                 !root.TryGetProperty("invoiceNumber",out _)&&
                 !root.TryGetProperty("recipient",out _),"no business data/Workspace override");
            return Json(HttpStatusCode.OK,Receipt(ItemId));
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"report-upload-issue");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root=body.RootElement;
            Equal("query_failed",root.GetProperty("issueCode").GetString(),"local issue code");
            True(!root.TryGetProperty("officialStatus",out _),"not AMEGO authority");
            return Json(HttpStatusCode.OK,Receipt("wi_upload_report_001"));
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"transition");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root=body.RootElement;
            Equal(2,root.GetProperty("expectedRevision").GetInt32(),"manager CAS");
            Equal("review",root.GetProperty("from").GetString(),"from state");
            Equal("closed",root.GetProperty("to").GetString(),"to state");
            True(!root.TryGetProperty("resolutionCode",out _),"VOID has no upload reason");
            return Json(HttpStatusCode.OK,Receipt(ItemId));
        });
        handler.Enqueue(_=>Json(HttpStatusCode.OK,Receipt(ItemId,official:true)));
        handler.Enqueue(_=>Json(HttpStatusCode.Conflict,new {
            ok=false,error=new{code="WORK_ITEM_CONFLICT"}
        }));
        using var http=new HttpClient(handler);
        var client=new CloudWorkItemClient(http,new Uri("https://cloud.example.test/"),
            Token,Workspace);
        var created=await client.SubmitVoidReviewAsync(
            new CloudVoidReviewRequest(ItemId,"test",ReferenceHash),"0001","synthetic-pass");
        Equal(ItemId,created.ItemId,"successful local receipt");
        var reported=await client.ReportUploadIssueAsync(
            new CloudUploadIssueReport("wi_upload_report_001","test",new string('c',64),"query_failed"),
            "0001","synthetic-pass");
        Equal("wi_upload_report_001",reported.ItemId,"reported local problem");
        var review=new CloudWorkItem(ItemId,Workspace,"test","VOID",ReferenceHash,"review",2);
        var closed=await client.TransitionManualAsync(review,"closed","0001","synthetic-pass");
        Equal(ItemId,closed.ItemId,"manual archive receipt");
        await ThrowsAsync<ArgumentException>(()=>client.SubmitVoidReviewAsync(
            new CloudVoidReviewRequest(ItemId,"test","not-a-hash"),"0001","synthetic-pass"));
        await ThrowsAsync<ArgumentException>(()=>client.ReportUploadIssueAsync(
            new CloudUploadIssueReport("wi_bad_report_001","test",ReferenceHash,"official_success"),
            "0001","synthetic-pass"));
        await ThrowsAsync<ArgumentException>(()=>client.TransitionManualAsync(
            review,"open","0001","synthetic-pass"));
        await ThrowsAsync<ArgumentException>(()=>client.TransitionManualAsync(
            review,"closed","0001","synthetic-pass","checked_official"));
        // Server may record an event, but it must never advertise official success.
        await ThrowsAsync<InvalidDataException>(()=>client.SubmitVoidReviewAsync(
            new CloudVoidReviewRequest(ItemId,"test",ReferenceHash),"0001","synthetic-pass"));
        await ThrowsAsync<CloudApiException>(()=>client.SubmitVoidReviewAsync(
            new CloudVoidReviewRequest(ItemId,"test",ReferenceHash),"0001","synthetic-pass"),
            "WORK_ITEM_CONFLICT");
        Equal(0,handler.PendingCount,"no retry, no extra network requests");
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
