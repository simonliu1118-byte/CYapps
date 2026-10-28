using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class AllowanceDiagnosticsTests
{
    public static async Task RunAsync()
    {
        using var temp=new AuthorityTemporaryDirectory();
        var data=Path.Combine(temp.Path,"Data");Directory.CreateDirectory(data);SqliteBootstrapper.EnsureMigrated(data);
        File.WriteAllText(Path.Combine(temp.Path,"VERSION"),"3.1.44");File.WriteAllText(Path.Combine(temp.Path,"BUILD"),"1");
        var log=IssueDiagnosticLog.LogDirectory(data);
        Check(log==Path.Combine(temp.Path,"log"),"log must be beside Data/EXE");
        using var handler=new Handler();using var http=new HttpClient(handler);
        var api=new CloudAllowanceClient(http,new Uri("https://example.invalid/"),"cydev_"+new string('a',64),data);
        Check((await api.ListAsync("1234","SYNTHETIC_PASSWORD",limit:1)).Count==1,"real nonempty seven-field contract");
        handler.Mode="error";
        try {await api.ApproveOperationAsync(new("application_synthetic_log","issue","open",1,[],"",""),"1234","SYNTHETIC_PASSWORD");throw new Exception("Expected error");}
        catch(CloudApiException error) {
            Check(error.Code=="ALLOWANCE_APPROVAL_READBACK_REQUIRED"&&error.Data["CloudStage"]?.ToString()=="approval_save_started","preserve safe error/stage");
        }
        handler.Mode="timeout";
        try {await api.ApproveOperationAsync(new("application_synthetic_log","issue","open",1,[],"",""),"1234","SYNTHETIC_PASSWORD");throw new Exception("Expected timeout");}
        catch(TaskCanceledException) { }
        Check(handler.Approvals==2,"each explicit approval exactly once; no retry");
        handler.Mode="diagnostics";
        await api.ExportOperationDiagnosticsAsync("application_synthetic_log",false,"1234","SYNTHETIC_PASSWORD",maxPages:1);
        Check(handler.Diagnostics==1,"automatic failure diagnostics are a single bounded page");
        var text=string.Join("\n",Directory.EnumerateFiles(log,"cloud-*.jsonl").Select(File.ReadAllText));
        foreach(var line in text.Split('\n',StringSplitOptions.RemoveEmptyEntries)) {
            using var doc=JsonDocument.Parse(line);Check(doc.RootElement.GetProperty("requestId").GetString()!.Length>0,"request correlation");
        }
        Check(text.Contains("approval_save_started")&&text.Contains("ALLOWANCE_APPROVAL_READBACK_REQUIRED")&&
            text.Contains("ALLOWANCE_APPROVAL_SOURCE_INVALID")&&text.Contains("elapsedMs")&&text.Contains("TaskCanceledException")&&text.Contains("server_event")&&text.Contains("\"build\":1"),"stages, errors and timing survive");
        Check(!text.Contains("SYNTHETIC_PASSWORD")&&!text.Contains("SYNTHETIC_BUYER_SECRET")&&!text.Contains("cydev_"),"never record credentials or raw response");
        var store=new InvoiceSyncIssueStore(data);
        store.ApplyCloudAllowance("test|12345678","application_synthetic_log","issue","processing","open","",originalInvoiceNumber:"AA00000001",originalOrderId:"SYNTHETIC_ORDER");
        var issue=store.Unresolved("test|12345678").Single();store.UpdateMessage(issue.Id,"presentation changed");
        Check(store.CloudProjection(issue.AccountKey,issue.OrderId)?.Status=="open","status is independent of presentation");
        store.ApplyCloudAllowance(issue.AccountKey,issue.OrderId,"issue","problem","open","SYNTHETIC_ERROR");
        Check(store.Unresolved(issue.AccountKey).Single().IssueType=="cloud_allowance_issue_problem"&&
            store.CloudProjection(issue.AccountKey,issue.OrderId)?.OriginalInvoiceNumber=="AA00000001","failure classification retains identity");
        // Old issue files remain exportable, without moving or rewriting them.
        var legacy=Path.Combine(data,"Logs","IssueDiagnostics");Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy,issue.Id+".jsonl"),"{\"AtUtc\":\"2026-10-28T00:00:00Z\",\"Stage\":\"old_evidence\",\"IssueType\":\"sync_issue\",\"Code\":\"0\"}");
        Check(IssueDiagnosticLog.Export(data,issue).Contains("old_evidence"),"legacy evidence remains read-only accessible");
        using var blocked=new AuthorityTemporaryDirectory();var blockedData=Path.Combine(blocked.Path,"Data");Directory.CreateDirectory(blockedData);
        File.WriteAllText(Path.Combine(blocked.Path,"log"),"blocked");
        handler.Mode="ok";var blockedApi=new CloudAllowanceClient(http,new Uri("https://example.invalid/"),"cydev_"+new string('a',64),blockedData);
        Check((await blockedApi.ListAsync("1234","SYNTHETIC_PASSWORD",limit:1)).Count==1&&blockedApi.DiagnosticWriteProblem.Length>0,
            "log failure is visible without changing confirmed business response");
        // Rotation is local-only, bounded; issue evidence is never removed by daily HTTP cleanup.
        var day=DateTime.UtcNow.ToString("yyyyMMdd");var current=Path.Combine(log,"cloud-"+day+".jsonl");
        using(var stream=new FileStream(current,FileMode.Open,FileAccess.Write)){stream.SetLength(4*1024*1024);}
        Check(IssueDiagnosticLog.RecordCloud(data,"status","rotation","synthetic_request"),"rotation succeeds");
        Check(File.Exists(Path.Combine(log,"cloud-"+day+"-previous.jsonl")),"bounded previous segment retained");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class Handler:HttpMessageHandler
    {
        public string Mode="ok";public int Approvals;public int Diagnostics;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/approve"))Approvals++;
            if(Mode=="timeout")return Task.FromException<HttpResponseMessage>(new TaskCanceledException("SYNTHETIC_PASSWORD must never be logged"));
            if(Mode=="diagnostics") {
                Diagnostics++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new {
                    ok=true,applicationId="application_synthetic_log",hasMore=true,nextEventId=1,
                    events=new[]{new {stage="approval_save_started",request_id="synthetic_server",error_code="ALLOWANCE_APPROVAL_READBACK_REQUIRED",
                        http_status=409,response_received=1,attempt=1,provider_code=0,recorded_at="2026-10-28T00:00:00Z",privatePayload="SYNTHETIC_BUYER_SECRET"}}
                })});
            }
            object body=Mode=="error"?new {ok=false,error=new {code="ALLOWANCE_APPROVAL_READBACK_REQUIRED",stage="approval_save_started",causeCode="ALLOWANCE_APPROVAL_SOURCE_INVALID",diagnosticSaved=false},privatePayload="SYNTHETIC_BUYER_SECRET"}:
                new {ok=true,items=new[]{new {applicationId="application_synthetic_log",status="open",revision=1,requestedGross="105",environment="test",updatedAt="2026-10-28T00:00:00Z",executionState="processing"}}};
            return Task.FromResult(new HttpResponseMessage(Mode=="error"?HttpStatusCode.Conflict:HttpStatusCode.OK){Content=JsonContent.Create(body)});
        }
    }
}
