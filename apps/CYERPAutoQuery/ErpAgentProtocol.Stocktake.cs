using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CYERPAutoQuery;

/// <summary>
/// Strict CY Web stocktake bridge. Never copies ERP exports or credentials into a response.
/// Job input is untrusted; malformed claims fail closed before any Win32 work.
/// </summary>
internal static partial class ErpAgentProtocol
{
    private static ErpAgentJob ParseStocktakeClaim(JsonObject body, string id, string type)
    {
        if (type == ErpAgentJob.StocktakeSyncType)
        {
            if (body["skipSheets"] is not JsonArray skips || skips.Count > StocktakeQuery.MaxSkipSheets)
                throw new InvalidDataException("盤點更新 skipSheets 缺少或超過 500。");
            var list = skips.Select(x => x is JsonValue v && v.TryGetValue<string>(out var n) && StocktakeQuery.IsSheetNo(n)
                ? n : throw new InvalidDataException("盤點更新的底稿編號錯誤。")).ToArray();
            if (list.Distinct(StringComparer.Ordinal).Count() != list.Length)
                throw new InvalidDataException("盤點 skipSheets 不得重複。");
            var only = body["onlySheet"] is null ? null
                : body["onlySheet"] is JsonValue v && v.TryGetValue<string>(out var n)
                    ? n : throw new InvalidDataException("盤點 onlySheet 格式錯誤。");
            if (only is not null && !StocktakeQuery.IsSheetNo(only))
                throw new InvalidDataException("盤點 onlySheet 不是有效底稿編號。");
            return new ErpAgentJob(id,"") { QueryType=type,SkipSheets=list,OnlySheet=only };
        }

        var sheet = body["sheetNo"] is JsonValue sv && sv.TryGetValue<string>(out var sn)
            ? sn : throw new InvalidDataException("盤點寫回缺底稿編號。");
        if (body["rows"] is not JsonArray rows || rows.Count > StocktakeQuery.MaxRowsPerSheet)
            throw new InvalidDataException("盤點寫回列數無效。");
        var values=new List<StocktakeWriteRow>();
        foreach(var raw in rows)
        {
            if(raw is not JsonObject row)throw new InvalidDataException("盤點寫回列格式錯誤。");
            string Need(string key) => row[key] is JsonValue v && v.TryGetValue<string>(out var val)
                ? val : throw new InvalidDataException("盤點寫回欄位缺少："+key);
            string? seq=row["seq"] is null?null:Need("seq");
            values.Add(new StocktakeWriteRow(Need("key"),seq,Need("itemCode"),Need("batch"),Need("quantity")));
        }
        return new ErpAgentJob(id,"") {QueryType=type,SheetNo=sheet,
            StocktakeRows=StocktakeQuery.ValidateWrite(sheet,values)};
    }

