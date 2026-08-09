using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Events;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class DownloadService(
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadClientRepository downloadClientRepository,
    IDownloadRepository downloadRepository,
    IJobExecutionRepository jobExecutionRepository,
    IJobManager jobManager,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<DownloadService> logger) : IDownloadService
{
    private static readonly DownloadStatus[] PausableStatuses =
    [
        DownloadStatus.Grabbed,
        DownloadStatus.Queued,
        DownloadStatus.Downloading,
        DownloadStatus.Stalled
    ];

    private static readonly DownloadStatus[] ResumableStatuses = [DownloadStatus.Paused];

    private static readonly DownloadStatus[] ForceStartableStatuses =
    [
        DownloadStatus.Paused,
        DownloadStatus.Queued,
        DownloadStatus.Stalled
    ];

    private static readonly string DownloadIdTriggerContextKey = MagpieEvents.Keys.DownloadId.ToEventContextKey();

    private const string DownloadImportItemJobType = "DownloadImportItem";
    private const string ReleaseAutoGrabItemJobType = "ReleaseAutoGrabItem";
    private const string AutoGrabItemContentItemIdParameter = "contentItemId";
    private const string DownloadIdParameter = "downloadId";

    private const string PauseReason = "Operator pause";
    private const string ResumeReason = "Operator resume";
    private const string ForceStartReason = "Operator force-start";

    private static bool ShouldApplyCurrentClientStatus(
        StuckDownloadCandidate candidate,
        DownloadInfo info,
        DateTime stalledCutoff)
    {
        return info.Status is DownloadStatus.Downloaded
                or DownloadStatus.Imported
                or DownloadStatus.Failed
                or DownloadStatus.Queued
                or DownloadStatus.Paused ||
            ShouldApplyStalledStatusBeforeShortCutoff(candidate, info, stalledCutoff) ||
            HasProgressAnchorUpdate(candidate, info);
    }

    private static bool ShouldApplyStalledStatusBeforeShortCutoff(
        StuckDownloadCandidate candidate,
        DownloadInfo info,
        DateTime stalledCutoff)
    {
        return info.Status == DownloadStatus.Stalled &&
            candidate.Status is DownloadStatus.Downloading or DownloadStatus.Grabbed &&
            candidate.InactivityAnchor >= stalledCutoff;
    }

    private static bool IsCompleteStatus(DownloadStatus status)
    {
        return status is DownloadStatus.Downloaded or DownloadStatus.Imported;
    }

    private static bool IsCompletionTransition(StuckDownloadCandidate candidate, DownloadInfo info)
    {
        return IsCompleteStatus(info.Status) && !IsCompleteStatus(candidate.Status);
    }

    private static bool HasProgressAnchorUpdate(StuckDownloadCandidate candidate, DownloadInfo info)
    {
        return info.ProgressPercent is { } reportedProgress &&
            (candidate.Progress != reportedProgress || candidate.LastProgressAt is null);
    }

    private static RefreshedDownloadState BuildRefreshedState(
        StuckDownloadCandidate candidate,
        DownloadInfo info,
        DateTime now)
    {
        var newDateCompleted = IsCompleteStatus(info.Status) && candidate.DateCompleted is null
            ? now
            : candidate.DateCompleted;

        var newProgress = candidate.Progress;
        var newLastProgressAt = candidate.LastProgressAt;
        var completionTransition = IsCompletionTransition(candidate, info);
        if (HasProgressAnchorUpdate(candidate, info) || completionTransition)
        {
            if (info.ProgressPercent is { } reportedProgress)
            {
                newProgress = reportedProgress;
            }

            newLastProgressAt = now;
        }

        var failedTransition = info.Status is DownloadStatus.Failed && candidate.Status is not DownloadStatus.Failed;

        return new RefreshedDownloadState(
            newDateCompleted,
            newProgress,
            newLastProgressAt,
            failedTransition ? DownloadFailureReason.DownloadErrored : candidate.FailureReason,
            failedTransition ? now : candidate.FailedAt);
    }

    public async Task<GrabResult> GrabAsync(
        Release release,
        DownloadClient source,
        IReadOnlyDictionary<string, string> settings,
        string? contentType = null,
        bool ignoreFailedTaint = false,
        CancellationToken cancellationToken = default)
    {
        var integration = downloadClientIntegrations.LastOrDefault(
            candidate => string.Equals(candidate.ProviderType, source.ProviderType, StringComparison.Ordinal));
        if (integration is null)
        {
            throw new InvalidOperationException(
                $"No download client integration is available for provider type {source.ProviderType}.");
        }

        if (!ignoreFailedTaint)
        {
            var taints = await downloadRepository
                .ListFailedByReleaseIdentitiesAsync([.. release.IdentityKeys], source.Id, cancellationToken)
                .ConfigureAwait(false);
            if (taints.Count > 0)
            {
                logger.LogInformation(
                    "The download service refused the grab of release {ReleaseId} on client " +
                    "{DownloadClientId}. {TaintCount} prior Failed rows exist",
                    release.Id,
                    source.Id,
                    taints.Count);
                return GrabResult.FromAlreadyTainted("Release tainted by prior Failed grab");
            }
        }
        else
        {
            logger.LogInformation(
                "The manual grab of release {ReleaseId} on client {DownloadClientId} ignored the " +
                "prior Failed grab taint",
                release.Id,
                source.Id);
        }

        var downloadInfo = await integration
            .DownloadReleaseAsync(
                release,
                new ReleaseGrabContext { ContentType = contentType },
                settings,
                cancellationToken)
            .ConfigureAwait(false);

        return GrabResult.FromSuccess(downloadInfo);
    }

    private async Task<bool> IsImportAlreadyDispatchedAsync(
        Guid downloadId,
        CancellationToken cancellationToken = default)
    {
        var downloadIdString = downloadId.ToString("D");

        const int pageSize = 500;
        var pageNumber = 1;
        while (true)
        {
            var page = await jobExecutionRepository.QueryAsync(
                new JobExecutionQuery
                {
                    JobType = DownloadImportItemJobType,
                    Statuses =
                    [
                        JobExecutionStatus.Queued,
                        JobExecutionStatus.Running,
                        JobExecutionStatus.Completed
                    ]
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            if (pageItems.Any(e =>
                (e.Parameters.TryGetValue(DownloadIdParameter, out var paramId) &&
                    string.Equals(paramId, downloadIdString, StringComparison.OrdinalIgnoreCase)) ||
                (e.TriggerContext.TryGetValue(DownloadIdTriggerContextKey, out var ctxId) &&
                    string.Equals(ctxId, downloadIdString, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            if (pageItems.Count < pageSize)
            {
                return false;
            }

            pageNumber++;
        }
    }

    private async Task<bool> TryEnqueueImportAsync(
        Guid downloadId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (await IsImportAlreadyDispatchedAsync(downloadId, cancellationToken).ConfigureAwait(false))
        {
            logger.LogDebug(
                "An import for download {DownloadId} already exists, so the download service did not " +
                "enqueue DownloadImportItem",
                downloadId);
            return false;
        }

        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = DownloadImportItemJobType,
                Parameters = new Dictionary<string, string>()
                {
                    [DownloadIdParameter] = downloadId.ToString("D")
                },
                UserId = userId,
                Mode = JobExecutionMode.Queue,
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<PagedList<DownloadQueueEntry>> QueryAsync(
        DownloadQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        var page = await downloadRepository.QueryQueueEntriesAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);
        return page with { Items = WithDownloadClientCapabilities(page.Items) };
    }

    public async Task<DownloadQueueEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await downloadRepository.GetQueueEntryByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        return WithDownloadClientCapabilities([entry])[0];
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (download is null || download.Status is DownloadStatus.Imported
            or DownloadStatus.Failed
            or DownloadStatus.Cancelled)
        {
            return false;
        }

        return await downloadRepository.PatchAsync(
            new()
            {
                Id = id,
                Status = new() { Value = DownloadStatus.Cancelled },
                StatusReason = new() { Value = "Operator cancel" }
            },
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await downloadRepository.RetryAsync(id, null, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != DownloadRetryOutcome.Retried)
        {
            return false;
        }

        if (result.ContentItemId is { } contentItemId)
        {
            await EnqueueAutoGrabItemAsync(contentItemId, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> ForceStartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DispatchClientTransitionAsync<IForcableDownloadClient>(
            id,
            ForceStartableStatuses,
            DownloadStatus.Downloading,
            ForceStartReason,
            clearHeldByPolicy: true,
            (capability, externalId, settings, ct) => capability.ForceStartDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DispatchClientTransitionAsync<IPausableDownloadClient>(
            id,
            PausableStatuses,
            DownloadStatus.Paused,
            PauseReason,
            clearHeldByPolicy: false,
            (capability, externalId, settings, ct) => capability.PauseDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DispatchClientTransitionAsync<IResumableDownloadClient>(
            id,
            ResumableStatuses,
            DownloadStatus.Downloading,
            ResumeReason,
            clearHeldByPolicy: false,
            (capability, externalId, settings, ct) => capability.ResumeDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        bool deleteFiles = false,
        CancellationToken cancellationToken = default)
    {
        if (!deleteFiles)
        {
            return await downloadRepository.DeleteAsync(id, null, cancellationToken).ConfigureAwait(false);
        }

        var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (download is null)
        {
            return false;
        }

        return await DeleteWithFilesAsync(download, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkCancelAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        var ids = (await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false))
            .Where(download => download.Status is not DownloadStatus.Imported
                and not DownloadStatus.Failed
                and not DownloadStatus.Cancelled)
            .Select(download => download.Id)
            .ToList();

        return await downloadRepository.BulkPatchAsync(
            new()
            {
                Ids = ids,
                Patch = new()
                {
                    Status = new() { Value = DownloadStatus.Cancelled },
                    StatusReason = new() { Value = "Operator cancel" }
                }
            },
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkDeleteAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        return await downloadRepository.BulkDeleteAsync(action, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkRemoveAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        var removed = 0;
        var targets = await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false);
        foreach (var download in targets)
        {
            if (await DeleteWithFilesAsync(download, cancellationToken).ConfigureAwait(false))
            {
                removed++;
            }
        }

        return new BulkResult { Count = removed };
    }

    public async Task<BulkResult> BulkPauseAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        await DispatchBulkClientTransitionAsync<IPausableDownloadClient>(
            action,
            PausableStatuses,
            (capability, externalId, settings, ct) => capability.PauseDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);

        var ids = (await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false))
            .Where(download => PausableStatuses.Contains(download.Status))
            .Select(download => download.Id)
            .ToList();

        return await downloadRepository.BulkPatchAsync(
            new()
            {
                Ids = ids,
                Patch = new()
                {
                    Status = new() { Value = DownloadStatus.Paused },
                    StatusReason = new() { Value = PauseReason }
                }
            },
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkResumeAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        await DispatchBulkClientTransitionAsync<IResumableDownloadClient>(
            action,
            ResumableStatuses,
            (capability, externalId, settings, ct) => capability.ResumeDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);

        var ids = (await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false))
            .Where(download => ResumableStatuses.Contains(download.Status))
            .Select(download => download.Id)
            .ToList();

        return await downloadRepository.BulkPatchAsync(
            new()
            {
                Ids = ids,
                Patch = new()
                {
                    Status = new() { Value = DownloadStatus.Downloading },
                    StatusReason = new() { Value = ResumeReason }
                }
            },
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkResult> BulkRetryAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        var targets = await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false);

        var retried = 0;
        var contentItemIds = new HashSet<Guid>();
        foreach (var download in targets.Where(d => d.Status == DownloadStatus.Failed))
        {
            var result = await downloadRepository.RetryAsync(download.Id, null, cancellationToken)
                .ConfigureAwait(false);
            if (result.Outcome != DownloadRetryOutcome.Retried)
            {
                continue;
            }

            retried++;
            if (result.ContentItemId is { } contentItemId)
            {
                contentItemIds.Add(contentItemId);
            }
        }

        foreach (var contentItemId in contentItemIds)
        {
            await EnqueueAutoGrabItemAsync(contentItemId, cancellationToken).ConfigureAwait(false);
        }

        return new BulkResult { Count = retried };
    }

    public async Task<BulkResult> BulkStopAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        var ids = (await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false))
            .Where(download => download.Status is not DownloadStatus.Imported
                and not DownloadStatus.Failed
                and not DownloadStatus.Cancelled)
            .Select(download => download.Id)
            .ToList();

        return await downloadRepository.BulkPatchAsync(
            new()
            {
                Ids = ids,
                Patch = new()
                {
                    Status = new() { Value = DownloadStatus.Cancelled },
                    StatusReason = new() { Value = "Operator stop" }
                }
            },
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var newDateCompleted = info.Status is DownloadStatus.Downloaded or DownloadStatus.Imported &&
            download.DateCompleted is null
                ? timeProvider.GetUtcNow().UtcDateTime
                : download.DateCompleted;

        var newProgress = download.Progress;
        var newLastProgressAt = download.LastProgressAt;
        if (info.ProgressPercent is { } reportedProgress)
        {
            if (download.Progress != reportedProgress)
            {
                newProgress = reportedProgress;
                newLastProgressAt = timeProvider.GetUtcNow().UtcDateTime;
            }
            else if (download.LastProgressAt is null)
            {
                newLastProgressAt = timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        var hasChanges = download.Status != info.Status ||
            download.StatusReason != info.StatusReason ||
            download.OutputPath != info.OutputPath ||
            download.DateCompleted != newDateCompleted ||
            download.Progress != newProgress ||
            download.LastProgressAt != newLastProgressAt ||
            download.HeldByPolicy != info.HeldByPolicy;

        if (!hasChanges)
        {
            return;
        }

        var completedTransition = info.Status is DownloadStatus.Downloaded &&
            download.Status is not DownloadStatus.Downloaded;

        var failedTransition = info.Status is DownloadStatus.Failed && download.Status is not DownloadStatus.Failed;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var newFailureReason = failedTransition
            ? DownloadFailureReason.DownloadErrored
            : download.FailureReason;
        var newFailedAt = failedTransition ? now : download.FailedAt;

        var applied = false;
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                applied = await downloadRepository.UpdateRefreshedStatusAsync(
                    download.Id,
                    info.Status,
                    info.StatusReason,
                    info.OutputPath,
                    newDateCompleted,
                    newProgress,
                    newLastProgressAt,
                    info.HeldByPolicy,
                    newFailureReason,
                    newFailedAt,
                    now,
                    userId,
                    ct)
                    .ConfigureAwait(false);

                if (!applied)
                {
                    logger.LogDebug(
                        "Download {DownloadId} reached a terminal status concurrently. " +
                        "The refreshed status did not apply",
                        download.Id);
                    return;
                }

                if (completedTransition && await TryEnqueueImportAsync(download.Id, userId, ct).ConfigureAwait(false))
                {
                    logger.LogInformation(
                        "Download {DownloadId} completed. The download service enqueued DownloadImportItem directly",
                        download.Id);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyImportedHoldTrackingAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (download.HeldByPolicy == info.HeldByPolicy)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await downloadRepository
            .UpdateImportedHoldTrackingAsync(download.Id, info.HeldByPolicy, now, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryApplyVerifiedClientStateAsync(
        StuckDownloadCandidate candidate,
        DownloadInfo info,
        DateTime stalledCutoff,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!ShouldApplyCurrentClientStatus(candidate, info, stalledCutoff))
        {
            return false;
        }

        var refreshed = BuildRefreshedState(candidate, info, now);
        var completedTransition = info.Status is DownloadStatus.Downloaded &&
            candidate.Status is not DownloadStatus.Downloaded;
        var updated = await downloadRepository.UpdateActiveRefreshedStatusAsync(
            candidate.DownloadId,
            info.Status,
            info.StatusReason,
            info.OutputPath,
            refreshed.DateCompleted,
            refreshed.Progress,
            refreshed.LastProgressAt,
            info.HeldByPolicy,
            refreshed.FailureReason,
            refreshed.FailedAt,
            now,
            userId,
            cancellationToken)
            .ConfigureAwait(false);

        if (!updated)
        {
            return true;
        }

        if (completedTransition)
        {
            if (await TryEnqueueImportAsync(candidate.DownloadId, userId, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Download {DownloadId} completed during the stuck download verification. " +
                    "The download service enqueued DownloadImportItem directly",
                    candidate.DownloadId);
            }
        }

        logger.LogInformation(
            "Download {DownloadId} on {DownloadClientName} refreshed to {DownloadStatus}. " +
            "The download service did not apply the Failed or DownloadStuck status",
            candidate.DownloadId,
            candidate.ClientName,
            info.Status);

        return true;
    }

    public async Task<bool> MarkStuckAsync(
        StuckDownloadCandidate candidate,
        TimeSpan threshold,
        DateTime now,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var transitioned = await downloadRepository
            .MarkStuckAsync(
                candidate.DownloadId,
                candidate.Title,
                candidate.InactivityAnchor,
                threshold,
                now,
                userId,
                correlationId,
                cancellationToken)
            .ConfigureAwait(false);

        return transitioned;
    }

    private async Task<bool> DispatchClientTransitionAsync<TCapability>(
        Guid id,
        DownloadStatus[] eligibleStatuses,
        DownloadStatus targetStatus,
        string statusReason,
        bool clearHeldByPolicy,
        Func<TCapability, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<bool>> dispatch,
        CancellationToken cancellationToken)
        where TCapability : class
    {
        var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (download is null ||
            Array.IndexOf(eligibleStatuses, download.Status) < 0 ||
            string.IsNullOrEmpty(download.ExternalId))
        {
            return false;
        }

        var client = await downloadClientRepository.GetByIdAsync(download.DownloadClientId, cancellationToken)
            .ConfigureAwait(false);
        var integration = client is null
            ? null
            : downloadClientIntegrations.LastOrDefault(
                candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
        if (client is null || integration is not TCapability capability)
        {
            return false;
        }

        var accepted = await dispatch(capability, download.ExternalId, client.Settings, cancellationToken)
            .ConfigureAwait(false);
        if (!accepted)
        {
            return false;
        }

        var patch = new DownloadPatch
        {
            Id = id,
            Status = new() { Value = targetStatus },
            StatusReason = new() { Value = statusReason },
            HeldByPolicy = clearHeldByPolicy ? new ScalarPatch<bool> { Value = false } : null,
        };

        return await downloadRepository.PatchAsync(patch, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchBulkClientTransitionAsync<TCapability>(
        BulkAction<DownloadQuery> action,
        DownloadStatus[] applicableStatuses,
        Func<TCapability, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<bool>> dispatch,
        CancellationToken cancellationToken)
        where TCapability : class
    {
        var downloads = await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false);
        var applicable = downloads.Where(d => Array.IndexOf(applicableStatuses, d.Status) >= 0);

        foreach (var group in applicable.GroupBy(d => d.DownloadClientId))
        {
            var client = await downloadClientRepository.GetByIdAsync(group.Key, cancellationToken)
                .ConfigureAwait(false);
            var integration = client is null
                ? null
                : downloadClientIntegrations.LastOrDefault(
                    candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
            if (client is null || integration is not TCapability capability)
            {
                continue;
            }

            foreach (var download in group)
            {
                if (string.IsNullOrEmpty(download.ExternalId))
                {
                    continue;
                }

                try
                {
                    await dispatch(capability, download.ExternalId, client.Settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The best-effort bulk transition of download {DownloadId} ({ExternalId}) on the client failed",
                        download.Id,
                        download.ExternalId);
                }
            }
        }
    }

    private async Task<IReadOnlyList<Download>> ListBulkTargetsAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken)
    {
        var targets = new Dictionary<Guid, Download>();
        if (action.Ids is { Count: > 0 })
        {
            foreach (var id in action.Ids)
            {
                var download = await downloadRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (download is not null)
                {
                    targets[download.Id] = download;
                }
            }
        }

        if (action.Filter is not null)
        {
            const int pageSize = 500;
            var pageNumber = 1;
            while (true)
            {
                var page = await downloadRepository.QueryAsync(
                    action.Filter,
                    new() { PageNumber = pageNumber, PageSize = pageSize },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var pageItems = page.Items.ToList();
                foreach (var download in pageItems)
                {
                    targets[download.Id] = download;
                }

                if (pageItems.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }
        }

        return [.. targets.Values];
    }

    public async Task<int> DeleteClearedFailedDownloadsAsync(
        DateTime failedBefore,
        CancellationToken cancellationToken = default)
    {
        return await downloadRepository.DeleteClearedFailedDownloadsAsync(failedBefore, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnqueueAutoGrabItemAsync(Guid contentItemId, CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = ReleaseAutoGrabItemJobType,
                Parameters = new Dictionary<string, string>()
                {
                    [AutoGrabItemContentItemIdParameter] = contentItemId.ToString("D")
                },
                Mode = JobExecutionMode.Queue,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The manual retry enqueued ReleaseAutoGrabItem for content item {ContentItemId}",
            contentItemId);
    }

    private async Task TryRemoveFromClientAsync(Download download, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(download.ExternalId))
        {
            return;
        }

        var client = await downloadClientRepository.GetByIdAsync(download.DownloadClientId, cancellationToken)
            .ConfigureAwait(false);
        var integration = client is null
            ? null
            : downloadClientIntegrations.LastOrDefault(
                candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
        if (client is null || integration is null)
        {
            return;
        }

        try
        {
            await integration.RemoveDownloadAsync(download.ExternalId, true, client.Settings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The download client integration failed to remove download {DownloadId} ({ExternalId}) " +
                "and its files from client {DownloadClientId}",
                download.Id,
                download.ExternalId,
                download.DownloadClientId);
        }
    }

    private async Task<bool> DeleteWithFilesAsync(Download download, CancellationToken cancellationToken)
    {
        await TryRemoveFromClientAsync(download, cancellationToken).ConfigureAwait(false);

        return await downloadRepository.DeleteAsync(download.Id, null, cancellationToken).ConfigureAwait(false);
    }

    private List<DownloadQueueEntry> WithDownloadClientCapabilities(IEnumerable<DownloadQueueEntry> entries)
    {
        var interfaceCapabilitiesByProvider =
            new Dictionary<string, (bool CanPause, bool CanResume, bool CanForceStart)>(StringComparer.Ordinal);
        var enriched = new List<DownloadQueueEntry>();
        foreach (var entry in entries)
        {
            var providerType = entry.ProviderType ?? string.Empty;
            if (!interfaceCapabilitiesByProvider.TryGetValue(providerType, out var interfaces))
            {
                var integration = downloadClientIntegrations.LastOrDefault(
                    candidate => string.Equals(candidate.ProviderType, providerType, StringComparison.Ordinal));
                interfaces = (
                    CanPause: integration is IPausableDownloadClient,
                    CanResume: integration is IResumableDownloadClient,
                    CanForceStart: integration is IForcableDownloadClient);
                interfaceCapabilitiesByProvider[providerType] = interfaces;
            }

            enriched.Add(entry with
            {
                CanPause = interfaces.CanPause && PausableStatuses.Contains(entry.Status),
                CanResume = interfaces.CanResume && ResumableStatuses.Contains(entry.Status),
                CanForceStart = interfaces.CanForceStart && ForceStartableStatuses.Contains(entry.Status)
            });
        }

        return enriched;
    }

    private readonly record struct RefreshedDownloadState(
        DateTime? DateCompleted,
        decimal? Progress,
        DateTime? LastProgressAt,
        DownloadFailureReason? FailureReason,
        DateTime? FailedAt);
}
