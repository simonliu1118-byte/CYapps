using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class AllowanceWorkflowTests
{
    public static async Task WrongCredentialsMakeZeroQueryAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        await ThrowsAsync<InvalidOperationException>(() => setup.Workflow.SubmitAsync(
            setup.Record, "3015", "wrong", "部分退貨", 50, editedItems:DraftItems()));
        Equal(0, setup.Gateway.QueryCalls);
    }

    public static async Task EmployeeQueuesManualReviewAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "OLD-001", tax: "2", total: "18"),
        ]));

        var result = await setup.Workflow.SubmitAsync(
            setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());

        Equal(false, result.AlreadyQueued);
        Equal("3015", result.Review.RequesterEmployeeNo);
        Equal("部分退貨", result.Review.Reason);
        Equal(50L, result.Review.TaxInclusiveAmount);
        Equal(false, result.Review.AwaitingConfirmation);
        Equal(1, result.Review.BaselineAllowanceNumbers.Length);
        Equal("OLD-001", result.Review.BaselineAllowanceNumbers[0]);
        Equal(1, setup.Gateway.QueryCalls);
        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal("OLD-001", InvoiceAllowanceMetadata.ReadOfficial(stored).Single().AllowanceNumber);
        var issue = AllowanceIssue(setup.Repository);
        Equal(InvoiceAllowanceIssueTypes.ManualReview, issue.IssueType);
        Equal(null, issue.ResolvedUtc);
    }

    public static async Task AdministratorCancelsManualReviewAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
        var issue = AllowanceIssue(setup.Repository);

        setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");

        Equal(1, setup.Gateway.QueryCalls);
        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(null, setup.Workflow.ManualReviewFor(stored));
        Equal(false, HasPending(stored));
        var resolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).All(AccountKey())
            .Single(item => item.Id == issue.Id);
        Equal(true, resolved.ResolvedUtc is not null);
    }

    public static async Task AdministratorCompletesAndOfficialQueryConfirmsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        var oldAllowance = Allowance("D0401", UploadStatuses.Complete, "OLD-001", tax: "2", total: "18");
        var newAllowance = Allowance("D0401", UploadStatuses.Complete, "NEW-002", tax: "2", total: "48");
        setup.Gateway.Queries.Enqueue(Query([oldAllowance]));
        setup.Gateway.Queries.Enqueue(Query([oldAllowance, newAllowance]));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
        var issue = AllowanceIssue(setup.Repository);

        var result = await CheckSeededHistoricalAsync(setup,
            issue, "2000", "AdminPass1");

        Equal(InvoiceAllowanceReconcileOutcome.Confirmed, result.Outcome);
        Equal(2, setup.Gateway.QueryCalls);
        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(null, setup.Workflow.ManualReviewFor(stored));
        Equal(false, HasPending(stored));
        Equal(2, InvoiceAllowanceMetadata.ReadOfficial(stored).Count);
        Equal(true, InvoiceAllowanceMetadata.ReadOfficial(stored)
            .Any(item => item.AllowanceNumber == "NEW-002"));
        var resolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).All(AccountKey())
            .Single(item => item.Id == issue.Id);
        Equal(true, resolved.ResolvedUtc is not null);
    }

    public static async Task AmountMismatchRemainsPendingAsync()
    {
        using (var temporary = new AllowanceTemporaryDirectory())
        {
            var setup = CreateSetup(temporary.Path);
            var oldAllowance = Allowance("D0401", UploadStatuses.Complete, "OLD-001", tax: "2", total: "18");
            var wrongAmount = Allowance("D0401", UploadStatuses.Complete, "NEW-002", tax: "1", total: "48");
            setup.Gateway.Queries.Enqueue(Query([oldAllowance]));
            setup.Gateway.Queries.Enqueue(Query([oldAllowance, wrongAmount]));
            await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
            var issue = AllowanceIssue(setup.Repository);

            var result = await CheckSeededHistoricalAsync(setup, issue, "2000", "AdminPass1");

            Equal(InvoiceAllowanceReconcileOutcome.Problem, result.Outcome);
            var stored = setup.Repository.Invoices.LoadOrCreate().Single();
            Equal(true, HasPending(stored));
            Equal(true, setup.Workflow.ManualReviewFor(stored)?.AwaitingConfirmation);
            Equal("2000", setup.Workflow.ManualReviewFor(stored)?.HandlerEmployeeNo);
            var unresolved = new InvoiceSyncIssueStore(setup.Repository.DataDirectory).Unresolved(AccountKey())
                .Single(item => item.Id == issue.Id);
            Equal(true, unresolved.Message.Contains("金額", StringComparison.Ordinal));
        }

        using (var temporary = new AllowanceTemporaryDirectory())
        {
            var setup = CreateSetup(temporary.Path);
            var oldAllowance = Allowance("D0401", UploadStatuses.Complete, "OLD-001", tax: "2", total: "18");
            var candidateA = Allowance("D0401", UploadStatuses.Complete, "NEW-002", tax: "2", total: "48");
            var candidateB = Allowance("B0401", UploadStatuses.Complete, "NEW-003", tax: "2", total: "48");
            setup.Gateway.Queries.Enqueue(Query([oldAllowance]));
            setup.Gateway.Queries.Enqueue(Query([oldAllowance, candidateA, candidateB]));
            setup.Gateway.Queries.Enqueue(Query([oldAllowance, candidateA, candidateB]));
            await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
            var issue = AllowanceIssue(setup.Repository);

            var ambiguous = await CheckSeededHistoricalAsync(setup, issue, "2000", "AdminPass1");
            Equal(InvoiceAllowanceReconcileOutcome.Problem, ambiguous.Outcome);
            Equal(1, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
            Equal(0, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
            Equal(true, ambiguous.Message.Contains("多筆", StringComparison.Ordinal));
            Equal(true, HasPending(setup.Repository.Invoices.LoadOrCreate().Single()));

            var confirmed = await setup.Workflow.ConfirmCandidateAsync(
                issue, "NEW-003", "2000", "AdminPass1");
            Equal(InvoiceAllowanceReconcileOutcome.Confirmed, confirmed.Outcome);
            var stored = setup.Repository.Invoices.LoadOrCreate().Single();
            Equal(false, HasPending(stored));
            Equal(null, setup.Workflow.ManualReviewFor(stored));
            Equal("2000", setup.Workflow.HandledReviewFor(stored)?.HandlerEmployeeNo);
            Equal("3015", setup.Workflow.HandledReviewFor(stored)?.RequesterEmployeeNo);
            Equal("NEW-003", setup.Workflow.HandledReviewFor(stored)?.ConfirmedAllowanceNumber);
            Equal(3, InvoiceAllowanceMetadata.ReadOfficial(stored).Count);
        }
    }

    public static async Task PendingCannotBeCancelledAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        var pending = await CheckSeededHistoricalAsync(setup,
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.PendingConfirmation, pending.Outcome);
        Equal(0, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);

        Throws<InvalidOperationException>(() => setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1"));
        Equal(true, HasPending(setup.Repository.Invoices.LoadOrCreate().Single()));
    }

    public static async Task QueueFollowsOfficialConfirmationAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
        Equal(0, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
        var issue = AllowanceIssue(setup.Repository);
        await CheckSeededHistoricalAsync(setup, issue, "2000", "AdminPass1");
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
        // A missing gateway result fails the authoritative refresh, preserving pending work.
        var failed = await setup.Workflow.ReconcilePendingAsync(setup.Record);
        Equal(InvoiceAllowanceReconcileOutcome.Problem, failed.Outcome);
        Equal(0, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.ReconcilePendingAsync(setup.Record);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
        setup.Gateway.Queries.Enqueue(Query([Allowance("D0401", UploadStatuses.Complete, "NEW-002", tax: "2", total: "48")]));
        var completed = await setup.Workflow.ReconcilePendingAsync(setup.Record);
        Equal(InvoiceAllowanceReconcileOutcome.Confirmed, completed.Outcome);
        Equal(0, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);
        Equal(true, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Single().ResolvedUtc is not null);
    }

    public static async Task VoidIsBlockedWhileAllowanceRequestExistsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50, editedItems:DraftItems());
        var callsBefore = setup.Gateway.QueryCalls;

        await ThrowsAsync<InvalidOperationException>(() => setup.VoidWorkflow.SubmitAsync(
            setup.Repository.Invoices.LoadOrCreate().Single(),
            "AA12345678",
            "3015",
            "Employee1",
            "退貨",
            PaperInvoiceReceiptStates.Collected));

        Equal(callsBefore, setup.Gateway.QueryCalls);
        Equal(0, setup.Gateway.VoidCalls);
    }

    public static async Task LocalManagerMustAuthenticateBeforeOfficialPreviewAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        Equal(1, setup.Gateway.QueryCalls);
        await ThrowsAsync<UnauthorizedAccessException>(() =>
            setup.Workflow.PreviewLocalDraftAsync(issue, "3015", "Employee1"));
        await ThrowsAsync<UnauthorizedAccessException>(() =>
            setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "wrong"));
        Equal(1, setup.Gateway.QueryCalls);
        Equal(null, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())?.LocalApprovedUtc);
    }

    public static async Task LocalManagerApprovesFreshLockedDraftAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(issue,
            "2000", "AdminPass1");
        Equal("100", preview.OfficialRemainingGross);
        Equal(50L, preview.RequestedGross);
        Equal(0, preview.OfficialHistory.Count);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var approved = await setup.Workflow.ApproveLocalDraftAsync(issue,
            "2000", "AdminPass1", preview.OfficialSnapshotHash);
        Equal("2000", approved.LocalApproverEmployeeNo);
        Equal(true, approved.LocalApprovedUtc is not null);
        Equal(3, setup.Gateway.QueryCalls);
        var stored = setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())!;
        Equal(approved.LocalApprovedUtc, stored.LocalApprovedUtc);
        Equal(approved.LocalApprovalOfficialHash, stored.LocalApprovalOfficialHash);
        await ThrowsAsync<InvalidOperationException>(() =>
            CheckSeededHistoricalAsync(setup, issue, "2000", "AdminPass1"));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1"));
        Equal(3, setup.Gateway.QueryCalls);
        // B2 approval must never call a live provider issue API.
        Equal(0, setup.Gateway.VoidCalls);
        setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");
        var cancelled = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(true, setup.Workflow.ManualReviewFor(cancelled) is null);
        Equal(approved.LocalApprovedUtc, setup.Workflow.HandledReviewFor(cancelled)!.LocalApprovedUtc);
        Equal("2000", setup.Workflow.HandledReviewFor(cancelled)!.CancelledByEmployeeNo);
        Equal(true, setup.Workflow.HandledReviewFor(cancelled)!.CancelledUtc is not null);
    }

    public static async Task LocalReservedCancellationNeverSendsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        var (issue, reserved) = await PrepareReservedAsync(setup);
        setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");
        var ledger = new LocalAllowanceNumberStore(setup.Repository.DataDirectory);
        Throws<InvalidOperationException>(() => ledger.MarkSending(AmegoDefaults.TestInvoice, Environments.Test, reserved));
        Equal("reserved", ledger.FindLatest(AmegoDefaults.TestInvoice, Environments.Test, reserved.WorkItemId)!.Status);
        Equal(true, setup.Workflow.HandledReviewFor(setup.Repository.Invoices.LoadOrCreate().Single())!.CancelledUtc is not null);
    }

    public static async Task LocalOfficialChangedBeforeApprovalMustRePreviewAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var first = await setup.Workflow.PreviewLocalDraftAsync(issue,
            "2000", "AdminPass1");
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "NEW-001", "5", "45")
        ], summaryPresent: true));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.ApproveLocalDraftAsync(issue, "2000", "AdminPass1",
                first.OfficialSnapshotHash));
        var stored = setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())!;
        Equal(null, stored.LocalApprovedUtc);
        Equal(3, setup.Gateway.QueryCalls);
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "NEW-001", "5", "45")
        ], summaryPresent: true));
        var rePreview = await setup.Workflow.PreviewLocalDraftAsync(issue,
            "2000", "AdminPass1");
        Equal("50", rePreview.OfficialRemainingGross);
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "NEW-001", "5", "45")
        ], summaryPresent: true));
        var approved = await setup.Workflow.ApproveLocalDraftAsync(issue,
            "2000", "AdminPass1", rePreview.OfficialSnapshotHash);
        Equal(true, approved.LocalApprovedUtc is not null);
    }

    public static async Task LocalIncompleteOfficialSummaryPreventsApprovalAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: false));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1"));
        Equal(null, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())?.LocalApprovedUtc);
    }

    public static async Task LocalApprovedDraftReservesFrozenPayloadWithoutSendingAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1");
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var approval = await setup.Workflow.ApproveLocalDraftAsync(issue, "2000",
            "AdminPass1", preview.OfficialSnapshotHash);
        Equal(true, approval.LocalApprovalDraftHash.Length > 0);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var reserved = await setup.Workflow.ReserveApprovedDraftAsync(issue, "2000");
        Equal("A20260919001", reserved.AllowanceNumber);
        Equal("reserved", reserved.Status);
        Equal("2000", reserved.ApproverEmployeeNo);
        Equal(approval.LocalApprovedUtc, reserved.ApprovedAtUtc);
        Equal(4, setup.Gateway.QueryCalls);
        using (var json = JsonDocument.Parse(reserved.PayloadJson))
        {
            var obj = json.RootElement;
            Equal("A20260919001", obj.GetProperty("AllowanceNumber").GetString());
            Equal("20260919", obj.GetProperty("AllowanceDate").GetString());
            Equal(50m, obj.GetProperty("TotalAmount").GetDecimal());
            Equal(0m, obj.GetProperty("TaxAmount").GetDecimal());
            Equal(50m, obj.GetProperty("ProductItem")[0].GetProperty("UnitPrice").GetDecimal());
            Equal(false, obj.GetProperty("ProductItem")[0].TryGetProperty(
                "GrossUnitPrice", out _));
        }
        // A second manager action or app restart gets the identical number
        // without another AMEGO query or allocation.
        var same = await setup.Workflow.ReserveApprovedDraftAsync(issue, "2000");
        Equal(reserved, same);
        Equal(4, setup.Gateway.QueryCalls);
        var recovered = new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Equal(reserved, recovered);
        Equal(0, setup.Gateway.VoidCalls);
        setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1");
    }

    public static async Task LocalReservationRequiresApprovalAndSameManagerAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        await ThrowsAsync<UnauthorizedAccessException>(() =>
            setup.Workflow.ReserveApprovedDraftAsync(issue, "3015"));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.ReserveApprovedDraftAsync(issue, "2000"));
        Equal(1, setup.Gateway.QueryCalls);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1");
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.ApproveLocalDraftAsync(issue, "2000", "AdminPass1",
            preview.OfficialSnapshotHash);
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.ReserveApprovedDraftAsync(issue, "0001"));
        Equal(3, setup.Gateway.QueryCalls);
        var noReservation = new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Equal(null, noReservation);
    }

    public static async Task LocalOfficialChangeAfterApprovalBlocksFirstReservationAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1");
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.ApproveLocalDraftAsync(issue, "2000", "AdminPass1",
            preview.OfficialSnapshotHash);
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "OTHER-001", "1", "19")
        ], summaryPresent: true));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.ReserveApprovedDraftAsync(issue, "2000"));
        Equal(4, setup.Gateway.QueryCalls);
        Equal(null, new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static async Task LocalOneClickApprovalAndIssueAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(issue, "2000", "AdminPass1");
        // Approve, reserve and first send each perform a fresh official read.
        for (var i = 0; i < 3; i++)
            setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var fake = new FakeLocalIssueGateway();
        var progress = new List<string>();
        var result = await setup.Workflow.ApproveAndIssueLocalDraftAsync(
            issue, "2000", "AdminPass1", preview.OfficialSnapshotHash,
            progress.Add, gateway: fake);
        Equal(LocalAllowanceSendOutcome.Problem, result.Outcome);
        Equal(1, fake.IssueCalls);
        Equal(true, progress.Count >= 2);
        var ledger = new LocalAllowanceNumberStore(setup.Repository.DataDirectory);
        Equal("sending", ledger.FindLatest(AmegoDefaults.TestInvoice, Environments.Test,
            issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))!.Status);
        Throws<InvalidOperationException>(() => setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1"));
        var resumed = await setup.Workflow.ExecuteApprovedLocalDraftAsync(issue, fake);
        Equal(LocalAllowanceSendOutcome.Problem, resumed.Outcome);
        Equal(1, fake.IssueCalls);
    }

    // Full Local workflow + temporary SQLite + fake official gateway (no real HTTP).
    public static async Task LocalFirstSendAuthenticatesAndNeverRepostsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        var (issue, reserved) = await PrepareReservedAsync(setup);
        var gateway = new FakeLocalIssueGateway();
        await ThrowsAsync<UnauthorizedAccessException>(() =>
            setup.Workflow.ApproveAndIssueLocalDraftAsync(issue, "2000", "wrong", "not-an-approval", gateway: gateway));
        Equal(4, setup.Gateway.QueryCalls);
        Equal(0, gateway.IssueCalls);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var first = await setup.Workflow.ExecuteApprovedLocalDraftAsync(issue, gateway);
        Equal(LocalAllowanceSendOutcome.Problem, first.Outcome);
        Equal(reserved.AllowanceNumber, first.AllowanceNumber);
        Equal(5, setup.Gateway.QueryCalls);
        Equal(1, gateway.IssueCalls);
        Equal("sending", new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString())!.Status);
        var retryEntry = await setup.Workflow.ExecuteApprovedLocalDraftAsync(issue, gateway);
        Equal(LocalAllowanceSendOutcome.Problem, retryEntry.Outcome);
        var readback = await setup.Workflow.ExecuteApprovedLocalDraftAsync(issue, gateway);
        Equal(LocalAllowanceSendOutcome.Problem, readback.Outcome);
        Equal(1, gateway.IssueCalls);
        Equal(3, gateway.QueryCalls); // original plus two read-only resumes, zero repeat posts
        Equal(5, setup.Gateway.QueryCalls);
    }

    public static async Task LocalSendRejectsChangedOfficialHistoryBeforePostAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        var (issue, reserved) = await PrepareReservedAsync(setup);
        var gateway = new FakeLocalIssueGateway();
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "OTHER-001", "1", "19")
        ], summaryPresent: true));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.ExecuteApprovedLocalDraftAsync(issue, gateway));
        Equal(0, gateway.IssueCalls);
        Equal("reserved", new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString())!.Status);
        Equal(reserved.AllowanceNumber,
            setup.Workflow.LocalReservationFor(issue)!.AllowanceNumber);
    }

    private static async Task<(InvoiceSyncIssue Issue, LocalAllowanceReservation Reservation)>
        PrepareReservedAsync(AllowanceSetup setup)
    {
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退貨", 50, editedItems: DraftItems());
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var preview = await setup.Workflow.PreviewLocalDraftAsync(
            issue, "2000", "AdminPass1");
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        await setup.Workflow.ApproveLocalDraftAsync(issue, "2000",
            "AdminPass1", preview.OfficialSnapshotHash);
        setup.Gateway.Queries.Enqueue(Query([], summaryPresent: true));
        var reserved = await setup.Workflow.ReserveApprovedDraftAsync(issue, "2000");
        return (issue, reserved);
    }

    public static async Task StartupResumesApprovedWorkWithoutManagerAsync()
    {
        foreach(var alreadySent in new[]{false,true})
        {
            using var temporary=new AllowanceTemporaryDirectory();var setup=CreateSetup(temporary.Path);
            for(var i=0;i<20;i++)setup.Gateway.Queries.Enqueue(Query([],summaryPresent:true));
            await setup.Workflow.SubmitAsync(setup.Record,"3015","Employee1","部分退貨",50,editedItems:DraftItems());
            var issue=AllowanceIssue(setup.Repository);
            var preview=await setup.Workflow.PreviewLocalDraftAsync(issue,"2000","AdminPass1");
            await setup.Workflow.ApproveLocalDraftAsync(issue,"2000","AdminPass1",preview.OfficialSnapshotHash);
            var outgoing=new FakeLocalIssueGateway();
            if(alreadySent)await setup.Workflow.ExecuteApprovedLocalDraftAsync(issue,outgoing);
            outgoing.Match=true;
            var reopened=TestRepository.Open(temporary.Path,new AllowanceTestProtector());
            var at=new DateTimeOffset(2026,9,19,12,0,0,TimeSpan.FromHours(8));
            var detail=new InvoiceDetailRefreshService(reopened,(_,_)=>setup.Gateway,()=>at);
            var workflow=new EmployeeAllowanceWorkflowService(reopened,detail,()=>at,outgoing);
            var sync=new InvoiceSyncService(reopened,(_,_)=>setup.Gateway,()=>at);
            var automatic=new InvoiceAutomaticSyncService(reopened,sync,()=>at,allowanceService:workflow);
            var coordinator=new InvoiceSyncCoordinator(sync,automaticService:automatic);
            await coordinator.RunStartupAsync();
            Equal(1,outgoing.IssueCalls);
            Equal(0,new InvoiceSyncIssueStore(reopened.DataDirectory).Unresolved(issue.AccountKey).Count);
            Equal("confirmed",new LocalAllowanceNumberStore(reopened.DataDirectory).FindLatest(AmegoDefaults.TestInvoice,Environments.Test,issue.Id.ToString())!.Status);
            await coordinator.RunScheduledAsync();Equal(1,outgoing.IssueCalls);
            Equal(false,JsonSerializer.Serialize(reopened.Invoices.LoadOrCreate()).Contains("AdminPass1",StringComparison.Ordinal));
        }
    }

    private sealed class FakeLocalIssueGateway : IAmegoAllowanceGateway
    {
        public int IssueCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public bool Match {get;set;}
        private AllowanceIssueRequest? saved;
        public Task<AllowanceIssueResponse> IssueAllowanceOnceAsync(
            AllowanceIssueRequest request, CancellationToken cancellationToken = default)
        {
            IssueCalls++;saved=request;
            return Task.FromResult(new AllowanceIssueResponse(0, "accepted, not complete"));
        }
        public Task<AllowanceQueryResponse> QueryAllowanceAsync(
            string allowanceNumber, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            if(Match && saved is not null)return Task.FromResult(new AllowanceQueryResponse(0,"",new AllowanceQueryItem(
                allowanceNumber,"D0401",99,saved.AllowanceDate,2,saved.BuyerIdentifier,saved.BuyerName,
                saved.TaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),saved.TotalAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),0,
                JsonSerializer.SerializeToElement(saved.ProductItem.Select(i=>new {original_invoice_number=i.OriginalInvoiceNumber,original_invoice_date=i.OriginalInvoiceDate,
                    original_description=i.OriginalDescription,quantity=i.Quantity,unit_price=i.UnitPrice,amount=i.Amount,tax=i.Tax,tax_type=i.TaxType})))));
            return Task.FromException<AllowanceQueryResponse>(
                new HttpRequestException("synthetic official readback unavailable"));
        }
    }

    private static IReadOnlyList<AllowanceProductItem> DraftItems() => [
        new AllowanceProductItem
        {
            OriginalInvoiceNumber = "AA12345678",
            OriginalInvoiceDate = 20260919,
            OriginalDescription = "部分折讓",
            Quantity = 1m,
            UnitPrice = 50m,
            Amount = 50m,
            Tax = 0m,
            TaxType = 3
        }
    ];

    // A historical fixture records the pre-existing completed website step.
    // The product API can now only READ this state, never assert completion.
    private static async Task<InvoiceAllowanceReconcileResult> CheckSeededHistoricalAsync(
        AllowanceSetup setup,InvoiceSyncIssue issue,string employee,string password)
    {
        var record=setup.Repository.Invoices.LoadOrCreate().Single();
        var review=setup.Workflow.ManualReviewFor(record)!;
        if(review.LocalApprovedUtc is null)
        {
            record.ExtensionData ??= new Dictionary<string,JsonElement>();
            record.ExtensionData["cyinvoice_allowance_manual_review"]=JsonSerializer.SerializeToElement(review with {
                DraftItems=null,AwaitingConfirmation=true,ManualCompletedUtc=DateTimeOffset.UtcNow.AddDays(-1),HandlerEmployeeNo=employee });
            record.ExtensionData["cyinvoice_allowance_pending"]=JsonSerializer.SerializeToElement(true);
            new InvoiceSyncRepository(setup.Repository.DataDirectory).UpsertMany([record]);
        }
        return await setup.Workflow.CheckHistoricalAsync(issue,employee,password);
    }

    private static AllowanceSetup CreateSetup(string path)
    {
        var repository = TestRepository.Open(path, new AllowanceTestProtector());
        repository.Employees.CreateFirstSuperAdmin("0001", "超管", "super@example.com", "SuperPass1");
        repository.Employees.CreateEmployee("0001", "3015", "員工", "employee@example.com", "Employee1");
        repository.Employees.CreateEmployee("0001", "2000", "管理員", "admin@example.com", "AdminPass1", EmployeeRoles.Admin);

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
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = UploadStatuses.Complete,
            UploadStatusText = "完成",
            InvoiceDate = "2026/09/19",
            InvoiceTime = "14:00:00",
            Items = [],
        };
        repository.Invoices.Append(record);

        var gateway = new AllowanceGateway();
        var clock = new DateTimeOffset(2026, 9, 19, 14, 32, 0, TimeSpan.FromHours(8));
        var detail = new InvoiceDetailRefreshService(repository, (_, _) => gateway, () => clock);
        var workflow = new EmployeeAllowanceWorkflowService(repository, detail, () => clock);
        var voidService = new InvoiceVoidService(repository, (_, _) => gateway, () => clock);
        var voidWorkflow = new EmployeeVoidWorkflowService(repository, voidService, () => clock);
        return new AllowanceSetup(repository, gateway, workflow, voidWorkflow, record);
    }

    private static InvoiceAllowanceResult Allowance(
        string type,
        int status,
        string number,
        string tax,
        string total) =>
        new(type, status, 2, number, "20260919", tax, total);

    private static QueryResponse Query(IReadOnlyList<InvoiceAllowanceResult>? allowances = null,
        bool summaryPresent = true)
    {
        using var productsDocument = JsonDocument.Parse("[]");
        var data = new QueryResult(
            InvoiceNumber: "AA12345678",
            InvoiceType: "A0401",
            InvoiceStatus: UploadStatuses.Complete,
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
            CancelDate: 0,
            OrderId: "M20260919001",
            CreateDate: 1789790000,
            ProductItems: productsDocument.RootElement.Clone(),
            DetailVat: 1,
            DetailVatPresent: true)
        {
            Allowances = allowances ?? [],
            AllowanceSummaryPresent = summaryPresent,
        };
        return new QueryResponse(0, "", data);
    }

    private static InvoiceSyncIssue AllowanceIssue(LocalRepository repository) =>
        new InvoiceSyncIssueStore(repository.DataDirectory).Unresolved(AccountKey())
            .Single(issue => issue.IssueType == InvoiceAllowanceIssueTypes.ManualReview);

    private static string AccountKey() => Environments.Test + "|" + AmegoDefaults.TestInvoice;

    private static bool HasPending(InvoiceRecord record) =>
        record.ExtensionData is not null &&
        record.ExtensionData.TryGetValue("cyinvoice_allowance_pending", out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, actual {actual}");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"expected {typeof(T).Name}");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"expected {typeof(T).Name}");
    }

    private sealed record AllowanceSetup(
        LocalRepository Repository,
        AllowanceGateway Gateway,
        EmployeeAllowanceWorkflowService Workflow,
        EmployeeVoidWorkflowService VoidWorkflow,
        InvoiceRecord Record);

    private sealed class AllowanceGateway : IAmegoGateway
    {
        public Queue<QueryResponse> Queries { get; } = new();
        public int QueryCalls { get; private set; }
        public int VoidCalls { get; private set; }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            if (Queries.Count == 0) throw new InvalidOperationException("allowance test query queue is empty");
            return Task.FromResult(Queries.Dequeue());
        }

        public Task<InvoiceListResponse> ListInvoicesAsync(DateOnly start,DateOnly end,int page=1,int limit=500,CancellationToken token=default)=>
            Task.FromResult(new InvoiceListResponse(0,"",0,1,0,[]));
        public Task<AllowanceListResponse> ListAllowancesAsync(DateOnly start,DateOnly end,int page=1,int limit=20,CancellationToken token=default)=>
            Task.FromResult(new AllowanceListResponse(1,[]));

        public Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default)
        {
            VoidCalls++;
            return Task.FromResult(new VoidResponse(0, "OK"));
        }

        public Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IssueResponse>(new NotSupportedException());
        public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
            Task.FromException<QueryResponse>(new NotSupportedException());
        public Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StatusResponse(0, "", []));
        public Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default) =>
            Task.FromException<BanResponse>(new NotSupportedException());
        public Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new NotSupportedException());
    }

    private sealed class AllowanceTestProtector : ISecretProtector
    {
        private const string Prefix = "test-protected:";
        public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
        public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(ciphertext[Prefix.Length..])
            : throw new InvalidDataException("invalid protected test value");
    }

    private sealed class AllowanceTemporaryDirectory : IDisposable
    {
        public AllowanceTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-allowance-workflow-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
