using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudAllowanceClientTests
{
    private const string Token="cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Id="application_synthetic_01";
    private const string Host="https://example.invalid/";
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage,HttpResponseMessage>> expected=new();
        public int Calls {get;private set;}
        public void Add(Func<HttpRequestMessage,HttpResponseMessage> f)=>expected.Enqueue(f);
        public int Remaining=>expected.Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if(expected.Count==0)throw new InvalidOperationException("Unexpected extra POST");
            return Task.FromResult(expected.Dequeue()(request));
        }
    }
    private static HttpResponseMessage Json(HttpStatusCode code,object body)=>
        new(code){Content=new StringContent(JsonSerializer.Serialize(body),
            Encoding.UTF8,"application/json")};
    private static void Check(HttpRequestMessage r,string name)
    {
        if(r.Method!=HttpMethod.Post||
           r.RequestUri?.AbsoluteUri!=Host+"v1/allowances/"+name||
           r.Headers.Authorization?.Scheme!="Bearer"||
           r.Headers.Authorization.Parameter!=Token)
            throw new InvalidOperationException("Cloud allowance route or Device token invalid.");
        using var obj=JsonDocument.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        if(obj.RootElement.GetProperty("employeeNo").GetString()!="0001"||
           obj.RootElement.GetProperty("password").GetString()!="fake-password"||
           obj.RootElement.TryGetProperty("workspaceId",out _))
            throw new InvalidOperationException("Client must use CYID credentials without workspace override.");
    }
    private static void Equal<T>(T expected,T actual)
    {
        if(!EqualityComparer<T>.Default.Equals(expected,actual))
            throw new InvalidOperationException("Cloud allowance contract mismatch.");
    }
    private static async Task Fail<T>(Func<Task> op) where T:Exception
    {
        try{await op();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
    public static async Task RunAsync()
    {
        using(var http=new HttpClient(new FakeHandler()))
        {
            await Fail<ArgumentException>(async()=>{
                _=new CloudAllowanceClient(http,
                    new Uri("http://example.invalid"),Token);
                await Task.CompletedTask;
            });
            await Fail<ArgumentException>(async()=>{
                _=new CloudAllowanceClient(http,
                    new Uri("https://example.invalid/?password=bad"),Token);
                await Task.CompletedTask;
            });
        }
        var fake=new FakeHandler();
        var template=JsonSerializer.Serialize(new {AllowanceNumber="",AllowanceType=2,
          AllowanceDate="20261019",TotalAmount=50,TaxAmount=0,
          BuyerIdentifier="0000000000",BuyerName="Synthetic",
          ProductItem=new[]{new{OriginalInvoiceNumber="AB12345678",
           OriginalInvoiceDate=20261019,OriginalDescription="Synthetic",
           Quantity=1,UnitPrice=50,Amount=50,Tax=0,TaxType=1}}});
        fake.Add(r=>{
            Check(r,"create");
            using var d=JsonDocument.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(Id,d.RootElement.GetProperty("applicationId").GetString());
            Equal("test",d.RootElement.GetProperty("environment").GetString());
            Equal(template,d.RootElement.GetProperty("unnumberedTemplateJson").GetString());
            Equal("Synthetic reason",d.RootElement.GetProperty("reason").GetString());
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="open",revision=1,officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"get");
            return Json(HttpStatusCode.OK,new{ok=true,application=new{
                applicationId=Id,environment="test",status="open",
                revision=1,requestedGross=50,allowanceDay="20261019",
                reason="Synthetic reason"},officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"list");
            return Json(HttpStatusCode.OK,new{ok=true,items=new[]{
                new{applicationId=Id,status="review",revision=2,
                    requestedGross="50",environment="test",
                    updatedAt="2026-10-19T08:00:00.000Z"}}});
        });
        fake.Add(r=>{
            Check(r,"review");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="review",revision=2,officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"preview");
            return Json(HttpStatusCode.OK,new{ok=true,preview=new{
                applicationId=Id,revision=2,originalInvoiceNumber="AB12345678",
                requestedGross=50,officialUploadStatus=99,
                officialSummaryComplete=true,officialAllowanceCount=0,
                officialMutationPending=false,officialInvoiceCancelled=false,
                canApprove=false,canAllocateNumber=false,
                requiresIndependentOfficialReconciliation=true}});
        });
        fake.Add(r=>{
            Check(r,"approve");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=3,validUntilUtc="2026-10-19T08:05:00.000Z",
                officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"reserve");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                ledgerId="al_11111111-1111-4111-8111-111111111111",
                allowanceNumber="A20261019001",status="reserved",
                maySendWithoutFreshOfficialCheck=false});
        });
        fake.Add(r=>{
            Check(r,"status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=3,allowanceNumber="A20261019001",
                ledgerStatus="reserved",officiallyConfirmed=false});
        });
        fake.Add(r=>{
            Check(r,"attempt");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                ledgerId="al_11111111-1111-4111-8111-111111111111",
                allowanceNumber="A20261019001",ledgerStatus="reserved",
                recordedExactReadback=false,manuallyUnattributed=false,
                proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false});
        });
        fake.Add(r=>{
            Check(r,"send-once");
            using var d=JsonDocument.Parse(r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(true,d.RootElement.GetProperty("confirmOnce").GetBoolean());
            Equal(3,d.RootElement.GetProperty("expectedRevision").GetInt32());
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                allowanceNumber="A20261019001",ledgerStatus="sending",
                issueKind="accepted_unconfirmed",
                manualDisposition="full_match_unattributed",
                recordedReadback=true,manualReviewRequired=true,
                proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false});
        });
        fake.Add(r=>{
            Check(r,"attempt");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                ledgerId="al_11111111-1111-4111-8111-111111111111",
                allowanceNumber="A20261019001",ledgerStatus="sending",
                recordedExactReadback=true,manuallyUnattributed=true,
                proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false});
        });
        fake.Add(r=>{
            Check(r,"send-once");
            return Json(HttpStatusCode.Conflict,new{ok=false,
                error=new{code="ALLOWANCE_SEND_READBACK_ONLY"}});
        });
        fake.Add(r=>{
            Check(r,"manual-review");
            using var d=JsonDocument.Parse(
                r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(true,d.RootElement.GetProperty("confirmReview").GetBoolean());
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                reviewId="synthetic_review_001",
                ledgerId="al_11111111-1111-4111-8111-111111111111",
                ledgerStatus="sending",disposition="full_match_unattributed",
                proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false});
        });
        fake.Add(r=>{
            Check(r,"manual-history");
            var review=new{reviewId="synthetic_review_001",
                ledgerId="al_11111111-1111-4111-8111-111111111111",
                ledgerStatus="sending",disposition="full_match_unattributed",
                proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false};
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                items=new[]{review},proofOfThisPost=false,officiallyConfirmed=false,
                safeToAutoRetry=false,canAllocateAnotherNumber=false});
        });
        fake.Add(r=>{
            Check(r,"status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=3,allowanceNumber="A20261019001",
                ledgerStatus="sending",officiallyConfirmed=true});
        });
        fake.Add(r=>{
            Check(r,"create");
            return Json(HttpStatusCode.Conflict,new{ok=false,
                error=new{code="ALLOWANCE_OPERATION_UNCONFIRMED"}});
        });
        using var httpClient=new HttpClient(fake);
        var api=new CloudAllowanceClient(httpClient,new Uri(Host),Token);
        var draft=new CloudAllowanceDraft(Id,"test","AB12345678","20261019",50,template,"Synthetic reason");
        var created=await api.CreateAsync(draft,"0001","fake-password");
        Equal("open",created.Status);
        Equal(1,created.Revision);
        var detail=await api.GetAsync(Id,"0001","fake-password");
        Equal("Synthetic reason",detail.Reason);
        Equal(50L,detail.RequestedGross);
        var page=await api.ListAsync("0001","fake-password");
        Equal(1,page.Count);
        Equal(Id,page[0].ApplicationId);
        Equal("review",page[0].Status);
        Equal("review",(await api.ReviewAsync(Id,1,"0001","fake-password")).Status);
        var preview=await api.PreviewAsync(Id,2,"0001","fake-password");
        Equal("AB12345678",preview.OriginalInvoiceNumber);
        Equal(0,preview.OfficialAllowanceCount);
        Equal("approved",(await api.ApproveAsync(Id,2,"0001","fake-password")).Status);
        var reserved=await api.ReserveAsync(Id,3,"0001","fake-password");
        Equal("A20261019001",reserved.AllowanceNumber);
        var status=await api.StatusAsync(Id,"0001","fake-password");
        Equal("reserved",status.LedgerStatus);
        var attempt=await api.InspectAttemptAsync(Id,3,"0001","fake-password");
        Equal("reserved",attempt.LedgerStatus);
        var sent=await api.SendOnceAsync(Id,3,"0001","fake-password");
        Equal("sending",sent.LedgerStatus);
        Equal("full_match_unattributed",sent.ManualDisposition);
        Equal(true,sent.RecordedReadback);
        var after=await api.InspectAttemptAsync(Id,3,"0001","fake-password");
        Equal("sending",after.LedgerStatus);
        Equal(true,after.ManuallyUnattributed);
        await Fail<CloudApiException>(()=>api.SendOnceAsync(Id,3,"0001","fake-password"));
        var manual=await api.RecordManualReviewAsync(Id,3,"synthetic_review_001",
            "full_match_unattributed","0001","fake-password");
        Equal("sending",manual.LedgerStatus);
        var history=await api.ManualHistoryAsync(Id,3,"0001","fake-password");
        Equal(1,history.Count);
        Equal("synthetic_review_001",history[0].ReviewId);
        await Fail<InvalidDataException>(()=>api.StatusAsync(Id,"0001","fake-password"));
        await Fail<CloudApiException>(()=>api.CreateAsync(draft,"0001","fake-password"));
        Equal(16,fake.Calls);
        Equal(0,fake.Remaining);
        // Invalid IDs and data never reach network; failure never retries.
        await Fail<ArgumentException>(()=>api.ApproveAsync(Id,0,"0001","fake-password"));
        await Fail<ArgumentException>(()=>api.CreateAsync(draft with{
           Environment="evil"},"0001","fake-password"));
        Equal(16,fake.Calls);
    }

    public static async Task RunVoidAsync()
    {
        // Reopening the same draft, or entering its original number on a
        // different CYID Device, must recover the SAME readback ID.
        var stable=CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_synthetic","test","ALW-001");
        Equal(stable,CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_synthetic","test","ALW-001"));
        Equal(69,stable.Length);
        Equal(true,stable.StartsWith("void_",StringComparison.Ordinal));
        Equal(false,stable.Contains("ALW-001",StringComparison.Ordinal));
        Equal(false,stable==CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_synthetic","production","ALW-001"));
        Equal(false,stable==CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_other","test","ALW-001"));
        Equal(false,stable==CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_synthetic","test","ALW-002"));
        var rejected=false;
        try{CloudAllowanceClient.OriginalVoidApplicationId("ws_synthetic","test","bad");}
        catch(ArgumentException){rejected=true;}
        Equal(true,rejected);
        var fake=new FakeHandler();
        fake.Add(r=>{
            Check(r,"void-create");
            using var payload=JsonDocument.Parse(r.Content!.ReadAsStringAsync()
                .GetAwaiter().GetResult());
            Equal(Id,payload.RootElement.GetProperty("applicationId").GetString());
            Equal("ALW-001",payload.RootElement.GetProperty("allowanceNumber").GetString());
            Equal("AB12345678",payload.RootElement.GetProperty("originalInvoiceNumber").GetString());
            if(payload.RootElement.TryGetProperty("workspaceId",out _) ||
               payload.RootElement.TryGetProperty("confirmOnce",out _))
                throw new InvalidOperationException("Client must not create send authority.");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="open",revision=1,claimed=false,readbackOnly=false,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"void-status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=2,claimed=true,readbackOnly=true,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"void-status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=2,claimed=true,readbackOnly=true,
                officiallyConfirmed=true,officialActionPerformed=false});
        });
        fake.Add(r=>{
            Check(r,"void-create");
            return Json(HttpStatusCode.ServiceUnavailable,new{
                ok=false,error=new{code="VOID_SCHEMA_NOT_READY"}});
        });
        using var http=new HttpClient(fake);
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        var draft=new CloudAllowanceVoidDraft(Id,"test","ALW-001",
            "AB12345678","Synthetic review");
        var created=await api.CreateVoidApplicationAsync(draft,"0001","fake-password");
        Equal("open",created.Status);
        Equal(false,created.Claimed);
        var reviewed=await api.ReadVoidApplicationAsync(Id,"0001","fake-password");
        Equal("approved",reviewed.Status);
        Equal(true,reviewed.Claimed);
        Equal(true,reviewed.ReadbackOnly);
        await Fail<InvalidDataException>(()=>api.ReadVoidApplicationAsync(
            Id,"0001","fake-password"));
        await Fail<CloudApiException>(()=>api.CreateVoidApplicationAsync(
            draft,"0001","fake-password"));
        Equal(4,fake.Calls);
        Equal(0,fake.Remaining);
        await Fail<ArgumentException>(()=>api.CreateVoidApplicationAsync(
            draft with{AllowanceNumber="invalid lower-case"},"0001","fake-password"));
        await Fail<ArgumentException>(()=>api.CreateVoidApplicationAsync(
            draft with{Reason="bad\nreason"},"0001","fake-password"));
        await Fail<ArgumentException>(()=>api.ReadVoidApplicationAsync(
            "x","0001","fake-password"));
        Equal(4,fake.Calls);
    }

    public static async Task RunVoidReconnectAsync()
    {
        var id=CloudAllowanceClient.OriginalVoidApplicationId(
            "ws_synthetic","test","ALW-001");
        var draft=new CloudAllowanceVoidDraft(id,"test","ALW-001",
            "AB12345678","Synthetic void reason");

        // A complete status is sufficient; never issue another create POST.
        var already=new FakeHandler();
        already.Add(req=>{
            Check(req,"void-status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=id,
                status="approved",revision=2,claimed=true,readbackOnly=true,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        using(var http=new HttpClient(already))
        {
            var api=new CloudAllowanceClient(http,new Uri(Host),Token);
            var original=await api.CreateOrReadVoidApplicationAsync(
                draft,"0001","fake-password");
            Equal(id,original.ApplicationId);
            Equal(true,original.ReadbackOnly);
            Equal(1,already.Calls);
        }

        // Explicit authenticated original-ID NOT_FOUND allows one CREATE.
        var initial=new FakeHandler();
        initial.Add(req=>{
            Check(req,"void-status");
            return Json(HttpStatusCode.NotFound,new{
                ok=false,error=new{code="VOID_APPLICATION_NOT_FOUND"}});
        });
        initial.Add(req=>{
            Check(req,"void-create");
            using var item=JsonDocument.Parse(req.Content!.ReadAsStringAsync()
                .GetAwaiter().GetResult());
            Equal(id,item.RootElement.GetProperty("applicationId").GetString());
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=id,
                status="open",revision=1,claimed=false,readbackOnly=false,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        using(var http=new HttpClient(initial))
        {
            var api=new CloudAllowanceClient(http,new Uri(Host),Token);
            var state=await api.CreateOrReadVoidApplicationAsync(
                draft,"0001","fake-password");
            Equal("open",state.Status);
            Equal(2,initial.Calls);
        }

        // A lost CREATE reply may have committed. Reopening by the SAME
        // deterministic ID must read back, never create a second work item.
        var lost=new FakeHandler();
        lost.Add(req=>{
            Check(req,"void-status");
            return Json(HttpStatusCode.NotFound,new{
                ok=false,error=new{code="VOID_APPLICATION_NOT_FOUND"}});
        });
        lost.Add(req=>{
            Check(req,"void-create");
            throw new OperationCanceledException("Synthetic lost D1 ACK");
        });
        lost.Add(req=>{
            Check(req,"void-status");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=id,
                status="open",revision=1,claimed=false,readbackOnly=false,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        using(var http=new HttpClient(lost))
        {
            var api=new CloudAllowanceClient(http,new Uri(Host),Token);
            await Fail<OperationCanceledException>(()=>api.CreateOrReadVoidApplicationAsync(
                draft,"0001","fake-password"));
            var recovered=await api.CreateOrReadVoidApplicationAsync(
                draft,"0001","fake-password");
            Equal(id,recovered.ApplicationId);
            Equal(3,lost.Calls);
        }

        // A connectivity error or uncertain server readback must never
        // trigger the create endpoint (including a device-revocation error).
        foreach(var code in new[]{"ALLOWANCES_NOT_READY","VOID_APPLICATION_AUTH_REQUIRED",
                                  "VOID_APPLICATION_UNCONFIRMED_READBACK_OR_INVESTIGATE"})
        {
            var failed=new FakeHandler();
            failed.Add(req=>{
                Check(req,"void-status");
                return Json(HttpStatusCode.ServiceUnavailable,new{
                    ok=false,error=new{code}});
            });
            using var http=new HttpClient(failed);
            var api=new CloudAllowanceClient(http,new Uri(Host),Token);
            await Fail<CloudApiException>(()=>api.CreateOrReadVoidApplicationAsync(
                draft,"0001","fake-password"));
            Equal(1,failed.Calls);
        }
    }

    public static async Task RunVoidManagerAsync()
    {
        const string hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string official="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var fake=new FakeHandler();
        fake.Add(req=>{
            Check(req,"void-preview");
            using var json=JsonDocument.Parse(req.Content!.ReadAsStringAsync()
                .GetAwaiter().GetResult());
            Equal(Id,json.RootElement.GetProperty("applicationId").GetString());
            Equal(1,json.RootElement.GetProperty("expectedRevision").GetInt32());
            return Json(HttpStatusCode.OK,new{ok=true,preview=new{
                applicationId=Id,revision=1,allowanceNumber="ALW-001",
                originalInvoiceNumber="AB12345678",reason="Synthetic reason",
                requestHash=hash,officialSnapshotHash=official,
                officialCheckedAtUtc="2026-10-21T09:00:00.000Z",
                canApproveWithoutFurtherReview=false,mayPostG0501=false},
                officialActionPerformed=false});
        });
        fake.Add(req=>{
            Check(req,"void-approve");
            using var json=JsonDocument.Parse(req.Content!.ReadAsStringAsync()
                .GetAwaiter().GetResult());
            Equal("作廢 ALW-001",json.RootElement.GetProperty("confirmation").GetString());
            Equal(hash,json.RootElement.GetProperty("requestHash").GetString());
            Equal(official,json.RootElement.GetProperty("expectedOfficialSnapshotHash").GetString());
            Equal(false,json.RootElement.TryGetProperty("confirmOnce",out _));
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=2,
                validUntilUtc="2026-10-21T09:15:00.000Z",
                mayPostG0501=false,requiresFreshOfficialCheck=true,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        fake.Add(req=>{
            Check(req,"void-preview");
            return Json(HttpStatusCode.OK,new{ok=true,preview=new{
                applicationId=Id,revision=1,allowanceNumber="ALW-001",
                originalInvoiceNumber="AB12345678",reason="Synthetic reason",
                requestHash=hash,officialSnapshotHash=official,
                officialCheckedAtUtc="2026-10-21T09:00:00.000Z",
                canApproveWithoutFurtherReview=false,mayPostG0501=true},
                officialActionPerformed=false});
        });
        fake.Add(req=>{
            Check(req,"void-approve");
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
                status="approved",revision=2,
                validUntilUtc="2026-10-21T09:15:00.000Z",
                mayPostG0501=true,requiresFreshOfficialCheck=true,
                officiallyConfirmed=false,officialActionPerformed=false});
        });
        using var http=new HttpClient(fake);
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        var preview=await api.PreviewVoidForManagerAsync(Id,"0001","fake-password");
        Equal("ALW-001",preview.AllowanceNumber);
        Equal("AB12345678",preview.OriginalInvoiceNumber);
        Equal("Synthetic reason",preview.Reason);
        Equal(hash,preview.RequestHash);
        Equal(official,preview.OfficialSnapshotHash);
        await Fail<ArgumentException>(()=>api.ApproveVoidForManagerAsync(
            preview,"作廢 WRONG","0001","fake-password"));
        Equal(1,fake.Calls);
        var grant=await api.ApproveVoidForManagerAsync(preview,
            "作廢 ALW-001","0001","fake-password");
        Equal(2,grant.Revision);
        Equal(Id,grant.ApplicationId);
        await Fail<InvalidDataException>(()=>api.PreviewVoidForManagerAsync(
            Id,"0001","fake-password"));
        await Fail<InvalidDataException>(()=>api.ApproveVoidForManagerAsync(
            preview,"作廢 ALW-001","0001","fake-password"));
        Equal(4,fake.Calls);
        Equal(0,fake.Remaining);
    }
}
