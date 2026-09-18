namespace CYInvoice.Core;

public static class InvoiceValidator
{
    public static void Validate(InvoiceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.OrderId = draft.OrderId.Trim();
        if (draft.OrderId.Length == 0)
        {
            throw new InvalidOperationException("請輸入訂單編號");
        }
        if (draft.OrderId.Any(char.IsControl))
        {
            throw new InvalidOperationException("訂單編號含有不可使用的控制字元");
        }

        if (draft.Items.Count is 0 or > InvoiceLimits.MaximumItems)
        {
            throw new InvalidOperationException(draft.Items.Count == 0
                ? "請至少輸入一筆商品"
                : $"商品明細最多 {InvoiceLimits.MaximumItems} 筆");
        }

        if (draft.MainRemark.EnumerateRunes().Count() > InvoiceLimits.MaximumRemarkCharacters)
        {
            throw new InvalidOperationException($"發票總備註最多 {InvoiceLimits.MaximumRemarkCharacters} 字");
        }

        if (draft.CompanyBuyer)
        {
            if (!IsEightDigits(draft.BuyerIdentifier))
            {
                throw new InvalidOperationException("公司統編必須為 8 碼");
            }

            if (string.IsNullOrWhiteSpace(draft.BuyerName))
            {
                throw new InvalidOperationException("請輸入買方名稱");
            }
        }
        else if (draft.PricesExcludeTax)
        {
            throw new InvalidOperationException("一般消費者只能使用含稅輸入");
        }

        for (var index = 0; index < draft.Items.Count; index++)
        {
            ValidateItem(draft.Items[index], index);
        }

        var totals = InvoiceCalculator.CalculateTotals(draft.Items, draft.CompanyBuyer, draft.PricesExcludeTax);
        if (draft.TotalAmount <= 0)
        {
            throw new InvalidOperationException("0元不開立發票");
        }
        if (draft.TotalAmount != totals.TotalAmount)
        {
            throw new InvalidOperationException($"發票金額不一致：明細加總 {totals.TotalAmount}，發票總額 {draft.TotalAmount}");
        }
    }

    private static void ValidateItem(InvoiceItem item, int index)
    {
        item.Description = item.Description.Trim();
        (FixedDecimal Quantity, FixedDecimal UnitPrice, FixedDecimal Amount) values;
        try
        {
            values = InvoiceCalculator.ItemDecimals(item);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new InvalidOperationException($"第 {index + 1} 筆商品明細的品名與數量不可空白", error);
        }
        if (item.Description.Length == 0 || values.Quantity.ScaledValue <= 0)
        {
            throw new InvalidOperationException($"第 {index + 1} 筆商品明細的品名與數量不可空白");
        }

        FixedDecimal calculated;
        try
        {
            calculated = FixedDecimal.Multiply(values.Quantity, values.UnitPrice);
        }
        catch (OverflowException error)
        {
            throw new InvalidOperationException($"第 {index + 1} 筆商品金額超出範圍", error);
        }
        if (calculated != values.Amount)
        {
            var roundedMatch = item.AllowSubtotalRounding &&
                calculated.RoundInt64() == item.Amount && values.Amount.RoundInt64() == item.Amount;
            if (!roundedMatch)
            {
                throw new InvalidOperationException(
                    $"第 {index + 1} 筆商品金額不一致：應為 {calculated}，實際為 {values.Amount}");
            }
        }
        if (values.Amount.RoundInt64() != item.Amount)
        {
            throw new InvalidOperationException($"第 {index + 1} 筆商品整數金額與 7 位小數資料不一致");
        }
    }

    private static bool IsEightDigits(string value) => value.Length == 8 && value.All(character => character is >= '0' and <= '9');
}
