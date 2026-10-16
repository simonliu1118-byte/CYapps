using System.Text.Json;
using System.Text.Json.Nodes;

namespace CYERPAutoQuery;

/// <summary>FAILED error codes of the CY Web ERP item query contract (chihyuan-web #169).</summary>
internal enum ErpAgentErrorCode { ERP_NOT_OPEN, ERP_BUSY, ERP_STATE, EXPORT_FAILED, CANCELLED, INTERNAL }

/// <summary>A query failure whose contract error code is known where it is raised.</summary>
internal sealed class QueryFailure(ErpAgentErrorCode code, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public ErpAgentErrorCode Code { get; } = code;

    /// <summary>The failure is ERP's own message box (its text is in the message).</summary>
    public bool FromErpMessage { get; init; }
}

/// <summary>
/// A job handed out by CY Web's claim endpoint. For includeHistory=true CY Web
/// resolves both dates in advance and supplies historyFrom/historyTo as yyyy-MM-dd.
/// Only the local desktop query is allowed to leave ERP date inputs untouched.
/// </summary>
internal sealed record ErpAgentJob(string Id, string ItemNo, bool IncludeHistory = false, string? HistoryFrom = null, string? HistoryTo = null)
{
    /// <summary>queryType ITEM_NAME: a 品名 search for <see cref="Keyword"/> (user, 2026-10-16: CY Web only sends the 品名).</summary>
    public const string NameSearch = "ITEM_NAME";

    public string QueryType { get; init; } = "ITEM_NO";
    public string? Keyword { get; init; }
    public bool IsNameSearch => QueryType == NameSearch;

    /// <summary>What the job asks for, for status lines.</summary>
    public string Display => IsNameSearch ? $"品名「{Keyword}」" : ItemNo;

    /// <summary>
    /// The query options of this job; a bad date or range throws <see cref="ArgumentException"/>
    /// (reported as FAILED, the job is not left hanging).
    /// </summary>
    public ItemQueryOptions Options(DateOnly today)
    {
        if (!IncludeHistory) return ItemQueryOptions.Default;
        DateOnly Date(string? text, string what) =>
            HistoryRange.ParseIso(text) ?? throw new ArgumentException($"CY Web 的歷史異動{what}日缺少或格式不正確。");
        return new ItemQueryOptions(HistoryRange.Resolve(Date(HistoryFrom, "起"), Date(HistoryTo, "迄"), today));
    }
}

/// <summary>
/// JSON shapes of the CY Web ERP item query Agent API (contract:
/// chihyuan-web docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md). Pure, so it is unit
/// tested on any OS.
/// </summary>
internal static class ErpAgentProtocol
{
    public const string ClaimPath = "api/erp-agent/item-queries/claim";
    public static string CompletePath(string jobId) => $"api/erp-agent/item-queries/{Uri.EscapeDataString(jobId)}/complete";

    public static string ErpState(Invi02Status status) => status switch
    {
        Invi02Status.Browse => "BROWSE",
        Invi02Status.Editing => "EDITING",
        Invi02Status.DialogOpen => "DIALOG_OPEN",
        Invi02Status.Multiple => "MULTIPLE",
        Invi02Status.NotOpen => "NOT_OPEN",
        _ => "UNKNOWN"
    };

    public static string ClaimBody(string agentVersion, string erpState) =>
        new JsonObject { ["agentVersion"] = agentVersion, ["erpState"] = erpState }.ToJsonString();

