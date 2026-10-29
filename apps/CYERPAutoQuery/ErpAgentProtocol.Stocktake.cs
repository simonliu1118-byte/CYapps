using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CYERPAutoQuery;

/// <summary>STOCKTAKE_SYNC／STOCKTAKE_WRITE claim and complete bodies (WO-1.3.0 §3, CY Web WO-0.9.52).</summary>
internal static partial class ErpAgentProtocol
{
    private static ErpAgentJob ParseStocktakeClaim(JsonObject body, string id, string type)
    {
        static string Text(JsonNode? node, string what) =>
            node is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw new InvalidDataException($"盤點工作缺少 {what}。");

        if (type == ErpAgentJob.StocktakeSyncType)
        {
            var skips = (body["skipSheets"] as JsonArray ?? []).Select(n => Text(n, "skipSheets")).ToArray();
            var only = body["onlySheet"] is null ? null : Text(body["onlySheet"], "onlySheet");
            return new ErpAgentJob(id, "") { QueryType = type, SkipSheets = skips, OnlySheet = only };
        }

        var sheet = Text(body["sheetNo"], "sheetNo");
        var rows = (body["rows"] as JsonArray ?? throw new InvalidDataException("盤點寫回缺少 rows。"))
            .Select(n => n as JsonObject ?? throw new InvalidDataException("盤點寫回列格式錯誤。"))
            .Select(r => new StocktakeWriteRow(Text(r["key"], "key"), r["seq"] is null ? null : Text(r["seq"], "seq"),
                Text(r["itemCode"], "itemCode"), r["batch"] is null ? "" : Text(r["batch"], "batch"), Text(r["quantity"], "quantity")))
            .ToList();
        return new ErpAgentJob(id, "") { QueryType = type, SheetNo = sheet, StocktakeRows = StocktakeQuery.ValidateWrite(sheet, rows) };
    }

    public static string StocktakeSyncBody(StocktakeSyncResult result, string agentVersion)
    {
        var body = new JsonObject
        {
            ["outcome"] = result.Details.Count > 0 ? "FOUND" : "NOT_FOUND",
            ["elapsedMs"] = (long)result.Elapsed.TotalMilliseconds,
            ["agentVersion"] = agentVersion,
            ["sheets"] = new JsonArray(result.Sheets.Select(s => (JsonNode)new JsonObject
            {
                ["sheetNo"] = s.SheetNo, ["date"] = s.Date.ToString("yyyy-MM-dd")
            }).ToArray()),
            ["details"] = new JsonArray(result.Details.Select(d => (JsonNode)new JsonObject
            {
                ["sheetNo"] = d.SheetNo,
                ["rows"] = new JsonArray(d.Rows.Select(v => (JsonNode)new JsonObject
                {
                    ["seq"] = v.Seq, ["itemCode"] = v.ItemCode, ["itemName"] = v.ItemName,
                    ["spec"] = v.Spec, ["warehouseName"] = v.WarehouseName, ["location"] = v.Location,
                    ["unit"] = v.Unit, ["batch"] = v.Batch, ["bookQty"] = StocktakeQuery.Quantity(v.BookQty)
                }).ToArray())
            }).ToArray())
        };
        return StocktakeJson(body);
    }

    public static string StocktakeWriteBody(StocktakeWriteResult result, string agentVersion)
    {
        var body = new JsonObject
        {
            ["outcome"] = result.Confirmed ? "FOUND" : "FAILED",
            ["sheetNo"] = result.SheetNo,
            ["elapsedMs"] = (long)result.Elapsed.TotalMilliseconds,
            ["agentVersion"] = agentVersion,
            ["results"] = new JsonArray(result.Results.Select(v => (JsonNode)new JsonObject
            {
                ["key"] = v.Key, ["ok"] = v.Ok, ["erpSeq"] = v.ErpSeq,
                ["message"] = v.Message.Length <= 200 ? v.Message : v.Message[..200]
            }).ToArray()),
            ["confirmed"] = result.Confirmed,
            ["mismatches"] = new JsonArray(result.Mismatches.Select(v => (JsonNode)new JsonObject
            {
                ["key"] = v.Key, ["seq"] = v.Seq, ["itemCode"] = v.ItemCode, ["batch"] = v.Batch,
                ["expected"] = v.Expected, ["actual"] = v.Actual
            }).ToArray())
        };
        return StocktakeJson(body);
    }

    private static string StocktakeJson(JsonObject body)
    {
        var json = body.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        // CY Web takes at most 1 MiB per complete.
        if (Encoding.UTF8.GetByteCount(json) > StocktakeQuery.MaxCompleteBytes)
            throw new InvalidDataException("盤點結果超過 900 KiB，請改用「重新整理」逐張讀取。");
        return json;
    }
}
