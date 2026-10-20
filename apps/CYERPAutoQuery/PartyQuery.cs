namespace CYERPAutoQuery;

/// <summary>
/// A 往來對象 queried the same way (user, 2026-10-17: 供應廠商 in PURI01 works like 客戶資料 in
/// COPI01): one by code from the form's 基本資料 export, or a name list from 資料瀏覽 where 簡稱
/// or 全名 contains the keyword. Pure, unit tested on any OS.
/// </summary>
/// <param name="Kind">客戶 or 廠商; the 簡稱／全名 labels are built from it.</param>
/// <param name="Table">The ERP table the 進階設定 condition names (MA002 簡稱, MA003 全名).</param>
/// <param name="ShortColumn">The export column of the 簡稱.</param>
/// <param name="FullColumn">The export column of the 全名.</param>
internal sealed record ErpParty(
    string Kind,
    ErpForm Form,
    string Table,
    string ShortColumn,
    string FullColumn,
    IReadOnlyList<PartyField> Fields,
    IReadOnlyList<PartyField> SearchList)
{
    public static readonly ErpParty Customer = new("客戶", ErpForm.Copi01, "COPMA", "客戶簡稱", "客戶全名",
        PartyFieldCatalog.Customer, PartyFieldCatalog.CustomerSearch);

    public static readonly ErpParty Supplier = new("廠商", ErpForm.Puri01, "PURMA", "簡稱", "公司全名",
        PartyFieldCatalog.Supplier, PartyFieldCatalog.SupplierSearch);

    /// <summary>客戶代號／廠商代號: the first export column and the code's name in messages.</summary>
    public string CodeName => Form.CodeName;
    public string ShortLabel => $"{Kind}簡稱";
    public string FullLabel => $"{Kind}全名";

    /// <summary>
    /// The 進階設定 condition for a name: 簡稱 OR 全名, always both (user, 2026-10-16: so a name
    /// written in either is found). Written the way ERP writes it (user's screenshot:
    /// 「(COPMA.MA002 = N'佳德')」, next line 「  OR (COPMA.MA003 = N'佳德')」), with like.
    /// The keyword is checked by <see cref="ItemNameRules.TryNormalize"/> (no quote or wildcard).
    /// </summary>
    public string NameCondition(string keyword) =>
        $"({Table}.MA002 like N'%{keyword}%')\r\n  OR ({Table}.MA003 like N'%{keyword}%')";
}

/// <summary>A delivered field: its label and how it is read from one exported row.</summary>
internal sealed record PartyField(string Label, Func<ExportRow, string> Read, params string[] Columns);

/// <summary>One customer or supplier: the <see cref="ErpParty.Fields"/> of its export.</summary>
internal sealed record PartyQueryResult(ErpParty Party, string Code, IReadOnlyList<ErpField> Fields, TimeSpan Elapsed)
{
    public bool Found { get; init; } = true;

    public string Value(string name) => Fields.FirstOrDefault(f => f.Name == name)?.Value ?? string.Empty;

    public static PartyQueryResult NotFound(ErpParty party, string code, TimeSpan elapsed) => new(party, code, [], elapsed) { Found = false };
}

/// <summary>
/// The fields delivered, in display order. Some are built from two export columns: a code with its
/// name (付款條件 R09 + 現金電匯-月結30天) or a 郵遞區號 with its address. COPI01's export repeats
/// 「郵遞區號」 four times, once per address, in this order: 登記、發票、送貨、文件 — so a column is
/// named with its occurrence. PURI01 names its two 郵遞區號(一)／(二) but repeats 「個月逢」 three
/// times: 結帳日期、付款日期、票期 (only 結帳日期 is delivered).
/// </summary>
internal static class PartyFieldCatalog
{
    private static PartyField Plain(string label, string column) => new(label, r => r[column], column);

    private static PartyField CodeWithName(string label, string code, string name) =>
        new(label, r => Join(r[code], r[name]), code, name);

