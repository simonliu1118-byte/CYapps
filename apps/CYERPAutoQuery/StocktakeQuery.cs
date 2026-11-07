using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CYERPAutoQuery;

/// <summary>
/// 庫存盤點 (WO-1.3.0): the data read from and written to ERP's 盤點底稿. Pure, so it is unit
/// tested on any OS; the ERP screens are driven by ItemQueryService.Stocktake*.cs.
/// </summary>
internal sealed record StocktakeSheet(string SheetNo, DateOnly Date);
internal sealed record StocktakeLine(
    string Seq, string ItemCode, string ItemName, string Spec, string WarehouseName,
    string Location, string Unit, string Batch, string BookQty);
internal sealed record StocktakeDetail(string SheetNo, IReadOnlyList<StocktakeLine> Rows);
internal sealed record StocktakeSyncResult(
    IReadOnlyList<StocktakeSheet> Sheets, IReadOnlyList<StocktakeDetail> Details, TimeSpan Elapsed);
/// <summary>One line to write: <see cref="Seq"/> null is a batch added in CY Web (INVI06 new row).</summary>
internal sealed record StocktakeWriteRow(string Key, string? Seq, string ItemCode, string Batch, string Quantity);
internal sealed record StocktakeWriteRowResult(string Key, bool Ok, string? ErpSeq, string Message);
internal sealed record StocktakeMismatch(
    string Key, string? Seq, string ItemCode, string Batch, string Expected, string Actual);
internal sealed record StocktakeWriteResult(
    string SheetNo, IReadOnlyList<StocktakeWriteRowResult> Results, bool Confirmed,
    IReadOnlyList<StocktakeMismatch> Mismatches, TimeSpan Elapsed);

