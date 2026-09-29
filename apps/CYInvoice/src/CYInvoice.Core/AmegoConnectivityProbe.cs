using System.Globalization;
using System.Text.Json;

namespace CYInvoice.Core.Amego;

public sealed class AmegoConnectivityProbe
{
    private readonly HttpClient httpClient;
    private readonly string baseUrl;

    public AmegoConnectivityProbe(HttpClient? httpClient = null, string? baseUrl = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        this.baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? AmegoDefaults.BaseUrl : baseUrl.TrimEnd('/');
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/json/time");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("timestamp", out var timestamp) ||
            !long.TryParse(Scalar(timestamp), NumberStyles.None, CultureInfo.InvariantCulture, out var serverUnix) ||
            serverUnix <= 0)
        {
            throw new InvalidDataException("光貿 API 時間服務回覆格式錯誤");
        }
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty,
    };
}
