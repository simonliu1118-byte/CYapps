using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class CloudRecoveryClientTests
{
    private const string Endpoint = "https://fixture.invalid/";
    private const string Code = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Password = "Synthetic8";
    private const string Invitation = "inv_11111111-1111-4111-8111-111111111111";
    private const string Device = "dev_22222222-2222-4222-8222-222222222222";
    private static readonly string Token = "cydev_" + new string('a', 64);

    public static async Task RunAsync()
    {
        await UnicodeCredentialsAsync();
        await DurablePendingAndLostReplyAsync();
        await RejectedRepliesAsync();
    }

    private static async Task UnicodeCredentialsAsync()
    {
        foreach (var password in new[] {new string('x',8),new string('x',16),
            string.Concat(Enumerable.Repeat("\U0001F600",9)),string.Concat(Enumerable.Repeat("\U0001F600",16))})
        {
            using var handler = new IsolatedHandler(async request => {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Check(request.Headers.Authorization is null,"Recovery cannot forward an old bearer.");
                Check(body.RootElement.GetProperty("password").GetString()==password,"Password must be exact.");
                Check(body.RootElement.GetProperty("code").GetString()==Code.ToLowerInvariant(),"Code normalization.");
                return PreviewReply();
            });
            using var http = new HttpClient(handler);
            var client = new CloudRecoveryClient(http,new Uri(Endpoint));
            var preview = await client.PreviewAsync(Code,"0001",password);
            Check(preview.WorkspaceId=="business_fixture" && handler.Calls==1,"Valid Unicode credential reaches only fixture.");
            var pending = new CloudPendingRecoveryState(Endpoint,"business_fixture","identity_fixture",Invitation,
                "Fixture Device",DateTimeOffset.UtcNow,Token);
            using var claimHandler = new IsolatedHandler(_=>Task.FromResult(ClaimReply()));
            using var claimHttp = new HttpClient(claimHandler);
            await new CloudRecoveryClient(claimHttp,new Uri(Endpoint)).ClaimAsync(preview,pending,
                Code,"0001",password,"3.0.0");
            Check(claimHandler.Calls==1,"Claim accepts same Unicode boundary.");
        }
        foreach (var password in new[] {new string('x',7),new string('x',17),
            string.Concat(Enumerable.Repeat("\U0001F600",7)),string.Concat(Enumerable.Repeat("\U0001F600",17))})
        {
            using var handler=new IsolatedHandler(_=>throw new Exception("Invalid credentials reached transport."));
            using var http=new HttpClient(handler);
            await ThrowsAsync<ArgumentException>(()=>new CloudRecoveryClient(http,new Uri(Endpoint)).PreviewAsync(Code,"0001",password));
            Check(handler.Calls==0,"Invalid password rejected before transport.");
        }
    }

    private static async Task DurablePendingAndLostReplyAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"CYInvoice.V3.Isolated",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sentinel=Path.Combine(directory,"synthetic-business-data.txt");
            File.WriteAllText(sentinel,"fixture must survive a pending recovery");
            var store=new SettingsStore(directory,new TestProtector());
            var settings=store.LoadOrCreate();
            store.SetCloudPendingRecovery(settings,Endpoint,"business_fixture","identity_fixture",Invitation,
                "Fixture Device",Token,DateTimeOffset.UtcNow);
            store.Save(settings);
            var saved=File.ReadAllText(Path.Combine(directory,"settings.json"));
            Check(!saved.Contains(Token,StringComparison.Ordinal)&&!saved.Contains(Password,StringComparison.Ordinal)
                &&!saved.Contains(Code,StringComparison.Ordinal),"Settings cannot contain transport credentials.");
            var restarted=new SettingsStore(directory,new TestProtector());
            var restartedSettings=restarted.LoadOrCreate();
            var pending=restarted.CloudPendingRecovery(restartedSettings) ?? throw new Exception("Pending token lost.");
            Check(pending.DeviceToken==Token,"Restart preserves exact pending token.");
            var preview=new CloudRecoveryPreview("business_fixture","Fixture","identity_fixture","CYINVOICE",Invitation,DateTimeOffset.UtcNow.AddMinutes(10));
            using(var handler=new IsolatedHandler(_=>throw new HttpRequestException("Synthetic lost reply.")))
            using(var http=new HttpClient(handler))
            {
                await ThrowsAsync<HttpRequestException>(()=>new CloudRecoveryClient(http,new Uri(Endpoint)).ClaimAsync(
                    preview,pending,Code,"0001",Password,"3.0.0"));
                Check(handler.Calls==1,"Lost reply cannot trigger automatic resend.");
            }
            var afterFailure=new SettingsStore(directory,new TestProtector());
            var afterSettings=afterFailure.LoadOrCreate();
            var samePending=afterFailure.CloudPendingRecovery(afterSettings) ?? throw new Exception("Lost pending attempt.");
            Check(samePending==pending,"Failure preserves scope, start time and token.");
            await ThrowsAsync<InvalidOperationException>(()=>{
                afterFailure.SetCloudPendingRecovery(afterSettings,Endpoint,"business_fixture","identity_fixture",Invitation,
                    "Fixture Device","cydev_"+new string('b',64),DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            });
            using var resumeHandler=new IsolatedHandler(async request=>{
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Check(body.RootElement.GetProperty("deviceToken").GetString()==Token,"Resume must reuse original token.");
                Check(request.Headers.Authorization is null,"No CYWEB or old device bearer forwarded.");
                return ClaimReply();
            });
            using var resumeHttp=new HttpClient(resumeHandler);
            var resumed=await new CloudRecoveryClient(resumeHttp,new Uri(Endpoint)).ClaimAsync(
                preview,samePending,Code,"0001",Password,"3.0.0");
            Check(resumed.Recovered&&resumed.DeviceToken==Token&&resumed.DeviceId==Device,"Confirmed same attempt resume.");
            using var scopeHandler=new IsolatedHandler(_=>throw new Exception("Wrong scope reached transport."));
            using var scopeHttp=new HttpClient(scopeHandler);
            await ThrowsAsync<InvalidOperationException>(()=>new CloudRecoveryClient(scopeHttp,new Uri(Endpoint)).ClaimAsync(
                preview,samePending with {WorkspaceId="other_fixture"},Code,"0001",Password,"3.0.0"));
            Check(scopeHandler.Calls==0,"Scope mismatch cannot send credentials.");
            Check(File.ReadAllText(sentinel)=="fixture must survive a pending recovery","Existing business files retained.");
        }
        finally {Directory.Delete(directory,true);}
    }

    private static async Task RejectedRepliesAsync()
    {
        foreach(var reply in new Func<HttpResponseMessage>[] {
            ()=>Reply(HttpStatusCode.OK,new {ok=true,workspace=new {workspaceId="business_fixture",displayName="Fixture"},
                identity=new {identityWorkspaceId="identity_fixture",applicationId="OTHER"},
                invitation=new {invitationId=Invitation,expiresAt=DateTimeOffset.UtcNow.AddMinutes(10)}}),
            ()=>Reply(HttpStatusCode.OK,new {ok=true,workspace=new {workspaceId="business_fixture",displayName="Fixture"},
                identity=new {identityWorkspaceId="identity_fixture",applicationId="CYINVOICE"},
                invitation=new {invitationId=Invitation,expiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)}})
        }) {
            using var handler=new IsolatedHandler(_=>Task.FromResult(reply()));
            using var http=new HttpClient(handler);
            await ThrowsAsync<InvalidDataException>(()=>new CloudRecoveryClient(http,new Uri(Endpoint)).PreviewAsync(Code,"0001",Password));
        }
        using var revokedHandler=new IsolatedHandler(_=>Task.FromResult(Reply(HttpStatusCode.Conflict,
            new {ok=false,error=new {code="RECOVERY_INVITATION_UNAVAILABLE"}})));
        using var revokedHttp=new HttpClient(revokedHandler);
        await ThrowsAsync<CloudApiException>(()=>new CloudRecoveryClient(revokedHttp,new Uri(Endpoint)).PreviewAsync(Code,"0001",Password));
        Check(revokedHandler.Calls==1,"Denied invitation is not retried.");
    }

    private static HttpResponseMessage PreviewReply()=>Reply(HttpStatusCode.OK,new {ok=true,
        workspace=new {workspaceId="business_fixture",displayName="Fixture"},
        identity=new {identityWorkspaceId="identity_fixture",applicationId="CYINVOICE"},
        invitation=new {invitationId=Invitation,expiresAt=DateTimeOffset.UtcNow.AddMinutes(10)}});
    private static HttpResponseMessage ClaimReply()=>Reply(HttpStatusCode.OK,new {ok=true,recovered=true,
        workspace=new {workspaceId="business_fixture"},device=new {deviceId=Device,displayName="Fixture Device"}});
    private static HttpResponseMessage Reply(HttpStatusCode status,object value)=>new(status) {
        Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static async Task ThrowsAsync<T>(Func<Task> run) where T:Exception {
        try {await run();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");
    }
    private sealed class IsolatedHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler {
        internal int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
            Check(request.RequestUri?.Host=="fixture.invalid","External network is forbidden.");
            Calls++;return response(request);
        }
    }
}
