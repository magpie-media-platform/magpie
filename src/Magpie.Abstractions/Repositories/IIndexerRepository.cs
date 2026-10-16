using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for indexers.
/// </summary>
public interface IIndexerRepository :
    IQueryRepository<Indexer, IndexerQuery>,
    ICrudRepository<Guid, Indexer>,
    IPatchRepository<Guid, Indexer, IndexerPatch>
{
    /// <summary>
    /// Creates a connection of an indexer.
    /// </summary>
    /// <param name="connection">The connection to create</param>
    /// <param name="createdByUserId">The id of the user that creates the record, or <c>null</c> when none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new connection</returns>
    Task<IndexerConnection> CreateConnectionAsync(
        IndexerConnection connection,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the given fields of an indexer connection.
    /// </summary>
    /// <param name="patch">The fields to change</param>
    /// <param name="modifiedByUserId">The id of the user that changes the record, or <c>null</c> when none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the connection exists, otherwise <see langword="false"/></returns>
    Task<bool> PatchConnectionAsync(
        IndexerConnectionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an indexer connection.
    /// </summary>
    /// <param name="connectionId">The id of the connection to delete</param>
    /// <param name="deletedByUserId">The id of the user that deletes the record, or <c>null</c> when none</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the connection exists, otherwise <see langword="false"/></returns>
    Task<bool> DeleteConnectionAsync(
        Guid connectionId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
