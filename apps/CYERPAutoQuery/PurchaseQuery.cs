using System.Globalization;

namespace CYERPAutoQuery;

/// <summary>
/// 進貨單明細表 PURR18 and 退貨單明細表 PURR22 (user, 2026-10-18: a supplier's 進貨單 the way a customer's 銷貨單 are read
/// from COPR15): 作業速查 → 基本選項 選擇廠商代號 起＝迄＝code, 選擇單據日期 like COPR15 → 進階選項
/// 確認狀態／結帳狀態 「全部」 → 即時報表 → XLSX → C:\My Documents → TEMP. Pure rules.
/// </summary>
internal static class PurchaseReportLayout
{
    public static readonly string[] Tabs = [SalesReportLayout.BasicTab, SalesReportLayout.AdvancedTab, "系統選項"];

    // Tab centers px right of the page control, from the user's screenshots (2026-10-18); OCR is the fallback.
    public static readonly ErpReport Report = new("PURR18", "進貨單明細表", Tabs, new TabStrip(Tabs, 0, 0, Centers: [47, 108, 171]));

    /// <summary>基本選項 groups (user's screenshot): 廠商代號 has 區間選擇 (ticked there); the others must not narrow the report.</summary>
    public const string SupplierGroup = "選擇廠商代號";
    public const string OrderGroup = "選擇進貨單號";
    public const string WarehouseGroup = "選擇進貨庫別";
    /// <summary>選擇日期: 單據日 (ERP's default) or 驗收日; the dates are 單據日期.</summary>
    public const string AcceptDateRadio = "驗收日";
    /// <summary>進階選項 check boxes that would hide 進貨單 (「僅列印暫不付款者」, 「僅列印無採購單者」).</summary>
    public const string OnlyPrintPrefix = "僅列印";

    /// <summary>
    /// 退貨單明細表 PURR22 (user, 2026-10-18: 有進貨就有退貨). Its 基本選項 has no 廠商 (only 退貨單號,
    /// 單據日期 and 幣別), so the whole date range is exported and the supplier's 退貨單 are kept here.
    /// </summary>
    public static readonly ErpReport Returns = new("PURR22", "退貨單明細表", Tabs, new TabStrip(Tabs, 0, 0, Centers: [47, 108, 171]));
    public const string ReturnOrderGroup = "選擇退貨單號";
}

/// <summary>A supplier's 進貨單 in a 單據日期 range; both dates optional (ERP then leaves that end open).</summary>
internal sealed record PurchaseQuery(string SupplierCode, DateOnly? From, DateOnly? To)
{
    public static PurchaseQuery Resolve(string supplierInput, DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (!ItemCodeRules.TryNormalize(supplierInput, out var code, out var error, ErpParty.Supplier.CodeName))
            throw new ArgumentException(error);
        if (from is { } start && start > today) throw new ArgumentException("單據日期的起日不可晚於今天。");
        if (from is { } a && to is { } b && a > b) throw new ArgumentException("單據日期的起日不可晚於迄日。");
        return new PurchaseQuery(code, from, to);
    }

    public HistoryRange Dates => new(From, To);
}

/// <summary>
/// One line of a 進貨單 or 退貨單. <see cref="SourceOrder"/> is a 退貨 line's 原進貨單 「單號-序號」 as ERP
/// gives it, or empty; linking it to its 進貨單 is left to CY Web (as 銷退 ↔ 銷貨).
/// </summary>
internal sealed record PurchaseLine(
    int Seq, string ItemCode, string ItemName, string Specification, DateOnly? AcceptDate, string Warehouse,
    decimal Quantity, decimal AcceptedQuantity, decimal ReturnedQuantity, string Unit, decimal UnitPrice,
    decimal Amount, decimal Tax, string Batch, string PurchaseOrderNo, string Note, string SourceOrder = "");

/// <summary>One 進貨單 or 退貨單 (<see cref="Kind"/>) with its lines in ERP's order. Amounts are 原幣.</summary>
internal sealed record PurchaseOrder(
    string Kind, string OrderNo, DateOnly Date, string SupplierCode, string SupplierName, string SupplierOrderNo,
    string InvoiceNo, string InvoiceType, string TaxType, string Currency,
    decimal TotalQuantity, decimal Amount, decimal Deduction, decimal Tax, decimal Total, string Settled,
    IReadOnlyList<PurchaseLine> Lines);

