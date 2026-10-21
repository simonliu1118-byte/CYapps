using System.Net;
using System.Text;
using System.Text.Json;
using CYInvoice.Core.Cloud;

internal static class CloudAllowanceCollisionClientTests
{
    private const string Id="application_collision_test";
    private const string Old="A20261019001",Next="A20261019002";
    private const string Ledger="al_11111111-1111-4111-8111-111111111111";
    private const string NextLedger="al_22222222-2222-4222-8222-222222222222";
    private sealed class Transport(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Calls++;return send(request); }
    }
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){
        Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    private static void Check(bool condition)
    {if(!condition)throw new InvalidOperationException("Cloud collision contract failed.");}
    private static async Task Fails<T>(Func<Task> action) where T:Exception
    {try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    public static async Task RunAsync()
    {
        var transport=new Transport(async request=>{
            Check(request.RequestUri!.AbsolutePath=="/v1/allowances/reserve-after-collision");
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var r=body.RootElement;
            Check(r.GetProperty("previousNumber").GetString()==Old&&
                r.GetProperty("confirmation").GetString()==$"確認外部占號 {Old} 並預留下一號"&&
                !r.TryGetProperty("ledgerId",out _)&&!r.TryGetProperty("workspaceId",out _));
            return Json(new{ok=true,applicationId=Id,ledgerId=NextLedger,
                allowanceNumber=Next,status="reserved",maySendWithoutFreshOfficialCheck=false});
        });
        using var http=new HttpClient(transport);
        var api=new CloudAllowanceClient(http,new Uri("https://example.invalid/"),"cydev_"+new string('a',64));
        Task<CloudAllowanceReservation> Reserve(string words)=>api.ReserveAfterCollisionAsync(Id,3,
            "synthetic_collision_review",Old,words,"0001","fake-password");
        await Fails<ArgumentException>(()=>Reserve("確認"));Check(transport.Calls==0);
        var result=await Reserve($"確認外部占號 {Old} 並預留下一號");
        Check(result.LedgerId==NextLedger&&result.AllowanceNumber==Next&&transport.Calls==1);
        await Fails<ArgumentException>(()=>api.RecordManualReviewAsync(Id,3,
            "synthetic_collision_review","external_collision_reserved","0001","fake-password"));
        Check(transport.Calls==1);
        var lost=new Transport(_=>throw new TaskCanceledException("synthetic lost reply"));
        using var lostHttp=new HttpClient(lost);
        var lostApi=new CloudAllowanceClient(lostHttp,new Uri("https://example.invalid/"),"cydev_"+new string('a',64));
        await Fails<OperationCanceledException>(()=>lostApi.ReserveAfterCollisionAsync(Id,3,
            "synthetic_collision_review",Old,$"確認外部占號 {Old} 並預留下一號","0001","fake-password"));
        Check(lost.Calls==1);
        foreach(var malformed in new[]{false,true})
        {
            var send=new Transport(_=>Task.FromResult(Json(new{ok=true,applicationId=Id,
                allowanceNumber=Old,ledgerStatus="occupied_external",
                issueKind=malformed?"accepted_unconfirmed":"confirmed_number_collision",
                manualDisposition="number_collision_requires_investigation",
                manualReviewRequired=true,recordedReadback=false,proofOfThisPost=false,
                officiallyConfirmed=false,safeToAutoRetry=false,canAllocateAnotherNumber=false})));
            using var sendHttp=new HttpClient(send);
            var sendApi=new CloudAllowanceClient(sendHttp,new Uri("https://example.invalid/"),"cydev_"+new string('a',64));
            if(malformed)await Fails<InvalidDataException>(()=>sendApi.SendOnceAsync(Id,3,"0001","fake-password"));
            else Check((await sendApi.SendOnceAsync(Id,3,"0001","fake-password")).LedgerStatus=="occupied_external");
            Check(send.Calls==1);
        }
        var history=new Transport(_=>Task.FromResult(Json(new{ok=true,applicationId=Id,
            proofOfThisPost=false,officiallyConfirmed=false,safeToAutoRetry=false,canAllocateAnotherNumber=false,
            items=new[]{new{reviewId="synthetic_collision_review",ledgerId=Ledger,
                ledgerStatus="occupied_external",disposition="external_collision_reserved",
                nextLedgerId=NextLedger,nextAllowanceNumber=Next,proofOfThisPost=false,
                officiallyConfirmed=false,safeToAutoRetry=false,canAllocateAnotherNumber=false}}})));
        using var historyHttp=new HttpClient(history);
        var historyApi=new CloudAllowanceClient(historyHttp,new Uri("https://example.invalid/"),"cydev_"+new string('a',64));
        var rows=await historyApi.ManualHistoryAsync(Id,3,"0001","fake-password");
        Check(rows.Count==1&&rows[0].NextLedgerId==NextLedger&&rows[0].NextAllowanceNumber==Next);
    }
}
