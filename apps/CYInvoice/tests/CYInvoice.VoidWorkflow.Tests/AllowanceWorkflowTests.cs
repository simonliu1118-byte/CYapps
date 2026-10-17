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
            setup.Record, "3015", "wrong", "部分退貨", 50));
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
            setup.Record, "3015", "Employee1", "部分退貨", 50);

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

    public static async Task OriginalApplicantWithdrawsManualReviewAsync()
    {
        using var temporary=new AllowanceTemporaryDirectory();
        var setup=CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record,"3015","Employee1",
            "員工本人提出",50);
        var issue=AllowanceIssue(setup.Repository);
        Throws<UnauthorizedAccessException>(()=>setup.Workflow.WithdrawManualReview(
            setup.Record,"2000","AdminPass1"));
        Throws<InvalidOperationException>(()=>setup.Workflow.WithdrawManualReview(
            setup.Record,"3015","incorrect-password"));
        Equal(null,issue.ResolvedUtc);
        Equal(true,setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single()) is not null);

        setup.Workflow.WithdrawManualReview(setup.Record,"3015","Employee1");
        var stored=setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(null,setup.Workflow.ManualReviewFor(stored));
        Equal(false,HasPending(stored));
        Equal(true,stored.ExtensionData!.ContainsKey(
            "cyinvoice_allowance_withdrawn_review"));
        var audit=stored.ExtensionData["cyinvoice_allowance_withdrawn_review"];
        Equal("3015",audit.GetProperty("WithdrawnByEmployeeNo").GetString());
        Equal("員工本人提出",audit.GetProperty("Review").GetProperty("Reason").GetString());
        Equal(true,new InvoiceSyncIssueStore(setup.Repository.DataDirectory)
            .All(AccountKey()).Single(x=>x.Id==issue.Id).ResolvedUtc is not null);
        Equal(1,setup.Gateway.QueryCalls);
        Equal(0,setup.Gateway.VoidCalls);
        Throws<InvalidOperationException>(()=>setup.Workflow.WithdrawManualReview(
            setup.Record,"3015","Employee1"));

        // User can submit a new, independent request after explicit withdrawal.
        setup.Gateway.Queries.Enqueue(Query());
        var next=await setup.Workflow.SubmitAsync(setup.Record,
            "3015","Employee1","重提申請",20);
        Equal(false,next.AlreadyQueued);
        Equal(20L,next.Review.TaxInclusiveAmount);
    }

    public static async Task AdministratorCancelsManualReviewAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
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
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
        var issue = AllowanceIssue(setup.Repository);

        var result = await setup.Workflow.MarkManualCompletedAsync(
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
            await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
            var issue = AllowanceIssue(setup.Repository);

            var result = await setup.Workflow.MarkManualCompletedAsync(issue, "2000", "AdminPass1");

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
            await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
            var issue = AllowanceIssue(setup.Repository);

            var ambiguous = await setup.Workflow.MarkManualCompletedAsync(issue, "2000", "AdminPass1");
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
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
        var issue = AllowanceIssue(setup.Repository);
        var pending = await setup.Workflow.MarkManualCompletedAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.PendingConfirmation, pending.Outcome);
        Equal(0, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().ProcessingCount);

        Throws<InvalidOperationException>(() =>
            setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1"));
        Equal(true, HasPending(setup.Repository.Invoices.LoadOrCreate().Single()));
    }

    public static async Task QueueFollowsOfficialConfirmationAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
        Equal(1, new InvoiceWorkQueue(setup.Repository).Load().UploadIssues.Count);
        var issue = AllowanceIssue(setup.Repository);
        await setup.Workflow.MarkManualCompletedAsync(issue, "2000", "AdminPass1");
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
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1", "部分退貨", 50);
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

    private static readonly AllowanceProductItem[] EditedAllowances =
    [
        new AllowanceProductItem {
            OriginalInvoiceNumber = "AA12345678",
            OriginalInvoiceDate = 20260919,
            OriginalDescription = "Selected product",
            Quantity = 1, UnitPrice = 47, Amount = 47, Tax = 3, TaxType = 1,
        }
    ];

    public static async Task LocalApprovalPersistedBeforeLedgerFailureAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        var workId = issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Force Reserve to fail after the approval write but before the
        // invoice pending marker. The business state must remain approved.
        var ledger = new LocalAllowanceNumberStore(setup.Repository.DataDirectory);
        ledger.Reserve(AmegoDefaults.TestInvoice, Environments.Test, workId,
            new DateOnly(2026, 9, 19), new AllowanceIssueRequest
            {
                AllowanceDate = "20260919",
                BuyerIdentifier = "0000000000",
                BuyerName = "消費者",
                ProductItem = EditedAllowances,
                TotalAmount = 1m,
                TaxAmount = 0m,
            });
        setup.Gateway.Queries.Enqueue(Query());
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.IssueApprovedLocalAsync(issue, "2000", "AdminPass1"));

        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        var approved = setup.Workflow.ManualReviewFor(stored);
        Equal(true, approved?.ApprovedAt is not null);
        Equal("2000", approved?.HandlerEmployeeNo);
        Equal(false, approved?.AwaitingConfirmation);
        Equal(false, HasPending(stored));
        Throws<InvalidOperationException>(() =>
            setup.Workflow.WithdrawManualReview(setup.Record, "3015", "Employee1"));
        Throws<InvalidOperationException>(() =>
            setup.Workflow.CancelManualReview(issue, "2000", "AdminPass1"));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.MarkManualCompletedAsync(issue, "2000", "AdminPass1"));
        Equal(true, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single()) is not null);
        Equal(null, new InvoiceSyncIssueStore(setup.Repository.DataDirectory)
            .All(AccountKey()).Single(item => item.Id == issue.Id).ResolvedUtc);
        Equal("reserved", ledger.FindLatest(AmegoDefaults.TestInvoice,
            Environments.Test, workId)?.Status);
        Equal(0, setup.Gateway.AllowanceIssueCalls);
    }

    public static async Task LocalManagerSendsOnceAndConfirmsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "A20260919001", "3", "47")
        ]));
        var result = await setup.Workflow.IssueApprovedLocalAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.Confirmed, result.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        Equal("A20260919001", setup.Gateway.LastAllowance!.AllowanceNumber);
        Equal(47m, setup.Gateway.LastAllowance.TotalAmount);
        Equal(3m, setup.Gateway.LastAllowance.TaxAmount);
        var reservation = new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test,
                issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Equal("confirmed", reservation?.Status);
        var handledReview = setup.Workflow.HandledReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single());
        Equal(true, handledReview?.ApprovedAt is not null);
        Equal("2000", handledReview?.HandlerEmployeeNo);
        Equal(null, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single()));
        Equal(true, new InvoiceSyncIssueStore(setup.Repository.DataDirectory)
            .All(AccountKey()).Single(x => x.Id == issue.Id).ResolvedUtc is not null);
    }

    public static async Task LocalAcceptedResponseWaitsForOfficialConfirmationAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        // Preflight and immediate readback both succeed as queries, but no
        // confirmed allowance is visible yet after g0401 API code 0.
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.Queries.Enqueue(Query());
        var pending = await setup.Workflow.IssueApprovedLocalAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.PendingConfirmation, pending.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        Equal(true, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())!.AwaitingConfirmation);
        Equal("sending", new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString())?.Status);
    }

    public static async Task LocalManagerTimeoutNeverResendsAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.AllowanceIssueError = new HttpRequestException("lost response");
        var unknown = await setup.Workflow.IssueApprovedLocalAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.Problem, unknown.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        var ledger = new LocalAllowanceNumberStore(setup.Repository.DataDirectory);
        var reservation = ledger.FindLatest(AmegoDefaults.TestInvoice, Environments.Test,
            issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Equal("sending", reservation?.Status);
        Equal(true, setup.Workflow.ManualReviewFor(
            setup.Repository.Invoices.LoadOrCreate().Single())!.AwaitingConfirmation);

        // A second manager click may read the original number, never send a
        // new g0401 payload or allocate another sequence.
        setup.Gateway.Queries.Enqueue(Query());
        var stillPending = await setup.Workflow.IssueApprovedLocalAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.PendingConfirmation, stillPending.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        Equal("sending", ledger.FindLatest(AmegoDefaults.TestInvoice,
            Environments.Test, issue.Id.ToString())?.Status);

        // Only a later official invoice_query can confirm the same number.
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "A20260919001", "3", "47")
        ]));
        var confirmed = await setup.Workflow.ReconcilePendingAsync(setup.Record);
        Equal(InvoiceAllowanceReconcileOutcome.Confirmed, confirmed.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        Equal("confirmed", ledger.FindLatest(AmegoDefaults.TestInvoice,
            Environments.Test, issue.Id.ToString())?.Status);
    }

    public static async Task LocalManagerChangedRemainingBlocksSendAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        // A fresh official allocation reduces the remaining gross from 100
        // to 40 after the applicant opened the original invoice.
        setup.Gateway.Queries.Enqueue(Query([
            Allowance("D0401", UploadStatuses.Complete, "EXTERNAL-60", "3", "57")
        ]));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.IssueApprovedLocalAsync(issue, "2000", "AdminPass1"));
        Equal(0, setup.Gateway.AllowanceIssueCalls);
        Equal(null, new LocalAllowanceNumberStore(setup.Repository.DataDirectory)
            .FindLatest(AmegoDefaults.TestInvoice, Environments.Test, issue.Id.ToString()));
    }

    public static async Task LocalManagerRejectsOldManualReviewAndWrongAuthAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "舊人工折讓", 50);
        var issue = AllowanceIssue(setup.Repository);
        await ThrowsAsync<UnauthorizedAccessException>(() =>
            setup.Workflow.IssueApprovedLocalAsync(issue, "3015", "Employee1"));
        await ThrowsAsync<InvalidOperationException>(() =>
            setup.Workflow.IssueApprovedLocalAsync(issue, "2000", "AdminPass1"));
        Equal(0, setup.Gateway.AllowanceIssueCalls);
        Equal(1, setup.Gateway.QueryCalls);
    }

    public static async Task LocalManagerCollisionKeepsSingleAttemptAsync()
    {
        using var temporary = new AllowanceTemporaryDirectory();
        var setup = CreateSetup(temporary.Path);
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.SubmitAsync(setup.Record, "3015", "Employee1",
            "部分退款", 50, editedItems: EditedAllowances);
        var issue = AllowanceIssue(setup.Repository);
        setup.Gateway.Queries.Enqueue(Query());
        setup.Gateway.AllowanceIssueError =
            new AmegoApiException(AmegoAllowanceErrorCodes.ExistingIssuedAllowanceNumber,
                "number occupied");
        var rejected = await setup.Workflow.IssueApprovedLocalAsync(
            issue, "2000", "AdminPass1");
        Equal(InvoiceAllowanceReconcileOutcome.Problem, rejected.Outcome);
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        var ledger = new LocalAllowanceNumberStore(setup.Repository.DataDirectory);
        Equal("occupied_external", ledger.FindLatest(AmegoDefaults.TestInvoice,
            Environments.Test, issue.Id.ToString())?.Status);
        // No automatic new number or second side effect after a confirmed
        // external collision; explicit future reconciliation is required.
        setup.Gateway.Queries.Enqueue(Query());
        await setup.Workflow.IssueApprovedLocalAsync(issue, "2000", "AdminPass1");
        Equal(1, setup.Gateway.AllowanceIssueCalls);
        Equal("occupied_external", ledger.FindLatest(AmegoDefaults.TestInvoice,
            Environments.Test, issue.Id.ToString())?.Status);
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
        var workflow = new EmployeeAllowanceWorkflowService(repository, detail, () => clock, (_, _) => gateway);
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

    private static QueryResponse Query(IReadOnlyList<InvoiceAllowanceResult>? allowances = null)
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
            AllowanceSummaryPresent = true,
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
        public int AllowanceIssueCalls { get; private set; }
        public AllowanceIssueRequest? LastAllowance { get; private set; }
        public Exception? AllowanceIssueError { get; set; }

        public Task<AllowanceIssueResponse> IssueAllowanceAsync(
            AllowanceIssueRequest request, CancellationToken cancellationToken = default)
        {
            AllowanceIssueCalls++;
            LastAllowance = request;
            return AllowanceIssueError is null
                ? Task.FromResult(new AllowanceIssueResponse(0, "accepted"))
                : Task.FromException<AllowanceIssueResponse>(AllowanceIssueError);
        }

        public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            if (Queries.Count == 0) throw new InvalidOperationException("allowance test query queue is empty");
            return Task.FromResult(Queries.Dequeue());
        }

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
