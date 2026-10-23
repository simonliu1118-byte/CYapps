using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;
using Microsoft.Data.Sqlite;

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
    private static void ProjectionSkipsUnchangedWrites()
    {
        using var directory = new AuthorityTemporaryDirectory();
        SqliteBootstrapper.EnsureMigrated(directory.Path);
        var store = new InvoiceSyncIssueStore(directory.Path);
        store.ApplyCloudAllowance("test|12345678",Id,"issue","processing","approved","");
        using var db = new SqliteConnection("Data Source=" + Path.Combine(directory.Path,SqliteBootstrapper.DatabaseFileName));
        db.Open();using var sql = db.CreateCommand();
        sql.CommandText = "CREATE TRIGGER reject_unchanged_projection BEFORE UPDATE ON sync_issues WHEN NEW.issue_type=OLD.issue_type AND NEW.message=OLD.message BEGIN SELECT RAISE(ABORT,'UNCHANGED_PROJECTION_WAS_WRITTEN'); END;";
        sql.ExecuteNonQuery();
        store.ApplyCloudAllowance("test|12345678",Id,"issue","processing","approved","");
        store.ApplyCloudAllowance("test|12345678",Id,"issue","problem","approved","SYNTHETIC_ERROR");
        Equal(1,store.Unresolved("test|12345678").Count);
        Equal("cloud_allowance_issue_problem",store.Unresolved("test|12345678").Single().IssueType);
    }

    public static async Task RunAsync()
    {
        ProjectionSkipsUnchangedWrites();
        using var fake=new FakeHandler(); using var http=new HttpClient(fake);
        foreach(var uri in new[]{"http://example.invalid/","https://example.invalid/?x=1","https://user:pass@example.invalid/","https://example.invalid/#secret"})
            await Fail<ArgumentException>(()=>{_=new CloudAllowanceClient(http,new Uri(uri),Token);return Task.CompletedTask;});
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        fake.Add(r=>{Check(r,"detail");return Json(HttpStatusCode.OK,new {ok=true,applicationId=Id,
            operation="issue",status="open",revision=1,rows=new[]{new {label="原發票",value="AA00000001"}},requestHash="",officialSnapshotHash=""});});
        var detail=await api.OpenOperationAsync(Id,false,"0001","fake-password");
        Equal(1,detail.Revision); Equal("原發票",detail.Rows[0].Key);
        fake.Add(r=>{Check(r,"approve");using var body=JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result);
            Equal(1,body.RootElement.GetProperty("expectedRevision").GetInt32());
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,status="approved",revision=3,executionState="completed"});});
        var completed=await api.ApproveOperationAsync(detail,"0001","fake-password");
        Equal("completed",completed.ExecutionState);Equal(2,fake.Calls);Equal(0,fake.Remaining);
        fake.Add(r=>Json(HttpStatusCode.OK,new{ok=true,applicationId="wrong-identity",status="approved",executionState="completed"}));
        await Fail<InvalidDataException>(()=>api.ApproveOperationAsync(detail,"0001","fake-password"));
        fake.Add(r=>Json(HttpStatusCode.OK,new {ok=true,applicationId=Id,status="approved",revision=3,executionState="completed",officiallyConfirmed=true,allowanceNumber="A20261022001",ledgerStatus="sending"}));
        await Fail<InvalidDataException>(()=>api.StatusAsync(Id,"0001","fake-password"));
        fake.Add(r=>Json(HttpStatusCode.OK,new {ok=true,applicationId=Id,status="approved",revision=3,executionState="completed",officiallyConfirmed=true,allowanceNumber="A20261022001",ledgerStatus="confirmed"}));
        Equal("confirmed",(await api.StatusAsync(Id,"0001","fake-password")).LedgerStatus);
    }
    public static async Task RunVoidAsync()
    {
        var id=CloudAllowanceClient.OriginalVoidApplicationId("ws_synthetic","test","ALW-001");
        Equal(id,CloudAllowanceClient.OriginalVoidApplicationId("ws_synthetic","test","ALW-001"));
        Equal(false,id==CloudAllowanceClient.OriginalVoidApplicationId("ws_synthetic","test","ALW-002"));
        using var fake=new FakeHandler();using var http=new HttpClient(fake);
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        fake.Add(r=>{Check(r,"void-create");return Json(HttpStatusCode.OK,new{ok=true,applicationId=id,status="open",revision=1,claimed=false,readbackOnly=false,officiallyConfirmed=false,officialActionPerformed=false});});
        var result=await api.CreateVoidApplicationAsync(new(id,"test","ALW-001","AA00000001","合成退貨"),"0001","fake-password");
        Equal(id,result.ApplicationId);Equal(1,fake.Calls);
    }
    public static async Task RunVoidReconnectAsync()
    {
        using var fake=new FakeHandler();using var http=new HttpClient(fake);
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        var draft=new CloudAllowanceVoidDraft(Id,"test","ALW-001","AA00000001","合成退貨");
        fake.Add(r=>{Check(r,"void-status");return Json(HttpStatusCode.NotFound,new{ok=false,error=new{code="VOID_APPLICATION_NOT_FOUND"}});});
        fake.Add(r=>{Check(r,"void-create");return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,status="open",revision=1,claimed=false,readbackOnly=false,officiallyConfirmed=false,officialActionPerformed=false});});
        await api.CreateOrReadVoidApplicationAsync(draft,"0001","fake-password");Equal(2,fake.Calls);
        fake.Add(r=>{Check(r,"void-status");return Json(HttpStatusCode.ServiceUnavailable,new{ok=false,error=new{code="VOID_UNKNOWN"}});});
        await Fail<CloudApiException>(()=>api.CreateOrReadVoidApplicationAsync(draft,"0001","fake-password"));
        Equal(3,fake.Calls); // Unknown readback never creates a second request.
    }
    public static async Task RunVoidManagerAsync()
    {
        using var fake=new FakeHandler();using var http=new HttpClient(fake);
        var api=new CloudAllowanceClient(http,new Uri(Host),Token);
        fake.Add(r=>{Check(r,"detail");return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,
            operation="void",status="open",revision=1,rows=new[]{new{label="原折讓",value="ALW-001"}},requestHash=new string('a',64),officialSnapshotHash=new string('b',64)});});
        var detail=await api.OpenOperationAsync(Id,true,"0001","fake-password");
        fake.Add(r=>{Check(r,"void-approve");using var body=JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result);
            Equal(false,body.RootElement.TryGetProperty("confirmation",out _));
            return Json(HttpStatusCode.OK,new{ok=true,applicationId=Id,status="approved",revision=2,executionState="processing"});});
        Equal("processing",(await api.ApproveOperationAsync(detail,"0001","fake-password")).ExecutionState);
        fake.Add(r=>Json(HttpStatusCode.ServiceUnavailable,new{ok=false,error=new{code="OFFICIAL_SEND_DISABLED"}}));
        await Fail<CloudApiException>(()=>api.ApproveOperationAsync(detail,"0001","fake-password"));
        Equal(3,fake.Calls);Equal(0,fake.Remaining);
    }
}
