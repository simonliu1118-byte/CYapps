using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// The Agent side of the CY Web ERP item query contract (chihyuan-web #169,
/// docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md), against a fake CY Web.
/// </summary>
public class ErpAgentProtocolTests
{
    // Contract field names (chihyuan-web shared/erp-item-query.ts ERP_ITEM_FIELDS /
    // ERP_WAREHOUSE_FIELDS). CY Web rejects any other name.
    private static readonly string[] ContractItemFields =
    [
        "品號", "品名", "規格", "單位", "庫存數量", "商品分類", "商品分類名稱",
        "商品描述", "條碼編號", "批號管理", "有效天數", "主供應商",
        "主供應商名稱", "標準進價", "標準售價", "售價定價一", "售價定價二",
        "售價定價三", "售價定價四", "售價定價五", "售價定價六",
    ];
    private static readonly string[] ContractWarehouseFields =
        ["庫別", "庫別名稱", "儲位", "安全存量", "經濟批量", "上次盤點日", "最近入庫日", "最近出庫日"];

    [Fact]
    public void CatalogMatchesTheWebContract()
    {
        Assert.Equal(ContractItemFields, ItemFieldCatalog.Header.Select(c => c.Label));
        Assert.Equal(ContractWarehouseFields, ItemFieldCatalog.Warehouse.Select(c => c.Label));
    }

    [Fact]
    public void ClaimBodyCarriesVersionAndState()
    {
        var body = JsonNode.Parse(ErpAgentProtocol.ClaimBody("V0.1.0 Build 4", ErpAgentProtocol.ErpState(Invi02Status.Browse)))!;
        Assert.Equal("V0.1.0 Build 4", body["agentVersion"]!.GetValue<string>());
        Assert.Equal("BROWSE", body["erpState"]!.GetValue<string>());
    }

    [Fact]
    public void ParsesClaimWithAndWithoutJob()
    {
        var job = ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":{"id":"0f8fad5b-d9cb-469f-a165-70867728950e","itemNo":"T0001","expiresAt":"x"}},"requestId":"r"}""");
        Assert.Equal(new ErpAgentJob("0f8fad5b-d9cb-469f-a165-70867728950e", "T0001"), job);
        Assert.Null(ErpAgentProtocol.ParseClaim("""{"ok":true,"data":{"job":null},"requestId":"r"}"""));
        Assert.Throws<InvalidDataException>(() => ErpAgentProtocol.ParseClaim("""{"ok":false,"error":{"code":"X"},"requestId":"r"}"""));
    }

    [Fact]
    public void FoundBodyUsesContractShape()
    {
        var result = new ItemQueryResult("T0001",
            [new ErpField("品號", "T0001"), new ErpField("商品描述", "第一行\n第二行")],
            [[new ErpField("庫別", "W01"), new ErpField("儲位", "A-01")]],
            TimeSpan.FromMilliseconds(14230));
        var body = JsonNode.Parse(ErpAgentProtocol.FoundBody(result, "v", includeCost: true))!;
        Assert.Equal("FOUND", body["outcome"]!.GetValue<string>());
        Assert.Equal("第一行\n第二行", body["fields"]![1]!["value"]!.GetValue<string>());
        Assert.Equal("儲位", body["warehouses"]![0]![1]!["name"]!.GetValue<string>());
        Assert.Equal(14230, body["elapsedMs"]!.GetValue<long>());
        Assert.Null(body["errorCode"]);
        Assert.Null(body["errorMessage"]);
    }

    [Fact]
    public void NotFoundAndFailedBodiesCarryNoFields()
    {
        var notFound = JsonNode.Parse(ErpAgentProtocol.NotFoundBody(900, "v"))!;
        Assert.Equal("NOT_FOUND", notFound["outcome"]!.GetValue<string>());
        Assert.Empty(notFound["fields"]!.AsArray());
        Assert.Empty(notFound["warehouses"]!.AsArray());

        var failed = JsonNode.Parse(ErpAgentProtocol.FailedBody(ErpAgentErrorCode.EXPORT_FAILED, 500, "v"))!;
        Assert.Equal("FAILED", failed["outcome"]!.GetValue<string>());
        Assert.Equal("EXPORT_FAILED", failed["errorCode"]!.GetValue<string>());
        Assert.Null(failed["errorMessage"]); // local messages never leave the PC
    }

    [Fact]
    public void ClassifiesFailures()
    {
        Assert.Equal(ErpAgentErrorCode.ERP_NOT_OPEN, ErpAgentProtocol.Classify(new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "x")));
        Assert.Equal(ErpAgentErrorCode.CANCELLED, ErpAgentProtocol.Classify(new OperationCanceledException()));
        Assert.Equal(ErpAgentErrorCode.EXPORT_FAILED, ErpAgentProtocol.Classify(new InvalidDataException()));
        Assert.Equal(ErpAgentErrorCode.INTERNAL, ErpAgentProtocol.Classify(new InvalidOperationException()));
    }

    [Fact]
    public void ReadsErrorCodeFromEnvelope()
    {
        Assert.Equal("QUERY_NOT_RUNNING", ErpAgentProtocol.ErrorCode("""{"ok":false,"error":{"code":"QUERY_NOT_RUNNING","message":"m"},"requestId":"r"}""", 409));
        Assert.Equal("HTTP_502", ErpAgentProtocol.ErrorCode("<html>", 502));
    }

    [Fact]
    public void RejectsPlainHttpExceptLocalhost()
    {
        Assert.Throws<ArgumentException>(() => new ErpAgentClient(new Uri("http://cyweb.example.test"), new string('t', 43)));
        using var local = new ErpAgentClient(new Uri("http://localhost:8787"), new string('t', 43));
    }
}

