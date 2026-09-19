using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("f0501 serializes one-item array with exact fields", Cases.F0501PayloadAsync),
    ("f0501 rejects reason longer than twenty characters", Cases.F0501ReasonLimitAsync),
    ("invoice_query detects pending C0501", Cases.QueryDetectsPendingVoidAsync),
    ("invoice_query parses allowance array", Cases.QueryParsesAllowancesAsync),
    ("preflight already voided never sends f0501", Cases.AlreadyVoidedAsync),
    ("preflight pending void never sends f0501", Cases.PreflightPendingAsync),
    ("preflight requires official status 99", Cases.PreflightRequiresCompleteAsync),
    ("successful f0501 still requires authoritative confirmation", Cases.ConfirmedVoidAsync),
    ("accepted but not yet visible remains pending", Cases.AcceptedButUnconfirmedAsync),
    ("transport ambiguity remains pending and blocks blind resend", Cases.TransportAmbiguityAsync),
    ("later stable open query only unlocks retry without resending", Cases.ReconcileUnlocksRetryAsync),
    ("explicit rejection clears pending state without resend", Cases.ExplicitRejectionAsync),
    ("concurrent same invoice sends at most one f0501", Cases.ConcurrentVoidAsync),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} void core tests passed");
return failures == 0 ? 0 : 1;

