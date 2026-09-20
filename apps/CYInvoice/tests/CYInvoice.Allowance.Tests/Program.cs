using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Func<Task> Run)[]
{
    ("allowance PDF official styles are exact", () => RunSync(TestOfficialStyles)),
    ("allowance PDF signs request caches and fingerprints official data", TestPdfCacheAndFingerprintAsync),
    ("allowance PDF code 15 synchronizes time once", TestPdfTimeSyncAsync),
    ("allowance PDF rejects untrusted file URL", TestPdfRejectsUntrustedUrlAsync),
    ("allowance PDF rejects non PDF response", TestPdfRejectsInvalidContentAsync),
    ("allowance void wrong credentials query nothing", TestAllowanceVoidWrongCredentialsAsync),
    ("allowance void requires one completed official allowance", TestAllowanceVoidEligibilityAsync),
    ("allowance void duplicate request is idempotent", TestAllowanceVoidDuplicateAsync),
    ("ordinary employee cannot complete allowance void work", TestAllowanceVoidEmployeeCannotCompleteAsync),
    ("manager completion is local only", TestAllowanceVoidManagerCompletesLocallyAsync),
    ("manager cancellation is local only", TestAllowanceVoidManagerCancelsLocallyAsync),
    ("expired allowance void work supports administrative closure", TestAllowanceVoidAdministrativeClosureAsync),
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
Console.WriteLine($"{tests.Length - failures}/{tests.Length} allowance tests passed");
return failures == 0 ? 0 : 1;

static Task RunSync(Action action)
{
    action();
    return Task.CompletedTask;
}

static void TestOfficialStyles()
{
    Equal(3, AllowancePdfStyles.Official.Count);
    Equal(0, AllowancePdfStyles.Official[0].Code);
    Equal(1, AllowancePdfStyles.Official[1].Code);
    Equal(3, AllowancePdfStyles.Official[2].Code);
    Throws<ArgumentOutOfRangeException>(() => AllowancePdfStyles.Require(2));
}

static async Task TestPdfCacheAndFingerprintAsync()
{
    using var temporary = new TemporaryDirectory("pdf-cache");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post) return JsonResponse(FileSuccess("https://invoice.amego.tw/allowance-a.pdf"));
        if (request.Method == HttpMethod.Get && request.RequestUri?.Host == "invoice.amego.tw") return PdfResponse("first-pdf");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });
    using var client = new HttpClient(handler);
    var clock = new DateTimeOffset(2026, 9, 20, 12, 30, 0, TimeSpan.FromHours(8));
    var service = new AllowancePdfService(repository, client, () => clock);

    var first = await service.GetAsync("ALW-001", 0);
    var second = await service.GetAsync("ALW-001", 0);
    Equal(false, first.FromCache);
    Equal(true, second.FromCache);
    Equal(first.Path, second.Path);
    Equal(2, handler.Requests.Count);

    var post = handler.Requests[0];
    Equal(HttpMethod.Post, post.Method);
    Equal("/json/allowance_file", post.Uri.AbsolutePath);
    var form = ParseForm(post.Body);
    Equal(AmegoDefaults.TestInvoice, form["invoice"]);
    using (var data = JsonDocument.Parse(form["data"]))
    {
        Equal("ALW-001", data.RootElement.GetProperty("allowance_number").GetString());
        Equal(0, data.RootElement.GetProperty("download_style").GetInt32());
    }
#pragma warning disable CA5351 // Test verifies AMEGO's documented MD5 signing protocol.
    var expectedSign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
        form["data"] + form["time"] + AmegoDefaults.TestAppKey))).ToLowerInvariant();
