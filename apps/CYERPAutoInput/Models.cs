namespace CYERPAutoInput;

internal sealed record FieldDefinition(
    string Key,
    string Group,
    string Label,
    FieldKind Kind = FieldKind.Text,
    bool Standard = false);

internal enum FieldKind
{
    Text,
    Date,
    Lookup,
    Combo,
    Boolean
}

internal sealed class DetailRow
{
    public string ItemCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public string GiftQuantity { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public string UnitPrice { get; set; } = string.Empty;

    public bool HasAnyData =>
        !string.IsNullOrWhiteSpace(ItemCode) ||
        !string.IsNullOrWhiteSpace(Unit) ||
        !string.IsNullOrWhiteSpace(Quantity) ||
        !string.IsNullOrWhiteSpace(GiftQuantity) ||
        !string.IsNullOrWhiteSpace(Batch) ||
        !string.IsNullOrWhiteSpace(Warehouse) ||
        !string.IsNullOrWhiteSpace(UnitPrice);
}

internal sealed class FormSnapshot
{
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DetailRow> Details { get; init; } = [];
}

internal static class FieldCatalog
{
    public static readonly FieldDefinition[] All =
    [
        new("order_type", "表頭", "銷貨單別", FieldKind.Lookup, true),
        new("order_date", "表頭", "單據日期", FieldKind.Date, true),
        new("customer_code", "表頭", "客戶代號", FieldKind.Lookup, true),

        new("dept_code", "交易資料", "部門代號", FieldKind.Lookup),
        new("currency", "交易資料", "幣別"),
        new("trade_note", "交易資料", "備註", FieldKind.Text, true),
        new("salesperson", "交易資料", "業務人員", FieldKind.Lookup),
        new("exchange_rate", "交易資料", "匯率"),
        new("transmit_count", "交易資料", "傳送次數"),
        new("invoice_print", "交易資料", "發票列印"),
        new("receipt_salesperson", "交易資料", "收款業務員", FieldKind.Lookup),
        new("employee_code", "交易資料", "員工代號", FieldKind.Lookup, true),
        new("payment_terms", "交易資料", "付款條件"),

        new("ship_name", "送貨資料", "送貨客戶全名"),
        new("ship_addr1", "送貨資料", "送貨地址(一)"),
        new("ship_addr2", "送貨資料", "送貨地址(二)"),
        new("contact", "送貨資料", "連絡人"),
        new("receiver", "送貨資料", "收貨人"),
        new("tel", "送貨資料", "TEL_NO"),
        new("fax", "送貨資料", "FAX_NO"),
        new("mobile", "送貨資料", "行動電話"),
        new("appoint_date", "送貨資料", "指定日期", FieldKind.Date),
        new("delivery_slot", "送貨資料", "配送時段", FieldKind.Combo),
        new("freight_type", "送貨資料", "貨運別", FieldKind.Lookup, true),
        new("cod", "送貨資料", "代收貨款", FieldKind.Text, true),
        new("freight_fee", "送貨資料", "運費", FieldKind.Text, true),
        new("freight_file", "送貨資料", "產生貨運文字檔", FieldKind.Boolean),

        new("inv_date", "發票資料(一)", "發票日期", FieldKind.Date, true),
        new("inv_time", "發票資料(一)", "發票開立時間", FieldKind.Text, true),
        new("inv_copies", "發票資料(一)", "發票聯數", FieldKind.Combo, true),
        new("inv_no", "發票資料(一)", "發票號碼", FieldKind.Text, true),
        new("tax_type", "發票資料(一)", "課稅別", FieldKind.Combo, true),
        new("customs", "發票資料(一)", "通關方式", FieldKind.Combo),
        new("tax_id", "發票資料(一)", "統一編號", FieldKind.Text, true),
        new("tax_rate", "發票資料(一)", "營業稅率"),
        new("report_month", "發票資料(一)", "申報年月"),
        new("voided", "發票資料(一)", "發票作廢", FieldKind.Boolean),
        new("inv_name", "發票資料(一)", "客戶全名", FieldKind.Text, true),
        new("card4", "發票資料(一)", "信用卡末四碼"),
        new("inv_addr1", "發票資料(一)", "發票地址(一)"),
        new("inv_addr2", "發票資料(一)", "發票地址(二)"),
        new("email", "發票資料(一)", "連絡人EMAIL")
    ];
}
