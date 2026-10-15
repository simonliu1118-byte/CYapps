using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CYERPAutoInput;

/// <summary>One order of CYInvoice's result.json (schema cyinvoice-autoinput-result/1).</summary>
internal sealed record CyInvoiceOrder(
    string OrderId,
    string Status,
    string InvoiceNumber,
    string InvoiceDate,
    string BuyerBan,
    string BuyerName,
    long TotalAmount,
    string Message,
    string PdfFile);

internal sealed record CyInvoiceResult(string RequestId, string Version, string Environment, IReadOnlyList<CyInvoiceOrder> Orders)
{
    public bool IsProduction => Environment == CyInvoiceLink.Production;
    public string EnvironmentName => CyInvoiceLink.EnvironmentName(Environment);
}

/// <summary>
/// The CYInvoice AutoInput contract (CYInvoice PR #445, README「MO店+ 發票（CYInvoice）」):
/// request.json out, result.json in, and what of it may reach ERP. CYInvoice parses the MO店+
/// export itself and owns 光貿; this side only asks, checks every order and fills the preview.
/// Pure: no process or UI, unit tested.
/// </summary>
internal static class CyInvoiceLink
{
    public const string RequestSchema = "cyinvoice-autoinput-request/1";
    public const string ResultSchema = "cyinvoice-autoinput-result/1";
    public const string Production = "production";
    public const string Test = "test";

    private static readonly Regex InvoiceNumber = new("^[A-Z]{2}[0-9]{8}$", RegexOptions.CultureInvariant);

    public static string EnvironmentName(string environment) => environment switch
    {
        Production => "正式環境",
        Test => "測試環境",
        _ => $"不明環境（{environment}）"
    };

    /// <summary>CYInvoice's company PDF styles (InvoicePdfStyles; code, name); chosen in 設定 › 印表機.</summary>
    public static readonly IReadOnlyList<(int Code, string Name)> PaperStyles =
        [(0, "A4 整張"), (1, "A4（地址＋A5）"), (2, "A4（A5 內容）"), (3, "A5"), (5, "QRcode A4")];

    public static string StatusName(string status) => status switch
    {
        "issued" => "已開立",
        "existing" => "先前已開立",
        "failed" => "開立失敗",
        "unknown" => "結果不明",
        "skipped" => "查無發票／未處理",
        "ready" => "PDF 已取得",
        "pending" => "光貿尚未完成",
        _ => status
    };

