using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Local g0501 signed transport never retries, and exact status decoder fails closed", TestG0501TransportAsync),
    ("Local g0501 approval, durable claim, lost reply, restart and official observation", TestLocalVoidLifecycleAsync),
    ("Local g0501 rejects wrong manager, expiry, changed official and revoked manager", TestLocalVoidPreconditionsAsync),
    ("Local g0501 concurrent sends and cancellation race permit at most one POST", TestLocalVoidRacesAsync),
    ("Local g0501 failed result audit retains permanent claim", TestLocalVoidAuditFailureAsync),
    ("Local g0501 changed draft, cancellation and account scope cannot claim", TestLocalVoidScopeAsync),
    ("Cloud to Local mode toggle cannot restore g0501 after reconnect/restart", TestLocalVoidModeHandoffFenceAsync),
    ("Local g0501 original-number fence blocks competing issue IDs", TestLocalVoidCompetingIssueAsync),
    ("Local g0501 retention preserves pending and audit while pruning unrelated history", TestLocalVoidRetentionAsync),
    ("V3.2-B1 Local immutable approved-number ledger survives restart and blocks blind replay", () => RunSync(LocalAllowanceLedgerTests.Run)),
    ("V3.2-B2c one-shot Local sending CAS, ambiguous readback and official collision", LocalAllowanceOneShotTests.RunAsync),
    ("V3.2-B2c signed g0401 transport has exactly one POST even on time error", TestG0401SinglePostAsync),
    ("V3.2 remaining cap fails closed on unknown status and excludes voided allowances", () => RunSync(TestOfficialRemainingGross)),
    ("V3.2 net draft lines keep seven-decimal precision and reject client tampering", () => RunSync(TestDraftLineValidation)),
    ("V3.2 gross input safely converts to net AMEGO fields and rejects altered prices", () => RunSync(TestGrossPriceInput)),
    ("allowance PDF official styles are exact", () => RunSync(TestOfficialStyles)),
    ("allowance PDF signs request caches and fingerprints official data", TestPdfCacheAndFingerprintAsync),
    ("allowance PDF code 15 synchronizes time once", TestPdfTimeSyncAsync),
    ("allowance PDF rejects untrusted file URL", TestPdfRejectsUntrustedUrlAsync),
    ("allowance PDF rejects non PDF response", TestPdfRejectsInvalidContentAsync),
    ("allowance void wrong credentials query nothing", TestAllowanceVoidWrongCredentialsAsync),
    ("allowance void requires one completed official allowance", TestAllowanceVoidEligibilityAsync),
    ("allowance void duplicate request is idempotent", TestAllowanceVoidDuplicateAsync),
    ("ordinary employee cannot complete allowance void work", TestAllowanceVoidEmployeeCannotCompleteAsync),
    ("manager completion is local only", TestAllowanceVoidManagerCompletesLocallyAsync),
    ("manager cancellation is local only", TestAllowanceVoidManagerCancelsLocallyAsync),
    ("expired allowance void work supports administrative closure", TestAllowanceVoidAdministrativeClosureAsync),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} allowance tests passed");
return failures == 0 ? 0 : 1;

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static async Task TestG0501TransportAsync()
{
    foreach (var reply in new[] { "{\"code\":15}", "{}", "[]", "{\"code\":null}" })
    {
        using var handler = new RecordingHttpHandler((_,_,_) => new HttpResponseMessage(HttpStatusCode.OK) {
            Content=new StringContent(reply) });
        var client = new AmegoClient(AmegoDefaults.TestInvoice,AmegoDefaults.TestAppKey,new HttpClient(handler));
        await ThrowsAsync<Exception>(() => client.VoidAllowanceOnceAsync("LEGACY-001"));
        Equal(1,handler.Requests.Count);
        Equal("/json/g0501",handler.Requests[0].Uri.AbsolutePath);
        var form=ParseForm(handler.Requests[0].Body);
        Equal("[{\"CancelAllowanceNumber\":\"LEGACY-001\"}]",form["data"]);
        var signature=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
            form["data"]+form["time"]+AmegoDefaults.TestAppKey))).ToLowerInvariant();
        Equal(signature,form["sign"]);
    }
    foreach (var state in new[] { ("D0501","99",true), ("D0501","91",false),
        ("D0401","99",false), ("UNKNOWN","99",false), ("D0501","1",false) })
    {
        using var handler=new RecordingHttpHandler((_,_,_) => new HttpResponseMessage(HttpStatusCode.OK) {
            Content=new StringContent("{\"code\":0,\"data\":{\"LEGACY-001\":{\"type\":\""+state.Item1+
                "\",\"status\":\""+state.Item2+"\"}}}") });
        var client=new AmegoClient(AmegoDefaults.TestInvoice,AmegoDefaults.TestAppKey,new HttpClient(handler));
        Equal(state.Item3,(await client.ReadAllowanceVoidStatusAsync("LEGACY-001")).OfficiallyVoided);
        Equal("[{\"AllowanceNumber\":\"LEGACY-001\"}]",ParseForm(handler.Requests[0].Body)["data"]);
    }
    foreach (var reply in new[] { "{\"code\":0,\"data\":{\"OTHER\":{\"type\":\"D0501\",\"status\":99}}}",
        "{\"code\":0,\"data\":{\"LEGACY-001\":{\"type\":\"D0501\"}}}",
        "{\"code\":0,\"data\":{\"LEGACY-001\":{\"type\":\"D0501\",\"status\":null}}}" })
    {
        using var handler=new RecordingHttpHandler((_,_,_)=>new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(reply) });
        var client=new AmegoClient(AmegoDefaults.TestInvoice,AmegoDefaults.TestAppKey,new HttpClient(handler));
        await ThrowsAsync<InvalidDataException>(()=>client.ReadAllowanceVoidStatusAsync("LEGACY-001"));
    }
}

