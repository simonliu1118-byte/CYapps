using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public enum InvoiceVoidOutcome
{
    Confirmed,
    PendingConfirmation,
    Rejected,
    AlreadyVoided,
    RetryReady,
}

public sealed record InvoiceVoidResult(
    InvoiceVoidOutcome Outcome,
    InvoiceRecord Record,
    bool RequestSent,
    int ApiCode = 0,
    string Message = "",
    Exception? LocalSaveError = null);

public sealed class InvoiceVoidService
{
    private const string PendingMetadataKey = "cyinvoice_void_pending";
    private static readonly TimeSpan RecoveryQueryTimeout = TimeSpan.FromSeconds(20);
    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly Func<string, string, IAmegoGateway> gatewayFactory;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim gate = new(1, 1);

    public InvoiceVoidService(
        LocalRepository repository,
        Func<string, string, IAmegoGateway>? gatewayFactory = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        this.gatewayFactory = gatewayFactory ?? ((invoice, appKey) => new AmegoClient(invoice, appKey));
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceVoidResult> VoidAsync(
        InvoiceRecord selected,
        string cancelReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancelReason = cancelReason?.Trim() ?? string.Empty;
        if (cancelReason.Length == 0) throw new InvalidOperationException("作廢原因不可空白");
        if (cancelReason.EnumerateRunes().Count() > 20)
            throw new InvalidOperationException("送往光貿的作廢原因不可超過 20 字");

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = Reload(selected);
            var account = CurrentAccount();
            ValidateAccount(record, account);
            var number = record.InvoiceNumber.Trim();
            if (number.Length == 0) throw new InvalidOperationException("這筆紀錄沒有發票號碼，無法作廢");
            var gateway = gatewayFactory(account.SellerInvoice, account.AppKey);

            if (HasPendingMarker(record))
                return await ReconcileCoreAsync(record, gateway, requestSent: false, cancellationToken).ConfigureAwait(false);

            var preflight = await InspectAsync(gateway, number, cancellationToken).ConfigureAwait(false);
            if (preflight.Confirmed)
            {
                ApplyConfirmed(record, preflight);
                var saveError = TryPersist(record);
                TryInvalidateCache(record);
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.AlreadyVoided,
                    Reload(record),
                    RequestSent: false,
                    Message: "光貿已確認這張發票為作廢狀態",
                    LocalSaveError: saveError);
            }
            if (preflight.Pending)
            {
                MarkPendingMarker(record);
                ApplyPending(record, preflight);
                var saveError = TryPersist(record);
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.PendingConfirmation,
                    Reload(record),
                    RequestSent: false,
                    Message: "光貿已有作廢作業待處理，未重複送出",
                    LocalSaveError: saveError);
            }
            if (preflight.Query is null)
                throw new InvalidOperationException("無法取得最新 invoice_query，已停止作廢", preflight.QueryError);
            if (preflight.Query.Data.InvoiceStatus != UploadStatuses.Complete)
            {
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.Rejected,
                    record,
                    RequestSent: false,
                    Message: $"發票目前官方狀態為 {StatusText(preflight.Query.Data.InvoiceStatus)}，尚不可送出作廢");
            }

            MarkPendingMarker(record);
            ApplyPending(record, preflight);
            Persist(record); // Durable anti-resend marker must exist before f0501 leaves the process.

            VoidResponse? response = null;
            Exception? sendError = null;
            try
            {
                response = await gateway.VoidAsync(
                    new VoidRequest
                    {
                        CancelInvoiceNumber = number,
                        CancelReason = cancelReason,
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                sendError = error;
            }

            using var recovery = new CancellationTokenSource(RecoveryQueryTimeout);
            var after = await InspectAsync(gateway, number, recovery.Token).ConfigureAwait(false);
            if (after.Confirmed)
            {
                ApplyConfirmed(record, after);
                var saveError = TryPersist(record);
                TryInvalidateCache(record);
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.Confirmed,
                    Reload(record),
                    RequestSent: true,
                    ApiCode: response?.Code ?? 0,
                    Message: "光貿已確認發票作廢完成",
                    LocalSaveError: saveError);
            }
            if (after.Pending)
            {
                ApplyPending(record, after);
                var saveError = TryPersist(record);
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.PendingConfirmation,
                    Reload(record),
                    RequestSent: true,
                    ApiCode: response?.Code ?? 0,
                    Message: "作廢已送出，等待光貿完成確認",
                    LocalSaveError: saveError);
            }

            if (response is { Code: not 0 } rejection)
            {
                if (CouldRepresentExistingVoid(rejection.Code))
                {
                    ApplyPending(record, after);
                    var saveError = TryPersist(record);
                    return new InvoiceVoidResult(
                        InvoiceVoidOutcome.PendingConfirmation,
                        Reload(record),
                        RequestSent: true,
                        ApiCode: rejection.Code,
                        Message: ApiMessage(rejection),
                        LocalSaveError: saveError);
                }

                ClearPendingMarker(record);
                ApplyOpen(record, after, ApiMessage(rejection));
                var rejectedSaveError = TryPersist(record);
                return new InvoiceVoidResult(
                    InvoiceVoidOutcome.Rejected,
                    Reload(record),
                    RequestSent: true,
                    ApiCode: rejection.Code,
                    Message: ApiMessage(rejection),
                    LocalSaveError: rejectedSaveError);
            }

            var uncertainMessage = sendError is null
                ? "光貿已接收作廢請求，但目前尚無法確認最終結果；禁止直接重送"
                : "作廢傳輸結果不明，已保留等待作廢狀態；禁止直接重送";
            ApplyPending(record, after);
            var uncertainSaveError = TryPersist(record);
            return new InvoiceVoidResult(
                InvoiceVoidOutcome.PendingConfirmation,
                Reload(record),
                RequestSent: true,
                ApiCode: response?.Code ?? 0,
                Message: uncertainMessage,
                LocalSaveError: uncertainSaveError);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<InvoiceVoidResult> ReconcilePendingAsync(
        InvoiceRecord selected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = Reload(selected);
            var account = CurrentAccount();
            ValidateAccount(record, account);
            if (record.InvoiceNumber.Trim().Length == 0)
                throw new InvalidOperationException("這筆紀錄沒有發票號碼，無法確認作廢狀態");
            var gateway = gatewayFactory(account.SellerInvoice, account.AppKey);
            return await ReconcileCoreAsync(record, gateway, requestSent: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<InvoiceVoidResult> ReconcileCoreAsync(
        InvoiceRecord record,
        IAmegoGateway gateway,
        bool requestSent,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectAsync(gateway, record.InvoiceNumber.Trim(), cancellationToken).ConfigureAwait(false);
        if (inspection.Confirmed)
        {
            ApplyConfirmed(record, inspection);
            var saveError = TryPersist(record);
            TryInvalidateCache(record);
            return new InvoiceVoidResult(
                InvoiceVoidOutcome.Confirmed,
                Reload(record),
                requestSent,
                Message: "光貿已確認發票作廢完成",
                LocalSaveError: saveError);
        }
        if (inspection.Pending)
        {
            MarkPendingMarker(record);
            ApplyPending(record, inspection);
            var saveError = TryPersist(record);
            return new InvoiceVoidResult(
                InvoiceVoidOutcome.PendingConfirmation,
                Reload(record),
                requestSent,
                Message: "光貿作廢作業仍在處理中",
                LocalSaveError: saveError);
        }
        if (inspection.VoidFailed)
        {
            ClearPendingMarker(record);
            ApplyOpen(record, inspection, "光貿已明確回報作廢處理失敗，可重新確認後再操作");
            var saveError = TryPersist(record);
            return new InvoiceVoidResult(
                InvoiceVoidOutcome.Rejected,
                Reload(record),
                requestSent,
                Message: "光貿已明確回報作廢處理失敗，可重新確認後再操作",
                LocalSaveError: saveError);
        }
        if (inspection.StableOpen)
        {
            ClearPendingMarker(record);
            ApplyOpen(record, inspection, string.Empty);
            var saveError = TryPersist(record);
            return new InvoiceVoidResult(
                InvoiceVoidOutcome.RetryReady,
                Reload(record),
                requestSent,
                Message: "官方查詢確認目前沒有作廢或待處理作廢；本次只解除等待作廢狀態，未自動重送",
                LocalSaveError: saveError);
        }

        MarkPendingMarker(record);
        ApplyPending(record, inspection);
        var pendingSaveError = TryPersist(record);
        return new InvoiceVoidResult(
            InvoiceVoidOutcome.PendingConfirmation,
            Reload(record),
            requestSent,
            Message: "目前仍無法向光貿確認作廢結果；禁止直接重送",
            LocalSaveError: pendingSaveError);
    }

    private async Task<OfficialInspection> InspectAsync(
        IAmegoGateway gateway,
        string invoiceNumber,
        CancellationToken cancellationToken)
    {
        QueryResponse? query = null;
        Exception? queryError = null;
        try
        {
            query = await gateway.QueryByInvoiceNumberAsync(invoiceNumber, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            queryError = error;
        }

        var queryVoidType = query is not null && IsVoidType(query.Data.InvoiceType);
        var confirmed = query?.Data.CancelDate > 0 ||
                        (queryVoidType && query!.Data.InvoiceStatus == UploadStatuses.Complete);
        var pending = !confirmed && query is not null &&
                      (query.Data.VoidPending ||
                       (queryVoidType && IsPendingStatus(query.Data.InvoiceStatus)));
        var voidFailed = !confirmed && !pending && queryVoidType &&
                         query!.Data.InvoiceStatus == UploadStatuses.Error;
        var stableOpen = !confirmed && !pending && !voidFailed && query is not null &&
                         query.Data.CancelDate == 0 && !query.Data.VoidPending &&
                         query.Data.InvoiceStatus == UploadStatuses.Complete && !queryVoidType;

        return new OfficialInspection(query, null, queryError, null, confirmed, pending, voidFailed, stableOpen);
    }

    private InvoiceRecord Reload(InvoiceRecord record) =>
        repository.Invoices.LoadOrCreate().SingleOrDefault(item => item.Id == record.Id)
        ?? throw new InvalidOperationException("本機找不到這筆發票紀錄，請重新整理清單");

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

    private static void ValidateAccount(InvoiceRecord record, Account account)
    {
        if (record.Environment.Trim().Length != 0 &&
            !string.Equals(record.Environment.Trim(), account.Environment, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他環境，已停止作廢");
        if (record.SellerInvoice.Trim().Length != 0 &&
            !string.Equals(record.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
            throw new InvalidOperationException("這筆發票屬於其他公司統編，已停止作廢");
        if (record.InvoiceState == InvoiceStates.Failed)
            throw new InvalidOperationException("開立失敗的紀錄不可作廢");
    }

    private void ApplyConfirmed(InvoiceRecord record, OfficialInspection inspection)
    {
        ClearPendingMarker(record);
        record.InvoiceState = InvoiceStates.Voided;
        record.ErrorMessage = string.Empty;
        ApplyOriginalInvoiceStatus(record, inspection);
    }

    private void ApplyPending(InvoiceRecord record, OfficialInspection inspection)
    {
        record.InvoiceState = InvoiceStates.OpenedWaitingVoid;
        record.ErrorMessage = string.Empty;
        ApplyOriginalInvoiceStatus(record, inspection);
    }

    private void ApplyOpen(InvoiceRecord record, OfficialInspection inspection, string message)
    {
        record.InvoiceState = InvoiceStates.Opened;
        record.ErrorMessage = message;
        ApplyOriginalInvoiceStatus(record, inspection);
    }

    private void ApplyOriginalInvoiceStatus(InvoiceRecord record, OfficialInspection inspection)
    {
        if (inspection.Query is { } query && !IsVoidType(query.Data.InvoiceType) && query.Data.InvoiceStatus > 0)
        {
            record.UploadStatus = query.Data.InvoiceStatus;
            record.UploadStatusText = StatusText(query.Data.InvoiceStatus);
        }
        record.LastChecked = now().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    internal static void MarkPendingMarker(InvoiceRecord record)
    {
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[PendingMetadataKey] = JsonSerializer.SerializeToElement(true);
    }

    internal static void ClearPendingMarker(InvoiceRecord record)
    {
        if (record.ExtensionData is null) return;
        record.ExtensionData.Remove(PendingMetadataKey);
        if (record.ExtensionData.Count == 0) record.ExtensionData = null;
    }

    internal static bool HasPendingMarker(InvoiceRecord record)
    {
        if (record.ExtensionData is null || !record.ExtensionData.TryGetValue(PendingMetadataKey, out var value)) return false;
        return value.ValueKind == JsonValueKind.True ||
               (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
    }

    private void Persist(InvoiceRecord record) => syncRepository.UpsertMany([record]);

    private Exception? TryPersist(InvoiceRecord record)
    {
        try
        {
            Persist(record);
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private void TryInvalidateCache(InvoiceRecord record)
    {
        if (record.InvoiceNumber.Trim().Length == 0) return;
        InvoiceCacheInvalidator.Invalidate(repository, record.Environment, record.InvoiceNumber);
    }

    private static bool CouldRepresentExistingVoid(int code) => code is 3050122 or 3050123 or 3050131;

    private static string ApiMessage(VoidResponse response) =>
        response.Message.Trim().Length == 0 ? $"光貿拒絕作廢（{response.Code}）" : $"光貿拒絕作廢（{response.Code}）：{response.Message.Trim()}";

    private static bool IsVoidType(string value) =>
        string.Equals(value.Trim(), "C0501", StringComparison.OrdinalIgnoreCase);

    private static bool IsPendingStatus(int status) => status is
        UploadStatuses.Pending or UploadStatuses.Uploading or UploadStatuses.Uploaded or
        UploadStatuses.Processing or UploadStatuses.Confirming;

    private static string StatusText(int status) => status switch
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

    private sealed record Account(string Environment, string SellerInvoice, string AppKey);

    private sealed record OfficialInspection(
        QueryResponse? Query,
        StatusResult? VoidStatus,
        Exception? QueryError,
        Exception? StatusError,
        bool Confirmed,
        bool Pending,
        bool VoidFailed,
        bool StableOpen);
}
