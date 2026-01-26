using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing indexer feed configurations.
/// </summary>
public interface IIndexerFeedRepository :
    ICrudRepository<Guid, IndexerFeed>,
    IPatchRepository<Guid, IndexerFeed, IndexerFeedPatch>
{
    /// <summary>
    /// Get an indexer feed by name.
    /// </summary>
    /// <param name="name">The feed name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IndexerFeed?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexer feeds for a provider.
    /// </summary>
    /// <param name="provider">The provider identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<IndexerFeed>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled indexer feeds.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<IndexerFeed>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last sync time for an indexer feed.
    /// </summary>
    /// <param name="id">The feed identifier.</param>
    /// <param name="lastSync">The time of the last successful sync.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<bool>> UpdateLastSyncAsync(
        Guid id,
        DateTimeOffset lastSync,
        CancellationToken cancellationToken = default);
}
