using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudAllowanceVoidClientTests
{
    private const string Token = "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ItemId = "wi_allowance_void_synthetic_01";
    private const string AllowanceNumber = "3821061800001";

    public static async Task RunAsync()
    {
        using var probe = new HttpClient(new QueueHandler());
        Reject<ArgumentException>(() => new CloudAllowanceVoidClient(
            probe,new Uri("http://cloud.example.test/"),Token));
        Reject<ArgumentException>(() => new CloudAllowanceVoidClient(
            probe,new Uri("https://cloud.example.test/?token=secret"),Token));
        Reject<ArgumentException>(() => new CloudAllowanceVoidClient(
            probe,new Uri("https://user:password@cloud.example.test/"),Token));
        Reject<ArgumentException>(() => new CloudAllowanceVoidClient(
            probe,new Uri("https://cloud.example.test/"),"bad_token"));

        var handler = new QueueHandler();
        using var http = new HttpClient(handler);
        var client = new CloudAllowanceVoidClient(http,new Uri("https://cloud.example.test/"),Token);
        var draft = new CloudAllowanceVoidDraft(ItemId,AllowanceNumber,
            "AB12345678","Synthetic return");
        handler.Enqueue(req =>
        {
            Check(req,"create");
            using var body=ReadBody(req);
            Equal(ItemId,body.RootElement.GetProperty("itemId").GetString(),"caller-owned Work Item");
            Equal(AllowanceNumber,body.RootElement.GetProperty("allowanceNumber").GetString(),"historic number");
            Equal("AB12345678",body.RootElement.GetProperty("originalInvoiceNumber").GetString(),"source invoice");
            Equal("Synthetic return",body.RootElement.GetProperty("reason").GetString(),"reason");
            True(!body.RootElement.TryGetProperty("workspaceId",out _),"Workspace is server-owned");
            True(!body.RootElement.TryGetProperty("sellerInvoice",out _),"seller is server-owned");
            True(!body.RootElement.TryGetProperty("appKey",out _),"company key never leaves server");
            True(!body.RootElement.TryGetProperty("environment",out _),"environment is server-owned");
            return Json(HttpStatusCode.Created,new{ok=true,itemId=ItemId,status="open",revision=1});
        });
        handler.Enqueue(req =>
        {
            Check(req,"get");
            return Json(HttpStatusCode.OK,new{ok=true,application=Summary()});
        });
        handler.Enqueue(req =>
        {
            Check(req,"list");
            using var body=ReadBody(req);
            Equal(20,body.RootElement.GetProperty("limit").GetInt32(),"list page size");
            return Json(HttpStatusCode.OK,new{ok=true,applications=new[]{Summary()},
                nextCursor=(object?)null});
        });
        handler.Enqueue(req =>
        {
            Check(req,"approve");
            using var body=ReadBody(req);
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"approved revision");
            True(!body.RootElement.TryGetProperty("officialApproved",out _),"no caller approval");
            return Json(HttpStatusCode.Created,new{ok=true,
                approvalId="ava_synthetic_1",itemId=ItemId,revision=2,
                validUntilUtc="2026-10-15T02:15:00Z",canIssueNow=false});
        });
        handler.Enqueue(req =>
        {
            Check(req,"send-once");
            using var body=ReadBody(req);
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"one-shot revision");
            True(!body.RootElement.TryGetProperty("allowanceNumber",out _),"no client supplied g0501 number");
            True(!body.RootElement.TryGetProperty("appKey",out _),"no client supplied key");
            return Json(HttpStatusCode.Accepted,new{ok=true,itemId=ItemId,revision=2,
                allowanceNumber=AllowanceNumber,outcome="accepted_unconfirmed",
                officialCode=0,canAutoRetry=false,canAutoClose=false,mustReadback=true});
        });
        handler.Enqueue(req =>
        {
            Check(req,"status-readback");
            return Json(HttpStatusCode.OK,new{ok=true,readback=new{
                itemId=ItemId,revision=2,allowanceNumber=AllowanceNumber,
                officialType="D0501",officialUploadStatus=99,
                officialCancelled=true,definitiveOpen=false,
                checkedAtUtc="2026-10-15T02:20:00Z",evidenceSha256=new string('a',64),
                canAutoRetry=false,canAutoClose=false
            }});
        });
        handler.Enqueue(req =>
        {
            Check(req,"close");
            return Json(HttpStatusCode.OK,new{ok=true,itemId=ItemId,
                previousRevision=2,revision=3,status="closed",
                allowanceNumber=AllowanceNumber,officialConfirmed=true});
        });
        handler.Enqueue(req =>
        {
            Check(req,"send-once");
            return Json(HttpStatusCode.Accepted,new{ok=true,itemId=ItemId,revision=2,
                allowanceNumber=AllowanceNumber,outcome="accepted_unconfirmed",
                officialCode=0,canAutoRetry=true,canAutoClose=false,mustReadback=true});
        });
        handler.Enqueue(req =>
        {
            Check(req,"status-readback");
            return Json(HttpStatusCode.OK,new{ok=true,readback=new{
                itemId=ItemId,revision=2,allowanceNumber=AllowanceNumber,
                officialType="D0501",officialUploadStatus=99,
                officialCancelled=false,definitiveOpen=true,
                checkedAtUtc="2026-10-15T02:20:00Z",evidenceSha256=new string('a',64),
                canAutoRetry=false,canAutoClose=false
            }});
        });
        handler.Enqueue(req =>
        {
            Check(req,"get");
            return Json(HttpStatusCode.NotFound,new {ok=false,error=new{code="NOT_FOUND"}});
        });
        handler.Enqueue(req =>
        {
            Check(req,"send-once");
            throw new HttpRequestException("synthetic connection was lost");
        });

        Equal(ItemId,await client.CreateAsync(draft,"0001","SyntheticPass12"),"create");
        var source=await client.GetAsync(ItemId,"0001","SyntheticPass12");
        Equal(ItemId,source?.ItemId,"readback");
        Equal(AllowanceNumber,source?.AllowanceNumber,"official historical number");
        var page=await client.ListForManagerAsync(20,null,"0001","SyntheticPass12");
        Equal(1,page.Applications.Count,"manager list");
        Equal((CloudAllowanceVoidCursor?)null,page.NextCursor,"last page");
        var approval=await client.ApproveAsync(ItemId,2,"0001","SyntheticPass12");
        Equal(false,approval.CanIssueNow,"approval is not an official send");
        var sent=await client.SendOnceAsync(ItemId,2,"0001","SyntheticPass12");
        Equal("accepted_unconfirmed",sent.Outcome,"g0501 accepted is not completed");
        Equal(false,sent.CanAutoRetry,"never auto-resend");
        Equal(false,sent.CanAutoClose,"never auto-close");
        Equal(true,sent.MustReadback,"official readback required");
        var official=await client.ReadbackAsync(ItemId,2,"0001","SyntheticPass12");
        Equal(true,official.OfficialCancelled,"D0501/99 is official cancellation");
        Equal(false,official.CanAutoClose,"still manager-protected");
        var closed=await client.CloseVerifiedAsync(ItemId,2,"0001","SyntheticPass12");
        Equal(true,closed.OfficialConfirmed,"official checked before closing");
        await RejectAsync<InvalidDataException>(() =>
            client.SendOnceAsync(ItemId,2,"0001","SyntheticPass12"));
        await RejectAsync<InvalidDataException>(() =>
            client.ReadbackAsync(ItemId,2,"0001","SyntheticPass12"));
        Equal((CloudAllowanceVoidSummary?)null,await client.GetAsync(ItemId,"0001","SyntheticPass12"),"404");
        await RejectAsync<HttpRequestException>(() =>
            client.SendOnceAsync(ItemId,2,"0001","SyntheticPass12"));
        Equal(0,handler.PendingCount,"only one HTTP attempt per explicit invocation");
        var before=handler.RequestCount;
        await RejectAsync<ArgumentException>(() =>
            client.CreateAsync(draft with {AllowanceNumber=" BAD"}, "0001","SyntheticPass12"));
        await RejectAsync<ArgumentException>(() =>
            client.CreateAsync(draft with {Reason="invalid\nreason"}, "0001","SyntheticPass12"));
        await RejectAsync<ArgumentOutOfRangeException>(() =>
            client.SendOnceAsync(ItemId,0,"0001","SyntheticPass12"));
        await RejectAsync<ArgumentOutOfRangeException>(() =>
            client.CloseVerifiedAsync(ItemId,int.MaxValue,"0001","SyntheticPass12"));
        await RejectAsync<ArgumentException>(() =>
            client.ApproveAsync(ItemId,2,"bad employee","SyntheticPass12"));
        Equal(before,handler.RequestCount,"invalid requests never hit Cloud");
        True(CloudAllowanceVoidClient.CreateItemId().StartsWith("wi_"),"stable client-generated ID");
    }

    private static object Summary()=>new{
        itemId=ItemId,environment="test",allowanceNumber=AllowanceNumber,
        originalInvoiceNumber="AB12345678",reason="Synthetic return",
        applicantEmployeeId="cyid_user",sellerInvoice="12345678",
        createdAt="2026-10-15T01:00:00Z",revision=2,status="review"
    };
    private static JsonDocument ReadBody(HttpRequestMessage req)=>
        JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    private static void Check(HttpRequestMessage req,string action)
    {
        Equal(HttpMethod.Post,req.Method,"POST only");
        Equal("/v1/allowance-void-applications/"+action,
            req.RequestUri?.AbsolutePath,"route");
        Equal("",req.RequestUri?.Query,"no credentials in URL");
        Equal("Bearer",req.Headers.Authorization?.Scheme,"CYID Device auth");
        Equal(Token,req.Headers.Authorization?.Parameter,"Device token");
        using var body=ReadBody(req);
        Equal("0001",body.RootElement.GetProperty("employeeNo").GetString(),"employee");
        Equal("SyntheticPass12",body.RootElement.GetProperty("password").GetString(),"credentials");
    }
    private static HttpResponseMessage Json(HttpStatusCode status,object data)=>
        new(status){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
    private static void True(bool ok,string why)
    {
        if(!ok)throw new InvalidOperationException(why);
    }
    private static void Equal<T>(T expected,T actual,string why)
    {
        if(!EqualityComparer<T>.Default.Equals(expected,actual))
            throw new InvalidOperationException($"{why}: expected '{expected}', got '{actual}'");
    }
    private static void Reject<T>(Action fn) where T:Exception
    {
        try{fn();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task> fn) where T:Exception
    {
        try{await fn();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
    private sealed class QueueHandler:HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage,HttpResponseMessage>> callbacks=new();
        public int PendingCount=>callbacks.Count;
        public int RequestCount{get;private set;}
        public void Enqueue(Func<HttpRequestMessage,HttpResponseMessage> response)=>
            callbacks.Enqueue(response);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if(callbacks.Count==0)
                throw new InvalidOperationException("Unexpected extra HTTP request");
            return Task.FromResult(callbacks.Dequeue()(req));
        }
    }
}
