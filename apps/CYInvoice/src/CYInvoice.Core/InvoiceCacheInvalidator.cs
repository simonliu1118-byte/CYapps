namespace CYInvoice.Core.Storage;

public static class InvoiceCacheInvalidator
{
    public static IReadOnlyList<string> Invalidate(LocalRepository repository, string environment, string invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(repository);
        environment = environment.Trim();
        invoiceNumber = invoiceNumber.Trim();
        if (invoiceNumber.Length == 0) return [];
        var problems = new List<string>();
        DeleteMatching(repository.InvoicePdfCacheDirectory, environment, invoiceNumber, problems);
        DeleteMatching(repository.InvoicePreviewCacheDirectory, environment, invoiceNumber, problems);
        return problems;
    }

    private static void DeleteMatching(string root, string environment, string invoiceNumber, List<string> problems)
    {
        var scoped = environment.Length == 0 ? root : Path.Combine(root, environment);
        if (!Directory.Exists(scoped)) return;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(scoped, "*", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception error)
        {
            problems.Add($"快取掃描失敗：{error.Message}");
            return;
        }

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(invoiceNumber + "_style", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception error)
            {
                problems.Add($"{name} 快取刪除失敗：{error.Message}");
            }
        }
    }
}
