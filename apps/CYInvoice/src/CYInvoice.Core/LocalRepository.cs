namespace CYInvoice.Core.Storage;

public sealed class LocalRepository
{
    private readonly IdentityProviderRuntime identityRuntime;

    private LocalRepository(
        string dataDirectory,
        string cacheDirectory,
        string invoicePdfCacheDirectory,
        string invoicePreviewCacheDirectory,
        SettingsStore settings,
        InvoiceStore invoices,
        BuyerNameStore buyerNames,
        EmployeeStore employees,
        CloudEmployeeCacheStore cloudEmployees,
        IdentityProviderRuntime identityRuntime)
    {
        DataDirectory = dataDirectory;
        CacheDirectory = cacheDirectory;
        InvoicePdfCacheDirectory = invoicePdfCacheDirectory;
        InvoicePreviewCacheDirectory = invoicePreviewCacheDirectory;
        Settings = settings;
        Invoices = invoices;
        BuyerNames = buyerNames;
        Employees = employees;
        CloudEmployees = cloudEmployees;
        this.identityRuntime = identityRuntime;
    }

    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string InvoicePdfCacheDirectory { get; }
    public string InvoicePreviewCacheDirectory { get; }
    public SettingsStore Settings { get; }
    public InvoiceStore Invoices { get; }
    public BuyerNameStore BuyerNames { get; }
    public EmployeeStore Employees { get; }
    public CloudEmployeeCacheStore CloudEmployees { get; }
    public IIdentityProvider IdentityProvider => identityRuntime.Current;

    public bool UsesCloudEmployeeAuthority() =>
        IdentityProvider.Kind == IdentityProviderKind.BuiltInCloud;

    public bool HasAuthorityEmployees() =>
        IdentityProvider.Kind == IdentityProviderKind.BuiltInCloud
            ? CloudEmployees.LoadAll().Count != 0
            : Employees.HasEmployees();

    public IReadOnlyList<EmployeeAccount> LoadAuthorityEmployees() =>
        IdentityProvider.Kind == IdentityProviderKind.BuiltInCloud
            ? CloudEmployees.LoadAll().Select(ToEmployeeAccount).ToArray()
            : Employees.LoadAll();

    public AppPrincipal? AuthenticatePrincipal(string employeeNo, string password) =>
        IdentityProvider
            .AuthenticateAsync(new IdentityAuthenticationRequest(employeeNo, password))
            .GetAwaiter()
            .GetResult();

    public EmployeeAccount? AuthenticateEmployee(string employeeNo, string password)
    {
        var principal = AuthenticatePrincipal(employeeNo, password);
        return principal?.ToEmployeeAccount();
    }

    private static EmployeeAccount ToEmployeeAccount(CloudEmployeeCachedAccount account) => new(
        account.EmployeeNo,
        account.Name,
        account.Email,
        account.Role,
        account.Enabled,
        account.SyncedUtc,
        account.SyncedUtc);

    public static LocalRepository Open(string baseDirectory, ISecretProtector protector)
    {
        var data = Path.Combine(baseDirectory, "Data");
        var cache = Path.Combine(baseDirectory, "Cache");
        var invoicePdfCache = Path.Combine(cache, "InvoicePDF");
        var invoicePreviewCache = Path.Combine(cache, "InvoicePreview");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(invoicePdfCache);
        Directory.CreateDirectory(invoicePreviewCache);

        var settings = new SettingsStore(data, protector);
        var currentSettings = settings.LoadOrCreate();
        SqliteBootstrapper.EnsureMigrated(data, currentSettings.ProductionInvoice);

        var invoices = new InvoiceStore(data, currentSettings.ProductionInvoice);
        var buyerNames = new BuyerNameStore(data);
        var employees = new EmployeeStore(data);
        var cloudEmployees = new CloudEmployeeCacheStore(data, protector);
        var identityRuntime = new IdentityProviderRuntime(
            settings,
            new LocalIdentityProvider(employees),
            new BuiltInCloudIdentityProvider(cloudEmployees));
        invoices.LoadOrCreate();
        buyerNames.LoadOrCreate();

        return new LocalRepository(
            data,
            cache,
            invoicePdfCache,
            invoicePreviewCache,
            settings,
            invoices,
            buyerNames,
            employees,
            cloudEmployees,
            identityRuntime);
    }
}
