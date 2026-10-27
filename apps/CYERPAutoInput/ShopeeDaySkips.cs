using System.Text.Json;

namespace CYERPAutoInput;

/// <summary>
/// Orders 蝦皮全自動 passes over for the rest of the day (user, 2026-10-26: 買家門市關轉 → skipped
/// today however often the program is started again, tried again the next day). Kept in
/// Data/ on this PC with the day it is for; another day's list counts as empty.
/// </summary>
internal static class ShopeeDaySkips
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "shopee_day_skips.json");

    private sealed record Day(string Date, Dictionary<string, string> Orders);

    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Order → reason for <paramref name="today"/>; empty for another day or unreadable text.</summary>
    public static Dictionary<string, string> Today(string? json, DateTime today)
    {
        try
        {
            var day = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Day>(json);
            return day is not null && day.Date == Key(today) && day.Orders is not null ? new(day.Orders) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The list for <paramref name="today"/> with <paramref name="orderSn"/> added.</summary>
    public static string With(string? json, DateTime today, string orderSn, string reason)
    {
        var orders = Today(json, today);
        orders[orderSn] = reason;
        return JsonSerializer.Serialize(new Day(Key(today), orders), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static Dictionary<string, string> Load(DateTime today, string? path = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) ? Today(File.ReadAllText(path), today) : []; }
        catch (IOException) { return []; }
    }

    public static void Add(DateTime today, string orderSn, string reason, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, With(json, today, orderSn, reason));
    }
}
