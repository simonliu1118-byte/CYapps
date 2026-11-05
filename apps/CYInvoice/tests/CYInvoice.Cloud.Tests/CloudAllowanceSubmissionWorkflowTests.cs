using System.Net;
using Microsoft.Data.Sqlite;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class CloudAllowanceSubmissionWorkflowTests
{
    public static async Task RunAsync()
    {
        foreach (var mode in new[] { "success", "committed-error", "timeout", "rejected" }) {
            using var temporary = new AuthorityTemporaryDirectory();
            var repository = TestRepository.Open(temporary.Path, new Protector());
            var settings = repository.Settings.LoadOrCreate();
            settings.Environment = Environments.Test;
            settings.CloudBaseUrl = "https://cloud.example.test/";
            settings.CloudWorkspaceId = "ws_services";
            settings.CloudDeviceId = "dev_services";
            repository.Settings.SetCloudDeviceToken(settings, "cydev_" + new string('a',64));
            repository.Settings.ConfirmCyIdConfiguration(settings, new CyIdBinding(settings.CloudBaseUrl,
                settings.CloudWorkspaceId, settings.CloudDeviceId, "identity_services", "CYINVOICE", "1.0.3",
                CyIdGateway.TokenDigest(repository.Settings.CloudDeviceToken(settings))));
            repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
            repository.Settings.Save(settings);
            var invoice = new InvoiceRecord { Id="invoice_synthetic", Environment=Environments.Test,
                SellerInvoice=AmegoDefaults.TestInvoice, InvoiceNumber="AA12345678", InvoiceDate="2026/10/27",
                OrderId="SYNTHETIC_ORDER", ApiOrderId="SYNTHETIC_ORDER", Amount=105,
                BuyerIdentifier="0000000000", InvoiceState=InvoiceStates.Opened, UploadStatus=99 };
            repository.Invoices.Append(invoice);
            using var handler = new Handler(mode); using var http = new HttpClient(handler);
            var service = new CloudAllowanceSubmissionService(repository, http,
                new InvoiceDetailRefreshService(repository, (_,_)=>new Gateway()));
            AllowanceProductItem[] items = [new() { OriginalInvoiceNumber=invoice.InvoiceNumber,
                OriginalInvoiceDate=20261027, OriginalDescription="Synthetic product", Quantity=1,
                UnitPrice=100, Amount=100, Tax=5, TaxType=1 }];
            Exception? failure=null;
            CloudAllowanceSubmissionResult? result=null;
            try { result=await service.SubmitAsync(invoice,"3002","SYNTHETIC_PASSWORD","Synthetic reason",105,items); }
            catch (Exception error) { failure=error; }
            Check(handler.Creates==1, "preflight failed in "+mode+": "+failure?.Message);
            var issue = new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("test|"+AmegoDefaults.TestInvoice).Single();
            Check(issue.OrderId==handler.Id && CloudAllowanceSubmissionService.PendingApplicationId(repository.Invoices.LoadOrCreate().Single())==handler.Id,
                "original application ID must survive every outcome");
            var confirmed=mode is "success" or "committed-error";
            Check((failure is null)==confirmed, "only confirmed receipt/readback is successful: "+mode+" "+failure?.Message);
            Check(issue.IssueType==(confirmed?"cloud_allowance_issue_processing":"cloud_allowance_issue_problem"),"task classification");
            Check(handler.Readbacks==(mode=="success"?0:1), "one bounded original-ID readback: "+mode+" count="+handler.Readbacks+" failure="+failure?.Message);
            if(confirmed)Check(result?.Status=="open" && issue.Message=="等待主管核准", "pending supervisor is a successful application");
            var diagnostics=IssueDiagnosticLog.Export(repository.DataDirectory,issue);
            Check(!diagnostics.Contains("SYNTHETIC_PASSWORD")&&!diagnostics.Contains("SYNTHETIC_ORDER"),"diagnostics exclude secrets/business payload");
            if(!confirmed)Check(diagnostics.Contains("create_response")&&diagnostics.Contains("create_readback"),"both uncertain stages retained");
            try { await service.SubmitAsync(invoice,"3002","SYNTHETIC_PASSWORD","Synthetic reason",105,items); } catch(InvalidOperationException) { }
            Check(handler.Creates==1,"reentry cannot create another financial application");
            if(mode=="success") {
                using (var db = new SqliteConnection("Data Source=" + Path.Combine(repository.DataDirectory, SqliteBootstrapper.DatabaseFileName))) {
                    db.Open();using var sql=db.CreateCommand();sql.CommandText="DELETE FROM sync_issues";sql.ExecuteNonQuery();
                }
                var connection = new ServiceConnectivity(repository.Settings,http,baseDirectory:temporary.Path);
                Check((await connection.CheckAsync()).CloudAvailable,"restored issue uses existing sync lifecycle");
                Check(new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("test|"+AmegoDefaults.TestInvoice).Single().OrderId==handler.Id,
                    "pre-fix pending marker cannot disappear from work queue");
                Check(handler.Creates==1,"marker restoration never sends another create");
            }
        }
    }
    private static void Check(bool value,string text) { if(!value)throw new InvalidOperationException(text); }
    private sealed class Handler(string mode) : HttpMessageHandler
    {
        public int Creates;public int Readbacks;public string Id="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!.AbsolutePath=="/json/time")return Reply(new {timestamp=1791626400});
            if(request.RequestUri.AbsolutePath=="/v1/runtime/sync")return Reply(new {ok=true,
                device=new {workspaceId="ws_services",deviceId="dev_services",status="active",workspaceStatus="active"},
                binding=new {provider="CYID",workspaceId="ws_services",identityWorkspaceId="identity_services",applicationId="CYINVOICE",consumerVersion="1.0.3"},
                permissions=new {ok=true,invalidated=Array.Empty<string>()}});
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/list"))return Reply(new {ok=true,items=Array.Empty<object>()});
            if(path.EndsWith("/create")) {
                Creates++;Id=json.RootElement.GetProperty("applicationId").GetString()!;
                if(mode=="timeout")throw new TaskCanceledException("Synthetic transport timeout");
                if(mode!="success")return Reply(new {ok=false,error=new {code="ALLOWANCE_ATOMIC_CREATE_NOT_CONFIRMED"}},HttpStatusCode.Conflict);
                return Reply(new {ok=true,applicationId=Id,status="open",revision=1,officialActionPerformed=false});
            }
            Check(path.EndsWith("/status"),"unexpected request");Readbacks++;
            Check(json.RootElement.GetProperty("applicationId").GetString()==Id,"readback uses original ID");
            if(mode is "timeout" or "rejected")return Reply(new {ok=false,error=new {code="NOT_FOUND"}},HttpStatusCode.NotFound);
            return Reply(new {ok=true,applicationId=Id,status="open",revision=1,executionState="processing",officialActionPerformed=false,officiallyConfirmed=false});
        }
        private static HttpResponseMessage Reply(object value,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=JsonContent.Create(value)};
    }
    private sealed class Protector : ISecretProtector
    {
        public string Protect(ReadOnlySpan<byte> value)=>Convert.ToBase64String(value);
        public byte[] Unprotect(string value)=>Convert.FromBase64String(value);
    }
    private sealed class Gateway : IAmegoGateway
    {
        private static Task<T> Unexpected<T>()=>Task.FromException<T>(new InvalidOperationException("Unexpected financial call"));
        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number,CancellationToken token=default)=>Task.FromResult(new QueryResponse(0,"",new QueryResult(
            number,"07",99,"20261027","120000","0000000000","Synthetic buyer","100","5","105","","","","",0,"SYNTHETIC_ORDER",0,
            JsonSerializer.SerializeToElement(new[]{new {description="Synthetic product",quantity=1,unit_price=100,amount=100,tax_type=1}}),1,true){AllowanceSummaryPresent=true}));
        public Task<QueryResponse> QueryByOrderIdAsync(string id,CancellationToken token=default)=>Unexpected<QueryResponse>();
        public Task<IssueResponse> IssueAsync(IssueRequest r,CancellationToken token=default)=>Unexpected<IssueResponse>();
        public Task<StatusResponse> StatusAsync(IEnumerable<string> ids,CancellationToken token=default)=>Unexpected<StatusResponse>();
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> ids,CancellationToken token=default)=>Unexpected<BanResponse>();
        public Task<byte[]> DownloadInvoicePdfAsync(string id,int style,CancellationToken token=default)=>Unexpected<byte[]>();
    }
}
