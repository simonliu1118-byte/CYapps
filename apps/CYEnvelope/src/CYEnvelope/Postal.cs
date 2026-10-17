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

    public static string? Infer(string address)
    {
        var normalized = address.Trim().Replace("台", "臺").Replace(" ", "");
        if (normalized.Length == 0) return null;
        if (normalized.Length >= 3 && normalized[..3].All(char.IsDigit))
            return Districts.Any(x => x.Code == normalized[..3]) ? normalized[..3] : null;
        var qualified = Districts
            .Where(x => x.County.Length > 0 && normalized.Contains(x.County) &&
                        (x.District.Length == 0 || normalized.Contains(x.District)))
            .OrderByDescending(x => x.District.Length).FirstOrDefault();
        if (qualified is not null) return qualified.Code;
        var candidates = Districts.Where(x => x.District.Length >= 2 && normalized.Contains(x.District))
            .GroupBy(x => x.Code).ToArray();
        return candidates.Length == 1 ? candidates[0].Key : null;
    }
}
