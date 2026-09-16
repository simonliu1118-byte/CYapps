namespace CYInvoice.Core.Storage;

public sealed class LocalRepository
{
    private LocalRepository(
        string dataDirectory,
        string cacheDirectory,
        string invoicePdfCacheDirectory,
        string invoicePreviewCacheDirectory,
        SettingsStore settings,
        InvoiceStore invoices,
        BuyerNameStore buyerNames)
    {
        DataDirectory = dataDirectory;
        CacheDirectory = cacheDirectory;
        InvoicePdfCacheDirectory = invoicePdfCacheDirectory;
        InvoicePreviewCacheDirectory = invoicePreviewCacheDirectory;
        Settings = settings;
        Invoices = invoices;
        BuyerNames = buyerNames;
    }

    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string InvoicePdfCacheDirectory { get; }
    public string InvoicePreviewCacheDirectory { get; }
    public SettingsStore Settings { get; }
    public InvoiceStore Invoices { get; }
    public BuyerNameStore BuyerNames { get; }

    public static LocalRepository Open(string baseDirectory, ISecretProtector protector)
    {
        var data = Path.Combine(baseDirectory, "Data");
        var cache = Path.Combine(baseDirectory, "Cache");
        var invoicePdfCache = Path.Combine(cache, "InvoicePDF");
        var invoicePreviewCache = Path.Combine(cache, "InvoicePreview");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(invoicePdfCache);
        Directory.CreateDirectory(invoicePreviewCache);
        var repository = new LocalRepository(
            data,
            cache,
            invoicePdfCache,
            invoicePreviewCache,
            new SettingsStore(data, protector),
            new InvoiceStore(data),
            new BuyerNameStore(data));
        repository.Settings.LoadOrCreate();
        repository.Invoices.LoadOrCreate();
        repository.BuyerNames.LoadOrCreate();
        return repository;
    }
}