static async Task<(VoidSetup Setup,InvoiceSyncIssue Issue,FakeAllowanceVoidApi Api)> CreateApiVoidAsync(string path)
{
    var setup=CreateVoidSetup(path,[Allowance(99)]);
    setup.Gateway.QueryResponse=setup.Gateway.QueryResponse with { Data=setup.Gateway.QueryResponse.Data! with {
        BuyerIdentifier="0000000000" } };
    await setup.Workflow.SubmitAsync(setup.Record,"ALW-001","3015","Employee1","合成作廢原因");
    return(setup,UnresolvedAllowanceVoidIssues(setup.Repository).Single(),new FakeAllowanceVoidApi());
}

static async Task ApproveApiVoidAsync(VoidSetup setup,InvoiceSyncIssue issue,FakeAllowanceVoidApi api)
{
    var preview=await setup.Workflow.PreviewLocalVoidAsync(issue,"2000","AdminPass1",api);
    await setup.Workflow.ApproveLocalVoidAsync(issue,"2000","AdminPass1",preview.OfficialSnapshotHash,api);
}

static async Task TestLocalVoidLifecycleAsync()
{
    using var temporary=new TemporaryDirectory("g0501-lifecycle");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ThrowsAsync<UnauthorizedAccessException>(()=>setup.Workflow.PreviewLocalVoidAsync(issue,"3015","Employee1",api));
    Equal(0,api.QueryCalls);
    await ApproveApiVoidAsync(setup,issue,api);
    Equal(0,api.PostCalls);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","錯誤確認",api));
    api.LoseReply=true;
    var result=await setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api);
    Equal(false,result.OfficialVoidObserved);
    Equal(1,api.PostCalls);
    Equal(true,setup.Workflow.LocalApprovalFor(issue)!.Claimed);
    var restarted=new EmployeeAllowanceVoidWorkflowService(setup.Repository);
    await ThrowsAsync<InvalidOperationException>(()=>restarted.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    Throws<InvalidOperationException>(()=>restarted.CancelManualReview(issue,"2000","AdminPass1"));
    Throws<InvalidOperationException>(()=>restarted.MarkManualCompleted(issue,"2000","AdminPass1"));
    var closure=new InvoiceAdministrativeClosureService(setup.Repository,now:()=>DateTimeOffset.UtcNow.AddYears(1));
    Equal(false,closure.CanClose(issue));
    Throws<InvalidOperationException>(()=>closure.Close(issue,"2000","AdminPass1"));
    api.Status=new("ALW-001","D0501",91);
    Equal(false,(await restarted.ReadbackLocalVoidAsync(issue,"2000","AdminPass1",api)).OfficialVoidObserved);
    api.Status=new("ALW-001","D0501",99);
    Equal(true,(await restarted.ReadbackLocalVoidAsync(issue,"0001","SuperPass1",api)).OfficialVoidObserved);
    Equal(1,api.PostCalls);
    Equal(1,UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    Equal(true,restarted.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()) is not null);
    using var db=OpenVoidDatabase(setup.Repository);
    foreach(var table in new[]{"approvals","claims","observations"})
    {
        using var command=db.CreateCommand(); command.CommandText="DELETE FROM allowance_void_local_"+table;
        Throws<Microsoft.Data.Sqlite.SqliteException>(()=>command.ExecuteNonQuery());
    }
    using var audit=db.CreateCommand();
    audit.CommandText="SELECT actor_employee_no FROM allowance_void_local_observations ORDER BY audit_id DESC LIMIT 1";
    Equal("0001",audit.ExecuteScalar() as string);
}

static async Task TestLocalVoidPreconditionsAsync()
{
    using var temporary=new TemporaryDirectory("g0501-preflight");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    var valid=api.Official;
    foreach(var invalid in new[]{valid with { BuyerIdentifier="OTHER" },valid with { InvoiceType="D0501" },
        valid with { InvoiceStatus=1 },valid with { CancelDate=1 },valid with { AllowanceNumber="OTHER" },
        valid with { ProductItems=JsonSerializer.SerializeToElement(new[]{new {original_invoice_number="BB12345678"}}) }})
    {
        api.Official=invalid;
        await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.PreviewLocalVoidAsync(issue,"2000","AdminPass1",api));
    }
    api.Official=valid;
    var preview=await setup.Workflow.PreviewLocalVoidAsync(issue,"2000","AdminPass1",api);
    api.Official=valid with {TotalAmount="96"};
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.ApproveLocalVoidAsync(issue,"2000","AdminPass1",preview.OfficialSnapshotHash,api));
    api.Official=valid;
    await ApproveApiVoidAsync(setup,issue,api);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"0001","SuperPass1","作廢 ALW-001",api));
    var expired=new EmployeeAllowanceVoidWorkflowService(setup.Repository,now:()=>new DateTimeOffset(2026,9,21,0,0,0,TimeSpan.Zero));
    await ThrowsAsync<InvalidOperationException>(()=>expired.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    api.Official=valid with {TaxAmount="6"};
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    api.Official=valid;
    api.OnQuery=()=>setup.Repository.Employees.SetEnabled("0001","2000",false);
    await ThrowsAsync<UnauthorizedAccessException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    Equal(0,api.PostCalls);
    Equal(false,setup.Workflow.LocalApprovalFor(issue)!.Claimed);
}

