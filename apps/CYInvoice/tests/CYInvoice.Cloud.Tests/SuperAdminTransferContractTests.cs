using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class SuperAdminTransferContractTests
{
    private const string DeviceToken = "cydev_3333333333333333333333333333333333333333333333333333333333333333";

    [ModuleInitializer]
    internal static void Initialize()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        await ChallengeAuthenticatesXAndTargetsYAsync();
        await ConfirmationCarriesXEmailOtpAndParsesAtomicRoleSwapAsync();
        Console.WriteLine("PASS SUPER_ADMIN transfer contract module tests");
    }

    private static async Task ChallengeAuthenticatesXAndTargetsYAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "transfer challenge method");
            Equal("/v1/employees/super-admin-transfer/challenge", request.RequestUri?.AbsolutePath ?? string.Empty, "transfer challenge path");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter ?? string.Empty, "transfer challenge Device token");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0001", body.RootElement.GetProperty("actorEmployeeNo").GetString() ?? string.Empty, "current SUPER_ADMIN Employee No");
            Equal("Password123", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "current SUPER_ADMIN action password");
            Equal("0002", body.RootElement.GetProperty("targetEmployeeNo").GetString() ?? string.Empty, "target ADMIN Employee No");
            True(!body.RootElement.TryGetProperty("targetEmail", out _), "client must not supply a replacement Recovery Email independently of target Cloud Employee");
            return JsonResponse(HttpStatusCode.Created,
                """{"ok":true,"challenge":{"challengeId":"otp_12345678-1234-1234-1234-123456789abc","maskedEmail":"x***@example.test","targetEmployee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":2,"revision":4},"expiresAt":"2026-09-22T05:10:00Z","resendAfter":"2026-09-22T05:01:00Z"}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudSuperAdminTransferClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var challenge = await client.StartAsync("0001", "Password123", "0002");
        Equal("x***@example.test", challenge.MaskedEmail, "current X masked Email");
        Equal("0002", challenge.TargetEmployee.EmployeeNo, "transfer target");
        Equal("ADMIN", challenge.TargetEmployee.Role, "target must be ADMIN before transfer");
    }

    private static async Task ConfirmationCarriesXEmailOtpAndParsesAtomicRoleSwapAsync()
    {
        const string challengeId = "otp_12345678-1234-1234-1234-123456789abc";
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "transfer confirmation method");
            Equal("/v1/employees/super-admin-transfer/confirm", request.RequestUri?.AbsolutePath ?? string.Empty, "transfer confirmation path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0001", body.RootElement.GetProperty("actorEmployeeNo").GetString() ?? string.Empty, "confirmation actor X");
            Equal("Password123", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "confirmation action password");
            Equal("0002", body.RootElement.GetProperty("targetEmployeeNo").GetString() ?? string.Empty, "confirmation target Y");
            Equal(challengeId, body.RootElement.GetProperty("challengeId").GetString() ?? string.Empty, "X Email challenge");
            Equal("654321", body.RootElement.GetProperty("otp").GetString() ?? string.Empty, "X Email OTP");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"transfer":{"formerSuperAdmin":{"employeeId":"emp_x","employeeNo":"0001","name":"X","email":"x@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":1,"revision":2},"newSuperAdmin":{"employeeId":"emp_y","employeeNo":"0002","name":"Y","email":"y@example.test","role":"SUPER_ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":2,"revision":5},"recoveryEmail":"y@example.test","completedAt":"2026-09-22T05:02:00Z"}}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudSuperAdminTransferClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var result = await client.ConfirmAsync("0001", "Password123", "0002", challengeId, "654321");
        Equal("ADMIN", result.FormerSuperAdmin.Role, "X role after transfer");
        Equal("SUPER_ADMIN", result.NewSuperAdmin.Role, "Y role after transfer");
        Equal("y@example.test", result.RecoveryEmail, "Workspace Recovery Email follows Y verified Email");
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
            if (responses.Count == 0) throw new InvalidOperationException("Unexpected HTTP request in SUPER_ADMIN transfer contract test.");
            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
