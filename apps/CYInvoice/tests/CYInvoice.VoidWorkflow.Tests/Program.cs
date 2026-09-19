using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

var tests = new (string Name, Action Run)[]
{
    ("wrong invoice number makes zero AMEGO calls", () => TestWrongInvoiceNumberAsync().GetAwaiter().GetResult()),
    ("wrong employee credentials make zero AMEGO calls", () => TestWrongCredentialsAsync().GetAwaiter().GetResult()),
    ("invalid employee number remains generic and makes zero AMEGO calls", () => TestInvalidEmployeeNumberAsync().GetAwaiter().GetResult()),
    ("disabled employee makes zero AMEGO calls", () => TestDisabledEmployeeAsync().GetAwaiter().GetResult()),
    ("uncollected paper invoice queues manual review without AMEGO", () => TestUncollectedQueuesManualReviewAsync().GetAwaiter().GetResult()),
    ("ordinary employee cannot approve or cancel manual review", () => TestEmployeeCannotManageReviewAsync().GetAwaiter().GetResult()),
    ("wrong manager password cannot approve manual review", () => TestWrongManagerPasswordAsync().GetAwaiter().GetResult()),
    ("administrator can cancel manual review without changing invoice state", () => TestAdminCancelsReviewAsync().GetAwaiter().GetResult()),
    ("administrator approval sends original requester employee number", () => TestAdminApprovesReviewAsync().GetAwaiter().GetResult()),
    ("pending approved void keeps core pending marker and resolves manual review", () => TestApprovedPendingVoidAsync().GetAwaiter().GetResult()),
    ("manual review cancel is blocked after remote void becomes pending", TestCancelBlockedAfterPending),
    ("non-paper invoice proceeds without paper receipt selection", () => TestCarrierInvoiceAsync().GetAwaiter().GetResult()),
    ("allowance wrong employee credentials make zero query calls", () => AllowanceWorkflowTests.WrongCredentialsMakeZeroQueryAsync().GetAwaiter().GetResult()),
    ("employee can queue allowance manual review", () => AllowanceWorkflowTests.EmployeeQueuesManualReviewAsync().GetAwaiter().GetResult()),
    ("administrator can cancel allowance manual review", () => AllowanceWorkflowTests.AdministratorCancelsManualReviewAsync().GetAwaiter().GetResult()),
    ("administrator completed allowance waits for and confirms official result", () => AllowanceWorkflowTests.AdministratorCompletesAndOfficialQueryConfirmsAsync().GetAwaiter().GetResult()),
    ("allowance amount mismatch stays pending", () => AllowanceWorkflowTests.AmountMismatchRemainsPendingAsync().GetAwaiter().GetResult()),
    ("allowance pending cannot be cancelled", () => AllowanceWorkflowTests.PendingCannotBeCancelledAsync().GetAwaiter().GetResult()),
    ("void is blocked while allowance request exists", () => AllowanceWorkflowTests.VoidIsBlockedWhileAllowanceRequestExistsAsync().GetAwaiter().GetResult()),
    ("work inside retained two periods cannot be administratively closed", AdministrativeClosureTests.WithinTwoPeriodsCannotClose),
    ("ordinary employee cannot administratively close expired work", AdministrativeClosureTests.OrdinaryEmployeeCannotCloseExpiredWork),
    ("administrator can close expired void pending work", AdministrativeClosureTests.AdministratorClosesExpiredVoidPendingWork),
    ("administrator can close expired allowance work", AdministrativeClosureTests.AdministratorClosesExpiredAllowanceWork),
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
Console.WriteLine($"{tests.Length - failures}/{tests.Length} employee void workflow tests passed");
return failures == 0 ? 0 : 1;

static async Task TestWrongInvoiceNumberAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, "AA00000000", "3015", "employee-pass", "退貨",
        PaperInvoiceReceiptStates.Collected));
    Equal(0, setup.Gateway.TotalCalls);
}

static async Task TestWrongCredentialsAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    var error = await ThrowsWithResultAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "3015", "wrong", "退貨",
        PaperInvoiceReceiptStates.Collected));
    Equal("員工編號或密碼錯誤", error.Message);
    Equal(0, setup.Gateway.TotalCalls);
}

static async Task TestInvalidEmployeeNumberAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    var error = await ThrowsWithResultAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "30", "wrong", "退貨",
        PaperInvoiceReceiptStates.Collected));
    Equal("員工編號或密碼錯誤", error.Message);
    Equal(0, setup.Gateway.TotalCalls);
}

static async Task TestDisabledEmployeeAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    setup.Repository.Employees.SetEnabled("0001", "3015", false);
    var error = await ThrowsWithResultAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "3015", "employee-pass", "退貨",
        PaperInvoiceReceiptStates.Collected));
    Equal("員工編號或密碼錯誤", error.Message);
    Equal(0, setup.Gateway.TotalCalls);
}

static async Task TestUncollectedQueuesManualReviewAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    var result = await setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "3015", "employee-pass", "退貨",
        PaperInvoiceReceiptStates.Uncollected);

    Equal(true, result.ManualReviewRequired);
    Equal(0, setup.Gateway.TotalCalls);
    var stored = setup.Repository.Invoices.LoadOrCreate().Single();
    Equal(InvoiceStates.Opened, stored.InvoiceState);
    var review = setup.Workflow.ManualReviewFor(stored) ?? throw new InvalidOperationException("manual review marker missing");
    Equal("3015", review.RequesterEmployeeNo);
    Equal("退貨", review.Reason);
    var issue = ManualIssue(setup.Repository);
    Equal(InvoiceVoidIssueTypes.ManualReview, issue.IssueType);
    Equal(null, issue.ResolvedUtc);
}

static async Task TestEmployeeCannotManageReviewAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await QueueReviewAsync(setup);
    var issue = ManualIssue(setup.Repository);

    await ThrowsAsync<UnauthorizedAccessException>(() => setup.Workflow.ApproveManualReviewAsync(issue, "3015", "employee-pass"));
    Throws<UnauthorizedAccessException>(() => setup.Workflow.CancelManualReview(issue, "3015", "employee-pass"));
    Equal(0, setup.Gateway.TotalCalls);
    Equal(null, issue.ResolvedUtc);
}

static async Task TestWrongManagerPasswordAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await QueueReviewAsync(setup);
    var issue = ManualIssue(setup.Repository);

    await ThrowsAsync<UnauthorizedAccessException>(() => setup.Workflow.ApproveManualReviewAsync(issue, "2000", "wrong"));
    Equal(0, setup.Gateway.TotalCalls);
    Equal(true, setup.Workflow.ManualReviewFor(setup.Repository.Invoices.LoadOrCreate().Single()) is not null);
}

static async Task TestAdminCancelsReviewAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await QueueReviewAsync(setup);
    var issue = ManualIssue(setup.Repository);

    setup.Workflow.CancelManualReview(issue, "2000", "admin-pass");

    Equal(0, setup.Gateway.TotalCalls);
    var stored = setup.Repository.Invoices.LoadOrCreate().Single();
    Equal(InvoiceStates.Opened, stored.InvoiceState);
    Equal(null, setup.Workflow.ManualReviewFor(stored));
    var resolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).All(AccountKey()).Single(item => item.Id == issue.Id);
    Equal(true, resolved.ResolvedUtc is not null);
}

static async Task TestAdminApprovesReviewAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await QueueReviewAsync(setup);
    setup.Gateway.Queries.Enqueue(Query());
    setup.Gateway.Queries.Enqueue(Query(cancelDate: 1789790000));
    var issue = ManualIssue(setup.Repository);

    var result = await setup.Workflow.ApproveManualReviewAsync(issue, "2000", "admin-pass");

    Equal(InvoiceVoidOutcome.Confirmed, result.Outcome);
    Equal(1, setup.Gateway.VoidCalls);
    Equal("3015 退貨", setup.Gateway.LastVoid?.CancelReason);
    var stored = setup.Repository.Invoices.LoadOrCreate().Single();
    Equal(InvoiceStates.Voided, stored.InvoiceState);
    Equal(null, setup.Workflow.ManualReviewFor(stored));
    Equal(false, HasCorePending(stored));
    var resolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).All(AccountKey()).Single(item => item.Id == issue.Id);
    Equal(true, resolved.ResolvedUtc is not null);
}

static async Task TestApprovedPendingVoidAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    await QueueReviewAsync(setup);
    setup.Gateway.Queries.Enqueue(Query());
    setup.Gateway.Queries.Enqueue(Query(voidPending: true));
    var issue = ManualIssue(setup.Repository);

    var result = await setup.Workflow.ApproveManualReviewAsync(issue, "0001", "super-pass");

    Equal(InvoiceVoidOutcome.PendingConfirmation, result.Outcome);
    Equal(1, setup.Gateway.VoidCalls);
    var stored = setup.Repository.Invoices.LoadOrCreate().Single();
    Equal(true, HasCorePending(stored));
    Equal(null, setup.Workflow.ManualReviewFor(stored));
    var resolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).All(AccountKey()).Single(item => item.Id == issue.Id);
    Equal(true, resolved.ResolvedUtc is not null);
}

static void TestCancelBlockedAfterPending()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path);
    QueueReviewAsync(setup).GetAwaiter().GetResult();
    var issue = ManualIssue(setup.Repository);
    var stored = setup.Repository.Invoices.LoadOrCreate().Single();
    stored.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    stored.ExtensionData["cyinvoice_void_pending"] = JsonSerializer.SerializeToElement(true);
    setup.Repository.Invoices.Save([stored]);

    Throws<InvalidOperationException>(() => setup.Workflow.CancelManualReview(issue, "2000", "admin-pass"));
    var after = setup.Repository.Invoices.LoadOrCreate().Single();
    Equal(true, setup.Workflow.ManualReviewFor(after) is not null);
    Equal(true, HasCorePending(after));
}