static async Task TestLocalVoidRacesAsync()
{
    using var temporary=new TemporaryDirectory("g0501-races");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,issue,api);
    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var callers=0;
    api.BeforeQueryAsync=async()=> {if(Interlocked.Increment(ref callers)==2)gate.SetResult(); await gate.Task;};
    async Task<bool> TrySend()
    {
        try {await setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api); return true;}
        catch(InvalidOperationException){return false;}
    }
    var results=await Task.WhenAll(TrySend(),TrySend());
    Equal(1,results.Count(x=>x)); Equal(1,api.PostCalls);
    using var cancelTemporary=new TemporaryDirectory("g0501-cancel-race");
    var cancel=await CreateApiVoidAsync(cancelTemporary.Path);
    await ApproveApiVoidAsync(cancel.Setup,cancel.Issue,cancel.Api);
    cancel.Api.OnQuery=()=>cancel.Setup.Workflow.CancelManualReview(cancel.Issue,"2000","AdminPass1");
    await ThrowsAsync<InvalidOperationException>(()=>cancel.Setup.Workflow.SendLocalVoidOnceAsync(cancel.Issue,"2000","AdminPass1","作廢 ALW-001",cancel.Api));
    Equal(0,cancel.Api.PostCalls);
}

static Microsoft.Data.Sqlite.SqliteConnection OpenVoidDatabase(LocalRepository repository)
{
    var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
        DataSource=Path.Combine(repository.DataDirectory,SqliteBootstrapper.DatabaseFileName),Pooling=false }.ToString());
    db.Open(); return db;
}

static async Task TestLocalVoidAuditFailureAsync()
{
    using var temporary=new TemporaryDirectory("g0501-audit-failure");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,issue,api);
    using(var db=OpenVoidDatabase(setup.Repository))
    using(var command=db.CreateCommand())
    {
        command.CommandText="CREATE TRIGGER injected_void_audit_failure BEFORE INSERT ON allowance_void_local_observations BEGIN SELECT RAISE(ABORT,'synthetic disk failure'); END;";
        command.ExecuteNonQuery();
    }
    Equal(false,(await setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api)).AuditRecorded);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    Equal(1,api.PostCalls); Equal(true,setup.Workflow.LocalApprovalFor(issue)!.Claimed);
}

static async Task TestLocalVoidScopeAsync()
{
    using var temporary=new TemporaryDirectory("g0501-scope");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,issue,api);
    using(var cancelled=new CancellationTokenSource())
    {
        cancelled.Cancel();
        await ThrowsAsync<OperationCanceledException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,
            "2000","AdminPass1","作廢 ALW-001",api,cancelled.Token));
    }
    Equal(false,setup.Workflow.LocalApprovalFor(issue)!.Claimed);
    var record=setup.Repository.Invoices.LoadOrCreate().Single();
    var review=setup.Workflow.ManualReviewFor(record)!;
    record.ExtensionData!["cyinvoice_allowance_void_manual_review"]=JsonSerializer.SerializeToElement(review with {Reason="已改動的原因"});
    setup.Repository.Invoices.Save([record]);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    record.ExtensionData["cyinvoice_allowance_void_manual_review"]=JsonSerializer.SerializeToElement(review);
    setup.Repository.Invoices.Save([record]);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue with {AccountKey="production|OTHER"},"2000","AdminPass1","作廢 ALW-001",api));
    var settings=setup.Repository.Settings.LoadOrCreate();
    settings.CloudMode=CloudModes.CloudPreferred; setup.Repository.Settings.Save(settings);
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api));
    Equal(0,api.PostCalls);
}

static async Task TestLocalVoidModeHandoffFenceAsync()
{
    using var temporary=new TemporaryDirectory("g0501-cloud-mode-handoff");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,issue,api);
    Equal(false,setup.Repository.Settings.LoadOrCreate().CloudVoidLocalSendBlocked);

    // Enter Cloud once. Merely switching the UI back to LocalOnly must NOT
    // re-enable local g0501 after another Device might have claimed it.
    var settings=setup.Repository.Settings.LoadOrCreate();
    settings.CloudMode=CloudModes.CloudPreferred;
    setup.Repository.Settings.Save(settings);
    Equal(true,setup.Repository.Settings.LoadOrCreate().CloudVoidLocalSendBlocked);
    settings=setup.Repository.Settings.LoadOrCreate();
    settings.CloudMode=CloudModes.LocalOnly;
    setup.Repository.Settings.Save(settings);
    Equal(true,setup.Repository.Settings.LoadOrCreate().CloudVoidLocalSendBlocked);

    // Simulated process restart reads the persisted quarantine, not a volatile flag.
    var resumed=new EmployeeAllowanceVoidWorkflowService(setup.Repository);
    Equal(true,setup.Repository.Settings.LoadOrCreate().CloudVoidLocalSendBlocked);
    Equal(null,resumed.LocalApprovalFor(issue));
    var queries=api.QueryCalls;
    await ThrowsAsync<InvalidOperationException>(()=>resumed.PreviewLocalVoidAsync(
        issue,"2000","AdminPass1",api));
    await ThrowsAsync<InvalidOperationException>(()=>resumed.ApproveLocalVoidAsync(
        issue,"2000","AdminPass1","not a real hash",api));
    await ThrowsAsync<InvalidOperationException>(()=>resumed.SendLocalVoidOnceAsync(
        issue,"2000","AdminPass1","作廢 ALW-001",api));
    Equal(queries,api.QueryCalls);
    Equal(0,api.PostCalls);

    // A stale settings object cannot clear the persisted monotonic quarantine.
    var stale=setup.Repository.Settings.LoadOrCreate();
    stale.CloudVoidLocalSendBlocked=false;
    setup.Repository.Settings.Save(stale);
    Equal(true,setup.Repository.Settings.LoadOrCreate().CloudVoidLocalSendBlocked);
}

