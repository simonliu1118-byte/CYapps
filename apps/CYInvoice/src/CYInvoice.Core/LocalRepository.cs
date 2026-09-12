namespace CYInvoice.Core.Storage;

public sealed class LocalRepository
{
    private LocalRepository(string dataDirectory, SettingsStore settings, InvoiceStore invoices, BuyerNameStore buyerNames)
    {
        DataDirectory = dataDirectory;
        Settings = settings;
        Invoices = invoices;
        BuyerNames = buyerNames;
    }

    public string DataDirectory { get; }
    public SettingsStore Settings { get; }
    public InvoiceStore Invoices { get; }
    public BuyerNameStore BuyerNames { get; }

    public static LocalRepository Open(string baseDirectory, ISecretProtector protector)
    {
        var data = Path.Combine(baseDirectory, "Data");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(baseDirectory, "Cache", "InvoicePDF"));
        var repository = new LocalRepository(data, new SettingsStore(data, protector), new InvoiceStore(data), new BuyerNameStore(data));
        repository.Settings.LoadOrCreate();
        repository.Invoices.LoadOrCreate();
        repository.BuyerNames.LoadOrCreate();
        return repository;
    }
}
