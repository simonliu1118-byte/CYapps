namespace CYInvoice.Core.Invoicing;

public static class InvoicePreviewCache
{
    public const int MaximumPreviewBytes = 20 * 1024 * 1024;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static string PathForPdf(string previewRoot, string pdfRoot, string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        var relative = Path.GetRelativePath(Path.GetFullPath(pdfRoot), Path.GetFullPath(pdfPath));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("PDF Cache 路徑不在允許的目錄內");

        var previewPath = Path.GetFullPath(Path.Combine(previewRoot, Path.ChangeExtension(relative, ".page1.png")));
        var fullRoot = Path.GetFullPath(previewRoot) + Path.DirectorySeparatorChar;
        if (!previewPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("預覽圖 Cache 路徑不正確");
        return previewPath;
    }

    public static async Task<byte[]?> TryReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < PngSignature.Length || info.Length > MaximumPreviewBytes) return null;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return HasValidSignature(bytes) ? bytes : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumPreviewBytes) throw new InvalidDataException("invoice preview exceeds size limit");
        if (!HasValidSignature(bytes)) throw new InvalidDataException("invoice preview is not a valid PNG");

        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("預覽圖 Cache 路徑不正確");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool HasValidSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= PngSignature.Length && bytes[..PngSignature.Length].SequenceEqual(PngSignature);
}
