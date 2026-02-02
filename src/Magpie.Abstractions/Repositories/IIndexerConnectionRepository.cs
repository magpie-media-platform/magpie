using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing indexer connections.
/// </summary>
public interface IIndexerConnectionRepository :
    ICrudRepository<Guid, IndexerConnection>,
    IPatchRepository<Guid, IndexerConnection, IndexerConnectionPatch>
{
    /// <summary>
    /// List indexer connections by indexer id.
    /// </summary>
    /// <param name="indexerId">The indexer id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<IndexerConnection>> ListByIndexerIdAsync(
        Guid indexerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List indexer connections by tag.
    /// </summary>
    /// <param name="tagName">The tag name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<IndexerConnection>> ListByTagAsync(string tagName, CancellationToken cancellationToken = default);
}
