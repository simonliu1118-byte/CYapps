using CYInvoice.Core;
using CYInvoice.Core.AutoInput;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Imports;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Digiwin;
using CYInvoice.Core.Imports.Mo;
using CYInvoice.Core.Storage;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var tests = new (string Name, Action Run)[]
{
    ("autoinput request accepts subset and rejects duplicate orders", () =>
    {
        var requestId = Guid.NewGuid().ToString("D");
        var file = Path.Combine(Path.GetTempPath(), "synthetic-orders.xls");
        var valid = JsonSerializer.Serialize(new {
            schema = AutoInputContract.RequestSchema, requestId, source = "mo", file,
            orderIds = new[] { "ORDER-A", "ORDER-B" }
        });
        var parsed = AutoInputContract.ParseRequest(valid);
        Equal("mo", parsed.Source);
        Equal(2, parsed.OrderIds!.Count);
        Throws<InvalidDataException>(() => AutoInputContract.ParseRequest(JsonSerializer.Serialize(new {
            schema = AutoInputContract.RequestSchema, requestId, source = "mo", file,
            orderIds = new[] { "ORDER-A", "ORDER-A" }
        })));
        Throws<InvalidDataException>(() => AutoInputContract.ParseRequest(JsonSerializer.Serialize(new {
            schema = "cyinvoice-autoinput-request/999", requestId, source = "mo", file
        })));
    }),
    ("autoinput PDF request validates style, folder and required order IDs", () =>
    {
        using var temp = new TemporaryDirectory();
        var request = new {
            schema = AutoInputContract.RequestSchema, requestId = Guid.NewGuid().ToString("D"),
            source = "mo", file = Path.Combine(temp.Path, "before.xls"),
            orderIds = new[] { "90000000000001" }, pdfFolder = temp.Path, pdfStyle = 5
        };
        var parsed = AutoInputContract.ParseRequest(JsonSerializer.Serialize(request));
        parsed.ValidatePdf();
        Equal(5, parsed.PdfStyle);
        Equal(temp.Path, parsed.PdfFolder);
        Throws<InvalidDataException>(() =>
            (parsed with { OrderIds = null }).ValidatePdf());
        Throws<InvalidDataException>(() =>
            (parsed with { PdfStyle = 4 }).ValidatePdf());
        Throws<InvalidDataException>(() =>
            AutoInputContract.ParseRequest(JsonSerializer.Serialize(new {
                request.schema, request.requestId, request.source, request.file,
                request.orderIds, request.pdfFolder, pdfStyle = 4 })));
        Equal(true, AutoInputCommandLine.IsAutoInputInvocation(["--autoinput-pdf"]));
        Equal(true, AutoInputCommandLine.IsAutoInputInvocation(["--autoinput-unsupported"]));
        Equal(true, AutoInputCommandLine.Parse([
            "--autoinput-pdf", request.file, "--result", Path.Combine(temp.Path,"out.json")]).Pdf);
        Throws<InvalidDataException>(() => AutoInputCommandLine.Parse([
            "--autoinput-invalid", request.file, "--result", Path.Combine(temp.Path,"out.json")]));
    }),
    ("autoinput PDF never issues and safely returns ready, pending or skipped", () =>
        TestAutoInputPdfAsync().GetAwaiter().GetResult()),
    ("autoinput blocks prior unresolved and mismatched invoice", () =>
    {
        var record = new InvoiceRecord {
            Source = InvoiceSources.Mo, OriginalOrderId = "ORDER-A", OrderId = "ORDER-A",
            Environment = Environments.Production, InvoiceState = InvoiceStates.Opened,
            InvoiceNumber = "AA12345678", InvoiceDate = "2026/10/14", Amount = 725
        };
        Equal("existing", AutoInputRecordDecision.Classify(
            [record], InvoiceSources.Mo, "ORDER-A", 725, "", Environments.Production)!.Status);
        Equal("unknown", AutoInputRecordDecision.Classify(
            [record], InvoiceSources.Mo, "ORDER-A", 700, "", Environments.Production)!.Status);
        record.InvoiceState = InvoiceStates.Unknown;
        Equal("unknown", AutoInputRecordDecision.Classify(
            [record], InvoiceSources.Mo, "ORDER-A", 725, "", Environments.Production)!.Status);
        record.InvoiceState = InvoiceStates.Failed;
        Equal("failed", AutoInputRecordDecision.Classify(
            [record], InvoiceSources.Mo, "ORDER-A", 725, "", Environments.Production)!.Status);
        Equal("unknown", AutoInputRecordDecision.Classify(
            [record, record], InvoiceSources.Mo, "ORDER-A", 725, "", Environments.Production)!.Status);
        Equal<AutoInputOrderResult?>(null, AutoInputRecordDecision.Classify(
            [record], InvoiceSources.Mo, "ORDER-Z", 725, "", Environments.Production));
    }),
    ("autoinput serialized issue replay guard prevents duplicate AMEGO call", () =>
    {
        using var temp = new TemporaryDirectory();
        const string id = "MO-AUTOINPUT-SAFE";
        var fake = new FakeGateway {
            IssueResponse = new IssueResponse(0, "", "AB12345678", 0, ""),
            QueryResponse = ConfirmedQuery(TestApiOrderId(id), "AB12345678", 105, 0, 105, 1)
        };
        var (service, _) = TestService(temp.Path, fake);
        MoOrder Order()
        {
            var order = new MoOrder { OrderId = id, TotalAmount = 105 };
            order.Items.Add(new InvoiceItem { Description = "測試品項", Quantity = 1, UnitPrice = 105, Amount = 105 });
            return order;
        }
        service.IssueMoAsync(Order(), replaySafe: true, expectedEnvironment: Environments.Test)
            .GetAwaiter().GetResult();
        Throws<InvalidOperationException>(() => service.IssueMoAsync(
            Order(), replaySafe: true, expectedEnvironment: Environments.Test)
            .GetAwaiter().GetResult());
        Equal(1, fake.IssueCalls);
    }),
    ("autoinput refuses environment switch before issuing", () =>
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeGateway();
        var (service, _) = TestService(temp.Path, fake);
        var order = new MoOrder { OrderId = "MO-ENV-BLOCK", TotalAmount = 105 };
        order.Items.Add(new InvoiceItem { Description = "測試品項", Quantity = 1, UnitPrice = 105, Amount = 105 });
        Throws<InvalidOperationException>(() => service.IssueMoAsync(
            order, replaySafe: true, expectedEnvironment: Environments.Production)
            .GetAwaiter().GetResult());
        Equal(0, fake.IssueCalls);
    }),
    ("autoinput query strictly reconciles unknown invoice without issuing", () =>
    {
        using var temp = new TemporaryDirectory();
        const string id = "MO-AI-QUERY";
        var response = ConfirmedQuery(TestApiOrderId(id), "AB87654321", 105, 0, 105, 1);
        var fake = new FakeGateway { QueryResponse = response with {
            Data = response.Data with { InvoiceDate = "2026/10/14" }
        } };
        var (service, repository) = TestService(temp.Path, fake);
        repository.Invoices.Append(new InvoiceRecord {
            Id = "autoinput-query-test", Source = InvoiceSources.Mo,
            OriginalOrderId = id, OrderId = id, ApiOrderId = TestApiOrderId(id),
            Environment = Environments.Test, InvoiceState = InvoiceStates.Unknown,
            Amount = 105, BuyerIdentifier = "0000000000",
            Items = [new InvoiceItem { Description = "測試品項", Quantity = 1, UnitPrice = 105, Amount = 105 }],
            DetailVat = 1
        });
        var updated = service.ReconcileForAutoInputAsync(
            "autoinput-query-test", Environments.Test).GetAwaiter().GetResult();
        Equal(InvoiceStates.Opened, updated.InvoiceState);
        Equal("AB87654321", updated.InvoiceNumber);
        Equal("existing", AutoInputRecordDecision.Classify(
            [updated], InvoiceSources.Mo, id, 105, "", Environments.Test)!.Status);
        Equal(0, fake.IssueCalls);
        Equal(1, fake.QueryCalls);
    }),
    ("autoinput result is atomic and cannot replace an existing file", () =>
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "result.json");
        var envelope = new AutoInputResult(AutoInputContract.ResultSchema, Guid.NewGuid().ToString("D"),
            "2.6.19+0", "test", "2026-10-14T10:00:00Z",
            [new AutoInputOrderResult("ORDER-A", "unknown", Message: "synthetic")]);
        AutoInputResultFile.WriteAtomicAsync(path, envelope).GetAwaiter().GetResult();
        using var output = JsonDocument.Parse(File.ReadAllText(path));
        Equal("unknown", output.RootElement.GetProperty("orders")[0].GetProperty("status").GetString());
        Throws<IOException>(() => AutoInputResultFile.WriteAtomicAsync(path, envelope)
            .GetAwaiter().GetResult());
        Equal(0, Directory.GetFiles(temp.Path, "*.tmp").Length);
    }),
    ("seven-place inclusive round trip", () =>
    {
        var inclusive = FixedDecimal.Parse("200");
        var untaxed = FixedDecimal.MultiplyRatio(inclusive, 20, 21);
        Equal("190.4761905", untaxed.ToString());
        Equal("200", FixedDecimal.MultiplyRatio(untaxed, 21, 20).ToString());
    }),
    ("reject eighth decimal place", () => Throws<FormatException>(() => FixedDecimal.Parse("1.12345678"))),
    ("multiply and half-away-from-zero rounding", () =>
    {
        var amount = FixedDecimal.Multiply(FixedDecimal.Parse("3"), FixedDecimal.Parse("33.3333333"));
        Equal("99.9999999", amount.ToString());
        Equal(100L, amount.RoundInt64());
        Equal(-2L, FixedDecimal.Parse("-1.5").RoundInt64());
    }),
    ("money thousands separators", () =>
    {
        Equal("1,234,567,890", MoneyFormatter.Integer(1_234_567_890));
        Equal("12,345.6700000", MoneyFormatter.Decimal("12345.6700000"));
        Equal("not-a-number", MoneyFormatter.Decimal("not-a-number"));
    }),
    ("manual order ID uses M prefix and continues today's sequence", () =>
    {
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(8));
        var records = new[]
        {
            new InvoiceRecord { OrderId = "M20260913001" },
            new InvoiceRecord { OrderId = "M20260913007" },
            new InvoiceRecord { OrderId = "20260913099" },
            new InvoiceRecord { OrderId = "M20260913ABC" },
        };
        Equal("M20260913008", ManualOrderId.Next(now, records));
    }),
    ("invoice total validation", () =>
    {
        var draft = ValidDraft();
        InvoiceValidator.Validate(draft);
        draft.TotalAmount = 99;
        Throws<InvalidOperationException>(() => InvoiceValidator.Validate(draft));
    }),
    ("AMEGO form and signature", () => TestAmegoSignatureAsync().GetAwaiter().GetResult()),
    ("AMEGO code 15 synchronizes time once", () => TestAmegoTimeSyncAsync().GetAwaiter().GetResult()),
    ("AMEGO string code 15 uses the same time sync", () => TestAmegoTimeSyncAsync("\"15\"").GetAwaiter().GetResult()),
    ("AMEGO numeric and string duplicate errors preserve the failed second attempt", () => TestWireDuplicateRejectionAsync().GetAwaiter().GetResult()),
    ("AMEGO envelope requires an explicit integer code", () => TestInvalidEnvelopeCodeAsync().GetAwaiter().GetResult()),
    ("AMEGO query selects exact order", () => TestAmegoExactQueryAsync().GetAwaiter().GetResult()),
    ("AMEGO invoice file downloads only trusted PDF", () => TestAmegoInvoicePdfAsync().GetAwaiter().GetResult()),
    ("AMEGO invoice file rejects untrusted URL", () => TestAmegoInvoicePdfUrlSafetyAsync().GetAwaiter().GetResult()),
    ("settings never write plaintext secrets", TestSettingsSecrets),
    ("corrupt invoice JSON is not overwritten", TestCorruptInvoiceJson),
    ("legacy invoice migration preserves unknown fields", TestLegacyInvoiceMigration),
    ("status update keeps immutable sent time", TestStatusUpdateKeepsSentTime),
    ("buyer name is remembered only after successful invoice", TestBuyerNameMemory),
    ("negative discount line remains valid", TestNegativeDiscountLine),
    ("untaxed company totals use official one-twentieth tax", TestUntaxedCompanyTotals),
    ("opened order resends original ID and lets AMEGO reject duplicate", () => TestIssueOpensAndBlocksResendAsync().GetAwaiter().GetResult()),
    ("recovery cannot count the previous invoice as a new successful attempt", () => TestRecoveryOfExistingInvoiceAsync().GetAwaiter().GetResult()),
    ("unknown result retries only after query confirms not found", () => TestAmbiguousIssueAsync().GetAwaiter().GetResult()),
    ("unknown retry stops when preflight query is unavailable", () => TestUnknownRetryPreflightFailureAsync().GetAwaiter().GetResult()),
    ("ambiguous issue recovers only from strict order query", () => TestAmbiguousRecoveryAsync().GetAwaiter().GetResult()),
    ("request timeout still performs one bounded recovery query", () => TestCancelledIssueRecoveryAsync().GetAwaiter().GetResult()),
    ("wrong order and voided query never establish success", () => TestStrictRecoveryRejectsMismatchAsync().GetAwaiter().GetResult()),
    ("concurrent same OrderID is blocked before second API call", () => TestConcurrentDuplicateIssueAsync().GetAwaiter().GetResult()),
    ("same order is isolated between test and production", () => TestEnvironmentIssueIsolationAsync().GetAwaiter().GetResult()),
    ("explicit API rejection becomes failed", () => TestExplicitRejectionAsync().GetAwaiter().GetResult()),
    ("voided test reissue keeps local order ID and applies API prefix", () => TestVoidedReissueAsync().GetAwaiter().GetResult()),
    ("remote success and local save failure remain opened", () => TestRemoteSuccessLocalFailureAsync().GetAwaiter().GetResult()),
    ("successful manual company invoice remembers confirmed name", () => TestCompanyNameMemoryAsync().GetAwaiter().GetResult()),
    ("successful manual company correction overwrites local name", () => TestLocalCompanyNameCorrectionAsync().GetAwaiter().GetResult()),
    ("refresh confirms unknown invoice before remembering name", () => TestRefreshUnknownAsync().GetAwaiter().GetResult()),
    ("refresh matches upload status by exact invoice number", () => TestUploadStatusMatchingAsync().GetAwaiter().GetResult()),
    ("refresh rejects unmatched upload status", () => TestUploadStatusMismatchAsync().GetAwaiter().GetResult()),
    ("BAN code 99 is reachable with no matching name", () => TestBanCode99Async().GetAwaiter().GetResult()),
    ("production health check returns the API company name", () => TestProductionHealthCompanyNameAsync().GetAwaiter().GetResult()),
    ("decimal issue fields serialize as JSON numbers", () => TestDecimalIssueNumbersAsync().GetAwaiter().GetResult()),
    ("success without invoice number is unknown", () => TestMissingInvoiceNumberAsync().GetAwaiter().GetResult()),
    ("production uses configured credentials without legacy admin password", () => TestProductionWithoutLegacyAdminPasswordAsync().GetAwaiter().GetResult()),
    ("xlsx resolves the first logical worksheet", TestXlsxRelationship),
    ("xlsx reads an exact named worksheet", TestXlsxNamedWorksheet),
    ("xlsx column references continue after Z", TestXlsxColumns),
    ("Excel display artifacts fall back to stable raw values", TestSpreadsheetCellValues),
    ("Coupang import uses headers and groups order items", TestCoupangGrouping),
    ("Coupang import keeps seven-place fractional unit price", TestCoupangFractionalUnitPrice),
    ("Digiwin import preserves negative detail and original company reference", TestDigiwinNegativeDiscount),
    ("Digiwin consumer clears source buyer name", TestDigiwinConsumerClearsBuyerName),
    ("Digiwin import rejects detail total mismatch", TestDigiwinTotalMismatch),
    ("MO converted workbook is rejected at the import boundary", TestMoConvertedRejected),
    ("MO raw export uses official invoice amounts", TestMoRawOfficialAmounts),
    ("MO official amounts reject scientific notation", TestMoScientificAmountRejected),
    ("MO raw export allows official rounded subtotal", TestMoRawRoundedSubtotal),
    ("MO raw export adds subsidy only when total requires it", TestMoRawConditionalSubsidy),
    ("MO raw export preserves specifications and member carrier", TestMoRawSpecifications),
    ("MO issue masks shared test-pool customer data", () => TestMoIssuePrivacyAsync().GetAwaiter().GetResult()),
    ("Coupang confirmation is reused and company test privacy uses AMEGO buyer", () => TestCoupangIssuePrivacyAsync().GetAwaiter().GetResult()),
    ("manual and all imports send BAN for empty name after successful no-result lookup", () => TestMissingCompanyNameAsync().GetAwaiter().GetResult()),
    ("uncertain BAN fallback does not become manual memory after recovery", () => TestMissingCompanyNameRecoveryAsync().GetAwaiter().GetResult()),
    ("missing lookup and lookup service failure never use BAN fallback", () => TestMissingCompanyNameRefusalAsync().GetAwaiter().GetResult()),
    ("unresolved imported company name blocks before API", () => TestImportedLookupBlockAsync().GetAwaiter().GetResult()),
    ("consumer PDF uses style zero and same-day cache", () => TestConsumerPdfCacheAsync().GetAwaiter().GetResult()),
    ("company PDF caches each style and refreshes next day", () => TestCompanyPdfStylesAsync().GetAwaiter().GetResult()),
    ("PDF preview cache is isolated and validates PNG", () => TestInvoicePreviewCacheAsync().GetAwaiter().GetResult()),
    ("opened PDF is attempted before upload completes", () => TestPendingUploadPdfAttemptAsync().GetAwaiter().GetResult()),
    ("PDF eligibility blocks unsafe invoice states", TestPdfEligibility),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} parity tests passed");
