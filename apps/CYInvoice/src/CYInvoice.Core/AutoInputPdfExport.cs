using System.Security.Cryptography;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.AutoInput;

// The AutoInput PDF bridge is read-only for invoice business state: it uses
// the existing InvoiceService PDF cache/download path, never calls issue.
// The caller must have the original portable single-writer Mutex or IPC.
public sealed class AutoInputPdfExport(LocalRepository repository, InvoiceService service)
{
    public async Task<AutoInputOrderResult> ExportAsync(
        string orderId, string source, string environment,
        string pdfFolder, int style, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = repository.Invoices.LoadOrCreate().Where(record =>
            (record.Environment == environment || record.Environment.Length == 0) &&
            (record.OriginalOrderId == orderId || record.OrderId == orderId ||
             record.ApiOrderId == orderId)).ToArray();
        if (records.Length == 0)
            return new(orderId, "skipped", Message: "本機沒有對應的已開立發票");
        if (records.Length != 1)
            return new(orderId, "unknown", Message: "同一訂單有多筆本機紀錄，禁止輸出紙本");
        var record = records[0];
        if (record.Source != source || record.Environment != environment)
            return new(orderId, "unknown", Message: "來源或環境不符，禁止輸出紙本");
        if (record.BuyerIdentifier.Trim() is "" or "0000000000")
            return new(orderId, "skipped", Message: "一般消費者為會員載具，不列印紙本");
        if (record.BuyerIdentifier.Length != 8 || !record.BuyerIdentifier.All(char.IsAsciiDigit))
            return new(orderId, "unknown", Message: "公司統編格式無法核實");
        if (record.InvoiceState != InvoiceStates.Opened ||
            !System.Text.RegularExpressions.Regex.IsMatch(record.InvoiceNumber, "^[A-Z]{2}[0-9]{8}$") ||
            record.InvoiceDate.Trim().Length == 0 || record.Amount <= 0 ||
            record.ErrorMessage.Trim().Length != 0)
            return new(orderId, "unknown", Message: "發票開立資料不完整或已變更，不得輸出紙本");
        if (record.Delivery != InvoiceService.DeliveryPaper)
            return new(orderId, "unknown", Message: "這筆公司戶發票並非紙本交付，需人工核對");
        var info = new AutoInputOrderResult(orderId, "pending", record.InvoiceNumber, record.InvoiceDate,
            record.BuyerIdentifier, record.BuyerName, record.Amount, "等待光貿完成發票上傳");
        // Local upload status can lag AMEGO's eventual completion. Even when
        // the cached status is still uploading, attempt exactly one official
        // PDF lookup; AMEGO code 99 means pending. Otherwise a headless CLI
        // without the periodic UI sync would stay pending forever.
        if (record.UploadStatus == UploadStatuses.Error)
            return info with { Status = "unknown", Message = "光貿回報上傳錯誤，請至 CYInvoice 核對" };
        if (repository.Settings.LoadOrCreate().Environment != environment)
            return info with { Status = "unknown", Message = "目前環境與發票環境不同" };

        InvoicePdfDocument pdf;
        try
        {
            pdf = await service.GetInvoicePdfAsync(record, style, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("尚未提供這張發票的官方 PDF", StringComparison.Ordinal))
        {
            return info with { Message = "光貿尚未提供官方 PDF，稍後查詢" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Transient errors are NOT proof that a PDF exists or that an
            // earlier invoice attempt failed. Keep the invoice record intact.
            return info with { Status = "unknown", Message = "官方 PDF 取得失敗，請在 CYInvoice 核對" };
        }
        if (pdf.InvoiceNumber != record.InvoiceNumber)
            return info with { Status = "unknown", Message = "官方 PDF 與原發票號碼不一致" };
        var destination = Path.Combine(pdfFolder, record.InvoiceNumber + "_style" + style + ".pdf");
        try
        {
            var src = new FileInfo(pdf.Path);
            if (!src.Exists || src.Length is <= 0 or > 20 * 1024 * 1024)
                throw new InvalidDataException("PDF cache size invalid");
            if (File.Exists(destination))
            {
                // Safe, idempotent on an existing file: don't replace someone
                // else's PDF, and never silently treat conflicting bytes as ours.
                if (!await SameFileAsync(pdf.Path, destination, cancellationToken).ConfigureAwait(false))
                    return info with { Status = "unknown", Message = "目標 PDF 已存在不同內容，不予覆蓋" };
            }
            else
            {
                var temp = Path.Combine(pdfFolder, "." + record.InvoiceNumber + "_" +
                    Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await using (var sourceFile = new FileStream(pdf.Path, FileMode.Open, FileAccess.Read,
                                     FileShare.Read, 81920, FileOptions.Asynchronous))
                    await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await sourceFile.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    File.Move(temp, destination, overwrite: false);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return info with { Status = "ready", Message = "", PdfFile = destination };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return info with { Status = "unknown", Message = "官方 PDF 輸出失敗，請檢查目標資料夾" };
        }
    }

    private static async Task<bool> SameFileAsync(string first, string second, CancellationToken token)
    {
        var a = new FileInfo(first);
        var b = new FileInfo(second);
        if (a.Length != b.Length || b.Length is <= 0 or > 20 * 1024 * 1024)
            return false;
        await using var left = File.OpenRead(first);
        await using var right = File.OpenRead(second);
        var ha = await SHA256.HashDataAsync(left, token).ConfigureAwait(false);
        var hb = await SHA256.HashDataAsync(right, token).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
