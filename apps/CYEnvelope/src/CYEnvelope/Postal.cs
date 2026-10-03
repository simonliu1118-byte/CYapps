using System.IO;

namespace CYEnvelope;

public static class Postal
{
    private sealed record Entry(string County, string District, string Code);
    private static readonly List<Entry> Districts = Load();

    private static List<Entry> Load()
    {
        var result = new List<Entry>();
        var resource = typeof(Postal).Assembly.GetManifestResourceStream("CYEnvelope.postal.tsv");
        if (resource is null) return result;
        using var reader = new StreamReader(resource);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 3) result.Add(new(parts[0], parts[1], parts[2]));
        }
        return result;
    }

    // One comparison form for inference and for matching saved addresses.
    public static string Normalize(string address) =>
        string.Concat(address.Replace("台", "臺").Where(c => !char.IsWhiteSpace(c)));

    public static bool SameAddress(string left, string right) => Normalize(left) == Normalize(right);

    public static string? Infer(string address)
    {
        var normalized = Normalize(address);
        if (normalized.Length == 0) return null;
        if (normalized.Length >= 3 && normalized[..3].All(char.IsDigit))
            return Districts.Any(x => x.Code == normalized[..3]) ? normalized[..3] : null;
        // Districts are stored without 市／區 suffixes, so match only the text after the county
        // (彰化縣員林市 must not match 彰化) and prefer the district written first
        // (臺北市中山區中正路 is 中山, not 中正). County-only rows such as 新竹市 come last.
        var qualified = Districts
            .Select(x => (Entry: x, Position: DistrictPosition(normalized, x)))
            .Where(x => x.Position >= 0)
            .OrderBy(x => x.Entry.District.Length == 0)
            .ThenBy(x => x.Position)
            .ThenByDescending(x => x.Entry.District.Length)
            .FirstOrDefault();
        if (qualified.Entry is not null) return qualified.Entry.Code;
        // A known county without a recognised district is left for the user rather than guessed.
        if (Districts.Any(x => x.County.Length > 0 && normalized.Contains(x.County))) return null;
        var candidates = Districts.Where(x => x.District.Length >= 2 && normalized.Contains(x.District))
            .GroupBy(x => x.Code).ToArray();
        return candidates.Length == 1 ? candidates[0].Key : null;
    }

    private static int DistrictPosition(string address, Entry entry)
    {
        if (entry.County.Length == 0) return -1;
        var county = address.IndexOf(entry.County, StringComparison.Ordinal);
        if (county < 0) return -1;
        if (entry.District.Length == 0) return 0;
        return address.IndexOf(entry.District, county + entry.County.Length, StringComparison.Ordinal);
    }
}
