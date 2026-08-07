using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.QueryEngine;

namespace Magpie.Extensions.OpenLibrary;

internal interface IOpenLibraryClient
{
    Task<T?> GetJsonAsync<T>(ApiRequest request, RequestContext context, CancellationToken cancellationToken)
        where T : class;

    Task<HttpStatusCode> GetStatusAsync(
        ApiRequest request,
        RequestContext context,
        CancellationToken cancellationToken);
}
