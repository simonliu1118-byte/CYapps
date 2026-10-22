using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Sync.Tests;

internal static class Program
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateOnly FixedDate = new(2026, 9, 18);

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("verified historical duplicate repairs transactionally and only once", TestHistoricalDuplicateAsync),
            ("unsafe historical duplicates preserve both records", TestUnsafeHistoricalDuplicatesAsync),
            ("historical repair isolates company environment and unrelated data", TestHistoricalIsolationAsync),
            ("three historical success candidates are never auto repaired", TestHistoricalThreeCandidatesAsync),
            ("historical duplicate query failure preserves both records", TestHistoricalQueryFailureAsync),
            ("historical duplicate backup failure prevents writes", TestHistoricalBackupFailureAsync),
            ("concurrent data changes prevent historical correction", TestHistoricalConcurrentChangeAsync),
            ("interrupted historical repair rolls back and retries", TestHistoricalRollbackAsync),
            ("cancelled historical repair preserves records", TestHistoricalCancellationAsync),
            ("source inference follows current OrderID", TestSourceInferenceAsync),
            ("test prefix is four characters and deterministic", TestPrefixFormulaAsync),
            ("test issue sends prefixed API OrderID but keeps local OrderID", TestIssueUsesTestPrefixAsync),
            ("production sync discovers remote invoice and queries details", TestProductionDiscoveryAsync),
            ("production sync overwrites cache and re-infers source", TestProductionOverwriteAsync),
            ("unchanged production summary skips invoice query", TestUnchangedSkipsQueryAsync),
            ("official allowance history survives invoice sync", TestAllowanceHistorySyncAsync),
            ("daily allowance discovery refreshes old invoice", TestHistoricalAllowanceDiscoveryAsync),
            ("unmatched allowance restores official old invoice on demand", TestUnmatchedAllowanceRestoresOriginalAsync),
            ("unmatched allowance not found stays visible and can recover", TestUnmatchedAllowanceNotFoundAsync),
            ("mismatched official original never enters local history", TestUnmatchedAllowanceMismatchedOriginalAsync),
            ("allowance discovery overflow never starts unbounded queries", TestAllowanceDiscoveryLimitAsync),
            ("three allowance list pages of 50 are accepted", TestAllowanceListCapacityAsync),
            ("existing allowance cancellation is refreshed without creation event", TestExistingAllowanceCancellationAsync),
            ("test list discovers only CYInvoice-prefixed invoices", TestTestEnvironmentPrefixDiscoveryAsync),
            ("test environment skips definite failed records", TestTestEnvironmentSkipsFailedAsync),
            ("remote absence never deletes local cache", TestRemoteAbsenceDoesNotDeleteAsync),
            ("invoice list pagination is exhausted", TestPaginationAsync),
            ("detail refresh always queries even when official data is unchanged", TestDetailRefreshAlwaysQueriesAsync),
            ("detail refresh overwrites official fields and invalidates cache", TestDetailRefreshOverwriteAsync),
            ("allowance-only detail change invalidates PDF cache", TestAllowanceOnlyRefreshInvalidatesCacheAsync),
            ("official allowance changes have append-only retained audit", TestOfficialAllowanceAuditAsync),
            ("historical allowance query failure remains actionable until recovered", TestHistoricalAllowanceRetryIssueAsync),
            ("test detail refresh strips prefix from displayed OrderID", TestTestDetailRefreshStripsPrefixAsync),
            ("detail refresh failure leaves local cache untouched", TestDetailRefreshFailureLeavesCacheAsync),
            ("detail refresh falls back to OrderID when invoice number is missing", TestDetailRefreshByOrderIdAsync),
            ("failed duplicate is not replaced by a detail query", TestFailedDetailRefreshAsync),
            ("official sync does not adopt a failed attempt by OrderID", TestFailedOrderMatchAsync),
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
                Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
            }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} sync tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static (InvoiceRecord Original, InvoiceRecord Extra, FakeGateway Gateway) HistoricalPair(LocalRepository repository)
    {
        ConfigureProduction(repository);
        var original = MatchingRecord("AA12345678", "66091800123456");
        original.Id = "1789697410000000000-000000000001";
        original.SentAt = "2026/09/18 10:10:10";
        var extra = JsonSerializer.Deserialize<InvoiceRecord>(JsonSerializer.Serialize(original))!;
        extra.Id = "1789697420000000000-000000000002";
        extra.SentAt = "2026/09/18 10:10:20";
        // Deliberately reverse insertion order: persisted chronology, not list order, selects the original.
        repository.Invoices.Save([extra, original]);
        var gateway = new FakeGateway { QueryResponse = Query(original.InvoiceNumber, original.ApiOrderId, "100", "買受人", description: "商品") };
        gateway.Pages[1] = ListResponse(1, [Remote(original.InvoiceNumber, original.ApiOrderId, "100")]);
        return (original, extra, gateway);
    }

    private static string Stored(LocalRepository repository) => JsonSerializer.Serialize(repository.Invoices.LoadOrCreate());
    private static string[] Backups(LocalRepository repository) => Directory.Exists(Path.Combine(repository.DataDirectory, "RepairBackups"))
        ? Directory.GetFiles(Path.Combine(repository.DataDirectory, "RepairBackups"), "*.db") : [];

    private static void Sql(LocalRepository repository, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(repository.DataDirectory, "CYInvoice.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task TestHistoricalDuplicateAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (original, extra, gateway) = HistoricalPair(repository);
        new InvoiceSyncIssueStore(repository.DataDirectory).Record("prod|12345675", original.InvoiceNumber, original.ApiOrderId, InvoiceSyncIssueTypes.AmbiguousMatch, "synthetic preexisting ambiguity", FixedNow);
        var result = await Sync(repository, gateway).SyncRecentAsync();
        Equal(0, result.Problems.Count);
        Equal(1, result.Queried);
        Equal(1, gateway.QueryByInvoiceCalls);
        Equal(0, gateway.QueryByOrderCalls);
        Equal(string.Empty, gateway.LastIssueOrderId);
        var records = repository.Invoices.LoadOrCreate();
        Equal(2, records.Count);
        var kept = records.Single(record => record.Id == original.Id);
        Equal(InvoiceStates.Opened, kept.InvoiceState);
        Equal(original.InvoiceNumber, kept.InvoiceNumber);
        var rejected = records.Single(record => record.Id == extra.Id);
        Equal(InvoiceStates.Failed, rejected.InvoiceState);
        Equal(string.Empty, rejected.InvoiceNumber);
        Equal(0, rejected.UploadStatus);
        Equal(string.Empty, rejected.UploadStatusText);
        Equal(extra.OrderId, rejected.OrderId);
        Equal(extra.SentAt, rejected.SentAt);
        Equal(true, rejected.ErrorMessage.Contains("重複訂單開立失敗", StringComparison.Ordinal));
        using (var backup = new SqliteConnection($"Data Source={Backups(repository).Single()};Mode=ReadOnly;Pooling=False"))
        {
            backup.Open();
            using var command = backup.CreateCommand();
            command.CommandText = "SELECT count(*) FROM invoices WHERE invoice_state = '已開立';";
            Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
            command.CommandText = "SELECT count(*) FROM invoice_items;";
            Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
            command.CommandText = "PRAGMA integrity_check;";
            Equal("ok", command.ExecuteScalar()?.ToString());
            command.CommandText = "PRAGMA foreign_key_check;";
            using var reader = command.ExecuteReader();
            Equal(false, reader.Read());
        }
        var after = Stored(repository);
        var repeat = await Sync(repository, gateway).SyncRecentAsync();
        Equal(0, repeat.Problems.Count);
        Equal(0, repeat.Queried);
        Equal(after, Stored(repository));
        Equal(1, Backups(repository).Length);
        Equal(0, new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("prod|12345675").Count);
    }

    private static async Task TestUnsafeHistoricalDuplicatesAsync()
    {
        var cases = new Action<InvoiceRecord, InvoiceRecord, FakeGateway>[]
        {
            (_, extra, _) => extra.Amount = 200,
            (_, extra, _) => extra.BuyerName = "另一個買受人",
            (_, extra, _) => extra.Items[0].Description = "另一項商品",
            (_, extra, _) => extra.SentAt = string.Empty,
            (_, extra, _) => extra.SentAt = "2026/09/18 10:10:30",
            (_, extra, _) => extra.SentAt = "2026/09/18 09:00:00",
            (original, extra, _) => extra.Id = original.Id[..19] + "-000000000002",
            (_, extra, _) => extra.Id = "legacy-record-id",
            (_, extra, _) => extra.ApiOrderId = "OTHER",
            (_, extra, _) => extra.RecordOrigin = RecordOrigins.Sync,
            (_, extra, _) => extra.InvoiceState = InvoiceStates.OpenedWaitingVoid,
            (_, extra, _) => extra.ExtensionData = new() { ["manual_work"] = JsonSerializer.SerializeToElement(true) },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Code = 70 },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { InvoiceTime = "110000" } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { CancelDate = 1789697430 } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { Allowances = [new InvoiceAllowanceResult("07", 99, 1, "CA12345678", "20260918", "0", "10")] } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { OrderId = "OTHER" } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { InvoiceNumber = "ZZ12345678" } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { ProductItems = default } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { VoidPending = true } },
            (_, _, gateway) => gateway.QueryResponse = gateway.QueryResponse with { Data = gateway.QueryResponse.Data with { DetailVatPresent = false } },
        };
        foreach (var change in cases)
        {
            using var temporary = new TemporaryDirectory();
            var repository = TestRepository.Open(temporary.Path, new TestProtector());
            var (original, extra, gateway) = HistoricalPair(repository);
            change(original, extra, gateway);
            repository.Invoices.Save([extra, original]);
            var before = Stored(repository);
            var result = await Sync(repository, gateway).SyncRecentAsync();
            Equal(true, result.Problems.Count > 0);
            Equal(before, Stored(repository));
            Equal(0, Backups(repository).Length);
        }
    }

    private static async Task TestHistoricalIsolationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (original, _, gateway) = HistoricalPair(repository);
        var others = new List<InvoiceRecord>();
        foreach (var scope in new[] { "other-company", "other-environment", "unrelated" })
        {
            var other = JsonSerializer.Deserialize<InvoiceRecord>(JsonSerializer.Serialize(original))!;
            other.Id = scope;
            if (scope == "other-company") other.SellerInvoice = "87654321";
            if (scope == "other-environment") other.Environment = Environments.Test;
            if (scope == "unrelated") { other.InvoiceNumber = "BB12345678"; other.ApiOrderId = other.OrderId = other.OriginalOrderId = "OTHER"; }
            repository.Invoices.Append(other);
            others.Add(other);
        }
        Equal(0, (await Sync(repository, gateway).SyncRecentAsync()).Problems.Count);
        var stored = repository.Invoices.LoadOrCreate();
        foreach (var other in others)
            Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(stored.Single(record => record.Id == other.Id)));
        Equal(1, stored.Count(record => record.InvoiceState == InvoiceStates.Failed));
    }

    private static async Task TestHistoricalThreeCandidatesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (original, _, gateway) = HistoricalPair(repository);
        var third = JsonSerializer.Deserialize<InvoiceRecord>(JsonSerializer.Serialize(original))!;
        third.Id = "1789697430000000000-000000000003";
        third.SentAt = "2026/09/18 10:10:30";
        repository.Invoices.Append(third);
        var before = Stored(repository);
        Equal(true, (await Sync(repository, gateway).SyncRecentAsync()).Problems.Count > 0);
        Equal(before, Stored(repository));
        Equal(0, gateway.QueryCalls);
        Equal(0, Backups(repository).Length);
    }

    private static async Task TestHistoricalQueryFailureAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (_, _, gateway) = HistoricalPair(repository);
        gateway.QueryException = new IOException("synthetic offline");
        var before = Stored(repository);
        Equal(true, (await Sync(repository, gateway).SyncRecentAsync()).Problems.Count > 0);
        Equal(before, Stored(repository));
        Equal(0, Backups(repository).Length);
    }

    private static async Task TestHistoricalBackupFailureAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (_, _, gateway) = HistoricalPair(repository);
        File.WriteAllText(Path.Combine(repository.DataDirectory, "RepairBackups"), "synthetic blocked directory");
        var before = Stored(repository);
        try { await Sync(repository, gateway).SyncRecentAsync(); throw new Exception("expected backup failure"); }
        catch (IOException) { }
        Equal(before, Stored(repository));
    }

    private static async Task TestHistoricalConcurrentChangeAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (_, _, gateway) = HistoricalPair(repository);
        string? concurrent = null;
        gateway.OnQuery = () =>
        {
            Sql(repository, "UPDATE invoices SET buyer_name = 'concurrent synthetic change' WHERE sent_at = '2026/09/18 10:10:20';");
            concurrent = Stored(repository);
        };
        try { await Sync(repository, gateway).SyncRecentAsync(); throw new Exception("expected snapshot conflict"); }
        catch (InvalidDataException) { }
        Equal(concurrent, Stored(repository));
        Equal(0, Backups(repository).Length);
    }

    private static async Task TestHistoricalRollbackAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (_, _, gateway) = HistoricalPair(repository);
        // Force an original-row refresh, then abort it after the extra-row update in the same transaction.
        Sql(repository, "UPDATE invoices SET upload_status = 2;");
        Sql(repository, "CREATE TRIGGER reject_repair BEFORE UPDATE ON invoices WHEN OLD.sent_at = '2026/09/18 10:10:10' BEGIN SELECT RAISE(ABORT, 'synthetic interruption'); END;");
        var before = Stored(repository);
        try { await Sync(repository, gateway).SyncRecentAsync(); throw new Exception("expected transaction failure"); }
        catch (SqliteException) { }
        Equal(before, Stored(repository));
        Equal(1, Backups(repository).Length);
        Sql(repository, "DROP TRIGGER reject_repair;");
        Equal(0, (await Sync(repository, gateway).SyncRecentAsync()).Problems.Count);
        Equal(1, repository.Invoices.LoadOrCreate().Count(record => record.InvoiceState == InvoiceStates.Failed));
        Equal(2, Backups(repository).Length);
    }

    private static async Task TestHistoricalCancellationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        var (_, _, gateway) = HistoricalPair(repository);
        using var cancellation = new CancellationTokenSource();
        gateway.OnQuery = cancellation.Cancel;
        var before = Stored(repository);
        try { await Sync(repository, gateway).SyncRecentAsync(cancellation.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Equal(before, Stored(repository));
        Equal(0, Backups(repository).Length);
    }

    private static Task TestSourceInferenceAsync()
    {
        Equal(InvoiceSources.Mo, InvoiceSourceInference.FromOrderId("66091800123456"));
        Equal(InvoiceSources.Coupang, InvoiceSourceInference.FromOrderId("115102696774269"));
        Equal(InvoiceSources.Digiwin, InvoiceSourceInference.FromOrderId("20260918001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("M20260918001-R2"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("20260230001"));
        Equal(InvoiceSources.Manual, InvoiceSourceInference.FromOrderId("OTHER"));
        return Task.CompletedTask;
    }

    private static Task TestPrefixFormulaAsync()
    {
        var prefix = TestOrderIdPrefix.ForDate(FixedDate);
        Equal(4, prefix.Length);
        Equal(prefix, TestOrderIdPrefix.ForDate(FixedDate));
        NotEqual(prefix, TestOrderIdPrefix.ForDate(FixedDate.AddDays(1)));
        var apiOrderId = TestOrderIdPrefix.Apply("66091800123456", FixedDate);
        Equal(true, TestOrderIdPrefix.TryStripForDate(apiOrderId, FixedDate, out var raw));
        Equal("66091800123456", raw);
        Equal(false, TestOrderIdPrefix.TryStripForDate(apiOrderId, FixedDate.AddDays(1), out _));
        return Task.CompletedTask;
    }

    private static async Task TestIssueUsesTestPrefixAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        var rawOrderId = "CUSTOM-TEST-001";
        var apiOrderId = TestOrderIdPrefix.Apply(rawOrderId, FixedDate);
        var fake = new FakeGateway
        {
            IssueResultResponse = new IssueResponse(0, "", "TA12345678", FixedNow.ToUnixTimeSeconds(), "1234"),
            QueryResponse = Query("TA12345678", apiOrderId, "100", "測試消費者"),
        };
        var service = new InvoiceService(repository, (_, _) => fake, () => FixedNow);
        var draft = new InvoiceDraft
        {
            OrderId = rawOrderId,
            CompanyBuyer = false,
            BuyerIdentifier = string.Empty,
            BuyerName = "消費者",
            TotalAmount = 100,
        };
        draft.Items.Add(new InvoiceItem
        {
            Description = "測試商品", Quantity = 1, QuantityDecimal = "1", Unit = "個",
            UnitPrice = 100, UnitPriceDecimal = "100", Amount = 100, AmountDecimal = "100", TaxType = "1",
        });

        var result = await service.IssueManualWithLookupAsync(draft, new NameLookup());
        Equal(apiOrderId, fake.LastIssueOrderId);
        Equal(rawOrderId, result.Record.OrderId);
        Equal(rawOrderId, result.Record.OriginalOrderId);
        Equal(apiOrderId, result.Record.ApiOrderId);
        Equal(InvoiceSources.Manual, result.Record.Source);
    }

    private static async Task TestProductionDiscoveryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var fake = new FakeGateway
        {
            QueryResponse = Query("AA12345678", "66091800123456", "100", "測試買受人"),
        };
        fake.Pages[1] = ListResponse(1, [Remote("AA12345678", "66091800123456", "100")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, result.Inserted);
        Equal(1, result.Queried);
        Equal(1, fake.ListCalls);
        Equal(FixedDate.AddDays(-2), fake.LastListStart);
        Equal(FixedDate, fake.LastListEnd);
        Equal(1, fake.QueryCalls);
        var record = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Sync, record.RecordOrigin);
        Equal(InvoiceSources.Mo, record.Source);
        Equal(InvoiceSourceInference.SyncTag, InvoiceSourceInference.DisplayTag(record));
        Equal("12345675", record.SellerInvoice);
        Equal("AA12345678", record.InvoiceNumber);
        Equal("66091800123456", record.OrderId);
        Equal("測試買受人", record.BuyerName);
        Equal(100L, record.Amount);
        Equal("同步商品", record.Items.Single().Description);
    }

    private static async Task TestProductionOverwriteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(new InvoiceRecord
        {
            Id = "local-old", SellerInvoice = "12345675", RecordOrigin = RecordOrigins.Local,
            Environment = Environments.Production, Source = InvoiceSources.Manual,
            OriginalOrderId = "M20260918001", OrderId = "M20260918001", ApiOrderId = "M20260918001",
            InvoiceNumber = "BB12345678", InvoiceState = InvoiceStates.Opened,
            InvoiceDate = "2026/09/18", InvoiceTime = "09:00:00",
            BuyerIdentifier = "0000000000", BuyerName = "舊買受人", Amount = 80,
            Delivery = InvoiceService.DeliveryPaper, UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成", DetailVat = 1,
            Items = [new InvoiceItem { Description = "舊商品", Quantity = 1, UnitPrice = 80, Amount = 80 }],
        });

        var pdfDir = Path.Combine(repository.InvoicePdfCacheDirectory, Environments.Production, "20260918");
        var previewDir = Path.Combine(repository.InvoicePreviewCacheDirectory, Environments.Production, "20260918");
        Directory.CreateDirectory(pdfDir);
        Directory.CreateDirectory(previewDir);
        var pdf = Path.Combine(pdfDir, "BB12345678_style0.pdf");
        var preview = Path.Combine(previewDir, "BB12345678_style0.page1.png");
        File.WriteAllBytes(pdf, Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        File.WriteAllBytes(preview, [137, 80, 78, 71, 13, 10, 26, 10]);

        var fake = new FakeGateway
        {
            QueryResponse = Query("BB12345678", "66091800999999", "120", "新買受人"),
        };
        fake.Pages[1] = ListResponse(1, [Remote("BB12345678", "66091800999999", "120", buyer: "新買受人")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, result.Updated);
        var record = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Local, record.RecordOrigin);
        Equal(InvoiceSources.Mo, record.Source);
        Equal(InvoiceSourceInference.UpdateTag, InvoiceSourceInference.DisplayTag(record));
        Equal("66091800999999", record.OrderId);
        Equal("新買受人", record.BuyerName);
        Equal(120L, record.Amount);
        Equal("同步商品", record.Items.Single().Description);
        Equal(false, File.Exists(pdf));
        Equal(false, File.Exists(preview));
    }

    private static async Task TestUnmatchedAllowanceRestoresOriginalAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        const string number = "XO12345678";
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1,
            [new AllowanceListItem("A20260918008", [number])]);
        var official = Query(number, "66091800234567", "100", "買受人");
        fake.QueryByInvoice[number] = official with
        {
            Data = official.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                    "A20260918008", "20260918", "0", "15")],
                AllowanceSummaryPresent = true,
            },
        };
        // No local invoice exists until the allowance discovers it.
        Equal(0, repository.Invoices.LoadOrCreate().Count);
        var result = await new InvoiceAutomaticSyncService(repository,
            Sync(repository, fake), () => FixedNow).SyncAsync();
        Equal(0, result.Problems.Count);
        Equal(1, result.Queried);
        Equal(1, fake.QueryByInvoiceCalls);
        Equal(number, fake.LastInvoiceQuery);
        var restored = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Sync, restored.RecordOrigin);
        Equal(number, restored.InvoiceNumber);
        Equal(100L, restored.Amount);
        Equal(85L, InvoiceAllowanceMetadata.RemainingGross(restored).RoundInt64());
        Equal(1, restored.Items.Count);
        // Normal retention remains unchanged: the recovered old record is
        // subject to the same cutoff on later days.
    }

    private static async Task TestUnmatchedAllowanceNotFoundAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        const string number = "XY12345678";
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1,
            [new AllowanceListItem("A20260918009", [number])]);
        fake.QueryException = new AmegoApiException(71, "not found");
        var clock = FixedNow;
        var service = new InvoiceSyncService(repository, (_, _) => fake, () => clock);
        var automatic = new InvoiceAutomaticSyncService(repository, service, () => clock);
        var first = await automatic.SyncAsync();
        Equal(true, first.Problems.Any(problem => problem.Contains("查無原發票", StringComparison.Ordinal)));
        Equal(0, repository.Invoices.LoadOrCreate().Count);
        var issues = new InvoiceSyncIssueStore(repository.DataDirectory);
        Equal(true, issues.Unresolved(Environments.Production + "|12345675").Any(issue =>
            issue.InvoiceNumber == number && issue.IssueType == InvoiceSyncIssueTypes.RemoteNotFound &&
            issue.Message.Contains("查無原發票", StringComparison.Ordinal)));

        // Next daily reconciliation reads the exact same official invoice;
        // a valid response restores it and closes the old Upload Issues row.
        fake.QueryException = null;
        var official = Query(number, "66091800234568", "100", "買受人");
        fake.QueryByInvoice[number] = official with
        {
            Data = official.Data with { AllowanceSummaryPresent = true },
        };
        clock = clock.AddDays(1);
        var second = await automatic.SyncAsync();
        Equal(0, second.Problems.Count);
        Equal(1, repository.Invoices.LoadOrCreate().Count);
        Equal(false, issues.Unresolved(Environments.Production + "|12345675")
            .Any(issue => issue.InvoiceNumber == number && issue.IssueType == InvoiceSyncIssueTypes.RemoteNotFound));
    }

    private static async Task TestUnmatchedAllowanceMismatchedOriginalAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        const string target = "XZ12345678";
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1,
            [new AllowanceListItem("A20260918010", [target])]);
        // AMEGO returned a different invoice: never adopt it as the requested original.
        fake.QueryByInvoice[target] = Query("ZZ12345678", "66091800234569", "100", "買受人");
        var result = await new InvoiceAutomaticSyncService(repository,
            Sync(repository, fake), () => FixedNow).SyncAsync();
        Equal(true, result.Problems.Any(problem => problem.Contains("原發票", StringComparison.Ordinal)));
        Equal(0, repository.Invoices.LoadOrCreate().Count);
        Equal(true, new InvoiceSyncIssueStore(repository.DataDirectory)
            .Unresolved(Environments.Production + "|12345675").Any(issue =>
                issue.InvoiceNumber == target && issue.IssueType == InvoiceSyncIssueTypes.QueryFailed));
    }

    private static async Task TestHistoricalAllowanceDiscoveryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("AF12345678", "66091800770000");
        original.InvoiceDate = "2026/08/01"; // outside the recent 3-day invoice_list window
        repository.Invoices.Append(original);
        var cache = CreatePdfCache(repository, original.InvoiceNumber);
        var remote = Query(original.InvoiceNumber, original.ApiOrderId, "100", "買受人");
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1,
            [new AllowanceListItem("A20260918001", [original.InvoiceNumber])]);
        fake.QueryByInvoice[original.InvoiceNumber] = remote with
        {
            Data = remote.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete,
                    2, "A20260918001", "20260918", "0", "25")],
                AllowanceSummaryPresent = true,
            },
        };
        var automatic = new InvoiceAutomaticSyncService(repository, Sync(repository, fake), () => FixedNow);
        var result = await automatic.SyncAsync();
        Equal(0, result.Problems.Count);
        Equal(1, result.Queried);
        Equal(1, fake.QueryByInvoiceCalls);
        Equal(1, fake.AllowanceListCalls);
        Equal(new DateOnly(2026, 9, 16), fake.AllowanceListStart);
        var refreshed = repository.Invoices.LoadOrCreate().Single();
        Equal(75L, InvoiceAllowanceMetadata.RemainingGross(refreshed).RoundInt64());
        Equal(false, File.Exists(cache));
        // The same day only needs recent invoice_list, not another allowance_list.
        await automatic.SyncAsync();
        Equal(1, fake.AllowanceListCalls);
    }

    private static async Task TestAllowanceDiscoveryLimitAsync()
    {
        using var temporary=new TemporaryDirectory();
        var repository=TestRepository.Open(temporary.Path,new TestProtector());ConfigureProduction(repository);
        var fake=new FakeGateway();fake.Pages[1]=ListResponse(1,[]);
        var numbers=Enumerable.Range(1,51).Select(i=>$"ZZ{i:00000000}").ToArray();
        fake.AllowancePages[1]=new AllowanceListResponse(1,[new AllowanceListItem("A20260918002",numbers)]);
        foreach(var number in numbers)
        {
            var response=Query(number,"66091800"+number[2..],"100","合成買方");
            fake.QueryByInvoice[number]=response with{Data=response.Data with{AllowanceSummaryPresent=true}};
        }
        var clock=FixedNow;
        var service=new InvoiceSyncService(repository,(_,_)=>fake,()=>clock);
        var automatic=new InvoiceAutomaticSyncService(repository,service,()=>clock);
        await automatic.SyncAsync();
        Equal(50,fake.QueryByInvoiceCalls);Equal(1,fake.AllowanceListCalls);
        var issues=new InvoiceSyncIssueStore(repository.DataDirectory);
        Equal(1,issues.Unresolved(Environments.Production+"|12345675").Count(i=>i.IssueType==InvoiceSyncIssueTypes.AllowanceLimitExceeded));
        await automatic.SyncAsync();Equal(1,fake.AllowanceListCalls);
        clock=clock.AddDays(1);await automatic.SyncAsync();
        Equal(1,fake.AllowanceListCalls); // Resume the pending original, not the list.
        Equal(51,repository.Invoices.LoadOrCreate().Count);
        Equal(0,issues.Unresolved(Environments.Production+"|12345675").Count(i=>i.IssueType==InvoiceSyncIssueTypes.AllowanceLimitExceeded));

        using var pagesTemporary=new TemporaryDirectory();
        var pagesRepository=TestRepository.Open(pagesTemporary.Path,new TestProtector());ConfigureProduction(pagesRepository);
        var pages=new FakeGateway();pages.Pages[1]=ListResponse(1,[]);
        for(var page=1;page<=4;page++)pages.AllowancePages[page]=new(4,Enumerable.Range(1,page==4?1:50)
            .Select(i=>new AllowanceListItem($"A{page:00000000}{i:000}",[numbers[0]])).ToArray());
        pages.QueryByInvoice[numbers[0]]=fake.QueryByInvoice[numbers[0]];
        var pageClock=FixedNow;
        var daily=new InvoiceAutomaticSyncService(pagesRepository,new InvoiceSyncService(pagesRepository,(_,_)=>pages,()=>pageClock),()=>pageClock);
        await daily.SyncAsync();Equal(3,pages.AllowanceListCalls);
        await daily.SyncAsync();Equal(3,pages.AllowanceListCalls);
        pageClock=pageClock.AddDays(1);await daily.SyncAsync();Equal(4,pages.AllowanceListCalls);
    }

    private static async Task TestAllowanceListCapacityAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var invoice = MatchingRecord("AK12345678", "66091800770010");
        repository.Invoices.Append(invoice);
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        for (var page = 1; page <= 3; page++)
            fake.AllowancePages[page] = new AllowanceListResponse(3,
                Enumerable.Range(1, 50).Select(index =>
                    new AllowanceListItem($"A{page:00}{index:000}", [invoice.InvoiceNumber])).ToArray());
        var query = Query(invoice.InvoiceNumber, invoice.ApiOrderId, "100", "買受人");
        fake.QueryByInvoice[invoice.InvoiceNumber] = query with
        {
            Data = query.Data with { AllowanceSummaryPresent = true },
        };
        var result = await new InvoiceAutomaticSyncService(repository,
            Sync(repository, fake), () => FixedNow).SyncAsync();
        Equal(0, result.Problems.Count);
        Equal(3, fake.AllowanceListCalls);
        Equal(50, fake.LastAllowancePageSize);
        Equal(1, fake.QueryByInvoiceCalls);
        Equal(0, new InvoiceSyncIssueStore(repository.DataDirectory)
            .Unresolved(Environments.Production + "|12345675").Count);
    }

    private static async Task TestExistingAllowanceCancellationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var old = MatchingRecord("AZ12345678", "66091800770011");
        old.InvoiceDate = "2026/08/01";
        InvoiceAllowanceMetadata.ApplyQuery(old,
            [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                "A20260801001", "20260801", "0", "20")], true);
        repository.Invoices.Append(old);
        var pdf = CreatePdfCache(repository, old.InvoiceNumber);
        var query = Query(old.InvoiceNumber, old.ApiOrderId, "100", "買受人");
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1, []); // no new allowance
        fake.QueryByInvoice[old.InvoiceNumber] = query with
        {
            Data = query.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                    "A20260801001", "20260801", "0", "20", CancelDate: 20260918)],
                AllowanceSummaryPresent = true,
            },
        };
        var result = await new InvoiceAutomaticSyncService(repository,
            Sync(repository, fake), () => FixedNow).SyncAsync();
        Equal(0, result.Problems.Count);
        Equal(1, result.Queried);
        Equal(1, fake.QueryByInvoiceCalls);
        var updated = repository.Invoices.LoadOrCreate().Single();
        Equal(100L, InvoiceAllowanceMetadata.RemainingGross(updated).RoundInt64());
        Equal(false, File.Exists(pdf));
    }

    private static async Task TestAllowanceHistorySyncAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var query = Query("AL12345678", "66091800880000", "100", "買受人");
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, [Remote("AL12345678", "66091800880000", "100")]);
        fake.QueryByInvoice["AL12345678"] = query with
        {
            Data = query.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                    "A20260918001", "20260918", "0", "20")],
                AllowanceSummaryPresent = true,
            },
        };
        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, result.Queried);
        var saved = repository.Invoices.LoadOrCreate().Single();
        Equal(true, InvoiceAllowanceMetadata.HasCompleteSummary(saved));
        Equal("A20260918001", InvoiceAllowanceMetadata.ReadOfficial(saved).Single().AllowanceNumber);
        Equal(80L, InvoiceAllowanceMetadata.RemainingGross(saved).RoundInt64());
        // Missing allowance[] is not the same as an authoritative empty history.
        fake.QueryByInvoice["AL12345678"] = query;
        var incomplete = await DetailRefresh(repository, fake).RefreshAsync(saved);
        Equal(false, InvoiceAllowanceMetadata.HasCompleteSummary(incomplete));
        Equal("A20260918001", InvoiceAllowanceMetadata.ReadOfficial(incomplete).Single().AllowanceNumber);
        try
        {
            _ = InvoiceAllowanceMetadata.RemainingGross(incomplete);
            throw new InvalidOperationException("missing official allowance summary was treated as complete");
        }
        catch (InvalidOperationException error)
            when (error.Message.Contains("光貿尚未提供", StringComparison.Ordinal)) { }
    }

    private static async Task TestUnchangedSkipsQueryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(MatchingRecord("CC12345678", "66091800111111"));
        var fake = new FakeGateway { QueryException = new InvalidOperationException("query must not be called") };
        fake.Pages[1] = ListResponse(1, [Remote("CC12345678", "66091800111111", "100")]);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(0, result.Queried);
        Equal(0, fake.QueryCalls);
        Equal(1, result.Unchanged);
    }

    private static async Task TestTestEnvironmentPrefixDiscoveryAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        var prefix = TestOrderIdPrefix.ForDate(FixedDate);
        var ours = prefix + "66091800123456";
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1,
        [
            Remote("DD12345678", ours, "100", buyer: "測試消費者"),
            Remote("ZZ12345678", "OTHER-USER-ORDER", "999", buyer: "其他測試者"),
        ]);
        fake.QueryByInvoice["DD12345678"] = Query("DD12345678", ours, "100", "測試消費者");

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, fake.ListCalls);
        Equal(FixedDate, fake.LastListStart);
        Equal(FixedDate, fake.LastListEnd);
        Equal(1, result.RemoteCount);
        Equal(1, result.Inserted);
        Equal(1, result.Queried);
        Equal(1, repository.Invoices.LoadOrCreate().Count);
        var record = repository.Invoices.LoadOrCreate().Single();
        Equal(RecordOrigins.Sync, record.RecordOrigin);
        Equal("66091800123456", record.OrderId);
        Equal("66091800123456", record.OriginalOrderId);
        Equal(ours, record.ApiOrderId);
        Equal(InvoiceSources.Mo, record.Source);
        Equal(InvoiceSourceInference.SyncTag, InvoiceSourceInference.DisplayTag(record));
    }

    private static async Task TestTestEnvironmentSkipsFailedAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        repository.Invoices.Append(new InvoiceRecord
        {
            Id = "test-failed", Environment = Environments.Test, SellerInvoice = AmegoDefaults.TestInvoice,
            RecordOrigin = RecordOrigins.Local, Source = InvoiceSources.Manual,
            OriginalOrderId = "CUSTOM-FAILED", OrderId = "CUSTOM-FAILED", ApiOrderId = TestOrderIdPrefix.Apply("CUSTOM-FAILED", FixedDate),
            InvoiceState = InvoiceStates.Failed, InvoiceDate = "2026/09/18", InvoiceTime = "10:00:00",
            BuyerName = "測試消費者", Amount = 100, Delivery = InvoiceService.DeliveryPaper,
            ErrorMessage = "光貿明確拒絕開立",
            Items = [new InvoiceItem { Description = "測試商品", Quantity = 1, UnitPrice = 100, Amount = 100 }],
        });
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(1, fake.ListCalls);
        Equal(FixedDate, fake.LastListStart);
        Equal(FixedDate, fake.LastListEnd);
        Equal(0, fake.QueryCalls);
        Equal(0, result.Queried);
        Equal(0, result.Problems.Count);
        Equal(0, new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved("test|12345678").Count);
    }

    private static async Task TestRemoteAbsenceDoesNotDeleteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        repository.Invoices.Append(MatchingRecord("EE12345678", "66091800222222"));
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(1, []);

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(0, result.RemoteCount);
        Equal(1, repository.Invoices.LoadOrCreate().Count);
    }

    private static async Task TestPaginationAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var fake = new FakeGateway();
        fake.Pages[1] = ListResponse(2, [Remote("FF12345678", "66091800333333", "100")]);
        fake.Pages[2] = ListResponse(2, [Remote("GG12345678", "66091800444444", "100")]);
        fake.QueryByInvoice["FF12345678"] = Query("FF12345678", "66091800333333", "100", "買受人");
        fake.QueryByInvoice["GG12345678"] = Query("GG12345678", "66091800444444", "100", "買受人");

        var result = await Sync(repository, fake).SyncRecentAsync();
        Equal(2, fake.ListCalls);
        Equal(2, result.RemoteCount);
        Equal(2, result.Inserted);
    }

    private static async Task TestDetailRefreshAlwaysQueriesAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("HH12345678", "66091800555555");
        repository.Invoices.Append(original);
        var cache = CreatePdfCache(repository, original.InvoiceNumber);
        var fake = new FakeGateway
        {
            QueryResponse = Query(
                "HH12345678", "66091800555555", "100", "買受人",
                description: "商品", buyerIdentifier: "0000000000"),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(1, fake.QueryByInvoiceCalls);
        Equal(0, fake.QueryByOrderCalls);
        Equal("HH12345678", fake.LastInvoiceQuery);
        Equal("HH12345678", fresh.InvoiceNumber);
        Equal(true, File.Exists(cache));
    }

    private static async Task TestHistoricalAllowanceRetryIssueAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var old = MatchingRecord("AQ12345678", "66091800123002");
        InvoiceAllowanceMetadata.ApplyQuery(old,
            [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                "A20260918032", "20260918", "0", "10")], true);
        repository.Invoices.Append(old);
        var fake = new FakeGateway { QueryException = new IOException("temporary timeout") };
        fake.Pages[1] = ListResponse(1, []);
        fake.AllowancePages[1] = new AllowanceListResponse(1, []);
        var now = FixedNow;
        var sync = new InvoiceSyncService(repository, (_, _) => fake, () => now);
        var automatic = new InvoiceAutomaticSyncService(repository, sync, () => now);
        var issues = new InvoiceSyncIssueStore(repository.DataDirectory);
        var first = await automatic.SyncAsync();
        Equal(true, first.Problems.Any(value => value.Contains("折讓原發票查回失敗", StringComparison.Ordinal)));
        Equal(true, issues.Unresolved(Environments.Production + "|12345675").Any(issue =>
            issue.InvoiceNumber == old.InvoiceNumber && issue.IssueType == InvoiceSyncIssueTypes.QueryFailed));

        fake.QueryException = null;
        var provider = Query(old.InvoiceNumber, old.ApiOrderId, "100", "買受人");
        fake.QueryByInvoice[old.InvoiceNumber] = provider with
        {
            Data = provider.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                    "A20260918032", "20260918", "0", "10", CancelDate: 20260919)],
                AllowanceSummaryPresent = true,
            },
        };
        now = now.AddDays(1);
        var second = await automatic.SyncAsync();
        Equal(0, second.Problems.Count);
        Equal(false, issues.Unresolved(Environments.Production + "|12345675").Any(issue =>
            issue.InvoiceNumber == old.InvoiceNumber && issue.IssueType == InvoiceSyncIssueTypes.QueryFailed));
        Equal(100L, InvoiceAllowanceMetadata.RemainingGross(repository.Invoices.LoadOrCreate().Single()).RoundInt64());
    }

    private static async Task TestOfficialAllowanceAuditAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("AH12345678", "66091800123001");
        repository.Invoices.Append(original);
        var query = Query(original.InvoiceNumber, original.ApiOrderId, "100", "買受人");
        QueryResponse Official(string total, long cancelDate = 0) => query with
        {
            Data = query.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete, 2,
                    "A20260918031", "20260918", "0", total, cancelDate)],
                AllowanceSummaryPresent = true,
            },
        };
        var fake = new FakeGateway { QueryResponse = Official("10") };
        var refresher = DetailRefresh(repository, fake);
        var audit = new OfficialAllowanceHistoryStore(repository.DataDirectory);
        Equal(0, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);
        await refresher.RefreshAsync(original);
        Equal(1, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);
        await refresher.RefreshAsync(original);
        Equal(1, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);

        fake.QueryResponse = Official("20");
        await refresher.RefreshAsync(original);
        Equal(2, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);
        fake.QueryResponse = Official("20", cancelDate: 20260919);
        await refresher.RefreshAsync(original);
        var sequence = audit.Read("12345675", Environments.Production, original.InvoiceNumber);
        Equal(3, sequence.Count);
        Equal(20260919L, sequence[0].Snapshot.CancelDate);
        Equal("10", sequence[2].Snapshot.TotalAmount);
        // Same value can recur after another official state and still be recorded.
        fake.QueryResponse = Official("10");
        await refresher.RefreshAsync(original);
        Equal(4, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);

        // Normal old invoice retention behavior MUST remain unchanged.
        var retention = new InvoiceRetentionService(repository).Prune(new DateOnly(2027, 3, 5));
        Equal(1, retention.Deleted);
        Equal(0, repository.Invoices.LoadOrCreate().Count);
        Equal(4, audit.Read("12345675", Environments.Production, original.InvoiceNumber).Count);
    }

    private static async Task TestAllowanceOnlyRefreshInvalidatesCacheAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("AZ12345678", "66091800990000");
        repository.Invoices.Append(original);
        var query = Query(original.InvoiceNumber, original.ApiOrderId, "100", "買受人");
        QueryResponse Official(string amount) => query with
        {
            Data = query.Data with
            {
                Allowances = [new InvoiceAllowanceResult("D0401", UploadStatuses.Complete,
                    2, "A20260918001", "20260918", "0", amount)],
                AllowanceSummaryPresent = true,
            },
        };
        var fake = new FakeGateway { QueryResponse = Official("10") };
        var service = DetailRefresh(repository, fake);
        var first = await service.RefreshAsync(original);
        Equal(90L, InvoiceAllowanceMetadata.RemainingGross(first).RoundInt64());
        var pdf = CreatePdfCache(repository, original.InvoiceNumber);
        var preview = CreatePreviewCache(repository, original.InvoiceNumber);
        fake.QueryResponse = Official("20");
        var second = await service.RefreshAsync(first);
        Equal(80L, InvoiceAllowanceMetadata.RemainingGross(second).RoundInt64());
        Equal(false, File.Exists(pdf));
        Equal(false, File.Exists(preview));
        Equal(2, fake.QueryByInvoiceCalls);
    }

    private static async Task TestDetailRefreshOverwriteAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("JJ12345678", "M20260918001");
        original.Source = InvoiceSources.Manual;
        original.OriginalOrderId = "M20260918001";
        repository.Invoices.Append(original);
        var pdf = CreatePdfCache(repository, original.InvoiceNumber);
        var preview = CreatePreviewCache(repository, original.InvoiceNumber);
        var fake = new FakeGateway
        {
            QueryResponse = Query(
                "JJ12345678", "66091800666666", "120", "",
                cancelDate: 1, description: "新商品", buyerIdentifier: ""),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(RecordOrigins.Local, fresh.RecordOrigin);
        Equal("M20260918001", fresh.OriginalOrderId);
        Equal("66091800666666", fresh.OrderId);
        Equal("66091800666666", fresh.ApiOrderId);
        Equal(InvoiceSources.Mo, fresh.Source);
        Equal(InvoiceSourceInference.UpdateTag, InvoiceSourceInference.DisplayTag(fresh));
        Equal(InvoiceStates.Voided, fresh.InvoiceState);
        Equal(string.Empty, fresh.BuyerIdentifier);
        Equal(string.Empty, fresh.BuyerName);
        Equal(120L, fresh.Amount);
        Equal("新商品", fresh.Items.Single().Description);
        Equal(false, File.Exists(pdf));
        Equal(false, File.Exists(preview));
    }

    private static async Task TestTestDetailRefreshStripsPrefixAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureTest(repository);
        var rawOrderId = "66091800666666";
        var apiOrderId = TestOrderIdPrefix.Apply(rawOrderId, FixedDate);
        var original = MatchingRecord("TM12345678", rawOrderId);
        original.Environment = Environments.Test;
        original.SellerInvoice = AmegoDefaults.TestInvoice;
        original.ApiOrderId = apiOrderId;
        repository.Invoices.Append(original);
        var fake = new FakeGateway
        {
            QueryResponse = Query("TM12345678", apiOrderId, "100", "測試消費者"),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(rawOrderId, fresh.OrderId);
        Equal(apiOrderId, fresh.ApiOrderId);
        Equal(InvoiceSources.Mo, fresh.Source);
    }

    private static async Task TestDetailRefreshFailureLeavesCacheAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("KK12345678", "66091800777777");
        repository.Invoices.Append(original);
        var fake = new FakeGateway { QueryException = new InvalidOperationException("forced detail query failure") };

        try
        {
            await DetailRefresh(repository, fake).RefreshAsync(original);
            throw new InvalidOperationException("expected detail refresh to fail");
        }
        catch (InvalidOperationException error) when (error.Message == "forced detail query failure")
        {
        }

        var stored = repository.Invoices.LoadOrCreate().Single();
        Equal("KK12345678", stored.InvoiceNumber);
        Equal("66091800777777", stored.OrderId);
        Equal("買受人", stored.BuyerName);
        Equal(100L, stored.Amount);
        Equal("商品", stored.Items.Single().Description);
    }

    private static async Task TestDetailRefreshByOrderIdAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var original = MatchingRecord("LL12345678", "M20260918002");
        original.InvoiceNumber = string.Empty;
        original.InvoiceState = InvoiceStates.Unknown;
        repository.Invoices.Append(original);
        var fake = new FakeGateway
        {
            QueryResponse = Query("LL12345678", "M20260918002", "100", "買受人", description: "商品", buyerIdentifier: "0000000000"),
        };

        var fresh = await DetailRefresh(repository, fake).RefreshAsync(original);
        Equal(0, fake.QueryByInvoiceCalls);
        Equal(1, fake.QueryByOrderCalls);
        Equal("M20260918002", fake.LastOrderQuery);
        Equal("LL12345678", fresh.InvoiceNumber);
        Equal(InvoiceStates.Opened, fresh.InvoiceState);
    }

    private static async Task TestFailedDetailRefreshAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var failed = MatchingRecord(string.Empty, "M20260918002");
        failed.InvoiceState = InvoiceStates.Failed;
        failed.UploadStatus = 0;
        failed.ErrorMessage = "API code 3040171: OrderId duplicate";
        repository.Invoices.Append(failed);
        var fake = new FakeGateway
        {
            QueryResponse = Query("LL12345678", failed.OrderId, "100", "買受人", description: "商品", buyerIdentifier: "0000000000"),
        };
        try
        {
            await DetailRefresh(repository, fake).RefreshAsync(failed);
            throw new InvalidDataException("failed attempt was queried and promoted");
        }
        catch (InvalidOperationException error)
        {
            Equal(true, error.Message.Contains("開立失敗", StringComparison.Ordinal));
        }
        Equal(0, fake.QueryCalls);
        var stored = repository.Invoices.LoadOrCreate().Single();
        Equal(InvoiceStates.Failed, stored.InvoiceState);
        Equal(string.Empty, stored.InvoiceNumber);
        Equal(failed.ErrorMessage, stored.ErrorMessage);
    }

    private static async Task TestFailedOrderMatchAsync()
    {
        using var temporary = new TemporaryDirectory();
        var repository = TestRepository.Open(temporary.Path, new TestProtector());
        ConfigureProduction(repository);
        var failed = MatchingRecord(string.Empty, "M20260918002");
        failed.InvoiceState = InvoiceStates.Failed;
        failed.UploadStatus = 0;
        failed.ErrorMessage = "API code 3040171: OrderId duplicate";
        repository.Invoices.Append(failed);
        var fake = new FakeGateway
        {
            QueryResponse = Query("LL12345678", failed.OrderId, "100", "買受人", description: "商品", buyerIdentifier: "0000000000"),
        };
        fake.Pages[1] = ListResponse(1, [Remote("LL12345678", failed.OrderId, "100")]);
        await Sync(repository, fake).SyncRecentAsync();
        var rows = repository.Invoices.LoadOrCreate();
        Equal(2, rows.Count);
        Equal(InvoiceStates.Failed, rows.Single(record => record.Id == failed.Id).InvoiceState);
        Equal(string.Empty, rows.Single(record => record.Id == failed.Id).InvoiceNumber);
        Equal(failed.ErrorMessage, rows.Single(record => record.Id == failed.Id).ErrorMessage);
        var official = rows.Single(record => record.Id != failed.Id);
        Equal(InvoiceStates.Opened, official.InvoiceState);
        Equal("LL12345678", official.InvoiceNumber);
        await Sync(repository, fake).SyncRecentAsync();
        Equal(2, repository.Invoices.LoadOrCreate().Count);
    }

    private static InvoiceSyncService Sync(LocalRepository repository, FakeGateway fake) => new(
        repository,
        (_, _) => fake,
        () => FixedNow);

    private static InvoiceDetailRefreshService DetailRefresh(LocalRepository repository, FakeGateway fake) => new(
        repository,
        (_, _) => fake,
        () => FixedNow);

    private static void ConfigureProduction(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345675";
        repository.Settings.SetProductionAppKey(settings, "TEST-APP-KEY-NOT-REAL");
        repository.Settings.Save(settings);
    }

    private static void ConfigureTest(LocalRepository repository)
    {
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Test;
        repository.Settings.Save(settings);
    }

    private static InvoiceRecord MatchingRecord(string invoiceNumber, string orderId) => new()
    {
        Id = "match-" + (invoiceNumber.Length == 0 ? orderId : invoiceNumber), SellerInvoice = "12345675", Environment = Environments.Production,
        RecordOrigin = RecordOrigins.Local, Source = InvoiceSourceInference.FromOrderId(orderId), OriginalOrderId = orderId,
        OrderId = orderId, ApiOrderId = orderId, InvoiceNumber = invoiceNumber, InvoiceState = InvoiceStates.Opened,
        InvoiceDate = "2026/09/18", InvoiceTime = "10:10:10", BuyerIdentifier = "0000000000",
        BuyerName = "買受人", Amount = 100, Delivery = InvoiceService.DeliveryPaper,
        UploadStatus = UploadStatuses.Complete, UploadStatusText = "完成", MainRemark = "備註", DetailVat = 1,
        Items = [new InvoiceItem
        {
            Description = "商品", Quantity = 1, QuantityDecimal = "1", Unit = "個",
            UnitPrice = 100, UnitPriceDecimal = "100", TaxType = "1",
            Amount = 100, AmountDecimal = "100", Remark = string.Empty,
        }],
    };

    private static InvoiceListItem Remote(
        string invoiceNumber,
        string orderId,
        string total,
        int status = UploadStatuses.Complete,
        string buyer = "買受人") => new(
            invoiceNumber, "07", status, "20260918", "101010", "0000000000", buyer,
            total, "0", total, "備註", "", "", "", "", 0, orderId, 0);

    private static InvoiceListResponse ListResponse(int pageTotal, IReadOnlyList<InvoiceListItem> data) =>
        new(0, "", pageTotal, 1, data.Count, data);

    private static QueryResponse Query(
        string invoiceNumber,
        string orderId,
        string total,
        string buyer,
        long cancelDate = 0,
        string description = "同步商品",
        string buyerIdentifier = "0000000000") => new(
        0,
        "",
        new QueryResult(
            invoiceNumber, "07", UploadStatuses.Complete, "20260918", "101010",
            buyerIdentifier, buyer, total, "0", total, "", "", "", "", cancelDate,
            orderId, 0, ProductItems(description, total), 1, true));

    private static JsonElement ProductItems(string description = "同步商品", string amount = "100")
    {
        using var document = JsonDocument.Parse($$"""
            [{"Description":{{JsonSerializer.Serialize(description)}},"Quantity":1,"Unit":"個","UnitPrice":{{amount}},"Amount":{{amount}},"TaxType":1,"Remark":""}]
            """);
        return document.RootElement.Clone();
    }

    private static string CreatePdfCache(LocalRepository repository, string invoiceNumber)
    {
        var directory = Path.Combine(repository.InvoicePdfCacheDirectory, Environments.Production, "20260918");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, invoiceNumber + "_style0.pdf");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        return path;
    }

    private static string CreatePreviewCache(LocalRepository repository, string invoiceNumber)
    {
        var directory = Path.Combine(repository.InvoicePreviewCacheDirectory, Environments.Production, "20260918");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, invoiceNumber + "_style0.page1.png");
        File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10]);
        return path;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void NotEqual<T>(T left, T right)
    {
        if (EqualityComparer<T>.Default.Equals(left, right))
            throw new InvalidOperationException($"expected values to differ, both were {left}");
    }

    private sealed class FakeGateway : IAmegoGateway
    {
        public Dictionary<int, InvoiceListResponse> Pages { get; } = [];
        public Dictionary<int, AllowanceListResponse> AllowancePages { get; } = [];
        public int AllowanceListCalls { get; private set; }
        public DateOnly? AllowanceListStart { get; private set; }
        public int LastAllowancePageSize { get; private set; }
        public Dictionary<string, QueryResponse> QueryByInvoice { get; } = new(StringComparer.OrdinalIgnoreCase);
        public QueryResponse QueryResponse { get; set; } = null!;
        public IssueResponse IssueResultResponse { get; set; } = new(0, "", "TT12345678", FixedNow.ToUnixTimeSeconds(), "1234");
        public Exception? QueryException { get; set; }
        public Action? OnQuery { get; set; }
        public bool ThrowIfListCalled { get; set; }
        public int ListCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int QueryByInvoiceCalls { get; private set; }
        public int QueryByOrderCalls { get; private set; }
        public string LastInvoiceQuery { get; private set; } = string.Empty;
        public string LastOrderQuery { get; private set; } = string.Empty;
        public string LastIssueOrderId { get; private set; } = string.Empty;
        public DateOnly? LastListStart { get; private set; }
        public DateOnly? LastListEnd { get; private set; }

        public Task<InvoiceListResponse> ListInvoicesAsync(DateOnly startDate, DateOnly endDate, int page = 1, int limit = 500, CancellationToken cancellationToken = default)
        {
            if (ThrowIfListCalled) throw new InvalidOperationException("invoice_list must not be called");
            ListCalls++;
            LastListStart = startDate;
            LastListEnd = endDate;
            return Task.FromResult(Pages.TryGetValue(page, out var response)
                ? response
                : new InvoiceListResponse(0, "", page, page, 0, []));
        }

        public Task<AllowanceListResponse> ListAllowancesAsync(
            DateOnly startDate, DateOnly endDate, int page = 1, int limit = 20,
            CancellationToken cancellationToken = default)
        {
            AllowanceListCalls++;
            AllowanceListStart = startDate;
            LastAllowancePageSize = limit;
            return Task.FromResult(AllowancePages.TryGetValue(page, out var response)
                ? response : new AllowanceListResponse(1, []));
        }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            QueryByInvoiceCalls++;
            LastInvoiceQuery = number;
            OnQuery?.Invoke();
            if (QueryException is not null) return Task.FromException<QueryResponse>(QueryException);
            if (QueryByInvoice.TryGetValue(number, out var response)) return Task.FromResult(response);
            return Task.FromResult(QueryResponse);
        }

        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            QueryByOrderCalls++;
            LastOrderQuery = orderId;
            return QueryException is null ? Task.FromResult(QueryResponse) : Task.FromException<QueryResponse>(QueryException);
        }

        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default)
        {
            LastIssueOrderId = request.OrderId;
            return Task.FromResult(IssueResultResponse);
        }
        public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StatusResponse(0, "", []));
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BanResponse(0, "", []));
        public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new NotSupportedException());
    }

    private sealed class TestProtector : ISecretProtector
    {
        private const string Prefix = "test-protected:";
        public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
        public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(ciphertext[Prefix.Length..])
            : throw new InvalidDataException("invalid protected test value");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-sync-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