    /// <summary>The job in a claim response, or null for <c>{job:null}</c>.</summary>
    public static ErpAgentJob? ParseClaim(string json)
    {
        var data = Data(json);
        if (data["job"] is not JsonObject job) return null;
        var id = job["id"]?.GetValue<string>();
        if (OptionalString(job["queryType"]) == ErpAgentJob.NameSearch)
        {
            var keyword = OptionalString(job["keyword"]);
            if (string.IsNullOrEmpty(id) || keyword is null)
                throw new InvalidDataException("CY Web claim 回應缺少 id／keyword。");
            return new ErpAgentJob(id, string.Empty) { QueryType = ErpAgentJob.NameSearch, Keyword = keyword };
        }
        var itemNo = job["itemNo"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id) || itemNo is null)
            throw new InvalidDataException("CY Web claim 回應缺少 id／itemNo。");
        // Options are read leniently: a wrong type here must not lose the claimed job.
        var includeHistory = job["includeHistory"] is JsonValue h && h.TryGetValue<bool>(out var b) && b;
        return new ErpAgentJob(id, itemNo, includeHistory, OptionalString(job["historyFrom"]), OptionalString(job["historyTo"]));
    }

    private static string? OptionalString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>品名 search rows sent at most (ERP's own 總資料筆數限制 is 500 by default).</summary>
    public const int MaxSearchItems = 500;

    /// <summary>
    /// A 品名 search: FOUND with the listed items, or NOT_FOUND when none matched. Each item has
    /// the <see cref="ItemFieldCatalog.SearchList"/> fields; fields／warehouses stay empty.
    /// </summary>
    public static string SearchBody(ItemSearchResult result, string agentVersion)
    {
        var found = result.Items.Count > 0;
        var body = CompleteObject(found ? "FOUND" : "NOT_FOUND", [], [], null, (long)result.Elapsed.TotalMilliseconds, agentVersion);
        body["keyword"] = result.Keyword;
        body["items"] = new JsonArray(result.Items.Take(MaxSearchItems).Select(row => (JsonNode)Fields(row)).ToArray());
        body["total"] = result.Items.Count;
        body["truncated"] = result.Truncated || result.Items.Count > MaxSearchItems;
        return body.ToJsonString(JsonOptions);
    }

    /// <summary>History rows sent at most (newest kept); the rest is counted in total.</summary>
    public const int MaxHistoryRows = 2000;
    /// <summary>Batches sent at most, the same bound as 庫別.</summary>
    public const int MaxBatchRows = 100;

    public static string FoundBody(ItemQueryResult result, string agentVersion)
    {
        var body = CompleteObject(
            "FOUND",
            Fields(result.Fields),
            new JsonArray(result.Warehouses.Select(row => (JsonNode)Fields(row)).ToArray()),
            null, (long)result.Elapsed.TotalMilliseconds, agentVersion);
        body["batchManaged"] = result.BatchManaged;
        body["batches"] = new JsonArray(result.Batches.Take(MaxBatchRows).Select(row => (JsonNode)Fields(row)).ToArray());
        body["history"] = History(result);
        return body.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 查歷史異動 as columns + rows (a year can hold hundreds of rows; name/value pairs per cell
    /// would multiply the size), newest rows kept when there are more than <see cref="MaxHistoryRows"/>.
    /// </summary>
    private static JsonNode? History(ItemQueryResult result)
    {
        if (result.HistoryRange is not { } range) return null;
        // A Web history job must always have both dates supplied by CY Web.
        if (range.From is null || range.To is null)
            throw new InvalidOperationException("CY Web 歷史異動工作沒有完整日期，不能回傳不一致的歷史結果。");
        var columns = ItemFieldCatalog.History.Select(c => c.Label).ToList();
        var rows = result.History.Skip(Math.Max(0, result.History.Count - MaxHistoryRows))
            .Select(row => (JsonNode)new JsonArray(columns.Select(c => (JsonNode?)(row.FirstOrDefault(f => f.Name == c)?.Value ?? string.Empty)).ToArray()))
            .ToArray();
        return new JsonObject
        {
            ["from"] = HistoryRange.Iso(range.From),
            ["to"] = HistoryRange.Iso(range.To),
            ["columns"] = new JsonArray(columns.Select(c => (JsonNode?)c).ToArray()),
            ["rows"] = new JsonArray(rows),
            ["total"] = result.History.Count,
            ["truncated"] = result.History.Count > MaxHistoryRows
        };
    }

    public static string NotFoundBody(long elapsedMs, string agentVersion) =>
        Complete("NOT_FOUND", [], [], null, elapsedMs, agentVersion);

    public static string FailedBody(ErpAgentErrorCode code, long elapsedMs, string agentVersion) =>
        Complete("FAILED", [], [], code, elapsedMs, agentVersion);

    /// <summary>
    /// Contract code for a failed query. The message itself is never sent: CY Web stores a
    /// fixed Chinese text per code, so local paths or ERP text cannot leak.
    /// </summary>
    public static ErpAgentErrorCode Classify(Exception ex) => ex switch
    {
        QueryFailure failure => failure.Code,
        OperationCanceledException => ErpAgentErrorCode.CANCELLED,
        ArgumentException => ErpAgentErrorCode.INTERNAL,
        InvalidDataException => ErpAgentErrorCode.EXPORT_FAILED,
        _ => ErpAgentErrorCode.INTERNAL
    };

    /// <summary>The error code of a failed CY Web response envelope, or the HTTP status when unreadable.</summary>
    public static string ErrorCode(string json, int httpStatus)
    {
        try
        {
            return JsonNode.Parse(json)?["error"]?["code"]?.GetValue<string>() ?? $"HTTP_{httpStatus}";
        }
        catch (JsonException)
        {
            return $"HTTP_{httpStatus}";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Complete(string outcome, JsonArray fields, JsonArray warehouses, ErpAgentErrorCode? code, long elapsedMs, string agentVersion) =>
        CompleteObject(outcome, fields, warehouses, code, elapsedMs, agentVersion).ToJsonString(JsonOptions);

    private static JsonObject CompleteObject(string outcome, JsonArray fields, JsonArray warehouses, ErpAgentErrorCode? code, long elapsedMs, string agentVersion) =>
        new JsonObject
        {
            ["outcome"] = outcome,
            ["fields"] = fields,
            ["warehouses"] = warehouses,
            ["errorCode"] = code?.ToString(),
            ["errorMessage"] = null,
            ["elapsedMs"] = Math.Clamp(elapsedMs, 0, 3_600_000),
            ["agentVersion"] = agentVersion
        };

    private static JsonArray Fields(IReadOnlyList<ErpField> fields) =>
        new(fields.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["value"] = f.Value }).ToArray());

    private static JsonObject Data(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("CY Web 回應不是 JSON 物件。");
        if (root["ok"]?.GetValue<bool>() != true) throw new InvalidDataException("CY Web 回應 ok 不是 true。");
        return root["data"] as JsonObject ?? throw new InvalidDataException("CY Web 回應缺少 data。");
    }
}
