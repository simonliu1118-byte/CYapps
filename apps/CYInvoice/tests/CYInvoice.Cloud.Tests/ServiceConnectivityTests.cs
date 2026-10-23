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
        repository.Settings.ConfirmCyIdConfiguration(settings, new CyIdBinding(
            settings.CloudBaseUrl,settings.CloudWorkspaceId,settings.CloudDeviceId,"identity_services","CYINVOICE","1.0.3",
            CyIdGateway.TokenDigest(repository.Settings.CloudDeviceToken(settings))));
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
        services.Amego = false;
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
        await invoices.LookupBuyerNameFromApiAsync("12345675");
        Check(gateway.Calls == 1, "read-only user retry resumes; recovery never sends an invoice automatically");
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
        repository.Settings.ConfirmCyIdConfiguration(settings, new CyIdBinding(
            settings.CloudBaseUrl,settings.CloudWorkspaceId,settings.CloudDeviceId,"identity_services","CYINVOICE","1.0.3",
            CyIdGateway.TokenDigest(repository.Settings.CloudDeviceToken(settings))));
        repository.Settings.MarkCloudEmployeeAuthorityReady(settings);
        repository.Settings.Save(settings);
        var connection = new ServiceConnectivity(repository.Settings, http, repository.CloudEmployees.Clear,
            builtInCache: repository.CloudEmployees, baseDirectory: temporary.Path);
        await connection.CheckAsync();
        Check(services.CloudCalls == 1 && services.Synchronizations.SequenceEqual(new[] { true }),
            "one startup request refreshes Device and CYID invalidation");
        await connection.CheckAsync();
        Check(services.Synchronizations.SequenceEqual(new[] { true, true }), "explicit checks include immediate permission confirmation without a second cooldown");
        await connection.CheckAsync();
        Check(services.Synchronizations.Last(), "60-second permission refresh uses the same aggregate route");
        services.Cloud = false;
        await connection.CheckAsync();
        services.Cloud = true;
        await connection.CheckAsync();
        Check(services.Synchronizations.Last(), "reconnect immediately refreshes authority without waiting another minute");
        services.HttpFailure = HttpStatusCode.NotFound;
        var unsupported = await connection.CheckAsync();
        Check(unsupported.UsesExistingFallback && unsupported.CloudProblem.Contains("雲端版本")
            && !LocalResetCoordinator.HasPendingReset(temporary.Path), "old Worker needs an additive upgrade and never resets the original installation");
        services.HttpFailure = HttpStatusCode.Unauthorized;
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "unknown token is denial without authenticated revoke evidence");
        services.HttpFailure = null;
        Check(!(await connection.CheckAsync()).CloudRejected, "original Device recovers after successful current verification");
        services.Reply = new { ok = true };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "missing successful-response fields fail closed without authorizing wipe");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_other",
            status = "revoked", workspaceStatus = "active", revokedAt = "2026-10-10T00:00:00Z" } };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "revocation of a different Device cannot wipe this installation");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services",
            status = "active", workspaceStatus = "disabled" } };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "Workspace disable blocks but does not authorize automatic wipe");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services",
            status = "revoked", workspaceStatus = "active" } };
        Check((await connection.CheckAsync()).CloudRejected && !LocalResetCoordinator.HasPendingReset(temporary.Path),
            "incomplete revocation evidence never authorizes wipe");
        Directory.CreateDirectory(Path.Combine(temporary.Path, "Logs"));
        File.WriteAllText(Path.Combine(temporary.Path, "Logs", "test.log"), "synthetic log");
        services.Reply = new { ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services",
            status = "revoked", workspaceStatus = "active", revokedAt = "2026-10-10T00:00:00Z" } };
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
        public HttpStatusCode? HttpFailure { get; set; }
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
            if (HttpFailure is { } failure) return Task.FromResult(new HttpResponseMessage(failure)
                { Content = JsonContent.Create(new { ok = false }) });
            if (Reply is not null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Reply) });
            if (!Cloud) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = JsonContent.Create(new { ok = false, error = new { code = "STORAGE_UNAVAILABLE" } }) });
            if (Revoked) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = JsonContent.Create(new { ok = false, error = new { code = "DEVICE_REVOKED" } }) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                ok = true, device = new { workspaceId = "ws_services", deviceId = "dev_services", status = "active", workspaceStatus = "active" },
                binding = new { provider = "CYID", workspaceId = "ws_services", identityWorkspaceId="identity_services", applicationId="CYINVOICE", consumerVersion="1.0.3" }, permissions = new { ok = true, invalidated=Array.Empty<string>() } }) });
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
