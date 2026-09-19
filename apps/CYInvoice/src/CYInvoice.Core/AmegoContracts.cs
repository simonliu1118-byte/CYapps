using System.Text.Json;
using System.Text.Json.Serialization;

namespace CYInvoice.Core.Amego;

public static class AmegoDefaults
{
    public const string BaseUrl = "https://invoice-api.amego.tw";
    public const string TestInvoice = "12345678";
    public const string TestAppKey = "sHeq7t8G1wiQvhAuIM27";
}

public static class UploadStatuses
{
    public const int Pending = 1;
    public const int Uploading = 2;
    public const int Uploaded = 3;
    public const int Processing = 31;
    public const int Confirming = 32;
    public const int Error = 91;
    public const int Complete = 99;
}

public interface IAmegoGateway
{
    Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default);
    Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default) =>
        Task.FromException<VoidResponse>(new NotSupportedException("invoice void is not supported by this gateway"));
    Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default);
    Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadInvoicePdfAsync(string invoiceNumber, int downloadStyle, CancellationToken cancellationToken = default);
    Task<InvoiceListResponse> ListInvoicesAsync(
        DateOnly startDate,
        DateOnly endDate,
        int page = 1,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        Task.FromException<InvoiceListResponse>(new NotSupportedException("invoice_list is not supported by this gateway"));
}

public sealed class ProductItem
{
    public string Description { get; set; } = string.Empty;
    public object Quantity { get; set; } = 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Unit { get; set; } = string.Empty;
    public object UnitPrice { get; set; } = 0;
    public object Amount { get; set; } = 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Remark { get; set; } = string.Empty;
    public int TaxType { get; set; }
}

public sealed class IssueRequest
{
    [JsonPropertyName("OrderId")] public string OrderId { get; set; } = string.Empty;
    public string BuyerIdentifier { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerAddress { get; set; } = string.Empty;
    public string BuyerTelephoneNumber { get; set; } = string.Empty;
    public string BuyerEmailAddress { get; set; } = string.Empty;
    public string MainRemark { get; set; } = string.Empty;
    public string CarrierType { get; set; } = string.Empty;
    [JsonPropertyName("CarrierId1")] public string CarrierId1 { get; set; } = string.Empty;
    [JsonPropertyName("CarrierId2")] public string CarrierId2 { get; set; } = string.Empty;
    [JsonPropertyName("NPOBAN")] public string NpoBan { get; set; } = string.Empty;
    [JsonPropertyName("ProductItem")] public List<ProductItem> ProductItems { get; set; } = [];
    public object SalesAmount { get; set; } = 0;
    public object FreeTaxSalesAmount { get; set; } = 0;
    public object ZeroTaxSalesAmount { get; set; } = 0;
    public int TaxType { get; set; }
    public string TaxRate { get; set; } = "0.05";
    public object TaxAmount { get; set; } = 0;
    public object TotalAmount { get; set; } = 0;
    [JsonPropertyName("DetailVat")] public int DetailVat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int DetailAmountRound { get; set; }
}

public sealed class VoidRequest
{
    [JsonPropertyName("CancelInvoiceNumber")]
    public string CancelInvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("CancelReason")]
    public string CancelReason { get; set; } = string.Empty;
}

public sealed record IssueResponse(int Code, string Message, string InvoiceNumber, long InvoiceTime, string RandomNumber);
public sealed record VoidResponse(int Code, string Message);

public sealed record InvoiceAllowanceResult(
    string InvoiceType,
    int InvoiceStatus,
    int AllowanceType,
    string AllowanceNumber,
    string AllowanceDate,
    string TaxAmount,
    string TotalAmount);

public sealed record QueryResult(
    string InvoiceNumber,
    string InvoiceType,
    int InvoiceStatus,
    string InvoiceDate,
    string InvoiceTime,
    string BuyerIdentifier,
    string BuyerName,
    string SalesAmount,
    string TaxAmount,
    string TotalAmount,
    string CarrierType,
    string CarrierId1,
    string CarrierId2,
    string NpoBan,
    long CancelDate,
    string OrderId,
    long CreateDate,
    JsonElement ProductItems,
    int DetailVat,
    bool DetailVatPresent,
    bool VoidPending = false)
{
    public IReadOnlyList<InvoiceAllowanceResult> Allowances { get; init; } = [];
}

public sealed record QueryResponse(int Code, string Message, QueryResult Data);

public sealed record InvoiceListItem(
    string InvoiceNumber,
    string InvoiceType,
    int InvoiceStatus,
    string InvoiceDate,
    string InvoiceTime,
    string BuyerIdentifier,
    string BuyerName,
    string SalesAmount,
    string TaxAmount,
    string TotalAmount,
    string MainRemark,
    string CarrierType,
    string CarrierId1,
    string CarrierId2,
    string NpoBan,
    long CancelDate,
    string OrderId,
    long CreateDate);

public sealed record InvoiceListResponse(
    int Code,
    string Message,
    int PageTotal,
    int PageNow,
    int DataTotal,
    IReadOnlyList<InvoiceListItem> Data);

public sealed record StatusResult(string InvoiceNumber, string Type, int Status, string TotalAmount);
public sealed record StatusResponse(int Code, string Message, IReadOnlyList<StatusResult> Data);
public sealed record BanResult(string Ban, string Name);
public sealed record BanResponse(int Code, string Message, IReadOnlyList<BanResult> Data);

public sealed class AmegoApiException(int code, string message) : Exception($"API code {code}: {message}")
{
    public int Code { get; } = code;
}