return failures == 0 ? 0 : 1;

static InvoiceDraft ValidDraft()
{
    var draft = new InvoiceDraft { OrderId = " 20260905001 ", TotalAmount = 100 };
    draft.Items.Add(new InvoiceItem
    {
        Description = "商品",
        Quantity = 2,
        QuantityDecimal = "2",
        UnitPrice = 50,
        UnitPriceDecimal = "50",
        Amount = 100,
        AmountDecimal = "100",
    });
    return draft;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

static T ThrowsWithResult<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T error)
    {
        return error;
    }
    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T error)
    {
        return error;
    }

    throw new InvalidOperationException($"expected {typeof(T).Name}");
}

static async Task<(IssueResult? Result, Exception? Error)> CaptureIssueAsync(Task<IssueResult> task)
{
    try
    {
        return (await task, null);
    }
    catch (Exception error)
    {
        return (null, error);
    }
}

static IssueRequest MinimalIssue() => new()
{
    OrderId = "ORDER-1",
    BuyerIdentifier = "0000000000",
    BuyerName = "客人",
    ProductItems = [new ProductItem { Description = "商品", Quantity = 1, UnitPrice = 100, Amount = 100, TaxType = 1 }],
    SalesAmount = 100,
    TaxType = 1,
    TaxAmount = 0,
    TotalAmount = 100,
};

static async Task TestAmegoSignatureAsync()
{
    var fixedTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    var handler = new StubHandler(async request =>
    {
        Equal("application/x-www-form-urlencoded", request.Content?.Headers.ContentType?.MediaType);
        var form = ParseForm(await request.Content!.ReadAsStringAsync());
#pragma warning disable CA5351
        var expected = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(form["data"] + "1700000000" + "secret"))).ToLowerInvariant();
#pragma warning restore CA5351
        Equal(expected, form["sign"]);
        Equal("12345678", form["invoice"]);
        return JsonResponse("{\"code\":0,\"msg\":\"\",\"invoice_number\":\"AB12345678\",\"invoice_time\":1700000000,\"random_number\":\"1234\"}");
    });
    var client = new AmegoClient("12345678", "secret", new HttpClient(handler), () => fixedTime) { BaseUrl = "https://test.invalid" };
    var response = await client.IssueAsync(MinimalIssue());
    Equal("AB12345678", response.InvoiceNumber);
}

static async Task TestAmegoTimeSyncAsync(string timeCode = "15")
{
    var fixedTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    var serverTime = fixedTime.AddMinutes(5).ToUnixTimeSeconds();
    var posts = 0;
    var clocks = 0;
    var handler = new StubHandler(async request =>
    {
        if (request.Method == HttpMethod.Get)
        {
            clocks++;
            return JsonResponse($"{{\"timestamp\":{serverTime}}}");
        }
        posts++;
        var form = ParseForm(await request.Content!.ReadAsStringAsync());
        if (posts == 1)
        {
            Equal("1700000000", form["time"]);
            return JsonResponse($"{{\"code\":{timeCode},\"msg\":\"time error\"}}");
        }
        Equal(serverTime.ToString(), form["time"]);
        return JsonResponse("{\"code\":0,\"msg\":\"\",\"data\":[{\"ban\":\"12345678\",\"name\":\"光貿測試公司\"}]}");
    });
    var client = new AmegoClient("12345678", "secret", new HttpClient(handler), () => fixedTime) { BaseUrl = "https://test.invalid" };
    var result = await client.QueryBanAsync(["12345678"]);
    Equal("光貿測試公司", result.Data[0].Name);
    Equal(2, posts);
    Equal(1, clocks);
}

