namespace CYInvoice.Core.Storage;

public sealed class BuyerNameStore(string dataDirectory)
{
    private readonly Lock gate = new();
    private readonly string path = Path.Combine(dataDirectory, "buyer_names.json");

    public IReadOnlyDictionary<string, string> LoadOrCreate()
    {
        lock (gate)
        {
            var found = JsonFile.TryRead<Dictionary<string, string>>(path, out var names);
            names ??= new Dictionary<string, string>(StringComparer.Ordinal);
            Validate(names);
            if (!found) JsonFile.Write(path, names);
            return new Dictionary<string, string>(names, StringComparer.Ordinal);
        }
    }

    public bool TryLookup(string ban, out string name)
    {
        lock (gate)
        {
            JsonFile.TryRead<Dictionary<string, string>>(path, out var names);
            names ??= new Dictionary<string, string>(StringComparer.Ordinal);
            Validate(names);
            return names.TryGetValue(ban.Trim(), out name!);
        }
    }

    public bool RememberAfterSuccessfulInvoice(string ban, bool lookupSucceeded, string apiName, string manualName, bool invoiceSucceeded)
    {
        ban = ban.Trim();
        manualName = manualName.Trim();
        if (!lookupSucceeded || apiName.Trim().Length != 0 || !invoiceSucceeded || manualName.Length == 0) return false;
        if (ban.Length != 8 || !ban.All(character => character is >= '0' and <= '9'))
            throw new InvalidDataException("公司統編必須為 8 碼");
        lock (gate)
        {
            JsonFile.TryRead<Dictionary<string, string>>(path, out var names);
            names ??= new Dictionary<string, string>(StringComparer.Ordinal);
            Validate(names);
            if (names.TryGetValue(ban, out var existing) && existing == manualName) return false;
            names[ban] = manualName;
            JsonFile.Write(path, names);
            return true;
        }
    }

    private static void Validate(IReadOnlyDictionary<string, string> names)
    {
        if (names.Any(item => item.Key.Length != 8 || !item.Key.All(character => character is >= '0' and <= '9') || string.IsNullOrWhiteSpace(item.Value)))
            throw new InvalidDataException("buyer_names.json contains an invalid entry");
    }
}