internal static class StocktakeQuery
{
    public const int MaxRowsPerSheet = 3000;
    public const int MaxCompleteBytes = 900 * 1024;
    private static readonly Regex SheetNumber = new(@"^[0-9]{6,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sequence = new(@"^[0-9]{5}$", RegexOptions.CultureInvariant);
    private static readonly Regex QuantityValue = new(@"^-?[0-9]{1,9}(?:\.[0-9]{1,4})?$", RegexOptions.CultureInvariant);
    private static readonly Regex F2Row = new(@"(?<![0-9])(?<number>[0-9]{6,20})\s*(?<date>[0-9]{4}/[0-9]{2}/[0-9]{2})(?![0-9])",
        RegexOptions.CultureInvariant);
    private static readonly Regex Position = new(@"(?<![0-9/])(?<at>[0-9]+)\s*/\s*(?<total>[0-9]+)(?![0-9/])", RegexOptions.CultureInvariant);

    public static bool IsSheetNo(string? number) => number is not null && SheetNumber.IsMatch(number);
    public static bool IsSeq(string? seq) => seq is not null && Sequence.IsMatch(seq);

    /// <summary>A quantity as a plain decimal string (ERP may show 「1,234.0」).</summary>
    public static string Quantity(string? value)
    {
        var text = (value ?? "").Trim().Replace(",", "");
        if (text.Length == 0) text = "0";
        if (!QuantityValue.IsMatch(text))
            throw new InvalidDataException($"盤點數量格式不正確：「{value}」。");
        return decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            .ToString("0.####", CultureInfo.InvariantCulture);
    }

    public static bool SameQuantity(string? a, string? b) => Quantity(a) == Quantity(b);

    // ---- INVR11 「F2開窗查詢」 -------------------------------------------------------------

    /// <summary>
    /// Text lines of an OCR result: tokens whose vertical centres are within half a token height,
    /// left to right (the grid's 底稿編號 and 盤點日期 cells come as separate tokens).
    /// </summary>
    public static IReadOnlyList<(string Text, Rectangle Rect)> Lines(IReadOnlyList<OcrToken> tokens)
    {
        var lines = new List<List<OcrToken>>();
        foreach (var token in tokens.OrderBy(t => t.Rect.Top + t.Rect.Height / 2))
        {
            var cy = token.Rect.Top + token.Rect.Height / 2.0;
            var line = lines.FirstOrDefault(l =>
                Math.Abs(l[0].Rect.Top + l[0].Rect.Height / 2.0 - cy) <= Math.Max(l[0].Rect.Height, token.Rect.Height) / 2.0);
            if (line is null) lines.Add([token]);
            else line.Add(token);
        }
        return lines.Select(l =>
        {
            var ordered = l.OrderBy(t => t.Rect.Left).ToList();
            var rect = ordered.Skip(1).Aggregate(ordered[0].Rect, (r, t) => Rectangle.Union(r, t.Rect));
            return (string.Join(" ", ordered.Select(t => t.Text.Trim())), rect);
        }).OrderBy(l => l.rect.Top).ToList();
    }

    /// <summary>The sheets in the F2 grid (底稿編號 + 盤點日期 on one line), each with where its line is.</summary>
    public static IReadOnlyList<(StocktakeSheet Sheet, Rectangle Rect)> ParseF2(IReadOnlyList<OcrToken> tokens)
    {
        var found = new List<(StocktakeSheet, Rectangle)>();
        foreach (var (text, rect) in Lines(tokens))
        {
            var match = F2Row.Match(text);
            if (!match.Success) continue;
            if (!DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                continue;
            found.Add((new StocktakeSheet(match.Groups["number"].Value, date), rect));
        }
        return found;
    }

    /// <summary>The record count in the F2 footer 「1 / 4」 (record 1 of 4), or null when it is not read.</summary>
    public static int? ParseF2Total(IReadOnlyList<OcrToken> tokens)
    {
        foreach (var (text, _) in Lines(tokens).Reverse())
            if (Position.Match(text) is { Success: true } m && int.TryParse(m.Groups["total"].Value, out var total))
                return total;
        return null;
    }

    // ---- INVI06 detail grid ------------------------------------------------------------------

    /// <summary>
    /// Centre x of each wanted column header in the grid's header line. 「批號盤點」 also contains
    /// 「批號」, so a header is only taken where it is not part of a longer header.
    /// </summary>
    public static IReadOnlyDictionary<string, int> GridColumns(IReadOnlyList<OcrToken> header, params string[] names)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var batchCheck = OcrWordFinder.FindWords(header, "批號盤點");
        foreach (var name in names)
        {
            var hit = OcrWordFinder.FindWords(header, name)
                .Where(r => name == "批號盤點" || !batchCheck.Any(b => b.IntersectsWith(r)))
                .Cast<Rectangle?>().FirstOrDefault();
            if (hit is { } r) columns[name] = r.Left + r.Width / 2;
        }
        return columns;
    }

    /// <summary>
    /// The 序號 down the grid's first column, top to bottom: tokens across the column's centre that
    /// start with 5 digits (OCR may join a 序號 with the 品號 next to it).
    /// </summary>
    public static IReadOnlyList<(string Seq, int Y)> GridSeqs(IReadOnlyList<OcrToken> tokens, int seqX) =>
        tokens.Where(t => t.Rect.Left <= seqX && t.Rect.Right >= seqX)
            .Select(t => (Match: Regex.Match(t.Text.Trim(), @"^[0-9]{5}(?![0-9])"), Y: t.Rect.Top + t.Rect.Height / 2))
            .Where(m => m.Match.Success).Select(m => (m.Match.Value, m.Y)).OrderBy(m => m.Y).ToList();

    /// <summary>A grid row's whole text without spaces (to check the 品號 and 批號 typed into it).</summary>
    public static string RowText(IReadOnlyList<OcrToken> tokens, int rowCenterY) =>
        string.Concat(Lines(tokens).Where(l => Math.Abs(l.Rect.Top + l.Rect.Height / 2 - rowCenterY) <= 8)
            .Select(l => OcrTextNormalizer.Normalize(l.Text).Replace(" ", "")));

    /// <summary>What a row shows under a column: the tokens on the row's line overlapping the column's centre ± <paramref name="halfWidth"/>.</summary>
    public static string CellText(IReadOnlyList<OcrToken> tokens, int rowCenterY, int columnX, int halfWidth)
    {
        var cell = tokens.Where(t => Math.Abs(t.Rect.Top + t.Rect.Height / 2 - rowCenterY) <= Math.Max(6, t.Rect.Height / 2) &&
                                     t.Rect.Right >= columnX - halfWidth && t.Rect.Left <= columnX + halfWidth)
            .OrderBy(t => t.Rect.Left).Select(t => t.Text.Trim());
        return string.Join("", cell);
    }

    // ---- INVR11 export ------------------------------------------------------------------------

    /// <summary>
    /// The INVR11 workbook (品號盤點清單 with 儲位／批號／盤點數量 shown): 盤點數量 is ERP's book
    /// quantity; 初盤數量 is a blank line for writing on paper and is ignored.
    /// </summary>
    public static StocktakeDetail ParseExport(string sheetNo,
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets)
    {
        string[] required = ["底稿編號", "底稿序號", "品號", "品名", "規格", "庫別名稱", "儲位", "單位", "批號", "盤點數量"];
        foreach (var (_, rows) in sheets)
        {
            for (var h = 0; h < Math.Min(rows.Count, 25); h++)
            {
                var columns = rows[h].Select(v => v.Trim()).ToArray();
                var index = required.ToDictionary(k => k, k => Array.IndexOf(columns, k));
                if (index.Values.Any(v => v < 0)) continue;
                var parsed = new List<StocktakeLine>();
                for (var n = h + 1; n < rows.Count; n++)
                {
                    var values = rows[n];
                    string Cell(string col) => index[col] < values.Length ? values[index[col]].Trim() : "";
                    if (!IsSeq(Cell("底稿序號"))) continue; // blank and total lines
                    if (Cell("底稿編號") != sheetNo)
                        throw new InvalidDataException($"INVR11 匯出含其他盤點底稿（{Cell("底稿編號")}），不是 {sheetNo}。");
                    parsed.Add(new StocktakeLine(Cell("底稿序號"), Cell("品號"), Cell("品名"), Cell("規格"),
                        Cell("庫別名稱"), Cell("儲位"), Cell("單位"), Cell("批號"), Quantity(Cell("盤點數量"))));
                }
                return new StocktakeDetail(sheetNo, parsed);
            }
        }
        throw new InvalidDataException("INVR11 匯出缺少欄位：" + string.Join("、", required) + "（欄位資訊的儲位／批號／盤點數量要切成 Y）。");
    }

    /// <summary>The ERP line a write is about: by 底稿序號, or for an added batch by 品號＋批號.</summary>
    public static StocktakeLine? Find(IReadOnlyList<StocktakeLine> lines, StocktakeWriteRow row) =>
        row.Seq is { } seq
            ? lines.FirstOrDefault(l => l.Seq == seq)
            : lines.FirstOrDefault(l => l.ItemCode == row.ItemCode && l.Batch == row.Batch);

    public static bool AlreadyApplied(StocktakeWriteRow row, StocktakeLine? found) =>
        found is not null && found.ItemCode == row.ItemCode && found.Batch == row.Batch &&
        SameQuantity(found.BookQty, row.Quantity);

    /// <summary>The INVR11 read after writing: every written line must show its quantity.</summary>
    public static IReadOnlyList<StocktakeMismatch> Verify(
        IReadOnlyList<StocktakeWriteRow> expected, IReadOnlyList<StocktakeLine> actual)
    {
        var mismatches = new List<StocktakeMismatch>();
        foreach (var row in expected)
        {
            var seen = Find(actual, row);
            if (!AlreadyApplied(row, seen))
                mismatches.Add(new StocktakeMismatch(row.Key, seen?.Seq ?? row.Seq, row.ItemCode, row.Batch,
                    Quantity(row.Quantity), seen is null ? "0" : seen.BookQty));
        }
        return mismatches;
    }

    public static IReadOnlyList<StocktakeWriteRow> ValidateWrite(string sheetNo, IReadOnlyList<StocktakeWriteRow> rows)
    {
        if (!IsSheetNo(sheetNo)) throw new InvalidDataException("盤點底稿編號錯誤。");
        if (rows.Count > MaxRowsPerSheet) throw new InvalidDataException("盤點寫回超過 3,000 列。");
        if (rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("盤點寫回的列鍵重複。");
        foreach (var row in rows)
        {
            if ((row.Seq is not null && !IsSeq(row.Seq)) || row.ItemCode.Length == 0 || (row.Seq is null && row.Batch.Length == 0))
                throw new InvalidDataException($"盤點寫回列 {row.Key} 的序號、品號或批號不正確。");
            _ = Quantity(row.Quantity);
        }
        return rows;
    }
}
