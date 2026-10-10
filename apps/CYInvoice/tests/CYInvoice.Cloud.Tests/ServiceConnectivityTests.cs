using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class ServiceConnectivityTests
{
    public static async Task RunAsync()
    {
        await AggregateSyncAsync();
        using var temporary = new AuthorityTemporaryDirectory();
        var services = new Services();
        using var http = new HttpClient(services);
        var repository = LocalRepository.Open(temporary.Path, new TestProtector(), http);
        var settings = repository.Settings.LoadOrCreate();
        settings.CloudBaseUrl = "https://cloud.example.test/";
        settings.CloudWorkspaceId = "ws_services";
        settings.CloudDeviceId = "dev_services";
        repository.Settings.SetCloudDeviceToken(settings, "cydev_" + new string('a', 64));
        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);
        var settingsBefore = File.ReadAllText(Path.Combine(repository.DataDirectory, "settings.json"));
        foreach (var ame in new[] { true, false })
        foreach (var cloud in new[] { true, false })
        {
            services.Amego = ame;
            services.Cloud = cloud;
            var state = await repository.Connections.CheckAsync();
            Check(state.AmegoAvailable == ame && state.CloudAvailable == cloud && state.CloudRequired
                && state.BlockAll == (!ame && !cloud) && state.UsesExistingFallback == (ame && !cloud),
                "four independent service states");
        }
        Check(File.ReadAllText(Path.Combine(repository.DataDirectory, "settings.json")) == settingsBefore,
            "availability checks never change Workspace, Device, Token or mode");

        services.Amego = true;
        services.Cloud = false;
        var cache = repository.CloudEmployees;
        var seed = BuiltInCloudAuthorityFreshnessTests.Seed;
        cache.ReplaceSnapshot("ws_services", 1, [
            seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1, true),
            seed("emp_user", "0002", "User", EmployeeRoles.User, "UserPass22", 1, 1, true),
        ]);
        var source = new ScriptedAuthoritySource("ws_services", _ => throw new CYInvoice.Core.Cloud.CloudApiException(
            "STORAGE_UNAVAILABLE", "temporary outage", HttpStatusCode.ServiceUnavailable));
        var provider = new BuiltInCloudIdentityProvider(cache, source, repository.Connections.RequireAmegoAsync);
        Check((await provider.AuthenticateAsync(new("0002", "UserPass22")))?.Role == AppRole.User,
            "503 plus reachable AMEGO reuses the original protected fallback");
        services.Amego = false;
        await ThrowsAsync<ServiceConnectionException>(() => provider.AuthenticateAsync(new("0002", "UserPass22")));
        Check(cache.LoadState()?.WorkspaceId == "ws_services", "both outages block without wiping the original cache");

        var record = new InvoiceRecord { Id = "retained", Source = "手動", OrderId = "M20261010001",
            Environment = Environments.Test, InvoiceNumber = "AA12345678", InvoiceState = InvoiceStates.Opened,
            Delivery = InvoiceService.DeliveryPaper, Amount = 100 };
        repository.Invoices.Append(record);
        repository.BuyerNames.RememberAfterSuccessfulInvoice("12345675", true, "", "Synthetic Buyer", true);
        var before = JsonSerializer.Serialize(repository.Invoices.LoadOrCreate());
        var gateway = new Gateway();
        var invoices = new InvoiceService(repository, (_, _) => gateway);
        var draft = new InvoiceDraft { OrderId = "M20261010002", TotalAmount = 100,
            Items = { new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 100, Amount = 100 } } };
        await ThrowsAsync<ServiceConnectionException>(() => invoices.IssueManualWithLookupAsync(draft, new()));
        await ThrowsAsync<ServiceConnectionException>(() => invoices.LookupBuyerNameAsync("12345675"));
        await ThrowsAsync<ServiceConnectionException>(() => invoices.RefreshAllAsync());
        await ThrowsAsync<ServiceConnectionException>(() => invoices.GetInvoicePdfAsync(record, 1));
        await ThrowsAsync<ServiceConnectionException>(() => new InvoiceDetailRefreshService(repository, (_, _) => gateway).RefreshAsync(record));
        await ThrowsAsync<ServiceConnectionException>(() => new InvoiceSyncService(repository, (_, _) => gateway).SyncRecentAsync());
        await ThrowsAsync<ServiceConnectionException>(() => new InvoiceVoidService(repository, (_, _) => gateway).VoidAsync(record, "退貨"));
        Check(gateway.Calls == 0 && JsonSerializer.Serialize(repository.Invoices.LoadOrCreate()) == before,
            "AMEGO outage makes zero business calls and preserves existing records without creating uncertain issuance");

        services.Cloud = true;
        Check(!(await repository.Connections.CheckAsync()).BlockAll, "Cloud-only recovery unlocks Cloud features");
        await ThrowsAsync<ServiceConnectionException>(() => invoices.IssueManualWithLookupAsync(draft, new()));
        services.Cloud = false;
        services.Amego = true;
        Check((await repository.Connections.CheckAsync()).UsesExistingFallback, "AMEGO-only recovery restores the original fallback state");
        Check((await provider.AuthenticateAsync(new("0002", "UserPass22")))?.Role == AppRole.User, "fallback resumes without restart");
        await invoices.LookupBuyerNameFromApiAsync("12345675");
        Check(gateway.Calls == 1, "read-only user retry resumes; recovery never sends an invoice automatically");
        source.Handler = _ => Task.FromResult(BuiltInCloudAuthorityFreshnessTests.Snapshot("ws_services", 2,
            seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 2, true),
            seed("emp_user", "0002", "Updated", EmployeeRoles.Admin, "NewPass22", 2, 2, true)));
        Check(await provider.AuthenticateAsync(new("0002", "UserPass22")) is null
            && (await provider.AuthenticateAsync(new("0002", "NewPass22")))?.Role == AppRole.Admin,
            "authority recovery immediately replaces stale password and role");

        services.Cloud = true;
        services.Revoked = true;
        var denied = await repository.Connections.CheckAsync();
        Check(denied.CloudRejected && denied.BlockAll && !denied.UsesExistingFallback
            && cache.LoadState() is null, "known revoked Device blocks and clears subordinate authority cache");
        services.Cloud = false;
        Check((await repository.Connections.CheckAsync()).CloudRejected,
            "a subsequent 503 cannot turn a known Device denial into fallback");
        await ThrowsAsync<ServiceConnectionException>(() => invoices.LookupBuyerNameFromApiAsync("12345675"));
        services.Cloud = true;
        services.Revoked = false;
        Check(!(await repository.Connections.CheckAsync()).CloudRejected,
            "successful verification of the same Device clears the blocker without changing binding");
        Check(File.ReadAllText(Path.Combine(repository.DataDirectory, "settings.json")) == settingsBefore,
            "denial and recovery preserve Workspace, Device, Token and input settings");

        settings.CloudMode = CloudModes.LocalOnly;
        repository.Settings.Save(settings);
        services.CloudCalls = 0;
        foreach (var ame in new[] { false, true })
        {
            services.Amego = ame;
            var state = await repository.Connections.CheckAsync();
            Check(!state.CloudRequired && state.BlockAll == !ame, "pure Local blocks only until AMEGO recovers");
        }
        Check(services.CloudCalls == 0, "Local never probes nonexistent Cloud authority");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => repository.Connections.CheckAsync(cancelled.Token));
    }

    private static async Task AggregateSyncAsync()
    {
        using var temporary = new AuthorityTemporaryDirectory();
        var services = new Services();
        using var http = new HttpClient(services);
        var repository = LocalRepository.Open(temporary.Path, new TestProtector(), http);
        var settings = repository.Settings.LoadOrCreate();
        settings.CloudBaseUrl = "https://cloud.example.test/";
        settings.CloudWorkspaceId = "ws_services";
        settings.CloudDeviceId = "dev_services";
        repository.Settings.SetCloudDeviceToken(settings, "cydev_" + new string('a', 64));
        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);
        long now = 100;
        var connection = new ServiceConnectivity(repository.Settings, http, repository.CloudEmployees.Clear,
            builtInCache: repository.CloudEmployees, baseDirectory: temporary.Path, monotonicMilliseconds: () => now);
        await connection.CheckAsync();
        Check(services.CloudCalls == 1 && services.Synchronizations.SequenceEqual(new[] { true })
            && repository.CloudEmployees.LoadState()?.WorkspaceId == "ws_services", "one startup request refreshes Device and Built-in authority");
        now += 15_000;
        await connection.CheckAsync();
        Check(services.Synchronizations.SequenceEqual(new[] { true, false }), "15-second liveness does not refresh all permissions");
        now = 60_100;
        await connection.CheckAsync();
        Check(services.Synchronizations.Last(), "60-second permission refresh uses the same aggregate route");
        services.Cloud = false;
        await connection.CheckAsync();
        services.Cloud = true;
        await connection.CheckAsync();
        Check(services.Synchronizations.Last(), "reconnect immediately refreshes authority without waiting another minute");
        var seed = BuiltInCloudAuthorityFreshnessTests.Seed;
        repository.CloudEmployees.ReplaceSnapshot("ws_services", 2, [seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 2, true)]);
        await ThrowsAsync<InvalidDataException>(() => Task.Run(() => repository.CloudEmployees.ReplaceSnapshot("ws_services", 1,
            [seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1, true)])));
        Check(repository.CloudEmployees.LoadState()?.WorkspaceRevision == 2, "older responses cannot overwrite newer Built-in authority");
        services.Reply = new { ok = true };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "missing successful-response fields fail closed without authorizing wipe");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_other",
            status = "revoked", workspaceStatus = "active" } };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "revocation of a different Device cannot wipe this installation");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services",
            status = "active", workspaceStatus = "disabled" } };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "Workspace disable blocks but does not authorize automatic wipe");
        Directory.CreateDirectory(Path.Combine(temporary.Path, "Logs"));
        File.WriteAllText(Path.Combine(temporary.Path, "Logs", "test.log"), "synthetic log");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services",
            status = "revoked", workspaceStatus = "active" } };
        Check((await connection.CheckAsync()).CloudRejected && LocalResetCoordinator.IsRevokedDeviceResetPending(temporary.Path)
            && File.Exists(Path.Combine(temporary.Path, "Logs", "test.log")), "explicit same-Device revoke persists recovery before shutdown or deletion");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task ThrowsAsync<T>(Func<Task> work) where T : Exception
    {
        try { await work(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private sealed class Services : HttpMessageHandler
    {
        public bool Amego { get; set; } = true;
        public bool Cloud { get; set; } = true;
        public bool Revoked { get; set; }
        public int CloudCalls { get; set; }
        public object? Reply { get; set; }
        public List<bool> Synchronizations { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath == "/json/time")
                return Task.FromResult(new HttpResponseMessage(Amego ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
                    { Content = JsonContent.Create(new { timestamp = 1791626400 }) });
            Check(request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/v1/runtime/sync", "only the aggregate route is used");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            Synchronizations.Add(body.RootElement.GetProperty("synchronize").GetBoolean());
            CloudCalls++;
            if (Reply is not null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Reply) });
            if (!Cloud) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = JsonContent.Create(new { ok = false, error = new { code = "STORAGE_UNAVAILABLE" } }) });
            if (Revoked) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = JsonContent.Create(new { ok = false, error = new { code = "DEVICE_REVOKED" } }) });
            var snapshot = BuiltInCloudAuthorityFreshnessTests.Snapshot("ws_services", 1,
                BuiltInCloudAuthorityFreshnessTests.Seed("emp_super", "0001", "Super", EmployeeRoles.SuperAdmin, "SuperPass1", 1, 1, true),
                BuiltInCloudAuthorityFreshnessTests.Seed("emp_user", "0002", "User", EmployeeRoles.User, "UserPass22", 1, 1, true));
            var employeeSnapshot = new { workspaceId = snapshot.WorkspaceId, workspaceRevision = snapshot.WorkspaceRevision,
                employees = snapshot.Employees.Select(e => new { employeeId = e.EmployeeId, employeeNo = e.EmployeeNo,
                    name = e.Name, email = e.Email, role = e.Role, enabled = e.Enabled, emailVerified = e.EmailVerified,
                    credentialVerifier = e.CredentialVerifier, credentialAlgorithm = "pbkdf2-sha256", credentialVersion = e.CredentialVersion,
                    revision = e.Revision }) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services", status = "active", workspaceStatus = "active" },
                binding = new { provider = "BUILT_IN", workspaceId = "ws_services" }, permissions = new { ok = true, employeeSnapshot } }) });
        }
    }

    private sealed class Gateway : IAmegoGateway
    {
        public int Calls { get; private set; }
        private Task<T> Unexpected<T>() { Calls++; throw new InvalidOperationException("Unexpected business call"); }
        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken token = default) => Unexpected<IssueResponse>();
        public Task<QueryResponse> QueryByOrderIdAsync(string id, CancellationToken token = default) => Unexpected<QueryResponse>();
        public Task<QueryResponse> QueryByInvoiceNumberAsync(string id, CancellationToken token = default) => Unexpected<QueryResponse>();
        public Task<StatusResponse> StatusAsync(IEnumerable<string> ids, CancellationToken token = default) => Unexpected<StatusResponse>();
        public Task<byte[]> DownloadInvoicePdfAsync(string id, int style, CancellationToken token = default) => Unexpected<byte[]>();
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken token = default)
        { Calls++; return Task.FromResult(new BanResponse(0, "", [new BanResult("12345675", "Synthetic Buyer")])); }
    }
}
