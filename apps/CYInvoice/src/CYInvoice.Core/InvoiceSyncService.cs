using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed record InvoiceSyncResult(
    int RemoteCount,
    int Inserted,
    int Updated,
    int Unchanged,
    int Queried,
    IReadOnlyList<string> Problems);

public sealed class InvoiceSyncService
{
    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly Func<string, string, IAmegoGateway> gatewayFactory;
    private readonly Func<DateTimeOffset> now;

    public InvoiceSyncService(
        LocalRepository repository,
        Func<string, string, IAmegoGateway>? gatewayFactory = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        this.gatewayFactory = gatewayFactory ?? ((invoice, appKey) => new AmegoClient(invoice, appKey));
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceSyncResult> SyncRecentAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now().DateTime);
        return await SyncRangeAsync(today.AddDays(-2), today, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<InvoiceSyncResult> SyncRangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (startDate > endDate) throw new ArgumentOutOfRangeException(nameof(startDate));
        var account = CurrentAccount();
        var gateway = gatewayFactory(account.SellerInvoice, account.AppKey);
        return account.Environment == Environments.Test
            ? await SyncKnownTestInvoicesAsync(gateway, account, startDate, endDate, cancellationToken).ConfigureAwait(false)
            : await SyncProductionAsync(gateway, account, startDate, endDate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InvoiceSyncResult> SyncProductionAsync(
        IAmegoGateway gateway,
        Account account,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var accountKey = AccountKey(account);
        IReadOnlyList<InvoiceListItem> remote;
        try
        {
            remote = await LoadAllPagesAsync(gateway, startDate, endDate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            TryRecordIssue(accountKey, string.Empty, string.Empty, InvoiceSyncIssueTypes.InvoiceListFailed, error.Message);
            throw;
        }
        TryResolveAccountIssue(accountKey, InvoiceSyncIssueTypes.InvoiceListFailed);

        var local = AccountRecords(account).ToList();
        var changes = new List<InvoiceRecord>();
        var invalidations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var successfulQueries = new List<IssueIdentity>();
        var successfulReconciliations = new List<IssueIdentity>();
        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var queried = 0;

        foreach (var item in remote)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invoiceNumber = item.InvoiceNumber.Trim();
            var orderId = item.OrderId.Trim();
            if (invoiceNumber.Length == 0) continue;
            try
            {
                var record = Match(local, item);
                var isNew = record is null;
                record ??= NewSyncedRecord(account, item);
                var before = Fingerprint(record);
                var summaryChanged = isNew || SummaryDiffers(record, item);
                ApplyList(record, item, account);
                var identity = Identity(record, item);
                successfulReconciliations.Add(identity);

                var pendingQueryIssue = issueStore.HasUnresolved(
                    accountKey,
                    identity.InvoiceNumber,
                    identity.OrderId,
                    InvoiceSyncIssueTypes.QueryFailed,
                    InvoiceSyncIssueTypes.RemoteNotFound,
                    InvoiceSyncIssueTypes.UnknownRemoteNotFound);
                if (isNew || summaryChanged || record.Items.Count == 0 || pendingQueryIssue)
                {
                    try
                    {
                        var query = await gateway.QueryByInvoiceNumberAsync(item.InvoiceNumber, cancellationToken).ConfigureAwait(false);
                        queried++;
                        ApplyQuery(record, query.Data, account);
                        successfulQueries.Add(Identity(record, item));
                    }
                    catch (Exception error)
                    {
                        var issueType = error is AmegoApiException { Code: 71 }
                            ? InvoiceSyncIssueTypes.RemoteNotFound
                            : InvoiceSyncIssueTypes.QueryFailed;
                        AddIssueProblem(
                            problems,
                            accountKey,
                            identity.InvoiceNumber,
                            identity.OrderId,
                            issueType,
                            $"完整資料回查失敗：{error.Message}");
                        problems.Add($"{item.InvoiceNumber}: 完整資料回查失敗，已先保存清單資料：{error.Message}");
                    }
                }

                var after = Fingerprint(record);
                if (isNew)
                {
                    inserted++;
                    changes.Add(record);
                    local.Add(record);
                }
                else if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    updated++;
                    changes.Add(record);
                    invalidations.Add(record.InvoiceNumber);
                }
                else
                {
                    unchanged++;
                }
            }
            catch (AmbiguousInvoiceMatchException error)
            {
                AddIssueProblem(
                    problems,
                    accountKey,
                    invoiceNumber,
                    orderId,
                    InvoiceSyncIssueTypes.AmbiguousMatch,
                    error.Message);
                problems.Add($"{invoiceNumber}: {error.Message}");
            }
            catch (Exception error)
            {
                AddIssueProblem(
                    problems,
                    accountKey,
                    invoiceNumber,
                    orderId,
                    InvoiceSyncIssueTypes.ReconciliationFailed,
                    error.Message);
                problems.Add($"{item.InvoiceNumber}: 同步失敗：{error.Message}");
            }
        }

        try
        {
            syncRepository.UpsertMany(changes);
        }
        catch (Exception error)
        {
            TryRecordIssue(accountKey, string.Empty, string.Empty, InvoiceSyncIssueTypes.LocalWriteFailed, error.Message);
            throw;
        }
        ResolveAccountIssue(problems, accountKey, InvoiceSyncIssueTypes.LocalWriteFailed);
        foreach (var identity in successfulReconciliations.Distinct())
        {
            ResolveIssues(
                problems,
                accountKey,
                identity,
                InvoiceSyncIssueTypes.AmbiguousMatch,
                InvoiceSyncIssueTypes.ReconciliationFailed);
        }
        foreach (var identity in successfulQueries.Distinct())
        {
            ResolveIssues(
                problems,
                accountKey,
                identity,
                InvoiceSyncIssueTypes.QueryFailed,
                InvoiceSyncIssueTypes.RemoteNotFound,
                InvoiceSyncIssueTypes.UnknownRemoteNotFound);
        }

        foreach (var number in invalidations)
            problems.AddRange(InvoiceCacheInvalidator.Invalidate(repository, account.Environment, number));
        return new InvoiceSyncResult(remote.Count, inserted, updated, unchanged, queried, problems);
    }

    private async Task<InvoiceSyncResult> SyncKnownTestInvoicesAsync(
        IAmegoGateway gateway,
        Account account,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var accountKey = AccountKey(account);
        var local = AccountRecords(account)
            .Where(record => IsInRange(record, startDate, endDate))
            .ToList();
        var changes = new List<InvoiceRecord>();
        var invalidations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var successfulQueries = new List<IssueIdentity>();
        var updated = 0;
        var unchanged = 0;
        var queried = 0;

        foreach (var record in local)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = Fingerprint(record);
            var identity = new IssueIdentity(record.InvoiceNumber.Trim(), EffectiveOrderId(record));
            try
            {
                QueryResponse query;
                if (record.InvoiceNumber.Trim().Length != 0)
                    query = await gateway.QueryByInvoiceNumberAsync(record.InvoiceNumber, cancellationToken).ConfigureAwait(false);
                else
                {
                    var orderId = EffectiveOrderId(record);
                    if (orderId.Length == 0) throw new InvalidDataException("本機測試紀錄缺少可回查的 OrderID");
                    query = await gateway.QueryByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
                }
                queried++;
                ApplyQuery(record, query.Data, account);
                identity = new IssueIdentity(record.InvoiceNumber.Trim(), EffectiveOrderId(record));
                successfulQueries.Add(identity);
                var after = Fingerprint(record);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    updated++;
                    changes.Add(record);
                    if (record.InvoiceNumber.Trim().Length != 0) invalidations.Add(record.InvoiceNumber);
                }
                else
                {
                    unchanged++;
                }
            }
            catch (AmegoApiException error) when (error.Code == 71)
            {
                var issueType = record.InvoiceState == InvoiceStates.Unknown
                    ? InvoiceSyncIssueTypes.UnknownRemoteNotFound
                    : InvoiceSyncIssueTypes.RemoteNotFound;
                AddIssueProblem(
                    problems,
                    accountKey,
                    identity.InvoiceNumber,
                    identity.OrderId,
                    issueType,
                    "光貿測試環境查無此筆，本機資料未刪除");
                problems.Add($"{EffectiveOrderId(record)}: 光貿測試環境查無此筆，本機資料未刪除");
            }
            catch (Exception error)
            {
                AddIssueProblem(
                    problems,
                    accountKey,
                    identity.InvoiceNumber,
                    identity.OrderId,
                    InvoiceSyncIssueTypes.QueryFailed,
                    error.Message);
                problems.Add($"{EffectiveOrderId(record)}: 測試資料回查失敗：{error.Message}");
            }
        }