#pragma warning restore CA5351
    Equal(expectedSign, form["sign"]);

    var stored = repository.Invoices.LoadOrCreate().Single();
    InvoiceAllowanceMetadata.ApplyQuery(stored, [Allowance(UploadStatuses.Processing)]);
    repository.Invoices.Save([stored]);

    var third = await service.GetAsync("ALW-001", 0);
    Equal(false, third.FromCache);
    NotEqual(first.Path, third.Path);
    Equal(4, handler.Requests.Count);
    Equal(false, File.Exists(first.Path));
}

static async Task TestPdfTimeSyncAsync()
{
    using var temporary = new TemporaryDirectory("pdf-time");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var postCount = 0;
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post)
        {
            postCount++;
            return postCount == 1
                ? JsonResponse("{\"code\":15,\"msg\":\"time\",\"data\":{}}")
                : JsonResponse(FileSuccess("https://invoice.amego.tw/allowance-time.pdf"));
        }
        if (request.RequestUri?.AbsolutePath == "/json/time")
            return JsonResponse("{\"timestamp\":1789878660}");
        if (request.RequestUri?.Host == "invoice.amego.tw") return PdfResponse("time-pdf");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client, () =>
        new DateTimeOffset(2026, 9, 20, 12, 30, 0, TimeSpan.FromHours(8)));

    var result = await service.GetAsync("ALW-001", 1);
    Equal(false, result.FromCache);
    Equal(2, postCount);
    Equal(4, handler.Requests.Count);
    Equal(1, handler.Requests.Count(item => item.Uri.AbsolutePath == "/json/time"));
}

static async Task TestPdfRejectsUntrustedUrlAsync()
{
    using var temporary = new TemporaryDirectory("pdf-url");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
        request.Method == HttpMethod.Post
            ? JsonResponse(FileSuccess("https://example.com/not-amego.pdf"))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client);

    await ThrowsAsync<InvalidDataException>(() => service.GetAsync("ALW-001", 3));
    Equal(1, handler.Requests.Count);
}

static async Task TestPdfRejectsInvalidContentAsync()
{
    using var temporary = new TemporaryDirectory("pdf-invalid");
    var repository = CreatePdfRepository(temporary.Path, UploadStatuses.Complete);
    var handler = new RecordingHttpHandler((request, _, _) =>
    {
        if (request.Method == HttpMethod.Post) return JsonResponse(FileSuccess("https://invoice.amego.tw/not-pdf"));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("not a pdf"u8.ToArray()) };
    });
    using var client = new HttpClient(handler);
    var service = new AllowancePdfService(repository, client);

    await ThrowsAsync<InvalidDataException>(() => service.GetAsync("ALW-001", 0));
    Equal(2, handler.Requests.Count);
}

static async Task TestAllowanceVoidWrongCredentialsAsync()
{
    using var temporary = new TemporaryDirectory("void-wrong");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, "ALW-001", "3015", "wrong", "退貨"));
    Equal(0, setup.Gateway.QueryCalls);
}

static async Task TestAllowanceVoidEligibilityAsync()
{
    using (var temporary = new TemporaryDirectory("void-pending"))
    {
        var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Processing)]);
        await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
            setup.Record, "ALW-001", "3015", "Employee1", "退貨"));
        Equal(1, setup.Gateway.QueryCalls);
        Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    }

    using (var temporary = new TemporaryDirectory("void-duplicate-official"))
    {
        var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete), Allowance(UploadStatuses.Complete)]);
        await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
            setup.Record, "ALW-001", "3015", "Employee1", "退貨"));
        Equal(1, setup.Gateway.QueryCalls);
        Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    }
}

