using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        handler.Enqueue(req =>
        {
            Check(req,"official-inspection");
            return Json(HttpStatusCode.OK,new{ok=true,inspection=new {
                itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
                checkedAtUtc="2026-10-15T02:00:00.000Z",
                officialSnapshotSha256=new string('a',64),
                officialUploadStatus=99,cancelled=false,officialInvoiceGross=100,
                requestedGrossAmount=50,existingAllowanceCount=1,
                uploadComplete=true,notCancelled=true,allowanceSummaryExplicit=true,
                zeroExistingAllowances=false,invoiceGrossEnough=true,
                officialProductsPresent=true,requiresPerItemVerification=true,
                canApprove=false,officialPendingMutation=false,
                itemCheck=new{fullyMatched=false,reason="OFFICIAL_STATE_NOT_SAFE",verifiedLines=0},
                safeToInferRemainingBalance=false,
                allowanceHistory=new[]{new{
                    allowanceNumber="3821061800001",allowanceDate="20261015",
                    totalAmount="50",taxAmount="3",uploadStatus=99,
                    cancelDate="0",metadataComplete=true
                }},
                historicalAllocationChecks=new[]{new{
                    allowanceNumber="3821061800001",
                    reason="ALLOCATED_LINES_MATCH_HEADER",
                    lineTotalsVerified=true,originalNet="50",originalTax="3",
                    originalGross="53",safeToInferRemainingBalance=false
                }},
                historicalAllocationNotQueriedCount=0,
                historicalAllocationsComplete=false,
                historicalCoverageReason="HISTORY_RANGE_NOT_PROVEN",
                status="OFFICIAL_READ_ONLY_NOT_APPROVED"
            }});
        });
        handler.Enqueue(req => {
            Check(req,"official-inspection");
            return Json(HttpStatusCode.OK,new{ok=true,inspection=new {
                itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
                checkedAtUtc="2026-10-15T02:00:00.000Z",
                officialSnapshotSha256=new string('a',64),
                officialUploadStatus=99,cancelled=false,officialInvoiceGross=100,
                requestedGrossAmount=50,existingAllowanceCount=1,
                uploadComplete=true,notCancelled=true,allowanceSummaryExplicit=true,
                zeroExistingAllowances=false,invoiceGrossEnough=true,
                officialProductsPresent=true,requiresPerItemVerification=true,
                canApprove=false,officialPendingMutation=false,
                itemCheck=new{fullyMatched=false,reason="OFFICIAL_STATE_NOT_SAFE",verifiedLines=0},
                safeToInferRemainingBalance=true,
                allowanceHistory=new[]{new{
                    allowanceNumber="3821061800001",allowanceDate="20261015",
                    totalAmount="50",taxAmount="3",uploadStatus=99,
                    cancelDate="0",metadataComplete=true
                }},
                historicalAllocationChecks=Array.Empty<object>(),
                historicalAllocationNotQueriedCount=1,
                historicalAllocationsComplete=false,
                historicalCoverageReason="DETAIL_QUERY_LIMITED",
                status="OFFICIAL_READ_ONLY_NOT_APPROVED"
            }});
        });
        handler.Enqueue(req => {
            Check(req,"official-inspection");
            var forged=JsonNode.Parse(JsonSerializer.Serialize(OfficialStatus(false)))!;
            forged["historicalCoverageReason"]="HISTORY_RANGE_NOT_PROVEN";
            return Json(HttpStatusCode.OK,new{ok=true,inspection=forged});
        });
        handler.Enqueue(req => {
            Check(req,"number-readback");
            using var data=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(Id,data.RootElement.GetProperty("itemId").GetString(),"readback frozen id");
            Equal(2,data.RootElement.GetProperty("expectedRevision").GetInt32(),"readback revision");
            True(!data.RootElement.TryGetProperty("allowanceNumber",out _),
                "client must never choose the official allowance number");
            return Json(HttpStatusCode.OK,new{ok=true,readback=NumberReadback(false)});
        });
        handler.Enqueue(req => {
            Check(req,"number-readback");
            return Json(HttpStatusCode.OK,new{ok=true,readback=NumberReadback(true)});
        });
        handler.Enqueue(req => {
            Check(req,"reconcile");
            using var body=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(Id,body.RootElement.GetProperty("itemId").GetString(),"reconcile original Work Item");
            Equal(2,body.RootElement.GetProperty("expectedRevision").GetInt32(),"reconcile revision");
            True(!body.RootElement.TryGetProperty("allowanceNumber",out _),"Windows never chooses allowance number");
            True(!body.RootElement.TryGetProperty("sellerInvoice",out _),"Windows never chooses company");
            return Json(HttpStatusCode.OK,new{ok=true,
                reconciliation=Reconciliation("OFFICIAL_STATUS_CHANGED_OR_PENDING"),
                readback=NumberReadback(false)});
        });
        handler.Enqueue(req => {
            Check(req,"reconcile");
            return Json(HttpStatusCode.OK,new{ok=true,
                reconciliation=Reconciliation("NOT_SEEN_NOT_PROOF")});
        });
        handler.Enqueue(req => {
            Check(req,"reconcile");
            return Json(HttpStatusCode.OK,new{ok=true,
                reconciliation=new {
                    itemId=Id,expectedRevision=2,allowanceNumber="A20261015001",
                    listReason="NOT_SEEN_NOT_PROOF",requiresManualReconciliation=true,
                    safeToAutoResend=true,safeToAutoConfirm=false
                }});
        });
        handler.Enqueue(req => {
            Check(req,"reconcile");
            return Json(HttpStatusCode.OK,new{ok=true,
                reconciliation=Reconciliation("OFFICIAL_STATUS_CHANGED_OR_PENDING"),
                readback=new {
                    itemId=Id,expectedRevision=2,allowanceNumber="A20261015002",
                    ledgerStatus="sending",checkedAtUtc="2026-10-15T02:00:00Z",
                    officialSnapshotSha256=new string('b',64),officialInvoiceType="D0401",
                    officialUploadStatus=99,officialCancelled=false,
                    exactOriginalInvoiceMatch=true,grossMatchesFrozenRequest=true,
                    possibleMatch=true,requiresManualReconciliation=true,
                    safeToAutoResend=false,safeToAutoConfirm=false
                }});
        });
        handler.Enqueue(req => {
            Check(req,"approve");
            return Json(HttpStatusCode.Created,new {ok=true,approval=new {
                approvalId="apr_synthetic_approved",itemId=Id,revision=2,
                approvedByEmployeeId="cyid_manager",
                approvedAt="2026-10-15T02:00:00Z",
                validUntilUtc="2026-10-15T02:15:00Z",
                canIssueNow=false,requiresFinalPreflight=true
            }});
        });
        handler.Enqueue(req => {
            Check(req,"reserve");
            return Json(HttpStatusCode.Created,new {ok=true,number=new{
                itemId=Id,revision=2,allowanceNumber="A20261015001",attempt=1,
                status="reserved",canAutoIssue=false,requiresFinalPreflight=true
            }});
        });
        handler.Enqueue(req => {
            Check(req,"number-status");
            return Json(HttpStatusCode.OK,new {ok=true,number=new{
                itemId=Id,revision=2,allowanceNumber="A20261015001",attempt=1,
                status="reserved",mustReadbackBeforeAnyNewAttempt=false,canAutoIssue=false
            }});
        });
        handler.Enqueue(req => {
            Check(req,"send-once");
            using var body=JsonDocument.Parse(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            True(!body.RootElement.TryGetProperty("allowanceNumber",out _),
                "Windows may not supply arbitrary allowance number");
            return Json(HttpStatusCode.Accepted,new {ok=true,attempt=new{
                itemId=Id,revision=2,allowanceNumber="A20261015001",
                result="ACCEPTED_PENDING_OFFICIAL",officialCode=0,
                safeToAutoResend=false,safeToAutoConfirm=false,
                mustReadbackByOriginalNumber=true
            }});
        });
        handler.Enqueue(req => {
            Check(req,"send-once");
            return Json(HttpStatusCode.Accepted,new {ok=true,attempt=new{
                itemId=Id,revision=2,allowanceNumber="A20261015001",
                result="ACCEPTED_PENDING_OFFICIAL",officialCode=0,
                safeToAutoResend=true,safeToAutoConfirm=false,
                mustReadbackByOriginalNumber=true
            }});
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
        Equal(0,official.AllowanceHistory.Count,"zero historical entries");
        Equal(0,official.HistoricalAllocationChecks.Count,"no detailed history readback");
        Equal(0,official.HistoricalAllocationNotQueriedCount,"no unread history");
        Equal(false,official.HistoricalAllocationsComplete,"coverage never inferred");
        Equal("NO_HISTORY_REPORTED_NOT_PROOF",official.HistoricalCoverageReason,
            "zero visible history cannot prove entire range");
        Equal(false,official.SafeToInferRemainingBalance,"summary cannot authorize balance");
        Equal(false,official.ItemCheck.FullyMatched,"missing original net details remain unverified");
        Equal("ORIGINAL_TAX_BASIS_NOT_VERIFIED",official.ItemCheck.Reason,"safe reason preserved");
        await RejectAsync<InvalidDataException>(() =>
            client.InspectOfficialForManagerAsync(Id,2,"0001","SyntheticPass12"));
        var historical=await client.InspectOfficialForManagerAsync(
            Id,2,"0001","SyntheticPass12");
        Equal(1,historical.AllowanceHistory.Count,"official historical records visible");
        Equal("3821061800001",historical.AllowanceHistory[0].AllowanceNumber,
            "legacy allowance number preserved");
        Equal(false,historical.SafeToInferRemainingBalance,
            "multi-invoice allocation remains unverified");
        Equal(1,historical.HistoricalAllocationChecks.Count,
            "one official original allocation readback");
        Equal("53",historical.HistoricalAllocationChecks[0].OriginalGross,
            "per-original gross, not full allowance gross");
        Equal(false,historical.HistoricalAllocationsComplete,
            "a historical readback never authorizes remaining balance");
        Equal("HISTORY_RANGE_NOT_PROVEN",historical.HistoricalCoverageReason,
            "matched details do not prove entire history");
        await RejectAsync<InvalidDataException>(() =>
            client.InspectOfficialForManagerAsync(Id,2,"0001","SyntheticPass12"));
        await RejectAsync<InvalidDataException>(() =>
            client.InspectOfficialForManagerAsync(Id,2,"0001","SyntheticPass12"));
        var readback=await client.ReadbackNumberForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal(false,readback.SafeToAutoResend,"never auto-resend an ambiguous request");
        Equal(false,readback.SafeToAutoConfirm,"never auto-confirm from a number match");
        Equal(true,readback.RequiresManualReconciliation,"manager must reconcile");
        Equal("A20261015001",readback.AllowanceNumber,"original reserved number only");
        await RejectAsync<InvalidDataException>(() =>
            client.ReadbackNumberForManagerAsync(Id,2,"0001","SyntheticPass12"));
        var reconciled=await client.ReconcileForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal("A20261015001",reconciled.AllowanceNumber,"same D1 allowance number");
        Equal("OFFICIAL_STATUS_CHANGED_OR_PENDING",reconciled.ListReason,"official list hint");
        True(reconciled.Readback is not null,"list hit selectively reads original number");
        Equal(false,reconciled.Readback!.SafeToAutoResend,"no repeat issue after match");
        Equal(false,reconciled.Readback.SafeToAutoConfirm,"no silent confirmation");
        var missing=await client.ReconcileForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal("NOT_SEEN_NOT_PROOF",missing.ListReason,"absence is not proof");
        True(missing.Readback is null,"absence must not invent a readback");
        await RejectAsync<InvalidDataException>(()=>
            client.ReconcileForManagerAsync(Id,2,"0001","SyntheticPass12"));
        await RejectAsync<InvalidDataException>(()=>
            client.ReconcileForManagerAsync(Id,2,"0001","SyntheticPass12"));
        await RejectAsync<ArgumentOutOfRangeException>(()=>
            client.ReconcileForManagerAsync(Id,0,"0001","SyntheticPass12"));
        var approval=await client.ApproveForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal(false,approval.CanIssueNow,"Approval grant is not issue permission");
        Equal(true,approval.RequiresFinalPreflight,"Preflight always required");
        var reserved=await client.ReserveForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal("A20261015001",reserved.AllowanceNumber,"server-allocated number");
        Equal(false,reserved.CanAutoIssue,"reservation does not trigger issue");
        var status=await client.GetNumberStatusForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal("reserved",status.Status,"no send yet");
        var sent=await client.SendOnceForManagerAsync(Id,2,"0001","SyntheticPass12");
        Equal("ACCEPTED_PENDING_OFFICIAL",sent.Result,"accepted is not confirmed");
        Equal(false,sent.SafeToAutoResend,"ambiguous or accepted cannot retry");
        Equal(false,sent.SafeToAutoConfirm,"never confirm on transport alone");
        await RejectAsync<InvalidDataException>(() =>
            client.SendOnceForManagerAsync(Id,2,"0001","SyntheticPass12"));
        // An invalid revision is rejected without reaching the provider.
        await RejectAsync<ArgumentOutOfRangeException>(() =>
            client.SendOnceForManagerAsync(Id,0,"0001","SyntheticPass12"));
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
    private static object Reconciliation(string reason) => new {
        itemId=Id,expectedRevision=2,allowanceNumber="A20261015001",
        listReason=reason,requiresManualReconciliation=true,
        safeToAutoResend=false,safeToAutoConfirm=false
    };
    private static object NumberReadback(bool forgedResend) => new {
        itemId=Id,expectedRevision=2,allowanceNumber="A20261015001",
        ledgerStatus="sending",checkedAtUtc="2026-10-15T02:00:00.000Z",
        officialSnapshotSha256=new string('b',64),
        officialInvoiceType="D0401",officialUploadStatus=99,officialCancelled=false,
        exactOriginalInvoiceMatch=true,grossMatchesFrozenRequest=true,
        possibleMatch=true,requiresManualReconciliation=true,
        safeToAutoResend=forgedResend,safeToAutoConfirm=false
    };
    private static object OfficialStatus(bool forgedApproval) => new {
        itemId=Id,revision=2,originalInvoiceNumber="AB12345678",
        checkedAtUtc="2026-10-15T02:00:00.000Z",
        officialSnapshotSha256=new string('a',64),
        officialUploadStatus=99,cancelled=false,officialInvoiceGross=100,
        requestedGrossAmount=50,existingAllowanceCount=0,
        uploadComplete=true,notCancelled=true,allowanceSummaryExplicit=true,
        zeroExistingAllowances=true,invoiceGrossEnough=true,officialProductsPresent=true,
        requiresPerItemVerification=true,canApprove=forgedApproval,
        officialPendingMutation=false,
        allowanceHistory=Array.Empty<object>(),
        historicalAllocationChecks=Array.Empty<object>(),
        historicalAllocationNotQueriedCount=0,
        historicalAllocationsComplete=false,
        historicalCoverageReason="NO_HISTORY_REPORTED_NOT_PROOF",
        safeToInferRemainingBalance=false,
        itemCheck=new{
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
