using System.Text.Json;
using System.Text.Json.Serialization;

namespace CYInvoice.Core.AutoInput;

// Wire contract only. The actual AMEGO operation must be owned by the existing InvoiceService.
public static class AutoInputContract
{
    public const string RequestSchema = "cyinvoice-autoinput-request/1";
    public const string ResultSchema = "cyinvoice-autoinput-result/1";
    public const int MaxRequestBytes = 1024 * 1024;
    public const int MaxOrderCount = 10000;

    public static AutoInputRequest ParseRequest(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxRequestBytes)
            throw new InvalidDataException("AutoInput request is too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("AutoInput request must be an object.");
        string Required(string key)
        {
            if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("AutoInput request missing string: " + key);
            return value.GetString() ?? string.Empty;
        }

        if (Required("schema") != RequestSchema)
            throw new InvalidDataException("Unsupported AutoInput request schema.");
        var requestId = Required("requestId");
        if (!Guid.TryParseExact(requestId, "D", out _))
            throw new InvalidDataException("Invalid AutoInput requestId.");
        var source = Required("source");
        if (source is not ("mo" or "coupang"))
            throw new InvalidDataException("Unsupported AutoInput source.");
        var file = Required("file");
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            throw new InvalidDataException("AutoInput file must be an absolute path.");

        List<string>? ids = null;
        if (root.TryGetProperty("orderIds", out var orders))
        {
            if (orders.ValueKind != JsonValueKind.Array || orders.GetArrayLength() is < 1 or > MaxOrderCount)
                throw new InvalidDataException("Invalid AutoInput orderIds.");
            ids = new List<string>(orders.GetArrayLength());
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in orders.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("AutoInput orderId must be a string.");
                var id = element.GetString() ?? string.Empty;
                if (id.Length == 0 || id.Length > 128 || !seen.Add(id))
                    throw new InvalidDataException("AutoInput orderId is blank, duplicated or too long.");
                ids.Add(id);
            }
        }
        return new AutoInputRequest(requestId, source, file, ids);
    }
}

public sealed record AutoInputRequest(
    string RequestId,
    string Source,
    string File,
    IReadOnlyList<string>? OrderIds);

public sealed record AutoInputOrderResult(
    string OrderId,
    string Status,
    string InvoiceNumber = "",
    string InvoiceDate = "",
    string BuyerBan = "",
    string BuyerName = "",
    long TotalAmount = 0,
    string Message = "");

public sealed record AutoInputResult(
    string Schema,
    string RequestId,
    string CyinvoiceVersion,
    string Environment,
    string FinishedAt,
    IReadOnlyList<AutoInputOrderResult> Orders);
