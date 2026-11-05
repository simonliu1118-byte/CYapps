using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

internal static class AllowanceDetailPresentation
{
    public static (IReadOnlyList<KeyValuePair<string,string>> Fields,IReadOnlyList<BusinessDetailTable> Tables) Build(
        InvoiceRecord? original, IReadOnlyList<KeyValuePair<string,string>> fields, IReadOnlyList<BusinessDetailTable> tables)
    {
        var rows = new List<KeyValuePair<string,string>>();
        if(original is not null) {
            rows.AddRange([new("原發票",original.InvoiceNumber),new("訂單編號",original.OrderId),
                new("原發票日期",InvoiceOperationHistoryControl.FormatAllowanceDate(original.InvoiceDate.ToString(CultureInfo.InvariantCulture))),
                new("買受人",original.BuyerName),new("統一編號",original.BuyerIdentifier),new("原發票含稅金額",MoneyFormatter.Integer(original.Amount)),
                new("使用環境",original.Environment==Environments.Test?"測試":"正式")]);
        }
        foreach(var field in fields) {
            if(field.Key=="原折讓商品"&&tables.Any(table=>table.Title=="原折讓商品"))continue;
            if(original is not null && rows.Any(row=>row.Key==field.Key))continue;
            var value=field.Value;
            if(field.Key.EndsWith("時間",StringComparison.Ordinal)&&DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var time))
                value=time.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss",CultureInfo.InvariantCulture);
            rows.Add(new(field.Key=="核准管理員"?"覆核主管":field.Key,value));
        }
        var all=tables.ToList();
        if(original is not null) {
            all.Insert(0,BusinessDetailControl.Products("原發票商品",original.Items));
            all.Insert(1,History(InvoiceAllowanceMetadata.ReadOfficial(original)));
        }
        return(rows,all);
    }

    public static BusinessDetailTable History(IReadOnlyList<InvoiceAllowanceResult> values) => new("既有折讓",
        ["折讓單號","日期","未稅金額","稅額","含稅金額","狀態"],values.Select(item=>(IReadOnlyList<string>)new[] {
            item.AllowanceNumber,InvoiceOperationHistoryControl.FormatAllowanceDate(item.AllowanceDate),item.TotalAmount,item.TaxAmount,
            InvoiceOperationHistoryControl.InclusiveAmount(item),item.CancelDate>0?"已作廢":item.InvoiceStatus==UploadStatuses.Complete?"已完成":"待核對" }).ToArray());

    public static BusinessDetailTable Official(AllowanceQueryItem item) => new("原折讓商品",
        ["品名","數量","單價","稅別","未稅金額","稅額"],item.ProductItems.EnumerateArray().Select(row=>(IReadOnlyList<string>)new[] {
            "original_description","quantity","unit_price","tax_type","amount","tax" }.Select(key=>row.TryGetProperty(key,out var value)?value.ToString():"－").ToArray()).ToArray());

    public static BusinessDetailTable Draft(IReadOnlyList<AllowanceProductItem> items) => new("本次折讓商品",
        ["品名","數量","單價","稅別","未稅金額","稅額"],items.Select(item=>(IReadOnlyList<string>)new[] {
            item.OriginalDescription,item.Quantity.ToString(CultureInfo.InvariantCulture),item.UnitPrice.ToString(CultureInfo.InvariantCulture),item.TaxType.ToString(CultureInfo.InvariantCulture),
            item.Amount.ToString(CultureInfo.InvariantCulture),item.Tax.ToString(CultureInfo.InvariantCulture) }).ToArray());
}
