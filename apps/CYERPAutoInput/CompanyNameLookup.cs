using System.Text.Json;

namespace CYERPAutoInput;

/// <summary>
/// 統一編號 → registered name through the Ministry of Economic Affairs open data API (no key;
/// user, 2026-10-11): companies first, then businesses (商號). Names found are cached on this
/// PC (Data/ban_names.json) so a known buyer needs no network. Nothing found, offline or a
/// timeout all return an empty name; the caller then leaves 客戶全名 blank.
/// </summary>
internal sealed class CompanyNameLookup
{
    private const string CompanyApi = "https://data.gcis.nat.gov.tw/od/data/api/5F64D864-61CB-4D0D-8AD9-492047CC1EA6";
    private const string BusinessApi = "https://data.gcis.nat.gov.tw/od/data/api/7E6AFA72-AD6A-46D3-8681-ED77951D912D";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private readonly AppLogger _log;
    private readonly string _cachePath;
    private readonly Dictionary<string, string> _cache;
    private readonly object _gate = new();

    public CompanyNameLookup(AppLogger log, string? cachePath = null)
    {
        _log = log;
        _cachePath = cachePath ?? Path.Combine(AppContext.BaseDirectory, "Data", "ban_names.json");
        _cache = LoadCache(_cachePath);
    }

    public static bool IsBan(string? value) => CompanyNameResponse.IsBan(value);

    public async Task<string> LookupAsync(string ban, CancellationToken cancellationToken = default)
    {
        ban = ban.Trim();
        if (!IsBan(ban)) return string.Empty;
        lock (_gate)
            if (_cache.TryGetValue(ban, out var cached)) return cached;

        try
        {
            var name = CompanyNameResponse.ParseName(await GetAsync($"{CompanyApi}?$format=json&$filter=Business_Accounting_NO eq {ban}&$skip=0&$top=1", cancellationToken), "Company_Name");
            if (name.Length == 0)
                name = CompanyNameResponse.ParseName(await GetAsync($"{BusinessApi}?$format=json&$filter=President_No eq {ban}&$skip=0&$top=1", cancellationToken), "Business_Name");
            _log.Info("ban", $"name lookup ban={_log.Value(ban)} found={name.Length > 0}");
            if (name.Length > 0) Remember(ban, name);
            return name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            _log.Warn("ban", $"name lookup failed: {ex.GetType().Name}");
            return string.Empty;
        }
    }

    private static async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private void Remember(string ban, string name)
    {
        lock (_gate)
        {
            _cache[ban] = name;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var temp = _cachePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, _cachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warn("ban", $"name cache not saved: {ex.GetType().Name}");
            }
        }
    }

    private static Dictionary<string, string> LoadCache(string path)
    {
        try
        {
            if (File.Exists(path))
                return new Dictionary<string, string>(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [], StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged cache only costs a network lookup.
        }
        return new(StringComparer.Ordinal);
    }
}
