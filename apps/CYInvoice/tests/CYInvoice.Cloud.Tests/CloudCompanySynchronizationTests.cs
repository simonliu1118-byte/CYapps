using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class CloudCompanySynchronizationTests
{
    public static async Task RunAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var protector = new Protector();
        var store = new SettingsStore(temporary.Path, protector);
        var original = store.LoadOrCreate();
        original.CloudBaseUrl = "https://cloud.example.test/";
        original.CloudWorkspaceId = "ws_synthetic";
        original.CloudDeviceId = "dev_synthetic";
        original.CloudIdentityProvider = "CYID";
        original.CyIdBindingEncrypted = "SYNTHETIC_BINDING";
        original.CloudMode = CloudModes.CloudPreferred;
        original.CloudEmployeeAuthorityReady = true;
        store.SetCloudDeviceToken(original, "cydev_" + new string('a', 64));
        store.Save(original);
        var path = Path.Combine(temporary.Path, "settings.json");
        var revision = 1;
        var sends = 0;
        Action? duringReply = null;
        var handler = new Handler(async request =>
        {
            sends++;
            Check(request.RequestUri!.AbsolutePath == "/v1/company-settings/sync", "existing card endpoint");
            Check(request.Headers.Authorization?.Parameter == store.CloudDeviceToken(original), "same Device proof");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(!body.RootElement.TryGetProperty("password", out _), "automatic synchronization retains no employee password");
            var known = body.RootElement.GetProperty("knownRevision").GetInt32();
            duringReply?.Invoke();
            var response = new Dictionary<string, object?> {
                ["ok"] = true,
                ["company"] = new {sellerInvoice="12345678",companyName="合成測試公司",revision,hasAmegoAppKey=true,hasMoExcelPassword=true}
            };
            if (known != revision) response["credentials"] = new {sellerInvoice="12345678",companyName="合成測試公司",revision,
                amegoAppKey="SYNTHETIC_APP_KEY",moExcelPassword="SYNTHETIC_MO_"+revision};
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=JsonContent.Create(response) };
        });
        using var http = new HttpClient(handler);
        var client = new CloudCompanySettingsClient(http,new Uri(original.CloudBaseUrl),store.CloudDeviceToken(original));
        await client.SynchronizeAsync(store);
        var first = store.LoadOrCreate();
        Check(first.Environment == Environments.Test && first.CloudCompanyAppliedRevision == 1 &&
            first.ProductionCompanyName == "合成測試公司" && store.MoPassword(first) == "SYNTHETIC_MO_1",
            "new authorized Device receives complete card without changing its environment");
        Check(!File.ReadAllText(path).Contains("SYNTHETIC_APP_KEY") && !File.ReadAllText(path).Contains("SYNTHETIC_MO_"),
            "secrets never enter plaintext settings");
        var bytes = File.ReadAllBytes(path);
        await client.SynchronizeAsync(store);
        Check(bytes.SequenceEqual(File.ReadAllBytes(path)), "unchanged revision causes no DPAPI rewrite");
        revision = 2;
        await client.SynchronizeAsync(store);
        Check(store.MoPassword(store.LoadOrCreate()) == "SYNTHETIC_MO_2", "rotation updates original card");
        revision = 3;
        bytes = File.ReadAllBytes(path);
        protector.FailOnMo = true;
        await ThrowsAsync(() => client.SynchronizeAsync(store));
        Check(bytes.SequenceEqual(File.ReadAllBytes(path)), "second-secret DPAPI failure cannot partially save credentials or stamp");
        protector.FailOnMo = false;
        duringReply = () => {
            var changed=store.LoadOrCreate();
            changed.CloudBaseUrl="https://other.example.test/";
            store.Save(changed);
        };
        await ThrowsAsync(() => client.SynchronizeAsync(store));
        Check(store.LoadOrCreate().CloudCompanyAppliedRevision == 2 &&
            store.MoPassword(store.LoadOrCreate()) == "SYNTHETIC_MO_2", "binding changes during I/O reject stale response");
        Check(sends == 5, "no automatic request retry");
    }

    private static async Task ThrowsAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException("Expected synchronization failure.");
    }
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => respond(request);
    }
    private sealed class Protector : ISecretProtector
    {
        public bool FailOnMo {get;set;}
        public string Protect(ReadOnlySpan<byte> value)
        {
            if(FailOnMo && Encoding.UTF8.GetString(value).StartsWith("SYNTHETIC_MO_"))throw new IOException("Synthetic protection failure");
            return Convert.ToBase64String(value);
        }
        public byte[] Unprotect(string value) => Convert.FromBase64String(value);
    }
}