static async Task TestAllowanceVoidDuplicateAsync()
{
    using var temporary = new TemporaryDirectory("void-idempotent");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    var first = await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var second = await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");

    Equal(false, first.AlreadyQueued);
    Equal(true, second.AlreadyQueued);
    Equal(2, setup.Gateway.QueryCalls);
    Equal(1, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidEmployeeCannotCompleteAsync()
{
    using var temporary = new TemporaryDirectory("void-auth");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    Throws<UnauthorizedAccessException>(() => setup.Workflow.MarkManualCompleted(issue, "3015", "Employee1"));
    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(true, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()) is not null);
}

static async Task TestAllowanceVoidManagerCompletesLocallyAsync()
{
    using var temporary = new TemporaryDirectory("void-complete");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    setup.Workflow.MarkManualCompleted(issue, "2000", "AdminPass1");

    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidManagerCancelsLocallyAsync()
{
    using var temporary = new TemporaryDirectory("void-cancel");
    var setup = CreateVoidSetup(temporary.Path, [Allowance(UploadStatuses.Complete)]);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var calls = setup.Gateway.QueryCalls;

    setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");

    Equal(calls, setup.Gateway.QueryCalls);
    Equal(0, setup.Gateway.OtherCalls);
    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
}

static async Task TestAllowanceVoidAdministrativeClosureAsync()
{
    using var temporary = new TemporaryDirectory("void-admin-close");
    var clock = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));
    var setup = CreateVoidSetup(
        temporary.Path,
        [Allowance(UploadStatuses.Complete, date: "20260630")],
        invoiceDate: "20260630",
        clock: clock);
    await setup.Workflow.SubmitAsync(setup.Record, "ALW-001", "3015", "Employee1", "退貨");
    var issue = UnresolvedAllowanceVoidIssues(setup.Repository).Single();
    var closure = new InvoiceAdministrativeClosureService(setup.Repository, () => clock);

    Equal(true, closure.CanClose(issue));
    closure.Close(issue, "2000", "AdminPass1");

    Equal(null, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()));
    Equal(0, UnresolvedAllowanceVoidIssues(setup.Repository).Count);
    Equal(0, setup.Gateway.OtherCalls);
}

static LocalRepository CreatePdfRepository(string path, int allowanceStatus)
{
    var repository = LocalRepository.Open(path, new TestProtector());
    var record = NewRecord("2026/09/20");
    InvoiceAllowanceMetadata.ApplyQuery(record, [Allowance(allowanceStatus)]);
    repository.Invoices.Append(record);
    return repository;
}

static VoidSetup CreateVoidSetup(
    string path,
    IReadOnlyList<InvoiceAllowanceResult> allowances,
    string invoiceDate = "20260920",
    DateTimeOffset? clock = null)
{
    var repository = LocalRepository.Open(path, new TestProtector());
    repository.Employees.CreateFirstSuperAdmin("0001", "超管", "super@example.com", "SuperPass1");
    repository.Employees.CreateEmployee("0001", "3015", "員工", "employee@example.com", "Employee1");
    repository.Employees.CreateEmployee("0001", "2000", "管理員", "admin@example.com", "AdminPass1", EmployeeRoles.Admin);

    var displayDate = invoiceDate.Length == 8
        ? invoiceDate[..4] + "/" + invoiceDate[4..6] + "/" + invoiceDate[6..]
        : invoiceDate;
    var record = NewRecord(displayDate);
    repository.Invoices.Append(record);

    var gateway = new FakeGateway { QueryResponse = Query(allowances, invoiceDate) };
    var current = clock ?? new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(8));
    var detail = new InvoiceDetailRefreshService(repository, (_, _) => gateway, () => current);
    var workflow = new EmployeeAllowanceVoidWorkflowService(repository, detail, () => current);
    return new VoidSetup(repository, gateway, workflow, record);
}

static InvoiceRecord NewRecord(string invoiceDate) => new()
{
    Id = Guid.NewGuid().ToString("N"),
    SellerInvoice = AmegoDefaults.TestInvoice,
    Environment = Environments.Test,
    Source = InvoiceSources.Manual,
    RecordOrigin = RecordOrigins.Local,
    OriginalOrderId = "M20260920001",
    OrderId = "M20260920001",
    ApiOrderId = "M20260920001",
    InvoiceNumber = "AA12345678",
    InvoiceState = InvoiceStates.Opened,
    Amount = 100,
    Delivery = InvoiceService.DeliveryPaper,
    UploadStatus = UploadStatuses.Complete,
    UploadStatusText = "完成",
    InvoiceDate = invoiceDate,
    InvoiceTime = "12:00:00",
    Items = [],
};

