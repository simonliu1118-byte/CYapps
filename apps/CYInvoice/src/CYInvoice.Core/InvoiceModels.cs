using System.Text.Json;
using System.Text.Json.Serialization;

namespace CYInvoice.Core;

public static class InvoiceLimits
{
    public const int MaximumItems = 50;
    public const int MaximumRemarkCharacters = 200;
}

public static class InvoiceStates
{
    public const string Changing = "資料變更中";
    public const string Opened = "已開立";
    public const string Failed = "開立失敗";
    public const string Unknown = "結果不明";
    public const string Voided = "已作廢";
}

public static class Environments
{
    public const string Test = "test";
    public const string Production = "prod";
}

public sealed class InvoiceItem
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("quantity_decimal")]
    public string QuantityDecimal { get; set; } = string.Empty;

    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string Unit { get; set; } = string.Empty;

    [JsonPropertyName("unit_price")]
    public long UnitPrice { get; set; }

    [JsonPropertyName("unit_price_decimal")]
    public string UnitPriceDecimal { get; set; } = string.Empty;

    [JsonPropertyName("tax_type")]
    public string TaxType { get; set; } = "1";

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("amount_decimal")]
    public string AmountDecimal { get; set; } = string.Empty;

    [JsonPropertyName("remark")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("allow_subtotal_rounding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowSubtotalRounding { get; set; }
}

public sealed class InvoiceDraft
{
    public string OrderId { get; set; } = string.Empty;
    public bool CompanyBuyer { get; set; }
    public string BuyerIdentifier { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public bool PricesExcludeTax { get; set; }
    public List<InvoiceItem> Items { get; } = [];
    public string MainRemark { get; set; } = string.Empty;
    public long TotalAmount { get; set; }
}

public sealed class InvoiceRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("original_order_id")]
    public string OriginalOrderId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("attempt")]
    public int Attempt { get; set; }

    [JsonPropertyName("api_order_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string ApiOrderId { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("invoice_state")]
    public string InvoiceState { get; set; } = string.Empty;

    [JsonPropertyName("carrier_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string CarrierType { get; set; } = string.Empty;

    [JsonPropertyName("carrier_id1")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string CarrierId1 { get; set; } = string.Empty;

    [JsonPropertyName("carrier_id2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string CarrierId2 { get; set; } = string.Empty;

    [JsonPropertyName("npo_ban")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string NpoBan { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("delivery")]
    public string Delivery { get; set; } = string.Empty;

    [JsonPropertyName("upload_status")]
    public int UploadStatus { get; set; }

    [JsonPropertyName("upload_status_text")]
    public string UploadStatusText { get; set; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("sent_at")]
    public string SentAt { get; set; } = string.Empty;

    [JsonPropertyName("invoice_date")]
    public string InvoiceDate { get; set; } = string.Empty;

    [JsonPropertyName("invoice_time")]
    public string InvoiceTime { get; set; } = string.Empty;

    [JsonPropertyName("last_checked")]
    public string LastChecked { get; set; } = string.Empty;

    [JsonPropertyName("buyer_identifier")]
    public string BuyerIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("buyer_name")]
    public string BuyerName { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<InvoiceItem> Items { get; set; } = [];

    [JsonPropertyName("main_remark")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string MainRemark { get; set; } = string.Empty;

    [JsonPropertyName("detail_vat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DetailVat { get; set; }

    [JsonPropertyName("buyer_name_needs_memory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool BuyerNameNeedsMemory { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
