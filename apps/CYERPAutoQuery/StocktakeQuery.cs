using System.Globalization;
using System.Text.RegularExpressions;

namespace CYERPAutoQuery;

/// <summary>
/// ERP stocktake contract: all data handling is pure and synthetic-testable.
/// No ERP side effect is permitted here. Mutations belong to the gated service.
/// </summary>
internal sealed record StocktakeSheet(string SheetNo, DateOnly Date);
internal sealed record StocktakeLine(
    string Seq, string ItemCode, string ItemName, string Spec, string WarehouseName,
    string Location, string Unit, string Batch, string BookQty);
internal sealed record StocktakeDetail(string SheetNo, IReadOnlyList<StocktakeLine> Rows);
internal sealed record StocktakeSyncResult(
    IReadOnlyList<StocktakeSheet> Sheets, IReadOnlyList<StocktakeDetail> Details,
    TimeSpan Elapsed);
internal sealed record StocktakeWriteRow(string Key, string? Seq, string ItemCode, string Batch, string Quantity);
internal sealed record StocktakeWriteRowResult(string Key, bool Ok, string? ErpSeq, string Message);
internal sealed record StocktakeMismatch(
    string Key, string? Seq, string ItemCode, string Batch, string Expected, string Actual);
internal sealed record StocktakeWriteResult(
    string SheetNo, IReadOnlyList<StocktakeWriteRowResult> Results, bool Confirmed,
    IReadOnlyList<StocktakeMismatch> Mismatches, TimeSpan Elapsed);

internal static class StocktakeQuery
{
    public const int MaxSheets = 200;
    public const int MaxSkipSheets = 500;
    public const int MaxRowsPerSheet = 3000;
    public const int MaxCompleteBytes = 900 * 1024;
    private static readonly Regex SheetNumber = new(@"^[0-9]{6,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sequence = new(@"^[0-9]{5}$", RegexOptions.CultureInvariant);
    private static readonly Regex QuantityValue = new(@"^-?(?:0|[1-9][0-9]{0,8})(?:\.[0-9]{1,4})?$", RegexOptions.CultureInvariant);
    private static readonly Regex F2Row = new(@"(?<![0-9])(?<number>[0-9]{6,20})\s+(?<date>[0-9]{4}[/\-][0-9]{2}[/\-][0-9]{2})(?![0-9])",
        RegexOptions.CultureInvariant);

    public static bool IsSheetNo(string? number) => number is not null && SheetNumber.IsMatch(number);
    public static bool IsSeq(string? seq) => seq is not null && Sequence.IsMatch(seq);

    public static string Quantity(string? value)
    {
        var text = value?.Trim() ?? "";
        if (!QuantityValue.IsMatch(text) ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            Math.Abs(amount) >= 1000000000m)
            throw new InvalidDataException("盤點數量格式不正確。");
        return text;
    }

    public static IReadOnlyList<StocktakeSheet> ParseF2(string ocr)
    {
        var result = new List<StocktakeSheet>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match row in F2Row.Matches(ocr))
        {
            var number = row.Groups["number"].Value;
            var date = row.Groups["date"].Value.Replace('/', '-');
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
                throw new InvalidDataException("盤點底稿的日期不正確。");
            if (!seen.Add(number)) continue; // Same row may repeat between OCR pages.
            result.Add(new StocktakeSheet(number, parsed));
            if (result.Count > MaxSheets)
                throw new InvalidDataException("F2 盤點底稿清單超過 200 張，不能截斷後宣稱已讀完。");
        }
        return result;
    }

