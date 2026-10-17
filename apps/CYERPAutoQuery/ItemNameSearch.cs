using System.Text;

namespace CYERPAutoQuery;

/// <summary>
/// 品名 search (user, 2026-10-16): INVI02 F6 → 進階設定 → the condition text, in ERP's own
/// form 「(INVMB.MB002 like N'%穴位貼%')」, pasted into the condition box → 確定 → ERP lists
/// the matches on 資料瀏覽, exported to Excel. The user then picks one and it is queried by
/// 品號. Pure rules, unit tested on any OS.
/// </summary>
internal static class ItemNameRules
{
    public const int MaxLength = 40;

    /// <summary>
    /// The ERP column of 品名 (MB002 of table INVMB, as ERP writes it in 進階設定). Fixed in
    /// code, not in a settings file: the condition is SQL text and must only ever hold this
    /// column and a checked keyword.
    /// </summary>
    public const string NameColumn = "INVMB.MB002";

    /// <summary>Characters that would end ERP's N'…' string (') or act as LIKE wildcards (% _ [ ]).</summary>
    private const string Forbidden = "'%_[]";

    /// <summary>One keyword: trimmed, 1..40 characters, no control characters and none of ' % _ [ ].</summary>
    public static bool TryNormalize(string? input, out string keyword, out string error)
    {
        keyword = (input ?? string.Empty).Trim();
        error = string.Empty;
        if (keyword.Length == 0)
        {
            error = "請輸入品名關鍵字。";
            return false;
        }
        if (keyword.Length > MaxLength)
        {
            error = $"品名關鍵字超過 {MaxLength} 字。";
            return false;
        }
        if (keyword.Any(char.IsControl))
        {
            error = "品名關鍵字不可包含控制字元。";
            return false;
        }
        if (keyword.Any(c => Forbidden.Contains(c)))
        {
            error = "品名關鍵字不可包含 ' % _ [ ] 這些符號。";
            return false;
        }
        return true;
    }

    /// <summary>The 進階設定 condition text for <paramref name="keyword"/> (already normalized).</summary>
    public static string Condition(string keyword) => $"({NameColumn} like N'%{keyword}%')";

    /// <summary>True when the condition box shows <paramref name="expected"/> (ERP may add spaces or line breaks).</summary>
    public static bool SameCondition(string? shown, string expected) =>
        Collapse(shown) == Collapse(expected);

    /// <summary>
    /// Whether ERP's 品名 contains the keyword the way SQL Server's case- and width-insensitive
    /// LIKE matches it (A = a = Ａ): every listed item must, or the condition was not applied.
    /// </summary>
    public static bool NameContains(string name, string keyword) =>
        Fold(name).Contains(Fold(keyword), StringComparison.Ordinal);

    private static string Fold(string value) => value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static string Collapse(string? value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
}

/// <summary>The items whose 品名 contains a keyword, as ERP listed them on 資料瀏覽.</summary>
internal sealed record ItemSearchResult(
    string Keyword,
    IReadOnlyList<IReadOnlyList<ErpField>> Items,
    int? Limit,
    TimeSpan Elapsed)
{
    /// <summary>True when ERP's 總資料筆數限制 was reached, so more items may match.</summary>
    public bool Truncated => Limit is int limit && limit > 0 && Items.Count >= limit;

    public static string Value(IReadOnlyList<ErpField> row, string name) =>
        row.FirstOrDefault(f => f.Name == name)?.Value ?? string.Empty;
}

/// <summary>
/// Reads the INVI02 export taken from 資料瀏覽 after a 品名 search (user's sample
/// 2026-10-16): sheet 單頭資料, one row per listed item, the same columns as a 品號 export.
/// </summary>
internal static class ItemSearchExport
{
    public static IReadOnlyList<IReadOnlyList<ErpField>> Parse(
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string keyword)
    {
        if (sheets.Count == 0) throw new InvalidDataException("ERP 匯出的 Excel 沒有工作表。");
        var header = sheets.FirstOrDefault(s => s.Name == Invi02Export.HeaderSheet);
        if (header.Rows is null) header = sheets[0];
        var items = Invi02Export.Table(header.Rows, "品號", "單頭")
            .Select(row => ItemFieldCatalog.Select(row, ItemFieldCatalog.SearchList, "單頭資料"))
            .ToList();
        foreach (var item in items)
        {
            var name = ItemSearchResult.Value(item, "品名");
            if (!ItemNameRules.NameContains(name, keyword))
                throw new InvalidDataException($"ERP 列出的品號「{ItemSearchResult.Value(item, "品號")}」品名不含「{keyword}」，查詢條件可能沒有套用，結果不採用。");
        }
        return items;
    }
}
