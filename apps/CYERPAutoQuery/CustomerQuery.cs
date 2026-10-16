namespace CYERPAutoQuery;

/// <summary>Customer query rules (COPI01, user 2026-10-16). Pure, unit tested on any OS.</summary>
internal static class CustomerRules
{
    public const string CodeName = "客戶代號";

    /// <summary>
    /// The 進階設定 condition for a customer name: 簡稱 OR 全名, always both (user, 2026-10-16:
    /// so a name written in either is found). Written the way ERP writes it (user's screenshot:
    /// 「(COPMA.MA002 = N'佳德')」, next line 「  OR (COPMA.MA003 = N'佳德')」), with like.
    /// The keyword is checked by <see cref="ItemNameRules.TryNormalize"/> (no quote or wildcard).
    /// </summary>
    public static string NameCondition(string keyword) =>
        $"(COPMA.MA002 like N'%{keyword}%')\r\n  OR (COPMA.MA003 like N'%{keyword}%')";
}

/// <summary>One customer: the <see cref="CustomerFieldCatalog"/> fields of COPI01's export.</summary>
internal sealed record CustomerQueryResult(string Code, IReadOnlyList<ErpField> Fields, TimeSpan Elapsed)
{
    public bool Found { get; init; } = true;

    public string Value(string name) => Fields.FirstOrDefault(f => f.Name == name)?.Value ?? string.Empty;

    public static CustomerQueryResult NotFound(string code, TimeSpan elapsed) => new(code, [], elapsed) { Found = false };
}

/// <summary>
/// The customer fields delivered (user, 2026-10-16), in display order. Some are built from two
/// export columns: a code with its name (付款條件 R09 + 現金電匯-月結30天) or a 郵遞區號 with its
/// address. COPI01's export repeats 「郵遞區號」 four times, once per address, in this order:
/// 登記、發票、送貨、文件 — so a column is named with its occurrence.
/// </summary>
internal static class CustomerFieldCatalog
{
    /// <summary>A delivered field: its label and how it is read from one exported row.</summary>
    internal sealed record Field(string Label, Func<CustomerRow, string> Read, params string[] Columns);

    private static Field Plain(string label, string column) => new(label, r => r[column], column);

    private static Field CodeWithName(string label, string code, string name) =>
        new(label, r => Join(r[code], r[name]), code, name);

    private static Field Address(string label, int zipOccurrence, string address) =>
        new(label, r => Join(r["郵遞區號", zipOccurrence], r[address]), $"郵遞區號#{zipOccurrence}", address);

    private static string Join(string first, string second) =>
        string.Join(" ", new[] { first.Trim(), second.Trim() }.Where(v => v.Length > 0));

    public static readonly IReadOnlyList<Field> Fields =
    [
        Plain("客戶代號", "客戶代號"),
        Plain("客戶簡稱", "客戶簡稱"),
        Plain("客戶全名", "客戶全名"),
        Plain("負責人", "負責人"),
        Plain("連絡人", "連絡人"),
        Plain("TEL", "TEL_NO"),
        Plain("FAX", "FAX NO"),
        Plain("統一編號", "統一編號"),
        Plain("行動電話", "行動電話"),
        Plain("部門別", "部門別"),
        Plain("業務人員", "業務人員"),
        Address("登記地址", 1, "登記地址(一)"),
        Address("送貨地址", 3, "送貨地址(一)"),
        CodeWithName("付款條件", "付款條件", "付款條件名稱"),
        Plain("發票聯數", "發票聯數"),
        Plain("課稅別", "課稅別"),
        CodeWithName("通路別", "通路別", "通路簡稱"),
        CodeWithName("型態別", "型態別", "型態簡稱"),
        Plain("客戶描述", "客戶描述"),
    ];

    /// <summary>The columns of the name search list; a row is picked and queried by 客戶代號.</summary>
    public static readonly IReadOnlyList<Field> SearchList =
    [
        Plain("客戶代號", "客戶代號"),
        Plain("客戶簡稱", "客戶簡稱"),
        Plain("客戶全名", "客戶全名"),
        Plain("TEL", "TEL_NO"),
        Plain("統一編號", "統一編號"),
        Plain("業務人員", "業務人員"),
    ];