static async Task TestLocalVoidCompetingIssueAsync()
{
    using var temporary=new TemporaryDirectory("g0501-competing-issue");
    var (setup,original,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,original,api);
    api.LoseReply=true;
    await setup.Workflow.SendLocalVoidOnceAsync(original,"2000","AdminPass1","作廢 ALW-001",api);
    Equal(1,api.PostCalls);
    // Repeat submission must NOT report that a claimed transaction is merely
    // pending manager approval, even if the original application ID survives.
    await ThrowsAsync<InvalidOperationException>(()=>setup.Workflow.SubmitAsync(
        setup.Record,"ALW-001","3015","Employee1","合成作廢原因"));

    // Simulate a second local work item pointing at the exact same AMEGO
    // allowance after a mode/device transition. Its issue ID must not bypass
    // the original company's durable allowance-number send claim.
    var alternateOrder=original.OrderId+"-MODE-SWITCH";
    var alternateId=new InvoiceSyncIssueStore(setup.Repository.DataDirectory).Record(
        original.AccountKey,original.InvoiceNumber,alternateOrder,
        InvoiceAllowanceVoidIssueTypes.ManualReview,"合成跨模式競爭待辦",DateTimeOffset.UtcNow);
    if(alternateId==original.Id)throw new Exception("Expected a competing issue ID.");
    var competing=original with { Id=alternateId,OrderId=alternateOrder };
    var resumed=new EmployeeAllowanceVoidWorkflowService(setup.Repository);
    var beforeQueries=api.QueryCalls;
    await ThrowsAsync<InvalidOperationException>(()=>resumed.PreviewLocalVoidAsync(
        competing,"2000","AdminPass1",api));
    Equal(beforeQueries,api.QueryCalls);
    Throws<InvalidOperationException>(()=>resumed.MarkManualCompleted(
        competing,"2000","AdminPass1"));
    Throws<InvalidOperationException>(()=>resumed.CancelManualReview(
        competing,"2000","AdminPass1"));
    Equal(true,new InvoiceSyncIssueStore(setup.Repository.DataDirectory)
        .Unresolved(original.AccountKey).Any(item=>item.Id==alternateId));
    Equal(1,api.PostCalls);
    Equal(true,resumed.LocalApprovalFor(original)!.Claimed);
    using var db=OpenVoidDatabase(setup.Repository);
    using var command=db.CreateCommand();
    command.CommandText="SELECT COUNT(*) FROM allowance_void_local_claims WHERE account_key=$account AND allowance_number=$number COLLATE NOCASE";
    command.Parameters.AddWithValue("$account",original.AccountKey);
    command.Parameters.AddWithValue("$number","alw-001");
    Equal(1L,Convert.ToInt64(command.ExecuteScalar()));
}

static async Task TestLocalVoidRetentionAsync()
{
    using var temporary=new TemporaryDirectory("g0501-retention");
    var (setup,issue,api)=await CreateApiVoidAsync(temporary.Path);
    await ApproveApiVoidAsync(setup,issue,api);
    await setup.Workflow.SendLocalVoidOnceAsync(issue,"2000","AdminPass1","作廢 ALW-001",api);
    var unrelated=NewRecord("2026/09/01");
    unrelated.InvoiceNumber="BB87654321"; unrelated.OrderId="OTHER";
    unrelated.ApiOrderId="OTHER"; unrelated.OriginalOrderId="OTHER";
    setup.Repository.Invoices.Append(unrelated);
    var retention=new InvoiceRetentionService(setup.Repository);
    Equal(1,retention.Prune(new DateOnly(2026,10,21)).Deleted);
    Equal(1,setup.Repository.Invoices.LoadOrCreate().Count);
    Equal(true,setup.Workflow.LocalApprovalFor(issue)!.Claimed);
    using var cancelledTemporary=new TemporaryDirectory("g0501-retention-cancelled");
    var cancelled=await CreateApiVoidAsync(cancelledTemporary.Path);
    await ApproveApiVoidAsync(cancelled.Setup,cancelled.Issue,cancelled.Api);
    cancelled.Setup.Workflow.CancelManualReview(cancelled.Issue,"2000","AdminPass1");
    Equal(1,new InvoiceRetentionService(cancelled.Setup.Repository).Prune(new DateOnly(2026,10,21)).Deleted);
    Equal(true,cancelled.Setup.Workflow.LocalApprovalFor(cancelled.Issue) is not null);
}

