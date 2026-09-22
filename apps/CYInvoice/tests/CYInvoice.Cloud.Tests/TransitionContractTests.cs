using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

internal static class TransitionContractTests
{
    private const string DeviceToken = "cydev_1111111111111111111111111111111111111111111111111111111111111111";
    private const string SnapshotHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PasswordVerifier = "pbkdf2-sha256$210000$00112233445566778899aabbccddeeff$0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [ModuleInitializer]
    internal static void Initialize()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        await InspectCarriesWholeLocalAccountStateAsync();
        await BootstrapOwnerUsesVerifierNotPlaintextAsync();
        await NewEmployeeVerificationUsesScopedTransitionContractAsync();
        LocalCredentialSnapshotReadsOnlyVerifier();
        Console.WriteLine("PASS Employee transition contract module tests");
    }

    private static async Task InspectCarriesWholeLocalAccountStateAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "transition inspect method");
            Equal("/v1/employee-transition/inspect", request.RequestUri?.AbsolutePath ?? string.Empty, "transition inspect path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter ?? string.Empty, "transition inspect Device token");

            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            var employees = body.RootElement.GetProperty("localEmployees");
            Equal(2, employees.GetArrayLength(), "transition inspect Local Employee count");
            var owner = employees[0];
            Equal("0001", owner.GetProperty("employeeNo").GetString() ?? string.Empty, "transition owner number");
            True(owner.GetProperty("enabled").GetBoolean(), "transition inspect must include enabled state");
            True(!owner.TryGetProperty("password", out _), "transition inspection must never upload plaintext password");
            True(!owner.TryGetProperty("credentialVerifier", out _), "transition inspection must not upload credentials before an explicit completion action");

            return JsonResponse(HttpStatusCode.OK,
                $$"""{"ok":true,"transition":{"deviceId":"dev_b","authorityState":"transitioning","snapshotHash":"{{SnapshotHash}}","localEmployeeCount":2,"unresolvedCount":1,"conflictCount":0,"readyForCutover":false,"items":[{"local":{"employeeNo":"0001","name":"X","email":"x@example.test","role":"SUPER_ADMIN","enabled":true},"suggestedCloudRole":"SUPER_ADMIN","state":"bootstrap_owner_pending","matchKind":"bootstrap_owner","matchedEmployee":null,"employeeNoMatch":null,"emailMatch":null},{"local":{"employeeNo":"0002","name":"Y","email":"y@example.test","role":"SUPER_ADMIN","enabled":true},"suggestedCloudRole":"ADMIN","state":"ready","matchKind":"same_employee","matchedEmployee":{"employeeId":"emp_y","employeeNo":"0002","name":"Cloud Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":3,"revision":9},"employeeNoMatch":{"employeeId":"emp_y","employeeNo":"0002","name":"Cloud Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":3,"revision":9},"emailMatch":{"employeeId":"emp_y","employeeNo":"0002","name":"Cloud Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":3,"revision":9}}]}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var now = DateTimeOffset.Parse("2026-09-22T00:00:00Z");
        var inspection = await client.InspectAsync(new[]
        {
            new EmployeeAccount("0001", "X", "x@example.test", EmployeeRoles.SuperAdmin, true, now, now),
            new EmployeeAccount("0002", "Y local", "y@example.test", EmployeeRoles.SuperAdmin, true, now, now),
        });

        Equal(SnapshotHash, inspection.SnapshotHash, "transition snapshot hash");
        Equal(2, inspection.Items.Count, "transition response item count");
        Equal("ADMIN", inspection.Items[1].SuggestedCloudRole, "exact existing identity must adopt Cloud role");
        Equal("Cloud Y", inspection.Items[1].MatchedEmployee?.Name ?? string.Empty, "exact existing identity must adopt Cloud data");
        True(inspection.Items[1].MatchedEmployee?.CredentialReady == true, "existing Cloud credential readiness");
    }

    private static async Task BootstrapOwnerUsesVerifierNotPlaintextAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/bootstrap-owner/complete", request.RequestUri?.AbsolutePath ?? string.Empty, "bootstrap owner completion path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0001", body.RootElement.GetProperty("employeeNo").GetString() ?? string.Empty, "bootstrap owner Employee No");
            Equal(SnapshotHash, body.RootElement.GetProperty("snapshotHash").GetString() ?? string.Empty, "bootstrap owner snapshot");
            Equal(PasswordVerifier, body.RootElement.GetProperty("credentialVerifier").GetString() ?? string.Empty, "bootstrap owner verifier");
            True(!body.RootElement.TryGetProperty("password", out _), "bootstrap owner action must never upload plaintext password");
            True(!body.RootElement.TryGetProperty("recoveryCode", out _), "bootstrap owner action must never upload Local recovery code");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"transitionItem":{"state":"ready","employee":{"employeeId":"emp_x","employeeNo":"0001","name":"X","email":"x@example.test","role":"SUPER_ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":1}}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionActionClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var result = await client.CompleteBootstrapOwnerAsync("0001", SnapshotHash, PasswordVerifier);
        Equal("ready", result.State, "bootstrap owner completion state");
        Equal("SUPER_ADMIN", result.Employee.Role, "bootstrap owner Cloud role");
        True(result.Employee.EmailVerified, "bootstrap owner reuses verified Workspace Email");
    }

    private static async Task NewEmployeeVerificationUsesScopedTransitionContractAsync()
    {
        const string challengeId = "otp_12345678-1234-1234-1234-123456789abc";
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/email-challenge", request.RequestUri?.AbsolutePath ?? string.Empty, "Employee Email challenge path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0002", body.RootElement.GetProperty("employeeNo").GetString() ?? string.Empty, "Email challenge Employee No");
            Equal(SnapshotHash, body.RootElement.GetProperty("snapshotHash").GetString() ?? string.Empty, "Email challenge snapshot");
            True(!body.RootElement.TryGetProperty("email", out _), "client must not choose a different Email than the inspected transition item");
            return JsonResponse(HttpStatusCode.Created,
                $$"""{"ok":true,"challenge":{"challengeId":"{{challengeId}}","maskedEmail":"y***@example.test","expiresAt":"2026-09-22T04:10:00Z","resendAfter":"2026-09-22T04:01:00Z"}}""");
        });
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/email-verify", request.RequestUri?.AbsolutePath ?? string.Empty, "Employee Email verify path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal(challengeId, body.RootElement.GetProperty("challengeId").GetString() ?? string.Empty, "Employee verification challenge");
            Equal("654321", body.RootElement.GetProperty("otp").GetString() ?? string.Empty, "Employee verification OTP");
            Equal(PasswordVerifier, body.RootElement.GetProperty("credentialVerifier").GetString() ?? string.Empty, "new Employee migrated verifier");
            True(!body.RootElement.TryGetProperty("password", out _), "Employee Email verification must never upload plaintext password");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"transitionItem":{"state":"ready","employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":1}}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionActionClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var challenge = await client.StartEmailVerificationAsync("0002", SnapshotHash);
        Equal(challengeId, challenge.ChallengeId, "Employee Email challenge ID");
        var result = await client.VerifyEmailAsync("0002", SnapshotHash, challengeId, "654321", PasswordVerifier);
        Equal("ADMIN", result.Employee.Role, "new Local SUPER_ADMIN becomes Cloud ADMIN");
        True(result.Employee.CredentialReady, "new Employee central credential is ready");
    }

    private static void LocalCredentialSnapshotReadsOnlyVerifier()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CYInvoice.Cloud.Transition.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var employees = new EmployeeStore(directory);
            var created = employees.CreateFirstSuperAdmin("0001", "X", "x@example.test", "Password123");
            var snapshots = new LocalEmployeeCredentialSnapshotStore(directory).LoadAll();
            Equal(1, snapshots.Count, "Local credential snapshot count");
            Equal("0001", snapshots[0].EmployeeNo, "Local credential snapshot Employee No");
            True(snapshots[0].PasswordVerifier.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "Local credential snapshot is a one-way verifier");
            True(!snapshots[0].PasswordVerifier.Contains("Password123", StringComparison.Ordinal), "Local credential snapshot must not contain plaintext password");
            True(!snapshots[0].PasswordVerifier.Contains(created.RecoveryCode, StringComparison.Ordinal), "Local credential snapshot must not expose Local recovery code");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static void Equal<T>(T expected, T actual, string description)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'");
    }

    private static void True(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) => responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (responses.Count == 0) throw new InvalidOperationException("Unexpected HTTP request in transition contract test.");
            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
