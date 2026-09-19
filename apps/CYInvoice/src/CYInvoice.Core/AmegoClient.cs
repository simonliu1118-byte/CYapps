using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CYInvoice.Core.Amego;

public sealed class AmegoClient : IAmegoGateway
{
    private const int ApiResponseLimit = 8 * 1024 * 1024;
    private const int PdfResponseLimit = 20 * 1024 * 1024;
    private const string InvoiceFileHost = "invoice.amego.tw";
    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> now;
    private long clockOffsetSeconds;

    public AmegoClient(string invoice, string appKey, HttpClient? httpClient = null, Func<DateTimeOffset>? now = null)
    {
        Invoice = invoice.Trim();
        AppKey = appKey.Trim();
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public string BaseUrl { get; set; } = AmegoDefaults.BaseUrl;
    public string Invoice { get; }
    public string AppKey { get; }

    public async Task<IssueResponse> IssueAsync(IssueRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId)) throw new ArgumentException("OrderId is required", nameof(request));
        if (request.ProductItems.Count == 0) throw new ArgumentException("ProductItem is required", nameof(request));
        using var document = await PostAsync("/json/f0401", request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        var result = root;
        if (Text(root, "invoice_number").Length == 0 && root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
        {
            if (data.ValueKind == JsonValueKind.Array)
            {
                if (data.GetArrayLength() > 0) result = data[0];
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                result = data;
            }
        }

        var number = Text(result, "invoice_number");
        return new IssueResponse(code, message, number, Int64(result, "invoice_time"), Text(result, "random_number"));
    }

    public async Task<VoidResponse> VoidAsync(VoidRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancelInvoiceNumber = request.CancelInvoiceNumber.Trim();
        request.CancelReason = request.CancelReason.Trim();
        if (request.CancelInvoiceNumber.Length is < 1 or > 10)
            throw new ArgumentException("CancelInvoiceNumber is required and cannot exceed 10 characters", nameof(request));
        if (request.CancelReason.EnumerateRunes().Count() > 20)
            throw new ArgumentException("CancelReason cannot exceed 20 characters", nameof(request));

        using var document = await PostAsync("/json/f0501", new[] { request }, cancellationToken).ConfigureAwait(false);
        var (code, message) = ReadEnvelope(document.RootElement);
        return new VoidResponse(code, message);
    }

    public Task<QueryResponse> QueryByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) =>
        QueryAsync("order", orderId.Trim(), string.Empty, cancellationToken);

    public Task<QueryResponse> QueryByInvoiceNumberAsync(string number, CancellationToken cancellationToken = default) =>
        QueryAsync("invoice", string.Empty, number.Trim(), cancellationToken);

