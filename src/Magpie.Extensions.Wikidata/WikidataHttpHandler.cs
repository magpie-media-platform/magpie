using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Wikidata;

internal sealed class WikidataHttpHandler : DelegatingHandler
{
    internal const string RequestObserver = "Wikidata.RequestObserver";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Properties.TryGetValue(RequestObserver, out var observer);
        UpstreamRequestAccounting.Record(observer as Action);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            response.RequestMessage = request;
            if (await WikidataClient.IsThrottledAsync(response, cancellationToken).ConfigureAwait(false))
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
            }

            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
