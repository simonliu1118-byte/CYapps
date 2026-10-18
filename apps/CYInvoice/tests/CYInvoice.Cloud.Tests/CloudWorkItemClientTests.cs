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
        await ListPagesAndRejectUnsafeRepliesAsync();
        await UploadIssueReportsAreUnverifiedAsync();
        await UploadIssueManualArchiveAsync();
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
        Equal(0, handler.PendingCount, "3.2 ALLOWANCE must not enter the V3.1 work-item transport");
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

    private static async Task UploadIssueReportsAreUnverifiedAsync()
    {
        var handler=new QueueHandler();
        var report=new CloudUploadIssueReport("wi_upload_test_1234","test",
            new string('c',64),"query_failed");
        handler.Enqueue(request=>{
            ValidateRequest(request,"report-upload-issue");
            using var json=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var data=json.RootElement;
            Equal(report.ItemId,data.GetProperty("itemId").GetString(),"source-bound ID");
            Equal("query_failed",data.GetProperty("issueCode").GetString(),"only local issue code");
            Equal(report.ReferenceHash,data.GetProperty("referenceHash").GetString(),"source digest");
            True(!data.TryGetProperty("invoiceNumber",out _)&&
                 !data.TryGetProperty("orderId",out _)&&
                 !data.TryGetProperty("message",out _)&&
                 !data.TryGetProperty("workspaceId",out _),"privacy and scope");
            return Json(HttpStatusCode.OK,new{ok=true,itemId=report.ItemId,
                reported=true,officialVerified=false});
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"report-upload-issue");
            return Json(HttpStatusCode.Conflict,new{ok=false,
                error=new{code="WORK_ITEM_CONFLICT"}});
        });
        handler.Enqueue(_=>Json(HttpStatusCode.OK,new{ok=true,
            itemId=report.ItemId,reported=true,officialVerified=true}));
        using var http=new HttpClient(handler);
        var client=new CloudWorkItemClient(http,new Uri("https://cloud.example.test/"),
            Token,Workspace);
        await ThrowsAsync<ArgumentException>(()=>client.ReportUploadIssueAsync(
            report with {IssueCode="invoice_success"},"0001","synthetic-pass"));
        await client.ReportUploadIssueAsync(report,"0001","synthetic-pass");
        await ThrowsAsync<CloudApiException>(()=>
            client.ReportUploadIssueAsync(report,"0001","synthetic-pass"),
            "WORK_ITEM_CONFLICT");
        await ThrowsAsync<InvalidDataException>(()=>
            client.ReportUploadIssueAsync(report,"0001","synthetic-pass"));
        Equal(0,handler.PendingCount,"no hidden resubmit after conflict or forged reply");
    }

    private static async Task UploadIssueManualArchiveAsync()
    {
        var handler=new QueueHandler();
        var id="wi_upload_test_1234";
        var hash=new string('c',64);
        object Row(string status,int revision)=>new{
            ok=true,item=new {
                work_item_id=id,workspace_id=Workspace,environment="test",
                kind="UPLOAD_ISSUE",reference_hash=hash,status,revision,
                issue_code="query_failed"
            }
        };
        handler.Enqueue(request=>{
            ValidateRequest(request,"transition");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            True(!body.RootElement.TryGetProperty("resolutionCode",out _),
                "review is not an archive");
            return Json(HttpStatusCode.OK,Row("review",2));
        });
        handler.Enqueue(request=>{
            ValidateRequest(request,"transition");
            using var body=JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal("needs_followup",body.RootElement.GetProperty("resolutionCode").GetString(),
                "manager closure reason");
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"CAS revision");
            return Json(HttpStatusCode.OK,Row("closed",3));
        });
        using var http=new HttpClient(handler);
        var client=new CloudWorkItemClient(http,new Uri("https://cloud.example.test/"),
            Token,Workspace);
        var open=new CloudWorkItem(id,Workspace,"test","UPLOAD_ISSUE",hash,"open",1);
        await ThrowsAsync<ArgumentException>(()=>client.TransitionAsync(open,
            "closed","0001","synthetic-pass",resolutionCode:"needs_followup"));
        var review=await client.TransitionAsync(open,"review","0001","synthetic-pass");
        await ThrowsAsync<ArgumentException>(()=>client.TransitionAsync(
            review,"closed","0001","synthetic-pass"));
        var closed=await client.TransitionAsync(review,"closed","0001",
            "synthetic-pass",resolutionCode:"needs_followup");
        Equal(3,closed.Revision,"archived revision");
        Equal("closed",closed.Status,"archived status");
        Equal(0,handler.PendingCount,"no hidden retries or extra operations");
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
