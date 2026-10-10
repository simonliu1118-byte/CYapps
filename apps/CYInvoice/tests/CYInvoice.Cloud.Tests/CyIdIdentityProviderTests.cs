using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Storage;

internal static class CyIdIdentityProviderTests
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.CyId.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var protector = new Protector();
            var settingsStore = new SettingsStore(directory, protector);
            var settings = settingsStore.LoadOrCreate();
            settings.CloudBaseUrl = "https://invoice.example.test/";
            settings.CloudWorkspaceId = "ws_invoice";
            settings.CloudDeviceId = "dev_test";
            var token = "cydev_" + new string('a', 64);
            settingsStore.SetCloudDeviceToken(settings, token);
            settingsStore.MarkCloudEmployeeAuthorityReady(settings);
            var binding = new CyIdBinding(settings.CloudBaseUrl, settings.CloudWorkspaceId, settings.CloudDeviceId,
                "ws_identity", "CYINVOICE", "1.0.2", CyIdGateway.TokenDigest(token));
            settingsStore.ConfirmCyIdConfiguration(settings, binding);
            settingsStore.Save(settings);
            var cache = new CyIdOfflineCache(directory, protector);
            var handler = new Handler(token);
            using var http = new HttpClient(handler);
            var provider = new CyIdIdentityProvider(settingsStore, cache, http);
            var credentials = new IdentityAuthenticationRequest("0002", "SyntheticPass1");
            Check(!provider.OwnsAccountManagement, "CYID must not own consumer account management");
            var principal = await provider.AuthenticateAsync(credentials);
            Check(principal?.StableEmployeeId == "emp_test" && principal.Role == AppRole.User
                && principal.WorkspaceId == "ws_identity" && principal.CredentialVersion == 1, "canonical principal");
            var saved = File.ReadAllText(Path.Combine(directory, "cyid_offline_cache.json"));
            Check(!saved.Contains("SyntheticPass1", StringComparison.Ordinal) && !saved.Contains("emp_test", StringComparison.Ordinal)
                && !saved.Contains("USER", StringComparison.Ordinal) && !saved.Contains("cyid_", StringComparison.Ordinal),
                "cache encrypts authority and local offline proof, no raw provider session");

            handler.Mode = "offline";
            Check((await provider.AuthenticateAsync(credentials))?.Role == AppRole.User, "real transport outage uses the last confirmed cache");
            Check(await provider.AuthenticateAsync(credentials with { Password = "WrongPassword1" }) is null, "offline password required");
            handler.Mode = "admin";
            Check((await provider.AuthenticateAsync(credentials))?.Role == AppRole.Admin, "online authority replaces cached role");
            handler.Mode = "offline";
            Check((await provider.AuthenticateAsync(credentials))?.Role == AppRole.Admin, "updated role persists offline");
            handler.Mode = "unavailable";
            await ThrowsAsync<CyIdAuthenticationException>(() => provider.AuthenticateAsync(credentials));
            handler.Mode = "offline";
            Check((await provider.AuthenticateAsync(credentials))?.Role == AppRole.Admin, "503 blocks this request without masquerading as transport outage");
            handler.Mode = "denied";
            await ThrowsAsync<CyIdAuthenticationException>(() => provider.AuthenticateAsync(credentials));
            handler.Mode = "offline";
            Check(await provider.AuthenticateAsync(credentials) is null, "App Access denial removes offline access");
            handler.Mode = "online";
            await provider.AuthenticateAsync(credentials);
            handler.Mode = "malformed";
            await ThrowsAsync<InvalidDataException>(() => provider.AuthenticateAsync(credentials));
            handler.Mode = "offline";
            Check(await provider.AuthenticateAsync(credentials) is null, "malformed/cross-workspace authority cannot preserve offline access");
            handler.Mode = "online";
            await provider.AuthenticateAsync(credentials);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await ThrowsAsync<OperationCanceledException>(() => provider.AuthenticateAsync(credentials, cancelled.Token));
            Check(await provider.AuthenticateAsync(credentials with { Password = "short" }) is null, "shared password minimum");
            Check(await provider.AuthenticateAsync(credentials with { Password = new string('x', 17) }) is null, "shared password maximum");
            Check(await provider.AuthenticateAsync(credentials with { Password = "密碼😀abcdefgh" }) is not null, "shared Unicode password boundary");
            await ThrowsAsync<CyIdAuthenticationException>(() => provider.RefreshPrincipalAsync("0002"));
            settingsStore.SetCloudDeviceToken(settings, "cydev_" + new string('b', 64));
            settingsStore.Save(settings);
            await ThrowsAsync<InvalidDataException>(() => provider.AuthenticateAsync(credentials));
            settingsStore.ClearCloudIdentity(settings);
            settingsStore.Save(settings);
            Check(settings.CloudIdentityProvider == "BUILT_IN" && settings.CyIdBindingEncrypted.Length == 0, "reset clears CYID binding");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task ThrowsAsync<T>(Func<Task> work) where T : Exception
    {
        try { await work(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private sealed class Protector : ISecretProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public string Protect(ReadOnlySpan<byte> plaintext)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var bytes = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, bytes, tag);
            return Convert.ToBase64String(nonce.Concat(tag).Concat(bytes).ToArray());
        }
        public byte[] Unprotect(string ciphertext)
        {
            var value = Convert.FromBase64String(ciphertext);
            var result = new byte[value.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(value.AsSpan(0, 12), value.AsSpan(28), value.AsSpan(12, 16), result);
            return result;
        }
    }

    private sealed class Handler(string token) : HttpMessageHandler
    {
        public string Mode { get; set; } = "online";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Check(request.Headers.Authorization?.Parameter == token, "only the current Device Token authenticates gateway transport");
            Check(request.RequestUri?.AbsolutePath == "/v1/cyid/authenticate", "canonical consumer gateway route");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Check(!body.RootElement.TryGetProperty("workspaceId", out _) && !body.RootElement.TryGetProperty("applicationId", out _),
                "Windows cannot select its authority scope");
            if (Mode == "offline") throw new HttpRequestException("synthetic transport outage");
            if (Mode is "denied" or "unavailable") return new(Mode == "denied" ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable)
                { Content = JsonContent.Create(new { ok = false, error = new { code = Mode == "denied" ? "ACCESS_DENIED" : "IDENTITY_UNAVAILABLE" } }) };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                ok = true, provider = "CYID", workspaceId = "ws_invoice", deviceId = "dev_test", identityWorkspaceId = "ws_identity",
                applicationId = "CYINVOICE", consumerVersion = "1.0.2", principal = new {
                    workspaceId = Mode == "malformed" ? "ws_other" : "ws_identity", employeeId = "emp_test", employeeNo = "0002",
                    displayName = "Synthetic User", workspaceRole = Mode == "admin" ? "ADMIN" : "USER",
                    isIdentityAdmin = false, emailVerified = true, isWorkspaceSuperAdmin = false,
                    credentialVersion = 1, employeeRevision = Mode == "admin" ? 2 : 1,
                } }) };
        }
    }
}
