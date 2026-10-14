using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>
/// 新竹物流 到廠取貨總表 (user, 2026-10-14): one sheet lists several orders, and a later 批次出貨
/// gives a sheet with the earlier orders on it again. The sheet's rows (宅配單號, 出貨單號) are
/// read from the PDF's words, and a sheet listing any order whose sheet was printed before is
/// not printed again (A printed → the A+B+C sheet is not; B and C get their labels only).
/// </summary>
internal static partial class MoPickupSummary
{
    /// <summary>The 宅配單號 of the sheet: a 10-digit word followed by a 14-digit 出貨單號.</summary>
    public static List<string> Numbers(IReadOnlyList<string> words)
    {
        var numbers = new List<string>();
        for (var i = 0; i + 1 < words.Count; i++)
            if (Tracking().IsMatch(words[i].Trim()) && ShipmentNo().IsMatch(words[i + 1].Trim()) && !numbers.Contains(words[i].Trim()))
                numbers.Add(words[i].Trim());
        return numbers;
    }

    public static List<string> Read(string path, string password)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { Password = password });
        return Numbers(document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList());
    }

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex Tracking();

    [GeneratedRegex(@"^\d{14}$")]
    private static partial Regex ShipmentNo();
}

/// <summary>The 宅配單號 whose 到廠取貨總表 was printed, kept 30 days (Data/pickup_summaries.json).</summary>
internal sealed class PickupSummaryLedger
{
    private readonly string _path;
    private readonly Dictionary<string, DateTime> _printed;

    public PickupSummaryLedger(string path)
    {
        _path = path;
        _printed = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? []
            : [];
        foreach (var old in _printed.Where(p => p.Value < DateTime.Now.AddDays(-30)).Select(p => p.Key).ToList()) _printed.Remove(old);
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "pickup_summaries.json");

    /// <summary>The sheet's orders printed on an earlier sheet.</summary>
    public List<string> AlreadyPrinted(IEnumerable<string> numbers) => numbers.Where(_printed.ContainsKey).ToList();

    public void Record(IEnumerable<string> numbers)
    {
        foreach (var n in numbers) _printed[n] = DateTime.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_printed));
        File.Move(temp, _path, overwrite: true);
    }
}
