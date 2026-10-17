using System.Globalization;

namespace CYERPAutoQuery;

/// <summary>
/// 未收款查詢 (user, 2026-10-17): SMART ERP 未結案應收明細表 (ACRR18), driven like COPR15. One
/// customer (at the customer's: how much to collect), one 業務員 (what that salesperson has to
/// collect), or everything (選擇客戶代號 left empty: ERP lists all). Pure rules.
/// </summary>
internal static class ReceivableLayout
{
    public const string BasicTab = "基本選項";
    public const string CustomerGroup = SalesReportLayout.CustomerGroup;
    public const string SalespersonGroup = "選擇業務員代號";
    public const string SalespersonName = "業務員代號";

    // Tab centers px right of the page control, from the user's screenshots (2026-10-17).
    public static readonly ErpReport Report = new("ACRR18", "未結案應收明細表",
        [BasicTab, "進階選項", "系統選項"],
        new TabStrip([BasicTab, "進階選項", "系統選項"], 0, 0, Centers: [43, 111, 176]));
}

internal enum ReceivableScope { Customer, Salesperson, All }

/// <summary>Whose 未收款: one customer, one 業務員 or all; a code is letters and digits only.</summary>
internal sealed record ReceivableQuery(ReceivableScope Scope, string Code)
{
    public static ReceivableQuery All { get; } = new(ReceivableScope.All, string.Empty);

    public static ReceivableQuery Resolve(ReceivableScope scope, string input)
    {
        if (scope == ReceivableScope.All) return All;
        var name = scope == ReceivableScope.Customer ? ErpParty.Customer.CodeName : ReceivableLayout.SalespersonName;
        if (!ItemCodeRules.TryNormalize(input, out var code, out var error, name))
            throw new ArgumentException(error);
        return new ReceivableQuery(scope, code);
    }

    public string Describe() => Scope switch
    {
        ReceivableScope.Customer => $"客戶 {Code}",
        ReceivableScope.Salesperson => $"業務員 {Code}",
        _ => "全部客戶",
    };
}

/// <summary>One 憑證 (銷貨單 or 銷退單, 「232A-…」/「241A-…」) a 結帳單 collects.</summary>
internal sealed record ReceivableVoucher(string VoucherNo, DateOnly? Date, decimal Amount, decimal Tax);

/// <summary>One 結帳單 with its 未收帳款 and the 憑證 it collects, in ERP's order.</summary>
internal sealed record ReceivableBill(
    string CustomerCode, string CustomerName, DateOnly Date, string BillNo,
    string SalespersonCode, string SalespersonName, string InvoiceNo, string Currency,
    decimal Receivable, decimal ReceivableTax, decimal Unpaid, IReadOnlyList<ReceivableVoucher> Vouchers);

internal sealed record ReceivableResult(ReceivableQuery Query, IReadOnlyList<ReceivableBill> Bills, TimeSpan Elapsed)
{
    public bool Found => Bills.Count > 0;
    public decimal TotalUnpaid => Bills.Sum(b => b.Unpaid);
    public int CustomerCount => Bills.Select(b => b.CustomerCode).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>
/// Reads ACRR18's export (user's samples 2026-10-17): one sheet, row 1 the titles, then one row
/// per 憑證 repeating its 結帳單 (whose 應收／未收 amounts repeat on every row), a 「小計:」 row after
/// each customer and a last 「合計」 row. The 結帳單 未收 amounts must add up to 合計.
/// </summary>
internal static class ReceivableExport
{
    private static readonly string[] Required =
    [
        "客戶代號", "客戶名稱", "日期", "結帳單號", "業務員代號", "業務員名稱", "發票號碼", "幣別",
        "原幣應收帳款", "原幣應收稅額", "原幣未收帳款", "憑證號碼", "憑證日期", "原幣未稅金額", "原幣稅額",
    ];

