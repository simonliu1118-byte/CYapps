using System.Globalization;

namespace CYERPAutoQuery;

/// <summary>
/// The condition window of SMART ERP 銷貨單明細表 (COPR15) and 銷退單明細表 (COPR16), opened through
/// 作業速查 (user, 2026-10-16/17: both the same). One run lists every line of a customer in a date
/// range, so a whole range costs one export instead of one per order. Pure rules.
/// </summary>
internal static class SalesReportLayout
{
    public const string BasicTab = "基本選項";
    public const string AdvancedTab = "進階選項";
    public static readonly string[] Tabs = [BasicTab, "交易分類選單", AdvancedTab, "系統選項"];

    // Tab centers px right of the page control, from the user's two screenshots (2026-10-16);
    // OCR is the fallback.
    public static readonly TabStrip TabStrip = new(Tabs, 0, 0, Centers: [44, 122, 199, 262]);

    /// <summary>基本選項 group boxes: 客戶代號 起＝迄＝the customer; 單據日期 typed like INVQ01's dates.</summary>
    public const string CustomerGroup = "選擇客戶代號";
    public const string DateGroup = "選擇單據日期";

    /// <summary>
    /// 進階選項 (user, 2026-10-16): 確認狀態 and 結帳狀態 both 「全部」 (ERP defaults to 已確認／未結帳,
    /// which would hide orders), and 列印單價金額 checked, or the export has no prices.
    /// </summary>
    public const string AllStatus = "全部";
    public const string PrintPriceCheck = "列印單價金額";

    public const string RunButton = "即時報表";
    public const string SaveDialogTitle = "存成EXCEL檔";
    /// <summary>「顯示數值欄位無千分符號，可進行加總」: plain numbers are read safely.</summary>
    public const string PlainNumbers = "顯示數值欄位無千分符號";
    public const string SaveConfirm = "確認";

    /// <summary>
    /// ERP writes the workbook into this fixed folder and fails when it does not exist (user,
    /// 2026-10-16: 「Cannot create file "C:\My Documents\COPR15….xlsx"」), so it is created first.
    /// </summary>
    public const string ErpExportFolder = @"C:\My Documents";

