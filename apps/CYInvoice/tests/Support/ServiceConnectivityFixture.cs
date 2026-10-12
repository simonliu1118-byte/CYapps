using System.Net;
using System.Net.Http.Json;
using CYInvoice.Core.Storage;

// Existing business tests already inject the AMEGO gateway. Supply an independent
// service-liveness fixture too, so they never contact a real external service.
internal static class TestRepository
{
    private static readonly HttpClient Services = new(new OnlineServices());
    public static LocalRepository Open(string directory, ISecretProtector protector) =>
        LocalRepository.Open(directory, protector, Services);

    private sealed class OnlineServices : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri?.AbsolutePath != "/json/time")
                throw new InvalidOperationException("Unexpected service request in a business fixture");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { timestamp = 1791626400 }),
            });
        }
    }
}
