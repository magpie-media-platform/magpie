using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for downloads.
/// </summary>
public interface IDownloadRepository :
    ICrudRepository<Guid, Download>,
    IQueryRepository<Download, DownloadQuery>,
    IPatchRepository<Guid, Download, DownloadPatch>,
    IBulkPatchRepository<Guid, Download, DownloadQuery, DownloadPatch>
{
    /// <summary>
    /// Gets one page of download queue entries that match a query.
    /// </summary>
    /// <param name="query">The criteria that an entry must match</param>
    /// <param name="paging">The page number and page size</param>
    /// <param name="sort">The field and the direction of the sort, or <c>null</c> for the default order</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<DownloadQueueEntry>> QueryQueueEntriesAsync(
        DownloadQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one download queue entry by id.
    /// </summary>
    /// <param name="id">The id of the download</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadQueueEntry?> GetQueueEntryByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a download that failed.
    /// </summary>
    /// <param name="id">The id of the download</param>
    /// <param name="retriedByUserId">The id of the user that retries the download</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<DownloadRetryResult> RetryAsync(
        Guid id,
        Guid? retriedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every download that the bulk selection matches.
    /// </summary>
    /// <param name="action">The selection of downloads to delete</param>
    /// <param name="deletedByUserId">The id of the user that deletes the downloads</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<BulkResult> BulkDeleteAsync(
        BulkAction<DownloadQuery> action,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets failed downloads for a set of release identities.
    /// </summary>
    /// <param name="releaseIdentityKeys">The release identity keys to look up</param>
    /// <param name="downloadClientId">The id of one download client, or null for every client</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Download>> ListFailedByReleaseIdentitiesAsync(
        IReadOnlyCollection<ReleaseIdentityKey> releaseIdentityKeys,
        Guid? downloadClientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent imported downloads for a content hierarchy item.
    /// </summary>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item</param>
    /// <param name="completedAfter">The earliest completion time to consider</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Download>> ListImportedByContentHierarchyItemAsync(
        Guid contentHierarchyItemId,
        DateTime completedAfter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a download that is not in a terminal state from a fresh client status.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="status">The status that the client reports</param>
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
    Task<bool> UpdateRefreshedStatusAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an active download from a fresh client status.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="status">The status that the client reports</param>
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
    Task<bool> UpdateActiveRefreshedStatusAsync(
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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the hold state of an imported download.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="heldByPolicy">Whether the client holds the download</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task UpdateImportedHoldTrackingAsync(
        Guid downloadId,
        bool heldByPolicy,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a downloaded download to the importing state.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns><see langword="true"/> when the download moves</returns>
    Task<bool> MarkImportingAsync(
        Guid downloadId,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a download as imported.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="contentHierarchyItemId">The id of the content hierarchy item that took the files</param>
    /// <param name="importedFileCount">The number of files that the import moved</param>
    /// <param name="dateCompleted">The UTC time when the download completed</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task MarkImportedAsync(
        Guid downloadId,
        Guid? contentHierarchyItemId,
        int importedFileCount,
        DateTime dateCompleted,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the downloads that passed a stuck-detection time limit.
    /// </summary>
    /// <param name="fetchingCutoff">The time limit for a download that still fetches</param>
    /// <param name="stalledCutoff">The time limit for a download that makes no progress</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<StuckDownloadCandidate>> ListStuckCandidatesAsync(
        DateTime fetchingCutoff,
        DateTime stalledCutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an active download to the failed state because it is stuck.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="title">The title of the download</param>
    /// <param name="inactivityAnchor">The UTC time of the last activity</param>
    /// <param name="threshold">The time limit that the download passed</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="correlationId">The id that links this change to the operation that caused it</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> MarkStuckAsync(
        Guid downloadId,
        string title,
        DateTime inactivityAnchor,
        TimeSpan threshold,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a download to the failed state, unless it is cancelled or imported.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="reason">The reason for the failure</param>
    /// <param name="statusReason">The text that explains the status</param>
    /// <param name="auditOperation">The name of the operation in the audit entry</param>
    /// <param name="auditDetail">The text of the audit entry</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="correlationId">The id that links this change to the operation that caused it</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> MarkFailedAsync(
        Guid downloadId,
        DownloadFailureReason reason,
        string statusReason,
        string auditOperation,
        string auditDetail,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the terminal downloads that the client cleanup can consider.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<DownloadCleanupCandidate>> ListCleanupCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the client no longer holds a download.
    /// </summary>
    /// <param name="downloadId">The id of the download</param>
    /// <param name="title">The title of the download</param>
    /// <param name="clientName">The name of the download client</param>
    /// <param name="trigger">The event that started the cleanup</param>
    /// <param name="now">The UTC time to record</param>
    /// <param name="userId">The id of the user that starts the operation</param>
    /// <param name="correlationId">The id that links this change to the operation that caused it</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> StampClientRemovedAsync(
        Guid downloadId,
        string title,
        string clientName,
        string trigger,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the downloads of a set of content items that a download client still holds.
    /// </summary>
    /// <param name="contentItemIds">The ids of the content items to look up</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<Download>> ListClientResidentByContentItemsAsync(
        IReadOnlyList<Guid> contentItemIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the failed downloads that the retention rules clear.
    /// </summary>
    /// <param name="failedBefore">The UTC time that a failure must be older than</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<int> DeleteClearedFailedDownloadsAsync(
        DateTime failedBefore,
        CancellationToken cancellationToken = default);
}