    /// <summary>ERP's 「匯出失敗Cannot create file "…"」: the folder is missing.</summary>
    public static bool IsMissingFolder(string message) =>
        message.Contains("Cannot create file", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("找不到指定的路徑", StringComparison.Ordinal);

    public static bool IsExportFailed(string message) =>
        message.Contains("匯出失敗", StringComparison.Ordinal) || IsMissingFolder(message);

    /// <summary>
    /// ERP's notice after 即時報表 that nothing matched (user's LOG 2026-10-18: COPR16 for a customer
    /// without 銷退單 shows a small box with only OK instead of a report). Only these words count;
    /// any other message still stops the query.
    /// </summary>
    public static bool IsNoData(string message)
    {
        var text = new string(message.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return new[] { "無資料", "查無", "沒有資料", "無符合", "沒有符合", "無可列印", "沒有可列印", "Nodata", "NoData", "nodata" }
            .Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a box shown after 即時報表 is ERP's 「nothing matched」 notice rather than an error.
    /// V0.9.0 Build 6 LOG (COPR16 for a customer without 銷退單): caption 「銷退單明細表[公司]」,
    /// body 「報表沒有資料」, one OK button. The words may be in the body (read apart from the
    /// caption) or in the whole box's text; a body with nothing in it on a box captioned with the
    /// report and a single button counts too. Any other text is a real message.
    /// </summary>
    public static bool IsNoDataNotice(string text, string body, int buttons, bool captionIsReport) =>
        IsNoData(body) || IsNoData(text) || (body.Length == 0 && buttons == 1 && captionIsReport);

    /// <summary>The workbook path named in ERP's message, or null.</summary>
    public static string? PathIn(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"[A-Za-z]:\\[^""\r\n]*?\.xlsx", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    /// <summary>A workbook ERP wrote for <paramref name="report"/>: 「COPR1520261016000003202610160001.xlsx」.</summary>
    public static bool IsReportFile(string fileName, ErpReport report) =>
        fileName.StartsWith(report.Code, StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A customer's 銷貨單 in a date range; both dates optional (ERP then leaves that end open).</summary>
internal sealed record SalesQuery(string CustomerCode, DateOnly? From, DateOnly? To)
{
    /// <summary>
    /// One 銷貨單／銷退單 by its number (user, 2026-10-17: open the order behind a 未收款 憑證 without
    /// leaving the list). Then <see cref="CustomerCode"/> is empty and the dates are not used.
    /// </summary>
    public string? OrderNo { get; init; }

    /// <summary>「232A-20260804003」: 單別, a dash, the number (ERP's mask shows the dash itself).</summary>
    public static SalesQuery ForOrder(string input)
    {
        var no = (input ?? string.Empty).Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(no, "^[A-Z0-9]{2,6}-[0-9]{6,20}$"))
            throw new ArgumentException("單號格式不正確（例如 232A-20260804003）。");
        if (SalesReport.ForOrder(no) is null)
            throw new ArgumentException($"「{no}」不是銷貨單（23 開頭）或銷退單（24 開頭），無法查詢。");
        return new SalesQuery(string.Empty, null, null) { OrderNo = no };
    }

    /// <summary>What is typed into ERP's 單號 box: the number without the dash (the mask adds it).</summary>
    public string OrderKeys => (OrderNo ?? string.Empty).Replace("-", string.Empty);

    public static SalesQuery Resolve(string customerInput, DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (!ItemCodeRules.TryNormalize(customerInput, out var code, out var error, ErpParty.Customer.CodeName))
            throw new ArgumentException(error);
        if (from is { } start && start > today) throw new ArgumentException("單據日期的起日不可晚於今天。");
        if (from is { } a && to is { } b && a > b) throw new ArgumentException("單據日期的起日不可晚於迄日。");
        return new SalesQuery(code, from, to);
    }

    public HistoryRange Dates => new(From, To);
}

/// <summary>
/// One of the two 明細表 a customer's sales are read from (user, 2026-10-17: 有銷貨就有銷退): COPR15
/// 銷貨單明細表 and COPR16 銷退單明細表, driven the same way. Their exports differ in column names
/// (samples 2026-10-16/17), mapped here.
/// </summary>
internal sealed record SalesReport(
    string Code, string Title, string Kind, string NoColumn, string NoteColumn,
    string QuantityColumn, string AmountColumn, string GiftColumn, string? TypeColumn,
    string HeaderAmountColumn, string HeaderTaxColumn,
    string? SourceColumn = null, string? ReasonCodeColumn = null, string? ReasonColumn = null)
{
    public static readonly SalesReport Sales = new(
        "COPR15", "銷貨單明細表", "銷貨", "銷貨單號", "備註",
        "銷貨數量", "金額", "贈/備品量", "類型", "原幣銷貨金額", "原幣銷貨稅額");

    // COPR16 has no 金額 column: 原幣未稅金額 is the line amount.
    public static readonly SalesReport Returns = new(
        "COPR16", "銷退單明細表", "銷退", "銷退單號", "備注",
        "銷退數量", "原幣未稅金額", "銷退贈品量", null, "原幣銷退金額", "原幣銷退稅額",
        SourceColumn: "原銷貨單", ReasonCodeColumn: "銷退原因代號", ReasonColumn: "原因說明");

    public static readonly IReadOnlyList<SalesReport> All = [Sales, Returns];

    /// <summary>基本選項 group holding the 單號 起／迄 (user's screenshot 2026-10-17: 選擇銷貨單號).</summary>
    public string OrderGroup => "選擇" + NoColumn;

    /// <summary>The report an order number belongs to: 單別 23x 銷貨, 24x 銷退; else null.</summary>
    public static SalesReport? ForOrder(string orderNo) =>
        orderNo.StartsWith("23", StringComparison.Ordinal) ? Sales
        : orderNo.StartsWith("24", StringComparison.Ordinal) ? Returns : null;

    /// <summary>The ERP report this one is read from.</summary>
    public ErpReport Form => new(Code, Title, SalesReportLayout.Tabs, SalesReportLayout.TabStrip);

    /// <summary>「銷貨單明細表[公司]」 or 「銷貨單明細表(COPR15)[A]」.</summary>
    public bool IsTitle(string title) => Form.IsTitle(title);
}

/// <summary>One line of a 銷貨單 or 銷退單.</summary>
internal sealed record SalesLine(
    int Seq, string ItemCode, string ItemName, string Specification, string Warehouse,
    decimal Quantity, string Unit, decimal UnitPrice, decimal Amount,
    string GiftType, decimal GiftQuantity, string Batch, string Note, string SourceOrder);
// SourceOrder is COPR16's 原銷貨單 as ERP gives it (「XXXA-yyyyMMdd001-0003」: 銷貨單 + 序號), empty
// when there is none. Linking a 銷退單 to its 銷貨單 is left to CY Web (user, 2026-10-17: the
// program is only the go-between).

/// <summary>One 銷貨單 or 銷退單 (<see cref="Kind"/>) with its lines in ERP's order.</summary>
internal sealed record SalesOrder(
    string Kind, string OrderNo, DateOnly Date, string CustomerCode, string CustomerName,
    string Salesperson, string Department, string PaymentTerm, string TaxType, string InvoiceType,
    decimal TotalQuantity, decimal Amount, decimal Tax, decimal Total, string Note, string Reason,
    IReadOnlyList<SalesLine> Lines);

/// <summary>A customer's 銷貨單 and 銷退單 in the range, newest first.</summary>
internal sealed record SalesQueryResult(SalesQuery Query, IReadOnlyList<SalesOrder> Orders, TimeSpan Elapsed)
{
    public bool Found => Orders.Count > 0;
    public int Count(string kind) => Orders.Count(o => o.Kind == kind);
}

/// <summary>
/// Reads a COPR15／COPR16 export (user's samples 2026-10-16/17): one sheet, row 1 the column
/// titles (COPR15 with spaces: 「品    號」, 「單   價」), then one row per line repeating its
/// order's 單頭, and a last 「小計:」 row. The note title is used twice: the first is the order's
/// note, the second the line's.
/// </summary>
internal static class SalesExport
{
    private static string Normalize(string title) => new(title.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static IEnumerable<string> Required(SalesReport report) =>
        new[]
        {
            "單據日期", report.NoColumn, "客戶代號", "客戶簡稱", "部門", "業務員", "課稅別", "發票聯數",
            "總數量", report.NoteColumn, report.HeaderAmountColumn, report.HeaderTaxColumn, "原幣合計",
            "品號", "品名", "規格", "庫別代號", report.QuantityColumn, report.GiftColumn, "單位", "單價",
            report.AmountColumn, "批號",
        }.Concat(new[] { report.TypeColumn, report.SourceColumn }.OfType<string>());

    /// <summary>
    /// The orders of <paramref name="query"/>, newest first. A row of another customer or outside
    /// the dates means ERP did not apply the conditions, and the whole export is refused. A
    /// missing 單價 column means 列印單價金額 was not ticked.
    /// </summary>
    public static IReadOnlyList<SalesOrder> Parse(IReadOnlyList<(string Name, List<string[]> Rows)> sheets, SalesReport report, SalesQuery query)
    {
        var what = report.Title;
        if (sheets.Count == 0) throw new InvalidDataException($"ERP 匯出的{what}沒有工作表。");
        var rows = sheets[0].Rows;
        var titleIndex = rows.FindIndex(r => r.Select(Normalize).Contains(report.NoColumn));
        if (titleIndex < 0)
        {
            // An empty report may export nothing but a 小計 row, or nothing at all.
            if (rows.All(r => r.All(string.IsNullOrWhiteSpace) || IsSubtotal(r))) return [];
            throw new InvalidDataException($"ERP 匯出的{what}找不到「{report.NoColumn}」欄位標題。");
        }
        var titles = rows[titleIndex].Select(Normalize).ToArray();
        var missing = Required(report).Where(t => !titles.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            var hint = missing.Contains("單價") ? "（請確認進階選項的「列印單價金額」有勾）" : string.Empty;
            throw new InvalidDataException($"ERP 匯出的{what}缺少欄位：{string.Join("、", missing)}{hint}。");
        }

        var orders = new List<(string No, List<ExportRow> Rows)>();
        foreach (var values in rows.Skip(titleIndex + 1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var r = new ExportRow(titles, values);
            var no = r[report.NoColumn].Trim();
            if (no.Length == 0)
            {
                if (IsSubtotal(values)) continue;
                throw new InvalidDataException($"ERP 匯出的{what}有一列沒有{report.NoColumn}。");
            }
            if (query.OrderNo is { } wanted)
            {
                // One order: every row is that order (a row of another one means the 單號 was not applied).
                if (no != wanted)
                    throw new InvalidDataException($"ERP 匯出的單號「{no}」不是查詢的「{wanted}」，查詢條件可能沒有套用，結果不採用。");
                var one = orders.FirstOrDefault(o => o.No == no);
                if (one.Rows is null) orders.Add(one = (no, []));
                one.Rows.Add(r);
                continue;
            }
            var code = r["客戶代號"];
            if (!ItemCodeRules.SameCode(code, query.CustomerCode))
                throw new InvalidDataException($"ERP 匯出的{report.Kind}單「{no}」是客戶「{code.Trim()}」，不是查詢的「{query.CustomerCode}」，查詢條件可能沒有套用，結果不採用。");
            var date = ParseDate(r["單據日期"], no);
            if ((query.From is { } from && date < from) || (query.To is { } to && date > to))
                throw new InvalidDataException($"ERP 匯出的{report.Kind}單「{no}」日期 {date:yyyy/MM/dd} 不在查詢範圍內，查詢條件可能沒有套用，結果不採用。");
            var group = orders.FirstOrDefault(o => o.No == no);
            if (group.Rows is null) orders.Add(group = (no, []));
            group.Rows.Add(r);
        }
        return orders.Select(o => ToOrder(report, o.No, o.Rows)).OrderBy(o => o, Newest).ToList();
    }

    /// <summary>Newest first; on one day the higher number first.</summary>
    public static readonly IComparer<SalesOrder> Newest = Comparer<SalesOrder>.Create((a, b) =>
        b.Date.CompareTo(a.Date) is var byDate && byDate != 0 ? byDate : string.CompareOrdinal(b.OrderNo, a.OrderNo));

    private static bool IsSubtotal(string[] values) =>
        values.Any(v => v.Trim().StartsWith("小計", StringComparison.Ordinal));

    private static SalesOrder ToOrder(SalesReport report, string no, List<ExportRow> rows)
    {
        var h = rows[0];
        string Opt(ExportRow r, string? column) => column is null ? string.Empty : r[column].Trim();
        var lines = rows.Select((r, i) => new SalesLine(
            i + 1, r["品號"].Trim(), r["品名"].Trim(), r["規格"].Trim(), JoinCode(r["庫別代號"], r["庫別名稱"]),
            Number(r[report.QuantityColumn]), r["單位"].Trim(), Number(r["單價"]), Number(r[report.AmountColumn]),
            Opt(r, report.TypeColumn), Number(r[report.GiftColumn]), r["批號"].Trim(), r[report.NoteColumn, 2].Trim(),
            Opt(r, report.SourceColumn) is var source && source.Trim('-').Length > 0 ? source : string.Empty)).ToList();
        return new SalesOrder(
            report.Kind, no, ParseDate(h["單據日期"], no), h["客戶代號"].Trim(), h["客戶簡稱"].Trim(),
            h["業務員"].Trim(), h["部門"].Trim(), Opt(h, "付款名稱"), h["課稅別"].Trim(), h["發票聯數"].Trim(),
            Number(h["總數量"]), Number(h[report.HeaderAmountColumn]), Number(h[report.HeaderTaxColumn]), Number(h["原幣合計"]),
            h[report.NoteColumn, 1].Trim(), JoinCode(Opt(h, report.ReasonCodeColumn), Opt(h, report.ReasonColumn)), lines);
    }

    private static string JoinCode(string code, string name) =>
        string.Join(" ", new[] { code.Trim(), name.Trim() }.Where(v => v.Length > 0));

    private static DateOnly ParseDate(string text, string no) =>
        DateOnly.TryParseExact(text.Trim(), ["yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new InvalidDataException($"ERP 匯出的「{no}」單據日期「{text.Trim()}」無法辨識。");

    private static decimal Number(string text) =>
        decimal.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