    /// <summary>
    /// Only INVR11's 盤點數量 is the current ERP book value.
    /// 初盤數量 (underlined blank) is deliberately ignored.
    /// </summary>
    public static StocktakeDetail ParseExport(string sheetNo,
        IReadOnlyList<(string Name, List<string[]> Rows)> sheets)
    {
        if (!IsSheetNo(sheetNo)) throw new InvalidDataException("盤點底稿編號不正確。");
        string[] required =
            ["底稿編號","底稿序號","品號","品名","規格","庫別名稱","儲位","單位","批號","盤點數量"];
        List<StocktakeLine>? parsed = null;
        foreach (var (_, rows) in sheets)
        {
            for (var h = 0; h < Math.Min(rows.Count, 25); h++)
            {
                var columns = rows[h].Select(v => v.Trim()).ToArray();
                var index = required.ToDictionary(k => k, k => Array.IndexOf(columns, k));
                if (index.Values.Any(v => v < 0)) continue;
                if (parsed is not null) throw new InvalidDataException("同一匯出有多組盤點明細，無法確定資料來源。");
                parsed = [];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var n = h + 1; n < rows.Count; n++)
                {
                    var values = rows[n];
                    string Cell(string col)
                    {
                        var pos = index[col];
                        return pos < values.Length ? values[pos].Trim() : "";
                    }
                    if (values.All(string.IsNullOrWhiteSpace)) continue;
                    if (Cell("底稿編號") != sheetNo)
                        throw new InvalidDataException("匯出資料含其他盤點底稿編號，已拒絕讀取。");
                    var seq = Cell("底稿序號");
                    if (!IsSeq(seq) || !seen.Add(seq))
                        throw new InvalidDataException("底稿序號缺漏、格式錯誤或重複。");
                    var item = Cell("品號");
                    if (string.IsNullOrWhiteSpace(item) || item.Length > 40 ||
                        item.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-'))
                        throw new InvalidDataException("盤點品號格式錯誤。");
                    parsed.Add(new StocktakeLine(seq,item,Cell("品名"),Cell("規格"),
                        Cell("庫別名稱"),Cell("儲位"),Cell("單位"),Cell("批號"),
                        Quantity(Cell("盤點數量"))));
                    if (parsed.Count > MaxRowsPerSheet)
                        throw new InvalidDataException("盤點底稿超過 3,000 列，不可截斷。");
                }
            }
        }
        return new StocktakeDetail(sheetNo, parsed ?? throw new InvalidDataException(
            "盤點明細缺少必要欄位：" + string.Join("、", required)));
    }

    public static IReadOnlyList<StocktakeMismatch> Verify(
        IReadOnlyList<StocktakeWriteRow> expected, IReadOnlyList<StocktakeLine> actual,
        IReadOnlyDictionary<string,string>? assignedSeq = null)
    {
        if (expected.Count > MaxRowsPerSheet || actual.Count > MaxRowsPerSheet)
            throw new InvalidDataException("盤點核對超過每張 3,000 列上限。");
        var bySeq = actual.ToDictionary(x=>x.Seq,StringComparer.Ordinal);
        var mismatches = new List<StocktakeMismatch>();
        foreach (var row in expected)
        {
            var seq = row.Seq ?? (assignedSeq?.TryGetValue(row.Key,out var value)==true?value:null);
            var seen = seq is not null && bySeq.TryGetValue(seq,out var found)?found:null;
            if (seen is null && row.Seq is null)
            {
                var possible = actual.Where(x=>x.ItemCode==row.ItemCode && x.Batch==row.Batch).ToArray();
                if (possible.Length==1) seen=possible[0];
            }
            var actualValue = seen?.BookQty ?? "";
            var matching = seen is not null && seen.ItemCode==row.ItemCode &&
                seen.Batch==row.Batch && Quantity(row.Quantity)==Quantity(actualValue);
            if (!matching)
                mismatches.Add(new StocktakeMismatch(row.Key,seen?.Seq ?? seq,
                    row.ItemCode,row.Batch,row.Quantity,actualValue));
        }
        return mismatches;
    }

    public static bool AlreadyApplied(StocktakeWriteRow row, StocktakeLine? found) =>
        found is not null && found.ItemCode==row.ItemCode && found.Batch==row.Batch &&
        Quantity(found.BookQty)==Quantity(row.Quantity);

