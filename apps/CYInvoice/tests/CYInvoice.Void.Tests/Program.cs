using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Action Run)[]
{
    ("f0501 serializes one-item array with exact fields", () => TestF0501PayloadAsync().GetAwaiter().GetResult()),
    ("f0501 rejects reason longer than twenty characters", () => TestF0501ReasonLimitAsync().GetAwaiter().GetResult()),
    ("invoice_query detects pending C0501", () => TestQueryDetectsPendingVoidAsync().GetAwaiter().GetResult()),
    ("preflight already voided never sends f0501", () => TestAlreadyVoidedAsync().GetAwaiter().GetResult()),
    ("preflight pending void never sends f0501", () => TestPreflightPendingAsync().GetAwaiter().GetResult()),
    ("preflight requires official status 99", () => TestPreflightRequiresCompleteAsync().GetAwaiter().GetResult()),
    ("successful f0501 still requires authoritative confirmation", () => TestConfirmedVoidAsync().GetAwaiter().GetResult()),
    ("accepted but not yet visible remains pending", () => TestAcceptedButUnconfirmedAsync().GetAwaiter().GetResult()),
    ("transport ambiguity remains pending and blocks blind resend", () => TestTransportAmbiguityAsync().GetAwaiter().GetResult()),
    ("later stable open query only unlocks retry without resending", () => TestReconcileUnlocksRetryAsync().GetAwaiter().GetResult()),
    ("explicit rejection clears pending state without resend", () => TestExplicitRejectionAsync().GetAwaiter().GetResult()),
    ("concurrent same invoice sends at most one f0501", () => TestConcurrentVoidAsync().GetAwaiter().GetResult()),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
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

static async Task TestF0501PayloadAsync()
{
    string data = string.Empty;
    var calls = 0;
    using var http = new HttpClient(new StubHandler(async request =>
    {
        calls++;
        Equal("/json/f0501", request.RequestUri?.AbsolutePath);
        var form = ParseForm(await request.Content!.ReadAsStringAsync());
        Equal("12345678", form["invoice"]);
        data = form["data"];
        return JsonResponse("{\"code\":0,\"msg\":\"OK\"}");
    }));
    var client = new AmegoClient(
        "12345678",
        "test-key",
        http,
        () => new DateTimeOffset(2026, 9, 19, 14, 0, 0, TimeSpan.FromHours(8)));

    var result = await client.VoidAsync(new VoidRequest
    {
        CancelInvoiceNumber = "AA12345678",
        CancelReason = "3015 退貨",
    });

    Equal(1, calls);
    Equal(0, result.Code);
    using var document = JsonDocument.Parse(data);
    Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    Equal(1, document.RootElement.GetArrayLength());
    var item = document.RootElement[0];
    Equal("AA12345678", item.GetProperty("CancelInvoiceNumber").GetString());
    Equal("3015 退貨", item.GetProperty("CancelReason").GetString());
}

static async Task TestF0501ReasonLimitAsync()
{
    var calls = 0;
    using var http = new HttpClient(new StubHandler(_ =>
    {
        calls++;
        return Task.FromResult(JsonResponse("{\"code\":0,\"msg\":\"OK\"}"));
    }));
    var client = new AmegoClient("12345678", "test-key", http);
    await ThrowsAsync<ArgumentException>(() => client.VoidAsync(new VoidRequest
    {
        CancelInvoiceNumber = "AA12345678",
        CancelReason = new string('退', 21),
    }));
    Equal(0, calls);
}

static async Task TestQueryDetectsPendingVoidAsync()
{
    using var http = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse("""
        {"code":0,"msg":"","data":{"invoice_number":"AA12345678","invoice_type":"A0401","invoice_status":99,"cancel_date":0,"order_id":"O1","wait":[{"invoice_type":"C0501","create_date":1789790000}]}}
        """))));
    var client = new AmegoClient("12345678", "test-key", http);
    var response = await client.QueryByInvoiceNumberAsync("AA12345678");
    Equal(true, response.Data.VoidPending);
}

static async Task TestAlreadyVoidedAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway();
    gateway.Queries.Enqueue(Query(cancelDate: 1789790000));
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.AlreadyVoided, result.Outcome);
    Equal(false, result.RequestSent);
    Equal(0, gateway.VoidCalls);
    Equal(InvoiceStates.Voided, repository.Invoices.LoadOrCreate().Single().InvoiceState);
}

static async Task TestPreflightPendingAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway();
    gateway.Queries.Enqueue(Query(voidPending: true));
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
    Equal(false, result.RequestSent);
    Equal(0, gateway.VoidCalls);
    Equal(true, PendingMarker(repository));
}

static async Task TestPreflightRequiresCompleteAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway();
    gateway.Queries.Enqueue(Query(status: UploadStatuses.Processing));
    var (service, _, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.Rejected, result.Outcome);
    Equal(false, result.RequestSent);
    Equal(0, gateway.VoidCalls);
}

static async Task TestConfirmedVoidAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway();
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query(cancelDate: 1789790000));
    gateway.VoidResponse = new VoidResponse(0, "OK");
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.Confirmed, result.Outcome);
    Equal(true, result.RequestSent);
    Equal(1, gateway.VoidCalls);
    Equal(InvoiceStates.Voided, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    Equal(false, PendingMarker(repository));
}

static async Task TestAcceptedButUnconfirmedAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway();
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query());
    gateway.VoidResponse = new VoidResponse(0, "OK");
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
    Equal(1, gateway.VoidCalls);
    Equal(true, PendingMarker(repository));
    Equal(InvoiceStates.Changing, repository.Invoices.LoadOrCreate().Single().InvoiceState);
}

static async Task TestTransportAmbiguityAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway { VoidException = new TaskCanceledException("simulated timeout") };
    gateway.Queries.Enqueue(Query());
    gateway.QueryFailures.Enqueue(new HttpRequestException("query unavailable"));
    gateway.StatusFailures.Enqueue(null);
    gateway.StatusFailures.Enqueue(new HttpRequestException("status unavailable"));
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
    Equal(1, gateway.VoidCalls);
    Equal(true, PendingMarker(repository));
}

static async Task TestReconcileUnlocksRetryAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway { VoidResponse = new VoidResponse(0, "OK") };
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query());
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var first = await service.VoidAsync(record, "3015 退貨");
    Equal(InvoiceVoidOutcome.PendingConfirmation, first.Outcome);
    Equal(1, gateway.VoidCalls);

    var second = await service.VoidAsync(repository.Invoices.LoadOrCreate().Single(), "3015 退貨");
    Equal(InvoiceVoidOutcome.RetryReady, second.Outcome);
    Equal(false, second.RequestSent);
    Equal(1, gateway.VoidCalls);
    Equal(false, PendingMarker(repository));
    Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
}

static async Task TestExplicitRejectionAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway { VoidResponse = new VoidResponse(3050126, "已超過修改期限") };
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query());
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var result = await service.VoidAsync(record, "3015 退貨");

    Equal(InvoiceVoidOutcome.Rejected, result.Outcome);
    Equal(3050126, result.ApiCode);
    Equal(1, gateway.VoidCalls);
    Equal(false, PendingMarker(repository));
    Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
}

static async Task TestConcurrentVoidAsync()
{
    using var temporary = new TemporaryDirectory();
    var gateway = new FakeGateway
    {
        VoidResponse = new VoidResponse(0, "OK"),
        VoidStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        VoidRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    gateway.Queries.Enqueue(Query());
    gateway.Queries.Enqueue(Query(voidPending: true));
    gateway.Queries.Enqueue(Query(voidPending: true));
    var (service, repository, record) = CreateService(temporary.Path, gateway);

    var firstTask = service.VoidAsync(record, "3015 退貨");
    await gateway.VoidStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var secondTask = service.VoidAsync(record, "3015 退貨");
    gateway.VoidRelease.SetResult(true);

    var first = await firstTask;
    var second = await secondTask;
    Equal(InvoiceVoidOutcome.PendingConfirmation, first.Outcome);
    Equal(InvoiceVoidOutcome.PendingConfirmation, second.Outcome);
    Equal(1, gateway.VoidCalls);
    Equal(true, PendingMarker(repository));
}

static (InvoiceVoidService Service, LocalRepository Repository, InvoiceRecord Record) CreateService(
    string path,
    FakeGateway gateway)
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
    var service = new InvoiceVoidService(
        repository,
        (_, _) => gateway,
        () => new DateTimeOffset(2026, 9, 19, 14, 32, 0, TimeSpan.FromHours(8)));
    return (service, repository, record);
}

static QueryResponse Query(
    int status = UploadStatuses.Complete,
    long cancelDate = 0,
    bool voidPending = false,
    string type = "A0401") => new(
        0,
        "",
        new QueryResult(
            InvoiceNumber: "AA12345678",
            InvoiceType: type,
            InvoiceStatus: status,
            InvoiceDate: "20260919",
            InvoiceTime: "140000",
            BuyerIdentifier: "",
            BuyerName: "消費者",
            SalesAmount: "95",
            TaxAmount: "5",
            TotalAmount: "100",
            CarrierType: "",
            CarrierId1: "",
            CarrierId2: "",
            NpoBan: "",
            CancelDate: cancelDate,
            OrderId: "M20260919001",
            CreateDate: 1789790000,
            ProductItems: default,
            DetailVat: 1,
            DetailVatPresent: true,
            VoidPending: voidPending));

static bool PendingMarker(LocalRepository repository)
{
    var record = repository.Invoices.LoadOrCreate().Single();
    return record.ExtensionData is not null &&
           record.ExtensionData.TryGetValue("cyinvoice_void_pending", out var value) &&
           value.ValueKind == JsonValueKind.True;
}

static Dictionary<string, string> ParseForm(string value)
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

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json"),
};

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
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

sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}

sealed class FakeGateway : IAmegoGateway
{
    public Queue<QueryResponse> Queries { get; } = new();
    public Queue<Exception?> QueryFailures { get; } = new();
    public Queue<Exception?> StatusFailures { get; } = new();
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
        if (QueryFailures.Count != 0 && QueryFailures.Dequeue() is { } failure)
            return Task.FromException<QueryResponse>(failure);
        if (Queries.Count == 0) return Task.FromResult(Query());
        return Task.FromResult(Queries.Dequeue());
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        if (StatusFailures.Count != 0 && StatusFailures.Dequeue() is { } failure)
            return Task.FromException<StatusResponse>(failure);
        return Task.FromResult(new StatusResponse(0, "", [new StatusResult("AA12345678", "A0401", UploadStatuses.Complete, "100")]));
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