    private static PartyField Address(string label, string zip, int zipOccurrence, string address) =>
        new(label, r => Join(r[zip, zipOccurrence], r[address]), $"{zip}#{zipOccurrence}", address);

    /// <summary>PURI01 交易資料 「結帳日期：驗收後 _ 個月逢 _ 日」 (empty months read as 0).</summary>
    private static PartyField MonthDay(string label, string monthColumn, string after, int dayOccurrence) =>
        new(label, r =>
        {
            var (month, day) = (r[monthColumn].Trim(), r["個月逢", dayOccurrence].Trim());
            return month.Length == 0 && day.Length == 0
                ? string.Empty
                : $"{after} {(month.Length > 0 ? month : "0")} 個月逢 {day} 日";
        }, monthColumn, $"個月逢#{dayOccurrence}");

    private static string Join(string first, string second) =>
        string.Join(" ", new[] { first.Trim(), second.Trim() }.Where(v => v.Length > 0));

    // 客戶 (user, 2026-10-16).
    public static readonly IReadOnlyList<PartyField> Customer =
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
        Address("登記地址", "郵遞區號", 1, "登記地址(一)"),
        Address("送貨地址", "郵遞區號", 3, "送貨地址(一)"),
        CodeWithName("付款條件", "付款條件", "付款條件名稱"),
        Plain("發票聯數", "發票聯數"),
        Plain("課稅別", "課稅別"),
        CodeWithName("通路別", "通路別", "通路簡稱"),
        CodeWithName("型態別", "型態別", "型態簡稱"),
        Plain("客戶描述", "客戶描述"),
    ];

    /// <summary>The columns of the customer name list; a row is picked and queried by 客戶代號.</summary>
    public static readonly IReadOnlyList<PartyField> CustomerSearch =
    [
        Plain("客戶代號", "客戶代號"),
        Plain("客戶簡稱", "客戶簡稱"),
        Plain("客戶全名", "客戶全名"),
        Plain("TEL", "TEL_NO"),
        Plain("統一編號", "統一編號"),
        Plain("業務人員", "業務人員"),
    ];

    // 廠商 (user, 2026-10-17): the whole header (代號、簡稱、全名、版次、統一編號), then the
    // fields the user listed in this order. 「廠商簡稱」 in the export is the 總公司's, so the 簡稱
    // is read from 「簡稱」.
    public static readonly IReadOnlyList<PartyField> Supplier =
    [
        Plain("廠商代號", "廠商代號"),
        Plain("廠商簡稱", "簡稱"),
        Plain("廠商全名", "公司全名"),
        Plain("版次", "版次"),
        Plain("統一編號", "統一編號"),
        Plain("TEL", "TEL"),
        Plain("FAX", "FAXNO"),
        Plain("E-MAIL", "E_MAIL"),
        Plain("負責人", "負責人"),
        Plain("聯絡人", "聯絡人"),
        Plain("行動電話", "行動電話"),
        CodeWithName("採購人員", "採購人員", "人員名稱"),
        Plain("備註", "備註"),
        CodeWithName("廠商分類", "廠商分類", "廠商分類名稱"),
        Plain("付款方式", "付款方式"),
        CodeWithName("付款條件", "付款條件", "付款條件名稱"),
        CodeWithName("匯款銀行", "匯款銀行", "銀行名稱"),
        Plain("匯款帳號", "匯款帳號"),
        Plain("發票聯數", "發票聯數"),
        Plain("課稅別", "課稅別"),
        MonthDay("結帳日期", "結帳日期:驗收後", "驗收後", 1),
        Address("聯絡地址", "郵遞區號(一)", 1, "聯絡地址(一)"),
        Address("帳單地址", "郵遞區號(二)", 1, "帳單地址(一)"),
        Plain("廠商描述", "廠商描述"),
    ];

    /// <summary>
    /// The columns of the supplier name list, all on PURI01's 資料瀏覽 (user's screenshot
    /// 2026-10-17: 廠商代號、簡稱、公司全名、版次、統一編號、TEL、FAXNO、E_MAIL).
    /// </summary>
    public static readonly IReadOnlyList<PartyField> SupplierSearch =
    [
        Plain("廠商代號", "廠商代號"),
        Plain("廠商簡稱", "簡稱"),
        Plain("廠商全名", "公司全名"),
        Plain("TEL", "TEL"),
        Plain("FAX", "FAXNO"),
        Plain("統一編號", "統一編號"),
    ];

    public static IReadOnlyList<ErpField> Select(ErpParty party, ExportRow row, IReadOnlyList<PartyField> catalog)
    {
        var missing = catalog.SelectMany(f => f.Columns).Where(c => !row.Has(c)).Distinct().ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"ERP 匯出的{party.Kind}資料缺少欄位：{string.Join("、", missing)}。");
        return catalog.Select(f => new ErpField(f.Label, f.Read(row))).ToList();
    }
}

