using System.Globalization;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

// Recalculate persisted editor rows on the Core boundary; do not trust a
// Windows-client-supplied amount or a line's claimed net/tax totals.
public static class AllowanceDraftValidator
{
    public static void Validate(
        IReadOnlyList<AllowanceProductItem> items,
        string invoiceNumber,
        string invoiceDate,
        long expectedGross)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is < 1 or > 50)
            throw new InvalidOperationException("折讓明細必須為 1～50 筆");
        var normalizedNumber = (invoiceNumber ?? string.Empty).Trim();
        var normalizedDate = (invoiceDate ?? string.Empty).Replace("/", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (normalizedNumber.Length == 0 ||
            !DateOnly.TryParseExact(normalizedDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new InvalidOperationException("原發票號碼或日期無法確認");
        var total = new FixedDecimal(0);
        foreach (var item in items)
        {
            if (!string.Equals(item.OriginalInvoiceNumber.Trim(), normalizedNumber,
                    StringComparison.OrdinalIgnoreCase) ||
                item.OriginalInvoiceDate != date.Year * 10000 + date.Month * 100 + date.Day)
                throw new InvalidOperationException("折讓品項不屬於目前查回的原發票");
            if (item.OriginalDescription.Trim().Length is < 1 or > 256 ||
                item.TaxType is < 1 or > 3)
                throw new InvalidOperationException("折讓明細品名或課稅別不合法");

            var quantity = FixedDecimal.Parse(item.Quantity.ToString(CultureInfo.InvariantCulture));
            var unitPrice = FixedDecimal.Parse(item.UnitPrice.ToString(CultureInfo.InvariantCulture));
            var declaredNet = FixedDecimal.Parse(item.Amount.ToString(CultureInfo.InvariantCulture));
            var declaredTax = FixedDecimal.Parse(item.Tax.ToString(CultureInfo.InvariantCulture));
            if (quantity.ScaledValue <= 0)
                throw new InvalidOperationException("折讓數量必須大於 0");
            var net = FixedDecimal.FromInt64(FixedDecimal.Multiply(quantity, unitPrice).RoundInt64());
            var tax = item.TaxType == 1
                ? FixedDecimal.FromInt64(FixedDecimal.MultiplyRatio(net, 5, 100).RoundInt64())
                : new FixedDecimal(0);
            if (declaredNet != net || declaredTax != tax)
                throw new InvalidDataException("折讓單價、金額或稅額與重新計算結果不符");
            total = FixedDecimal.Add(total, FixedDecimal.Add(net, tax));
        }
        if (expectedGross <= 0 || total != FixedDecimal.FromInt64(expectedGross))
            throw new InvalidDataException("本次折讓含稅總額與編輯品項不一致");
    }
}