    public static IReadOnlyList<ErpField> Select(CustomerRow row, IReadOnlyList<Field> catalog)
    {
        var missing = catalog.SelectMany(f => f.Columns).Where(c => !row.Has(c)).Distinct().ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"ERP 匯出的客戶資料缺少欄位：{string.Join("、", missing)}。");
        return catalog.Select(f => new ErpField(f.Label, f.Read(row))).ToList();
    }
}

/// <summary>One exported customer row; a title used more than once is reached by its occurrence (1-based).</summary>
internal sealed class CustomerRow(string[] titles, string[] values)
{
    public string this[string title] => this[title, 1];

    public string this[string title, int occurrence]
    {
        get
        {
            var index = IndexOf(title, occurrence);
            return index >= 0 && index < values.Length ? values[index] : string.Empty;
        }
    }

    /// <summary>「郵遞區號#3」 names the third 郵遞區號 column.</summary>
    public bool Has(string column)
    {
        var parts = column.Split('#');
        return IndexOf(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 1) >= 0;
    }

    private int IndexOf(string title, int occurrence)
    {
        var seen = 0;
        for (var i = 0; i < titles.Length; i++)
            if (titles[i] == title && ++seen == occurrence) return i;
        return -1;
    }
}

/// <summary>
/// Reads COPI01's 「轉出至 Excel」／「轉出全部」 workbook (user's samples 2026-10-16): sheet
/// 單頭資料, row 1 title, row 3 column titles starting with 客戶代號, one row per customer.
/// </summary>
internal static class CustomerExport
{
    private static List<CustomerRow> Rows(IReadOnlyList<(string Name, List<string[]> Rows)> sheets)
    {
        if (sheets.Count == 0) throw new InvalidDataException("ERP 匯出的 Excel 沒有工作表。");
        var sheet = sheets.FirstOrDefault(s => s.Name == Invi02Export.HeaderSheet);
        if (sheet.Rows is null) sheet = sheets[0];
        var titleIndex = sheet.Rows.FindIndex(r => r.Length > 0 && r[0].Trim() == "客戶代號");
        if (titleIndex < 0) throw new InvalidDataException("ERP 匯出的客戶資料找不到「客戶代號」欄位標題。");
        var titles = sheet.Rows[titleIndex].Select(t => t.Trim()).ToArray();
        return sheet.Rows.Skip(titleIndex + 1)
            .Where(r => !r.All(string.IsNullOrWhiteSpace))
            .Select(r => new CustomerRow(titles, r))
            .ToList();
    }

    /// <summary>The one customer asked for; another code or several rows are refused.</summary>
    public static IReadOnlyList<ErpField> Parse(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string code)
    {
        var rows = Rows(sheets);
        if (rows.Count != 1)
            throw new InvalidDataException($"ERP 匯出的客戶資料應為 1 筆，實際 {rows.Count} 筆。");
        var shown = rows[0]["客戶代號"];
        if (!ItemCodeRules.SameCode(shown, code))
            throw new InvalidDataException($"ERP 匯出的客戶代號「{shown.Trim()}」不是查詢的「{code}」。");
        return CustomerFieldCatalog.Select(rows[0], CustomerFieldCatalog.Fields);
    }

    /// <summary>
    /// The name search list. Every customer's 簡稱 or 全名 must contain the keyword, or ERP did
    /// not apply the condition and the list is refused.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ErpField>> ParseList(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, string keyword)
    {
        var list = new List<IReadOnlyList<ErpField>>();
        foreach (var row in Rows(sheets))
        {
            if (!ItemNameRules.NameContains(row["客戶簡稱"], keyword) && !ItemNameRules.NameContains(row["客戶全名"], keyword))
                throw new InvalidDataException($"ERP 列出的客戶「{row["客戶代號"].Trim()}」簡稱與全名都不含「{keyword}」，查詢條件可能沒有套用，結果不採用。");
            list.Add(CustomerFieldCatalog.Select(row, CustomerFieldCatalog.SearchList));
        }
        return list;
    }
}