        try
        {
            syncRepository.UpsertMany(changes);
        }
        catch (Exception error)
        {
            TryRecordIssue(accountKey, string.Empty, string.Empty, InvoiceSyncIssueTypes.LocalWriteFailed, error.Message);
            throw;
        }
        ResolveAccountIssue(problems, accountKey, InvoiceSyncIssueTypes.LocalWriteFailed);
        foreach (var identity in successfulQueries.Distinct())
        {
            ResolveIssues(
                problems,
                accountKey,
                identity,
                InvoiceSyncIssueTypes.QueryFailed,
                InvoiceSyncIssueTypes.RemoteNotFound,
                InvoiceSyncIssueTypes.UnknownRemoteNotFound,
                InvoiceSyncIssueTypes.ReconciliationFailed);
        }
        foreach (var number in invalidations)
            problems.AddRange(InvoiceCacheInvalidator.Invalidate(repository, account.Environment, number));
        return new InvoiceSyncResult(local.Count, 0, updated, unchanged, queried, problems);
    }

    private async Task<IReadOnlyList<InvoiceListItem>> LoadAllPagesAsync(
        IAmegoGateway gateway,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        const int limit = 500;
        const int maximumPages = 1000;
        var byInvoice = new Dictionary<string, InvoiceListItem>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; page <= maximumPages; page++)
        {
            var response = await gateway.ListInvoicesAsync(startDate, endDate, page, limit, cancellationToken).ConfigureAwait(false);
            foreach (var item in response.Data)
            {
                var number = item.InvoiceNumber.Trim();
                if (number.Length == 0) throw new InvalidDataException("invoice_list returned an empty invoice number");
                byInvoice[number] = item;
            }

            if (response.PageTotal > 0)
            {
                if (page >= response.PageTotal) break;
            }
            else if (response.Data.Count < limit)
            {
                break;
            }

            if (page == maximumPages) throw new InvalidDataException("invoice_list pagination exceeded safety limit");
        }
        return byInvoice.Values.ToArray();
    }

    private IEnumerable<InvoiceRecord> AccountRecords(Account account) =>
        repository.Invoices.LoadOrCreate().Where(record =>
            string.Equals(record.Environment, account.Environment, StringComparison.Ordinal) &&
            (record.SellerInvoice.Trim().Length == 0 ||
             string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal)));

    private static InvoiceRecord? Match(IReadOnlyList<InvoiceRecord> local, InvoiceListItem remote)
    {
        var number = remote.InvoiceNumber.Trim();
        var byNumber = local.Where(record =>
            string.Equals(record.InvoiceNumber.Trim(), number, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (byNumber.Length > 1)
            throw new AmbiguousInvoiceMatchException($"同一發票號碼對到 {byNumber.Length} 筆本機紀錄，已停止自動更新");
        if (byNumber.Length == 1) return byNumber[0];

        var orderId = remote.OrderId.Trim();
        if (orderId.Length == 0) return null;
        var byOrder = local.Where(record =>
            string.Equals(record.ApiOrderId.Trim(), orderId, StringComparison.Ordinal) ||
            string.Equals(record.OrderId.Trim(), orderId, StringComparison.Ordinal) ||
            string.Equals(record.OriginalOrderId.Trim(), orderId, StringComparison.Ordinal)).ToArray();
        if (byOrder.Length > 1)
            throw new AmbiguousInvoiceMatchException($"同一 OrderID 對到 {byOrder.Length} 筆本機紀錄，已停止自動更新");
        return byOrder.Length == 0 ? null : byOrder[0];
    }

    private InvoiceRecord NewSyncedRecord(Account account, InvoiceListItem item)
    {
        var orderId = item.OrderId.Trim();
        if (orderId.Length == 0) throw new InvalidDataException("光貿清單缺少 OrderID");
        var issuedAt = JoinDateTime(NormalizeDate(item.InvoiceDate), NormalizeTime(item.InvoiceTime));
        return new InvoiceRecord
        {
            Id = "sync-" + Guid.NewGuid().ToString("N"),
            SellerInvoice = account.SellerInvoice,
            Environment = account.Environment,
            RecordOrigin = RecordOrigins.Sync,
            OriginalOrderId = orderId,
            OrderId = orderId,
            ApiOrderId = orderId,
            InvoiceNumber = item.InvoiceNumber.Trim(),
            InvoiceState = item.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided,
            SentAt = issuedAt,
            Items = [],
        };
    }

    private void ApplyList(InvoiceRecord record, InvoiceListItem item, Account account)
    {
        var orderId = item.OrderId.Trim();
        if (orderId.Length == 0) throw new InvalidDataException("光貿清單缺少 OrderID");
        record.SellerInvoice = account.SellerInvoice;
        record.Environment = account.Environment;
        if (record.RecordOrigin.Trim().Length == 0) record.RecordOrigin = RecordOrigins.Local;
        record.InvoiceNumber = item.InvoiceNumber.Trim();
        record.OrderId = orderId;
        record.ApiOrderId = orderId;
        if (record.RecordOrigin == RecordOrigins.Sync || record.OriginalOrderId.Trim().Length == 0)
            record.OriginalOrderId = orderId;
        record.Source = InvoiceSourceInference.FromOrderId(orderId);
        record.InvoiceState = item.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided;
        record.UploadStatus = item.InvoiceStatus;
        record.UploadStatusText = UploadStatusText(item.InvoiceStatus);
        record.ErrorMessage = string.Empty;
        record.InvoiceDate = NormalizeDate(item.InvoiceDate);
        record.InvoiceTime = NormalizeTime(item.InvoiceTime);
        if (record.SentAt.Trim().Length == 0) record.SentAt = JoinDateTime(record.InvoiceDate, record.InvoiceTime);
        record.LastChecked = FormatDateTime(now());
        record.BuyerIdentifier = item.BuyerIdentifier.Trim();
        record.BuyerName = item.BuyerName.Trim();
        record.Amount = Money(item.TotalAmount, "發票總額");
        record.CarrierType = item.CarrierType.Trim();
        record.CarrierId1 = item.CarrierId1.Trim();
        record.CarrierId2 = item.CarrierId2.Trim();
        record.NpoBan = item.NpoBan.Trim();
        record.MainRemark = item.MainRemark.Trim();
        record.Delivery = Delivery(record.CarrierType, record.NpoBan);
    }

    private void ApplyQuery(InvoiceRecord record, QueryResult query, Account account)
    {
        var number = query.InvoiceNumber.Trim();
        if (number.Length == 0) throw new InvalidDataException("invoice_query 缺少發票號碼");
        if (record.InvoiceNumber.Trim().Length != 0 &&
            !string.Equals(record.InvoiceNumber.Trim(), number, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("invoice_query 發票號碼與同步目標不符");

        record.SellerInvoice = account.SellerInvoice;
        record.Environment = account.Environment;
        record.InvoiceNumber = number;
        var orderId = query.OrderId.Trim();
        if (orderId.Length != 0)
        {
            record.OrderId = orderId;
            record.ApiOrderId = orderId;
            if (record.RecordOrigin == RecordOrigins.Sync || record.OriginalOrderId.Trim().Length == 0)
                record.OriginalOrderId = orderId;
            record.Source = InvoiceSourceInference.FromOrderId(orderId);
        }
        record.InvoiceState = query.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided;
        if (query.InvoiceStatus != 0)
        {
            record.UploadStatus = query.InvoiceStatus;
            record.UploadStatusText = UploadStatusText(query.InvoiceStatus);
        }
        if (query.InvoiceDate.Trim().Length != 0) record.InvoiceDate = NormalizeDate(query.InvoiceDate);
        if (query.InvoiceTime.Trim().Length != 0) record.InvoiceTime = NormalizeTime(query.InvoiceTime);
        if (record.SentAt.Trim().Length == 0) record.SentAt = JoinDateTime(record.InvoiceDate, record.InvoiceTime);
        if (query.BuyerIdentifier.Trim().Length != 0) record.BuyerIdentifier = query.BuyerIdentifier.Trim();
        if (query.BuyerName.Trim().Length != 0) record.BuyerName = query.BuyerName.Trim();
        if (query.TotalAmount.Trim().Length != 0) record.Amount = Money(query.TotalAmount, "invoice_query 發票總額");
        if (query.CarrierType.Trim().Length != 0 || record.CarrierType.Length == 0) record.CarrierType = query.CarrierType.Trim();
        if (query.CarrierId1.Trim().Length != 0 || record.CarrierId1.Length == 0) record.CarrierId1 = query.CarrierId1.Trim();
        if (query.CarrierId2.Trim().Length != 0 || record.CarrierId2.Length == 0) record.CarrierId2 = query.CarrierId2.Trim();
        if (query.NpoBan.Trim().Length != 0 || record.NpoBan.Length == 0) record.NpoBan = query.NpoBan.Trim();
        if (query.DetailVatPresent) record.DetailVat = query.DetailVat;
        var items = ParseItems(query.ProductItems);
        if (items is not null) record.Items = items;
        record.Delivery = Delivery(record.CarrierType, record.NpoBan);
        record.ErrorMessage = string.Empty;
        record.LastChecked = FormatDateTime(now());
    }

    private static List<InvoiceItem>? ParseItems(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("invoice_query ProductItem 不是陣列");
        var items = new List<InvoiceItem>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invoice_query ProductItem 含非物件資料");
            var description = FirstText(element, "description", "Description");
            var quantityText = FirstScalar(element, "quantity", "Quantity");
            var unitPriceText = FirstScalar(element, "unit_price", "UnitPrice");
            var amountText = FirstScalar(element, "amount", "Amount");
            if (description.Length == 0 || quantityText.Length == 0 || unitPriceText.Length == 0 || amountText.Length == 0)
                throw new InvalidDataException("invoice_query ProductItem 缺少必要欄位");
            var quantity = FixedDecimal.Parse(quantityText);
            var unitPrice = FixedDecimal.Parse(unitPriceText);
            var amount = FixedDecimal.Parse(amountText);
            items.Add(new InvoiceItem
            {
                Description = description,
                Quantity = double.Parse(quantityText, NumberStyles.Float, CultureInfo.InvariantCulture),
                QuantityDecimal = quantity.ToString(),
                Unit = FirstText(element, "unit", "Unit"),
                UnitPrice = unitPrice.RoundInt64(),
                UnitPriceDecimal = unitPrice.ToString(),
                TaxType = FirstScalar(element, "tax_type", "TaxType") is { Length: > 0 } tax ? tax : "1",
                Amount = amount.RoundInt64(),
                AmountDecimal = amount.ToString(),
                Remark = FirstText(element, "remark", "Remark"),
                AllowSubtotalRounding = FixedDecimal.Multiply(quantity, unitPrice) != amount &&
                    FixedDecimal.Multiply(quantity, unitPrice).RoundInt64() == amount.RoundInt64(),
            });
        }
        return items;
    }

    private static bool SummaryDiffers(InvoiceRecord record, InvoiceListItem item)
    {
        var state = item.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided;
        return !string.Equals(record.InvoiceNumber.Trim(), item.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(record.OrderId.Trim(), item.OrderId.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.Source.Trim(), InvoiceSourceInference.FromOrderId(item.OrderId), StringComparison.Ordinal) ||
               !string.Equals(record.InvoiceState, state, StringComparison.Ordinal) ||
               record.UploadStatus != item.InvoiceStatus ||
               !string.Equals(record.InvoiceDate, NormalizeDate(item.InvoiceDate), StringComparison.Ordinal) ||
               !string.Equals(record.InvoiceTime, NormalizeTime(item.InvoiceTime), StringComparison.Ordinal) ||
               !string.Equals(record.BuyerIdentifier.Trim(), item.BuyerIdentifier.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.BuyerName.Trim(), item.BuyerName.Trim(), StringComparison.Ordinal) ||
               record.Amount != Money(item.TotalAmount, "發票總額") ||
               !string.Equals(record.CarrierType.Trim(), item.CarrierType.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.CarrierId1.Trim(), item.CarrierId1.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.CarrierId2.Trim(), item.CarrierId2.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.NpoBan.Trim(), item.NpoBan.Trim(), StringComparison.Ordinal) ||
               !string.Equals(record.MainRemark.Trim(), item.MainRemark.Trim(), StringComparison.Ordinal);
    }

    private static string Fingerprint(InvoiceRecord record) => JsonSerializer.Serialize(new
    {
        record.SellerInvoice,
        record.Environment,
        record.Source,
        record.RecordOrigin,
        record.OriginalOrderId,
        record.OrderId,
        record.ApiOrderId,
        record.InvoiceNumber,
        record.InvoiceState,
        record.CarrierType,
        record.CarrierId1,
        record.CarrierId2,
        record.NpoBan,
        record.Amount,
        record.Delivery,
        record.UploadStatus,
        record.UploadStatusText,
        record.InvoiceDate,
        record.InvoiceTime,
        record.BuyerIdentifier,
        record.BuyerName,
        record.MainRemark,
        record.DetailVat,
        record.Items,
    });

    private static bool IsInRange(InvoiceRecord record, DateOnly startDate, DateOnly endDate)
    {
        var text = record.InvoiceDate.Trim();
        if (text.Length == 0 && record.SentAt.Length >= 10) text = record.SentAt[..10];
        text = text.Replace("/", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
               date >= startDate && date <= endDate;
    }

    private Account CurrentAccount()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (settings.Environment == Environments.Test)
            return new Account(Environments.Test, AmegoDefaults.TestInvoice, AmegoDefaults.TestAppKey);
        var sellerInvoice = settings.ProductionInvoice.Trim();
        var appKey = repository.Settings.ProductionAppKey(settings).Trim();
        if (sellerInvoice.Length == 0 || appKey.Length == 0)
            throw new InvalidOperationException("正式環境尚未設定公司統編與 App Key");
        return new Account(Environments.Production, sellerInvoice, appKey);
    }

    private static string AccountKey(Account account) => account.Environment + "|" + account.SellerInvoice;

    private static IssueIdentity Identity(InvoiceRecord record, InvoiceListItem item) => new(
        record.InvoiceNumber.Trim().Length == 0 ? item.InvoiceNumber.Trim() : record.InvoiceNumber.Trim(),
        EffectiveOrderId(record).Length == 0 ? item.OrderId.Trim() : EffectiveOrderId(record));

    private void AddIssueProblem(
        List<string> problems,
        string accountKey,
        string invoiceNumber,
        string orderId,
        string issueType,
        string message)
    {
        try
        {
            issueStore.Record(accountKey, invoiceNumber, orderId, issueType, message, now());
        }
        catch (Exception error)
        {
            problems.Add($"同步異常記錄保存失敗：{error.Message}");
        }
    }

    private void TryRecordIssue(
        string accountKey,
        string invoiceNumber,
        string orderId,
        string issueType,
        string message)
    {
        try { issueStore.Record(accountKey, invoiceNumber, orderId, issueType, message, now()); }
        catch { }
    }

    private void ResolveIssues(
        List<string> problems,
        string accountKey,
        IssueIdentity identity,
        params string[] issueTypes)
    {
        try
        {
            issueStore.ResolveMatching(accountKey, identity.InvoiceNumber, identity.OrderId, now(), issueTypes);
        }
        catch (Exception error)
        {
            problems.Add($"同步異常解決狀態保存失敗：{error.Message}");
        }
    }

    private void ResolveAccountIssue(List<string> problems, string accountKey, string issueType)
    {
        try { issueStore.ResolveAccountType(accountKey, issueType, now()); }
        catch (Exception error) { problems.Add($"同步異常解決狀態保存失敗：{error.Message}"); }
    }

    private void TryResolveAccountIssue(string accountKey, string issueType)
    {
        try { issueStore.ResolveAccountType(accountKey, issueType, now()); }
        catch { }
    }

    private static string Delivery(string carrierType, string npoBan)
    {
        if (npoBan.Trim().Length != 0) return "捐贈";
        return carrierType.Trim() switch
        {
            "" => InvoiceService.DeliveryPaper,
            "amego" => "會員載具",
            "3J0002" => "手機條碼",
            "CQ0001" => "自然人憑證",
            var other => other,
        };
    }

    private static long Money(string text, string field)
    {
        try { return FixedDecimal.Parse(text).RoundInt64(); }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new InvalidDataException($"{field}格式錯誤：{text}", error);
        }
    }

    private static string UploadStatusText(int status) => status switch
    {
        UploadStatuses.Pending => "待處理",
        UploadStatuses.Uploading => "上傳中",
        UploadStatuses.Uploaded => "已上傳",
        UploadStatuses.Processing => "處理中",
        UploadStatuses.Confirming => "待確認",
        UploadStatuses.Error => "錯誤",
        UploadStatuses.Complete => "完成",
        0 => string.Empty,
        _ => $"狀態 {status}",
    };

    private static string NormalizeDate(string value)
    {
        value = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal);
        return value.Length == 8 && value.All(char.IsAsciiDigit)
            ? value[..4] + "/" + value[4..6] + "/" + value[6..]
            : value;
    }

    private static string NormalizeTime(string value)
    {
        value = value.Trim();
        if (value.Length == 6 && value.All(char.IsAsciiDigit)) return value[..2] + ":" + value[2..4] + ":" + value[4..];
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) && unix >= 1_000_000_000)
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return value;
    }

    private static string JoinDateTime(string date, string time) =>
        date.Length == 0 ? string.Empty : time.Length == 0 ? date : date + " " + time;

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private static string FirstText(JsonElement element, string first, string second) =>
        Text(element, first) is { Length: > 0 } value ? value : Text(element, second);

    private static string FirstScalar(JsonElement element, string first, string second) =>
        Scalar(element, first) is { Length: > 0 } value ? value : Scalar(element, second);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string Scalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return string.Empty;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText().Trim(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => throw new InvalidDataException($"ProductItem.{name} 必須為文字或數值"),
        };
    }

    private sealed record Account(string Environment, string SellerInvoice, string AppKey);
    private sealed record IssueIdentity(string InvoiceNumber, string OrderId);

    private sealed class AmbiguousInvoiceMatchException : InvalidDataException
    {
        public AmbiguousInvoiceMatchException(string message) : base(message)
        {
        }
    }
}
