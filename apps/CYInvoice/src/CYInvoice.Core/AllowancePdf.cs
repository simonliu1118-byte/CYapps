using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Storage;

namespace CYInvoice.Core.Invoicing;

public sealed record AllowancePdfStyle(int Code, string Name);

public static class AllowancePdfStyles
{
    public static readonly AllowancePdfStyle A4 = new(0, "A4 整張");
    public static readonly AllowancePdfStyle AddressA5 = new(1, "A4 (地址+A5)");
    public static readonly AllowancePdfStyle A5 = new(3, "A5");
    public static readonly IReadOnlyList<AllowancePdfStyle> Official = [A4, AddressA5, A5];

    public static AllowancePdfStyle Require(int code) =>
        Official.FirstOrDefault(style => style.Code == code)
        ?? throw new ArgumentOutOfRangeException(nameof(code), "unsupported AMEGO allowance download style");
}

public sealed record AllowancePdfDocument(
    string Path,
    string AllowanceNumber,
    AllowancePdfStyle Style,
    bool FromCache);

public sealed class AllowancePdfService
{
    private const int ApiResponseLimit = 8 * 1024 * 1024;
    private const int PdfResponseLimit = 20 * 1024 * 1024;
    private const string FileHost = "invoice.amego.tw";
    private readonly LocalRepository repository;
    private readonly HttpClient httpClient;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim gate = new(1, 1);
    private long clockOffsetSeconds;

