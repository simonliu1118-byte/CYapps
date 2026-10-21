using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CYERPAutoQuery;

/// <summary>Where the Agent stands, for the status strip.</summary>
internal enum ErpAgentState { Stopped, Connecting, Online, Querying, Unauthorized, Offline }

/// <summary>Calls the two Agent endpoints of CY Web with the Agent's Bearer token.</summary>
internal sealed class ErpAgentClient : IDisposable
{
    private readonly HttpClient _http;

    public ErpAgentClient(Uri baseUrl, string token, HttpMessageHandler? handler = null)
    {
        if (baseUrl.Scheme != Uri.UriSchemeHttps && !baseUrl.IsLoopback)
            throw new ArgumentException("CY Web 網址必須是 https。", nameof(baseUrl));
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ErpAgentJob?> ClaimAsync(string agentVersion, string erpState, CancellationToken cancellationToken)
    {
        var json = await PostAsync(ErpAgentProtocol.ClaimPath, ErpAgentProtocol.ClaimBody(agentVersion, erpState), cancellationToken);
        return ErpAgentProtocol.ParseClaim(json);
    }

    public Task CompleteAsync(string jobId, string body, CancellationToken cancellationToken) =>
        PostAsync(ErpAgentProtocol.CompletePath(jobId), body, cancellationToken);

    public Task ProgressAsync(string jobId, string body, CancellationToken cancellationToken) =>
        PostAsync(ErpAgentProtocol.ProgressPath(jobId), body, cancellationToken);

    private async Task<string> PostAsync(string path, string body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ErpAgentHttpException(response.StatusCode, ErpAgentProtocol.ErrorCode(text, (int)response.StatusCode),
                ErpAgentProtocol.ErrorMessage(text));
        return text;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class ErpAgentHttpException(HttpStatusCode status, string code, string detail = "")
    : Exception($"CY Web 回應 {(int)status} {code}")
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
    /// <summary>CY Web's message, e.g. which field of a result it refused.</summary>
    public string Detail { get; } = detail;
}

/// <summary>
/// The Agent: claims a job from CY Web every few seconds, runs it through the same query path
/// as the local window, and reports the outcome (contract: chihyuan-web
/// ERP_ITEM_QUERY_BRIDGE_CONTRACT.md). One job at a time; while a local query runs it does
/// not claim. Network errors back off up to 30 s; a rejected token stops the Agent.
/// </summary>
internal sealed class ErpAgentLoop(
    ErpAgentClient client,
    string agentVersion,
    Func<string> erpState,
    Func<bool> localBusy,
    Func<ErpAgentJob, CancellationToken, Task<ItemQueryResult>> runQuery,
    Action<ErpAgentState, string> report,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<ErpAgentJob, CancellationToken, Task<ItemSearchResult>>? runSearch = null,
    Func<ErpAgentJob, CancellationToken, Task<(bool Found, string Body)>>? runBusiness = null)
{
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>Runs until cancelled or the token is rejected.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = IdleInterval;
        report(ErpAgentState.Connecting, "連線 CY Web 中…");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (localBusy())
            {
                await _delay(IdleInterval, cancellationToken);
                continue;
            }

            ErpAgentJob? job;
            try
            {
                job = await client.ClaimAsync(agentVersion, erpState(), cancellationToken);
                backoff = IdleInterval;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ErpAgentHttpException ex) when (ex.Status == HttpStatusCode.Unauthorized)
            {
                report(ErpAgentState.Unauthorized, "CY Web 拒絕連線憑證，請重新設定 Token。");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or ErpAgentHttpException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException)
            {
                report(ErpAgentState.Offline, $"CY Web 連線失敗（{Describe(ex)}），{backoff.TotalSeconds:0} 秒後重試。");
                await _delay(backoff, cancellationToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                continue;
            }

            if (job is null)
            {
                report(ErpAgentState.Online, "CY Web 已連線，等待查詢。");
                await _delay(IdleInterval, cancellationToken);
                continue;
            }

            await RunJobAsync(job, cancellationToken);
        }
        report(ErpAgentState.Stopped, "Web 連線已停止。");
    }

    private async Task RunJobAsync(ErpAgentJob job, CancellationToken cancellationToken)
    {
        report(ErpAgentState.Querying, $"Web 查詢：{job.Display}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string body;
        string outcome;
        try
        {
            if (job.IsBusiness)
            {
                // Customer, supplier, 銷貨銷退 and 未收款 jobs build their own complete body (ErpAgentProtocol).
                if (runBusiness is null) throw new NotSupportedException("客戶／廠商／銷貨銷退／未收款查詢未接上。");
                var reply = await runBusiness(job, cancellationToken);
                body = reply.Body;
                outcome = reply.Found ? "FOUND" : "NOT_FOUND";
            }
            else if (job.IsNameSearch)
            {
                if (runSearch is null) throw new NotSupportedException("品名查詢未接上。");
                var search = await runSearch(job, cancellationToken);
                body = ErpAgentProtocol.SearchBody(search, agentVersion);
                outcome = search.Items.Count > 0 ? "FOUND" : "NOT_FOUND";
            }
            else
            {
                var result = await runQuery(job, cancellationToken);
                body = result.Found
                    ? ErpAgentProtocol.FoundBody(result, agentVersion, job.IncludeCost)
                    : ErpAgentProtocol.NotFoundBody(watch.ElapsedMilliseconds, agentVersion);
                outcome = result.Found ? "FOUND" : "NOT_FOUND";
            }
        }
        catch (Exception ex)
        {
            // Esc, or the Agent being stopped mid-query, is reported as CANCELLED.
            var code = ErpAgentProtocol.Classify(ex);
            body = ErpAgentProtocol.FailedBody(code, watch.ElapsedMilliseconds, agentVersion);
            outcome = code.ToString();
        }

        try
        {
            // Reported even when the Agent is being stopped, so the Web user is not left waiting.
            await CompleteWithRetryAsync(job.Id, body);
            report(ErpAgentState.Online, $"Web 查詢 {job.Display} 已回報（{outcome}）。");
        }
        catch (ErpAgentHttpException ex) when (ex.Status == HttpStatusCode.BadRequest && outcome != ErpAgentErrorCode.INTERNAL.ToString())
        {
            // CY Web refused the result (contract drift, or ERP data it does not accept): report the
            // job FAILED instead, so the Web user is not left waiting until it times out.
            report(ErpAgentState.Offline, $"Web 查詢 {job.Display} 回報被 CY Web 拒絕（{Describe(ex)}），改回報查詢失敗。");
            try
            {
                using var failedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await client.CompleteAsync(job.Id, ErpAgentProtocol.FailedBody(ErpAgentErrorCode.INTERNAL, watch.ElapsedMilliseconds, agentVersion), failedTimeout.Token);
                report(ErpAgentState.Online, $"Web 查詢 {job.Display} 已回報查詢失敗。");
            }
            catch (Exception again) when (again is HttpRequestException or ErpAgentHttpException or TaskCanceledException)
            {
                report(ErpAgentState.Offline, $"Web 查詢 {job.Display} 回報失敗（{Describe(again)}）。");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or ErpAgentHttpException or TaskCanceledException)
        {
            // 409 QUERY_NOT_RUNNING: CY Web already timed the job out.
            report(ErpAgentState.Offline, $"Web 查詢 {job.Display} 回報失敗（{Describe(ex)}）。");
        }
    }

    /// <summary>
    /// The result after a query that may have taken minutes: a network error or timeout is tried
    /// twice more (V0.9.0 Build 1 LOG: a result was lost to a brief outage). CY Web's own answers
    /// (4xx) are not retried.
    /// </summary>
    private async Task CompleteWithRetryAsync(string jobId, string body)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await client.CompleteAsync(jobId, body, reportTimeout.Token);
                return;
            }
            catch (Exception ex) when (attempt < CompleteAttempts && ex is HttpRequestException or TaskCanceledException)
            {
                report(ErpAgentState.Offline, $"Web 查詢結果回報失敗（{Describe(ex)}），{attempt * 3} 秒後重試。");
                await _delay(TimeSpan.FromSeconds(attempt * 3), CancellationToken.None);
            }
        }
    }

    private const int CompleteAttempts = 3;

    private static string Describe(Exception ex) => ex switch
    {
        ErpAgentHttpException http => $"{(int)http.Status} {http.Code}{(http.Detail.Length > 0 ? "：" + http.Detail : string.Empty)}",
        TaskCanceledException => "逾時",
        _ => ex.GetType().Name
    };
}
