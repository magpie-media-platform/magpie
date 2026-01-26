using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Indexers;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing releases.
/// </summary>
public interface IReleaseRepository : ICrudRepository<Guid, Release>
{
    /// <summary>
    /// Get all releases from a specific indexer.
    /// </summary>
    /// <param name="indexerId">The indexer identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Release>>> GetByIndexerIdAsync(
        Guid indexerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a release by its external identifier and indexer.
    /// </summary>
    /// <param name="indexerId">The indexer identifier.</param>
    /// <param name="externalId">The external identifier from the indexer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Release?>> GetByExternalIdAsync(
        Guid indexerId,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all releases from a specific provider.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Release>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all releases that have a specific info hash.
    /// </summary>
    /// <param name="infoHash">The torrent info hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Release>>> GetByInfoHashAsync(
        string infoHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all releases that have a specific tag.
    /// </summary>
    /// <param name="tagName">The tag name to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Release>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all releases for a specific indexer.
    /// </summary>
    /// <param name="indexerId">The indexer identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the releases.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of releases deleted.</returns>
    Task<Result<int>> DeleteByIndexerIdAsync(
        Guid indexerId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