public class ErpAgentLoopTests
{
    private const string JobId = "0f8fad5b-d9cb-469f-a165-70867728950e";

    /// <summary>A scripted CY Web: each claim takes the next response; completes are recorded.</summary>
    private sealed class FakeCyWeb : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Claims = new();
        public readonly List<(string Path, string Body, string? Auth)> Requests = [];
        public HttpStatusCode CompleteStatus = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString()));
            if (request.RequestUri.AbsolutePath.EndsWith("/claim"))
                return Claims.Count > 0 ? Claims.Dequeue()() : Json(HttpStatusCode.OK, """{"ok":true,"data":{"job":null},"requestId":"r"}""");
            return Json(CompleteStatus, CompleteStatus == HttpStatusCode.OK
                ? $$"""{"ok":true,"data":{"id":"{{JobId}}","status":"DONE","completedAt":"x"},"requestId":"r"}"""
                : """{"ok":false,"error":{"code":"QUERY_NOT_RUNNING","message":"m"},"requestId":"r"}""");
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage Job(string itemNo) =>
            Json(HttpStatusCode.OK, $$$"""{"ok":true,"data":{"job":{"id":"{{{JobId}}}","itemNo":"{{{itemNo}}}","expiresAt":"x"}},"requestId":"r"}""");
    }

    private sealed class Harness
    {
        public readonly FakeCyWeb Web = new();
        public readonly List<TimeSpan> Delays = [];
        public readonly List<ErpAgentState> States = [];
        public readonly List<string> Queried = [];
        public readonly List<ErpAgentJob> Jobs = [];
        public Func<string, ItemQueryResult> Query = code => new ItemQueryResult(code, [new ErpField("品號", code)], [], TimeSpan.FromSeconds(1));
        public bool LocalBusy;
        public int StopAfterDelays = 3;
        private readonly CancellationTokenSource _cts = new();

        public async Task RunAsync()
        {
            using var client = new ErpAgentClient(new Uri("https://cyweb.example.test/"), "secret-token-0123456789-abcdefghijklmnop", Web);
            var loop = new ErpAgentLoop(client, "V0.1.0 Build 4", () => "BROWSE", () => LocalBusy,
                (job, _) => { Queried.Add(job.ItemNo); Jobs.Add(job); return Task.FromResult(Query(job.ItemNo)); },
                (state, _) => States.Add(state),
                (span, _) =>
                {
                    Delays.Add(span);
                    if (Delays.Count >= StopAfterDelays) _cts.Cancel();
                    return Task.CompletedTask;
                });
            await loop.RunAsync(_cts.Token);
        }
    }

    [Fact]
    public async Task ClaimsRunsAndReportsFound()
    {
        var h = new Harness();
        h.Web.Claims.Enqueue(() => FakeCyWeb.Job("T0001"));
        await h.RunAsync();

        Assert.Equal(["T0001"], h.Queried);
        var complete = h.Web.Requests.Single(r => r.Path.EndsWith("/complete"));
        Assert.Equal($"/api/erp-agent/item-queries/{JobId}/complete", complete.Path);
        Assert.Equal("FOUND", JsonNode.Parse(complete.Body)!["outcome"]!.GetValue<string>());
        Assert.All(h.Web.Requests, r => Assert.Equal("Bearer secret-token-0123456789-abcdefghijklmnop", r.Auth));
        Assert.Contains(ErpAgentState.Querying, h.States);
        Assert.All(h.Delays, d => Assert.Equal(ErpAgentLoop.IdleInterval, d));
    }

    [Fact]
    public async Task ReportsNotFoundAndFailures()
    {
        var h = new Harness { Query = code => code == "T0404" ? ItemQueryResult.NotFound(code, TimeSpan.Zero) : throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "C:\\secret\\path") };
        h.Web.Claims.Enqueue(() => FakeCyWeb.Job("T0404"));
        h.Web.Claims.Enqueue(() => FakeCyWeb.Job("T0500"));
        await h.RunAsync();

        var completes = h.Web.Requests.Where(r => r.Path.EndsWith("/complete")).Select(r => JsonNode.Parse(r.Body)!).ToList();
        Assert.Equal("NOT_FOUND", completes[0]["outcome"]!.GetValue<string>());
        Assert.Equal("FAILED", completes[1]["outcome"]!.GetValue<string>());
        Assert.Equal("ERP_STATE", completes[1]["errorCode"]!.GetValue<string>());
        Assert.DoesNotContain("secret", h.Web.Requests[^1].Body + string.Join("", h.Web.Requests.Select(r => r.Body)));
    }

    [Fact]
    public async Task UnauthorizedStopsTheAgent()
    {
        var h = new Harness { StopAfterDelays = 100 };
        h.Web.Claims.Enqueue(() => FakeCyWeb.Json(HttpStatusCode.Unauthorized, """{"ok":false,"error":{"code":"AGENT_UNAUTHORIZED","message":"m"},"requestId":"r"}"""));
        await h.RunAsync();

        Assert.Equal(ErpAgentState.Unauthorized, h.States[^1]);
        Assert.Single(h.Web.Requests);
        Assert.Empty(h.Queried);
    }

    [Fact]
    public async Task NetworkErrorsBackOffUpToThirtySeconds()
    {
        var h = new Harness { StopAfterDelays = 6 };
        for (var i = 0; i < 6; i++) h.Web.Claims.Enqueue(() => throw new HttpRequestException("down"));
        await h.RunAsync();

        Assert.Equal(new[] { 3, 6, 12, 24, 30, 30 }.Select(s => TimeSpan.FromSeconds(s)), h.Delays);
        Assert.All(h.States.Skip(1).SkipLast(1), s => Assert.Equal(ErpAgentState.Offline, s));
        Assert.Equal(ErpAgentState.Stopped, h.States[^1]);
    }

    [Fact]
    public async Task DoesNotClaimWhileALocalQueryRuns()
    {
        var h = new Harness { LocalBusy = true };
        await h.RunAsync();
        Assert.Empty(h.Web.Requests);
    }

    [Fact]
    public async Task RejectedReportDoesNotStopTheAgent()
    {
        var h = new Harness();
        h.Web.CompleteStatus = HttpStatusCode.Conflict; // CY Web already timed the job out
        h.Web.Claims.Enqueue(() => FakeCyWeb.Job("T0001"));
        await h.RunAsync();

        Assert.Contains(ErpAgentState.Offline, h.States);
        Assert.True(h.Web.Requests.Count(r => r.Path.EndsWith("/claim")) >= 2);
    }
}
