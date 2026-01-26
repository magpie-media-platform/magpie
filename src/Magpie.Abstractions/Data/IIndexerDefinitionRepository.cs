using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing indexer definitions.
/// </summary>
public interface IIndexerDefinitionRepository : ICrudRepository<Guid, IndexerDefinition>
{
    /// <summary>
    /// Get an indexer definition by name.
    /// </summary>
    /// <param name="name">The definition name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IndexerDefinition?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexer definitions for a feed.
    /// </summary>
    /// <param name="feedId">The feed identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<IndexerDefinition>>> GetByFeedIdAsync(
        Guid feedId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexer definitions for a provider.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<IndexerDefinition>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexer definitions with a specific access type.
    /// </summary>
    /// <param name="access">The access type to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<IndexerDefinition>>> GetByAccessAsync(
        IndexerAccess access,
        CancellationToken cancellationToken = default);
}
