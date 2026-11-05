using System.Text.Json;
using CYInvoice.Core.Amego;

internal sealed class FakeAllowanceVoidApi : IAmegoAllowanceVoidGateway
{
    public AllowanceQueryItem Official { get; set; }=new("ALW-001","D0401",99,"20260920",2,
        "0000000000","合成買方","5","95",0,JsonSerializer.SerializeToElement(
            new[]{new {original_invoice_number="AA12345678",original_description="合成品項",quantity=1}}));
    public AllowanceVoidStatus Status { get; set; }=new("ALW-001","D0401",99);
    public int QueryCalls {get;private set;}
    public int PostCalls {get;private set;}
    public bool LoseReply {get;set;}
    public Action? OnPost {get;set;}
    public Action? OnQuery {get;set;}
    public Func<Task>? BeforeQueryAsync {get;set;}
    public async Task<AllowanceQueryResponse> QueryAllowanceAsync(string number,CancellationToken cancellationToken=default)
    {
        QueryCalls++; if(BeforeQueryAsync is not null)await BeforeQueryAsync(); OnQuery?.Invoke();
        return new(0,"",Official);
    }
    public Task<AllowanceIssueResponse> VoidAllowanceOnceAsync(string number,CancellationToken cancellationToken=default)
    {
        PostCalls++;
        OnPost?.Invoke();
        return LoseReply ? Task.FromException<AllowanceIssueResponse>(new HttpRequestException("synthetic lost response"))
            : Task.FromResult(new AllowanceIssueResponse(0,""));
    }
    public Task<AllowanceVoidStatus> ReadAllowanceVoidStatusAsync(string number,CancellationToken cancellationToken=default)=>Task.FromResult(Status);
}
