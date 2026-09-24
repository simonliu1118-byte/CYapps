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
    internal static void Initialize() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        await InspectContractAsync();
        await TransitionActionContractAsync();
        LocalCredentialSnapshotReadsOnlyVerifier();
        Console.WriteLine("PASS Employee transition contract module tests");
    }

    private static async Task InspectContractAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/inspect", request.RequestUri?.AbsolutePath ?? string.Empty, "transition inspect path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter ?? string.Empty, "transition inspect Device token");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            var employees = body.RootElement.GetProperty("localEmployees");
            Equal(2, employees.GetArrayLength(), "transition Local Employee count");
            True(employees[0].GetProperty("enabled").GetBoolean(), "transition must include enabled state");
            True(!employees[0].TryGetProperty("password", out _), "inspection must not upload plaintext password");
            True(!employees[0].TryGetProperty("credentialVerifier", out _), "inspection must not upload credentials");

            var json = """{"ok":true,"transition":{"deviceId":"dev_b","authorityState":"transitioning","snapshotHash":"__HASH__","localEmployeeCount":2,"unresolvedCount":1,"conflictCount":0,"readyForCutover":false,"items":[{"local":{"employeeNo":"0001","name":"X","email":"x@example.test","role":"SUPER_ADMIN","enabled":true},"suggestedCloudRole":"SUPER_ADMIN","state":"bootstrap_owner_pending","matchKind":"bootstrap_owner","matchedEmployee":null,"employeeNoMatch":null,"emailMatch":null},{"local":{"employeeNo":"0002","name":"Y","email":"y@example.test","role":"SUPER_ADMIN","enabled":true},"suggestedCloudRole":"ADMIN","state":"ready","matchKind":"same_employee","matchedEmployee":{"employeeId":"emp_y","employeeNo":"0002","name":"Cloud Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":3,"revision":9},"employeeNoMatch":null,"emailMatch":null}]}}"""
                .Replace("__HASH__", SnapshotHash, StringComparison.Ordinal);
            return JsonResponse(HttpStatusCode.OK, json);
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var now = DateTimeOffset.Parse("2026-09-22T00:00:00Z");
        var result = await client.InspectAsync([
            new EmployeeAccount("0001", "X", "x@example.test", EmployeeRoles.SuperAdmin, true, now, now),
            new EmployeeAccount("0002", "Y local", "y@example.test", EmployeeRoles.SuperAdmin, true, now, now),
        ]);
        Equal(SnapshotHash, result.SnapshotHash, "transition snapshot hash");
        Equal("ADMIN", result.Items[1].SuggestedCloudRole, "exact match adopts Cloud role");
        Equal("Cloud Y", result.Items[1].MatchedEmployee?.Name ?? string.Empty, "exact match adopts Cloud data");
    }

    private static async Task TransitionActionContractAsync()
    {
        const string challengeId = "otp_12345678-1234-1234-1234-123456789abc";
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/bootstrap-owner/complete", request.RequestUri?.AbsolutePath ?? string.Empty, "bootstrap completion path");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(PasswordVerifier, body.RootElement.GetProperty("credentialVerifier").GetString() ?? string.Empty, "bootstrap verifier");
            True(!body.RootElement.TryGetProperty("password", out _), "bootstrap completion must not upload password");
            True(!body.RootElement.TryGetProperty("recoveryCode", out _), "bootstrap completion must not upload recovery code");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"transitionItem":{"state":"ready","employee":{"employeeId":"emp_x","employeeNo":"0001","name":"X","email":"x@example.test","role":"SUPER_ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":1}}}""");
        });
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/email-challenge", request.RequestUri?.AbsolutePath ?? string.Empty, "Email challenge path");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(SnapshotHash, body.RootElement.GetProperty("snapshotHash").GetString() ?? string.Empty, "Email challenge snapshot");
            True(!body.RootElement.TryGetProperty("email", out _), "client must not substitute inspected Email");
            var json = """{"ok":true,"challenge":{"challengeId":"__CHALLENGE__","maskedEmail":"y***@example.test","expiresAt":"2026-09-22T04:10:00Z","resendAfter":"2026-09-22T04:01:00Z"}}"""
                .Replace("__CHALLENGE__", challengeId, StringComparison.Ordinal);
            return JsonResponse(HttpStatusCode.Created, json);
        });
        handler.Enqueue(request =>
        {
            Equal("/v1/employee-transition/email-verify", request.RequestUri?.AbsolutePath ?? string.Empty, "Email verify path");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Equal(PasswordVerifier, body.RootElement.GetProperty("credentialVerifier").GetString() ?? string.Empty, "new Employee verifier");
            Equal("654321", body.RootElement.GetProperty("otp").GetString() ?? string.Empty, "Employee OTP");
            True(!body.RootElement.TryGetProperty("password", out _), "Email verification must not upload password");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"transitionItem":{"state":"ready","employee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":1}}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionActionClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var owner = await client.CompleteBootstrapOwnerAsync("0001", SnapshotHash, PasswordVerifier);
        Equal("SUPER_ADMIN", owner.Employee.Role, "bootstrap role");
        var challenge = await client.StartEmailVerificationAsync("0002", SnapshotHash);
        Equal(challengeId, challenge.ChallengeId, "Employee challenge ID");
        var employee = await client.VerifyEmailAsync("0002", SnapshotHash, challengeId, "654321", PasswordVerifier);
        Equal("ADMIN", employee.Employee.Role, "new Local SUPER_ADMIN Cloud role");
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
            True(snapshots[0].PasswordVerifier.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal), "snapshot is a verifier");
            True(!snapshots[0].PasswordVerifier.Contains("Password123", StringComparison.Ordinal), "snapshot has no plaintext password");
            True(!snapshots[0].PasswordVerifier.Contains(created.RecoveryCode, StringComparison.Ordinal), "snapshot has no Local recovery code");
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
            if (responses.Count == 0) throw new InvalidOperationException("Unexpected transition contract HTTP request.");
            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
