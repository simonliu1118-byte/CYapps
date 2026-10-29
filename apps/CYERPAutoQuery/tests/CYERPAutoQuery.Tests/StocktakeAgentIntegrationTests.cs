using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CYERPAutoQuery.Tests;

/// <summary>Full Agent claim-to-complete roundtrip, using synthetic CY Web only.</summary>
public sealed class StocktakeAgentIntegrationTests
{
    private const string JobId = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private sealed class Web : HttpMessageHandler
    {
        internal readonly List<(string Path,string Body)> Requests=[];
        internal string Job = "{}";
        private int _claims;
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            var body=request.Content is null?"":await request.Content.ReadAsStringAsync(ct);
            Requests.Add((path,body));
            var json=path.EndsWith("/claim",StringComparison.Ordinal)
                ? _claims++==0
                    ? "{\"ok\":true,\"data\":{\"job\":"+Job+"},\"requestId\":\"t\"}"
                    : "{\"ok\":true,\"data\":{\"job\":null},\"requestId\":\"t\"}"
                : "{\"ok\":true,\"data\":{},\"requestId\":\"t\"}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {Content=new StringContent(json,Encoding.UTF8,"application/json")};
        }
    }
    private static async Task<(Web Web,int Calls)> RunAsync(
        string job,Func<ErpAgentJob,(bool Found,string Body)>? execute)
    {
        var web=new Web {Job=job};
        using var client=new ErpAgentClient(new Uri("https://cyweb.example.test/"),
            "secret-token-0123456789-abcdefghijklmnop",web);
        using var stop=new CancellationTokenSource();
        int delays=0,calls=0;
        var loop=new ErpAgentLoop(client,"V1.3.0",()=>"BROWSE",()=>false,
            (j,ct)=>throw new InvalidOperationException("stocktake must never run as item"),
            (s,t)=>{},
            (span,ct)=>{if(++delays>=2)stop.Cancel();return Task.CompletedTask;},
            runStocktake:execute is null?null:(j,ct)=>
            {
                calls++;
                return Task.FromResult(execute(j));
            });
        await loop.RunAsync(stop.Token);
        return (web,calls);
    }

    [Fact]
    public async Task SyncClaimUsesDedicatedRunnerAndReturnsContractBody()
    {
        var job="{\"id\":\""+JobId+"\",\"queryType\":\"STOCKTAKE_SYNC\",\"skipSheets\":[],\"onlySheet\":null}";
        var (web,calls)=await RunAsync(job,j=>
        {
            Assert.Equal(ErpAgentJob.StocktakeSyncType,j.QueryType);
            var sync=new StocktakeSyncResult(
                [new StocktakeSheet("990001",new DateOnly(2026,10,29))],
                [new StocktakeDetail("990001",[new StocktakeLine("00001","X0001","測試品","","假倉","","盒","B1","2")])],TimeSpan.Zero);
            return (true,ErpAgentProtocol.StocktakeSyncBody(sync,"V1.3.0"));
        });
        Assert.Equal(1,calls);
        var complete=web.Requests.Single(x=>x.Path.EndsWith("/complete",StringComparison.Ordinal));
        var payload=JsonNode.Parse(complete.Body)!;
        Assert.Equal("FOUND",payload["outcome"]!.GetValue<string>());
        Assert.Equal("990001",payload["details"]![0]!["sheetNo"]!.GetValue<string>());
    }

    [Fact]
    public async Task WriteWithoutRunnerReportsFailed()
    {
        var job="{\"id\":\""+JobId+"\",\"queryType\":\"STOCKTAKE_WRITE\",\"sheetNo\":\"990001\",\"rows\":[{\"key\":\"00001\",\"seq\":\"00001\",\"itemCode\":\"X0001\",\"batch\":\"B1\",\"quantity\":\"3\"}]}";
        var (web,calls)=await RunAsync(job,null);
        Assert.Equal(0,calls);
        var complete=web.Requests.Single(x=>x.Path.EndsWith("/complete",StringComparison.Ordinal));
        Assert.Equal("FAILED",JsonNode.Parse(complete.Body)!["outcome"]!.GetValue<string>());
    }
}