    public AllowancePdfService(
        LocalRepository repository,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? now = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<AllowancePdfDocument> GetAsync(
        string allowanceNumber,
        int downloadStyle,
        CancellationToken cancellationToken = default)
    {
        allowanceNumber = (allowanceNumber ?? string.Empty).Trim();
        if (allowanceNumber.Length is < 1 or > 16)
            throw new InvalidOperationException("折讓單號不可空白且不可超過 16 字");
        var style = AllowancePdfStyles.Require(downloadStyle);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = repository.Settings.LoadOrCreate();
            var environment = settings.Environment;
            var invoice = environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
            var appKey = environment == Environments.Test ? AmegoDefaults.TestAppKey : repository.Settings.ProductionAppKey(settings);
            if (invoice.Length == 0 || appKey.Length == 0)
                throw new InvalidOperationException("目前環境缺少公司統編或 App Key，無法取得折讓 PDF");

            var cacheDirectory = Path.Combine(repository.CacheDirectory, "AllowancePDF");
            Directory.CreateDirectory(cacheDirectory);
            var cacheVersion = OfficialAllowanceCacheVersion(environment, invoice, allowanceNumber);
            var cachePath = Path.Combine(
                cacheDirectory,
                $"{Safe(environment)}_{Safe(invoice)}_{Safe(allowanceNumber)}_{style.Code}_{cacheVersion}.pdf");
            PruneObsoleteCacheVersions(cacheDirectory, environment, invoice, allowanceNumber, cacheVersion);
            if (await TryReadPdfAsync(cachePath, cancellationToken).ConfigureAwait(false) is not null)
                return new AllowancePdfDocument(cachePath, allowanceNumber, style, FromCache: true);

            var fileUrl = await GetFileUrlAsync(invoice, appKey, allowanceNumber, style.Code, cancellationToken).ConfigureAwait(false);
            var bytes = await DownloadPdfAsync(fileUrl, cancellationToken).ConfigureAwait(false);
            await WriteAtomicallyAsync(cachePath, bytes, cancellationToken).ConfigureAwait(false);
            return new AllowancePdfDocument(cachePath, allowanceNumber, style, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    private string OfficialAllowanceCacheVersion(string environment, string sellerInvoice, string allowanceNumber)
    {
        var official = repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment.Trim(), environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 ||
                             string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .SelectMany(InvoiceAllowanceMetadata.ReadOfficial)
            .Where(item => string.Equals(
                item.AllowanceNumber.Trim(),
                allowanceNumber,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.AllowanceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AllowanceDate, StringComparer.Ordinal)
            .ThenBy(item => item.InvoiceType, StringComparer.Ordinal)
            .ThenBy(item => item.InvoiceStatus)
            .ThenBy(item => item.AllowanceType)
            .ThenBy(item => item.TaxAmount, StringComparer.Ordinal)
            .ThenBy(item => item.TotalAmount, StringComparer.Ordinal)
            .Select(item => new
            {
                item.AllowanceNumber,
                item.AllowanceDate,
                item.InvoiceType,
                item.InvoiceStatus,
                item.AllowanceType,
                item.TaxAmount,
                item.TotalAmount,
            })
            .ToArray();
        var serialized = JsonSerializer.Serialize(official);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(digest).ToLowerInvariant()[..16];
    }

    private static void PruneObsoleteCacheVersions(
        string cacheDirectory,
        string environment,
        string sellerInvoice,
        string allowanceNumber,
        string currentVersion)
    {
        var prefix = $"{Safe(environment)}_{Safe(sellerInvoice)}_{Safe(allowanceNumber)}_";
        var currentSuffix = "_" + currentVersion + ".pdf";
        foreach (var path in Directory.EnumerateFiles(cacheDirectory, prefix + "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.EndsWith(currentSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A stale cache file must never block opening the current official version.
                // Because the official-data fingerprint is part of the active path, this file
                // cannot be reused even when Windows temporarily prevents its deletion.
            }
        }
    }

    private async Task<Uri> GetFileUrlAsync(
        string invoice,
        string appKey,
        string allowanceNumber,
        int downloadStyle,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["allowance_number"] = allowanceNumber,
            ["download_style"] = downloadStyle,
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var body = await PostSignedAsync(
                "/json/allowance_file",
                invoice,
                appKey,
                data,
                cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = ReadInt32(root, "code");
            var message = ReadText(root, "msg");
            if (code == 15 && attempt == 0)
            {
                await SynchronizeServerTimeAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (code != 0)
                throw new AmegoApiException(code, message.Length == 0 ? "unknown API error" : message);
            if (!root.TryGetProperty("data", out var responseData) || responseData.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("API returned success without allowance file data");
            var fileUrl = ReadText(responseData, "file_url");
            if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, FileHost, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                uri.UserInfo.Length != 0)
                throw new InvalidDataException("API returned an untrusted allowance file URL");
            return uri;
        }
        throw new InvalidDataException("光貿 API 時間校正後仍無法取得折讓 PDF");
    }

    private async Task<byte[]> PostSignedAsync(
        string path,
        string invoice,
        string appKey,
        string data,
        CancellationToken cancellationToken)
    {
        var timestamp = (now().ToUnixTimeSeconds() + Interlocked.Read(ref clockOffsetSeconds))
            .ToString(CultureInfo.InvariantCulture);
        var signatureInput = Encoding.UTF8.GetBytes(data + timestamp + appKey);
#pragma warning disable CA5351 // AMEGO's documented signing protocol requires MD5.
        var signature = Convert.ToHexString(MD5.HashData(signatureInput)).ToLowerInvariant();
#pragma warning restore CA5351
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["invoice"] = invoice,
            ["data"] = data,
            ["time"] = timestamp,
            ["sign"] = signature,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, AmegoDefaults.BaseUrl.TrimEnd('/') + path)
        {
            Content = content,
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, ApiResponseLimit, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"API HTTP {(int)response.StatusCode}: {Encoding.UTF8.GetString(body).Trim()}",
                null,
                response.StatusCode);
        return body;
    }

    private async Task SynchronizeServerTimeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AmegoDefaults.BaseUrl.TrimEnd('/') + "/json/time");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("timestamp", out var timestamp) ||
            !long.TryParse(Scalar(timestamp), NumberStyles.None, CultureInfo.InvariantCulture, out var serverUnix) ||
            serverUnix <= 0)
            throw new InvalidDataException("server time response contains invalid timestamp");
        Interlocked.Exchange(ref clockOffsetSeconds, serverUnix - now().ToUnixTimeSeconds());
    }

    private async Task<byte[]> DownloadPdfAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var body = await ReadLimitedAsync(response.Content, PdfResponseLimit, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"allowance PDF HTTP {(int)response.StatusCode}", null, response.StatusCode);
        AmegoClient.ValidatePdf(body);
        return body;
    }

    private static async Task<byte[]?> TryReadPdfAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            AmegoClient.ValidatePdf(bytes);
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(path); } catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

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

    private static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private static int ReadInt32(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) ? result : 0;

    private static string ReadText(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText().Trim(),
        _ => string.Empty,
    };
}
