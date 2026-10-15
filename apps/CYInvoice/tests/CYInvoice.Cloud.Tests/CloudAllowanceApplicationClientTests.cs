using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Amego;

internal static class CloudAllowanceApplicationClientTests
{
    private const string Token =
        "cydev_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Id = "wi_synthetic_allowance_request_01";
    private const string Endpoint = "https://cloud.example.test/";
    public static async Task RunAsync()
    {
        using (var probeHttp = new HttpClient(new QueueHandler()))
        {
            Reject<ArgumentException>(() => new CloudAllowanceApplicationClient(
                probeHttp, new Uri("http://cloud.example.test/"), Token));
            Reject<ArgumentException>(() => new CloudAllowanceApplicationClient(
                probeHttp, new Uri(Endpoint+"?token=raw"), Token));
        }
        var request = new AllowanceIssueRequest {
            AllowanceNumber="",AllowanceDate="20261015",
            ProductItem=[new AllowanceProductItem {
                OriginalInvoiceNumber="AB12345678",OriginalInvoiceDate=20261015,
                OriginalDescription="Synthetic",Quantity=1,UnitPrice=50,Amount=50,Tax=0,TaxType=1
            }],TotalAmount=50,TaxAmount=0
        };
        var draft = new CloudAllowanceApplicationDraft(
            Id,"AB12345678",new DateOnly(2026,10,15),50,"Synthetic allowance",request);
        var handler = new QueueHandler();
        handler.Enqueue(req =>
        {
            Check(req,"create");
            using var data=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var body=data.RootElement;
            Equal(Id,body.GetProperty("itemId").GetString(),"stable Work Item ID");
            Equal("AB12345678",body.GetProperty("originalInvoiceNumber").GetString(),"invoice");
            Equal(50,body.GetProperty("requestedGrossAmount").GetInt32(),"gross request");
            True(!body.TryGetProperty("workspaceId",out _),"scope from Worker only");
            True(!body.TryGetProperty("sellerInvoice",out _),"company from Worker only");
            True(!body.TryGetProperty("environment",out _),"environment from Worker only");
            using var json=JsonDocument.Parse(body.GetProperty("unnumberedTemplateJson").GetString()!);
            Equal("",json.RootElement.GetProperty("AllowanceNumber").GetString(),"no premature number");
            Equal("AB12345678",
                json.RootElement.GetProperty("ProductItem")[0].GetProperty("OriginalInvoiceNumber").GetString(),
                "exact invoice item");
            return Json(HttpStatusCode.Created,new {ok=true,itemId=Id,status="open",revision=1});
        });
        handler.Enqueue(req => {Check(req,"get"); return Json(HttpStatusCode.OK,new {
            ok=true,application=Summary()});});
        handler.Enqueue(req => {
            Check(req,"list");
            using var body=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(1,body.RootElement.GetProperty("limit").GetInt32(),"list limit");
            True(!body.RootElement.TryGetProperty("workspaceId",out _),"list scoped to CYID authority");
            return Json(HttpStatusCode.OK,new{ok=true,applications=new[]{Summary()},nextCursor=(object?)null});
        });
        handler.Enqueue(req =>
        {
            Check(req,"review-preview");
            using var body=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(Id,body.RootElement.GetProperty("itemId").GetString(),"review item scope");
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"review revision");
            True(!body.RootElement.TryGetProperty("officialApproved",out _),
                "client cannot supply official approval");
            return Json(HttpStatusCode.OK,new{ok=true,review=ReviewPreview()});
        });
        handler.Enqueue(req =>
        {
            Check(req,"review-preview");
            return Json(HttpStatusCode.OK,new{ok=true,review=new {
                itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
                allowanceDay="20261015",sellerInvoice="12345678",environment="test",
                requestedGrossAmount=50,reason="Synthetic allowance",applicantEmployeeId="cyid_user",
                templateSha256=new string('a',64),officialVerification="NOT_PERFORMED",
                canApprove=true,items=new[]{new{description="Synthetic",quantity=1,
                    unitPrice=50,amount=50,tax=0,taxType=1}}
            }});
        });
        handler.Enqueue(req =>
        {
            Check(req,"official-inspection");
            using var body=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(Id,body.RootElement.GetProperty("itemId").GetString(),"official exact id");
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"official revision");
            True(!body.RootElement.TryGetProperty("appKey",out _),
                "Windows must never send company key into official query body");
            return Json(HttpStatusCode.OK,new{ok=true,inspection=OfficialStatus(false)});
        });
        handler.Enqueue(req =>
        {
            Check(req,"official-inspection");
            return Json(HttpStatusCode.OK,new{ok=true,inspection=OfficialStatus(true)});
        });
        handler.Enqueue(req => {Check(req,"create"); return Json(HttpStatusCode.Conflict,new {
            ok=false,error=new{code="APPLICATION_RESULT_UNKNOWN_READBACK_REQUIRED"}});});
        handler.Enqueue(req => {Check(req,"get"); return Json(HttpStatusCode.OK,new {
            ok=true,application=Summary()});});
        using var http = new HttpClient(handler);
        var client = new CloudAllowanceApplicationClient(http,new Uri(Endpoint),Token);
        Equal(Id,await client.CreateAsync(draft,"0001","SyntheticPass12"),"created id");
        var loaded=await client.GetAsync(Id,"0001","SyntheticPass12");
        Equal(Id,loaded!.ItemId,"same readback id");
        Equal("review",loaded.Status,"server review state");
        Equal(50,loaded.RequestedGrossAmount,"gross amount");
        var list=await client.ListForManagerAsync(1,null,"0001","SyntheticPass12");
        Equal(1,list.Applications.Count,"list page");
        True(list.NextCursor is null,"last page");
        var preview=await client.PreviewForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal(false,preview.CanApprove,"preview never implies official approval");
        Equal("NOT_PERFORMED",preview.OfficialVerification,"official data is not yet verified");
        Equal(1,preview.Items.Count,"verified frozen item count");
        Equal("Synthetic",preview.Items[0].Description,"allowance line description");
        await RejectAsync<InvalidDataException>(() =>
            client.PreviewForManagerAsync(Id,2,"0001","SyntheticPass12"));
        var official=await client.InspectOfficialForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal(false,official.CanApprove,"official query does not grant approval");
        Equal(true,official.RequiresPerItemVerification,"per-item verification remains required");
        Equal(0,official.ExistingAllowanceCount,"explicit no allowance summary");
        Equal(false,official.ItemCheck.FullyMatched,"missing original net details remain unverified");
        Equal("ORIGINAL_TAX_BASIS_NOT_VERIFIED",official.ItemCheck.Reason,"safe reason preserved");
        await RejectAsync<InvalidDataException>(() =>
            client.InspectOfficialForManagerAsync(Id,2,"0001","SyntheticPass12"));
        try {
            await client.CreateAsync(draft,"0001","SyntheticPass12");
            throw new InvalidOperationException("Expected ambiguous create result");
        } catch (CloudApiException ex) when (ex.Code=="APPLICATION_RESULT_UNKNOWN_READBACK_REQUIRED") {}
        Equal(Id,(await client.GetAsync(Id,"0001","SyntheticPass12"))?.ItemId,
            "caller read back original ID rather than allocating new one");
        Equal(0,handler.PendingCount,"no implicit HTTP retry");
        await RejectAsync<ArgumentException>(()=>client.CreateAsync(draft with{
            UnnumberedTemplate=new AllowanceIssueRequest{AllowanceNumber="A20261015001",
                AllowanceDate="20261015",ProductItem=request.ProductItem}
        },"0001","SyntheticPass12"));
        await RejectAsync<ArgumentException>(()=>client.ListForManagerAsync(51,null,"0001","SyntheticPass12"));
        Equal(0,handler.PendingCount,"client validations never send unsafe requests");
        True(CloudAllowanceApplicationClient.CreateItemId().StartsWith("wi_",StringComparison.Ordinal),
            "caller can prepare and durably persist an opaque request ID");
    }
    private static object OfficialStatus(bool forgedApproval) => new {
        itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
        checkedAtUtc="2026-10-15T02:00:00.000Z",
        officialSnapshotSha256=new string('a',64),
        officialUploadStatus=99,cancelled=false,officialInvoiceGross=100,
        requestedGrossAmount=50,existingAllowanceCount=0,
        uploadComplete=true,notCancelled=true,allowanceSummaryExplicit=true,
        zeroExistingAllowances=true,invoiceGrossEnough=true,officialProductsPresent=true,
        requiresPerItemVerification=true,canApprove=forgedApproval,
        officialPendingMutation=false,itemCheck=new{
            fullyMatched=false,reason="ORIGINAL_TAX_BASIS_NOT_VERIFIED",verifiedLines=0
        },
        status="OFFICIAL_READ_ONLY_NOT_APPROVED"
    };
    private static object ReviewPreview() => new {
        itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
        allowanceDay="20261015",sellerInvoice="12345678",environment="test",
        requestedGrossAmount=50,reason="Synthetic allowance",applicantEmployeeId="cyid_user",
        templateSha256=new string('a',64),officialVerification="NOT_PERFORMED",
        canApprove=false,items=new[]{new{description="Synthetic",quantity=1,
            unitPrice=50,amount=50,tax=0,taxType=1}}
    };
    private static object Summary() => new {
        itemId=Id,environment="test",status="review",revision=2,
        originalInvoiceNumber="AB12345678",allowanceDay="20261015",
        requestedGrossAmount=50,reason="Synthetic allowance",
        applicantEmployeeId="cyid_user",sellerInvoice="12345678",
        createdAt="2026-10-15T02:00:00.000Z"
    };
    private static void Check(HttpRequestMessage req,string action)
    {
        Equal(HttpMethod.Post,req.Method,"method");
        Equal("/v1/allowance-applications/"+action,req.RequestUri?.AbsolutePath,"route");
        Equal(string.Empty,req.RequestUri?.Query,"no query credentials");
        Equal("Bearer",req.Headers.Authorization?.Scheme,"Device bearer");
        Equal(Token,req.Headers.Authorization?.Parameter,"Device token");
        using var doc=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Equal("0001",doc.RootElement.GetProperty("employeeNo").GetString(),"CYID employee");
        Equal("SyntheticPass12",doc.RootElement.GetProperty("password").GetString(),"per-operation password");
    }
    private static HttpResponseMessage Json(HttpStatusCode status,object payload)=>new(status)
    {
        Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json")
    };
    private static void Equal<T>(T expected,T actual,string hint)
    {
        if(!EqualityComparer<T>.Default.Equals(expected,actual))
            throw new InvalidOperationException(hint+" mismatch");
    }
    private static void True(bool condition,string hint)
    {
        if(!condition)throw new InvalidOperationException(hint);
    }
    private static void Reject<T>(Action action) where T:Exception
    {
        try{action();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task> action) where T:Exception
    {
        try{await action();}catch(T){return;}
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }
}