static async Task TestWireDuplicateRejectionAsync()
{
    foreach (var code in new[] { "3040171", "\"3040171\"" })
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var posts = 0;
        var queries = 0;
        var apiOrderId = TestApiOrderId("20260905001");
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/json/f0401")
            {
                posts++;
                return Task.FromResult(JsonResponse(posts == 1
                    ? "{\"code\":0,\"msg\":\"\",\"invoice_number\":\"AB12345678\"}"
                    : $"{{\"code\":{code},\"msg\":\"OrderId duplicate\",\"invoice_number\":\"AB12345678\"}}"));
            }
            Equal("/json/invoice_query", request.RequestUri.AbsolutePath);
            queries++;
            return Task.FromResult(JsonResponse($"{{\"code\":0,\"data\":{{\"invoice_number\":\"AB12345678\",\"order_id\":\"{apiOrderId}\",\"sales_amount\":105,\"tax_amount\":0,\"total_amount\":105,\"detail_vat\":1}}}}"));
        });
        using var http = new HttpClient(handler);
        var client = new AmegoClient(AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey, http) { BaseUrl = "https://test.invalid" };
        var service = new InvoiceService(repository, (_, _) => client,
            () => new DateTimeOffset(2026, 9, 5, 9, 8, 7, TimeSpan.FromHours(8)));
        var first = await service.IssueManualAsync(SafeDraft());
        var rejection = await ThrowsAsync<AmegoApiException>(() => service.IssueManualAsync(SafeDraft()));
        Equal(3040171, rejection.Code);
        Equal(2, posts);
        Equal(1, queries); // A definite rejection must not recover the previous invoice.
        var records = repository.Invoices.LoadOrCreate();
        Equal(2, records.Count);
        Equal(InvoiceStates.Opened, records.Single(record => record.Id == first.Record.Id).InvoiceState);
        var failed = records.Single(record => record.Id != first.Record.Id);
        Equal(InvoiceStates.Failed, failed.InvoiceState);
        Equal(string.Empty, failed.InvoiceNumber);
        Equal(0, failed.UploadStatus);
        Equal(true, failed.ErrorMessage.Contains("3040171", StringComparison.Ordinal));
    }
}

static async Task TestInvalidEnvelopeCodeAsync()
{
    foreach (var code in new[] { "", "\"code\":null,", "\"code\":true,", "\"code\":0.5,", "\"code\":\"invalid\",", "\"code\":2147483648," })
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(
            JsonResponse("{" + code + "\"invoice_number\":\"AB12345678\"}"))));
        var client = new AmegoClient(AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey, http) { BaseUrl = "https://test.invalid" };
        await ThrowsAsync<InvalidDataException>(() => client.IssueAsync(MinimalIssue()));
    }
    using var successHttp = new HttpClient(new StubHandler(_ => Task.FromResult(
        JsonResponse("{\"code\":\"0\",\"invoice_number\":\"AB12345678\"}"))));
    var successClient = new AmegoClient(AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey, successHttp) { BaseUrl = "https://test.invalid" };
    Equal("AB12345678", (await successClient.IssueAsync(MinimalIssue())).InvoiceNumber);
}

static async Task TestAmegoExactQueryAsync()
{
    var handler = new StubHandler(_ => Task.FromResult(JsonResponse("{\"code\":0,\"msg\":\"\",\"data\":[{\"invoice_number\":\"AA12345678\",\"order_id\":\"OTHER\"},{\"invoice_number\":\"BB12345678\",\"order_id\":\"ORDER-1\",\"sales_amount\":100,\"tax_amount\":5,\"total_amount\":105,\"detail_vat\":1}]}")));
    var client = new AmegoClient("12345678", "secret", new HttpClient(handler)) { BaseUrl = "https://test.invalid" };
    var response = await client.QueryByOrderIdAsync("ORDER-1");
    Equal("BB12345678", response.Data.InvoiceNumber);
    Equal(true, response.Data.DetailVatPresent);
    Equal(1, response.Data.DetailVat);
}

static async Task TestAmegoInvoicePdfAsync()
{
    var requests = 0;
    var handler = new StubHandler(async request =>
    {
        requests++;
        if (request.Method == HttpMethod.Post)
        {
            Equal("/json/invoice_file", request.RequestUri!.AbsolutePath);
            var form = ParseForm(await request.Content!.ReadAsStringAsync());
            using var data = JsonDocument.Parse(form["data"]);
            Equal("invoice", data.RootElement.GetProperty("type").GetString());
            Equal("AB12345678", data.RootElement.GetProperty("invoice_number").GetString());
            Equal(5, data.RootElement.GetProperty("download_style").GetInt32());
            return JsonResponse("{\"code\":0,\"msg\":\"\",\"data\":{\"file_url\":\"https://invoice.amego.tw/user/invoice_print_type?token=TEST-NOT-REAL&type=5\"}}");
        }
        Equal("invoice.amego.tw", request.RequestUri!.Host);
        return PdfResponse(TestPdfBytes());
    });
    var client = new AmegoClient("12345678", "secret", new HttpClient(handler)) { BaseUrl = "https://test.invalid" };
    var pdf = await client.DownloadInvoicePdfAsync("AB12345678", 5);
    Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    Equal(2, requests);
}

static async Task TestAmegoInvoicePdfUrlSafetyAsync()
{
    var requests = 0;
    var handler = new StubHandler(_ =>
    {
        requests++;
        return Task.FromResult(JsonResponse("{\"code\":0,\"msg\":\"\",\"data\":{\"file_url\":\"https://example.invalid/invoice.pdf\"}}"));
    });
    var client = new AmegoClient("12345678", "secret", new HttpClient(handler)) { BaseUrl = "https://test.invalid" };
    await ThrowsAsync<InvalidDataException>(() => client.DownloadInvoicePdfAsync("AB12345678", 0));
    Equal(1, requests);
}

static Dictionary<string, string> ParseForm(string value) => value.Split('&').Select(part => part.Split('=', 2))
    .ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part.Length == 2 ? part[1] : string.Empty));

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json"),
};

static HttpResponseMessage PdfResponse(byte[] bytes) => new(HttpStatusCode.OK)
{
    Content = new ByteArrayContent(bytes),
};

static byte[] TestPdfBytes() => Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");

static void TestSettingsSecrets()
{
    using var temporary = new TemporaryDirectory();
    var store = new SettingsStore(temporary.Path, new TestProtector());
    var settings = store.LoadOrCreate();
    store.SetMoPassword(settings, "TEST-MO-PASSWORD-NOT-REAL");
    store.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
    settings.ProductionInvoice = "12345675";
    settings.Environment = Environments.Production;
    store.Save(settings);
    var json = File.ReadAllText(System.IO.Path.Combine(temporary.Path, "settings.json"));
    foreach (var secret in new[] { "TEST-MO-PASSWORD-NOT-REAL", "TEST-APP-KEY-NOT-REAL" })
        if (json.Contains(secret, StringComparison.Ordinal)) throw new InvalidOperationException("plaintext secret was stored");
    Equal("TEST-MO-PASSWORD-NOT-REAL", store.MoPassword(settings));
    Equal("TEST-APP-KEY-NOT-REAL", store.ProductionAppKey(settings));
}

static void TestCorruptInvoiceJson()
{
    using var temporary = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temporary.Path, "invoices.json");
    const string corrupt = "{not-json";
    File.WriteAllText(path, corrupt);
    Throws<InvalidDataException>(() => new InvoiceStore(temporary.Path).LoadOrCreate());
    Equal(corrupt, File.ReadAllText(path));
}

static void TestLegacyInvoiceMigration()
{
    using var temporary = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temporary.Path, "invoices.json");
    File.WriteAllText(path, "[{\"id\":\"legacy\",\"order_id\":\"old\",\"amount\":1,\"invoice_state\":\"已開立\",\"invoice_date\":\"2026/09/04\",\"invoice_time\":\"12:34:56\",\"legacy_field\":\"keep-me\"}]");
    var records = new InvoiceStore(temporary.Path).LoadOrCreate();
    Equal("2026/09/04 12:34:56", records[0].SentAt);
    var migrated = File.ReadAllText(path);
    if (!migrated.Contains("\"legacy_field\": \"keep-me\"", StringComparison.Ordinal)) throw new InvalidOperationException("unknown field was discarded");
}

static void TestStatusUpdateKeepsSentTime()
{
    using var temporary = new TemporaryDirectory();
    var store = new InvoiceStore(temporary.Path);
    store.Append(new InvoiceRecord { Id = "record-1", OrderId = "20260905001", Amount = 100, InvoiceState = InvoiceStates.Opened, SentAt = "2026/09/05 07:10:00" });
    store.UpdateStatus("record-1", new StatusUpdate("AB12345678", InvoiceStates.Opened, 99, "完成", "", "2026/09/05", "07:11:00", "2026/09/05 07:12:00"));
    var record = store.LoadOrCreate()[0];
    Equal("2026/09/05 07:10:00", record.SentAt);
    Equal("07:11:00", record.InvoiceTime);
}

static void TestBuyerNameMemory()
{
    using var temporary = new TemporaryDirectory();
    var store = new BuyerNameStore(temporary.Path);
    store.LoadOrCreate();
    Equal(false, store.RememberAfterSuccessfulInvoice("12345675", true, "API 公司名稱", "人工名稱", true));
    Equal(false, store.RememberAfterSuccessfulInvoice("12345675", true, "", "人工名稱", false));
    Equal(true, store.RememberAfterSuccessfulInvoice("12345675", true, "", "人工名稱", true));
    Equal(true, store.TryLookup("12345675", out var name));
    Equal("人工名稱", name);
}

static void TestNegativeDiscountLine()
{
    var draft = new InvoiceDraft { OrderId = "20260905001", TotalAmount = 105 };
    draft.Items.Add(new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 150, Amount = 150 });
    draft.Items.Add(new InvoiceItem { Description = "折扣", Quantity = 1, UnitPrice = -45, Amount = -45 });
    InvoiceValidator.Validate(draft);
}

static void TestUntaxedCompanyTotals()
{
    var items = new[]
    {
        new InvoiceItem
        {
            Description = "商品", Quantity = 1, QuantityDecimal = "1",
            UnitPrice = 190, UnitPriceDecimal = "190.4761905",
            Amount = 190, AmountDecimal = "190.4761905",
        },
    };
    var totals = InvoiceCalculator.CalculateTotals(items, companyBuyer: true, pricesExcludeTax: true);
    Equal(190L, totals.SalesAmount);
    Equal(10L, totals.TaxAmount);
    Equal(200L, totals.TotalAmount);
}

static async Task TestIssueOpensAndBlocksResendAsync()
{
    using var temporary = new TemporaryDirectory();
    var apiOrderId = TestApiOrderId("20260905001");
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "AB12345678", 0, ""),
        QueryResponse = ConfirmedQuery(apiOrderId, "AB12345678", 105, 0, 105, 1),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    fake.OnIssue = _ => Equal(InvoiceStates.Changing, repository.Invoices.LoadOrCreate().Last().InvoiceState);
    var result = await service.IssueManualAsync(SafeDraft());
    Equal(true, result.Opened);
    Equal(InvoiceStates.Opened, result.Record.InvoiceState);
    Equal("AB12345678", result.Record.InvoiceNumber);
    Equal(1, fake.IssueCalls);
    Equal(apiOrderId, fake.LastIssue!.OrderId);
    Equal("0000000000", fake.LastIssue.BuyerIdentifier);
    Equal("測試消費者", fake.LastIssue.BuyerName);
    Equal(105L, Convert.ToInt64(fake.LastIssue.SalesAmount));
    Equal(0L, Convert.ToInt64(fake.LastIssue.TaxAmount));

    fake.IssueException = new AmegoApiException(3040171, "OrderId duplicate");
    var duplicate = await ThrowsAsync<AmegoApiException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(3040171, duplicate.Code);
    Equal(2, fake.IssueCalls);
    var records = repository.Invoices.LoadOrCreate();
    Equal(2, records.Count);
    Equal(1, records.Count(record => record.InvoiceState == InvoiceStates.Opened));
    Equal(1, records.Count(record => record.InvoiceState == InvoiceStates.Failed));
    Equal(true, records.All(record => record.ApiOrderId == apiOrderId));
}