static async Task TestCarrierInvoiceAsync()
{
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary.Path, paper: false);
    setup.Gateway.Queries.Enqueue(Query());
    setup.Gateway.Queries.Enqueue(Query(cancelDate: 1789790000));

    var result = await setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "3015", "employee-pass", "取消交易", string.Empty);

    Equal(false, result.ManualReviewRequired);
    Equal(InvoiceVoidOutcome.Confirmed, result.VoidResult?.Outcome);
    Equal("3015 取消交易", setup.Gateway.LastVoid?.CancelReason);
}

static async Task QueueReviewAsync(TestSetup setup)
{
    var result = await setup.Workflow.SubmitAsync(
        setup.Record, setup.Record.InvoiceNumber, "3015", "employee-pass", "退貨",
        PaperInvoiceReceiptStates.Uncollected);
    Equal(true, result.ManualReviewRequired);
}

static TestSetup CreateSetup(string path, bool paper = true)
{
    var repository = LocalRepository.Open(path, new TestProtector());
    repository.Employees.CreateFirstSuperAdmin("0001", "超管", "super@example.com", "super-pass");
    repository.Employees.CreateEmployee("0001", "3015", "員工", "employee@example.com", "employee-pass");
    repository.Employees.CreateEmployee("0001", "2000", "管理員", "admin@example.com", "admin-pass", EmployeeRoles.Admin);

    var record = new InvoiceRecord
    {
        Id = Guid.NewGuid().ToString("N"),
        SellerInvoice = AmegoDefaults.TestInvoice,
        Environment = Environments.Test,
        Source = InvoiceSources.Manual,
        RecordOrigin = RecordOrigins.Local,
        OriginalOrderId = "M20260919001",
        OrderId = "M20260919001",
        ApiOrderId = "M20260919001",
        InvoiceNumber = "AA12345678",
        InvoiceState = InvoiceStates.Opened,
        Amount = 100,
        Delivery = paper ? InvoiceService.DeliveryPaper : "會員載具",
        UploadStatus = UploadStatuses.Complete,
        UploadStatusText = "完成",
        InvoiceDate = "2026/09/19",
        InvoiceTime = "14:00:00",
        Items = [],
    };
    repository.Invoices.Append(record);

    var gateway = new FakeGateway();
    var clock = new DateTimeOffset(2026, 9, 19, 14, 32, 0, TimeSpan.FromHours(8));
    var voidService = new InvoiceVoidService(repository, (_, _) => gateway, () => clock);
    var workflow = new EmployeeVoidWorkflowService(repository, voidService, () => clock);
    return new TestSetup(repository, gateway, workflow, record);
}

static InvoiceSyncIssue ManualIssue(LocalRepository repository) =>
    new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved(AccountKey())
        .Single(issue => issue.IssueType == InvoiceVoidIssueTypes.ManualReview);

static string AccountKey() => Environments.Test + "|" + AmegoDefaults.TestInvoice;

static bool HasCorePending(InvoiceRecord record) =>
    record.ExtensionData is not null &&
    record.ExtensionData.TryGetValue("cyinvoice_void_pending", out var value) &&
    value.ValueKind == JsonValueKind.True;

static QueryResponse Query(
    int status = UploadStatuses.Complete,
    long cancelDate = 0,
    bool voidPending = false) => new(
        0,
        "",
        new QueryResult(
            InvoiceNumber: "AA12345678",
            InvoiceType: "A0401",
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

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
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

static async Task<T> ThrowsWithResultAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T error) { return error; }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

sealed record TestSetup(
    LocalRepository Repository,
    FakeGateway Gateway,
    EmployeeVoidWorkflowService Workflow,
    InvoiceRecord Record);

sealed class FakeGateway : IAmegoGateway
{
    public Queue<QueryResponse> Queries { get; } = new();
    public VoidResponse VoidResponse { get; set; } = new(0, "OK");
    public VoidRequest? LastVoid { get; private set; }
    public int QueryCalls { get; private set; }
    public int StatusCalls { get; private set; }
    public int VoidCalls { get; private set; }
    public int TotalCalls => QueryCalls + StatusCalls + VoidCalls;

    public Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default)
    {
        VoidCalls++;
        LastVoid = new VoidRequest
        {
            CancelInvoiceNumber = request.CancelInvoiceNumber,
            CancelReason = request.CancelReason,
        };
        return Task.FromResult(VoidResponse);
    }

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        if (Queries.Count == 0) throw new InvalidOperationException("test query queue is empty");
        return Task.FromResult(Queries.Dequeue());
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        return Task.FromResult(new StatusResponse(0, "", []));
    }

    public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<IssueResponse>(new NotSupportedException());
    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
        Task.FromException<QueryResponse>(new NotSupportedException());
    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
        Task.FromException<BanResponse>(new NotSupportedException());
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
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-void-workflow-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
