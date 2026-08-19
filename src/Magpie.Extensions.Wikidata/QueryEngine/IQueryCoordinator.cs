using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal interface IQueryCoordinator
{
    Task<EntityDto?> GetEntityAsync(
        string qid,
        RequestContext context,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, EntityDto>> GetEntitiesAsync(
        IReadOnlyCollection<string> qids,
        RequestContext context,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null);

    Task<EntityResolution> GetIdentityAsync(
        string key,
        Func<CancellationToken, Task<EntityResolution>> factory,
        CancellationToken cancellationToken);

    Task<SearchCacheResult> GetSearchAsync(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver = null);

    Task<IReadOnlyList<string>> GetEntitySearchAsync(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<string>>> factory,
        CancellationToken cancellationToken,
        Action<SearchCacheProvenance>? provenanceObserver);

    Task<HierarchyResult> GetHierarchyAsync(
        string key,
        Func<CancellationToken, Task<HierarchyResult>> factory,
        CancellationToken cancellationToken);
}