    /// <summary>
    /// request.json. <paramref name="pdf"/> only for <c>--autoinput-pdf</c> (proposal in
    /// README「MO店+ 發票（CYInvoice）」): the folder for the official PDFs and the style.
    /// </summary>
    public static string Request(string requestId, string file, IReadOnlyCollection<string> orderIds, (string Folder, int Style)? pdf = null)
    {
        var request = new Dictionary<string, object>
        {
            ["schema"] = RequestSchema,
            ["requestId"] = requestId,
            ["source"] = "mo",
            ["file"] = file,
            ["orderIds"] = orderIds.ToArray()
        };
        if (pdf is { } p)
        {
            request["pdfFolder"] = p.Folder;
            request["pdfStyle"] = p.Style;
        }
        return JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>result.json for <paramref name="requestId"/>; InvalidDataException when it is not that.</summary>
    public static CyInvoiceResult ParseResult(string json, string requestId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("結果不是 JSON 物件");
        if (Text(root, "schema") != ResultSchema) throw new InvalidDataException($"結果格式不是 {ResultSchema}");
        if (Text(root, "requestId") != requestId) throw new InvalidDataException("結果的 requestId 與這次的請求不同");
        var orders = new List<CyInvoiceOrder>();
        if (root.TryGetProperty("orders", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var o in list.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object) continue;
                var amount = o.TryGetProperty("totalAmount", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt64(out var n) ? n : -1;
                orders.Add(new CyInvoiceOrder(Text(o, "orderId"), Text(o, "status"), Text(o, "invoiceNumber").Trim().ToUpperInvariant(),
                    Text(o, "invoiceDate").Trim(), Text(o, "buyerBan").Trim(), Text(o, "buyerName").Trim(), amount, Text(o, "message"), Text(o, "pdfFile").Trim()));
            }
        return new CyInvoiceResult(requestId, Text(root, "cyinvoiceVersion"), Text(root, "environment"), orders);
    }

    private static string Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    /// <summary>
    /// Why this result may not go on the order (null when it may): only issued／existing, a real
    /// invoice number and date, the MO店+ invoice total, the same 統編, and no other invoice
    /// number already on the export. Anything else is left for a person.
    /// </summary>
    public static string? Mismatch(MoOrder order, CyInvoiceOrder result)
    {
        if (result.Status is not ("issued" or "existing"))
            return $"CYInvoice：{StatusName(result.Status)}{(result.Message.Length > 0 ? $"（{result.Message}）" : string.Empty)}";
        if (!InvoiceNumber.IsMatch(result.InvoiceNumber)) return $"CYInvoice 回傳的發票號碼「{result.InvoiceNumber}」格式不對";
        if (!DateTime.TryParseExact(result.InvoiceDate, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return $"CYInvoice 回傳的發票日期「{result.InvoiceDate}」格式不對";
        if (result.TotalAmount != order.InvoiceTotal)
            return $"發票金額 {result.TotalAmount} 與 MO店+ 訂單的發票金額 {order.InvoiceTotal:0.##} 不同";
        if (!string.Equals(result.BuyerBan, order.BuyerTaxId.Trim(), StringComparison.Ordinal))
            return $"發票統編「{result.BuyerBan}」與訂單統編「{order.BuyerTaxId}」不同";
        if (order.InvoiceNo.Trim().Length > 0 && !string.Equals(order.InvoiceNo.Trim(), result.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
            return $"訂單匯出檔已有另一個發票號碼「{order.InvoiceNo}」";
        return null;
    }

    /// <summary>A checked result onto the preview's invoice fields (客戶全名 only for a buyer with 統編).</summary>
    public static void Fill(ImportedOrder imported, CyInvoiceOrder result)
    {
        imported.InvoiceNo = result.InvoiceNumber;
        imported.InvoiceDate = result.InvoiceDate;
        if (result.BuyerBan.Length > 0 && result.BuyerName.Length > 0) imported.InvoiceName = result.BuyerName;
    }

    /// <summary>A paper invoice is printed for a buyer with 統編; a consumer's goes to the member carrier (user, 2026-10-15).</summary>
    public static bool IsPaper(CyInvoiceOrder result) => result.BuyerBan.Length > 0;

    /// <summary>How long to wait for CYInvoice: a minute per order plus one (the handoff's suggestion).</summary>
    public static TimeSpan Timeout(int orders) => TimeSpan.FromSeconds(60 * Math.Max(1, orders) + 60);
}

/// <summary>
/// 紙本發票 printed (Data\paper_invoices.json, per environment and number, kept 180 days): a
/// rerun that gets <c>existing</c> back does not print the same invoice again.
/// </summary>
internal sealed class PaperInvoiceLedger
{
    private readonly string _path;
    private readonly Dictionary<string, DateTime> _printed;

    public PaperInvoiceLedger(string path)
    {
        _path = path;
        _printed = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? []
            : [];
        foreach (var old in _printed.Where(p => p.Value < DateTime.Now.AddDays(-180)).Select(p => p.Key).ToList()) _printed.Remove(old);
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "paper_invoices.json");

    private static string Key(string environment, string number) => $"{environment}:{number}";

    public bool Printed(string environment, string number) => _printed.ContainsKey(Key(environment, number));

    public void Record(string environment, string number)
    {
        _printed[Key(environment, number)] = DateTime.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_printed));
        File.Move(temp, _path, overwrite: true);
    }
}
