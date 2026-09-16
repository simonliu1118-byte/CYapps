using CYInvoice.Core;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;
using PDFtoImage;

namespace CYInvoice.WinForms;

internal static class InvoicePdfPreview
{
    public static async Task<Bitmap> LoadFirstPageAsync(
        InvoiceRecord record,
        LocalRepository repository,
        InvoiceService service,
        CancellationToken cancellationToken = default)
    {
        var document = await service.GetInvoicePdfAsync(record, InvoicePdfStyles.A4.Code, cancellationToken);
        var previewPath = InvoicePreviewCache.PathForPdf(
            repository.InvoicePreviewCacheDirectory,
            repository.InvoicePdfCacheDirectory,
            document.Path);

        var cached = await InvoicePreviewCache.TryReadAsync(previewPath, cancellationToken);
        if (cached is not null)
        {
            try { return Decode(cached); }
            catch (ArgumentException)
            {
                try { File.Delete(previewPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }

        var rendered = await Task.Run(
            () => RenderFirstPage(document.Path, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        await InvoicePreviewCache.WriteAsync(previewPath, rendered, cancellationToken).ConfigureAwait(false);
        return Decode(rendered);
    }

    private static byte[] RenderFirstPage(string pdfPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "CYInvoice", "Preview");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var pdf = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Conversion.SavePng(temporaryPath, pdf, page: 0);
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(temporaryPath);
            if (!info.Exists || info.Length > InvoicePreviewCache.MaximumPreviewBytes)
                throw new InvalidDataException("官方 PDF 第一頁轉換後的預覽圖不正確");
            var bytes = File.ReadAllBytes(temporaryPath);
            if (!InvoicePreviewCache.HasValidSignature(bytes))
                throw new InvalidDataException("官方 PDF 第一頁未能轉換成有效 PNG");
            return bytes;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image.Width <= 0 || image.Height <= 0 || image.Width > 6000 || image.Height > 6000)
            throw new InvalidDataException("發票預覽圖尺寸不正確");
        return new Bitmap(image);
    }
}