static class Cases
{
    public static async Task F0501PayloadAsync()
    {
        string data = string.Empty;
        var calls = 0;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            AssertEx.Equal("/json/f0501", request.RequestUri?.AbsolutePath);
            var form = Fixtures.ParseForm(await request.Content!.ReadAsStringAsync());
            AssertEx.Equal("12345678", form["invoice"]);
            data = form["data"];
            return Fixtures.JsonResponse("{\"code\":0,\"msg\":\"OK\"}");
        }));
        var client = new AmegoClient("12345678", "test-key", http,
            () => new DateTimeOffset(2026, 9, 19, 14, 0, 0, TimeSpan.FromHours(8)));

        var response = await client.VoidAsync(new VoidRequest
        {
            CancelInvoiceNumber = "AA12345678",
            CancelReason = "3015 退貨",
        });

        AssertEx.Equal(1, calls);
        AssertEx.Equal(0, response.Code);
        using var document = JsonDocument.Parse(data);
        AssertEx.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        AssertEx.Equal(1, document.RootElement.GetArrayLength());
        var item = document.RootElement[0];
        AssertEx.Equal("AA12345678", item.GetProperty("CancelInvoiceNumber").GetString());
        AssertEx.Equal("3015 退貨", item.GetProperty("CancelReason").GetString());
    }

    public static async Task F0501ReasonLimitAsync()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return Task.FromResult(Fixtures.JsonResponse("{\"code\":0,\"msg\":\"OK\"}"));
        }));
        var client = new AmegoClient("12345678", "test-key", http);
        await AssertEx.ThrowsAsync<ArgumentException>(() => client.VoidAsync(new VoidRequest
        {
            CancelInvoiceNumber = "AA12345678",
            CancelReason = new string('退', 21),
        }));
        AssertEx.Equal(0, calls);
    }

    public static async Task QueryDetectsPendingVoidAsync()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(Fixtures.JsonResponse("""
            {"code":0,"msg":"","data":{"invoice_number":"AA12345678","invoice_type":"A0401","invoice_status":99,"cancel_date":0,"order_id":"O1","wait":[{"invoice_type":"C0501","create_date":1789790000}]}}
            """))));
        var client = new AmegoClient("12345678", "test-key", http);
        var response = await client.QueryByInvoiceNumberAsync("AA12345678");
        AssertEx.Equal(true, response.Data.VoidPending);
    }

    public static async Task QueryParsesAllowancesAsync()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(Fixtures.JsonResponse("""
            {"code":0,"msg":"","data":{"invoice_number":"AA12345678","invoice_type":"A0401","invoice_status":99,"cancel_date":0,"order_id":"O1","allowance":[{"invoice_type":"D0401","invoice_status":99,"allowance_type":"2","allowance_number":"ALW20260919001","allowance_date":20260919,"tax_amount":"5","total_amount":95}]}}
            """))));
        var client = new AmegoClient("12345678", "test-key", http);
        var response = await client.QueryByInvoiceNumberAsync("AA12345678");
        var allowance = response.Data.Allowances.Single();
        AssertEx.Equal("D0401", allowance.InvoiceType);
        AssertEx.Equal(UploadStatuses.Complete, allowance.InvoiceStatus);
        AssertEx.Equal(2, allowance.AllowanceType);
        AssertEx.Equal("ALW20260919001", allowance.AllowanceNumber);
        AssertEx.Equal("20260919", allowance.AllowanceDate);
        AssertEx.Equal("5", allowance.TaxAmount);
        AssertEx.Equal("95", allowance.TotalAmount);
    }

    public static async Task AlreadyVoidedAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway();
        gateway.QueryPlan.Enqueue(Fixtures.Query(cancelDate: 1789790000));
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.AlreadyVoided, result.Outcome);
        AssertEx.Equal(false, result.RequestSent);
        AssertEx.Equal(0, gateway.VoidCalls);
        AssertEx.Equal(InvoiceStates.Voided, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    }

    public static async Task PreflightPendingAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway();
        gateway.QueryPlan.Enqueue(Fixtures.Query(voidPending: true));
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
        AssertEx.Equal(false, result.RequestSent);
        AssertEx.Equal(0, gateway.VoidCalls);
        AssertEx.Equal(true, Fixtures.PendingMarker(repository));
    }

    public static async Task PreflightRequiresCompleteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway();
        gateway.QueryPlan.Enqueue(Fixtures.Query(status: UploadStatuses.Processing));
        var (service, _, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.Rejected, result.Outcome);
        AssertEx.Equal(false, result.RequestSent);
        AssertEx.Equal(0, gateway.VoidCalls);
    }

    public static async Task ConfirmedVoidAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway { VoidResponse = new VoidResponse(0, "OK") };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query(cancelDate: 1789790000));
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.Confirmed, result.Outcome);
        AssertEx.Equal(true, result.RequestSent);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(InvoiceStates.Voided, repository.Invoices.LoadOrCreate().Single().InvoiceState);
        AssertEx.Equal(false, Fixtures.PendingMarker(repository));
    }

    public static async Task AcceptedButUnconfirmedAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway { VoidResponse = new VoidResponse(0, "OK") };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(true, Fixtures.PendingMarker(repository));
        AssertEx.Equal(InvoiceStates.OpenedWaitingVoid, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    }

    public static async Task TransportAmbiguityAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway { VoidException = new TaskCanceledException("simulated timeout") };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(new HttpRequestException("query unavailable"));
        gateway.StatusPlan.Enqueue(Fixtures.StatusOriginal());
        gateway.StatusPlan.Enqueue(new HttpRequestException("status unavailable"));
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(true, Fixtures.PendingMarker(repository));
    }

    public static async Task ReconcileUnlocksRetryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway { VoidResponse = new VoidResponse(0, "OK") };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var first = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, first.Outcome);
        AssertEx.Equal(1, gateway.VoidCalls);

        var second = await service.VoidAsync(repository.Invoices.LoadOrCreate().Single(), "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.RetryReady, second.Outcome);
        AssertEx.Equal(false, second.RequestSent);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(false, Fixtures.PendingMarker(repository));
        AssertEx.Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    }

    public static async Task ExplicitRejectionAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway { VoidResponse = new VoidResponse(3050126, "已超過修改期限") };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);
        var result = await service.VoidAsync(record, "3015 退貨");
        AssertEx.Equal(InvoiceVoidOutcome.Rejected, result.Outcome);
        AssertEx.Equal(3050126, result.ApiCode);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(false, Fixtures.PendingMarker(repository));
        AssertEx.Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    }

    public static async Task ConcurrentVoidAsync()
    {
        using var temporary = new TemporaryDirectory();
        var gateway = new FakeGateway
        {
            VoidResponse = new VoidResponse(0, "OK"),
            VoidStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            VoidRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        gateway.QueryPlan.Enqueue(Fixtures.Query());
        gateway.QueryPlan.Enqueue(Fixtures.Query(voidPending: true));
        gateway.QueryPlan.Enqueue(Fixtures.Query(voidPending: true));
        var (service, repository, record) = Fixtures.CreateService(temporary.Path, gateway);

        var firstTask = service.VoidAsync(record, "3015 退貨");
        await gateway.VoidStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = service.VoidAsync(record, "3015 退貨");
        gateway.VoidRelease.SetResult(true);
        var first = await firstTask;
        var second = await secondTask;

        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, first.Outcome);
        AssertEx.Equal(InvoiceVoidOutcome.PendingConfirmation, second.Outcome);
        AssertEx.Equal(1, gateway.VoidCalls);
        AssertEx.Equal(true, Fixtures.PendingMarker(repository));
    }
}

