using System.Text.Json;

namespace CYERPAutoInput;

internal enum LedgerStatus
{
    /// <summary>CY saved the ERP document: entering it again is blocked.</summary>
    Saved,
    /// <summary>CY finished the input without saving (auto-save off): a person was to save it.</summary>
    Entered,
    /// <summary>CY entered the header only and handed the order to a person.</summary>
    HandedOff
}

internal sealed record LedgerEntry(LedgerStatus Status, string Document, DateTime Time)
{
    public string Describe() => Status switch
    {
        LedgerStatus.Saved => $"已於 {Time:yyyy/MM/dd HH:mm} 儲存為 {DisplayDocument}",
        LedgerStatus.Entered => $"曾於 {Time:yyyy/MM/dd HH:mm} 輸入 {DisplayDocument}（未由 CY 儲存）",
        _ => $"曾於 {Time:yyyy/MM/dd HH:mm} 輸入單頭 {DisplayDocument} 後轉人工"
    };

    public string ShortText => Status switch
    {
        LedgerStatus.Saved => "已儲存過",
        LedgerStatus.Entered => "曾輸入（未儲存）",
        _ => "曾轉人工"
    };

    private string DisplayDocument => Document.Length > 0 ? Document : "（單號未取得）";
}

/// <summary>
/// Local record of platform orders CY has entered (key: source + platform order number),
/// so the same order is not entered twice (user, 2026-10-10): saved orders are blocked,
/// entered-but-unsaved and handed-off orders only warn. Lives in Data/ on this PC and is
/// never committed (order numbers are business data).
/// </summary>
internal sealed class OrderLedger
{
    private readonly string _path;
    private readonly Dictionary<string, LedgerEntry> _entries;

    public OrderLedger(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "order_ledger.json");

    public LedgerEntry? Find(string key) => _entries.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>Records an outcome; a saved order is never downgraded by a later unsaved run.</summary>
    public void Record(string key, LedgerStatus status, string document, DateTime? time = null)
    {
        if (_entries.TryGetValue(key, out var existing) && existing.Status == LedgerStatus.Saved && status != LedgerStatus.Saved)
            return;
        _entries[key] = new LedgerEntry(status, document, time ?? DateTime.Now);
        Save();
    }

    private static Dictionary<string, LedgerEntry> Load(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, LedgerEntry>>(File.ReadAllText(path));
        return new Dictionary<string, LedgerEntry>(loaded ?? [], StringComparer.Ordinal);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, overwrite: true);
    }
}
