using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Digiwin;
using CYInvoice.Core.Imports.Mo;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public static class InvoiceSources
{
    public const string Manual = "手動";
    public const string Mo = "MO店+";
    public const string Coupang = "酷澎";
    public const string Digiwin = "鼎新ERP";
}

public sealed record NameLookup(
    string Name = "",
    bool Local = false,
    bool LookupSucceeded = false,
    string ApiName = "");

public sealed record IssueResult(
    InvoiceRecord Record,
    bool Opened = false,
    bool Unknown = false,
    Exception? LocalSaveError = null);

public sealed class LocalPersistenceException(InvoiceRecord record, Exception innerException)
    : Exception(
        $"光貿已成功開立發票 {record.InvoiceNumber}，但本機紀錄保存失敗：{innerException.Message}；請勿直接重送，重新整理或再次操作時會先回查光貿",
        innerException)
{
    public InvoiceRecord Record { get; } = record;
    public string InvoiceNumber => Record.InvoiceNumber;
}

public sealed class PartialRefreshException(IReadOnlyList<InvoiceRecord> records, string message)
    : Exception(message)
{
    public IReadOnlyList<InvoiceRecord> Records { get; } = records;
}

public sealed class InvoiceService
{
    public const string DeliveryPaper = "紙本";
    private const string TestBuyerIdentifier = "28080623";
    private static readonly TimeSpan RecoveryQueryTimeout = TimeSpan.FromSeconds(20);
    private readonly LocalRepository repository;
    private readonly Func<string, string, IAmegoGateway> gatewayFactory;
    private readonly Func<DateTimeOffset> now;
    private readonly Action<string, StatusUpdate>? statusUpdater;
    private readonly SemaphoreSlim issueGate = new(1, 1);
    private readonly SemaphoreSlim pdfGate = new(1, 1);
    private readonly object activeIssueGate = new();
    private readonly HashSet<string> activeIssueKeys = new(StringComparer.Ordinal);
    private readonly object gatewayGate = new();
    private IAmegoGateway? cachedGateway;
    private string cachedGatewayKey = string.Empty;

    public InvoiceService(
        LocalRepository repository,
        Func<string, string, IAmegoGateway>? gatewayFactory = null,
        Func<DateTimeOffset>? now = null,
        Action<string, StatusUpdate>? statusUpdater = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.gatewayFactory = gatewayFactory ?? ((invoice, appKey) => new AmegoClient(invoice, appKey));
        this.now = now ?? (() => DateTimeOffset.Now);
        this.statusUpdater = statusUpdater;
    }

    public async Task<string> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = repository.Settings.LoadOrCreate();
        var ban = settings.Environment == Environments.Production
            ? settings.ProductionInvoice.Trim()
            : AmegoDefaults.TestInvoice;
        var (gateway, _) = GetGateway();
        BanResponse response;
        try
        {
            response = await gateway.QueryBanAsync([ban], cancellationToken).ConfigureAwait(false);
        }
        catch (AmegoApiException error) when (error.Code == 99)
        {
            return string.Empty;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("API 健康檢查失敗", error);
        }

