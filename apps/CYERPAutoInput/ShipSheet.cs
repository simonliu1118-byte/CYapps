using System.Globalization;

namespace CYERPAutoInput;

/// <summary>One product line of the 出貨明細.</summary>
internal sealed record ShipSheetLine(string Code, string Name, string Quantity, string Unit, string Price, string Amount)
{
    /// <summary>贈／備品量: goes out too (the 撿貨單 counts it), not on the 出貨明細's 數量.</summary>
    public string GiftQuantity { get; init; } = string.Empty;
}

/// <summary>
/// 全熱感紙列印 (user, 2026-10-26: 二聯報表紙 is costly; every order gets a thermal 出貨明細 instead
/// of ERP's 直接列印, one per 寄件單): the saved 銷貨單 as ERP exported it (品號／品名／數量／單位／
/// 單價／金額) with the platform's 訂單編號, 寄件編號 and 貨運, the platform's 註記 and the time it
/// was printed. Pure: no Windows drawing, unit tested.
/// </summary>
internal sealed record ShipSheet(
    string Document,
    string Platform,
    string OrderSn,
    string Tracking,
    string Carrier,
    IReadOnlyList<ShipSheetLine> Lines,
    string Note,
    DateTime PrintedAt)
{
    /// <summary>The 金額 added up (lines without one count their 數量 × 單價).</summary>
    public decimal Total => Lines.Sum(l => Number(l.Amount) ?? (Number(l.Quantity) ?? 0) * (Number(l.Price) ?? 0));

    /// <summary>A number split for printing: everything but the last three characters, and those three (printed large).</summary>
    public static (string Head, string Tail) SplitTail(string number)
    {
        var text = number.Trim();
        return text.Length <= 3 ? (string.Empty, text) : (text[..^3], text[^3..]);
    }

    public static decimal? Number(string text) =>
        decimal.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A number as printed: no trailing zeros, thousands apart.</summary>
    public static string Show(string text) => Number(text) is { } n ? Show(n) : text.Trim();

    public static string Show(decimal n) => n.ToString(n == decimal.Truncate(n) ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// ERP's COPI08 「轉出至 Excel」 workbook: like INVI02's and COPI01's (CYERPAutoQuery), sheets
/// 單頭資料／單身資料, a title row, then a row of column titles. The detail lines are found by
/// their titles (品號 and 數量 are required; 品名／單位／單價／金額 when present), wherever the
/// title row is. Pure.
/// </summary>
internal static class ShipSheetExport
{
    private static readonly string[] CodeTitles = ["品號"];
    private static readonly string[] NameTitles = ["品名"];
    private static readonly string[] QuantityTitles = ["數量", "銷貨數量"];
    private static readonly string[] UnitTitles = ["單位"];
    private static readonly string[] PriceTitles = ["單價"];
    private static readonly string[] AmountTitles = ["金額"];
    private static readonly string[] GiftTitles = ["贈/備品量", "贈／備品量", "贈品量", "備品量"];

    public static List<ShipSheetLine> Lines(IReadOnlyList<(string Name, List<string[]> Rows)> sheets)
    {
        var ordered = sheets.OrderByDescending(s => s.Name.Contains("單身", StringComparison.Ordinal)).ToList();
        foreach (var (_, rows) in ordered)
        {
            var titleIndex = rows.FindIndex(r => Column(r, CodeTitles) >= 0 && Column(r, QuantityTitles) >= 0);
            if (titleIndex < 0) continue;
            var titles = rows[titleIndex];
            var code = Column(titles, CodeTitles);
            var name = Column(titles, NameTitles);
            var quantity = Column(titles, QuantityTitles);
            var unit = Column(titles, UnitTitles);
            var price = Column(titles, PriceTitles);
            var amount = Column(titles, AmountTitles);
            var gift = Column(titles, GiftTitles);
            string Cell(string[] row, int i) => i >= 0 && i < row.Length ? row[i].Trim() : string.Empty;
            return rows.Skip(titleIndex + 1)
                .Where(r => Cell(r, code).Length > 0)
                .Select(r => new ShipSheetLine(Cell(r, code), Cell(r, name), Cell(r, quantity), Cell(r, unit), Cell(r, price), Cell(r, amount)) { GiftQuantity = Cell(r, gift) })
                .ToList();
        }
        throw new InvalidDataException("ERP 匯出的 Excel 找不到有「品號」「數量」欄位的單身資料。");
    }

    /// <summary>The 單頭資料 of one document: column title → value (the first record after the title row with 單號).</summary>
    public static Dictionary<string, string> Header(IReadOnlyList<(string Name, List<string[]> Rows)> sheets)
    {
        foreach (var (_, rows) in sheets.OrderByDescending(s => s.Name.Contains("單頭", StringComparison.Ordinal)))
        {
            var titleIndex = rows.FindIndex(r => Column(r, ["單號"]) >= 0);
            if (titleIndex < 0) continue;
            var titles = rows[titleIndex];
            var record = rows.Skip(titleIndex + 1).FirstOrDefault(r => !r.All(string.IsNullOrWhiteSpace));
            if (record is null) continue;
            var header = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < titles.Length; i++)
                if (titles[i].Trim().Length > 0) header.TryAdd(titles[i].Trim(), i < record.Length ? record[i].Trim() : string.Empty);
            return header;
        }
        throw new InvalidDataException("ERP 匯出的 Excel 找不到有「單號」欄位的單頭資料。");
    }

    /// <summary>
    /// 出貨明細 of a 銷貨單 opened in ERP by hand (user, 2026-10-26: orders entered by a person get
    /// one too): everything from the export — 單別-單號; the platform and its 訂單編號 from 備註
    /// (each platform's 備註前綴 + 訂單編號, as the program enters them); 寄件編號 from 送貨地址(一);
    /// 貨運 from 貨運別 (named back through 設定 › 貨運別 when the export has only the code).
    /// </summary>
    public static ShipSheet FromDocument(IReadOnlyList<(string Name, List<string[]> Rows)> sheets,
        IReadOnlyList<(string Platform, string Prefix)> prefixes, IReadOnlyDictionary<string, string> freightTypes,
        Func<string, string> noteFor, DateTime printedAt)
    {
        var header = Header(sheets);
        string Value(params string[] titles) => titles.Select(t => header.GetValueOrDefault(t, string.Empty)).FirstOrDefault(v => v.Length > 0) ?? string.Empty;
        var type = Value("單別");
        var number = Value("單號");
        var document = type.Length > 0 && number.Length > 0 ? $"{type}-{number}" : number;
        var remark = Value("備註");
        var platform = string.Empty;
        var orderSn = string.Empty;
        foreach (var (name, prefix) in prefixes.Where(p => p.Prefix.Trim().Length > 0).OrderByDescending(p => p.Prefix.Trim().Length))
            if (remark.StartsWith(prefix.Trim(), StringComparison.Ordinal))
            {
                platform = name;
                orderSn = remark[prefix.Trim().Length..].Trim();
                break;
            }
        var freight = Value("貨運別名稱", "貨運方式", "貨運別");
        // 設定 › 貨運別 maps the platform's carrier to ERP's code; a code is named back by it.
        var carrier = freightTypes.FirstOrDefault(p => string.Equals(p.Value.Trim(), freight, StringComparison.OrdinalIgnoreCase)).Key ?? freight;
        return new ShipSheet(document, platform, orderSn, Value("送貨地址(一)", "送貨地址一", "送貨地址"), carrier,
            Lines(sheets), noteFor(platform), printedAt);
    }

    /// <summary>The column titled exactly one of <paramref name="titles"/>, else -1.</summary>
    private static int Column(string[] row, string[] titles)
    {
        for (var i = 0; i < row.Length; i++)
            if (titles.Contains(row[i].Trim(), StringComparer.Ordinal)) return i;
        return -1;
    }

    /// <summary>The column titles of each sheet, for the LOG when the workbook is not as expected.</summary>
    public static string Describe(IReadOnlyList<(string Name, List<string[]> Rows)> sheets) =>
        string.Join(" / ", sheets.Select(s => $"{s.Name}: " + string.Join(" | ", s.Rows.Take(4).Select(r => string.Join(",", r.Take(60))))));
}
