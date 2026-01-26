using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Downloads;
using Magpie.Models.Result;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing downloads.
/// </summary>
public interface IDownloadRepository : ICrudRepository<Guid, Download>
{
    /// <summary>
    /// Get all downloads for a specific content item.
    /// </summary>
    /// <param name="contentItemId">The content item identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Download>>> GetByContentItemIdAsync(
        Guid contentItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all downloads for a specific download client.
    /// </summary>
    /// <param name="downloadClientId">The download client identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Download>>> GetByDownloadClientIdAsync(
        Guid downloadClientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a download by its external identifier and download client.
    /// </summary>
    /// <param name="downloadClientId">The download client identifier.</param>
    /// <param name="externalId">The external identifier from the download client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<Download?>> GetByExternalIdAsync(
        Guid downloadClientId,
        string externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all downloads in a specific state.
    /// </summary>
    /// <param name="state">The download state to filter by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<Download>>> GetByStateAsync(
        DownloadState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the state of a download.
    /// </summary>
    /// <param name="id">The download identifier.</param>
    /// <param name="state">The new state.</param>
    /// <param name="outputPath">The output path, if applicable.</param>
    /// <param name="failureReason">The failure reason, if failed.</param>
    /// <param name="dateCompleted">The completion date, if completed.</param>
    /// <param name="modifiedByUserId">The id of the user modifying the record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing <see langword="true"/> if the download was updated,
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<Result<bool>> UpdateStateAsync(
        Guid id,
        DownloadState state,
        string? outputPath,
        string? failureReason,
        DateTimeOffset? dateCompleted,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all downloads for a specific download client.
    /// </summary>
    /// <param name="downloadClientId">The download client identifier.</param>
    /// <param name="deletedByUserId">The id of the user deleting the downloads.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of downloads deleted.</returns>
    Task<Result<int>> DeleteByDownloadClientIdAsync(
        Guid downloadClientId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);
}