        return response.Data.FirstOrDefault(item => item.Ban.Trim() == ban)?.Name.Trim() ?? string.Empty;
    }

    public InvoicePdfEligibility GetInvoicePdfEligibility(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var environment = repository.Settings.LoadOrCreate().Environment;
        var company = IsCompanyBuyer(record);
        if (record.Environment != environment)
            return new(false, company, "這筆發票屬於其他環境，請切換到正確環境後再取得官方 PDF");
        if (record.InvoiceState != InvoiceStates.Opened)
            return new(false, company, "只有已開立且未作廢的發票可以取得官方 PDF");
        if (record.InvoiceNumber.Trim().Length == 0)
            return new(false, company, "這筆紀錄沒有發票號碼");
        if (record.Delivery != DeliveryPaper)
            return new(false, company, "只有紙本發票可以下載官方 PDF");
        return new(true, company, string.Empty);
    }

    public IReadOnlyList<InvoicePdfStyle> GetInvoicePdfStyles(InvoiceRecord record) =>
        GetInvoicePdfEligibility(record).CompanyBuyer ? InvoicePdfStyles.Company : InvoicePdfStyles.Consumer;

    public async Task<InvoicePdfDocument> GetInvoicePdfAsync(
        InvoiceRecord record,
        int downloadStyle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await pdfGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = repository.Invoices.LoadOrCreate().SingleOrDefault(item => item.Id == record.Id)
                ?? throw new InvalidOperationException("本機找不到這筆發票紀錄，請重新整理清單");
            if (!string.Equals(stored.InvoiceNumber.Trim(), record.InvoiceNumber.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("發票紀錄已變更，請重新開啟詳細資訊");
            var eligibility = GetInvoicePdfEligibility(stored);
            if (!eligibility.Allowed) throw new InvalidOperationException(eligibility.Reason);
            var style = InvoicePdfStyles.Require(downloadStyle);
            if (!eligibility.CompanyBuyer && style.Code != InvoicePdfStyles.A4.Code)
                throw new InvalidOperationException("一般消費者紙本發票只支援 A4 整張版型");

            var (gateway, environment) = GetGateway();
            var cachePath = InvoicePdfCache.PathFor(
                repository.InvoicePdfCacheDirectory,
                environment,
                stored.InvoiceNumber,
                style.Code,
                now());
            if (await InvoicePdfCache.TryReadAsync(cachePath, cancellationToken).ConfigureAwait(false) is not null)
                return new(cachePath, stored.InvoiceNumber.Trim(), style, FromCache: true);

            byte[] bytes;
            try
            {
                bytes = await gateway.DownloadInvoicePdfAsync(
                    stored.InvoiceNumber.Trim(),
                    style.Code,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AmegoApiException error) when (error.Code == 99)
            {
                throw new InvalidOperationException("光貿尚未提供這張發票的官方 PDF，請稍後再試", error);
            }
            await InvoicePdfCache.WriteAsync(cachePath, bytes, cancellationToken).ConfigureAwait(false);
            return new(cachePath, stored.InvoiceNumber.Trim(), style, FromCache: false);
        }
        finally
        {
            pdfGate.Release();
        }
    }

    public async Task<NameLookup> LookupBuyerNameAsync(
        string ban,
        CancellationToken cancellationToken = default)
    {
        ban = ValidateBuyerBan(ban);
        if (repository.BuyerNames.TryLookup(ban, out var localName))
        {
            return new NameLookup(localName, Local: true);
        }
        var (gateway, _) = GetGateway();
        return await LookupBuyerNameFromApiCoreAsync(gateway, ban, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NameLookup> LookupBuyerNameFromApiAsync(
        string ban,
        CancellationToken cancellationToken = default)
    {
        ban = ValidateBuyerBan(ban);
        var (gateway, _) = GetGateway();
        return await LookupBuyerNameFromApiCoreAsync(gateway, ban, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IssueResult> IssueManualAsync(
        InvoiceDraft draft,
        CancellationToken cancellationToken = default)
    {
        var lookup = new NameLookup();
        if (draft.CompanyBuyer)
        {
            lookup = await LookupBuyerNameAsync(draft.BuyerIdentifier, cancellationToken).ConfigureAwait(false);
            if (lookup.Name.Length != 0)
            {
                draft.BuyerName = lookup.Name;
            }
        }
        return await IssueManualWithLookupAsync(draft, lookup, cancellationToken).ConfigureAwait(false);
    }

    public Task<IssueResult> IssueManualWithLookupAsync(
        InvoiceDraft draft,
        NameLookup lookup,
        CancellationToken cancellationToken = default) =>
        IssueAsync(
            draft,
            new IssueOptions(InvoiceSources.Manual, draft.OrderId, DeliveryPaper),
            lookup,
            cancellationToken);

    public async Task<IssueResult> IssueMoAsync(MoOrder order, CancellationToken cancellationToken = default)
    {
        if (IsMoCompany(order))
        {
            var lookup = await LookupBuyerNameAsync(order.BuyerBan, cancellationToken).ConfigureAwait(false);
            if (lookup.Name.Trim().Length == 0)
            {
                throw new InvalidOperationException("光貿查詢成功但沒有公司名稱，請先在匯入確認視窗輸入買方名稱");
            }
            order.BuyerName = lookup.Name;
            return await IssueMoWithLookupAsync(order, lookup, cancellationToken).ConfigureAwait(false);
        }
        return await IssueMoWithLookupAsync(order, new NameLookup(), cancellationToken).ConfigureAwait(false);
    }

    public Task<IssueResult> IssueMoWithLookupAsync(
        MoOrder order,
        NameLookup lookup,
        CancellationToken cancellationToken = default)
    {
        var company = IsMoCompany(order);
        ResolveImportedBuyerName(company, lookup, order.BuyerName, value => order.BuyerName = value);
        if (company)
        {
            order.Carrier = "公司戶";
            order.CarrierId1 = string.Empty;
            order.CarrierId2 = string.Empty;
            order.NpoBan = string.Empty;
        }
        else
        {
            order.BuyerBan = string.Empty;
            order.BuyerName = order.BuyerName.Trim().Length == 0 ? "消費者" : order.BuyerName.Trim();
            order.Carrier = MoCarriers.Member;
            order.CarrierId1 = order.CarrierId1.Trim().Length == 0 ? "motmp_" + order.OrderId : order.CarrierId1;
            order.CarrierId2 = order.CarrierId2.Trim().Length == 0 ? order.CarrierId1 : order.CarrierId2;
        }

        var draft = DraftFromImportedOrder(
            order.OrderId,
            company,
            order.BuyerBan,
            order.BuyerName,
            order.Items,
            order.TotalAmount,
            order.MainRemark);
        var carrierType = CarrierType(order.Carrier);
        var delivery = company ? DeliveryPaper : order.Carrier.Trim();
        if (order.NpoBan.Trim().Length != 0)
        {
            delivery = "捐贈";
        }
        if (delivery.Length == 0)
        {
            delivery = DeliveryPaper;
        }
        var testPrivacy = repository.Settings.LoadOrCreate().Environment == Environments.Test;
        return IssueAsync(
            draft,
            new IssueOptions(
                Source: InvoiceSources.Mo,
                OriginalOrderId: order.OrderId,
                Delivery: delivery,
                BuyerAddress: order.BuyerAddress,
                BuyerPhone: order.BuyerPhone,
                BuyerEmail: order.BuyerEmail,
                BuyerName: order.BuyerName,
                CarrierType: carrierType,
                CarrierId1: order.CarrierId1,
                CarrierId2: order.CarrierId2,
                NpoBan: order.NpoBan,
                ApiBuyerName: testPrivacy ? "測試消費者" : string.Empty,
                TestPrivacy: testPrivacy),
            lookup,
            cancellationToken);
    }

    public async Task<IssueResult> IssueCoupangAsync(CoupangOrder order, CancellationToken cancellationToken = default)
    {
        var company = order.BuyerBan.Trim().Length != 0;
        var lookup = new NameLookup();
        if (company)
        {
            lookup = await LookupBuyerNameAsync(order.BuyerBan, cancellationToken).ConfigureAwait(false);
            if (lookup.Name.Length == 0)
            {
                throw new InvalidOperationException("光貿查不到公司名稱，請在匯入確認視窗輸入買方名稱");
            }
            order.BuyerName = lookup.Name;
        }
        return await IssueCoupangWithLookupAsync(order, lookup, cancellationToken).ConfigureAwait(false);
    }

    public Task<IssueResult> IssueCoupangWithLookupAsync(
        CoupangOrder order,
        NameLookup lookup,
        CancellationToken cancellationToken = default)
    {
        var company = order.BuyerBan.Trim().Length != 0;
        ResolveImportedBuyerName(company, lookup, order.BuyerName, value => order.BuyerName = value);
        if (!company)
        {
            order.BuyerBan = string.Empty;
            order.BuyerName = order.BuyerName.Trim().Length == 0 ? "消費者" : order.BuyerName.Trim();
        }
        var draft = DraftFromImportedOrder(
            order.OrderId,
            company,
            order.BuyerBan,
            order.BuyerName,
            order.Items,
            order.TotalAmount,
            string.Empty);
        var testPrivacy = repository.Settings.LoadOrCreate().Environment == Environments.Test;
        return IssueAsync(
            draft,
            new IssueOptions(
                Source: InvoiceSources.Coupang,
                OriginalOrderId: order.OrderId,
                Delivery: DeliveryPaper,
                BuyerName: order.BuyerName,
                ApiBuyerName: testPrivacy ? "測試消費者" : string.Empty,
                TestPrivacy: testPrivacy),
            lookup,
            cancellationToken);
    }

    public Task<IssueResult> IssueDigiwinWithLookupAsync(
        DigiwinOrder order,
        NameLookup lookup,
        CancellationToken cancellationToken = default)
    {
        var ban = order.BuyerBan.Trim();
        var name = order.BuyerName.Trim();
        if ((ban.Length == 0) != (name.Length == 0))
            throw new InvalidOperationException("鼎新公司統編與買方名稱必須同時填寫或同時留空");
        var company = ban.Length != 0;
        if (company && !lookup.Local && !lookup.LookupSucceeded)
            throw new InvalidOperationException("買方名稱尚未完成查詢，已擋下且未送出");

        var draft = DraftFromImportedOrder(
            order.OrderId,
            company,
            company ? ban : string.Empty,
            company ? name : string.Empty,
            order.Items,
            order.TotalAmount,
            string.Empty);
        return IssueAsync(
            draft,
            new IssueOptions(
                Source: InvoiceSources.Digiwin,
                OriginalOrderId: order.OrderId,
                Delivery: DeliveryPaper,
                BuyerName: company ? name : string.Empty),
            lookup,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceRecord>> RefreshAllAsync(
        CancellationToken cancellationToken = default)
    {
        var records = repository.Invoices.LoadOrCreate().ToList();
        if (records.Count == 0)
        {
            return records;
        }

        var (gateway, environment) = GetGateway();
        var problems = new List<string>();
        foreach (var record in records)
        {
            if (record.Environment.Length != 0 && record.Environment != environment)
            {
                continue;
            }
            if (record.InvoiceState == InvoiceStates.Failed)
            {
                continue;
            }

            QueryResponse query;
            try
            {
                query = record.InvoiceNumber.Trim().Length != 0
                    ? await gateway.QueryByInvoiceNumberAsync(record.InvoiceNumber, cancellationToken).ConfigureAwait(false)
                    : await gateway.QueryByOrderIdAsync(
                        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId : record.OriginalOrderId,
                        cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                record.LastChecked = FormatDateTime(now());
                if (record.InvoiceState == InvoiceStates.Unknown && error is AmegoApiException { Code: 71 })
                {
                    record.ErrorMessage = "查無發票；原結果仍不明，未自動重送";
                }
                try
                {
                    SaveStatus(record.Id, CreateStatusUpdate(record));
                }
                catch (Exception saveError)
                {
                    problems.Add($"{record.OriginalOrderId}: 本機保存失敗：{saveError.Message}");
                }
                problems.Add($"{record.OriginalOrderId}: {error.Message}");
                continue;
            }

            try
            {
                VerifyStoredQueryResult(record, query.Data);
            }
            catch (Exception error)
            {
                problems.Add($"{record.OriginalOrderId}: {error.Message}");
                continue;
            }

            ApplyQueryResult(record, query.Data, record.UploadStatus, record.UploadStatusText);
            try
            {
                SaveStatus(record.Id, CreateStatusUpdate(record));
            }
            catch (Exception error)
            {
                problems.Add($"{record.OriginalOrderId}: 本機保存失敗：{error.Message}");
                continue;
            }

            if (record.BuyerNameNeedsMemory)
            {
                try
                {
                    RememberBuyerName(record, new NameLookup(LookupSucceeded: true), record.BuyerName);
                }
                catch (Exception error)
                {
                    problems.Add($"{record.OriginalOrderId}: 買方名稱記憶失敗：{error.Message}");
                }
            }

            if (record.InvoiceNumber.Length == 0)
            {
                continue;
            }
            StatusResponse status;
            try
            {
                status = await gateway.StatusAsync([record.InvoiceNumber], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                problems.Add($"{record.InvoiceNumber}: {error.Message}");
                continue;
            }
            if (status.Data.Count == 0)
            {
                continue;
            }
            var matchedStatuses = status.Data
                .Where(item => string.Equals(
                    item.InvoiceNumber.Trim(),
                    record.InvoiceNumber.Trim(),
                    StringComparison.Ordinal))
                .ToArray();
            if (matchedStatuses.Length != 1)
            {
                problems.Add(matchedStatuses.Length == 0
                    ? $"{record.InvoiceNumber}: 上傳狀態回覆未包含指定發票"
                    : $"{record.InvoiceNumber}: 上傳狀態回覆包含重複發票");
                continue;
            }
            record.UploadStatus = matchedStatuses[0].Status;
            record.UploadStatusText = UploadStatusText(record.UploadStatus);
            try
            {
                SaveStatus(record.Id, CreateStatusUpdate(record));
            }
            catch (Exception error)
            {
                problems.Add($"{record.OriginalOrderId}: 上傳狀態保存失敗：{error.Message}");
            }
        }

        var updated = repository.Invoices.LoadOrCreate();
        if (problems.Count != 0)
        {
            throw new PartialRefreshException(updated, "部分紀錄更新失敗：" + string.Join("；", problems));
        }
        return updated;
    }

    private async Task<IssueResult> IssueAsync(
        InvoiceDraft draft,
        IssueOptions options,
        NameLookup lookup,
        CancellationToken cancellationToken)
    {
        InvoiceValidator.Validate(draft);
        options = options with
        {
            Source = options.Source.Length == 0 ? InvoiceSources.Manual : options.Source,
            OriginalOrderId = options.OriginalOrderId.Length == 0 ? draft.OrderId : options.OriginalOrderId,
            Delivery = options.Delivery.Length == 0 ? DeliveryPaper : options.Delivery,
        };

        var (gateway, environment) = GetGateway();
        var createdAt = now();
        var apiOrderId = RequestOrderId(draft, options);
        if (environment == Environments.Test)
            apiOrderId = TestOrderIdPrefix.Apply(apiOrderId, DateOnly.FromDateTime(createdAt.DateTime));
        var activeKey = environment + "\0" + apiOrderId;
        lock (activeIssueGate)
        {
            if (!activeIssueKeys.Add(activeKey))
            {
                throw new InvalidOperationException("此訂單正在開立，請勿重複送出");
            }
        }

        try
        {
            await issueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var records = repository.Invoices.LoadOrCreate();
                var uncertain = FindUncertainRecord(records, apiOrderId, environment);
                if (uncertain is not null)
                {
                    var uncertainOrderId = EffectiveRecordOrderId(uncertain);
                    try
                    {
                        using var recoveryTimeout = new CancellationTokenSource(RecoveryQueryTimeout);
                        var query = await gateway.QueryByOrderIdAsync(uncertainOrderId, recoveryTimeout.Token).ConfigureAwait(false);
                        VerifyStoredQueryResult(uncertain, query.Data);
                        ApplyQueryResult(uncertain, query.Data, uncertain.UploadStatus, uncertain.UploadStatusText);
                        SaveStatus(uncertain.Id, CreateStatusUpdate(uncertain));
                        if (uncertain.InvoiceState == InvoiceStates.Opened)
                        {
                            RememberBuyerName(
                                uncertain,
                                new NameLookup(LookupSucceeded: true),
                                uncertain.BuyerName);
                            return new IssueResult(uncertain, Opened: true);
                        }
                    }
                    catch (AmegoApiException error) when (error.Code == 71)
                    {
                        // AMEGO explicitly confirmed that the previous uncertain OrderID is not present.
                        // It is therefore safe to let AMEGO decide the new issue request normally.
                    }
                    catch (Exception error)
                    {
                        throw new InvalidOperationException(
                            "上一筆相同 OrderID 的開立結果尚未確認，回查光貿失敗，本次未重送：" + error.Message,
                            error);
                    }
                }

                if (environment == Environments.Test)
                {
                    var safeBuyerName = "測試消費者";
                    if (draft.CompanyBuyer)
                    {
                        var testLookup = await LookupBuyerNameFromApiCoreAsync(
                            gateway, TestBuyerIdentifier, cancellationToken).ConfigureAwait(false);
                        safeBuyerName = testLookup.ApiName.Trim();
                        if (safeBuyerName.Length == 0)
                            throw new InvalidOperationException($"光貿測試統編 {TestBuyerIdentifier} 查不到買受人名稱，已停止送出");
                    }
                    options = options with { TestPrivacy = true, ApiBuyerName = safeBuyerName };
                }

                var manualName = draft.BuyerName.Trim();
                var rememberLocalCorrection = draft.CompanyBuyer && lookup.Local && manualName.Length != 0 &&
                    !string.Equals(manualName, lookup.Name.Trim(), StringComparison.Ordinal);
                var rememberMissingApiName = draft.CompanyBuyer && !lookup.Local && lookup.LookupSucceeded && lookup.ApiName.Trim().Length == 0;
                var record = new InvoiceRecord
                {
                    Id = NewRecordId(createdAt),
                    Source = options.Source,
                    OriginalOrderId = options.OriginalOrderId,
                    OrderId = draft.OrderId,
                    Attempt = 1,
                    BuyerIdentifier = NormalizedBuyerIdentifier(draft),
                    BuyerName = EffectiveBuyerName(draft, options.BuyerName),
                    ApiOrderId = apiOrderId,
                    CarrierType = options.CarrierType,
                    CarrierId1 = options.CarrierId1,
                    CarrierId2 = options.CarrierId2,
                    NpoBan = options.NpoBan,
                    Amount = draft.TotalAmount,
                    Delivery = options.Delivery,
                    InvoiceState = InvoiceStates.Changing,
                    Environment = environment,
                    SentAt = FormatDateTime(createdAt),
                    Items = draft.Items.ToList(),
                    MainRemark = draft.MainRemark,
                    DetailVat = DetailVat(draft),
                    BuyerNameNeedsMemory = rememberLocalCorrection || rememberMissingApiName,
                };
                repository.Invoices.Append(record);

                options = options with { ApiOrderId = apiOrderId };
                IssueResponse response;
                try
                {
                    response = await gateway.IssueAsync(BuildIssueRequest(draft, options), cancellationToken).ConfigureAwait(false);
                }
                catch (AmegoApiException error)
                {
                    record.InvoiceState = InvoiceStates.Failed;
                    record.ErrorMessage = error.Message;
                    try
                    {
                        SaveStatus(record.Id, CreateStatusUpdate(record));
                    }
                    catch (Exception saveError)
                    {
                        throw new AggregateException(error, saveError);
                    }
                    throw;
                }
                catch (Exception issueError)
                {
                    QueryResponse? query = null;
                    Exception? queryError;
                    try
                    {
                        using var recoveryTimeout = new CancellationTokenSource(RecoveryQueryTimeout);
                        query = await gateway.QueryByOrderIdAsync(apiOrderId, recoveryTimeout.Token).ConfigureAwait(false);
                        VerifyQueryResult(record, draft, query.Data, string.Empty);
                        queryError = null;
                    }
                    catch (Exception error)
                    {
                        queryError = error;
                    }

                    if (queryError is null && query is not null)
                    {
                        ApplyQueryResult(record, query.Data, 0, string.Empty);
                        try
                        {
                            PersistOpened(record, lookup, draft.BuyerName);
                        }
                        catch (Exception saveError)
                        {
                            throw new LocalPersistenceException(record, saveError);
                        }
                        return new IssueResult(record, Opened: true);
                    }

                    record.InvoiceState = InvoiceStates.Unknown;
                    record.ErrorMessage = issueError.Message + "；嚴格回查未確認成功：" + queryError!.Message;
                    try
                    {
                        SaveStatus(record.Id, CreateStatusUpdate(record));
                    }
                    catch (Exception saveError)
                    {
                        throw new InvalidOperationException(
                            $"開立結果不明且本機狀態保存失敗：{saveError.Message}；原始錯誤：{issueError.Message}",
                            issueError);
                    }
                    throw new UnknownInvoiceResultException(record, issueError);
                }

                record.InvoiceNumber = response.InvoiceNumber.Trim();
                if (record.InvoiceNumber.Length == 0)
                {
                    record.InvoiceState = InvoiceStates.Unknown;
                    record.ErrorMessage = "光貿回覆成功但沒有發票號碼";
                    Exception? saveError = null;
                    try
                    {
                        SaveStatus(record.Id, CreateStatusUpdate(record));
                    }
                    catch (Exception error)
                    {
                        saveError = error;
                        record.ErrorMessage += "；本機狀態保存失敗：" + error.Message;
                    }
                    throw new UnknownInvoiceResultException(record, saveError);
                }
                if (response.InvoiceTime > 0)
                {
                    var issuedAt = DateTimeOffset.FromUnixTimeSeconds(response.InvoiceTime).ToLocalTime();
                    record.InvoiceDate = issuedAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
                    record.InvoiceTime = issuedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }

                try
                {
                    var query = await gateway.QueryByInvoiceNumberAsync(record.InvoiceNumber, cancellationToken).ConfigureAwait(false);
                    VerifyQueryResult(record, draft, query.Data, record.InvoiceNumber);
                    ApplyQueryResult(record, query.Data, 0, string.Empty);
                }
                catch (Exception queryError)
                {
                    record.InvoiceState = InvoiceStates.Opened;
                    record.ErrorMessage = "發票已開立；嚴格回查待確認：" + queryError.Message;
                    record.LastChecked = FormatDateTime(now());
                }

                try
                {
                    PersistOpened(record, lookup, draft.BuyerName);
                }
                catch (Exception saveError)
                {
                    throw new LocalPersistenceException(record, saveError);
                }
                return new IssueResult(record, Opened: true);
            }
            finally
            {
                issueGate.Release();
            }
        }
        finally
        {
            lock (activeIssueGate)
            {
                activeIssueKeys.Remove(activeKey);
            }
        }
    }

    private (IAmegoGateway Gateway, string Environment) GetGateway()
    {
        var settings = repository.Settings.LoadOrCreate();
        var invoice = AmegoDefaults.TestInvoice;
        var appKey = AmegoDefaults.TestAppKey;
        if (settings.Environment == Environments.Production)
        {
            invoice = settings.ProductionInvoice.Trim();
            try
            {
                appKey = repository.Settings.ProductionAppKey(settings);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("解密正式 App Key 失敗", error);
            }
            if (invoice.Length == 0 || appKey.Trim().Length == 0)
            {
                throw new InvalidOperationException("正式環境尚未設定公司統編與 App Key");
            }
        }

        var cacheKey = settings.Environment + "\0" + invoice + "\0" + appKey;
        lock (gatewayGate)
        {
            if (cachedGateway is null || cachedGatewayKey != cacheKey)
            {
                cachedGateway = gatewayFactory(invoice, appKey);
                cachedGatewayKey = cacheKey;
            }
            return (cachedGateway, settings.Environment);
        }
    }

    private static async Task<NameLookup> LookupBuyerNameFromApiCoreAsync(
        IAmegoGateway gateway,
        string ban,
        CancellationToken cancellationToken)
    {
        BanResponse response;
        try
        {
            response = await gateway.QueryBanAsync([ban], cancellationToken).ConfigureAwait(false);
        }
        catch (AmegoApiException error) when (error.Code == 99)
        {
            return new NameLookup(LookupSucceeded: true);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("查詢買受人名稱失敗", error);
        }
        var apiName = response.Data.FirstOrDefault(item => item.Ban.Trim() == ban)?.Name.Trim() ?? string.Empty;
        return new NameLookup(apiName, LookupSucceeded: true, ApiName: apiName);
    }

    private static string ValidateBuyerBan(string ban)
    {
        ban = ban.Trim();
        if (ban.Length != 8) throw new InvalidOperationException("公司統編必須為 8 碼");
        if (!ban.All(character => character is >= '0' and <= '9'))
            throw new InvalidOperationException("公司統編必須為 8 碼數字");
        return ban;
    }

    private static bool IsCompanyBuyer(InvoiceRecord record)
    {
        var buyerIdentifier = record.BuyerIdentifier.Trim();
        return buyerIdentifier.Length != 0 && buyerIdentifier != "0000000000";
    }

    private static IssueRequest BuildIssueRequest(InvoiceDraft draft, IssueOptions options)
    {
        var items = draft.Items.Select((item, index) => new ProductItem
        {
            Description = options.TestPrivacy ? $"測試商品 {index + 1}" : item.Description,
            Quantity = NumericValue(item.QuantityDecimal, item.Quantity),
            Unit = item.Unit.Trim(),
            UnitPrice = NumericValue(item.UnitPriceDecimal, item.UnitPrice),
            Amount = NumericValue(item.AmountDecimal, item.Amount),
            Remark = options.TestPrivacy ? string.Empty : item.Remark,
            TaxType = 1,
        }).ToList();
        var totals = InvoiceCalculator.CalculateTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax);
        var sales = draft.CompanyBuyer ? totals.SalesAmount : totals.TotalAmount;
        var tax = draft.CompanyBuyer ? totals.TaxAmount : 0;
        var buyerIdentifier = NormalizedBuyerIdentifier(draft);
        var buyerName = RequestBuyerName(draft, options);
        var buyerAddress = options.BuyerAddress;
        var buyerPhone = options.BuyerPhone;
        var buyerEmail = options.BuyerEmail;
        var mainRemark = draft.MainRemark;
        var carrierId1 = options.CarrierId1;
        var carrierId2 = options.CarrierId2;
        var npoBan = options.NpoBan;
        if (options.TestPrivacy)
        {
            buyerIdentifier = draft.CompanyBuyer ? TestBuyerIdentifier : "0000000000";
            buyerName = options.ApiBuyerName.Trim().Length == 0 ? "測試消費者" : options.ApiBuyerName.Trim();
            buyerAddress = buyerPhone = buyerEmail = mainRemark = npoBan = string.Empty;
            carrierId1 = options.CarrierType.Length == 0 ? string.Empty : "cyinvoice-test@example.com";
            carrierId2 = carrierId1;
        }

        return new IssueRequest
        {
            OrderId = RequestOrderId(draft, options),
            BuyerIdentifier = buyerIdentifier,
            BuyerName = buyerName,
            BuyerAddress = buyerAddress,
            BuyerTelephoneNumber = buyerPhone,
            BuyerEmailAddress = buyerEmail,
            CarrierType = options.CarrierType,
            CarrierId1 = carrierId1,
            CarrierId2 = carrierId2,
            NpoBan = npoBan,
            MainRemark = mainRemark,
            ProductItems = items,
            SalesAmount = sales,
            FreeTaxSalesAmount = 0L,
            ZeroTaxSalesAmount = 0L,
            TaxType = 1,
            TaxRate = "0.05",
            TaxAmount = tax,
            TotalAmount = totals.TotalAmount,
            DetailVat = DetailVat(draft),
            DetailAmountRound = draft.Items.Any(item => item.AllowSubtotalRounding) ? 1 : 0,
        };
    }

    private static object NumericValue(string text, double fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : JsonNumber(text);

    private static object NumericValue(string text, long fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : JsonNumber(text);

    private static JsonElement JsonNumber(string text)
    {
        using var document = JsonDocument.Parse(text.Trim());
        if (document.RootElement.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException("數值格式錯誤");
        }
        return document.RootElement.Clone();
    }

    private void PersistOpened(InvoiceRecord record, NameLookup lookup, string manualName)
    {
        SaveStatus(record.Id, CreateStatusUpdate(record));
        RememberBuyerName(record, lookup, manualName);
    }

    private void RememberBuyerName(InvoiceRecord record, NameLookup lookup, string manualName)
    {
        if (!record.BuyerNameNeedsMemory || record.InvoiceState != InvoiceStates.Opened)
        {
            return;
        }
        repository.BuyerNames.RememberAfterSuccessfulInvoice(
            record.BuyerIdentifier,
            lookup.Local || lookup.LookupSucceeded,
            lookup.ApiName,
            manualName,
            invoiceSucceeded: true);
    }

    private void SaveStatus(string id, StatusUpdate update)
    {
        if (statusUpdater is not null)
        {
            statusUpdater(id, update);
            return;
        }
        repository.Invoices.UpdateStatus(id, update);
    }

    private StatusUpdate CreateStatusUpdate(InvoiceRecord record) => new(
        record.InvoiceNumber,
        record.InvoiceState,
        record.UploadStatus,
        record.UploadStatusText,
        record.ErrorMessage,
        record.InvoiceDate,
        record.InvoiceTime,
        FormatDateTime(now()));

    private void ApplyQueryResult(InvoiceRecord record, QueryResult query, int uploadStatus, string uploadText)
    {
        if (query.InvoiceNumber.Length != 0)
        {
            record.InvoiceNumber = query.InvoiceNumber;
        }
        record.InvoiceState = query.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided;
        if (query.InvoiceDate.Length != 0)
        {
            record.InvoiceDate = NormalizeDate(query.InvoiceDate);
        }
        if (query.InvoiceTime.Length != 0)
        {
            record.InvoiceTime = query.InvoiceTime;
        }
        record.UploadStatus = uploadStatus;
        record.UploadStatusText = uploadText;
        record.ErrorMessage = string.Empty;
        record.LastChecked = FormatDateTime(now());
    }

    private static void VerifyStoredQueryResult(InvoiceRecord record, QueryResult query)
    {
        if (query.InvoiceNumber.Trim().Length == 0)
        {
            throw new InvalidDataException("回查缺少發票號碼");
        }
        if (record.InvoiceNumber.Length != 0 && query.InvoiceNumber.Trim() != record.InvoiceNumber.Trim())
        {
            throw new InvalidDataException("回查發票號碼與本機紀錄不符");
        }
        var expectedOrderId = EffectiveRecordOrderId(record);
        if (query.OrderId.Trim() != expectedOrderId)
        {
            throw new InvalidDataException("回查訂單編號與本次送出不符");
        }
        if (!long.TryParse(query.TotalAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            total != record.Amount)
        {
            throw new InvalidDataException($"回查發票總額不符：取得 {query.TotalAmount}，預期 {record.Amount}");
        }
        if (record.Items.Count == 0)
        {
            return;
        }
        if (!query.DetailVatPresent)
        {
            throw new InvalidDataException("回查缺少 DetailVat 計稅模式");
        }
        if (query.DetailVat != record.DetailVat)
        {
            throw new InvalidDataException($"回查 DetailVat 不符：取得 {query.DetailVat}，預期 {record.DetailVat}");
        }
        var companyBuyer = record.BuyerIdentifier.Trim() is not "" and not "0000000000";
        var totals = InvoiceCalculator.CalculateTotals(record.Items, companyBuyer, record.DetailVat == 0);
        var expectedSales = companyBuyer ? totals.SalesAmount : totals.TotalAmount;
        var expectedTax = companyBuyer ? totals.TaxAmount : 0;
        if (totals.TotalAmount != record.Amount)
        {
            throw new InvalidDataException("本機明細與發票總額不一致");
        }
        if (!long.TryParse(query.SalesAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sales) ||
            !long.TryParse(query.TaxAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tax) ||
            sales != expectedSales || tax != expectedTax)
        {
            throw new InvalidDataException(
                $"回查稅額資料不符：取得銷售額 {query.SalesAmount}、稅額 {query.TaxAmount}，預期 {expectedSales}、{expectedTax}");
        }
    }

    private static void VerifyQueryResult(
        InvoiceRecord record,
        InvoiceDraft draft,
        QueryResult query,
        string expectedInvoiceNumber)
    {
        VerifyStoredQueryResult(record, query);
        if (expectedInvoiceNumber.Length != 0 && query.InvoiceNumber.Trim() != expectedInvoiceNumber.Trim())
        {
            throw new InvalidDataException("回查發票號碼不是本次開立取得的號碼");
        }
        if (query.CancelDate != 0)
        {
            throw new InvalidDataException("回查取得的是已作廢發票，不能視為本次開立成功");
        }
        var totals = InvoiceCalculator.CalculateTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax);
        var expectedSales = draft.CompanyBuyer ? totals.SalesAmount : totals.TotalAmount;
        var expectedTax = draft.CompanyBuyer ? totals.TaxAmount : 0;
        if (totals.TotalAmount != record.Amount)
        {
            throw new InvalidDataException("本次發票計算總額與本機紀錄不符");
        }
        if (!long.TryParse(query.SalesAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sales) ||
            !long.TryParse(query.TaxAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tax) ||
            sales != expectedSales || tax != expectedTax)
        {
            throw new InvalidDataException(
                $"回查稅額資料不符：取得銷售額 {query.SalesAmount}、稅額 {query.TaxAmount}，預期 {expectedSales}、{expectedTax}");
        }
    }

    private static InvoiceRecord? FindUncertainRecord(
        IEnumerable<InvoiceRecord> records,
        string orderId,
        string environment)
    {
        return records.Reverse().FirstOrDefault(record =>
            record.InvoiceState is InvoiceStates.Unknown or InvoiceStates.Changing &&
            SameEnvironment(record.Environment, environment) &&
            (string.Equals(EffectiveRecordOrderId(record), orderId, StringComparison.Ordinal) ||
             string.Equals(record.OriginalOrderId.Trim(), orderId, StringComparison.Ordinal) ||
             string.Equals(record.OrderId.Trim(), orderId, StringComparison.Ordinal)));
    }

    private static bool SameEnvironment(string recordEnvironment, string currentEnvironment)
    {
        recordEnvironment = recordEnvironment.Trim();
        currentEnvironment = currentEnvironment.Trim();
        return recordEnvironment.Length == 0 || currentEnvironment.Length == 0 || recordEnvironment == currentEnvironment;
    }

    private static string EffectiveRecordOrderId(InvoiceRecord record)
    {
        if (record.ApiOrderId.Trim().Length != 0) return record.ApiOrderId.Trim();
        if (record.OrderId.Trim().Length != 0) return record.OrderId.Trim();
        return record.OriginalOrderId.Trim();
    }

    private static string RequestOrderId(InvoiceDraft draft, IssueOptions options) =>
        options.ApiOrderId.Trim().Length != 0 ? options.ApiOrderId.Trim() : draft.OrderId.Trim();

    private static string RequestBuyerName(InvoiceDraft draft, IssueOptions options) =>
        options.ApiBuyerName.Trim().Length != 0 ? options.ApiBuyerName.Trim() : EffectiveBuyerName(draft, options.BuyerName);

    private static string NormalizedBuyerIdentifier(InvoiceDraft draft) =>
        draft.CompanyBuyer ? draft.BuyerIdentifier.Trim() : "0000000000";

    private static string EffectiveBuyerName(InvoiceDraft draft, string overrideName) =>
        overrideName.Trim().Length != 0
            ? overrideName.Trim()
            : draft.CompanyBuyer ? draft.BuyerName.Trim() : "消費者";

    private static int DetailVat(InvoiceDraft draft) => draft.PricesExcludeTax ? 0 : 1;

    private static bool IsMoCompany(MoOrder order) =>
        order.BuyerBan.Trim().Length != 0 && order.BuyerBan.Trim() != "0000000000";

    private static void ResolveImportedBuyerName(
        bool company,
        NameLookup lookup,
        string currentName,
        Action<string> apply)
    {
        if (!company)
        {
            return;
        }
        if (lookup.Local && lookup.Name.Trim().Length != 0)
        {
            apply(lookup.Name.Trim());
            return;
        }
        if (lookup.LookupSucceeded && lookup.ApiName.Trim().Length != 0)
        {
            apply(lookup.ApiName.Trim());
            return;
        }
        if (!lookup.LookupSucceeded)
        {
            throw new InvalidOperationException("買方名稱尚未完成查詢，已擋下且未送出");
        }
        currentName = currentName.Trim();
        if (currentName.Length == 0)
        {
            throw new InvalidOperationException("光貿查不到公司名稱，請在匯入確認視窗輸入買方名稱");
        }
        apply(currentName);
    }

    private static InvoiceDraft DraftFromImportedOrder(
        string orderId,
        bool company,
        string buyerBan,
        string buyerName,
        IEnumerable<InvoiceItem> items,
        long totalAmount,
        string mainRemark)
    {
        var draft = new InvoiceDraft
        {
            OrderId = orderId,
            CompanyBuyer = company,
            BuyerIdentifier = buyerBan,
            BuyerName = buyerName,
            TotalAmount = totalAmount,
            MainRemark = mainRemark,
        };
        draft.Items.AddRange(items);
        return draft;
    }

    private static string CarrierType(string carrier) => carrier.Trim() switch
    {
        "" or DeliveryPaper or "公司戶" => string.Empty,
        MoCarriers.Member => "amego",
        MoCarriers.Mobile => "3J0002",
        MoCarriers.Citizen => "CQ0001",
        _ => throw new InvalidOperationException($"尚未支援的 MO店+ 載具類型：{carrier}"),
    };

    private static string UploadStatusText(int status) => status switch
    {
        UploadStatuses.Pending => "待處理",
        UploadStatuses.Uploading => "上傳中",
        UploadStatuses.Uploaded => "已上傳",
        UploadStatuses.Processing => "處理中",
        UploadStatuses.Confirming => "待確認",
        UploadStatuses.Error => "錯誤",
        UploadStatuses.Complete => "完成",
        _ => $"狀態 {status}",
    };

    private static string NewRecordId(DateTimeOffset value)
    {
        var nanoseconds = checked((value.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
        return nanoseconds.ToString(CultureInfo.InvariantCulture) + "-" +
            Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string NormalizeDate(string value)
    {
        value = value.Trim();
        return value.Length == 8 && !value.Contains('/', StringComparison.Ordinal)
            ? value[..4] + "/" + value[4..6] + "/" + value[6..]
            : value;
    }

    private sealed record IssueOptions(
        string Source = "",
        string OriginalOrderId = "",
        string Delivery = "",
        string BuyerAddress = "",
        string BuyerPhone = "",
        string BuyerEmail = "",
        string BuyerName = "",
        string CarrierType = "",
        string CarrierId1 = "",
        string CarrierId2 = "",
        string NpoBan = "",
        string ApiOrderId = "",
        string ApiBuyerName = "",
        bool TestPrivacy = false);
}

public sealed class UnknownInvoiceResultException(InvoiceRecord record, Exception? innerException = null)
    : Exception(
        record.InvoiceNumber.Length == 0 && record.ErrorMessage.StartsWith("光貿回覆成功", StringComparison.Ordinal)
            ? "光貿回覆成功但沒有發票號碼，結果不明；再次開立前會先向光貿回查"
            : "開立結果不明；再次開立相同 OrderID 前會先向光貿回查",
        innerException)
{
    public InvoiceRecord Record { get; } = record;
}
