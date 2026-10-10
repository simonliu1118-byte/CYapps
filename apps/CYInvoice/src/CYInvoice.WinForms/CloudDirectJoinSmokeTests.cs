using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed partial class CloudDirectJoinForm
{
    // Exercise the real controls/events, HTTP clients and protected persistence.
    // Only transport and native notifications are substituted; no live Cloud is used.
    internal static void VerifySmokeJoinFlow()
    {
        var previousContext = SynchronizationContext.Current;
        using var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var work = VerifyJoinFlowAsync();
            var timer = Stopwatch.StartNew();
            while (!work.IsCompleted)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("Cloud direct-join regression timed out.");
                Application.DoEvents();
                Thread.Sleep(1);
            }
            work.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static async Task VerifyJoinFlowAsync()
    {
        foreach (var scenario in new[] { "invitation", "pairing", "mismatch", "recover", "edited", "cyid-invitation", "cyid-pairing", "cyid-mismatch" })
        {
            var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.JoinRegression", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var repository = LocalRepository.Open(directory, new DpapiSecretProtector());
                var server = new JoinSmokeServer
                {
                    WrongWorkspace = scenario == "mismatch",
                    FailSnapshotOnce = scenario == "recover",
                    CyId = scenario.StartsWith("cyid-", StringComparison.Ordinal),
                    WrongIdentityBinding = scenario == "cyid-mismatch",
                };
                var messages = new List<(string Message, MessageBoxIcon Icon)>();
                using (var form = new CloudDirectJoinForm(repository, new HttpClient(new JoinSmokeHandler(server)),
                    (message, _, icon) => messages.Add((message, icon))))
                {
                    form.url.Text = "https://cloud.example.test/";
                    form.ownerMethod.Checked = !scenario.EndsWith("pairing", StringComparison.Ordinal);
                    form.pairingCode.Text = new string('a', 20);
                    form.invitationCode.Text = new string('b', 40);
                    form.employeeNo.Text = "0001";
                    form.password.Text = "SmokePassword123";
                    form.deviceName.Text = "Synthetic smoke device";
                    await form.NextAsync();
                    Require(form.preview is not null && form.confirmJoin.Enabled, scenario + ": preview not confirmed");

                    if (scenario == "edited")
                    {
                        form.deviceName.Text = "Edited synthetic device";
                        Require(form.preview is null && !form.confirmJoin.Enabled, "Edits must invalidate authorization");
                        await form.JoinAsync();
                        Require(server.Claims == 0 && !form.IdentityCompleted, "Unconfirmed edits must not claim");
                        continue;
                    }

                    await form.JoinAsync();
                    if (scenario is "mismatch" or "recover" or "cyid-mismatch")
                    {
                        Require(!form.IdentityCompleted, scenario + ": failure must not commit identity");
                        Require(repository.Settings.CloudPendingDeviceJoin(repository.Settings.LoadOrCreate()) is not null,
                            scenario + ": protected pending token must survive");
                        Require(repository.Settings.LoadOrCreate().CloudDeviceId.Length == 0,
                            scenario + ": failure must not save a formal device");
                        Require(messages.Count == 1 && messages[0].Icon == MessageBoxIcon.Warning,
                            scenario + ": expected one failure notification");
                        if (scenario is "mismatch" or "cyid-mismatch")
                        {
                            Require(messages[0].Message.Contains("Workspace", StringComparison.Ordinal),
                                "Workspace mismatch must fail explicitly, without a null reference");
                            Require(server.Snapshots == 0, "Mismatched Workspace must not fetch Employee authority");
                            continue;
                        }
                    }
                    else
                    {
                        Require(form.IdentityCompleted, scenario + ": first attempt must complete");
                        Require(form.preview is null && form.password.Text.Length == 0,
                            scenario + ": password clearing must still invalidate editable authorization");
                        Require(messages.Count == 1 && messages[0].Icon == MessageBoxIcon.Information,
                            scenario + ": success must not show a false failure");
                    }
                }

                if (scenario == "recover")
                {
                    messages.Clear();
                    using var reopened = new CloudDirectJoinForm(repository, new HttpClient(new JoinSmokeHandler(server)),
                        (message, _, icon) => messages.Add((message, icon)));
                    await reopened.RecoverPendingAsync();
                    Require(reopened.IdentityCompleted, "Reopen must recover the already claimed device");
                    Require(messages.Count == 1 && messages[0].Icon == MessageBoxIcon.Information,
                        "Recovery must complete without invitation preview or claim");
                }

                var saved = repository.Settings.LoadOrCreate();
                Require(saved.CloudWorkspaceId == "ws_smoke" && saved.CloudDeviceId == "dev_smoke",
                    scenario + ": verified identity must persist");
                Require(repository.Settings.CloudPendingDeviceJoin(saved) is null,
                    scenario + ": completed join must clear pending identity");
                Require(repository.CloudEmployees.LoadAll().Count == (server.CyId ? 0 : 1),
                    scenario + ": CYID must not import Built-in Employee credentials");
                Require(server.Snapshots == (server.CyId ? 0 : scenario == "recover" ? 2 : 1),
                    scenario + ": only Built-in mode may read its Employee snapshot");
                if (server.CyId)
                {
                    Require(saved.CloudIdentityProvider == "CYID" && !repository.IdentityProvider.OwnsAccountManagement,
                        scenario + ": confirmed CYID must hide app account management");
                    Require(repository.Settings.CyIdConfiguration(saved).IdentityWorkspaceId == "ws_identity_smoke",
                        scenario + ": protected CYID binding must persist");
                }
                Require(!repository.Employees.HasEmployees(), scenario + ": direct join must not create Local authority");
                Require(server.Claims == 1 && server.Previews == 1,
                    scenario + ": recovery must not consume an invitation twice or create a second device");
                Require(server.Token.Length == 70 && repository.Settings.CloudDeviceToken(saved) == server.Token,
                    scenario + ": the original protected Device Token must persist");
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Cloud direct-join regression: " + message);
    }

    private sealed class JoinSmokeServer
    {
        public bool WrongWorkspace { get; init; }
        public bool FailSnapshotOnce { get; set; }
        public bool CyId { get; init; }
        public bool WrongIdentityBinding { get; init; }
        public int Claims { get; set; }
        public int Previews { get; set; }
        public int Snapshots { get; set; }
        public string Token { get; set; } = string.Empty;
    }

    private sealed class JoinSmokeHandler(JoinSmokeServer server) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/v1/health")
                return Json(new { ok = true, storage = "ok", service = "cyinvoice-cloud",
                    cloudVersion = "0.8.8", apiVersion = "1", schemaVersion = "8", environment = "test" });
            if (path.EndsWith("/preview", StringComparison.Ordinal))
            {
                server.Previews++;
                Require(server.Claims == 0, "A used invitation must not be previewed again");
                return Json(new { workspace = new { workspaceId = "ws_smoke", displayName = "Synthetic Workspace" } });
            }
            if (path.EndsWith("/claim", StringComparison.Ordinal))
            {
                server.Claims++;
                Require(server.Claims == 1, "Device claim must happen once");
                Require(request.Content is not null, "Claim body required");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                server.Token = body.RootElement.GetProperty("deviceToken").GetString() ?? string.Empty;
                return Identity(server.WrongWorkspace ? "ws_other" : "ws_smoke");
            }

            Require(server.Claims == 1, "Device must be claimed before authenticated reads");
            Require(request.Headers.Authorization?.Parameter == server.Token, "Recovery must use the original Device Token");
            if (path == "/v1/device") return Identity("ws_smoke");
            if (path == "/v1/identity-provider")
            {
                if (server.CyId)
                    return Json(new { ok = true, provider = "CYID", workspaceId = server.WrongIdentityBinding ? "ws_other" : "ws_smoke",
                        deviceId = "dev_smoke", identityWorkspaceId = "ws_identity_smoke", applicationId = "CYINVOICE", consumerVersion = "1.0.2" });
                return Json(new { ok = true, provider = "BUILT_IN", workspaceId = "ws_smoke", deviceId = "dev_smoke" });
            }
            if (path == "/v1/employee-authority/status")
                return Json(new { authority = new { deviceId = "dev_smoke", workspaceId = "ws_smoke",
                    state = "cloud", transitionSnapshotHash = "", transitionItemCount = 0, unresolvedCount = 0,
                    invalidCentralEmployeeCount = 0, readyForCutover = false, completedAt = (string?)null,
                    workspaceRevision = 1 } });
            if (path == "/v1/employee-authority/snapshot")
            {
                server.Snapshots++;
                if (server.FailSnapshotOnce)
                {
                    server.FailSnapshotOnce = false;
                    throw new HttpRequestException("Synthetic interruption after successful claim");
                }
                return Json(new { employeeSnapshot = new { workspaceId = "ws_smoke", workspaceRevision = 1,
                    employees = new[] { new { employeeId = "emp_smoke", employeeNo = "0001", name = "Synthetic Owner",
                        email = "owner@example.test", role = "SUPER_ADMIN", enabled = true, emailVerified = true,
                        credentialAlgorithm = "pbkdf2-sha256",
                        credentialVerifier = "pbkdf2-sha256$210000$00112233445566778899aabbccddeeff$0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        credentialVersion = 1, revision = 1 } } } });
            }
            throw new InvalidOperationException("Unexpected synthetic join request: " + path);
        }

        private static HttpResponseMessage Identity(string workspaceId) =>
            Json(new { workspace = new { workspaceId }, device = new { deviceId = "dev_smoke", displayName = "Synthetic smoke device" } });

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