static async Task TestG0401SinglePostAsync()
{
    var handler = new RecordingHttpHandler((request, _, _) =>
        JsonResponse("{\"code\":15,\"msg\":\"time error\"}"));
    using var http = new HttpClient(handler);
    var gateway = new AmegoClient(AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey, http);
    var frozen = new AllowanceIssueRequest
    {
        AllowanceNumber = "A20261019001", AllowanceDate = "20261019",
        AllowanceType = 2, BuyerIdentifier = "12345678", BuyerName = "Synthetic",
        ProductItem = [new AllowanceProductItem
        {
            OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261018,
            OriginalDescription = "Synthetic item", Quantity = 1m, UnitPrice = 100m,
            Amount = 100m, Tax = 5m, TaxType = 1,
        }], TotalAmount = 100m, TaxAmount = 5m
    };
    await ThrowsAsync<AmegoApiException>(() => gateway.IssueAllowanceOnceAsync(frozen));
    Equal(1, handler.Requests.Count);
    Equal("/json/g0401", handler.Requests[0].Uri.AbsolutePath);
    var form = ParseForm(handler.Requests[0].Body);
    Equal(AmegoDefaults.TestInvoice, form["invoice"]);
    using var payload = JsonDocument.Parse(form["data"]);
    Equal(JsonValueKind.Array, payload.RootElement.ValueKind);
    Equal("A20261019001", payload.RootElement[0].GetProperty("AllowanceNumber").GetString());
    Equal(100m, payload.RootElement[0].GetProperty("TotalAmount").GetDecimal());
    var accepted = new RecordingHttpHandler((request, _, _) => request.RequestUri?.AbsolutePath switch
    {
        "/json/g0401" => JsonResponse("{\"code\":0,\"msg\":\"\"}"),
        "/json/allowance_query" => JsonResponse("{\"code\":0,\"msg\":\"\",\"data\":{\"allowance_number\":\"A20261019001\",\"invoice_type\":\"D0401\",\"invoice_status\":99,\"allowance_date\":\"20261019\",\"allowance_type\":2,\"buyer_identifier\":\"12345678\",\"tax_amount\":5,\"total_amount\":100,\"product_item\":[]}}"),
        _ => JsonResponse("{\"code\":71,\"msg\":\"not found\"}")
    });
    using var goodHttp = new HttpClient(accepted);
    var normal = new AmegoClient(AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey, goodHttp);
    var sent = await normal.IssueAllowanceOnceAsync(frozen);
    Equal(0, sent.Code);
    var query = await normal.QueryAllowanceAsync(frozen.AllowanceNumber);
    Equal("A20261019001", query.Data.AllowanceNumber);
    Equal("D0401", query.Data.InvoiceType);
    Equal(2, accepted.Requests.Count);
    Equal("/json/allowance_query", accepted.Requests[1].Uri.AbsolutePath);
}

static void TestOfficialRemainingGross()
{
    var record = new InvoiceRecord { Amount = 1000 };
    var active = new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
        "A20261019001", "20261019", "10", "190");
    var voided = new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
        "A20261019002", "20261019", "10", "190", CancelDate: 20261019);
    InvoiceAllowanceMetadata.ApplyQuery(record, [active, voided], summaryPresent: true);
    Equal("800", InvoiceAllowanceMetadata.RemainingGross(record).ToString());
    InvoiceAllowanceMetadata.ApplyQuery(record, [], summaryPresent: true);
    Equal("1000", InvoiceAllowanceMetadata.RemainingGross(record).ToString());
    InvoiceAllowanceMetadata.ApplyQuery(record, [active], summaryPresent: false);
    Equal(1, InvoiceAllowanceMetadata.ReadOfficial(record).Count);
    Throws<InvalidOperationException>(() => InvoiceAllowanceMetadata.RemainingGross(record));
    InvoiceAllowanceMetadata.ApplyQuery(record, [], summaryPresent: false);
    Equal(0, InvoiceAllowanceMetadata.ReadOfficial(record).Count);
    Throws<InvalidOperationException>(() => InvoiceAllowanceMetadata.RemainingGross(record));
    InvoiceAllowanceMetadata.ApplyQuery(record, [
        active with { InvoiceStatus = UploadStatuses.Processing }
    ], summaryPresent: true);
    Throws<InvalidOperationException>(() => InvoiceAllowanceMetadata.RemainingGross(record));
    InvoiceAllowanceMetadata.ApplyQuery(record, [active, active], summaryPresent: true);
    Throws<InvalidDataException>(() => InvoiceAllowanceMetadata.RemainingGross(record));
    InvoiceAllowanceMetadata.ApplyQuery(record, [
        active with { TaxAmount = "bad" }
    ], summaryPresent: true);
    Throws<InvalidDataException>(() => InvoiceAllowanceMetadata.RemainingGross(record));
}

