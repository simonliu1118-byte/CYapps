namespace CYInvoice.Core.AutoInput;

// No AMEGO calls occur until this classification has excluded prior/uncertain attempts.
public static class AutoInputRecordDecision
{
    public static AutoInputOrderResult? Classify(
        IEnumerable<InvoiceRecord> allRecords, string source, string orderId,
        long expectedAmount, string buyerBan, string environment)
    {
        var records = allRecords.Where(record =>
            (record.Environment == environment || record.Environment.Length == 0) &&
            (string.Equals(record.OriginalOrderId, orderId, StringComparison.Ordinal) ||
             string.Equals(record.OrderId, orderId, StringComparison.Ordinal) ||
             string.Equals(record.ApiOrderId, orderId, StringComparison.Ordinal)))
            .ToArray();
        if (records.Length == 0) return null;
        if (records.Length != 1)
            return new(orderId, "unknown", Message: "同一環境存在多筆相同訂單紀錄，須人工核對");
        var record = records[0];
        if (record.Source != source || record.Amount != expectedAmount ||
            (buyerBan.Length != 0 && buyerBan != "0000000000" &&
             !string.Equals(record.BuyerIdentifier, buyerBan, StringComparison.Ordinal)))
            return new(orderId, "unknown", Message: "歷史發票來源、買方或金額與匯入檔不一致");

        if (record.InvoiceState == InvoiceStates.Opened &&
            record.InvoiceNumber.Trim().Length != 0 && record.ErrorMessage.Trim().Length == 0 &&
            record.InvoiceDate.Trim().Length != 0)
            return new(orderId, "existing", record.InvoiceNumber, record.InvoiceDate,
                record.BuyerIdentifier == "0000000000" ? string.Empty : record.BuyerIdentifier,
                record.BuyerName, record.Amount);

        if (record.InvoiceState == InvoiceStates.Failed)
            return new(orderId, "failed", Message: "先前開立明確失敗，請在 CYInvoice 人工核對後處理");

        return new(orderId, "unknown", Message: "先前開立結果尚未完整確認，請先在 CYInvoice 回查");
    }
}
