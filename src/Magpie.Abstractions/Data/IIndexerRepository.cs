using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing indexer configurations.
/// </summary>
public interface IIndexerRepository :
    ICrudRepository<Guid, Indexer>,
    IPatchRepository<Guid, Indexer, IndexerPatch>
{
    /// <summary>
    /// Get an indexer by name.
    /// </summary>
    /// <param name="name">The indexer name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<Indexer?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexers for a definition.
    /// </summary>
    /// <param name="definitionId">The definition identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<Indexer>>> GetByDefinitionIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all indexers with a specific status.
    /// </summary>
    /// <param name="status">The status to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<Indexer>>> GetByStatusAsync(
        IndexerStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled indexers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<Indexer>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the status and last checked time for an indexer.
    /// </summary>
    /// <param name="id">The indexer identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="lastChecked">The time the indexer was checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        IndexerStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken = default);
}
