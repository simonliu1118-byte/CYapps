using System.Text.Json;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

// invoice_query allowance[] is the existing official history shown in invoice
// details. Its effective gross amount, NOT original item quantities, is the
// sole monetary ceiling for a new allowance. Unknown history fails closed.
public static class InvoiceAllowanceMetadata
{
    private const string OfficialAllowancesKey = "cyinvoice_official_allowances";
    private const string SummaryPresentKey = "cyinvoice_allowance_summary_present";

    public static void ApplyQuery(InvoiceRecord record,
        IReadOnlyList<InvoiceAllowanceResult> allowances, bool summaryPresent = true)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(allowances);
        record.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        record.ExtensionData[SummaryPresentKey] = JsonSerializer.SerializeToElement(summaryPresent);
        // Always retain the returned history for existing read-only views
        // and the V3.1 manual reconciliation workflow. Completeness is a
        // separate signal: new V3.2 monetary caps MUST require the true
        // SummaryPresentKey, even when an older/synthetic query has allowances.
        record.ExtensionData[OfficialAllowancesKey] =
            JsonSerializer.SerializeToElement(allowances);
    }

    public static IReadOnlyList<InvoiceAllowanceResult> ReadOfficial(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(OfficialAllowancesKey, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        try { return value.Deserialize<List<InvoiceAllowanceResult>>() ?? []; }
        catch (JsonException error)
        {
            throw new InvalidDataException("官方折讓資料格式錯誤", error);
        }
    }

    public static FixedDecimal RemainingGross(InvoiceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.ExtensionData is null ||
            !record.ExtensionData.TryGetValue(SummaryPresentKey, out var marker) ||
            marker.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(
                "光貿尚未提供完整折讓摘要，不能確認本次可折讓金額，請重新查詢");
        if (record.Amount<=0)
            throw new InvalidDataException("原發票含稅總額無效");
        var original=FixedDecimal.FromInt64(record.Amount);
        var deducted=new FixedDecimal(0);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var allowance in ReadOfficial(record))
        {
            var type=allowance.InvoiceType.Trim().ToUpperInvariant();
            // g0501 is a cancellation event, not a new effective deduction.
            if (type is "D0501" or "B0501" or "B0201" or "B0202")
                continue;
            if (type is not ("D0401" or "B0401" or "B0101" or "B0102") ||
                allowance.AllowanceType!=2 ||
                allowance.AllowanceNumber.Trim().Length==0 ||
                !seen.Add(allowance.AllowanceNumber.Trim()))
                throw new InvalidDataException("官方折讓紀錄不完整或重複，無法確認剩餘額");
            if (allowance.InvoiceStatus != UploadStatuses.Complete)
                throw new InvalidOperationException(
                    "光貿有尚未確認的折讓，請待官方完成後重新查詢");
            if (allowance.CancelDate>0)continue;
            FixedDecimal inclusive;
            try
            {
                var net=FixedDecimal.Parse(allowance.TotalAmount);
                var tax=FixedDecimal.Parse(allowance.TaxAmount);
                if (net.ScaledValue<0||tax.ScaledValue<0)
                    throw new FormatException();
                inclusive=FixedDecimal.Add(net,tax);
                deducted=FixedDecimal.Add(deducted,inclusive);
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new InvalidDataException("官方折讓金額無法核對",error);
            }
        }
        var remainder=checked(original.ScaledValue-deducted.ScaledValue);
        if (remainder<0)
            throw new InvalidDataException("已折讓總額超過原發票金額，請人工核對");
        return new FixedDecimal(remainder);
    }
}
