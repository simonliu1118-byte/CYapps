using System.Text;
using System.Text.Json;
using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

internal static class AdministrativeClosureTests
{
    private static readonly DateTimeOffset Clock = new(2026, 9, 19, 23, 30, 0, TimeSpan.FromHours(8));

    public static void WithinTwoPeriodsCannotClose()
    {
        using var temporary = new AdministrativeClosureTemporaryDirectory();
        var setup = CreateSetup(temporary.Path, "2026/08/15", InvoiceVoidSyncIssueTypes.PendingConfirmation);
        var service = new InvoiceAdministrativeClosureService(setup.Repository, () => Clock);

        Equal(false, service.CanClose(setup.Issue));
        Throws<InvalidOperationException>(() => service.Close(setup.Issue, "2000", "admin-pass"));

        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(true, HasMetadata(stored, "cyinvoice_void_pending"));
        Equal(null, ReloadIssue(setup).ResolvedUtc);
    }

    public static void OrdinaryEmployeeCannotCloseExpiredWork()
    {
        using var temporary = new AdministrativeClosureTemporaryDirectory();
        var setup = CreateSetup(temporary.Path, "2026/06/30", InvoiceVoidSyncIssueTypes.PendingConfirmation);
        var service = new InvoiceAdministrativeClosureService(setup.Repository, () => Clock);

        Equal(true, service.CanClose(setup.Issue));
        Throws<UnauthorizedAccessException>(() => service.Close(setup.Issue, "3015", "employee-pass"));

        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(true, HasMetadata(stored, "cyinvoice_void_pending"));
        Equal(null, ReloadIssue(setup).ResolvedUtc);
    }

    public static void AdministratorClosesExpiredVoidPendingWork()
    {
        using var temporary = new AdministrativeClosureTemporaryDirectory();
        var setup = CreateSetup(temporary.Path, "2026/06/30", InvoiceVoidSyncIssueTypes.PendingConfirmation);
        var service = new InvoiceAdministrativeClosureService(setup.Repository, () => Clock);

        service.Close(setup.Issue, "2000", "admin-pass");

        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(false, HasMetadata(stored, "cyinvoice_void_pending"));
        Equal(false, HasMetadata(stored, "cyinvoice_void_manual_review"));
        Equal(InvoiceStates.Unknown, stored.InvoiceState);
        Equal(true, stored.ErrorMessage.Contains("管理員已手動結案", StringComparison.Ordinal));
        Equal(true, ReloadIssue(setup).ResolvedUtc is not null);

        var retention = new InvoiceRetentionService(setup.Repository).Prune(DateOnly.FromDateTime(Clock.DateTime));
        Equal(1, retention.Deleted);
        Equal(0, setup.Repository.Invoices.LoadOrCreate().Count);
    }

    public static void AdministratorClosesExpiredAllowanceWork()
    {
        using var temporary = new AdministrativeClosureTemporaryDirectory();
        var setup = CreateSetup(temporary.Path, "2026/06/30", InvoiceAllowanceIssueTypes.ManualReview, allowance: true);
        var service = new InvoiceAdministrativeClosureService(setup.Repository, () => Clock);

        service.Close(setup.Issue, "0001", "super-pass");

        var stored = setup.Repository.Invoices.LoadOrCreate().Single();
        Equal(false, HasMetadata(stored, "cyinvoice_allowance_pending"));
        Equal(false, HasMetadata(stored, "cyinvoice_allowance_manual_review"));
        Equal(InvoiceStates.Opened, stored.InvoiceState);
        Equal(true, ReloadIssue(setup).ResolvedUtc is not null);
    }

    private static AdministrativeClosureSetup CreateSetup(
        string path,
        string invoiceDate,
        string issueType,
        bool allowance = false)
    {
        var repository = LocalRepository.Open(path, new AdministrativeClosureProtector());
        var settings = repository.Settings.LoadOrCreate();
        settings.Environment = Environments.Production;
        settings.ProductionInvoice = "12345678";
        repository.Settings.SetProductionAppKey(settings, "test-app-key");
        repository.Settings.Save(settings);

        repository.Employees.CreateFirstSuperAdmin("0001", "超管", "super@example.com", "super-pass");
        repository.Employees.CreateEmployee("0001", "3015", "員工", "employee@example.com", "employee-pass");
        repository.Employees.CreateEmployee("0001", "2000", "管理員", "admin@example.com", "admin-pass", EmployeeRoles.Admin);

        var record = new InvoiceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SellerInvoice = "12345678",
            Environment = Environments.Production,
            Source = InvoiceSources.Manual,
            RecordOrigin = RecordOrigins.Local,
            OriginalOrderId = "M20260630001",
            OrderId = "M20260630001",
            ApiOrderId = "M20260630001",
            InvoiceNumber = "AA12345678",
            InvoiceState = allowance ? InvoiceStates.Opened : InvoiceStates.OpenedWaitingVoid,
            Amount = 100,
            Delivery = InvoiceService.DeliveryPaper,
            UploadStatus = 99,
            UploadStatusText = "完成",
            InvoiceDate = invoiceDate,
            InvoiceTime = "14:00:00",
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        };
        if (allowance)
        {
            record.ExtensionData["cyinvoice_allowance_pending"] = JsonSerializer.SerializeToElement(true);
            record.ExtensionData["cyinvoice_allowance_manual_review"] = JsonSerializer.SerializeToElement(new
            {
                requester_employee_no = "3015",
                reason = "退貨",
            });
        }
        else
        {
            record.ExtensionData["cyinvoice_void_pending"] = JsonSerializer.SerializeToElement(true);
            record.ExtensionData["cyinvoice_void_manual_review"] = JsonSerializer.SerializeToElement(new
            {
                requester_employee_no = "3015",
                reason = "退貨",
            });
        }
        repository.Invoices.Append(record);

        var issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        var id = issueStore.Record(
            Environments.Production + "|12345678",
            record.InvoiceNumber,
            record.ApiOrderId,
            issueType,
            "等待人工處理",
            Clock.AddMonths(-3));
        var issue = issueStore.All(Environments.Production + "|12345678").Single(item => item.Id == id);
        return new AdministrativeClosureSetup(repository, issue);
    }

    private static InvoiceSyncIssue ReloadIssue(AdministrativeClosureSetup setup) =>
        new InvoiceSyncIssueStore(setup.Repository.DataDirectory)
            .All(Environments.Production + "|12345678")
            .Single(issue => issue.Id == setup.Issue.Id);

    private static bool HasMetadata(InvoiceRecord record, string key) =>
        record.ExtensionData is not null && record.ExtensionData.ContainsKey(key);

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

    private sealed record AdministrativeClosureSetup(LocalRepository Repository, InvoiceSyncIssue Issue);

    private sealed class AdministrativeClosureProtector : ISecretProtector
    {
        private const string Prefix = "admin-close-test:";
        public string Protect(ReadOnlySpan<byte> plaintext) => Prefix + Convert.ToBase64String(plaintext);
        public byte[] Unprotect(string ciphertext) => ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(ciphertext[Prefix.Length..])
            : throw new InvalidDataException("invalid protected test value");
    }

    private sealed class AdministrativeClosureTemporaryDirectory : IDisposable
    {
        public AdministrativeClosureTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CYInvoice-admin-close-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