static async Task TestRecoveryOfExistingInvoiceAsync()
{
    foreach (var transportFailure in new[] { true, false })
    {
        using var temporary = new TemporaryDirectory();
        var fake = new FakeGateway
        {
            IssueResponse = new IssueResponse(0, "", "AB12345678", 0, ""),
            QueryResponse = ConfirmedQuery(TestApiOrderId("20260905001"), "AB12345678", 105, 0, 105, 1),
        };
        var (service, repository) = TestService(temporary.Path, fake);
        var original = await service.IssueManualAsync(SafeDraft());
        if (transportFailure) fake.IssueException = new IOException("decode API response");
        var error = await ThrowsAsync<InvalidOperationException>(() => service.IssueManualAsync(SafeDraft()));
        Equal(true, error.Message.Contains("訂單編號重複", StringComparison.Ordinal));
        Equal(2, fake.IssueCalls); // AMEGO remains responsible for the issue decision.
        var records = repository.Invoices.LoadOrCreate();
        Equal(2, records.Count);
        Equal(InvoiceStates.Opened, records.Single(record => record.Id == original.Record.Id).InvoiceState);
        var failed = records.Single(record => record.Id != original.Record.Id);
        Equal(InvoiceStates.Failed, failed.InvoiceState);
        Equal(string.Empty, failed.InvoiceNumber);
        Equal(0, failed.UploadStatus);
        Equal(true, failed.ErrorMessage.Contains("訂單編號重複", StringComparison.Ordinal));
        await service.RefreshAllAsync();
        Equal(InvoiceStates.Failed, repository.Invoices.LoadOrCreate().Single(record => record.Id == failed.Id).InvoiceState);
    }
}

static async Task TestAmbiguousIssueAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueException = new IOException("connection reset"),
        QueryException = new AmegoApiException(71, "not found"),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    var first = await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(InvoiceStates.Unknown, first.Record.InvoiceState);
    Equal(1, fake.IssueCalls);

    var second = await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(InvoiceStates.Unknown, second.Record.InvoiceState);
    Equal(2, fake.IssueCalls);
    Equal(2, repository.Invoices.LoadOrCreate().Count(record => record.InvoiceState == InvoiceStates.Unknown));
    Equal(3, fake.QueryCalls);
}

static async Task TestUnknownRetryPreflightFailureAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueException = new IOException("connection reset"),
        QueryException = new AmegoApiException(71, "not found"),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(1, fake.IssueCalls);
    Equal(1, repository.Invoices.LoadOrCreate().Count);

    fake.QueryException = new IOException("query unavailable");
    var retry = await ThrowsAsync<InvalidOperationException>(() => service.IssueManualAsync(SafeDraft()));
    if (!retry.Message.Contains("本次未重送", StringComparison.Ordinal))
        throw new InvalidOperationException("uncertain retry did not report that it was not resent");
    Equal(1, fake.IssueCalls);
    Equal(2, fake.QueryCalls);
    Equal(1, repository.Invoices.LoadOrCreate().Count);
}

static async Task TestAmbiguousRecoveryAsync()
{
    using var temporary = new TemporaryDirectory();
    var apiOrderId = TestApiOrderId("20260905001");
    var fake = new FakeGateway
    {
        IssueException = new IOException("decode failed"),
        QueryResponse = ConfirmedQuery(apiOrderId, "CD87654321", 105, 0, 105, 1),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var result = await service.IssueManualAsync(SafeDraft());
    Equal(true, result.Opened);
    Equal("CD87654321", result.Record.InvoiceNumber);
    Equal(apiOrderId, fake.LastQueryOrderId);
}

static async Task TestCancelledIssueRecoveryAsync()
{
    using var temporary = new TemporaryDirectory();
    using var timeout = new CancellationTokenSource();
    var apiOrderId = TestApiOrderId("20260905001");
    var fake = new FakeGateway
    {
        IssueException = new OperationCanceledException("request timed out"),
        QueryResponse = ConfirmedQuery(apiOrderId, "EF87654321", 105, 0, 105, 1),
        RejectCancelledQueryToken = true,
        OnIssue = _ => timeout.Cancel(),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var result = await service.IssueManualAsync(SafeDraft(), timeout.Token);
    Equal(true, result.Opened);
    Equal(1, fake.QueryCalls);
    Equal(apiOrderId, fake.LastQueryOrderId);
    Equal(false, fake.LastQueryTokenWasCancelled);
}

static async Task TestStrictRecoveryRejectsMismatchAsync()
{
    foreach (var query in new[]
    {
        ConfirmedQuery("ANOTHER-ORDER", "GH12345678", 105, 0, 105, 1),
        ConfirmedQuery(TestApiOrderId("20260905001"), "GH12345678", 105, 0, 105, 1, cancelDate: 1),
    })
    {
        using var temporary = new TemporaryDirectory();
        var fake = new FakeGateway { IssueException = new IOException("connection reset"), QueryResponse = query };
        var (service, _) = TestService(temporary.Path, fake);
        var error = await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualAsync(SafeDraft()));
        Equal(InvoiceStates.Unknown, error.Record.InvoiceState);
    }
}

static async Task TestConcurrentDuplicateIssueAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "IJ12345678", 0, ""),
        QueryResponse = ConfirmedQuery(TestApiOrderId("20260905001"), "IJ12345678", 105, 0, 105, 1),
        IssueStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        IssueRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var first = CaptureIssueAsync(service.IssueManualAsync(SafeDraft()));
    await fake.IssueStarted!.Task;
    var second = CaptureIssueAsync(service.IssueManualAsync(SafeDraft()));
    await Task.Yield();
    Equal(1, fake.IssueCalls);
    fake.IssueRelease!.SetResult(true);
    var outcomes = await Task.WhenAll(first, second);
    Equal(1, outcomes.Count(outcome => outcome.Result?.Opened == true));
    Equal(1, outcomes.Count(outcome => outcome.Error is InvalidOperationException));
    Equal(1, fake.IssueCalls);
}

static async Task TestEnvironmentIssueIsolationAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = TestRepository.Open(temporary.Path, new TestProtector());
    var gateways = new List<(string Invoice, string AppKey, FakeGateway Gateway)>();
    var service = new InvoiceService(
        repository,
        (invoice, appKey) =>
        {
            var number = invoice == AmegoDefaults.TestInvoice ? "KL12345678" : "MN12345678";
            var gateway = new FakeGateway
            {
                IssueResponse = new IssueResponse(0, "", number, 0, ""),
                QueryResponse = ConfirmedQuery("20260905001", number, 105, 0, 105, 1),
            };
            gateways.Add((invoice, appKey, gateway));
            return gateway;
        },
        () => new DateTimeOffset(2026, 9, 5, 9, 8, 7, TimeSpan.FromHours(8)));

    await service.IssueManualAsync(SafeDraft());
    var settings = repository.Settings.LoadOrCreate();
    settings.Environment = Environments.Production;
    settings.ProductionInvoice = "12345675";
    repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
    repository.Settings.Save(settings);
    await service.IssueManualAsync(SafeDraft());

    Equal(2, gateways.Count);
    Equal(AmegoDefaults.TestInvoice, gateways[0].Invoice);
    Equal("12345675", gateways[1].Invoice);
    Equal(TestApiOrderId("20260905001"), gateways[0].Gateway.LastIssue!.OrderId);
    Equal("20260905001", gateways[1].Gateway.LastIssue!.OrderId);
    var records = repository.Invoices.LoadOrCreate();
    Equal(1, records.Count(record => record.Environment == Environments.Test));
    Equal(1, records.Count(record => record.Environment == Environments.Production));
}

static async Task TestExplicitRejectionAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { IssueException = new AmegoApiException(101, "invalid") };
    var (service, repository) = TestService(temporary.Path, fake);
    await ThrowsAsync<AmegoApiException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(InvoiceStates.Failed, repository.Invoices.LoadOrCreate()[0].InvoiceState);
    Equal(0, fake.QueryCalls);
}

static async Task TestVoidedReissueAsync()
{
    using var temporary = new TemporaryDirectory();
    var apiOrderId = TestApiOrderId("20260905001");
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "JK12345678", 0, ""),
        QueryResponse = ConfirmedQuery(apiOrderId, "JK12345678", 105, 0, 105, 1),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    repository.Invoices.Append(new InvoiceRecord
    {
        Id = "old-attempt", Source = InvoiceSources.Manual,
        OriginalOrderId = "20260905001", OrderId = "20260905001", ApiOrderId = "20260905001",
        Attempt = 7, Amount = 105, InvoiceState = InvoiceStates.Voided, Environment = Environments.Test,
    });
    var result = await service.IssueManualAsync(SafeDraft());
    Equal("20260905001", result.Record.OrderId);
    Equal(apiOrderId, fake.LastIssue!.OrderId);
    Equal(apiOrderId, result.Record.ApiOrderId);
    Equal(1, result.Record.Attempt);
}

static async Task TestRemoteSuccessLocalFailureAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "LM12345678", 0, ""),
        QueryException = new IOException("query unavailable"),
    };
    var (service, _) = TestService(
        temporary.Path,
        fake,
        (_, _) => throw new IOException("disk full"));
    var error = await ThrowsAsync<LocalPersistenceException>(() => service.IssueManualAsync(SafeDraft()));
    Equal("LM12345678", error.InvoiceNumber);
    Equal(InvoiceStates.Opened, error.Record.InvoiceState);
    if (!error.Message.Contains("請勿直接重送", StringComparison.Ordinal))
        throw new InvalidOperationException("local failure did not warn against immediate resend");
}

static async Task TestCompanyNameMemoryAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "MO12345678", 0, ""),
        QueryResponse = ConfirmedQuery("20260905002", "MO12345678", 100, 5, 105, 1),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    var draft = CompanyDraft("20260905002", "人工醫院名稱");
    var result = await service.IssueManualWithLookupAsync(draft, new NameLookup(LookupSucceeded: true));
    Equal(true, result.Opened);
    Equal(100L, Convert.ToInt64(fake.LastIssue!.SalesAmount));
    Equal(5L, Convert.ToInt64(fake.LastIssue.TaxAmount));
    Equal(true, repository.BuyerNames.TryLookup("12345675", out var name));
    Equal("人工醫院名稱", name);
}

static async Task TestLocalCompanyNameCorrectionAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "MP12345678", 0, ""),
        QueryResponse = ConfirmedQuery("20260905006", "MP12345678", 100, 5, 105, 1),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    repository.BuyerNames.RememberAfterSuccessfulInvoice("12345675", true, "", "舊名稱", true);
    var draft = CompanyDraft("20260905006", "新名稱");
    var result = await service.IssueManualWithLookupAsync(draft, new NameLookup("舊名稱", Local: true));
    Equal(true, result.Opened);
    Equal(true, repository.BuyerNames.TryLookup("12345675", out var name));
    Equal("新名稱", name);
}

static async Task TestRefreshUnknownAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueException = new IOException("connection reset"),
        QueryException = new IOException("temporary query failure"),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    var draft = CompanyDraft("20260905003", "稍後才記住");
    await ThrowsAsync<UnknownInvoiceResultException>(() =>
        service.IssueManualWithLookupAsync(draft, new NameLookup(LookupSucceeded: true)));
    Equal(false, repository.BuyerNames.TryLookup("12345675", out _));

    fake.QueryException = null;
    fake.QueryResponse = ConfirmedQuery(TestApiOrderId("20260905003"), "NO87654321", 100, 5, 105, 1);
    await service.RefreshAllAsync();
    Equal(true, repository.BuyerNames.TryLookup("12345675", out var name));
    Equal("稍後才記住", name);
}

static async Task TestUploadStatusMatchingAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        QueryResponse = ConfirmedQuery(TestApiOrderId("20260905001"), "OP12345678", 105, 0, 105, 1),
        StatusResponse = new StatusResponse(0, "", [
            new StatusResult("WRONG00000", "", UploadStatuses.Error, "105"),
            new StatusResult("OP12345678", "", UploadStatuses.Complete, "105"),
        ]),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    repository.Invoices.Append(OpenedRecord("OP12345678"));
    await service.RefreshAllAsync();
    var record = repository.Invoices.LoadOrCreate().Single();
    Equal(UploadStatuses.Complete, record.UploadStatus);
    Equal("完成", record.UploadStatusText);

    fake.StatusResponse = new StatusResponse(0, "", [
        new StatusResult("OP12345678", "", 77, "105"),
    ]);
    await service.RefreshAllAsync();
    record = repository.Invoices.LoadOrCreate().Single();
    Equal(77, record.UploadStatus);
    Equal("狀態 77", record.UploadStatusText);

    fake.StatusResponse = new StatusResponse(0, "", [
        new StatusResult("OP12345678", "", UploadStatuses.Error, "105"),
    ]);
    await service.RefreshAllAsync();
    record = repository.Invoices.LoadOrCreate().Single();
    Equal(UploadStatuses.Error, record.UploadStatus);
    Equal("錯誤", record.UploadStatusText);
}

static async Task TestUploadStatusMismatchAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        QueryResponse = ConfirmedQuery(TestApiOrderId("20260905001"), "QR12345678", 105, 0, 105, 1),
        StatusResponse = new StatusResponse(0, "", [
            new StatusResult("ANOTHER000", "", UploadStatuses.Complete, "105"),
        ]),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    repository.Invoices.Append(OpenedRecord("QR12345678"));
    await ThrowsAsync<PartialRefreshException>(() => service.RefreshAllAsync());
    var record = repository.Invoices.LoadOrCreate().Single();
    Equal(0, record.UploadStatus);
    Equal(string.Empty, record.UploadStatusText);
}

static async Task TestBanCode99Async()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { BanException = new AmegoApiException(99, "not found") };
    var (service, _) = TestService(temporary.Path, fake);
    var lookup = await service.LookupBuyerNameAsync("13871381");
    Equal(true, lookup.LookupSucceeded);
    Equal(string.Empty, lookup.Name);
    Equal(string.Empty, await service.HealthCheckAsync());
    Equal(2, fake.BanCalls);
}

static async Task TestProductionHealthCompanyNameAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        BanResponse = new BanResponse(0, "", [new BanResult("12345675", "志遠醫療器材行")]),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    var settings = repository.Settings.LoadOrCreate();
    settings.Environment = Environments.Production;
    settings.ProductionInvoice = "12345675";
    repository.Settings.SetProductionAppKey(settings, "TEST-KEY-NOT-REAL");
    repository.Settings.Save(settings);

    Equal("志遠醫療器材行", await service.HealthCheckAsync());
    Equal("12345675", fake.LastBanQuery.Single());
}

static async Task TestDecimalIssueNumbersAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "WX12345678", 0, ""),
        QueryResponse = ConfirmedQuery(TestApiOrderId("20260905004"), "WX12345678", 190, 10, 200, 0),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var draft = CompanyDraft("20260905004", "測試公司");
    draft.PricesExcludeTax = true;
    draft.Items.Clear();
    draft.Items.Add(new InvoiceItem
    {
        Description = "商品", Quantity = 1, QuantityDecimal = "1",
        UnitPrice = 190, UnitPriceDecimal = "190.4761905",
        Amount = 190, AmountDecimal = "190.4761905",
    });
    draft.TotalAmount = 200;
    await service.IssueManualWithLookupAsync(draft, new NameLookup(LookupSucceeded: true, ApiName: "測試公司"));
    var json = JsonSerializer.Serialize(fake.LastIssue);
    if (!json.Contains("\"UnitPrice\":190.4761905", StringComparison.Ordinal) ||
        json.Contains("\"UnitPrice\":\"190.4761905\"", StringComparison.Ordinal))
        throw new InvalidOperationException("decimal value was not serialized as a JSON number");
    Equal(0, fake.LastIssue!.DetailVat);
}

static async Task TestMissingInvoiceNumberAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { IssueResponse = new IssueResponse(0, "", "", 0, "") };
    var (service, repository) = TestService(temporary.Path, fake);
    var error = await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualAsync(SafeDraft()));
    Equal(InvoiceStates.Unknown, error.Record.InvoiceState);
    Equal(InvoiceStates.Unknown, repository.Invoices.LoadOrCreate()[0].InvoiceState);
    Equal(0, fake.QueryCalls);
}

static async Task TestProductionWithoutLegacyAdminPasswordAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "PX12345678", 0, ""),
        QueryResponse = ConfirmedQuery("20260905001", "PX12345678", 105, 0, 105, 1),
    };
    var (service, repository) = TestService(temporary.Path, fake);
    var settings = repository.Settings.LoadOrCreate();
    settings.Environment = Environments.Production;
    settings.ProductionInvoice = "12345675";
    repository.Settings.SetProductionAppKey(settings, "TEST-KEY-NOT-REAL");
    repository.Settings.Save(settings);
    var result = await service.IssueManualAsync(SafeDraft());
    Equal(true, result.Opened);
    Equal(1, fake.IssueCalls);
    Equal("20260905001", fake.LastIssue!.OrderId);
}

static void TestXlsxColumns()
{
    Equal(0, XlsxRows.ColumnIndex("A1"));
    Equal(25, XlsxRows.ColumnIndex("Z2"));
    Equal(26, XlsxRows.ColumnIndex("AA3"));
    Equal(51, XlsxRows.ColumnIndex("AZ4"));
    Equal(52, XlsxRows.ColumnIndex("BA5"));
}

static void TestSpreadsheetCellValues()
{
    Equal("115102696774269", SpreadsheetCellValue.FromExcel("1.15103E+14", 115102696774269D));
    Equal("0000000000", SpreadsheetCellValue.FromExcel("0000000000", 0D));
    Equal("25130", SpreadsheetCellValue.FromExcel("########", 25130D));
    Equal("MO店+", SpreadsheetCellValue.FromExcel(" MO店+ ", null));
    Equal(string.Empty, SpreadsheetCellValue.FromExcel(string.Empty, null));
    Equal(true, SpreadsheetCellValue.IsScientificNumber("1.15103E+14"));
    Equal(false, SpreadsheetCellValue.IsScientificNumber("MO店+"));
}

static void TestXlsxRelationship()
{
    using var temporary = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temporary.Path, "relationship.xlsx");
    using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        WriteZipPart(archive, "xl/workbook.xml", """
            <?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Orders" sheetId="1" r:id="rId7"/></sheets></workbook>
            """);
        WriteZipPart(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId7" Type="worksheet" Target="worksheets/orders.xml"/></Relationships>
            """);
        WriteZipPart(archive, "xl/worksheets/sheet1.xml", Worksheet("WRONG"));
        WriteZipPart(archive, "xl/worksheets/orders.xml", Worksheet("RIGHT"));
    }
    var rows = XlsxRows.ReadFirstWorksheet(path);
    Equal("RIGHT", rows[0][0]);
}

static void TestXlsxNamedWorksheet()
{
    using var temporary = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temporary.Path, "named.xlsx");
    using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        WriteZipPart(archive, "xl/workbook.xml", """
            <?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="單頭資料" sheetId="1" r:id="rId1"/><sheet name="單身資料" sheetId="2" r:id="rId2"/></sheets></workbook>
            """);
        WriteZipPart(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="worksheet" Target="worksheets/head.xml"/><Relationship Id="rId2" Type="worksheet" Target="worksheets/detail.xml"/></Relationships>
            """);
        WriteZipPart(archive, "xl/worksheets/head.xml", Worksheet("HEAD"));
        WriteZipPart(archive, "xl/worksheets/detail.xml", Worksheet("DETAIL"));
    }
    Equal("DETAIL", XlsxRows.ReadWorksheet(path, "單身資料")[0][0]);
}

static void TestCoupangGrouping()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        ["統一編號", "應開立予酷澎之發票金額", "訂購人姓名", "數量", "顯示產品名稱", "應開立予買家之發票金額", "訂單編號"],
        ["", "0", "買家", "1", "商品甲", "100", "ORDER-1"],
        ["", "0", "買家", "2", "商品乙", "400", "ORDER-1"],
    ];
    var orders = CoupangImporter.ParseRows(rows);
    Equal(1, orders.Count);
    Equal(2, orders[0].Items.Count);
    Equal(500L, orders[0].TotalAmount);
    Equal(200L, orders[0].Items[1].UnitPrice);
}

static void TestCoupangFractionalUnitPrice()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        ["訂單編號", "顯示產品名稱", "數量", "訂購人姓名", "應開立予買家之發票金額", "應開立予酷澎之發票金額", "統一編號"],
        ["ORDER-1", "商品", "3", "買家", "100", "0", ""],
    ];
    var item = CoupangImporter.ParseRows(rows)[0].Items[0];
    Equal("33.3333333", item.UnitPriceDecimal);
    Equal("100", item.AmountDecimal);
    Equal(true, item.AllowSubtotalRounding);
}

static void TestDigiwinNegativeDiscount()
{
    IReadOnlyList<IReadOnlyList<string>> head =
    [
        ["銷貨單建立作業"],
        [],
        ["銷貨單號", "客戶全名", "統一編號", "本幣合計"],
        ["20260917005", "原始公司名稱", "12345675", "580"],
    ];
    IReadOnlyList<IReadOnlyList<string>> detail =
    [
        ["序號"],
        [],
        ["品名", "數量", "金額"],
        ["商品", "2", "600"],
        ["活動折讓", "3", "-20"],
    ];
    var order = DigiwinImporter.ParseRows(head, detail);
    Equal("20260917005", order.OrderId);
    Equal("12345675", order.BuyerBan);
    Equal("原始公司名稱", order.OriginalBuyerName);
    Equal(580L, order.TotalAmount);
    Equal(-20L, order.Items[1].Amount);
    Equal("-6.6666667", order.Items[1].UnitPriceDecimal);
    Equal(true, order.Items[1].AllowSubtotalRounding);
}

