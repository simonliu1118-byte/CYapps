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
        await Fail<InvalidDataException>(()=>api.StatusAsync(Id,"0001","fake-password"));
        await Fail<CloudApiException>(()=>api.CreateAsync(draft,"0001","fake-password"));
        Equal(14,fake.Calls);
        Equal(0,fake.Remaining);
        // Invalid IDs and data never reach network; failure never retries.
        await Fail<ArgumentException>(()=>api.ApproveAsync(Id,0,"0001","fake-password"));
        await Fail<ArgumentException>(()=>api.CreateAsync(draft with{
           Environment="evil"},"0001","fake-password"));
        Equal(14,fake.Calls);
    }
}