static InvoiceAllowanceResult Allowance(int status, string date = "20260920") =>
    new("D0401", status, 2, "ALW-001", date, "5", "95");

static QueryResponse Query(IReadOnlyList<InvoiceAllowanceResult> allowances, string invoiceDate)
{
    var data = new QueryResult(
        InvoiceNumber: "AA12345678",
        InvoiceType: "A0401",
        InvoiceStatus: UploadStatuses.Complete,
        InvoiceDate: invoiceDate,
        InvoiceTime: "120000",
        BuyerIdentifier: "",
        BuyerName: "消費者",
        SalesAmount: "95",
        TaxAmount: "5",
        TotalAmount: "100",
        CarrierType: "",
        CarrierId1: "",
        CarrierId2: "",
        NpoBan: "",
        CancelDate: 0,
        OrderId: "M20260920001",
        CreateDate: 1789876800,
        ProductItems: JsonSerializer.SerializeToElement(Array.Empty<object>()),
        DetailVat: 1,
        DetailVatPresent: true)
    {
        Allowances = allowances,
    };
    return new QueryResponse(0, "", data);
}

static IReadOnlyList<InvoiceSyncIssue> UnresolvedAllowanceVoidIssues(LocalRepository repository) =>
    new InvoiceSyncIssueStore(repository.DataDirectory)
        .Unresolved(Environments.Test + "|" + AmegoDefaults.TestInvoice)
        .Where(issue => issue.IssueType == InvoiceAllowanceVoidIssueTypes.ManualReview)
        .ToArray();

static string FileSuccess(string url) =>
    JsonSerializer.Serialize(new { code = 0, msg = "", data = new { file_url = url } });

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json"),
};

static HttpResponseMessage PdfResponse(string marker)
{
    var body = Encoding.UTF8.GetBytes("%PDF-1.4\n" + marker + "\n%%EOF");
    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
    return response;
}

static Dictionary<string, string> ParseForm(string body)
{
    return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            item => Uri.UnescapeDataString(item[0].Replace('+', ' ')),
            item => Uri.UnescapeDataString((item.Length > 1 ? item[1] : string.Empty).Replace('+', ' ')),
            StringComparer.Ordinal);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
}

static void NotEqual<T>(T first, T second)
{
    if (EqualityComparer<T>.Default.Equals(first, second))
        throw new InvalidOperationException($"values should differ: {first}");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

sealed record VoidSetup(
    LocalRepository Repository,
    FakeGateway Gateway,
    EmployeeAllowanceVoidWorkflowService Workflow,
    InvoiceRecord Record);

sealed record RequestSnapshot(HttpMethod Method, Uri Uri, string Body);

sealed class RecordingHttpHandler(
    Func<HttpRequestMessage, string, int, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RequestSnapshot> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("request URI missing");
        Requests.Add(new RequestSnapshot(request.Method, uri, body));
        return responder(request, body, Requests.Count);
    }
}

sealed class FakeGateway : IAmegoGateway
{
    public QueryResponse QueryResponse { get; set; } = null!;
    public int QueryCalls { get; private set; }
    public int OtherCalls { get; private set; }

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return Task.FromResult(QueryResponse);
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return Task.FromResult(QueryResponse);
    }

    public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }

    public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default)
    {
        OtherCalls++;
        throw new NotSupportedException();
    }
}

sealed class TestProtector : ISecretProtector
{
    private const string Prefix = "allowance-test:";
    public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
    public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
        ? Convert.FromBase64String(ciphertext[Prefix.Length..])
        : throw new InvalidDataException("invalid test secret");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string name)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CYInvoice-allowance-tests-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
