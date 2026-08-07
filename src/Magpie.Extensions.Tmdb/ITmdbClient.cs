using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.QueryEngine;

namespace Magpie.Extensions.Tmdb;

internal interface ITmdbClient
{
    Task<T?> GetAsync<T>(RequestContext context, Request request, CancellationToken cancellationToken)
        where T : class;

    Task<T?> GetAsync<T>(
        RequestContext context,
        string path,
        IReadOnlyList<(string Key, string Value)>? queryParameters,
        ResponseProfile profile,
        CancellationToken cancellationToken)
        where T : class;
}
