using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Tmdb;

internal interface IHydrationCache
{
    Task<MetadataSearchCandidate?> GetOrCreateAsync<TState>(
        HydrationCache.HydrationKey key,
        TState state,
        Func<TState, CancellationToken, Task<MetadataSearchCandidate?>> loader,
        CancellationToken cancellationToken);
}
