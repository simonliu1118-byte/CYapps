using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class TransitionConflictContractTests
{
    private const string DeviceToken = "cydev_2222222222222222222222222222222222222222222222222222222222222222";

    [ModuleInitializer]
    internal static void Initialize()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        await CountIsDeviceAuthenticatedAsync();
        await ListRequiresScopedSuperAdminCredentialsAsync();
        await ResolveTargetsExplicitCandidateAsync();
        Console.WriteLine("PASS Employee transition conflict contract module tests");
    }

    private static async Task CountIsDeviceAuthenticatedAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Get, request.Method, "conflict count method");
            Equal("/v1/employee-transition/conflicts/count", request.RequestUri?.AbsolutePath ?? string.Empty, "conflict count path");
            Equal("Bearer", request.Headers.Authorization?.Scheme ?? string.Empty, "conflict count bearer scheme");
            Equal(DeviceToken, request.Headers.Authorization?.Parameter ?? string.Empty, "conflict count Device token");
            return JsonResponse(HttpStatusCode.OK, """{"ok":true,"conflictCount":2}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionConflictClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        Equal(2, await client.GetCountAsync(), "conflict count");
    }

    private static async Task ListRequiresScopedSuperAdminCredentialsAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "conflict list method");
            Equal("/v1/employee-transition/conflicts/list", request.RequestUri?.AbsolutePath ?? string.Empty, "conflict list path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0001", body.RootElement.GetProperty("actorEmployeeNo").GetString() ?? string.Empty, "conflict actor Employee No");
            Equal("Password123", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "conflict actor password");
            True(!body.RootElement.TryGetProperty("credentialVerifier", out _), "conflict client must not send a credential verifier in place of action-time password authentication");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"conflictCount":1,"conflicts":[{"transitionItemId":"eti_123","sourceDeviceId":"dev_b","sourceDeviceName":"B機","local":{"employeeNo":"0002","name":"Y Local","email":"y@example.test","role":"SUPER_ADMIN","enabled":true},"matchKind":"employee_no_only","employeeNoMatch":{"employeeId":"emp_y","employeeNo":"0002","name":"Y Cloud","email":"old@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":2,"revision":4},"emailMatch":null}]}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionConflictClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var conflicts = await client.ListAsync("0001", "Password123");
        Equal(1, conflicts.Count, "conflict list count");
        Equal("employee_no_only", conflicts[0].MatchKind, "conflict match kind");
        Equal(1, conflicts[0].Candidates.Count, "conflict candidate count");
        Equal("emp_y", conflicts[0].Candidates[0].EmployeeId, "Employee No candidate");
    }

    private static async Task ResolveTargetsExplicitCandidateAsync()
    {
        var handler = new QueueHandler();
        handler.Enqueue(request =>
        {
            Equal(HttpMethod.Post, request.Method, "conflict resolve method");
            Equal("/v1/employee-transition/conflicts/resolve", request.RequestUri?.AbsolutePath ?? string.Empty, "conflict resolve path");
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var body = JsonDocument.Parse(payload);
            Equal("0001", body.RootElement.GetProperty("actorEmployeeNo").GetString() ?? string.Empty, "resolve actor Employee No");
            Equal("Password123", body.RootElement.GetProperty("actorPassword").GetString() ?? string.Empty, "resolve actor password");
            Equal("eti_123", body.RootElement.GetProperty("transitionItemId").GetString() ?? string.Empty, "resolve transition item");
            Equal("emp_y", body.RootElement.GetProperty("targetEmployeeId").GetString() ?? string.Empty, "resolve target Employee");
            return JsonResponse(HttpStatusCode.OK,
                """{"ok":true,"resolved":true,"transitionItemId":"eti_123","resolvedByEmployeeId":"emp_x","targetEmployee":{"employeeId":"emp_y","employeeNo":"0002","name":"Y Cloud","email":"old@example.test","role":"ADMIN","enabled":true,"emailVerified":true,"credentialReady":true,"credentialVersion":2,"revision":4},"conflict":null}""");
        });

        using var http = new HttpClient(handler);
        var client = new CloudEmployeeTransitionConflictClient(http, new Uri("https://cloud.example.test/"), DeviceToken);
        var employee = await client.ResolveAsync("0001", "Password123", "eti_123", "emp_y");
        Equal("emp_y", employee.EmployeeId, "resolved Employee ID");
        Equal("ADMIN", employee.Role, "resolved Employee role");
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
            if (responses.Count == 0) throw new InvalidOperationException("Unexpected HTTP request in transition conflict contract test.");
            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