static void TestDraftLineValidation()
{
    var lines = new[]
    {
        new AllowanceProductItem {
            OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261019,
            OriginalDescription = "原商品", Quantity = 2m, UnitPrice = 100.1234567m,
            Amount = 200m, Tax = 10m, TaxType = 1
        },
        new AllowanceProductItem {
            OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261019,
            OriginalDescription = "負數折扣", Quantity = 1m, UnitPrice = -10m,
            Amount = -10m, Tax = -1m, TaxType = 1
        }
    };
    AllowanceDraftValidator.Validate(lines, "AA12345678", "2026/10/19", 199);
    Throws<InvalidDataException>(() =>
        AllowanceDraftValidator.Validate(lines, "AA12345678", "2026/10/19", 200));
    Throws<InvalidDataException>(() =>
        AllowanceDraftValidator.Validate([
            lines[0] with { Tax = 0m }, lines[1]
        ], "AA12345678", "2026/10/19", 199));
    Throws<InvalidOperationException>(() =>
        AllowanceDraftValidator.Validate([
            lines[0] with { OriginalInvoiceNumber = "OTHER" }, lines[1]
        ], "AA12345678", "2026/10/19", 199));
    Throws<FormatException>(() =>
        AllowanceDraftValidator.Validate([
            lines[0] with { UnitPrice = 100.12345678m }, lines[1]
        ], "AA12345678", "2026/10/19", 199));
}

static void TestGrossPriceInput()
{
    var netPrice = FixedDecimal.MultiplyRatio(FixedDecimal.Parse("105"), 100, 105);
    Equal("100", netPrice.ToString());
    var grossLine = new AllowanceProductItem
    {
        OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261019,
        OriginalDescription = "含稅輸入商品", TaxType = 1, Quantity = 1m,
        GrossUnitPrice = 105m, UnitPrice = 100m, Amount = 100m, Tax = 5m,
    };
    AllowanceDraftValidator.Validate([grossLine], "AA12345678", "2026/10/19", 105);
    // Gross unit price from a real employee input must be preserved so
    // gross-line-first rounding is independently reproducible at the Core.
    var fractionalGross = new AllowanceProductItem
    {
        OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261019,
        OriginalDescription = "含稅七位小數", TaxType = 1, Quantity = 1m,
        GrossUnitPrice = 100.1234567m,
        UnitPrice = 95.3556730m, // seven-decimal rounded 100.1234567 / 1.05
        Amount = 95m, Tax = 5m,
    };
    AllowanceDraftValidator.Validate([fractionalGross], "AA12345678", "2026/10/19", 100);
    var grossDiscount = new AllowanceProductItem
    {
        OriginalInvoiceNumber = "AA12345678", OriginalInvoiceDate = 20261019,
        OriginalDescription = "含稅負折扣", TaxType = 1, Quantity = 1m,
        GrossUnitPrice = -10.5m, UnitPrice = -10m,
        Amount = -10m, Tax = -1m,
    };
    AllowanceDraftValidator.Validate([grossLine, grossDiscount],
        "AA12345678", "2026/10/19", 94);
    Throws<InvalidDataException>(() =>
        AllowanceDraftValidator.Validate([
            grossLine with { UnitPrice = 105m }
        ], "AA12345678", "2026/10/19", 105));
    Throws<InvalidDataException>(() =>
        AllowanceDraftValidator.Validate([
            grossLine with { Tax = 4m }
        ], "AA12345678", "2026/10/19", 105));
    Throws<FormatException>(() =>
        AllowanceDraftValidator.Validate([
            grossLine with { GrossUnitPrice = 105.12345678m }
        ], "AA12345678", "2026/10/19", 105));
    Throws<InvalidDataException>(() =>
        AllowanceDraftValidator.Validate([
            grossLine with { TaxType = 3 }
        ], "AA12345678", "2026/10/19", 105));
}

static void TestOfficialStyles()
{
    Equal(3, AllowancePdfStyles.Official.Count);
    Equal(0, AllowancePdfStyles.Official[0].Code);
    Equal(1, AllowancePdfStyles.Official[1].Code);
    Equal(3, AllowancePdfStyles.Official[2].Code);
    Throws<ArgumentOutOfRangeException>(() => AllowancePdfStyles.Require(2));
}

static async Task TestPdfCacheAndFingerprintAsync()
{
    using var temporary = new TemporaryDirectory("pdf-cache");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post) return JsonResponse(FileSuccess("https://invoice.amego.tw/allowance-a.pdf"));
        if (request.Method == HttpMethod.Get && request.RequestUri?.Host == "invoice.amego.tw") return PdfResponse("first-pdf");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });
    using var client = new HttpClient(handler);
    var clock = new DateTimeOffset(2026, 9, 20, 12, 30, 0, TimeSpan.FromHours(8));
    var service = new AllowancePdfService(repository, client, () => clock);

    var first = await service.GetAsync("ALW-001", 0);
    var second = await service.GetAsync("ALW-001", 0);
    Equal(false, first.FromCache);
    Equal(true, second.FromCache);
    Equal(first.Path, second.Path);
    Equal(2, handler.Requests.Count);

    var post = handler.Requests[0];
    Equal(HttpMethod.Post, post.Method);
    Equal("/json/allowance_file", post.Uri.AbsolutePath);
    var form = ParseForm(post.Body);
    Equal(AmegoDefaults.TestInvoice, form["invoice"]);
    using (var data = JsonDocument.Parse(form["data"]))
    {
        Equal("ALW-001", data.RootElement.GetProperty("allowance_number").GetString());
        Equal(0, data.RootElement.GetProperty("download_style").GetInt32());
    }
#pragma warning disable CA5351 // Test verifies AMEGO's documented MD5 signing protocol.
    var expectedSign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
        form["data"] + form["time"] + AmegoDefaults.TestAppKey))).ToLowerInvariant();