    public static string StocktakeSyncBody(StocktakeSyncResult result, string agentVersion, string? onlySheet)
    {
        if (result.Sheets.Count > StocktakeQuery.MaxSheets || result.Details.Count > StocktakeQuery.MaxSheets)
            throw new InvalidDataException("盤點更新回應超過 200 張上限。");
        var listed=result.Sheets.Select(x=>x.SheetNo).ToHashSet(StringComparer.Ordinal);
        if(result.Sheets.Count!=listed.Count ||
            result.Sheets.Any(x=>!StocktakeQuery.IsSheetNo(x.SheetNo)) ||
            (onlySheet is not null && result.Details.Count != 1))
            throw new InvalidDataException("盤點底稿清單重複或只讀底稿數不符。");
        var details=new HashSet<string>(StringComparer.Ordinal);
        foreach(var detail in result.Details)
        {
            if(!listed.Contains(detail.SheetNo)||!details.Add(detail.SheetNo) ||
                detail.Rows.Count>StocktakeQuery.MaxRowsPerSheet ||
                detail.Rows.Select(x=>x.Seq).Distinct(StringComparer.Ordinal).Count()!=detail.Rows.Count)
                throw new InvalidDataException("盤點明細有未列出的底稿、重複或超過 3000 列。");
            if(detail.Rows.Any(x=>!StocktakeQuery.IsSeq(x.Seq)))throw new InvalidDataException("盤點序號格式錯誤。");
        }
        if (onlySheet is not null && result.Details[0].SheetNo!=onlySheet)
            throw new InvalidDataException("盤點 onlySheet 實際匯出編號不相同。");
        var body=new JsonObject
        {
            ["outcome"]=result.Details.Count>0?"FOUND":"NOT_FOUND",
            ["elapsedMs"]=(long)result.Elapsed.TotalMilliseconds,
            ["agentVersion"]=agentVersion,
            ["sheets"]=new JsonArray(result.Sheets.Select(s=>(JsonNode)new JsonObject
            {
                ["sheetNo"]=s.SheetNo,["date"]=s.Date.ToString("yyyy-MM-dd")
            }).ToArray()),
            ["details"]=new JsonArray(result.Details.Select(d=>(JsonNode)new JsonObject
            {
                ["sheetNo"]=d.SheetNo,
                ["rows"]=new JsonArray(d.Rows.Select(v=>(JsonNode)new JsonObject
                {
                    ["seq"]=v.Seq,["itemCode"]=v.ItemCode,["itemName"]=v.ItemName,
                    ["spec"]=v.Spec,["warehouseName"]=v.WarehouseName,["location"]=v.Location,
                    ["unit"]=v.Unit,["batch"]=v.Batch,["bookQty"]=StocktakeQuery.Quantity(v.BookQty)
                }).ToArray())
            }).ToArray())
        };
        return BoundedStocktake(body);
    }

    public static string StocktakeWriteBody(StocktakeWriteResult result,
        IReadOnlyList<StocktakeWriteRow> expected, string agentVersion)
    {
        StocktakeQuery.ValidateWrite(result.SheetNo,expected);
        var keys=expected.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
        if(result.Results.Count!=expected.Count ||
            result.Results.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=result.Results.Count ||
            result.Results.Any(x=>!keys.Contains(x.Key)) ||
            (result.Confirmed && (result.Mismatches.Count>0||result.Results.Any(x=>!x.Ok))))
            throw new InvalidDataException("ERP 盤點寫回結果／確認狀態不一致。");
        var original=expected.ToDictionary(x=>x.Key,StringComparer.Ordinal);
        var assignedSequences=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in result.Results.Where(x=>x.Ok))
        {
            var old=original[row.Key];
            if(!StocktakeQuery.IsSeq(row.ErpSeq) ||
                !assignedSequences.Add(row.ErpSeq!) ||
                (old.Seq is not null && old.Seq!=row.ErpSeq))
                throw new InvalidDataException("ERP 底稿序號與工作不一致，不能回報成功。");
        }
        var body=new JsonObject
        {
            ["outcome"]=result.Confirmed?"FOUND":"FAILED",["sheetNo"]=result.SheetNo,
            ["elapsedMs"]=(long)result.Elapsed.TotalMilliseconds,["agentVersion"]=agentVersion,
            ["results"]=new JsonArray(result.Results.Select(v=>(JsonNode)new JsonObject
            {
                ["key"]=v.Key,["ok"]=v.Ok,["erpSeq"]=v.ErpSeq,
                ["message"]=v.Message.Length<=200?v.Message:v.Message[..200]
            }).ToArray()),
            ["confirmed"]=result.Confirmed,
            ["mismatches"]=new JsonArray(result.Mismatches.Select(v=>(JsonNode)new JsonObject
            {
                ["key"]=v.Key,["seq"]=v.Seq,["itemCode"]=v.ItemCode,["batch"]=v.Batch,
                ["expected"]=v.Expected,["actual"]=v.Actual
            }).ToArray())
        };
        return BoundedStocktake(body);
    }

    private static string BoundedStocktake(JsonObject body)
    {
        var json=body.ToJsonString(new JsonSerializerOptions
        {
            Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        if(Encoding.UTF8.GetByteCount(json)>StocktakeQuery.MaxCompleteBytes)
            throw new InvalidDataException("盤點回報超過 900 KiB，不能截斷資料後宣稱成功。");
        return json;
    }
}