/// <summary>One exported row; a title used more than once is reached by its occurrence (1-based).</summary>
internal sealed class ExportRow(string[] titles, string[] values)
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
        var at = column.LastIndexOf('#');
        return at < 0 ? IndexOf(column, 1) >= 0 : IndexOf(column[..at], int.Parse(column[(at + 1)..])) >= 0;
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
/// Reads COPI01's／PURI01's 「轉出至 Excel」／「轉出全部」 workbook (user's samples 2026-10-16／17):
/// sheet 單頭資料, row 1 title, row 3 column titles starting with the code, one row per record.
/// </summary>
internal static class PartyExport
{
    private static List<ExportRow> Rows(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, ErpParty party)
    {
        if (sheets.Count == 0) throw new InvalidDataException("ERP 匯出的 Excel 沒有工作表。");
        var sheet = sheets.FirstOrDefault(s => s.Name == Invi02Export.HeaderSheet);
        if (sheet.Rows is null) sheet = sheets[0];
        var titleIndex = sheet.Rows.FindIndex(r => r.Length > 0 && r[0].Trim() == party.CodeName);
        if (titleIndex < 0) throw new InvalidDataException($"ERP 匯出的{party.Kind}資料找不到「{party.CodeName}」欄位標題。");
        var titles = sheet.Rows[titleIndex].Select(t => t.Trim()).ToArray();
        return sheet.Rows.Skip(titleIndex + 1)
            .Where(r => !r.All(string.IsNullOrWhiteSpace))
            .Select(r => new ExportRow(titles, r))
            .ToList();
    }

    /// <summary>The one record asked for; another code or several rows are refused.</summary>
    public static IReadOnlyList<ErpField> Parse(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, ErpParty party, string code)
    {
        var rows = Rows(sheets, party);
        if (rows.Count != 1)
            throw new InvalidDataException($"ERP 匯出的{party.Kind}資料應為 1 筆，實際 {rows.Count} 筆。");
        var shown = rows[0][party.CodeName];
        if (!ItemCodeRules.SameCode(shown, code))
            throw new InvalidDataException($"ERP 匯出的{party.CodeName}「{shown.Trim()}」不是查詢的「{code}」。");
        return PartyFieldCatalog.Select(party, rows[0], party.Fields);
    }

    /// <summary>
    /// The name search list. Every record's 簡稱 or 全名 must contain the keyword, or ERP did not
    /// apply the condition and the list is refused.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ErpField>> ParseList(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, ErpParty party, string keyword)
    {
        var list = new List<IReadOnlyList<ErpField>>();
        foreach (var row in Rows(sheets, party))
        {
            if (!ItemNameRules.NameContains(row[party.ShortColumn], keyword) && !ItemNameRules.NameContains(row[party.FullColumn], keyword))
                throw new InvalidDataException($"ERP 列出的{party.Kind}「{row[party.CodeName].Trim()}」簡稱與全名都不含「{keyword}」，查詢條件可能沒有套用，結果不採用。");
            list.Add(PartyFieldCatalog.Select(party, row, party.SearchList));
        }
        return list;
    }
}
