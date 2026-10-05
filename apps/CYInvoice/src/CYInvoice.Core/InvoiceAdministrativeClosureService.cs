using System.Globalization;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceAdministrativeClosureService
{
    private const string VoidPendingMetadataKey = "cyinvoice_void_pending";
    private const string VoidManualReviewMetadataKey = "cyinvoice_void_manual_review";
    private const string AllowancePendingMetadataKey = "cyinvoice_allowance_pending";
    private const string AllowanceManualReviewMetadataKey = "cyinvoice_allowance_manual_review";
    private const string AllowanceVoidManualReviewMetadataKey = "cyinvoice_allowance_void_manual_review";

    private static readonly string[] ActiveWorkIssueTypes =
    [
        InvoiceVoidIssueTypes.ManualReview,
        InvoiceVoidSyncIssueTypes.PendingConfirmation,
        InvoiceAllowanceIssueTypes.ManualReview,
        InvoiceAllowanceVoidIssueTypes.ManualReview,
    ];

    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly Func<DateTimeOffset> now;

    public InvoiceAdministrativeClosureService(
        LocalRepository repository,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public bool CanClose(InvoiceSyncIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.ResolvedUtc is not null || !IsAdministrativeClosureIssue(issue)) return false;
        if (!string.Equals(issue.AccountKey, CurrentAccountKey(), StringComparison.Ordinal)) return false;

        var record = TryFindIssueRecord(issue);
        if (record is null) return false;
        return IsOutsideRetentionWindow(record, DateOnly.FromDateTime(now().DateTime));
    }

    public void Close(
        InvoiceSyncIssue issue,
        string actorEmployeeNo,
        string actorPassword)
    {
        ArgumentNullException.ThrowIfNull(issue);
        EmployeeOperationAuthentication.AuthenticateManager(repository, actorEmployeeNo, actorPassword);

        if (issue.ResolvedUtc is not null)
            throw new InvalidOperationException("這筆待處理作業已經結案");
        if (!IsAdministrativeClosureIssue(issue))
            throw new InvalidOperationException("這筆上傳問題不屬於可由管理員手動結案的作廢或折讓作業");
        if (!string.Equals(issue.AccountKey, CurrentAccountKey(), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("這筆待處理作業屬於其他公司或環境，已停止結案");

        var record = FindIssueRecord(issue);
        if (!IsOutsideRetentionWindow(record, DateOnly.FromDateTime(now().DateTime)))
            throw new InvalidOperationException("這筆資料仍在目前保留的兩期範圍內，不能手動結案");

        var hadVoidPending = InvoiceVoidService.HasPendingMarker(record);
        ClearActiveWorkMetadata(record);
        if (hadVoidPending && record.InvoiceState == InvoiceStates.OpenedWaitingVoid)
        {
            record.InvoiceState = InvoiceStates.Unknown;
            record.ErrorMessage = "管理員已手動結案；光貿最終作廢結果未由系統確認";
        }
        syncRepository.UpsertMany([record]);

        var resolvedAt = now();
        issueStore.Resolve(issue.Id, resolvedAt);
        issueStore.ResolveMatching(
            CurrentAccountKey(),
            record.InvoiceNumber.Trim(),
            EffectiveOrderId(record),
            resolvedAt,
            ActiveWorkIssueTypes);
    }

    public static bool IsAdministrativeClosureIssue(InvoiceSyncIssue issue) =>
        ActiveWorkIssueTypes.Contains(issue.IssueType, StringComparer.Ordinal);

    internal static bool IsOutsideRetentionWindow(
        InvoiceRecord record,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(record);
        var recordDate = RecordDate(record.InvoiceDate, record.SentAt);
        if (recordDate is null) return false;
        var cutoff = string.Equals(record.Environment, Environments.Test, StringComparison.Ordinal)
            ? today
            : InvoiceAutomaticSyncService.TwoPeriodRangeStart(today);
        return recordDate.Value < cutoff;
    }

    private InvoiceRecord FindIssueRecord(InvoiceSyncIssue issue) =>
        TryFindIssueRecord(issue)
        ?? throw new InvalidOperationException("本機找不到這筆待處理作業對應的發票");

    private InvoiceRecord? TryFindIssueRecord(InvoiceSyncIssue issue)
    {
        var account = CurrentAccount();
        var matches = repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, account.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            .Where(record =>
                (issue.InvoiceNumber.Trim().Length != 0 &&
                 string.Equals(record.InvoiceNumber.Trim(), issue.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (issue.OrderId.Trim().Length != 0 &&
                 (string.Equals(record.OriginalOrderId.Trim(), issue.OrderId.Trim(), StringComparison.Ordinal) ||
                  string.Equals(record.OrderId.Trim(), issue.OrderId.Trim(), StringComparison.Ordinal) ||
                  string.Equals(record.ApiOrderId.Trim(), issue.OrderId.Trim(), StringComparison.Ordinal))))
            .ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("待處理作業對到多筆本機發票，已停止手動結案");
        return matches.SingleOrDefault();
    }

    private static void ClearActiveWorkMetadata(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(VoidPendingMetadataKey);
        record.ExtensionData.Remove(VoidManualReviewMetadataKey);
        record.ExtensionData.Remove(AllowancePendingMetadataKey);
        record.ExtensionData.Remove(AllowanceManualReviewMetadataKey);
        record.ExtensionData.Remove(AllowanceVoidManualReviewMetadataKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }

    private Account CurrentAccount()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test
            ? AmegoDefaults.TestInvoice
            : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0)
            throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return new Account(settings.Environment, sellerInvoice);
    }

    private string CurrentAccountKey()
    {
        var account = CurrentAccount();
        return account.Environment + "|" + account.SellerInvoice;
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private static DateOnly? RecordDate(string invoiceDate, string sentAt)
    {
        var direct = ParseDate(invoiceDate);
        if (direct is not null) return direct;

        var value = sentAt.Trim();
        if (value.Length >= 10 && (value[4] == '/' || value[4] == '-')) value = value[..10];
        else if (value.Length >= 8 && value[..8].All(char.IsAsciiDigit)) value = value[..8];
        return ParseDate(value);
    }

    private static DateOnly? ParseDate(string value)
    {
        value = value.Trim();
        foreach (var format in new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyyMMdd" })
        {
            if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }
        return null;
    }

    private sealed record Account(string Environment, string SellerInvoice);
}
