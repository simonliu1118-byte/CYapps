using System.Globalization;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed class InvoiceDetailRefreshService
{
    private readonly LocalRepository repository;
    private readonly InvoiceSyncRepository syncRepository;
    private readonly Func<string, string, IAmegoGateway> gatewayFactory;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim gate = new(1, 1);

    public InvoiceDetailRefreshService(
        LocalRepository repository,
        Func<string, string, IAmegoGateway>? gatewayFactory = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        syncRepository = new InvoiceSyncRepository(repository.DataDirectory);
        this.gatewayFactory = gatewayFactory ?? ((invoice, appKey) => new AmegoClient(invoice, appKey));
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<InvoiceRecord> RefreshAsync(InvoiceRecord selected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = repository.Invoices.LoadOrCreate().SingleOrDefault(record => record.Id == selected.Id)
                ?? throw new InvalidOperationException("本機找不到這筆發票紀錄，請重新整理清單");
            var account = CurrentAccount();
            if (stored.Environment.Trim().Length != 0 &&
                !string.Equals(stored.Environment, account.Environment, StringComparison.Ordinal))
                throw new InvalidOperationException("這筆發票屬於其他環境，請切換到正確環境後再查詢");
            if (stored.SellerInvoice.Trim().Length != 0 &&
                !string.Equals(stored.SellerInvoice.Trim(), account.SellerInvoice, StringComparison.Ordinal))
                throw new InvalidOperationException("這筆發票屬於其他公司統編，已停止回查");

            var gateway = gatewayFactory(account.SellerInvoice, account.AppKey);
            QueryResponse response;
            if (stored.InvoiceNumber.Trim().Length != 0)
            {
                response = await gateway.QueryByInvoiceNumberAsync(stored.InvoiceNumber.Trim(), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var orderId = EffectiveOrderId(stored);
                if (orderId.Length == 0) throw new InvalidOperationException("這筆紀錄沒有可供光貿查詢的發票號碼或 OrderID");
                response = await gateway.QueryByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
            }

            var before = OfficialFingerprint(stored);
            ApplyOfficialQuery(stored, response.Data, account);
            syncRepository.UpsertMany([stored]);
            if (!string.Equals(before, OfficialFingerprint(stored), StringComparison.Ordinal) && stored.InvoiceNumber.Trim().Length != 0)
            {
                var cacheProblems = InvoiceCacheInvalidator.Invalidate(repository, account.Environment, stored.InvoiceNumber);
                if (cacheProblems.Count != 0)
                    throw new IOException("官方資料已更新，但舊 PDF/預覽快取清除失敗：" + string.Join("；", cacheProblems));
            }

            return repository.Invoices.LoadOrCreate().Single(record => record.Id == stored.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    private void ApplyOfficialQuery(InvoiceRecord record, QueryResult query, Account account)
    {
        var number = query.InvoiceNumber.Trim();
        if (number.Length == 0) throw new InvalidDataException("invoice_query 缺少發票號碼");
        if (record.InvoiceNumber.Trim().Length != 0 &&
            !string.Equals(record.InvoiceNumber.Trim(), number, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("invoice_query 發票號碼與本機紀錄不符");

        var apiOrderId = query.OrderId.Trim();
        if (apiOrderId.Length == 0) throw new InvalidDataException("invoice_query 缺少 OrderID");
        var orderId = account.Environment == Environments.Test
            ? TestOrderIdPrefix.StripIfPresent(apiOrderId)
            : apiOrderId;
        record.SellerInvoice = account.SellerInvoice;
        record.Environment = account.Environment;
        record.InvoiceNumber = number;
        record.OrderId = orderId;
        record.ApiOrderId = apiOrderId;
        if (string.Equals(record.RecordOrigin, RecordOrigins.Sync, StringComparison.Ordinal) ||
            record.OriginalOrderId.Trim().Length == 0)
            record.OriginalOrderId = orderId;
        record.Source = InvoiceSourceInference.FromOrderId(orderId);
        record.InvoiceState = query.CancelDate == 0 ? InvoiceStates.Opened : InvoiceStates.Voided;
        if (query.InvoiceStatus != 0) record.UploadStatus = query.InvoiceStatus;
        record.UploadStatusText = UploadStatusText(record.UploadStatus);
        record.ErrorMessage = string.Empty;
        record.InvoiceDate = NormalizeDate(query.InvoiceDate);
        record.InvoiceTime = NormalizeTime(query.InvoiceTime);
        record.LastChecked = now().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        record.BuyerIdentifier = query.BuyerIdentifier.Trim();
        record.BuyerName = query.BuyerName.Trim();
        record.Amount = Money(query.TotalAmount, "invoice_query 發票總額");
        record.CarrierType = query.CarrierType.Trim();
        record.CarrierId1 = query.CarrierId1.Trim();
        record.CarrierId2 = query.CarrierId2.Trim();
        record.NpoBan = query.NpoBan.Trim();
        if (query.DetailVatPresent) record.DetailVat = query.DetailVat;
        record.Items = ParseItems(query.ProductItems);
        record.Delivery = Delivery(record.CarrierType, record.NpoBan);
    }

    private static List<InvoiceItem> ParseItems(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("invoice_query ProductItem 不是陣列");
        var items = new List<InvoiceItem>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("invoice_query ProductItem 含非物件資料");
            var description = FirstText(element, "description", "Description");
            var quantityText = FirstScalar(element, "quantity", "Quantity");
            var unitPriceText = FirstScalar(element, "unit_price", "UnitPrice");
            var amountText = FirstScalar(element, "amount", "Amount");
            if (description.Length == 0 || quantityText.Length == 0 || unitPriceText.Length == 0 || amountText.Length == 0)
                throw new InvalidDataException("invoice_query ProductItem 缺少必要欄位");
            var quantity = FixedDecimal.Parse(quantityText);
            var unitPrice = FixedDecimal.Parse(unitPriceText);
            var amount = FixedDecimal.Parse(amountText);
            var calculated = FixedDecimal.Multiply(quantity, unitPrice);
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
                AllowSubtotalRounding = calculated != amount && calculated.RoundInt64() == amount.RoundInt64(),
            });
        }
        return items;
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

    private static string OfficialFingerprint(InvoiceRecord record) => JsonSerializer.Serialize(new
    {
        record.SellerInvoice,
        record.Environment,
        record.Source,
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
        record.DetailVat,
        record.Items,
    });

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private static long Money(string text, string field)
    {
        try { return FixedDecimal.Parse(text).RoundInt64(); }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new InvalidDataException($"{field}格式錯誤：{text}", error);
        }
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
        if (value.Length == 6 && value.All(char.IsAsciiDigit))
            return value[..2] + ":" + value[2..4] + ":" + value[4..];
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) && unix >= 1_000_000_000)
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return value;
    }

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
}
