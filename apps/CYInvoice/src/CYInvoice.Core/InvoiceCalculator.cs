using System.Globalization;

namespace CYInvoice.Core;

public readonly record struct InvoiceTotals(long SalesAmount, long TaxAmount, long TotalAmount);

public static class InvoiceCalculator
{
    public static InvoiceTotals CalculateTotals(
        IEnumerable<InvoiceItem> items,
        bool companyBuyer,
        bool pricesExcludeTax)
    {
        if (pricesExcludeTax && !companyBuyer)
        {
            throw new InvalidOperationException("未稅輸入只適用於公司統編發票");
        }

        var subtotal = new FixedDecimal(0);
        foreach (var item in items)
        {
            subtotal = FixedDecimal.Add(subtotal, ItemDecimals(item).Amount);
        }

        if (pricesExcludeTax)
        {
            var sales = subtotal.RoundInt64();
            var tax = FixedDecimal.MultiplyRatio(FixedDecimal.FromInt64(sales), 1, 20).RoundInt64();
            return new InvoiceTotals(sales, tax, checked(sales + tax));
        }

        var total = subtotal.RoundInt64();
        var salesAmount = FixedDecimal.MultiplyRatio(FixedDecimal.FromInt64(total), 20, 21).RoundInt64();
        return new InvoiceTotals(salesAmount, checked(total - salesAmount), total);
    }

    public static (FixedDecimal Quantity, FixedDecimal UnitPrice, FixedDecimal Amount) ItemDecimals(InvoiceItem item)
    {
        var quantityText = string.IsNullOrWhiteSpace(item.QuantityDecimal)
            ? item.Quantity.ToString("F7", CultureInfo.InvariantCulture)
            : item.QuantityDecimal.Trim();
        return (
            FixedDecimal.Parse(quantityText),
            DecimalOrInteger(item.UnitPriceDecimal, item.UnitPrice),
            DecimalOrInteger(item.AmountDecimal, item.Amount));
    }

    private static FixedDecimal DecimalOrInteger(string text, long integer) =>
        string.IsNullOrWhiteSpace(text) ? FixedDecimal.FromInt64(integer) : FixedDecimal.Parse(text);
}