/// <summary>A supplier's 進貨單 and 退貨單 in the range, newest first.</summary>
internal sealed record PurchaseQueryResult(PurchaseQuery Query, IReadOnlyList<PurchaseOrder> Orders, TimeSpan Elapsed)
{
    public const string PurchaseKind = "進貨", ReturnKind = "退貨";
    public bool Found => Orders.Count > 0;
    public int Count(string kind) => Orders.Count(o => o.Kind == kind);
}

/// <summary>
/// Reads a PURR18 export (user's sample 2026-10-18): one sheet, row 1 the column titles (with
/// spaces: 「品    號」), then one row per line repeating its order's 單頭, and a last 「小計:N張」
/// row. 原幣進貨金額、原幣扣款金額、原幣稅額、本幣稅額 are used twice: first the order's, then the line's.
/// </summary>
internal static class PurchaseExport
{
    private static string Normalize(string title) => new(title.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static readonly string[] Required =
    [
        "進貨單號", "單據日期", "發票號碼", "廠商代號", "廠商簡稱", "廠商單號", "發票聯數", "課稅別", "幣別",
        "原幣進貨金額", "原幣扣款金額", "原幣稅額", "原幣金額合計", "數量合計",
        "品號", "品名", "規格", "驗收日期", "庫別", "進貨數量", "驗收數量", "驗退數量", "單位", "原幣單位進價",
        "採購單號", "備注", "結帳", "批號",
    ];

    /// <summary>
    /// The orders of <paramref name="query"/>, newest first. A row of another supplier or outside
    /// the dates means ERP did not apply the conditions, and the whole export is refused; so is a
    /// 小計 that does not match the orders read. A missing 原幣單位進價 means 列印單價金額 was not ticked.
    /// </summary>
    public static IReadOnlyList<PurchaseOrder> Parse(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, PurchaseQuery query)
    {
        const string what = "進貨單明細表";
        if (sheets.Count == 0) throw new InvalidDataException($"ERP 匯出的{what}沒有工作表。");
        var rows = sheets[0].Rows;
        var titleIndex = rows.FindIndex(r => r.Select(Normalize).Contains("進貨單號"));
        if (titleIndex < 0)
        {
            if (rows.All(r => r.All(string.IsNullOrWhiteSpace) || IsSubtotal(r))) return [];
            throw new InvalidDataException($"ERP 匯出的{what}找不到「進貨單號」欄位標題。");
        }
        var titles = rows[titleIndex].Select(Normalize).ToArray();
        var missing = Required.Where(t => !titles.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            var hint = missing.Contains("原幣單位進價") ? "（請確認進階選項的「列印單價金額」有勾）" : string.Empty;
            throw new InvalidDataException($"ERP 匯出的{what}缺少欄位：{string.Join("、", missing)}{hint}。");
        }

        var orders = new List<(string No, List<ExportRow> Rows)>();
        var subtotals = new List<ExportRow>();
        foreach (var values in rows.Skip(titleIndex + 1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var r = new ExportRow(titles, values);
            var no = r["進貨單號"].Trim();
            if (IsSubtotal(values))
            {
                subtotals.Add(r);
                continue;
            }
            if (no.Length == 0) throw new InvalidDataException($"ERP 匯出的{what}有一列沒有進貨單號。");
            var code = r["廠商代號"];
            if (!ItemCodeRules.SameCode(code, query.SupplierCode))
                throw new InvalidDataException($"ERP 匯出的進貨單「{no}」是廠商「{code.Trim()}」，不是查詢的「{query.SupplierCode}」，查詢條件可能沒有套用，結果不採用。");
            var date = ParseDate(r["單據日期"], no);
            if ((query.From is { } from && date < from) || (query.To is { } to && date > to))
                throw new InvalidDataException($"ERP 匯出的進貨單「{no}」日期 {date:yyyy/MM/dd} 不在查詢範圍內，查詢條件可能沒有套用，結果不採用。");
            var group = orders.FirstOrDefault(o => o.No == no);
            if (group.Rows is null) orders.Add(group = (no, []));
            group.Rows.Add(r);
        }
        var result = orders.Select(o => ToOrder(o.No, o.Rows)).OrderBy(o => o, Newest).ToList();
        if (subtotals.Count > 0)
        {
            // 「小計:N張」 sums each order once: it must match what was read.
            var count = subtotals.Sum(s => int.TryParse(new string(s["進貨單號"].Where(char.IsAsciiDigit).ToArray()), out var n) ? n : -1);
            var total = subtotals.Sum(s => Number(s["原幣金額合計"]));
            if (count != result.Count || total != result.Sum(o => o.Total))
                throw new InvalidDataException($"ERP 匯出的{what}小計（{count} 張、{total}）與讀到的進貨單（{result.Count} 張、{result.Sum(o => o.Total)}）不一致，結果不採用。");
        }
        return result;
    }

    private static readonly string[] ReturnRequired =
    [
        "單據日期", "退貨單號", "廠商代號", "簡稱", "幣別", "聯數", "課稅別", "原幣退貨金額", "原幣退貨稅額", "數量合計", "發票號碼",
        "品號", "品名", "規格", "庫別代號", "庫別名稱", "數量", "單位", "單價", "原幣未稅金額", "原幣稅額", "備註",
        "原進貨單號", "原採購單號", "結帳",
    ];

    /// <summary>
    /// The supplier's 退貨單 from a PURR22 export (user's sample 2026-10-18: every supplier, titles in
    /// row 1, a last 「小計:N張」 row). The report cannot be narrowed to one supplier, so other
    /// suppliers' rows are skipped; but every row must be inside the dates and the 小計 must match
    /// all the 退貨單 read, or ERP did not apply the conditions and the export is refused.
    /// </summary>
    public static IReadOnlyList<PurchaseOrder> ParseReturns(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, PurchaseQuery query)
    {
        const string what = "退貨單明細表";
        if (sheets.Count == 0) throw new InvalidDataException($"ERP 匯出的{what}沒有工作表。");
        var rows = sheets[0].Rows;
        var titleIndex = rows.FindIndex(r => r.Select(Normalize).Contains("退貨單號"));
        if (titleIndex < 0)
        {
            if (rows.All(r => r.All(string.IsNullOrWhiteSpace) || IsSubtotal(r))) return [];
            throw new InvalidDataException($"ERP 匯出的{what}找不到「退貨單號」欄位標題。");
        }
        var titles = rows[titleIndex].Select(Normalize).ToArray();
        var missing = ReturnRequired.Where(t => !titles.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            var hint = missing.Contains("單價") ? "（請確認進階選項的「列印單價金額」有勾）" : string.Empty;
            throw new InvalidDataException($"ERP 匯出的{what}缺少欄位：{string.Join("、", missing)}{hint}。");
        }

        var all = new List<(string No, List<ExportRow> Rows)>();
        var subtotals = new List<ExportRow>();
        foreach (var values in rows.Skip(titleIndex + 1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var r = new ExportRow(titles, values);
            if (IsSubtotal(values))
            {
                subtotals.Add(r);
                continue;
            }
            var no = r["退貨單號"].Trim();
            if (no.Length == 0) throw new InvalidDataException($"ERP 匯出的{what}有一列沒有退貨單號。");
            var date = ParseDate(r["單據日期"], no);
            if ((query.From is { } from && date < from) || (query.To is { } to && date > to))
                throw new InvalidDataException($"ERP 匯出的退貨單「{no}」日期 {date:yyyy/MM/dd} 不在查詢範圍內，查詢條件可能沒有套用，結果不採用。");
            var group = all.FirstOrDefault(o => o.No == no);
            if (group.Rows is null) all.Add(group = (no, []));
            group.Rows.Add(r);
        }
        if (subtotals.Count > 0)
        {
            var count = subtotals.Sum(s => int.TryParse(new string(s["退貨單號"].Where(char.IsAsciiDigit).ToArray()), out var n) ? n : -1);
            var amount = subtotals.Sum(s => Number(s["原幣退貨金額"]));
            var read = all.Sum(o => Number(o.Rows[0]["原幣退貨金額"]));
            if (count != all.Count || amount != read)
                throw new InvalidDataException($"ERP 匯出的{what}小計（{count} 張、{amount}）與讀到的退貨單（{all.Count} 張、{read}）不一致，結果不採用。");
        }
        return all.Where(o => ItemCodeRules.SameCode(o.Rows[0]["廠商代號"], query.SupplierCode))
            .Select(o => ToReturn(o.No, o.Rows)).OrderBy(o => o, Newest).ToList();
    }

    private static PurchaseOrder ToReturn(string no, List<ExportRow> rows)
    {
        var h = rows[0];
        var lines = rows.Select((r, i) => new PurchaseLine(
            i + 1, r["品號"].Trim(), r["品名"].Trim(), r["規格"].Trim(), null, JoinCode(r["庫別代號"], r["庫別名稱"]),
            Number(r["數量"]), 0m, 0m, r["單位"].Trim(), Number(r["單價"]), Number(r["原幣未稅金額"]), Number(r["原幣稅額"]),
            string.Empty, Text(r["原採購單號"]), r["備註"].Trim(), Text(r["原進貨單號"]))).ToList();
        var amount = Number(h["原幣退貨金額"]);
        var tax = Number(h["原幣退貨稅額"]);
        return new PurchaseOrder(
            PurchaseQueryResult.ReturnKind, no, ParseDate(h["單據日期"], no), h["廠商代號"].Trim(), h["簡稱"].Trim(), string.Empty,
            Text(h["發票號碼"]), h["聯數"].Trim(), h["課稅別"].Trim(), h["幣別"].Trim(),
            Number(h["數量合計"]), amount, 0m, tax, amount + tax, h["結帳"].Trim(), lines);
    }

    private static string JoinCode(string code, string name) =>
        string.Join(" ", new[] { code.Trim(), name.Trim() }.Where(v => v.Length > 0));

    /// <summary>Newest first; on one day the higher number first.</summary>
    public static readonly IComparer<PurchaseOrder> Newest = Comparer<PurchaseOrder>.Create((a, b) =>
        b.Date.CompareTo(a.Date) is var byDate && byDate != 0 ? byDate : string.CompareOrdinal(b.OrderNo, a.OrderNo));

    private static bool IsSubtotal(string[] values) =>
        values.Any(v => v.Trim().StartsWith("小計", StringComparison.Ordinal));

    private static PurchaseOrder ToOrder(string no, List<ExportRow> rows)
    {
        var h = rows[0];
        var lines = rows.Select((r, i) => new PurchaseLine(
            i + 1, r["品號"].Trim(), r["品名"].Trim(), r["規格"].Trim(), OptionalDate(r["驗收日期"]), r["庫別"].Trim(),
            Number(r["進貨數量"]), Number(r["驗收數量"]), Number(r["驗退數量"]), r["單位"].Trim(), Number(r["原幣單位進價"]),
            Number(r["原幣進貨金額", 2]), Number(r["原幣稅額", 2]), r["批號"].Trim(), Text(r["採購單號"]), r["備注"].Trim())).ToList();
        return new PurchaseOrder(
            PurchaseQueryResult.PurchaseKind, no, ParseDate(h["單據日期"], no), h["廠商代號"].Trim(), h["廠商簡稱"].Trim(), Text(h["廠商單號"]),
            Text(h["發票號碼"]), h["發票聯數"].Trim(), h["課稅別"].Trim(), h["幣別"].Trim(),
            Number(h["數量合計"]), Number(h["原幣進貨金額", 1]), Number(h["原幣扣款金額", 1]), Number(h["原幣稅額", 1]),
            Number(h["原幣金額合計"]), h["結帳"].Trim(), lines);
    }

    /// <summary>ERP writes 「-」 or 「--」 for an empty number.</summary>
    private static string Text(string value) => value.Trim() is var t && t.Trim('-').Length == 0 ? string.Empty : t;

    private static DateOnly ParseDate(string text, string no) =>
        OptionalDate(text) ?? throw new InvalidDataException($"ERP 匯出的「{no}」單據日期「{text.Trim()}」無法辨識。");

    private static DateOnly? OptionalDate(string text) =>
        DateOnly.TryParseExact(text.Trim(), ["yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static decimal Number(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