static void TestDigiwinConsumerClearsBuyerName()
{
    IReadOnlyList<IReadOnlyList<string>> head =
    [
        ["銷貨單號", "客戶全名", "統一編號", "本幣合計"],
        ["20260917006", "鼎新一定有的客戶名稱", "", "100"],
    ];
    IReadOnlyList<IReadOnlyList<string>> detail =
    [
        ["品名", "數量", "金額"],
        ["商品", "1", "100"],
    ];
    var order = DigiwinImporter.ParseRows(head, detail);
    Equal(string.Empty, order.BuyerBan);
    Equal(string.Empty, order.BuyerName);
    Equal("鼎新一定有的客戶名稱", order.OriginalBuyerName);
}

static void TestDigiwinTotalMismatch()
{
    IReadOnlyList<IReadOnlyList<string>> head =
    [
        ["銷貨單號", "客戶全名", "統一編號", "本幣合計"],
        ["20260917007", "一般消費者", "", "100"],
    ];
    IReadOnlyList<IReadOnlyList<string>> detail =
    [
        ["品名", "數量", "金額"],
        ["商品", "1", "99"],
    ];
    Throws<InvalidDataException>(() => DigiwinImporter.ParseRows(head, detail));
}

static void TestMoConvertedRejected()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        ["訂單編號", "發票類型", "載具", "載具顯碼", "載具隱碼", "捐贈對象", "買方統編", "買方名稱", "買方地址", "買方電話", "買方電子信箱", "品名", "課稅別", "數量", "單價(含稅)", "小計金額(含稅)", "商品備註(最多40個字)", "總備註(最多200個字)"],
        ["66090300839412", "B2C", "會員載具", "motmp_66090300839412", "motmp_66090300839412", "", "", "陳*菁", "", "", "", "商品", "應稅", "1", "653", "653", "備註", "總備註"],
        ["66090300839412", "", "", "", "", "", "", "", "", "", "", "運費", "應稅", "1", "45", "45", "", ""],
        ["66090300839412", "", "", "", "", "", "", "", "", "", "", "運費補貼", "應稅", "1", "-45", "-45", "", ""],
    ];
    var error = ThrowsWithResult<InvalidDataException>(() => MoImporter.ParseRows(rows));
    if (!error.Message.Contains("原始 OrderExport", StringComparison.Ordinal))
        throw new InvalidOperationException("converted MO workbook was not rejected with a clear instruction");
}

static void TestMoRawOfficialAmounts()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        MoRawHeader(),
        ["66090500872566", "神龍鍍金磁珠", "", "", "1", "應稅", "", "65", "0", "-65", "113", "225", "92644802"],
        ["66090500872566", "神龍鍍金磁珠", "", "", "1", "應稅", "", "", "", "", "112", "225", ""],
    ];
    var order = MoImporter.ParseRows(rows).Single();
    Equal(225L, order.TotalAmount);
    Equal(4, order.Items.Count);
    Equal(113L, order.Items[0].Amount);
    Equal(112L, order.Items[1].Amount);
    Equal("運費", order.Items[2].Description);
    Equal(65L, order.Items[2].Amount);
    Equal("滿額免運費", order.Items[3].Description);
    Equal(-65L, order.Items[3].Amount);
    Equal("92644802", order.BuyerBan);
    Equal("公司戶", order.Carrier);
}

static void TestMoRawRoundedSubtotal()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        MoRawHeader(),
        ["ORDER-ROUND", "三入商品", "", "", "3", "應稅", "", "0", "0", "0", "100", "100", ""],
    ];
    var item = MoImporter.ParseRows(rows).Single().Items[0];
    Equal(100L, item.Amount);
    Equal(true, item.AllowSubtotalRounding);
    Equal("33.3333333", item.UnitPriceDecimal);
}

static void TestMoScientificAmountRejected()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        MoRawHeader(),
        ["ORDER-SCI", "商品", "", "", "1", "應稅", "", "0", "0", "0", "1e2", "100", ""],
    ];
    Throws<InvalidDataException>(() => MoImporter.ParseRows(rows));
}

static void TestMoRawConditionalSubsidy()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        MoRawHeader(),
        ["ORDER-SUBSIDY", "商品", "", "", "1", "應稅", "", "0", "20", "0", "100", "120", ""],
    ];
    var order = MoImporter.ParseRows(rows).Single();
    Equal(2, order.Items.Count);
    Equal("運費補貼", order.Items[1].Description);
    Equal(20L, order.Items[1].Amount);
}

static void TestMoRawSpecifications()
{
    IReadOnlyList<IReadOnlyList<string>> rows =
    [
        MoRawHeader(),
        ["66090700928934", "匿名測試商品一", "18cm", "100碼", "1", "應稅", "測*者", "45", "-45", "0", "500", "500", ""],
    ];
    var order = MoImporter.ParseRows(rows).Single();
    Equal("匿名測試商品一 18cm 100碼", order.Items[0].Description);
    Equal(MoCarriers.Member, order.Carrier);
    Equal("motmp_66090700928934", order.CarrierId1);
    Equal("測*者", order.BuyerName);
}

static IReadOnlyList<string> MoRawHeader() =>
[
    "訂單編號", "商品名稱", "規格1", "規格2", "數量", "應稅(免稅)", "收件人姓名",
    "客人支付運費", "平台補貼運費", "商品滿額免運費",
    MoImporter.RawItemAmountHeader, MoImporter.RawTotalAmountHeader, "發票開立統編",
];

static async Task TestMoIssuePrivacyAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "MO12345678", 0, ""),
        QueryResponse = ConfirmedQuery(TestApiOrderId("66090300839412"), "MO12345678", 653, 0, 653, 1),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var order = new MoOrder
    {
        OrderId = "66090300839412",
        InvoiceType = "B2C",
        Carrier = MoCarriers.Member,
        CarrierId1 = "motmp_66090300839412",
        CarrierId2 = "motmp_66090300839412",
        BuyerName = "陳*菁",
        BuyerAddress = "真實地址",
        BuyerPhone = "0912345678",
        BuyerEmail = "buyer@example.com",
        MainRemark = "真實主備註",
        TotalAmount = 653,
    };
    order.Items.Add(new InvoiceItem { Description = "隱私商品名稱", Quantity = 1, UnitPrice = 653, Amount = 653, Remark = "真實明細備註" });
    var result = await service.IssueMoWithLookupAsync(order, new NameLookup());
    Equal(TestApiOrderId("66090300839412"), fake.LastIssue!.OrderId);
    Equal("測試消費者", fake.LastIssue.BuyerName);
    Equal("0000000000", fake.LastIssue.BuyerIdentifier);
    Equal("cyinvoice-test@example.com", fake.LastIssue.CarrierId1);
    Equal(string.Empty, fake.LastIssue.BuyerAddress);
    Equal("測試商品 1", fake.LastIssue.ProductItems[0].Description);
    Equal(string.Empty, fake.LastIssue.ProductItems[0].Remark);
    Equal("陳*菁", result.Record.BuyerName);
    Equal("隱私商品名稱", result.Record.Items[0].Description);
    Equal("真實主備註", result.Record.MainRemark);
}

static async Task TestCoupangIssuePrivacyAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway
    {
        IssueResponse = new IssueResponse(0, "", "CP12345678", 0, ""),
        QueryResponse = ConfirmedQuery(TestApiOrderId("CP-PRIVATE-1234567"), "CP12345678", 100, 5, 105, 1),
    };
    var (service, _) = TestService(temporary.Path, fake);
    var order = new CoupangOrder
    {
        OrderId = "CP-PRIVATE-1234567",
        BuyerBan = "12345675",
        BuyerName = "人工醫院名稱",
        TotalAmount = 105,
    };
    order.Items.Add(new InvoiceItem { Description = "真實酷澎商品", Quantity = 1, UnitPrice = 105, Amount = 105, Remark = "真實備註" });
    var result = await service.IssueCoupangWithLookupAsync(order, new NameLookup(LookupSucceeded: true));
    Equal(TestApiOrderId("CP-PRIVATE-1234567"), fake.LastIssue!.OrderId);
    Equal(1, fake.BanCalls);
    Equal("28080623", fake.LastBanQuery.Single());
    Equal("28080623", fake.LastIssue.BuyerIdentifier);
    Equal("光貿測試買受人", fake.LastIssue.BuyerName);
    Equal("測試商品 1", fake.LastIssue.ProductItems[0].Description);
    Equal("12345675", result.Record.BuyerIdentifier);
    Equal("人工醫院名稱", result.Record.BuyerName);
    Equal("真實酷澎商品", result.Record.Items[0].Description);
}

static async Task TestMissingCompanyNameAsync()
{
    foreach (var path in new[] { "manual", "manual-lookup", "mo", "mo-lookup", "coupang", "coupang-lookup", "digiwin" })
    foreach (var name in new[] { "", "  ", "人工非營利名稱" })
    {
        using var temporary = new TemporaryDirectory();
        var orderId = "MISSING-NAME-" + path;
        var fake = new FakeGateway
        {
            BanException = path == "coupang" ? null : new AmegoApiException(99, "synthetic no matching name"),
            BanResponse = new BanResponse(0, "", []),
            IssueResponse = new IssueResponse(0, "", "MN12345678", 0, ""),
            QueryResponse = ConfirmedQuery(orderId, "MN12345678", 100, 5, 105, 1),
        };
        var (service, repository) = TestService(temporary.Path, fake);
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-KEY-NOT-REAL");
        repository.Settings.Save(settings);
        var lookup = new NameLookup(LookupSucceeded: true);
        var item = new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 105, Amount = 105 };
        IssueResult result;
        if (path.StartsWith("manual"))
        {
            var draft = CompanyDraft(orderId, name);
            result = path == "manual" ? await service.IssueManualAsync(draft) : await service.IssueManualWithLookupAsync(draft, lookup);
        }
        else if (path.StartsWith("mo"))
        {
            var order = new MoOrder { OrderId = orderId, BuyerBan = "12345675", BuyerName = name, TotalAmount = 105 };
            order.Items.Add(item);
            result = path == "mo" ? await service.IssueMoAsync(order) : await service.IssueMoWithLookupAsync(order, lookup);
        }
        else if (path.StartsWith("coupang"))
        {
            var order = new CoupangOrder { OrderId = orderId, BuyerBan = "12345675", BuyerName = name, TotalAmount = 105 };
            order.Items.Add(item);
            result = path == "coupang" ? await service.IssueCoupangAsync(order) : await service.IssueCoupangWithLookupAsync(order, lookup);
        }
        else
        {
            var order = new DigiwinOrder { OrderId = orderId, BuyerBan = "12345675", BuyerName = name, TotalAmount = 105 };
            order.Items.Add(item);
            result = await service.IssueDigiwinWithLookupAsync(order, lookup);
        }
        Equal(true, result.Opened);
        Equal(1, fake.IssueCalls);
        var expected = name.Trim().Length == 0 ? "12345675" : name.Trim();
        Equal("12345675", fake.LastIssue!.BuyerIdentifier);
        Equal(expected, fake.LastIssue.BuyerName);
        Equal(expected, result.Record.BuyerName);
        Equal(expected, repository.Invoices.LoadOrCreate().Single().BuyerName);
        Equal(name.Trim().Length != 0, result.Record.BuyerNameNeedsMemory);
        Equal(name.Trim().Length != 0, repository.BuyerNames.TryLookup("12345675", out var remembered));
        if (name.Trim().Length != 0) Equal(name.Trim(), remembered);
    }
}

static async Task TestMissingCompanyNameRecoveryAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { IssueException = new IOException("synthetic connection reset"), QueryException = new IOException("synthetic temporary failure") };
    var (service, repository) = TestService(temporary.Path, fake);
    var draft = CompanyDraft("MISSING-NAME-RECOVERY", "");
    await ThrowsAsync<UnknownInvoiceResultException>(() => service.IssueManualWithLookupAsync(draft, new NameLookup(LookupSucceeded: true)));
    Equal("", draft.BuyerName); // Auto transport fallback must not turn into a supplied manual name on retry.
    var stored = repository.Invoices.LoadOrCreate().Single();
    Equal("12345675", stored.BuyerName);
    Equal(false, stored.BuyerNameNeedsMemory);
    Equal(false, repository.BuyerNames.TryLookup("12345675", out _));
    fake.QueryException = null;
    fake.QueryResponse = ConfirmedQuery(TestApiOrderId(draft.OrderId), "MR12345678", 100, 5, 105, 1);
    await service.RefreshAllAsync();
    Equal(InvoiceStates.Opened, repository.Invoices.LoadOrCreate().Single().InvoiceState);
    Equal(false, repository.BuyerNames.TryLookup("12345675", out _));
}

static async Task TestMissingCompanyNameRefusalAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway();
    var (service, repository) = TestService(temporary.Path, fake);
    await ThrowsAsync<InvalidOperationException>(() => service.IssueManualWithLookupAsync(CompanyDraft("UNRESOLVED", ""), new NameLookup()));
    foreach (var error in new Exception[] { new IOException("synthetic transport failure"), new AmegoApiException(503, "unavailable"), new AmegoApiException(401, "denied") })
    {
        fake.BanException = error;
        await ThrowsAsync<Exception>(() => service.IssueManualAsync(CompanyDraft("FAILED-LOOKUP", "")));
    }
    Equal(0, fake.IssueCalls);
    Equal(0, repository.Invoices.LoadOrCreate().Count);
    Equal(false, repository.BuyerNames.TryLookup("12345675", out _));
}

static async Task TestImportedLookupBlockAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway();
    var (service, _) = TestService(temporary.Path, fake);
    var order = new CoupangOrder
    {
        OrderId = "ORDER-BLOCK",
        BuyerBan = "12345675",
        BuyerName = "未經確認",
        TotalAmount = 105,
    };
    order.Items.Add(new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 105, Amount = 105 });
    await ThrowsAsync<InvalidOperationException>(() => service.IssueCoupangWithLookupAsync(order, new NameLookup()));
    Equal(0, fake.IssueCalls);
}

static async Task TestAutoInputPdfAsync()
{
    using var temp = new TemporaryDirectory();
    var fake = new FakeGateway { PdfBytes = TestPdfBytes() };
    var (service, repository) = TestService(temp.Path, fake);
    const string orderId = "90000000000001";
    var record = PdfReadyRecord("AX12345678");
    record.Source = InvoiceSources.Mo;
    record.OriginalOrderId = orderId;
    record.OrderId = orderId;
    record.InvoiceDate = "2026/10/15";
    record.BuyerIdentifier = "12345675";
    record.BuyerName = "範例股份有限公司";
    repository.Invoices.Append(record);
    var folder = Path.Combine(temp.Path,"paper");
    Directory.CreateDirectory(folder);
    var exporter = new AutoInputPdfExport(repository,service);
    var ready = await exporter.ExportAsync(orderId,InvoiceSources.Mo,Environments.Test,folder,5);
    Equal("ready",ready.Status);
    Equal(record.InvoiceNumber,ready.InvoiceNumber);
    Equal(record.Amount,ready.TotalAmount);
    Equal(record.BuyerIdentifier,ready.BuyerBan);
    Equal(Path.Combine(folder,"AX12345678_style5.pdf"),ready.PdfFile);
    Equal(true,File.Exists(ready.PdfFile));
    Equal(1,fake.PdfCalls);
    Equal(0,fake.IssueCalls);
    var second = await exporter.ExportAsync(orderId,InvoiceSources.Mo,Environments.Test,folder,5);
    Equal("ready",second.Status);
    Equal(1,fake.PdfCalls);
    File.WriteAllText(ready.PdfFile,"conflicting file");
    var clash = await exporter.ExportAsync(orderId,InvoiceSources.Mo,Environments.Test,folder,5);
    Equal("unknown",clash.Status);
    Equal("conflicting file",File.ReadAllText(ready.PdfFile));
    Equal(0,fake.IssueCalls);
    Equal("unknown",(await exporter.ExportAsync(orderId,InvoiceSources.Coupang,
        Environments.Test,folder,5)).Status);
    Equal("skipped",(await exporter.ExportAsync("NOT-IN-LOCAL",InvoiceSources.Mo,
        Environments.Test,folder,5)).Status);

    record = repository.Invoices.LoadOrCreate().Single();
    record.UploadStatus = UploadStatuses.Pending;
    repository.Invoices.Save([record]);
    fake.PdfException = new AmegoApiException(99, "PDF 尚未產生");
    Equal("pending",(await exporter.ExportAsync(orderId,InvoiceSources.Mo,
        Environments.Test,folder,0)).Status);
    Equal(2,fake.PdfCalls);
    // Once the real official PDF becomes available it must work even before
    // the local upload status has been refreshed by the GUI timer.
    fake.PdfException = null;
    Equal("ready",(await exporter.ExportAsync(orderId,InvoiceSources.Mo,
        Environments.Test,folder,0)).Status);
    Equal(3,fake.PdfCalls);
    Equal(0,fake.IssueCalls);

    record.UploadStatus = UploadStatuses.Complete;
    record.BuyerIdentifier = "0000000000";
    repository.Invoices.Save([record]);
    var paperConsumer = await exporter.ExportAsync(orderId,InvoiceSources.Mo,
        Environments.Test,folder,5);
    Equal("ready",paperConsumer.Status);
    Equal(string.Empty,paperConsumer.BuyerBan);
    Equal(Path.Combine(folder,"AX12345678_style0.pdf"),paperConsumer.PdfFile);
    Equal(true,paperConsumer.Message.Contains("pdfStyle=0",StringComparison.Ordinal));
    Equal(0,fake.LastPdfStyle);
    Equal(0,fake.IssueCalls);

    record.Delivery = "會員載具";
    repository.Invoices.Save([record]);
    Equal("skipped",(await exporter.ExportAsync(orderId,InvoiceSources.Mo,
        Environments.Test,folder,5)).Status);
    record.Delivery = "捐贈";
    repository.Invoices.Save([record]);
    Equal("skipped",(await exporter.ExportAsync(orderId,InvoiceSources.Mo,
        Environments.Test,folder,5)).Status);

    // Coupang B2C has no company BAN but MUST be paper and downloadable.
    var coupangRecord = PdfReadyRecord("AY12345678");
    coupangRecord.Source = InvoiceSources.Coupang;
    coupangRecord.OriginalOrderId = "COUPANG-ORDER-01";
    coupangRecord.OrderId = "COUPANG-ORDER-01";
    coupangRecord.BuyerIdentifier = string.Empty;
    coupangRecord.BuyerName = "消費者";
    repository.Invoices.Save([record,coupangRecord]);
    var coupangPaper = await exporter.ExportAsync("COUPANG-ORDER-01",
        InvoiceSources.Coupang,Environments.Test,folder,3);
    Equal("ready",coupangPaper.Status);
    Equal("COUPANG-ORDER-01",coupangPaper.OrderId);
    Equal(coupangRecord.Amount,coupangPaper.TotalAmount);
    Equal(string.Empty,coupangPaper.BuyerBan);
    Equal(Path.Combine(folder,"AY12345678_style0.pdf"),coupangPaper.PdfFile);
    Equal(true,coupangPaper.Message.Contains("A4",StringComparison.Ordinal));
    Equal(0,fake.LastPdfStyle);
    Equal(0,fake.IssueCalls);
    // A replay reads the original safe PDF without calling Issue or replacing it.
    var coupangAgain = await exporter.ExportAsync("COUPANG-ORDER-01",
        InvoiceSources.Coupang,Environments.Test,folder,5);
    Equal("ready",coupangAgain.Status);
    Equal(coupangPaper.PdfFile,coupangAgain.PdfFile);
    Equal(0,fake.IssueCalls);
    var collision = coupangRecord;
    collision.BuyerIdentifier = "12AB5678";
    repository.Invoices.Save([record,collision]);
    Equal("unknown",(await exporter.ExportAsync("COUPANG-ORDER-01",
        InvoiceSources.Coupang,Environments.Test,folder,5)).Status);
    Equal(0,fake.IssueCalls);

    var output = Path.Combine(temp.Path,"result.json");
    var envelope = new AutoInputResult(AutoInputContract.ResultSchema,Guid.NewGuid().ToString("D"),
        "2.6.21+0","test",DateTimeOffset.UtcNow.ToString("O"),[ready]);
    await AutoInputResultFile.WriteAtomicAsync(output,envelope);
    using var document = JsonDocument.Parse(File.ReadAllText(output));
    Equal("ready",document.RootElement.GetProperty("orders")[0].GetProperty("status").GetString());
    Equal(ready.PdfFile,document.RootElement.GetProperty("orders")[0].GetProperty("pdfFile").GetString());
    await ThrowsAsync<IOException>(() => AutoInputResultFile.WriteAtomicAsync(output,envelope));
}

static async Task TestConsumerPdfCacheAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { PdfBytes = TestPdfBytes() };
    var (service, repository) = TestService(temporary.Path, fake);
    var record = PdfReadyRecord("UV12345678");
    repository.Invoices.Append(record);

    var first = await service.GetInvoicePdfAsync(record, 0);
    var second = await service.GetInvoicePdfAsync(record, 0);
    Equal(false, first.FromCache);
    Equal(true, second.FromCache);
    Equal(first.Path, second.Path);
    Equal(1, fake.PdfCalls);
    Equal(0, fake.LastPdfStyle);
    Equal("UV12345678", fake.LastPdfInvoiceNumber);
    await ThrowsAsync<InvalidOperationException>(() => service.GetInvoicePdfAsync(record, 1));
    Equal(1, fake.PdfCalls);

    File.WriteAllText(first.Path, "not a pdf");
    var repaired = await service.GetInvoicePdfAsync(record, 0);
    Equal(false, repaired.FromCache);
    Equal(2, fake.PdfCalls);

    File.Delete(repaired.Path);
    fake.PdfBytes = Encoding.UTF8.GetBytes("<html>not a PDF</html>");
    await ThrowsAsync<InvalidDataException>(() => service.GetInvoicePdfAsync(record, 0));
    Equal(false, File.Exists(repaired.Path));
    Equal(3, fake.PdfCalls);
}

static async Task TestCompanyPdfStylesAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { PdfBytes = TestPdfBytes() };
    var repository = TestRepository.Open(temporary.Path, new TestProtector());
    var current = new DateTimeOffset(2026, 9, 5, 9, 8, 7, TimeSpan.FromHours(8));
    var service = new InvoiceService(repository, (_, _) => fake, () => current);
    var record = PdfReadyRecord("WX12345678");
    record.BuyerIdentifier = "12345675";
    repository.Invoices.Append(record);

    Equal("0,1,2,3,5", string.Join(",", service.GetInvoicePdfStyles(record).Select(style => style.Code)));
    var a4 = await service.GetInvoicePdfAsync(record, 0);
    var a5 = await service.GetInvoicePdfAsync(record, 3);
    var a5Cached = await service.GetInvoicePdfAsync(record, 3);
    Equal(false, a4.FromCache);
    Equal(false, a5.FromCache);
    Equal(true, a5Cached.FromCache);
    Equal(false, string.Equals(a4.Path, a5.Path, StringComparison.OrdinalIgnoreCase));
    Equal(2, fake.PdfCalls);

    current = current.AddDays(1);
    var nextDay = await service.GetInvoicePdfAsync(record, 0);
    Equal(false, nextDay.FromCache);
    Equal(false, string.Equals(a4.Path, nextDay.Path, StringComparison.OrdinalIgnoreCase));
    Equal(3, fake.PdfCalls);
}

static async Task TestInvoicePreviewCacheAsync()
{
    using var temporary = new TemporaryDirectory();
    var repository = TestRepository.Open(temporary.Path, new TestProtector());
    var pdfPath = Path.Combine(repository.InvoicePdfCacheDirectory, "test", "20260905", "AA12345678_style0.pdf");
    var previewPath = InvoicePreviewCache.PathForPdf(
        repository.InvoicePreviewCacheDirectory,
        repository.InvoicePdfCacheDirectory,
        pdfPath);
    Equal(
        Path.Combine(repository.InvoicePreviewCacheDirectory, "test", "20260905", "AA12345678_style0.page1.png"),
        previewPath);

    var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
    await InvoicePreviewCache.WriteAsync(previewPath, png);
    var cached = await InvoicePreviewCache.TryReadAsync(previewPath);
    Equal(true, cached is not null && cached.SequenceEqual(png));

    File.WriteAllText(previewPath, "not png");
    Equal<byte[]?>(null, await InvoicePreviewCache.TryReadAsync(previewPath));
    await ThrowsAsync<InvalidDataException>(() => InvoicePreviewCache.WriteAsync(previewPath, [1, 2, 3]));
    Throws<InvalidOperationException>(() => InvoicePreviewCache.PathForPdf(
        repository.InvoicePreviewCacheDirectory,
        repository.InvoicePdfCacheDirectory,
        Path.Combine(temporary.Path, "outside.pdf")));
}

static async Task TestPendingUploadPdfAttemptAsync()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway { PdfException = new AmegoApiException(99, "尚未產生") };
    var (service, repository) = TestService(temporary.Path, fake);
    var record = PdfReadyRecord("XY12345678");
    record.UploadStatus = UploadStatuses.Uploading;
    record.UploadStatusText = "上傳中";
    repository.Invoices.Append(record);

    Equal(true, service.GetInvoicePdfEligibility(record).Allowed);
    var error = await ThrowsAsync<InvalidOperationException>(() => service.GetInvoicePdfAsync(record, 0));
    Equal("光貿尚未提供這張發票的官方 PDF，請稍後再試", error.Message);
    Equal(1, fake.PdfCalls);
}

static void TestPdfEligibility()
{
    using var temporary = new TemporaryDirectory();
    var fake = new FakeGateway();
    var (service, _) = TestService(temporary.Path, fake);
    var record = PdfReadyRecord("YZ12345678");
    Equal(true, service.GetInvoicePdfEligibility(record).Allowed);
    record.UploadStatus = UploadStatuses.Pending;
    Equal(true, service.GetInvoicePdfEligibility(record).Allowed);
    record.UploadStatus = UploadStatuses.Error;
    Equal(true, service.GetInvoicePdfEligibility(record).Allowed);
    record.Delivery = "會員載具";
    Equal(false, service.GetInvoicePdfEligibility(record).Allowed);
    record.Delivery = InvoiceService.DeliveryPaper;
    record.InvoiceState = InvoiceStates.Voided;
    Equal(false, service.GetInvoicePdfEligibility(record).Allowed);
    record.InvoiceState = InvoiceStates.Opened;
    record.Environment = Environments.Production;
    Equal(false, service.GetInvoicePdfEligibility(record).Allowed);
}

static void WriteZipPart(ZipArchive archive, string name, string content)
{
    using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
    writer.Write(content);
}

static string Worksheet(string value) =>
    $"<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row><c r=\"A1\" t=\"inlineStr\"><is><t>{value}</t></is></c></row></sheetData></worksheet>";

static InvoiceDraft SafeDraft()
{
    var draft = new InvoiceDraft { OrderId = "20260905001", TotalAmount = 105 };
    draft.Items.Add(new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 105, Amount = 105 });
    return draft;
}

static string TestApiOrderId(string orderId) =>
    TestOrderIdPrefix.Apply(orderId, new DateOnly(2026, 9, 5));

static InvoiceDraft CompanyDraft(string orderId, string buyerName)
{
    var draft = SafeDraft();
    draft.OrderId = orderId;
    draft.CompanyBuyer = true;
    draft.BuyerIdentifier = "12345675";
    draft.BuyerName = buyerName;
    return draft;
}

static InvoiceRecord OpenedRecord(string invoiceNumber) => new()
{
    Id = "opened-record",
    Source = InvoiceSources.Manual,
    OriginalOrderId = "20260905001",
    OrderId = "20260905001",
    ApiOrderId = TestApiOrderId("20260905001"),
    Attempt = 1,
    Environment = Environments.Test,
    InvoiceNumber = invoiceNumber,
    InvoiceState = InvoiceStates.Opened,
    BuyerIdentifier = "0000000000",
    BuyerName = "消費者",
    Amount = 105,
    Delivery = InvoiceService.DeliveryPaper,
    DetailVat = 1,
    Items = [new InvoiceItem { Description = "商品", Quantity = 1, UnitPrice = 105, Amount = 105 }],
};

static InvoiceRecord PdfReadyRecord(string invoiceNumber)
{
    var record = OpenedRecord(invoiceNumber);
    record.Id = "pdf-" + invoiceNumber;
    record.UploadStatus = UploadStatuses.Complete;
    record.UploadStatusText = "完成";
    return record;
}

static QueryResponse ConfirmedQuery(
    string orderId,
    string invoiceNumber,
    long sales,
    long tax,
    long total,
    int detailVat,
    long cancelDate = 0) => new(
        0,
        "",
        new QueryResult(
            InvoiceNumber: invoiceNumber,
            InvoiceType: "",
            InvoiceStatus: 0,
            InvoiceDate: "",
            InvoiceTime: "",
            BuyerIdentifier: "",
            BuyerName: "",
            SalesAmount: sales.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TaxAmount: tax.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TotalAmount: total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarrierType: "",
            CarrierId1: "",
            CarrierId2: "",
            NpoBan: "",
            CancelDate: cancelDate,
            OrderId: orderId,
            CreateDate: 0,
            ProductItems: default,
            DetailVat: detailVat,
            DetailVatPresent: true));

static (InvoiceService Service, LocalRepository Repository) TestService(
    string path,
    FakeGateway gateway,
    Action<string, StatusUpdate>? statusUpdater = null)
{
    var repository = TestRepository.Open(path, new TestProtector());
    var service = new InvoiceService(
        repository,
        (_, _) => gateway,
        () => new DateTimeOffset(2026, 9, 5, 9, 8, 7, TimeSpan.FromHours(8)),
        statusUpdater);
    return (service, repository);
}

sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}

sealed class FakeGateway : IAmegoGateway
{
    public IssueResponse IssueResponse { get; set; } = new(0, "", "", 0, "");
    public Exception? IssueException { get; set; }
    public QueryResponse QueryResponse { get; set; } = null!;
    public Exception? QueryException { get; set; }
    public BanResponse BanResponse { get; set; } = new(0, "", [new BanResult("28080623", "光貿測試買受人")]);
    public Exception? BanException { get; set; }
    public StatusResponse StatusResponse { get; set; } = new(0, "", []);
    public Exception? StatusException { get; set; }
    public byte[] PdfBytes { get; set; } = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n");
    public Exception? PdfException { get; set; }
    public int IssueCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public int BanCalls { get; private set; }
    public int PdfCalls { get; private set; }
    public string LastPdfInvoiceNumber { get; private set; } = string.Empty;
    public int LastPdfStyle { get; private set; } = -1;
    public IReadOnlyList<string> LastBanQuery { get; private set; } = [];
    public IssueRequest? LastIssue { get; private set; }
    public string LastQueryOrderId { get; private set; } = string.Empty;
    public bool LastQueryTokenWasCancelled { get; private set; }
    public bool RejectCancelledQueryToken { get; set; }
    public TaskCompletionSource<bool>? IssueStarted { get; set; }
    public TaskCompletionSource<bool>? IssueRelease { get; set; }
    public Action<IssueRequest>? OnIssue { get; set; }

    public async Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default)
    {
        IssueCalls++;
        LastIssue = request;
        OnIssue?.Invoke(request);
        IssueStarted?.TrySetResult(true);
        if (IssueRelease is not null)
            await IssueRelease.Task.WaitAsync(cancellationToken);
        if (IssueException is { } issueException)
            throw issueException;
        return IssueResponse;
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        LastQueryOrderId = orderId;
        LastQueryTokenWasCancelled = cancellationToken.IsCancellationRequested;
        if (RejectCancelledQueryToken && cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<QueryResponse>(cancellationToken);
        return QueryException is null ? Task.FromResult(QueryResponse) : Task.FromException<QueryResponse>(QueryException);
    }

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return QueryException is null ? Task.FromResult(QueryResponse) : Task.FromException<QueryResponse>(QueryException);
    }

    public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
        StatusException is null ? Task.FromResult(StatusResponse) : Task.FromException<StatusResponse>(StatusException);

    public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default)
    {
        BanCalls++;
        LastBanQuery = bans.ToArray();
        return BanException is null ? Task.FromResult(BanResponse) : Task.FromException<BanResponse>(BanException);
    }

    public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default)
    {
        PdfCalls++;
        LastPdfInvoiceNumber = invoiceNumber;
        LastPdfStyle = downloadStyle;
        return PdfException is null ? Task.FromResult(PdfBytes) : Task.FromException<byte[]>(PdfException);
    }
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
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
