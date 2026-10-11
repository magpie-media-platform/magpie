using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class DownloadService(
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadClientRepository downloadClientRepository,
    IDownloadRepository downloadRepository,
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
            HasProgressAnchorUpdate(candidate.Progress, candidate.LastProgressAt, info);
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

    private static decimal? RoundProgress(decimal? progress) =>
        progress is { } value ? decimal.Round(value, 2, MidpointRounding.AwayFromZero) : null;

    private static bool HasProgressAnchorUpdate(decimal? progress, DateTime? lastProgressAt, DownloadInfo info)
    {
        return RoundProgress(info.ProgressPercent) is { } reportedProgress &&
            (progress != reportedProgress || lastProgressAt is null);
    }

    private static RefreshedDownloadState BuildRefreshedState(
        RefreshedDownloadState current,
        DownloadInfo info,
        DateTime now)
    {
        var newDateCompleted = IsCompleteStatus(info.Status) && current.DateCompleted is null
            ? now
            : current.DateCompleted;

        var newProgress = current.Progress;
        var newLastProgressAt = current.LastProgressAt;
        var completionTransition = IsCompleteStatus(info.Status) && !IsCompleteStatus(current.Status);
        if (HasProgressAnchorUpdate(current.Progress, current.LastProgressAt, info) || completionTransition)
        {
            if (RoundProgress(info.ProgressPercent) is { } reportedProgress)
            {
                newProgress = reportedProgress;
            }

            newLastProgressAt = now;
        }

        var failedTransition = info.Status is DownloadStatus.Failed &&
            !DownloadStatuses.Failures.Contains(current.Status);

        return new(
            info.Status is DownloadStatus.Failed ? DownloadStatus.Faulted : info.Status,
            newDateCompleted,
            newProgress,
            newLastProgressAt,
            failedTransition ? DownloadFailureReason.DownloadErrored : current.FailureReason,
            failedTransition ? now : current.FailedAt);
    }

    public async Task<GrabResult> GrabAsync(
        Release release,
        DownloadClient source,
        IReadOnlyDictionary<string, string> settings,
        ReleaseGrabContext? context = null,
        bool interactive = false,
        CancellationToken cancellationToken = default)
    {
        var modeTag = new KeyValuePair<string, object?>("mode", interactive ? "interactive" : "auto");

        var integration = downloadClientIntegrations.LastOrDefault(
            candidate => string.Equals(candidate.ProviderType, source.ProviderType, StringComparison.Ordinal));
        if (integration is null)
        {
            ReleaseTelemetry.Grabs.Add(1, modeTag, new("outcome", "no_download_client"));
            throw new InvalidOperationException(
                $"No download client integration is available for provider type {source.ProviderType}.");
        }

        if (!interactive)
        {
            var taints = (await downloadRepository
                .ListByReleaseAsync([release], cancellationToken)
                .ConfigureAwait(false))
                .Where(download => download.DownloadClientId == source.Id && download.Status == DownloadStatus.Failed)
                .ToList();
            if (taints.Count > 0)
            {
                var refusedTag = new KeyValuePair<string, object?>("ignored", "false");
                ReleaseTelemetry.GrabTaints.Add(1, refusedTag);
                ReleaseTelemetry.Grabs.Add(1, modeTag, new("outcome", "tainted"));
                logger.LogInformation(
                    "The download service refused the grab of release {ReleaseId} on client " +
                    "{DownloadClientId} because {TaintCount} prior Failed rows exist",
                    release.Id,
                    source.Id,
                    taints.Count);
                return GrabResult.FromAlreadyTainted("Release tainted by prior Failed grab");
            }
        }
        else
        {
            var ignoredTag = new KeyValuePair<string, object?>("ignored", "true");
            ReleaseTelemetry.GrabTaints.Add(1, ignoredTag);
            logger.LogInformation(
                "The interactive grab of release {ReleaseId} on client {DownloadClientId} ignored the " +
                "prior Failed grab taint",
                release.Id,
                source.Id);
        }

        DownloadInfo downloadInfo;
        try
        {
            downloadInfo = await integration
                .DownloadReleaseAsync(release, context ?? new(), settings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ReleasePayloadRejectedException ex)
        {
            ReleaseTelemetry.Grabs.Add(1, modeTag, new("outcome", "rejected"));
            logger.LogWarning(
                "The download client integration rejected release {ReleaseId}: {Reason}",
                release.Id,
                ex.Message);
            return new() { Success = false, ErrorMessage = ex.Message, Transient = ex.Transient };
        }

        ReleaseTelemetry.Grabs.Add(1, modeTag, new("outcome", "accepted"));
        return GrabResult.FromSuccess(downloadInfo);
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
        var outcome = await CancelWithOutcomeAsync(id, cancellationToken).ConfigureAwait(false);
        return outcome is DownloadCancellationOutcome.Cancelled or DownloadCancellationOutcome.AlreadyCancelled;
    }

    /// <inheritdoc />
    public Task<DownloadCancellationOutcome> CancelWithOutcomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return CancelAsync(id, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DownloadCancellationOutcome> CancelFulfilledAsync(
        Guid id,
        Guid importedDownloadId,
        CancellationToken cancellationToken = default)
    {
        return CancelAsync(id, importedDownloadId, cancellationToken);
    }

    private async Task<DownloadCancellationOutcome> CancelAsync(
        Guid id,
        Guid? importedDownloadId,
        CancellationToken cancellationToken)
    {
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var lockedIds = importedDownloadId is null
                    ? [id]
                    : (await downloadRepository.ListTransferAttemptsAsync(id, ct).ConfigureAwait(false))
                        .Select(attempt => attempt.Id).ToArray();

                foreach (var attemptId in lockedIds)
                {
                    await downloadRepository.LockAsync(attemptId, ct).ConfigureAwait(false);
                }

                var download = await downloadRepository.GetByIdAsync(id, ct).ConfigureAwait(false);
                if (download is null)
                {
                    return DownloadCancellationOutcome.NotFound;
                }

                if (download.Status == DownloadStatus.Cancelled && download.ClientRemovedAt is not null)
                {
                    return DownloadCancellationOutcome.AlreadyCancelled;
                }

                if (download.Status is DownloadStatus.Imported or DownloadStatus.Importing or DownloadStatus.Failed
                    or DownloadStatus.Faulted)
                {
                    return DownloadCancellationOutcome.NotCancellable;
                }

                var client = await downloadClientRepository.GetByIdAsync(download.DownloadClientId, ct)
                    .ConfigureAwait(false);
                var integration = client is null
                    ? null
                    : downloadClientIntegrations.LastOrDefault(candidate =>
                        string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
                if (client is null || integration is null || string.IsNullOrWhiteSpace(download.ExternalId))
                {
                    return DownloadCancellationOutcome.ClientUnavailable;
                }

                IReadOnlyList<Guid> cancelledIds = [id];
                if (importedDownloadId is { } importedId)
                {
                    if ((await downloadRepository.ListFulfilledIdsAsync(importedId, id, ct)
                        .ConfigureAwait(false)).Count == 0)
                    {
                        return DownloadCancellationOutcome.NotCancellable;
                    }

                    var attempts = await downloadRepository.ListTransferAttemptsAsync(id, ct).ConfigureAwait(false);
                    if (attempts.Any(attempt => !lockedIds.Contains(attempt.Id)))
                    {
                        throw new InvalidOperationException("The transfer attempts changed during cancellation.");
                    }

                    cancelledIds = attempts.Where(attempt =>
                        attempt.Status is not DownloadStatus.Cancelled and not DownloadStatus.Failed
                            and not DownloadStatus.Faulted)
                        .Select(attempt => attempt.Id).ToArray();
                }

                try
                {
                    await integration.RemoveDownloadAsync(download.ExternalId, false, client.Settings, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The client failed to cancel download {DownloadId} ({ExternalId})",
                        download.Id,
                        download.ExternalId);
                    return DownloadCancellationOutcome.ClientError;
                }

                foreach (var cancelledId in cancelledIds)
                {
                    var updated = await downloadRepository.PatchAsync(
                    new()
                    {
                        Id = cancelledId,
                        Status = new() { Value = DownloadStatus.Cancelled },
                        StatusReason = new()
                        {
                            Value = importedDownloadId is null ? "Download cancelled" : "Content fulfilled"
                        },
                        HeldByPolicy = new() { Value = false },
                        ClientRemovedAt = new() { Value = timeProvider.GetUtcNow().UtcDateTime },
                        FailureReason = new() { Value = null },
                        FailedAt = new() { Value = null }
                    },
                    null,
                    ct).ConfigureAwait(false);
                    if (!updated)
                    {
                        if (importedDownloadId is null)
                        {
                            return DownloadCancellationOutcome.NotFound;
                        }

                        throw new InvalidOperationException("A transfer attempt disappeared during cancellation.");
                    }
                }

                return DownloadCancellationOutcome.Cancelled;
            },
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
        var cancelled = 0;
        var failed = 0;
        var downloads = await ListBulkTargetsAsync(action, cancellationToken).ConfigureAwait(false);
        foreach (var download in downloads)
        {
            var outcome = await CancelWithOutcomeAsync(download.Id, cancellationToken).ConfigureAwait(false);
            if (outcome == DownloadCancellationOutcome.Cancelled)
            {
                cancelled++;
            }
            else if (outcome != DownloadCancellationOutcome.AlreadyCancelled)
            {
                failed++;
            }
        }

        return new()
        {
            Count = cancelled,
            Error = failed == 0 ? null : $"Failed to cancel {failed} {(failed == 1 ? "download" : "downloads")}."
        };
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

        var failed = targets.Count - removed;
        return new()
        {
            Count = removed,
            Error = failed == 0 ? null : $"Failed to remove {failed} {(failed == 1 ? "download" : "downloads")}."
        };
    }

    public async Task<BulkResult> BulkPauseAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        var ids = await DispatchBulkClientTransitionAsync<IPausableDownloadClient>(
            action,
            PausableStatuses,
            (capability, externalId, settings, ct) => capability.PauseDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);

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
        var ids = await DispatchBulkClientTransitionAsync<IResumableDownloadClient>(
            action,
            ResumableStatuses,
            (capability, externalId, settings, ct) => capability.ResumeDownloadAsync(externalId, settings, ct),
            cancellationToken).ConfigureAwait(false);

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
        foreach (var download in targets.Where(d => DownloadStatuses.Failures.Contains(d.Status)))
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

        return new() { Count = retried };
    }

    public Task<BulkResult> BulkStopAsync(
        BulkAction<DownloadQuery> action,
        CancellationToken cancellationToken = default)
    {
        return BulkCancelAsync(action, cancellationToken);
    }

    public async Task ApplyRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (BuildRefreshedChange(download, info, now) is { } refreshed)
        {
            await ApplyRefreshedClientStateAsync(download, info, refreshed, now, userId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (BuildRefreshedChange(download, info, now) is not { } refreshed)
        {
            return false;
        }

        if (download.Status == refreshed.Status &&
            await downloadRepository.UpdateRefreshedClientStateAsync(
                download.Id,
                refreshed.Status,
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
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        await ApplyRefreshedClientStateAsync(download, info, refreshed, now, userId, cancellationToken)
            .ConfigureAwait(false);
        return false;
    }

    /// <inheritdoc />
    public Task PublishRefreshedStatusAsync(
        IReadOnlyCollection<Guid> downloadIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return downloadRepository.PublishRefreshedStatusAsync(downloadIds, userId, cancellationToken);
    }

    private static RefreshedDownloadState? BuildRefreshedChange(Download download, DownloadInfo info, DateTime now)
    {
        var refreshed = BuildRefreshedState(
            new RefreshedDownloadState(
                download.Status,
                download.DateCompleted,
                download.Progress,
                download.LastProgressAt,
                download.FailureReason,
                download.FailedAt),
            info,
            now);

        var hasChanges = download.Status != refreshed.Status ||
            download.StatusReason != info.StatusReason ||
            download.OutputPath != info.OutputPath ||
            download.DateCompleted != refreshed.DateCompleted ||
            download.Progress != refreshed.Progress ||
            download.LastProgressAt != refreshed.LastProgressAt ||
            download.HeldByPolicy != info.HeldByPolicy;

        return hasChanges ? refreshed : null;
    }

    private async Task ApplyRefreshedClientStateAsync(
        Download download,
        DownloadInfo info,
        RefreshedDownloadState refreshed,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var applied = await downloadRepository.UpdateRefreshedStatusAsync(
                    download.Id,
                    refreshed.Status,
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
                    ct)
                    .ConfigureAwait(false);

                if (!applied)
                {
                    logger.LogDebug(
                        "Download {DownloadId} reached a terminal status; the refreshed status was not applied",
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

        var refreshed = BuildRefreshedState(
            new RefreshedDownloadState(
                candidate.Status,
                candidate.DateCompleted,
                candidate.Progress,
                candidate.LastProgressAt,
                candidate.FailureReason,
                candidate.FailedAt),
            info,
            now);
        var updated = await downloadRepository.UpdateActiveRefreshedStatusAsync(
            candidate.DownloadId,
            refreshed.Status,
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

        logger.LogInformation(
            "Download {DownloadId} on {DownloadClientName} refreshed to {DownloadStatus} instead of DownloadStuck",
            candidate.DownloadId,
            candidate.ClientName,
            refreshed.Status);

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

    private async Task<List<Guid>> DispatchBulkClientTransitionAsync<TCapability>(
        BulkAction<DownloadQuery> action,
        DownloadStatus[] applicableStatuses,
        Func<TCapability, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<bool>> dispatch,
        CancellationToken cancellationToken)
        where TCapability : class
    {
        var accepted = new List<Guid>();
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
                    if (await dispatch(capability, download.ExternalId, client.Settings, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        accepted.Add(download.Id);
                    }
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

        return accepted;
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
            var matches = await downloadRepository.QueryAllAsync(action.Filter, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var download in matches)
            {
                targets[download.Id] = download;
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
                JobType = MagpieJobs.Types.ReleaseAutoGrabItem,
                Parameters = new Dictionary<string, string>()
                {
                    [MagpieJobs.Parameters.ContentItemId] = contentItemId.ToString("D")
                },
                Mode = JobExecutionMode.Queue,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The manual retry enqueued ReleaseAutoGrabItem for content item {ContentItemId}",
            contentItemId);
    }

    private async Task<bool> TryRemoveFromClientAsync(Download download, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(download.ExternalId))
        {
            return true;
        }

        var client = await downloadClientRepository.GetByIdAsync(download.DownloadClientId, cancellationToken)
            .ConfigureAwait(false);
        var integration = client is null
            ? null
            : downloadClientIntegrations.LastOrDefault(
                candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal));
        if (client is null || integration is null)
        {
            return false;
        }

        try
        {
            return await integration.RemoveDownloadAsync(download.ExternalId, true, client.Settings, cancellationToken)
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
            return false;
        }
    }

    private async Task<bool> DeleteWithFilesAsync(Download download, CancellationToken cancellationToken)
    {
        if (!await TryRemoveFromClientAsync(download, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await downloadRepository.DeleteAsync(download.Id, null, cancellationToken).ConfigureAwait(false);
        return true;
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
        DownloadStatus Status,
        DateTime? DateCompleted,
        decimal? Progress,
        DateTime? LastProgressAt,
        DownloadFailureReason? FailureReason,
        DateTime? FailedAt);
}
