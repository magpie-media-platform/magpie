using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal interface IQueryCoordinator
{
    Task<string> GetImageBaseUrlAsync(
        RequestContext context,
        ITmdbClient client,
        ILogger logger,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, int>> GetGenreMapAsync(
        RequestContext context,
        Request request,
        ITmdbClient client,
        CancellationToken cancellationToken);

    Task<SubjectResolution> GetSubjectResolutionAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<SubjectResolution>> loader,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubjectResolution>> GetSubjectResolutionsAsync<TState>(
        string key,
        TState state,
        Func<TState, CancellationToken, Task<IReadOnlyList<SubjectResolution>>> loader,
        CancellationToken cancellationToken);

    Task<CreditResponse> GetCreditsAsync<TState>(
        string key,
        string role,
        TState state,
        Func<TState, CancellationToken, Task<CreditResponse>> loader,
        CancellationToken cancellationToken);

    QueryScanState GetScanState(string key);
}
