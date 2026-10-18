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
    public const string ItemQuery = "ITEM_NO";
    // ERP 客戶查詢 and ERP 未收款查詢 (CY Web, user 2026-10-17).
    public const string CustomerQuery = "CUSTOMER_NO";
    public const string CustomerSearch = "CUSTOMER_NAME";
    // ERP 供應商查詢 (PURI01, user 2026-10-17).
    public const string SupplierQuery = "SUPPLIER_NO";
    public const string SupplierSearch = "SUPPLIER_NAME";
    /// <summary>A supplier's 進貨單 (PURR18, user 2026-10-18).</summary>
    public const string PurchaseQueryType = "PURCHASES";
    public const string SalesQueryType = "SALES";
    public const string ReceivableQueryType = "RECEIVABLE";
    /// <summary>One 銷貨單／銷退單 by number (a 未收款 憑證, user 2026-10-17).</summary>
    public const string OrderQueryType = "ORDER";
    /// <summary>Every query type this Agent runs; sent with each claim so CY Web only hands those out.</summary>
    public static readonly IReadOnlyList<string> SupportedTypes =
        [ItemQuery, NameSearch, CustomerQuery, CustomerSearch, SupplierQuery, SupplierSearch, PurchaseQueryType, SalesQueryType, ReceivableQueryType, OrderQueryType];

    public string QueryType { get; init; } = ItemQuery;
    public string? Keyword { get; init; }
    public bool IsNameSearch => QueryType == NameSearch;
    /// <summary>A customer, supplier, 銷貨銷退 or 未收款 job (not the item queries).</summary>
    public bool IsBusiness => QueryType is CustomerQuery or CustomerSearch or SupplierQuery or SupplierSearch
        or PurchaseQueryType or SalesQueryType or ReceivableQueryType or OrderQueryType;

    /// <summary>CUSTOMER_*／SUPPLIER_*: whose form is queried.</summary>
    public ErpParty Party => QueryType is SupplierQuery or SupplierSearch ? ErpParty.Supplier : ErpParty.Customer;

    /// <summary>ITEM_NO: the member may see 標準進價 (ERP 商品成本); not sent means not allowed.</summary>
    public bool IncludeCost { get; init; }
    /// <summary>CUSTOMER_NO／SALES: the customer.</summary>
    public string? CustomerCode { get; init; }
    /// <summary>SUPPLIER_NO／PURCHASES: the supplier.</summary>
    public string? SupplierCode { get; init; }
    /// <summary>The code of a CUSTOMER_NO／SUPPLIER_NO job.</summary>
    public string? PartyCode => QueryType == SupplierQuery ? SupplierCode : CustomerCode;
    /// <summary>SALES／PURCHASES: the 單據日期 range CY Web resolved (yyyy-MM-dd), echoed back unchanged.</summary>
    public string? DateFrom { get; init; }
    public string? DateTo { get; init; }
    /// <summary>RECEIVABLE: CUSTOMER／SALESPERSON／ALL and the code (null for ALL).</summary>
    public string? Scope { get; init; }
    public string? Code { get; init; }
    /// <summary>ORDER: the 銷貨單／銷退單 number, echoed back unchanged.</summary>
    public string? OrderNo { get; init; }

    /// <summary>What the job asks for, for status lines.</summary>
    public string Display => QueryType switch
    {
        NameSearch => $"品名「{Keyword}」",
        CustomerQuery => $"客戶 {CustomerCode}",
        CustomerSearch => $"客戶名稱「{Keyword}」",
        SupplierQuery => $"廠商 {SupplierCode}",
        SupplierSearch => $"廠商名稱「{Keyword}」",
        PurchaseQueryType => $"廠商 {SupplierCode} 進貨退貨單（{DateFrom}～{DateTo}）",
        SalesQueryType => $"客戶 {CustomerCode} 銷貨銷退單（{DateFrom}～{DateTo}）",
        ReceivableQueryType => Scope == "ALL" ? "全部未收款" : $"{(Scope == "CUSTOMER" ? "客戶" : "業務員")} {Code} 未收款",
        OrderQueryType => $"單號 {OrderNo}",
        _ => ItemNo
    };

    /// <summary>The SALES request; a bad code or date throws <see cref="ArgumentException"/> (reported as FAILED).</summary>
    public SalesQuery Sales(DateOnly today)
    {
        DateOnly Date(string? text, string what) =>
            HistoryRange.ParseIso(text) ?? throw new ArgumentException($"CY Web 的單據日期{what}日缺少或格式不正確。");
        return SalesQuery.Resolve(CustomerCode ?? string.Empty, Date(DateFrom, "起"), Date(DateTo, "迄"), today);
    }

    /// <summary>The PURCHASES request; a bad code or date throws <see cref="ArgumentException"/> (reported as FAILED).</summary>
    public PurchaseQuery Purchases(DateOnly today)
    {
        DateOnly Date(string? text, string what) =>
            HistoryRange.ParseIso(text) ?? throw new ArgumentException($"CY Web 的單據日期{what}日缺少或格式不正確。");
        return PurchaseQuery.Resolve(SupplierCode ?? string.Empty, Date(DateFrom, "起"), Date(DateTo, "迄"), today);
    }

    /// <summary>The RECEIVABLE request; an unknown scope or bad code throws <see cref="ArgumentException"/>.</summary>
    public ReceivableQuery Receivable() => ReceivableQuery.Resolve(Scope switch
    {
        "CUSTOMER" => ReceivableScope.Customer,
        "SALESPERSON" => ReceivableScope.Salesperson,
        "ALL" => ReceivableScope.All,
        _ => throw new ArgumentException("CY Web 的未收款查詢範圍不正確。")
    }, Code ?? string.Empty);

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
        new JsonObject
        {
            ["agentVersion"] = agentVersion,
            ["erpState"] = erpState,
            ["queryTypes"] = new JsonArray(ErpAgentJob.SupportedTypes.Select(t => (JsonNode?)t).ToArray())
        }.ToJsonString();

    /// <summary>The job in a claim response, or null for <c>{job:null}</c>.</summary>
    public static ErpAgentJob? ParseClaim(string json)
    {
        var data = Data(json);
        if (data["job"] is not JsonObject job) return null;
        var id = job["id"]?.GetValue<string>();
        var type = OptionalString(job["queryType"]) ?? ErpAgentJob.ItemQuery;
        string Required(string name) => string.IsNullOrEmpty(id) || OptionalString(job[name]) is not { } value
            ? throw new InvalidDataException($"CY Web claim 回應缺少 id／{name}。")
            : value;
        switch (type)
        {
            case ErpAgentJob.NameSearch or ErpAgentJob.CustomerSearch or ErpAgentJob.SupplierSearch:
                return new ErpAgentJob(id!, string.Empty) { QueryType = type, Keyword = Required("keyword") };
            case ErpAgentJob.CustomerQuery:
                return new ErpAgentJob(id!, string.Empty) { QueryType = type, CustomerCode = Required("customerCode") };
            case ErpAgentJob.SupplierQuery:
                return new ErpAgentJob(id!, string.Empty) { QueryType = type, SupplierCode = Required("supplierCode") };
            case ErpAgentJob.SalesQueryType:
                // Dates are checked when the job runs, so a bad one is reported as FAILED, not lost.
                return new ErpAgentJob(id!, string.Empty)
                {
                    QueryType = type, CustomerCode = Required("customerCode"),
                    DateFrom = OptionalString(job["dateFrom"]), DateTo = OptionalString(job["dateTo"])
                };
            case ErpAgentJob.PurchaseQueryType:
                return new ErpAgentJob(id!, string.Empty)
                {
                    QueryType = type, SupplierCode = Required("supplierCode"),
                    DateFrom = OptionalString(job["dateFrom"]), DateTo = OptionalString(job["dateTo"])
                };
            case ErpAgentJob.ReceivableQueryType:
                return new ErpAgentJob(id!, string.Empty) { QueryType = type, Scope = Required("scope"), Code = OptionalString(job["code"]) };
            case ErpAgentJob.OrderQueryType:
                return new ErpAgentJob(id!, string.Empty) { QueryType = type, OrderNo = Required("orderNo") };
            case ErpAgentJob.ItemQuery:
                break;
            default:
                throw new InvalidDataException($"CY Web claim 回應的查詢類型「{type}」不支援。");
        }
        var itemNo = job["itemNo"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id) || itemNo is null)
            throw new InvalidDataException("CY Web claim 回應缺少 id／itemNo。");
        // Options are read leniently: a wrong type here must not lose the claimed job.
        var includeHistory = job["includeHistory"] is JsonValue h && h.TryGetValue<bool>(out var b) && b;
        // 標準進價 only when CY Web says the member may see it.
        var includeCost = job["includeCost"] is JsonValue c && c.TryGetValue<bool>(out var cost) && cost;
        return new ErpAgentJob(id, itemNo, includeHistory, OptionalString(job["historyFrom"]), OptionalString(job["historyTo"])) { IncludeCost = includeCost };
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

    /// <summary>Item fields only sent when the CY Web member may see them (user, 2026-10-17: 標準進價).</summary>
    public static readonly IReadOnlySet<string> CostFields = new HashSet<string>(["標準進價"]);

    public static string FoundBody(ItemQueryResult result, string agentVersion, bool includeCost)
    {
        var body = CompleteObject(
            "FOUND",
            Fields(includeCost ? result.Fields : result.Fields.Where(f => !CostFields.Contains(f.Name)).ToList()),
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

    // ---- ERP 客戶／供應商／未收款查詢 (CY Web, user 2026-10-17) ---------------------------

    public const int MaxOrders = 1000, MaxOrderLines = 500, MaxBills = 3000, MaxVouchers = 500;

    private static string Text(decimal value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>CUSTOMER_NO／SUPPLIER_NO: the fields, or NOT_FOUND; customerCode／supplierCode is echoed either way.</summary>
    public static string PartyBody(ErpAgentJob job, PartyQueryResult result, string agentVersion)
    {
        var body = CompleteObject(result.Found ? "FOUND" : "NOT_FOUND", result.Found ? Fields(result.Fields) : [], [], null,
            (long)result.Elapsed.TotalMilliseconds, agentVersion);
        if (job.QueryType == ErpAgentJob.SupplierQuery) body["supplierCode"] = job.SupplierCode;
        else body["customerCode"] = job.CustomerCode;
        return body.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// PURCHASES: the 進貨單 and 退貨單 (kind 進貨／退貨) newest first (at most <see cref="MaxOrders"/>, each at most
    /// <see cref="MaxOrderLines"/> lines); the job's supplier and dates echoed. Amounts are 原幣.
    /// </summary>
    public static string PurchaseBody(ErpAgentJob job, PurchaseQueryResult result, string agentVersion)
    {
        var orders = result.Orders.Take(MaxOrders).Select(o => (JsonNode)new JsonObject
        {
            ["kind"] = o.Kind, ["orderNo"] = o.OrderNo, ["date"] = Iso(o.Date), ["supplierCode"] = o.SupplierCode, ["supplierName"] = o.SupplierName,
            ["supplierOrderNo"] = o.SupplierOrderNo, ["invoiceNo"] = o.InvoiceNo, ["invoiceType"] = o.InvoiceType,
            ["taxType"] = o.TaxType, ["currency"] = o.Currency, ["totalQuantity"] = Text(o.TotalQuantity),
            ["amount"] = Text(o.Amount), ["deduction"] = Text(o.Deduction), ["tax"] = Text(o.Tax), ["total"] = Text(o.Total),
            ["settled"] = o.Settled,
            ["lines"] = new JsonArray(o.Lines.Take(MaxOrderLines).Select(l => (JsonNode)new JsonObject
            {
                ["seq"] = l.Seq, ["itemCode"] = l.ItemCode, ["itemName"] = l.ItemName, ["specification"] = l.Specification,
                ["acceptDate"] = l.AcceptDate is { } d ? Iso(d) : string.Empty, ["warehouse"] = l.Warehouse,
                ["quantity"] = Text(l.Quantity), ["acceptedQuantity"] = Text(l.AcceptedQuantity), ["returnedQuantity"] = Text(l.ReturnedQuantity),
                ["unit"] = l.Unit, ["unitPrice"] = Text(l.UnitPrice), ["amount"] = Text(l.Amount), ["tax"] = Text(l.Tax),
                ["batch"] = l.Batch, ["purchaseOrderNo"] = l.PurchaseOrderNo, ["note"] = l.Note, ["sourceOrder"] = l.SourceOrder
            }).ToArray())
        }).ToArray();
        var body = CompleteObject(result.Found ? "FOUND" : "NOT_FOUND", [], [], null, (long)result.Elapsed.TotalMilliseconds, agentVersion);
        body["supplierCode"] = job.SupplierCode;
        body["dateFrom"] = job.DateFrom;
        body["dateTo"] = job.DateTo;
        body["orders"] = new JsonArray(orders);
        body["total"] = result.Orders.Count;
        body["truncated"] = result.Orders.Count > MaxOrders;
        return body.ToJsonString(JsonOptions);
    }

    /// <summary>SALES／ORDER: the orders newest first (at most <see cref="MaxOrders"/>); the job's customer and dates echoed.</summary>
    public static string SalesBody(ErpAgentJob job, SalesQueryResult result, string agentVersion)
    {
        var orders = result.Orders.Take(MaxOrders).Select(o => (JsonNode)new JsonObject
        {
            ["kind"] = o.Kind, ["orderNo"] = o.OrderNo, ["date"] = Iso(o.Date),
            ["customerCode"] = o.CustomerCode, ["customerName"] = o.CustomerName,
            ["salesperson"] = o.Salesperson, ["department"] = o.Department, ["paymentTerm"] = o.PaymentTerm,
            ["taxType"] = o.TaxType, ["invoiceType"] = o.InvoiceType,
            ["totalQuantity"] = Text(o.TotalQuantity), ["amount"] = Text(o.Amount), ["tax"] = Text(o.Tax), ["total"] = Text(o.Total),
            ["note"] = o.Note, ["reason"] = o.Reason,
            ["lines"] = new JsonArray(o.Lines.Take(MaxOrderLines).Select(l => (JsonNode)new JsonObject
            {
                ["seq"] = l.Seq, ["itemCode"] = l.ItemCode, ["itemName"] = l.ItemName, ["specification"] = l.Specification,
                ["warehouse"] = l.Warehouse, ["quantity"] = Text(l.Quantity), ["unit"] = l.Unit,
                ["unitPrice"] = Text(l.UnitPrice), ["amount"] = Text(l.Amount), ["giftType"] = l.GiftType,
                ["giftQuantity"] = Text(l.GiftQuantity), ["batch"] = l.Batch, ["note"] = l.Note, ["sourceOrder"] = l.SourceOrder
            }).ToArray())
        }).ToArray();
        var body = CompleteObject(result.Found ? "FOUND" : "NOT_FOUND", [], [], null, (long)result.Elapsed.TotalMilliseconds, agentVersion);
        // ORDER echoes its number, SALES its customer and dates.
        if (job.QueryType == ErpAgentJob.OrderQueryType) body["orderNo"] = job.OrderNo;
        else
        {
            body["customerCode"] = job.CustomerCode;
            body["dateFrom"] = job.DateFrom;
            body["dateTo"] = job.DateTo;
        }
        body["orders"] = new JsonArray(orders);
        body["total"] = result.Orders.Count;
        body["truncated"] = result.Orders.Count > MaxOrders;
        return body.ToJsonString(JsonOptions);
    }

    /// <summary>RECEIVABLE: the 結帳單 in ERP's order (at most <see cref="MaxBills"/>); the job's scope and code echoed.</summary>
    public static string ReceivableBody(ErpAgentJob job, ReceivableResult result, string agentVersion)
    {
        var bills = result.Bills.Take(MaxBills).Select(b => (JsonNode)new JsonObject
        {
            ["customerCode"] = b.CustomerCode, ["customerName"] = b.CustomerName, ["date"] = Iso(b.Date), ["billNo"] = b.BillNo,
            ["salespersonCode"] = b.SalespersonCode, ["salespersonName"] = b.SalespersonName, ["invoiceNo"] = b.InvoiceNo,
            ["currency"] = b.Currency, ["receivable"] = Text(b.Receivable), ["receivableTax"] = Text(b.ReceivableTax),
            ["unpaid"] = Text(b.Unpaid),
            ["vouchers"] = new JsonArray(b.Vouchers.Take(MaxVouchers).Select(v => (JsonNode)new JsonObject
            {
                ["voucherNo"] = v.VoucherNo, ["date"] = v.Date is { } d ? Iso(d) : string.Empty,
                ["amount"] = Text(v.Amount), ["tax"] = Text(v.Tax)
            }).ToArray())
        }).ToArray();
        var body = CompleteObject(result.Found ? "FOUND" : "NOT_FOUND", [], [], null, (long)result.Elapsed.TotalMilliseconds, agentVersion);
        body["scope"] = job.Scope;
        body["code"] = job.Scope == "ALL" ? null : job.Code;
        body["bills"] = new JsonArray(bills);
        body["total"] = result.Bills.Count;
        body["truncated"] = result.Bills.Count > MaxBills;
        return body.ToJsonString(JsonOptions);
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
    /// <summary>CY Web's error message (it names the rejected field), or empty.</summary>
    public static string ErrorMessage(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>() ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

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
