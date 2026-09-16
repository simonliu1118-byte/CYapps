using System.Globalization;
using CYInvoice.Core.Amego;

namespace CYInvoice.Core.Invoicing;

public sealed record InvoicePdfStyle(int Code, string Name);

public static class InvoicePdfStyles
{
    public static readonly InvoicePdfStyle A4 = new(0, "A4 整張");
    public static readonly InvoicePdfStyle A4AddressAndA5 = new(1, "A4（地址＋A5）");
    public static readonly InvoicePdfStyle A4TwoA5 = new(2, "A4（A5 內容）");
    public static readonly InvoicePdfStyle A5 = new(3, "A5");
    public static readonly InvoicePdfStyle QrCodeA4 = new(5, "QRcode A4");

    public static IReadOnlyList<InvoicePdfStyle> Company { get; } =
        [A4, A4AddressAndA5, A4TwoA5, A5, QrCodeA4];

    public static IReadOnlyList<InvoicePdfStyle> Consumer { get; } = [A4];

    public static InvoicePdfStyle Require(int code) =>
        Company.FirstOrDefault(style => style.Code == code)
        ?? throw new ArgumentOutOfRangeException(nameof(code), "不支援的發票 PDF 版型");
}

public sealed record InvoicePdfEligibility(bool Allowed, bool CompanyBuyer, string Reason);

public sealed record InvoicePdfDocument(
    string Path,
    string InvoiceNumber,
    InvoicePdfStyle Style,
    bool FromCache);

internal static class InvoicePdfCache
{
    internal const int MaximumPdfBytes = 20 * 1024 * 1024;

    internal static string PathFor(
        string cacheRoot,
        string environment,
        string invoiceNumber,
        int style,
        DateTimeOffset now)
    {
        if (environment is not (Environments.Test or Environments.Production))
            throw new InvalidOperationException("PDF Cache 的環境識別不正確");
        invoiceNumber = invoiceNumber.Trim().ToUpperInvariant();
        if (invoiceNumber.Length is < 1 or > 10 || !invoiceNumber.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("發票號碼格式不正確，無法建立 PDF Cache");
        InvoicePdfStyles.Require(style);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return System.IO.Path.Combine(cacheRoot, environment, date, $"{invoiceNumber}_style{style}.pdf");
    }

    internal static async Task<byte[]?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        byte[] bytes;
        await using (var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length <= 0 || stream.Length > MaximumPdfBytes)
            {
                bytes = [];
            }
            else
            {
                bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        try
        {
            AmegoClient.ValidatePdf(bytes);
            return bytes;
        }
        catch (InvalidDataException)
        {
            File.Delete(path);
            return null;
        }
    }

    internal static async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > MaximumPdfBytes)
            throw new InvalidDataException("downloaded invoice PDF exceeds size limit");
        AmegoClient.ValidatePdf(bytes);
        var directory = System.IO.Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("PDF Cache 路徑不正確");
        Directory.CreateDirectory(directory);
        var temporary = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
