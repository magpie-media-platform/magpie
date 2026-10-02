using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for the download queue.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Queries the download queue.
    /// </summary>
    /// <param name="query">The filter criteria for the queue entries</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The sort field and direction, or <see langword="null"/> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<DownloadQueueEntry>> QueryAsync(
        DownloadQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A download queue entry by id.
    /// </summary>
    /// <param name="id">The id of the download to return</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadQueueEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a client transfer and keeps the cancelled attempt in history without deleting its files.
    /// </summary>
    /// <param name="id">The id of the download to cancel</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a download only when all its current scopes are fulfilled after a successful import.
    /// </summary>
    /// <param name="id">The id of the download to cancel</param>
    /// <param name="importedDownloadId">The id of the imported download to keep</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadCancellationOutcome> CancelFulfilledAsync(
        Guid id,
        Guid importedDownloadId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This download service does not support fulfillment cancellation.");
    }

    /// <summary>
    /// Cancels a client transfer and reports why the request succeeded or failed.
    /// </summary>
    /// <param name="id">The id of the download to cancel</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    async Task<DownloadCancellationOutcome> CancelWithOutcomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await CancelAsync(id, cancellationToken).ConfigureAwait(false)
            ? DownloadCancellationOutcome.Cancelled
            : DownloadCancellationOutcome.NotCancellable;
    }

    /// <summary>
    /// Retries a download.
    /// </summary>
    /// <param name="id">The id of the download to retry</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a download immediately, ahead of the queue order.
    /// </summary>
    /// <param name="id">The id of the download to start</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ForceStartAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a download.
    /// </summary>
    /// <param name="id">The id of the download to pause</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused download.
    /// </summary>
    /// <param name="id">The id of the download to resume</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a download.
    /// </summary>
    /// <param name="id">The id of the download to delete</param>
    /// <param name="deleteFiles">Whether to delete the downloaded files</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteAsync(Guid id, bool deleteFiles = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkCancelAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkDeleteAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every selected download, including its files and record.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkRemoveAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkPauseAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkResumeAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkRetryAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops every download the action selects.
    /// </summary>
    /// <param name="action">The selection of downloads to act on</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkStopAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grabs a release with a download client and tracks the resulting download.
    /// </summary>
    /// <param name="release">The release to grab</param>
    /// <param name="source">The download client that receives the release</param>
    /// <param name="settings">The resolved settings of the download client</param>
    /// <param name="contentType">The slug of the content type for the download, or <see langword="null"/></param>
    /// <param name="interactive">Whether the grab is interactive</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<GrabResult> GrabAsync(
        Release release,
        DownloadClient source,
        IReadOnlyDictionary<string, string> settings,
        string? contentType = null,
        bool interactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies refreshed client state to a tracked download.
    /// </summary>
    /// <param name="download">The tracked download to update</param>
    /// <param name="info">The status as the download client reports it</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ApplyRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the refreshed client state of a download without publishing its status.
    /// </summary>
    /// <param name="download">The tracked download to update</param>
    /// <param name="info">The status as the download client reports it</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>
    /// <see langword="true"/> when the caller must include the download in <see cref="PublishRefreshedStatusAsync"/>
    /// </returns>
    Task<bool> UpdateRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the refreshed status of the downloads.
    /// </summary>
    /// <param name="downloadIds">
    /// The downloads for which <see cref="UpdateRefreshedClientStateAsync"/> returned <see langword="true"/>
    /// </param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>A task that completes after every content item and event is processed</returns>
    Task PublishRefreshedStatusAsync(
        IReadOnlyCollection<Guid> downloadIds,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies policy-hold tracking to a download that is already imported.
    /// </summary>
    /// <param name="download">The tracked download to update</param>
    /// <param name="info">The status as the download client reports it</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task ApplyImportedHoldTrackingAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies verified client state to a stuck candidate and reports whether it refreshed the download.
    /// </summary>
    /// <param name="candidate">The stuck-download candidate to update</param>
    /// <param name="info">The status as the download client reports it</param>
    /// <param name="stalledCutoff">The time before which a download counts as stalled</param>
    /// <param name="now">The time to treat as the present</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> TryApplyVerifiedClientStateAsync(
        StuckDownloadCandidate candidate,
        DownloadInfo info,
        DateTime stalledCutoff,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the cleared failed downloads that failed before a cutoff.
    /// </summary>
    /// <param name="failedBefore">The exclusive cutoff for the failure time</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<int> DeleteClearedFailedDownloadsAsync(
        DateTime failedBefore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a stuck candidate Faulted/DownloadStuck and reports whether the state changed.
    /// </summary>
    /// <param name="candidate">The stuck-download candidate to mark</param>
    /// <param name="threshold">The time a download must sit without progress before it counts as stuck</param>
    /// <param name="now">The time to treat as the present</param>
    /// <param name="userId">The user to record as the actor, or <see langword="null"/> for the system</param>
    /// <param name="correlationId">The id that groups related calls, or <see langword="null"/> when standalone</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> MarkStuckAsync(
        StuckDownloadCandidate candidate,
        TimeSpan threshold,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);
}
