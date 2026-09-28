using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Enums;

namespace Magpie.Core;

/// <summary>
/// Extends <see cref="IDownloadRepository"/> to store refreshed state without the acquisition status and the events,
/// and to complete them later.
/// </summary>
internal interface IDeferrableDownloadRepository : IDownloadRepository
{
    /// <summary>
    /// Updates a download whose status did not change and records the transition, without the follow-up work.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="status">The unchanged status; the update applies only while the stored status is equal</param>
    /// <param name="statusReason">The text that explains the status</param>
    /// <param name="outputPath">The path that holds the downloaded files</param>
    /// <param name="dateCompleted">The UTC time when the download completed</param>
    /// <param name="progress">The percent of the download that is complete</param>
    /// <param name="lastProgressAt">The UTC time of the last progress</param>
    /// <param name="heldByPolicy">Whether the client holds the download</param>
    /// <param name="failureReason">The reason for the failure</param>
    /// <param name="failedAt">The UTC time of the failure</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the row changed and <see cref="CompleteDeferredRefreshAsync"/> must follow
    /// </returns>
    Task<bool> UpdateDeferredRefreshedStatusAsync(
        Guid downloadId,
        DownloadStatus status,
        string? statusReason,
        string? outputPath,
        DateTime? dateCompleted,
        decimal? progress,
        DateTime? lastProgressAt,
        bool heldByPolicy,
        DownloadFailureReason? failureReason,
        DateTime? failedAt,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the acquisition status of the scoped content items and publishes the update events.
    /// </summary>
    /// <param name="downloadIds">The downloads that <see cref="UpdateDeferredRefreshedStatusAsync"/> changed</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>A task that completes after every content item and event is processed</returns>
    Task CompleteDeferredRefreshAsync(
        IReadOnlyCollection<Guid> downloadIds,
        Guid? userId,
        CancellationToken cancellationToken);
}