#pragma warning restore CA5351
    Equal(expectedSign, form["sign"]);

    var stored = repository.Invoices.LoadOrCreate().Single();
    InvoiceAllowanceMetadata.ApplyQuery(stored, [Allowance(UploadStatuses.Processing)]);
    repository.Invoices.Save([stored]);

    var third = await service.GetAsync("ALW-001", 0);
    Equal(false, third.FromCache);
    NotEqual(first.Path, third.Path);
    Equal(4, handler.Requests.Count);
    Equal(false, File.Exists(first.Path));
}

static async Task TestPdfTimeSyncAsync()
{
    using var temporary = new TemporaryDirectory("pdf-time");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var postCount = 0;
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post)
        {
            postCount++;
            return postCount == 1
                ? JsonResponse("{\"code\":15,\"msg\":\"time\",\"data\":{}}")
                : JsonResponse(FileSuccess("https://invoice.amego.tw/allowance-time.pdf"));
        }
        if (request.RequestUri?.AbsolutePath == "/json/time")
            return JsonResponse("{\"timestamp\":1789878660}");
        if (request.RequestUri?.Host == "invoice.amego.tw") return PdfResponse("time-pdf");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client, () =>
        new DateTimeOffset(2026, 9, 20, 12, 30, 0, TimeSpan.FromHours(8)));

    var result = await service.GetAsync("ALW-001", 1);
    Equal(false, result.FromCache);
    Equal(2, postCount);
    Equal(4, handler.Requests.Count);
    Equal(1, handler.Requests.Count(item => item.Uri.AbsolutePath == "/json/time"));
}

static async Task TestPdfRejectsUntrustedUrlAsync()
{
    using var temporary = new TemporaryDirectory("pdf-url");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
        request.Method == HttpMethod.Post
            ? JsonResponse(FileSuccess("https://example.com/not-amego.pdf"))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client);

    await ThrowsAsync<InvalidDataException>(() => service.GetAsync("ALW-001", 3));
    Equal(1, handler.Requests.Count);
}

static async Task TestPdfRejectsInvalidContentAsync()
{
    using var temporary = new TemporaryDirectory("pdf-invalid");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post) return JsonResponse(FileSuccess("https://invoice.amego.tw/not-pdf"));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("not a pdf"u8.ToArray()) };
    });
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client);

    await ThrowsAsync<InvalidDataException>(() => service.GetAsync("ALW-001", 0));
    Equal(2, handler.Requests.Count);
}

static async Task TestAllowanceVoidWrongCredentialsAsync()
{
    using var temporary = new TemporaryDirectory("void-wrong");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, "ALW-001", "3015", "wrong", "退貨"));
    Equal(0, setup.Gateway.QueryCalls);
}

static async Task TestAllowanceVoidEligibilityAsync()
{
    using (var temporary = new TemporaryDirectory("void-pending"))
    {
        var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Processing)]);
        await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
            setup.Record, "ALW-001", "3015", "Employee1", "退貨"));
        Equal(1, setup.Gateway.QueryCalls);
        Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    }

    using (var temporary = new TemporaryDirectory("void-duplicate-official"))
    {
        var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete), Allowance(UploadStatuses.Complete)]);
        await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
            setup.Record, "ALW-001", "3015", "Employee1", "退貨"));
        Equal(1, setup.Gateway.QueryCalls);
        Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    }
}

static async Task TestAllowanceVoidDuplicateAsync()
{
    using var temporary = new TemporaryDirectory("void-idempotent");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    var first = await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var second = await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");

    Equal(false, first.AlreadyQueued);
    Equal(true, second.AlreadyQueued);
    Equal(2, setup.Gateway.QueryCalls);
    Equal(1, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidEmployeeCannotCompleteAsync()
{
    using var temporary = new TemporaryDirectory("void-auth");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    Throws<UnauthorizedAccessException>(() => setup.Workflow.MarkManualCompleted(issue, "3015", "Employee1"));
    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(true, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()) is not null);
}

static async Task TestAllowanceVoidManagerCompletesLocallyAsync()
{
    using var temporary = new TemporaryDirectory("void-complete");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    setup.Workflow.MarkManualCompleted(issue, "2000", "AdminPass1");

    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidManagerCancelsLocallyAsync()
{
    using var temporary = new TemporaryDirectory("void-cancel");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");

    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidAdministrativeClosureAsync()
{
    using var temporary = new TemporaryDirectory("void-admin-close");
    var clock = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));
    var setup = CreateVoidSetup(
        temporary.Path,
        [Allowance(UploadStatuses.Complete, date: "20260630")],
        invoiceDate: "20260630",
        clock: clock);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var closure = new InvoiceAdministrativeClosureService(setup.Repository, () => clock);

    Equal(true, closure.CanClose(issue));
    closure.Close(issue, "2000", "AdminPass1");

    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    Equal(0, setup.Gateway.OtherCalls);
}

static LocalRepository CreatePdfRepository(string path, int allowanceStatus)
{
    var repository = TestRepository.Open(path, new TestProtector());
    var record = NewRecord("2026/09/20");
    InvoiceAllowanceMetadata.ApplyQuery(record, [Allowance(allowanceStatus)]);
    repository.Invoices.Append(record);
    return repository;
}

static VoidSetup CreateVoidSetup(
    string path,
    IReadOnlyList<InvoiceAllowanceResult> allowances,
    string invoiceDate = "20260920",
    DateTimeOffset? clock = null)
{
    var repository = TestRepository.Open(path, new TestProtector());
    repository.Employees.CreateFirstSuperAdmin("0001", "超管", "super@example.com", "SuperPass1");
    repository.Employees.CreateEmployee("0001", "3015", "員工", "employee@example.com", "Employee1");
    repository.Employees.CreateEmployee("0001", "2000", "管理員", "admin@example.com", "AdminPass1", EmployeeRoles.Admin);

    var displayDate = invoiceDate.Length == 8
        ? invoiceDate[..4] + "/" + invoiceDate[4..6] + "/" + invoiceDate[6..]
        : invoiceDate;
    var record = NewRecord(displayDate);
    repository.Invoices.Append(record);

    var gateway = new FakeGateway { QueryResponse = Query(allowances, invoiceDate) };
    var current = clock ?? new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));
    var detail = new InvoiceDetailRefreshService(repository, (_, _) => gateway, () => current);
    var workflow = new EmployeeAllowanceVoidWorkflowService(repository, detail, () => current);
    return new VoidSetup(repository, gateway, workflow, record);
}