static class Fixtures
{
    public static (InvoiceVoidService Service, LocalRepository Repository, InvoiceRecord Record) CreateService(string path, FakeGateway gateway)
    {
        var repository = LocalRepository.Open(path, new TestProtector());
        var record = new InvoiceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SellerInvoice = AmegoDefaults.TestInvoice,
            Environment = Environments.Test,
            Source = "手動",
            RecordOrigin = RecordOrigins.Local,
            OriginalOrderId = "M20260919001",
            OrderId = "M20260919001",
            ApiOrderId = "M20260919001",
            InvoiceNumber = "AA12345678",
            InvoiceState = InvoiceStates.Opened,
            Amount = 100,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成",
            InvoiceDate = "2026/09/19",
            InvoiceTime = "14:00:00",
            Items = [],
        };
        repository.Invoices.Append(record);
        return (
            new InvoiceVoidService(repository, (_, _) => gateway,
                () => new DateTimeOffset(2026, 9, 19, 14, 32, 0, TimeSpan.FromHours(8))),
            repository,
            record);
    }

    public static QueryResponse Query(
        int status = UploadStatuses.Complete,
        long cancelDate = 0,
        bool voidPending = false,
        string type = "A0401") => new(
        0,
        "",
        new QueryResult(
            "AA12345678", type, status, "20260919", "140000", "", "消費者",
            "95", "5", "100", "", "", "", "", cancelDate, "M20260919001",
            1789790000, default, 1, true, voidPending));

    public static StatusResponse StatusOriginal() =>
        new(0, "", [new StatusResult("AA12345678", "A0401", UploadStatuses.Complete, "100")]);

    public static bool PendingMarker(LocalRepository repository)
    {
        var record = repository.Invoices.LoadOrCreate().Single();
        return record.ExtensionData is not null &&
               record.ExtensionData.TryGetValue("cyinvoice_void_pending", out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    public static Dictionary<string, string> ParseForm(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var item = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            result[key] = item;
        }
        return result;
    }

    public static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

static class AssertEx
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"expected {typeof(T).Name}");
    }
}

sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}

sealed class FakeGateway : IAmegoGateway
{
    public Queue<object> QueryPlan { get; } = new();
    public Queue<object> StatusPlan { get; } = new();
    public VoidResponse VoidResponse { get; set; } = new(0, "OK");
    public Exception? VoidException { get; set; }
    public int VoidCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public int StatusCalls { get; private set; }
    public TaskCompletionSource<bool>? VoidStarted { get; set; }
    public TaskCompletionSource<bool>? VoidRelease { get; set; }

    public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<IssueResponse>(new NotSupportedException());

    public async Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default)
    {
        VoidCalls++;
        VoidStarted?.TrySetResult(true);
        if (VoidRelease is not null) await VoidRelease.Task.WaitAsync(cancellationToken);
        if (VoidException is not null) throw VoidException;
        return VoidResponse;
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
        QueryByInvoiceNumberAsync(orderId, cancellationToken);

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        if (QueryPlan.Count == 0) return Task.FromResult(Fixtures.Query());
        var next = QueryPlan.Dequeue();
        return next is Exception error ? Task.FromException<QueryResponse>(error) : Task.FromResult((QueryResponse)next);
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        if (StatusPlan.Count == 0) return Task.FromResult(Fixtures.StatusOriginal());
        var next = StatusPlan.Dequeue();
        return next is Exception error ? Task.FromException<StatusResponse>(error) : Task.FromResult((StatusResponse)next);
    }

    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BanResponse(0, "", []));

    public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
        Task.FromException<byte[]>(new NotSupportedException());
}

sealed class TestProtector : ISecretProtector
{
    private const string Prefix = "test-protected:";
    public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
    public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
        ? Convert.FromBase64String(ciphertext[Prefix.Length..])
        : throw new InvalidDataException("invalid protected test value");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-void-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