    public static IReadOnlyList<ReceivableBill> Parse(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, ReceivableQuery query)
    {
        const string what = "未結案應收明細表";
        if (sheets.Count == 0) throw new InvalidDataException($"ERP 匯出的{what}沒有工作表。");
        var rows = sheets[0].Rows;
        var titleIndex = rows.FindIndex(r => r.Select(Normalize).Contains("結帳單號"));
        if (titleIndex < 0)
        {
            if (rows.All(r => r.All(string.IsNullOrWhiteSpace) || IsTotal(r) || IsSubtotal(r))) return [];
            throw new InvalidDataException($"ERP 匯出的{what}找不到「結帳單號」欄位標題。");
        }
        var titles = rows[titleIndex].Select(Normalize).ToArray();
        var missing = Required.Where(t => !titles.Contains(t)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException($"ERP 匯出的{what}缺少欄位：{string.Join("、", missing)}。");

        var bills = new List<(string No, List<ExportRow> Rows)>();
        decimal? total = null;
        foreach (var values in rows.Skip(titleIndex + 1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var r = new ExportRow(titles, values);
            if (r["客戶代號"].Trim().Length == 0)
            {
                if (IsSubtotal(values)) continue;
                if (IsTotal(values)) { total = Number(r["原幣未收帳款"]); continue; }
                throw new InvalidDataException($"ERP 匯出的{what}有一列沒有客戶代號。");
            }
            var no = r["結帳單號"].Trim();
            if (no.Length == 0) throw new InvalidDataException($"ERP 匯出的{what}有一列沒有結帳單號。");
            Check(query, r, no);
            var group = bills.FirstOrDefault(b => b.No == no);
            if (group.Rows is null) bills.Add(group = (no, []));
            group.Rows.Add(r);
        }

        var result = bills.Select(b => ToBill(b.No, b.Rows)).ToList();
        if (total is { } expected && result.Sum(b => b.Unpaid) != expected)
            throw new InvalidDataException($"ERP 匯出的{what}各結帳單未收合計 {result.Sum(b => b.Unpaid):0.##} 與報表合計 {expected:0.##} 不符，結果不採用。");
        return result;
    }

    /// <summary>A row of another customer or 業務員 means ERP did not apply the condition.</summary>
    private static void Check(ReceivableQuery query, ExportRow r, string no)
    {
        var (column, name) = query.Scope switch
        {
            ReceivableScope.Customer => ("客戶代號", "客戶"),
            ReceivableScope.Salesperson => ("業務員代號", "業務員"),
            _ => (null, null),
        };
        if (column is null) return;
        var shown = r[column];
        if (!ItemCodeRules.SameCode(shown, query.Code))
            throw new InvalidDataException($"ERP 匯出的結帳單「{no}」是{name}「{shown.Trim()}」，不是查詢的「{query.Code}」，查詢條件可能沒有套用，結果不採用。");
    }

    private static ReceivableBill ToBill(string no, List<ExportRow> rows)
    {
        var h = rows[0];
        var unpaid = Number(h["原幣未收帳款"]);
        if (rows.Any(r => Number(r["原幣未收帳款"]) != unpaid || r["客戶代號"].Trim() != h["客戶代號"].Trim()))
            throw new InvalidDataException($"ERP 匯出的結帳單「{no}」各列的客戶或未收帳款不一致，結果不採用。");
        var vouchers = rows.Select(r => new ReceivableVoucher(r["憑證號碼"].Trim(), TryDate(r["憑證日期"]),
            Number(r["原幣未稅金額"]), Number(r["原幣稅額"]))).ToList();
        return new ReceivableBill(
            h["客戶代號"].Trim(), h["客戶名稱"].Trim(),
            TryDate(h["日期"]) ?? throw new InvalidDataException($"ERP 匯出的結帳單「{no}」日期「{h["日期"].Trim()}」無法辨識。"),
            no, h["業務員代號"].Trim(), h["業務員名稱"].Trim(), h["發票號碼"].Trim(), h["幣別"].Trim(),
            Number(h["原幣應收帳款"]), Number(h["原幣應收稅額"]), unpaid, vouchers);
    }

    private static string Normalize(string title) => new(title.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static bool IsSubtotal(string[] values) => values.Any(v => v.Trim().StartsWith("小計", StringComparison.Ordinal));

    private static bool IsTotal(string[] values) => values.Any(v => v.Trim() == "合計");

    private static DateOnly? TryDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), ["yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal Number(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
