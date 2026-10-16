using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>
/// Opt-in: the Agent against a real CY Web Worker (for example chihyuan-web `wrangler dev` with
/// local D1 holding one QUEUED fake job). Skipped unless CYQ_LIVE_BASE and CYQ_LIVE_TOKEN are set.
/// </summary>
public class LiveCyWebTests
{
    [Fact]
    public async Task AgentClaimsAndReportsAgainstARealWorker()
    {
        var baseUrl = Environment.GetEnvironmentVariable("CYQ_LIVE_BASE");
        var token = Environment.GetEnvironmentVariable("CYQ_LIVE_TOKEN");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) return;

        using var client = new ErpAgentClient(new Uri(baseUrl), token);
        var queried = new List<string>();
        var states = new List<(ErpAgentState, string)>();
        using var cts = new CancellationTokenSource();
        var loop = new ErpAgentLoop(client, "live-test", () => "BROWSE", () => false,
            (job, _) =>
            {
                var code = job.ItemNo;
                queried.Add(code);
                return Task.FromResult(new ItemQueryResult(code,
                    ItemFieldCatalog.Header.Select(c => new ErpField(c.Label, c.Label == "品號" ? code : "假" + c.Label)).ToList(),
                    [ItemFieldCatalog.Warehouse.Select(c => new ErpField(c.Label, "W")).ToList()],
                    TimeSpan.FromMilliseconds(1234)));
            },
            (state, text) => { states.Add((state, text)); if (state == ErpAgentState.Online && queried.Count > 0) cts.Cancel(); },
            (span, ct) => Task.Delay(TimeSpan.FromMilliseconds(200), ct));
        try { await loop.RunAsync(cts.Token); } catch (OperationCanceledException) { }

        Assert.NotEmpty(queried);
        Assert.Contains(states, s => s.Item2.Contains("已回報（FOUND）"));
    }
}