static InvoiceRecord NewRecord(string invoiceDate) => new()
{
    Id = Guid.NewGuid().ToString("N"),
    SellerInvoice = AmegoDefaults.TestInvoice,
    Environment = Environments.Test,
    Source = InvoiceSources.Manual,
    RecordOrigin = RecordOrigins.Local,
    OriginalOrderId = "M20260920001",
    OrderId = "M20260920001",
    ApiOrderId = "M20260920001",
    InvoiceNumber = "AA12345678",
    InvoiceState = InvoiceStates.Opened,
    Amount = 100,
    Delivery = InvoiceService.DeliveryPaper,
    UploadStatus = UploadStatuses.Complete,
    UploadStatusText = "完成",
    InvoiceDate = invoiceDate,
    InvoiceTime = "12:00:00",
    Items = [],
};

static InvoiceAllowanceResult Allowance(int status, string date = "20260920") =>
    new("D0401", status, 2, "ALW-001", date, "5", "95");

static QueryResponse Query(IReadOnlyList<InvoiceAllowanceResult> allowances, string invoiceDate)
{
    var data = new QueryResult(
        InvoiceNumber: "AA12345678",
        InvoiceType: "A0401",
        InvoiceStatus: UploadStatuses.Complete,
        InvoiceDate: invoiceDate,
        InvoiceTime: "120000",
        BuyerIdentifier: "",
        BuyerName: "消費者",
        SalesAmount: "95",
        TaxAmount: "5",
        TotalAmount: "100",
        CarrierType: "",
        CarrierId1: "",
        CarrierId2: "",
        NpoBan: "",
        CancelDate: 0,
        OrderId: "M20260920001",
        CreateDate: 1789876800,
        ProductItems: JsonSerializer.SerializeToElement(Array.Empty<object>()),
        DetailVat: 1,
        DetailVatPresent: true)
    {
        Allowances = allowances,
    };
    return new QueryResponse(0, "", data);
}

static IReadOnlyList<InvoiceSyncIssue> UnresolvedAllowanceVoidIssues(LocalRepository repository) =>
    new InvoiceSyncIssueStore(repository.DataDirectory)
        .Unresolved(Environments.Test + "|" + AmegoDefaults.TestInvoice)
        .Where(issue => issue.IssueType == InvoiceAllowanceVoidIssueTypes.ManualReview)
        .ToArray();

static string FileSuccess(string url) =>
    JsonSerializer.Serialize(new { code = 0, msg = "", data = new { file_url = url } });

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json"),
};

static HttpResponseMessage PdfResponse(string marker)
{
    var body = Encoding.UTF8.GetBytes("%PDF-1.4\n" + marker + "\n%%EOF");
    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
    return response;
}

static Dictionary<string, string> ParseForm(string body)
{
    return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            item => Uri.UnescapeDataString(item[0].Replace('+', ' ')),
            item => Uri.UnescapeDataString((item.Length > 1 ? item[1] : string.Empty).Replace('+', ' ')),
            StringComparer.Ordinal);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

static void NotEqual<T>(T first, T second)
{
    if (EqualityComparer<T>.Default.Equals(first, second))
        throw new InvalidOperationException($"values should differ: {first}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

sealed record VoidSetup(
    LocalRepository Repository,
    FakeGateway Gateway,
    EmployeeAllowanceVoidWorkflowService Workflow,
    InvoiceRecord Record);

sealed record RequestSnapshot(HttpMethod Method, Uri Uri, string Body);

sealed class RecordingHttpHandler(
    Func<HttpRequestMessage, string, int, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RequestSnapshot> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("request URI missing");
        Requests.Add(new RequestSnapshot(request.Method, uri, body));
        return responder(request, body, Requests.Count);
    }
}

sealed class FakeGateway : IAmegoGateway
{
    public QueryResponse QueryResponse { get; set; } = null!;
    public int QueryCalls { get; private set; }
    public int OtherCalls { get; private set; }

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return Task.FromResult(QueryResponse);
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return Task.FromResult(QueryResponse);
    }

    public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }
}

sealed class TestProtector : ISecretProtector
{
    private const string Prefix = "allowance-test:";
    public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
    public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
        ? Convert.FromBase64String(ciphertext[Prefix.Length..])
        : throw new InvalidDataException("invalid test secret");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string name)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CYInvoice-allowance-tests-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