    /// <summary>
    /// INVR11 after write must match the entire original sheet, not just the
    /// subset returned to CY Web for modification. This detects unexpected
    /// changes to untouched batches and missing/duplicate ERP rows.
    /// </summary>
    public static IReadOnlyList<StocktakeMismatch> VerifyEntireSheet(
        IReadOnlyList<StocktakeLine> before,
        IReadOnlyList<StocktakeLine> after,
        IReadOnlyList<StocktakeWriteRow> writes,
        IReadOnlyDictionary<string,string>? assignedSeq = null)
    {
        var targetsBySequence=new Dictionary<string,StocktakeWriteRow>(StringComparer.Ordinal);
        foreach(var write in writes)
        {
            var sequence=write.Seq ?? (assignedSeq is not null &&
                assignedSeq.TryGetValue(write.Key,out var mapped)?mapped:null);
            if(sequence is not null)targetsBySequence[sequence]=write;
        }
        var afterBySeq=after.ToDictionary(row=>row.Seq,StringComparer.Ordinal);
        var mismatches=new List<StocktakeMismatch>();
        var checkedSequences=new HashSet<string>(StringComparer.Ordinal);
        foreach(var line in before)
        {
            var key=line.Seq;
            var write=targetsBySequence.GetValueOrDefault(key);
            var wanted=write?.Quantity ?? line.BookQty;
            afterBySeq.TryGetValue(key,out var result);
            checkedSequences.Add(key);
            if(result is null || result.ItemCode!=line.ItemCode || result.Batch!=line.Batch)
                throw new InvalidDataException("INVR11 核對時 ERP 底稿列遺失或識別變更，禁止確認。");
            if(decimal.Parse(Quantity(result.BookQty),CultureInfo.InvariantCulture)!=
                decimal.Parse(Quantity(wanted),CultureInfo.InvariantCulture))
                mismatches.Add(new StocktakeMismatch(key,key,line.ItemCode,line.Batch,wanted,
                    result.BookQty));
        }
        foreach(var line in writes.Where(x=>x.Seq is null))
        {
            var seq=assignedSeq is not null && assignedSeq.TryGetValue(line.Key,out var assigned)
                ? assigned : after.SingleOrDefault(x=>x.ItemCode==line.ItemCode && x.Batch==line.Batch)?.Seq;
            var result=seq is not null && afterBySeq.TryGetValue(seq,out var found)?found:null;
            if(result is null || result.ItemCode!=line.ItemCode || result.Batch!=line.Batch)
                throw new InvalidDataException("INVR11 新增批號核對時未找到或識別錯誤，禁止確認。");
            if(decimal.Parse(Quantity(result.BookQty),CultureInfo.InvariantCulture)!=
                decimal.Parse(Quantity(line.Quantity),CultureInfo.InvariantCulture))
                mismatches.Add(new StocktakeMismatch(line.Key,seq,line.ItemCode,line.Batch,line.Quantity,
                    result.BookQty));
            if(seq is not null)checkedSequences.Add(seq);
        }
        // Any unrecognised ERP row is a dangerous discrepancy, never approve
        // confirmation if a new row appeared outside the authorized job.
        if(after.Any(row=>!checkedSequences.Contains(row.Seq)))
            throw new InvalidDataException("INVR11 出現工作範圍外的新列，無法安全確認。");
        return mismatches;
    }

    public static IReadOnlyList<StocktakeWriteRow> ValidateWrite(
        string sheetNo,IReadOnlyList<StocktakeWriteRow> rows)
    {
        if (!IsSheetNo(sheetNo)) throw new InvalidDataException("盤點底稿編號錯誤。");
        if (rows.Count>MaxRowsPerSheet) throw new InvalidDataException("寫回列數超過 3,000。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            if (!keys.Add(row.Key) || row.Key.Length>60 ||
                (row.Seq is not null && (!IsSeq(row.Seq) || row.Key!=row.Seq)) ||
                (row.Seq is null && (!Regex.IsMatch(row.Key,@"^N[0-9]{4,}$") || row.Batch.Length==0)) ||
                row.ItemCode.Length is < 1 or > 40 || row.Batch.Length>80)
                throw new InvalidDataException("ERP 寫回工作含不合法或重複的列鍵。");
            _ = Quantity(row.Quantity);
        }
        return rows;
    }
}