    public async Task<InvoiceListResponse> ListInvoicesAsync(
        DateOnly startDate,
        DateOnly endDate,
        int page = 1,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        if (startDate > endDate) throw new ArgumentOutOfRangeException(nameof(startDate), "start date cannot be later than end date");
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "page must be at least 1");
        if (limit is < 20 or > 500) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 20 and 500");
        var request = new Dictionary<string, object>
        {
            ["date_select"] = 1,
            ["date_start"] = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["date_end"] = endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["limit"] = limit,
            ["page"] = page,
        };
        using var document = await PostAsync("/json/invoice_list", request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        var results = new List<InvoiceListItem>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invoice_list data contains a non-object item");
                var number = Text(item, "invoice_number");
                if (number.Length == 0) throw new InvalidDataException("invoice_list item is missing invoice_number");
                results.Add(new InvoiceListItem(
                    number,
                    Text(item, "invoice_type"),
                    Int32(item, "invoice_status"),
                    Scalar(item, "invoice_date"),
                    Scalar(item, "invoice_time"),
                    Text(item, "buyer_identifier"),
                    Text(item, "buyer_name"),
                    Scalar(item, "sales_amount"),
                    Scalar(item, "tax_amount"),
                    Scalar(item, "total_amount"),
                    Text(item, "main_remark"),
                    Text(item, "carrier_type"),
                    Text(item, "carrier_id1"),
                    Text(item, "carrier_id2"),
                    Text(item, "npoban"),
                    Int64(item, "cancel_date"),
                    Text(item, "order_id"),
                    Int64(item, "create_date")));
            }
        }
        else if (data.ValueKind != JsonValueKind.Undefined && data.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException("invoice_list data is not an array");
        }
        return new InvoiceListResponse(
            code,
            message,
            Int32(root, "page_total"),
            Int32(root, "page_now"),
            Int32(root, "data_total"),
            results);
    }

    public async Task<StatusResponse> StatusAsync(IEnumerable<string> invoiceNumbers, CancellationToken cancellationToken = default)
    {
        var request = invoiceNumbers.Select(number => number.Trim()).Where(number => number.Length != 0)
            .Select(number => new Dictionary<string, string> { ["InvoiceNumber"] = number }).ToArray();
        if (request.Length == 0) throw new ArgumentException("at least one invoice number is required", nameof(invoiceNumbers));
        using var document = await PostAsync("/json/invoice_status", request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        var results = new List<StatusResult>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            results.AddRange(data.EnumerateArray().Select(item => ParseStatus(item, string.Empty)));
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            results.AddRange(data.EnumerateObject().Select(item => ParseStatus(item.Value, item.Name)));
        }
        return new StatusResponse(code, message, results);
    }

    public async Task<BanResponse> QueryBanAsync(IEnumerable<string> bans, CancellationToken cancellationToken = default)
    {
        var request = bans.Select(ban => ban.Trim()).Where(ban => ban.Length != 0)
            .Select(ban => new Dictionary<string, string> { ["ban"] = ban }).ToArray();
        if (request.Length == 0) throw new ArgumentException("at least one BAN is required", nameof(bans));
        using var document = await PostAsync("/json/ban_query", request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        var results = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(item => new BanResult(Text(item, "ban"), Text(item, "name"))).ToArray()
            : [];
        return new BanResponse(code, message, results);
    }

    public async Task<byte[]> DownloadInvoicePdfAsync(
        string invoiceNumber,
        int downloadStyle,
        CancellationToken cancellationToken = default)
    {
        invoiceNumber = invoiceNumber.Trim();
        if (invoiceNumber.Length is < 1 or > 10)
            throw new ArgumentException("invoice_number is required and cannot exceed 10 characters", nameof(invoiceNumber));
        if (downloadStyle is not (0 or 1 or 2 or 3 or 5))
            throw new ArgumentOutOfRangeException(nameof(downloadStyle), "unsupported AMEGO invoice download style");

        using var document = await PostAsync(
            "/json/invoice_file",
            new Dictionary<string, object>
            {
                ["type"] = "invoice",
                ["invoice_number"] = invoiceNumber,
                ["download_style"] = downloadStyle,
            },
            cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("API returned success without invoice file data");
        var fileUrl = Text(data, "file_url");
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, InvoiceFileHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0)
            throw new InvalidDataException("API returned an untrusted invoice file URL");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, PdfResponseLimit, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"invoice PDF HTTP {(int)response.StatusCode}", null, response.StatusCode);
        ValidatePdf(body);
        return body;
    }

    private async Task<QueryResponse> QueryAsync(string type, string orderId, string invoiceNumber, CancellationToken cancellationToken)
    {
        if (type == "order" && orderId.Length == 0) throw new ArgumentException("order_id is required", nameof(orderId));
        if (type == "invoice" && invoiceNumber.Length == 0) throw new ArgumentException("invoice_number is required", nameof(invoiceNumber));
        var request = new Dictionary<string, string> { ["type"] = type };
        if (orderId.Length != 0) request["order_id"] = orderId;
        if (invoiceNumber.Length != 0) request["invoice_number"] = invoiceNumber;
        using var document = await PostAsync("/json/invoice_query", request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var (code, message) = ReadEnvelope(root);
        ThrowResponseError(code, message);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new InvalidDataException("API returned success without query data");
        var all = new List<QueryResult>();
        ParseQueryData(data, all);
        var matched = all.Where(item => type == "order"
            ? string.Equals(item.OrderId.Trim(), orderId, StringComparison.Ordinal)
            : string.Equals(item.InvoiceNumber.Trim(), invoiceNumber, StringComparison.Ordinal)).ToArray();
        if (matched.Length == 0) throw new InvalidDataException("API query data does not contain the requested invoice");
        if (matched.Length > 1) throw new InvalidDataException("API query data contains more than one matching invoice");
        return new QueryResponse(code, message, matched[0]);
    }

    private async Task<JsonDocument> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        if (Invoice.Length == 0 || AppKey.Length == 0) throw new InvalidOperationException("invoice and App Key are required");
        var data = JsonSerializer.Serialize(payload);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var body = await PostSignedAsync(path, data, cancellationToken).ConfigureAwait(false);
            JsonDocument document;
            try { document = JsonDocument.Parse(body); }
            catch (JsonException error) { throw new InvalidDataException("decode API response", error); }
            if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == 15 && attempt == 0)
            {
                document.Dispose();
                await SynchronizeServerTimeAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            return document;
        }
        throw new InvalidDataException("光貿 API 時間校正後仍無法完成請求");
    }

    private async Task<byte[]> PostSignedAsync(string path, string data, CancellationToken cancellationToken)
    {
        var timestamp = (now().ToUnixTimeSeconds() + Interlocked.Read(ref clockOffsetSeconds)).ToString(CultureInfo.InvariantCulture);
        var signatureInput = Encoding.UTF8.GetBytes(data + timestamp + AppKey);
#pragma warning disable CA5351 // AMEGO's documented signing protocol requires MD5.
        var signature = Convert.ToHexString(MD5.HashData(signatureInput)).ToLowerInvariant();
#pragma warning restore CA5351
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["invoice"] = Invoice, ["data"] = data, ["time"] = timestamp, ["sign"] = signature,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, ApiResponseLimit, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API HTTP {(int)response.StatusCode}: {Encoding.UTF8.GetString(body).Trim()}", null, response.StatusCode);
        return body;
    }

    private async Task SynchronizeServerTimeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/json/time"));
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("timestamp", out var timestamp) ||
            !long.TryParse(ScalarText(timestamp), NumberStyles.None, CultureInfo.InvariantCulture, out var serverUnix) || serverUnix <= 0)
            throw new InvalidDataException("server time response contains invalid timestamp");
        Interlocked.Exchange(ref clockOffsetSeconds, serverUnix - now().ToUnixTimeSeconds());
    }

    private string Url(string path) => BaseUrl.TrimEnd('/') + path;

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > limit) throw new InvalidDataException("API response exceeds size limit");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }

    internal static void ValidatePdf(ReadOnlySpan<byte> body)
    {
        if (body.Length < 8 || !body[..5].SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("downloaded invoice file is not a valid PDF");
    }

    private static void ParseQueryData(JsonElement value, List<QueryResult> results)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) ParseQueryData(item, results);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("unsupported query data shape");
        var number = Text(value, "invoice_number");
        var order = Text(value, "order_id");
        if (number.Length == 0 && order.Length == 0) throw new InvalidDataException("API query object is missing invoice_number and order_id");
        var detailPresent = value.TryGetProperty("detail_vat", out var detail) || value.TryGetProperty("DetailVat", out detail);
        var query = new QueryResult(number, FirstText(value, "type", "invoice_type"), FirstInt(value, "status", "invoice_status"),
            FirstScalar(value, "date", "invoice_date"), FirstScalar(value, "time", "invoice_time"),
            Text(value, "buyer_identifier"), Text(value, "buyer_name"), Scalar(value, "sales_amount"), Scalar(value, "tax_amount"),
            Scalar(value, "total_amount"), Text(value, "carrier_type"), Text(value, "carrier_id1"), Text(value, "carrier_id2"),
            Text(value, "npoban"), Int64(value, "cancel_date"), order, Int64(value, "create_date"),
            value.TryGetProperty("product_item", out var products) ? products.Clone() : default,
            detailPresent ? detail.GetInt32() : 0, detailPresent, HasPendingVoid(value));
        results.Add(query with { Allowances = ParseAllowances(value) });
    }

    private static IReadOnlyList<InvoiceAllowanceResult> ParseAllowances(JsonElement value)
    {
        if (!value.TryGetProperty("allowance", out var allowances) ||
            allowances.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (allowances.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("invoice_query allowance is not an array");

        var results = new List<InvoiceAllowanceResult>();
        foreach (var item in allowances.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("invoice_query allowance contains a non-object item");
            results.Add(new InvoiceAllowanceResult(
                FirstText(item, "invoice_type", "type"),
                FlexibleInt32(item, "invoice_status"),
                FlexibleInt32(item, "allowance_type"),
                Text(item, "allowance_number"),
                Scalar(item, "allowance_date"),
                Scalar(item, "tax_amount"),
                Scalar(item, "total_amount")));
        }
        return results;
    }

    private static bool HasPendingVoid(JsonElement value)
    {
        if (!value.TryGetProperty("wait", out var wait) || wait.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in wait.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var type = FirstText(item, "invoice_type", "type");
            if (string.Equals(type, "C0501", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static StatusResult ParseStatus(JsonElement value, string fallbackNumber) =>
        new(Text(value, "invoice_number") is { Length: > 0 } number ? number : fallbackNumber,
            Text(value, "type"), Int32(value, "status"), Scalar(value, "total_amount"));

    private static (int Code, string Message) ReadEnvelope(JsonElement root) => (Int32(root, "code"), Text(root, "msg"));
    private static void ThrowResponseError(int code, string message)
    {
        if (code != 0) throw new AmegoApiException(code, message.Trim().Length == 0 ? "unknown API error" : message);
    }
    private static string FirstText(JsonElement value, string first, string second) => Text(value, first) is { Length: > 0 } text ? text : Text(value, second);
    private static string FirstScalar(JsonElement value, string first, string second) => Scalar(value, first) is { Length: > 0 } text ? text : Scalar(value, second);
    private static int FirstInt(JsonElement value, string first, string second) => Int32(value, first) is var number && number != 0 ? number : Int32(value, second);
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? string.Empty : string.Empty;
    private static string Scalar(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? ScalarText(property) : string.Empty;
    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText().Trim(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => throw new InvalidDataException("expected string or number"),
    };
    private static int FlexibleInt32(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric)) return numeric;
        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var text))
            return text;
        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return 0;
        throw new InvalidDataException($"{name} must be an integer");
    }
    private static int Int32(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? result : 0;
    private static long Int64(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetInt64(out var result) ? result : 0;
}
