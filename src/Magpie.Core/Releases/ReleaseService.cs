using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Integrations;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ContentEventImpact = Magpie.Sdk.Events.ContentEventImpact;

namespace Magpie.Core.Releases;

internal sealed class ReleaseService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IIndexerRepository indexerRepository,
    IReleaseRepository releaseRepository,
    IReleaseScopeRepository releaseScopeRepository,
    IDownloadScopeRepository downloadScopeRepository,
    IDownloadRepository downloadRepository,
    ReleaseBlockList releaseBlockList,
    IDownloadClientRepository downloadClientRepository,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadService downloadService,
    IRootFolderService rootFolderService,
    IFileSystem fileSystem,
    IAuditRecordRepository auditRecordRepository,
    IJobManager jobManager,
    IJobExecutionRepository jobExecutionRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    IReleaseEvaluationService releaseEvaluationService,
    IEnumerable<IReleaseComparer> releaseComparers,
    TimeProvider timeProvider,
    ILogger<ReleaseService> logger) : IReleaseService
{
    private static readonly TimeSpan DownloadBackoffBaseDelay = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DownloadBackoffCap = TimeSpan.FromMinutes(30);

    private static readonly DownloadFailureReason[] NonAttemptFailureReasons =
    [
        DownloadFailureReason.AmbiguousDispatchFailure
    ];

    private const int PageSize = 200;

    private const double DownloadBackoffFactor = 2.0;

    private enum DownloadDecision
    {
        Proceed,
        RetryAfter
    }

    private static bool RefersToSameRelease(Release left, Release right)
    {
        var leftKeys = NormalizedIdentityKeys(left);
        var rightKeys = NormalizedIdentityKeys(right);
        var leftImmutableKeys = ImmutableIdentityKeys(leftKeys);
        var rightImmutableKeys = ImmutableIdentityKeys(rightKeys);
        if (leftImmutableKeys.Count > 0 && rightImmutableKeys.Count > 0)
        {
            return leftImmutableKeys.Overlaps(rightImmutableKeys);
        }

        return leftKeys.Overlaps(rightKeys) || SourceIdentities(left).Overlaps(SourceIdentities(right));
    }

    private static HashSet<(string Authority, string Value)> NormalizedIdentityKeys(Release release) =>
        release.IdentityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => (
                key.Authority.Trim().ToLowerInvariant(),
                key.Value.Trim().ToLowerInvariant()))
            .ToHashSet();

    private static HashSet<(string Authority, string Value)> ImmutableIdentityKeys(
        HashSet<(string Authority, string Value)> keys) =>
        keys.Where(key => key.Authority != "fallback" &&
            !key.Authority.StartsWith("indexer:", StringComparison.Ordinal)).ToHashSet();

    private static HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)> SourceIdentities(Release release)
    {
        var identities = new HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)>();
        foreach (var source in release.Sources)
        {
            AddSourceIdentity(identities, source.IndexerId, source.ExternalId, source.InfoUrl);
        }

        AddSourceIdentity(identities, release.IndexerId, release.ExternalId, release.InfoUrl);
        return identities;
    }

    private static void AddSourceIdentity(
        HashSet<(Guid IndexerId, string? ExternalId, Uri? InfoUrl)> identities,
        Guid? indexerId,
        string? externalId,
        Uri? infoUrl)
    {
        if (indexerId is not { } value)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            identities.Add((value, externalId, null));
        }
        else if (infoUrl is not null)
        {
            identities.Add((value, null, infoUrl));
        }
    }

    private static ContentEventImpact? GrabScopeImpact(IReadOnlyCollection<Guid> contentScopeIds)
    {
        return contentScopeIds.Count switch
        {
            0 => null,
            1 => new() { HierarchyItemId = contentScopeIds.First() },
            _ => new() { BatchIds = [.. contentScopeIds] }
        };
    }

    private static bool IsActiveDownloadStatus(DownloadStatus status) => !DownloadStatuses.Finished.Contains(status);

    private static IReadOnlyList<DownloadClient> FilterEligibleClients(IEnumerable<DownloadClient> clients)
    {
        return [.. clients
            .Where(c => c.Enabled && c.Status != IntegrationHealthStatus.Unhealthy)
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Id)];
    }

    private static IReadOnlyList<(Release Release, Guid? SourceId)> ReleaseSourceCandidates(Release release)
    {
        if (release.Sources.Count == 0)
        {
            return [(release, null)];
        }

        return
        [
            ..release.Sources
                .OrderByDescending(source => source.InfoUrl is not null &&
                    source.InfoUrl.Scheme is "magnet" or "http" or "https")
                .ThenBy(source => source.IndexerId is null)
                .ThenBy(source => source.IndexerId)
                .ThenBy(source => source.ExternalId, StringComparer.Ordinal)
                .ThenBy(source => source.InfoUrl?.OriginalString, StringComparer.Ordinal)
                .ThenBy(source => source.Id)
                .Select(source =>
                {
                    var candidate = release with
                    {
                        IndexerId = source.IndexerId,
                        Protocol = source.Protocol,
                        ExternalId = source.ExternalId,
                        Title = source.Title,
                        PublishDate = source.PublishDate,
                        Size = source.Size,
                        InfoUrl = source.InfoUrl,
                        Categories = [.. source.Categories],
                        Languages = [.. source.Languages],
                        Properties = source.Properties,
                    };
                    return (candidate, (Guid?)source.Id);
                })
        ];
    }

    private static DownloadDecisionInfo EvaluateDownloadBackoff(
        DownloadAttempt? previousAttempt,
        DownloadClient client,
        TimeProvider timeProvider)
    {
        if (previousAttempt is null || previousAttempt.Status != DownloadStatus.Failed)
        {
            return new(DownloadDecision.Proceed, null, TimeSpan.Zero);
        }

        var elapsed = timeProvider.GetUtcNow().UtcDateTime - previousAttempt.LastAttemptUtc;
        var requiredBackoff = ComputeDownloadBackoff(previousAttempt.AttemptNumber);

        if (elapsed >= requiredBackoff)
        {
            return new(DownloadDecision.Proceed, null, TimeSpan.Zero);
        }

        var remaining = requiredBackoff - elapsed;
        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Previous attempt failed {0:F1}s ago on client {1}; backoff requires {2:F1}s.",
            elapsed.TotalSeconds,
            client.Name,
            requiredBackoff.TotalSeconds);

        return new(DownloadDecision.RetryAfter, reason, remaining);
    }

    private static TimeSpan ComputeDownloadBackoff(int previousAttemptNumber)
    {
        var n = Math.Max(1, previousAttemptNumber);
        var multiplier = Math.Pow(DownloadBackoffFactor, n - 1);
        var seconds = Math.Min(DownloadBackoffBaseDelay.TotalSeconds * multiplier, DownloadBackoffCap.TotalSeconds);
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = DownloadBackoffBaseDelay.TotalSeconds;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static void CopyReleaseIdentityKeys(Release release, Download download)
    {
        foreach (var key in release.IdentityKeys
            .Where(k => !string.IsNullOrWhiteSpace(k.Authority) && !string.IsNullOrWhiteSpace(k.Value))
            .DistinctBy(k => (k.Authority.Trim(), k.Value.Trim())))
        {
            download.ReleaseIdentityKeys.Add(new ReleaseIdentityKey
            {
                Authority = key.Authority.Trim(),
                Value = key.Value.Trim(),
                Ordinal = download.ReleaseIdentityKeys.Count
            });
        }
    }

    private static bool IsTerminalDownload(Download download) =>
        download.Status is DownloadStatus.Failed or DownloadStatus.Faulted or DownloadStatus.Cancelled;

    private static DecisionResult ResolveDecision(
        Release release,
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId,
        ReleaseAssessment? score,
        Guid? qualityTierListId)
    {
        if (downloadsByReleaseId.TryGetValue(release.Id, out var download))
        {
            return new DecisionResult(

                ReleaseHistoryDecisionKind.Grabbed,
                download.StatusReason ?? "Grabbed",
                download.CreatedAt);
        }

        if (score is not null)
        {
            if (score.IsAcceptable != true)
            {
                return new DecisionResult(
                    ReleaseHistoryDecisionKind.Skipped,
                    JoinReasons(score.QualityReasons) ?? "No matching quality tier",
                    null);
            }

            if (score.IsCutoffMet != true)
            {
                return new DecisionResult(ReleaseHistoryDecisionKind.Skipped, "Below cutoff tier", null);
            }

            return new DecisionResult(ReleaseHistoryDecisionKind.Stalled, "Eligible but not yet grabbed", null);
        }

        var stalledReason = qualityTierListId is null
            ? "No quality tier list configured"
            : "Scoring did not produce a verdict";
        return new DecisionResult(ReleaseHistoryDecisionKind.Stalled, stalledReason, null);
    }

    private static string? JoinReasons(IReadOnlyList<string>? reasons)
    {
        if (reasons is null || reasons.Count == 0)
        {
            return null;
        }

        return string.Join("; ", reasons);
    }

    private static Dictionary<(Guid IndexerId, string ExternalId), Download> BuildDownloadsByIndexerExternalId(
        IReadOnlyList<Release> releases,
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId)
    {
        var keyByReleaseId = new Dictionary<Guid, (Guid IndexerId, string ExternalId)>();
        foreach (var release in releases)
        {
            if (release.IndexerId is { } indexerId && !string.IsNullOrWhiteSpace(release.ExternalId))
            {
                keyByReleaseId[release.Id] = (indexerId, release.ExternalId!);
            }
        }

        var downloadsByKey = new Dictionary<(Guid IndexerId, string ExternalId), Download>();
        foreach (var download in downloadsByReleaseId.Values)
        {
            if (download.ReleaseId is not { } downloadReleaseId ||
                !keyByReleaseId.TryGetValue(downloadReleaseId, out var key))
            {
                continue;
            }

            if (!downloadsByKey.TryGetValue(key, out var existing) || download.CreatedAt > existing.CreatedAt)
            {
                downloadsByKey[key] = download;
            }
        }

        return downloadsByKey;
    }

    private static (DownloadStatus? AcquisitionStatus, DownloadFailureReason? FailureReason) ResolveAcquisition(
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId,
        IReadOnlyDictionary<(Guid IndexerId, string ExternalId), Download> downloadsByIndexerExternalId,
        Release release)
    {
        if (!downloadsByReleaseId.TryGetValue(release.Id, out var download) &&
            (release.IndexerId is not { } indexerId ||
                string.IsNullOrWhiteSpace(release.ExternalId) ||
                !downloadsByIndexerExternalId.TryGetValue((indexerId, release.ExternalId!), out download)))
        {
            return (null, null);
        }

        var failureReason = DownloadStatuses.Failures.Contains(download.Status)
            ? download.FailureReason
            : null;
        return (download.Status, failureReason);
    }

    private static Dictionary<Guid, string> IndexerNamesById(IEnumerable<Indexer> indexers)
    {
        return indexers.ToDictionary(i => i.Id, i => i.Name);
    }

    private static Dictionary<Guid, Download> LatestDownloadsByReleaseId(IEnumerable<Download> downloads)
    {
        return downloads
            .Where(d => d.ReleaseId is not null)
            .GroupBy(d => d.ReleaseId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id).First());
    }

    private static IReadOnlyList<LogicalRelease> CoalesceLogicalReleases(IEnumerable<Release> releases)
    {
        var groups = new List<(
            Release Release,
            List<Guid> ReleaseIds,
            List<Release> Observations,
            List<ReleaseSource> Sources)>();
        foreach (var release in releases.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
        {
            EnsureViewSource(release);
            var matchingGroupIndexes = Enumerable.Range(0, groups.Count)
                .Where(index => groups[index].Observations.Any(existing =>
                    RefersToSameRelease(existing, release)))
                .ToArray();
            if (matchingGroupIndexes.Length == 0)
            {
                groups.Add((release, [release.Id], [release], [.. release.Sources]));
                continue;
            }

            var group = groups[matchingGroupIndexes[0]];
            group.ReleaseIds.Add(release.Id);
            group.Observations.Add(release);
            group.Sources.AddRange(release.Sources);
            foreach (var index in matchingGroupIndexes.Skip(1).Reverse())
            {
                var mergedGroup = groups[index];
                group.ReleaseIds.AddRange(mergedGroup.ReleaseIds);
                group.Observations.AddRange(mergedGroup.Observations);
                group.Sources.AddRange(mergedGroup.Sources);
                groups.RemoveAt(index);
            }
        }

        return
        [
            ..groups.Select(group => new LogicalRelease(
                group.Release,
                group.ReleaseIds,
                group.Sources
                    .DistinctBy(source => (source.IndexerId, source.ExternalId, source.InfoUrl))
                    .ToArray()))
        ];
    }

    private static void EnsureViewSource(Release release)
    {
        if (release.Sources.Count > 0)
        {
            return;
        }

        release.Sources.Add(new ReleaseSource
        {
            Key = string.Empty,
            ReleaseId = release.Id,
            IndexerId = release.IndexerId,
            Protocol = release.Protocol,
            ExternalId = release.ExternalId,
            Title = release.Title,
            PublishDate = release.PublishDate,
            Size = release.Size,
            InfoUrl = release.InfoUrl,
            Categories = [.. release.Categories],
            Languages = [.. release.Languages],
            Properties = release.Properties,
            CreatedAt = release.CreatedAt,
            ObservedAt = release.ObservedAt,
        });
    }

    private async Task<(Release Release, bool Created)> UpsertAsync(
        Release release,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (await releaseRepository.UpdateAsync(release, userId, cancellationToken).ConfigureAwait(false))
        {
            return (release, false);
        }

        try
        {
            var created = await releaseRepository.CreateAsync(release, userId, cancellationToken).ConfigureAwait(false);
            return (created, true);
        }
        catch (DbUpdateException)
        {
            if (await releaseRepository.UpdateAsync(release, userId, cancellationToken).ConfigureAwait(false))
            {
                return (release, false);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<ContentRelease>?> GetReleaseViewAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchyItem is null)
        {
            return null;
        }

        var contentHierarchyItemIds = await ListContentReleaseScopeNodeIdsAsync(
            contentHierarchyItemId,
            cancellationToken)
            .ConfigureAwait(false);

        var releases = await releaseRepository.QueryAllAsync(
            new ReleaseQuery { ContentHierarchyItemIds = contentHierarchyItemIds, IncludeUnmatched = true },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        if (releases.Count == 0)
        {
            return [];
        }

        var matchedReleaseIds = (await releaseScopeRepository.QueryAllAsync(
                new ReleaseScopeQuery { ReleaseIds = [.. releases.Select(release => release.Id)] },
                sort: null,
                cancellationToken).ConfigureAwait(false))
            .Where(scope => scope.Status == ReleaseScopeStatus.Matched &&
                contentHierarchyItemIds.Contains(scope.ContentHierarchyItemId))
            .Select(scope => scope.ReleaseId)
            .ToHashSet();

        var releaseGroups = CoalesceLogicalReleases(releases);

        var indexers = await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var indexerNamesById = IndexerNamesById(indexers);

        var downloadsByReleaseId = await LoadDownloadsByReleaseIdAsync(
            contentHierarchyItemIds,
            releases,
            cancellationToken)
            .ConfigureAwait(false);
        var logicalDownloadsByReleaseId = new Dictionary<Guid, Download>();

        foreach (var group in releaseGroups)
        {
            var latest = group.ReleaseIds
                .Where(downloadsByReleaseId.ContainsKey)
                .Select(releaseId => downloadsByReleaseId[releaseId])
                .OrderByDescending(download => download.CreatedAt)
                .FirstOrDefault();
            if (latest is not null)
            {
                logicalDownloadsByReleaseId[group.Release.Id] = latest;
            }
        }

        var downloadsByIndexerExternalId = BuildDownloadsByIndexerExternalId(releases, downloadsByReleaseId);

        var acquisitionJobs = await jobExecutionRepository.QueryAllAsync(new JobExecutionQuery
        {
            JobType = MagpieJobs.Types.ReleaseDownloadItem,
            Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
            ParameterValueReferences = contentHierarchyItemIds.Select(id => id.ToString("D")).ToArray()
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var evaluationContext = await releaseEvaluationService
            .CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        var matchTarget = await ResolveMatchTargetAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<ContentRelease>(releaseGroups.Count);
        foreach (var group in releaseGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = group.Release;

            string? indexerName = null;
            if (release.IndexerId is { } indexerId)
            {
                indexerNamesById.TryGetValue(indexerId, out indexerName);
            }

            ReleaseAssessment? score = null;
            if (evaluationContext.QualityTierList is not null)
            {
                try
                {
                    score = await releaseEvaluationService
                        .ScoreForContentAsync(
                            release,
                            contentHierarchyItemId,
                            evaluationContext.CurrentFileQualityRank,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(
                        ex,
                        "The evaluation service failed to score release {ReleaseId} against tier list " +
                        "{QualityTierListId}",
                        release.Id,
                        evaluationContext.QualityTierListId);
                }
            }

            var (acquisitionStatus, failureReason) = ResolveAcquisition(
                logicalDownloadsByReleaseId,
                downloadsByIndexerExternalId,
                release);
            var (isMatch, rejectionReason) = MatchRelease(release, matchTarget, contentHierarchyItemId);
            var waitingJob = acquisitionJobs
                .Where(job => job.CancellationRequestedAt is null &&
                    job.StatusMessage?.StartsWith("Waiting for destination capacity: ",
                        StringComparison.Ordinal) == true &&
                    job.Parameters.TryGetValue(MagpieJobs.Parameters.ReleaseId, out var rawReleaseId) &&
                    Guid.TryParse(rawReleaseId, out var jobReleaseId) && group.ReleaseIds.Contains(jobReleaseId) &&
                    job.Parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawTargetId) &&
                    Guid.TryParse(rawTargetId, out var targetId) && contentHierarchyItemIds.Contains(targetId))
                .OrderByDescending(job => job.ModifiedAt ?? job.CreatedAt)
                .FirstOrDefault();
            var waitingReason = waitingJob is not null &&
                (!logicalDownloadsByReleaseId.TryGetValue(release.Id, out var latestDownload) ||
                    (latestDownload.CreatedAt < waitingJob.CreatedAt ||
                        Guid.TryParse(waitingJob.Parameters.GetValueOrDefault(
                            ReleaseDownloadItemJob.CapacityPreviousDownloadId), out var previousDownloadId) &&
                        latestDownload.Id == previousDownloadId) &&
                    DownloadStatuses.Finished.Contains(latestDownload.Status))
                ? waitingJob.StatusMessage : null;
            var decision = ResolveDecision(
                release,
                logicalDownloadsByReleaseId,
                score,
                evaluationContext.QualityTierListId);

            rows.Add(new ContentRelease
            {
                Id = release.Id,
                Title = release.Title,
                IndexerId = release.IndexerId,
                IndexerName = indexerName,
                Protocol = release.Protocol,
                Size = release.Size,
                PublishDate = release.PublishDate,
                InfoUrl = release.InfoUrl,
                ExternalId = release.ExternalId,

                Categories = [.. release.Categories],
                Languages = [.. release.Languages],
                Properties = release.Properties,
                Score = score?.MatchedRankOrdinal,
                IsAcceptable = score?.IsAcceptable,
                MeetsCutoff = score?.IsCutoffMet,
                IsMatch = isMatch,
                RejectionReason = rejectionReason,
                AcquisitionStatus = acquisitionStatus,
                AcquisitionWaitingReason = waitingReason,
                FailureReason = failureReason,
                Decision = decision.Kind,
                DecisionReason = decision.Reason,
                GrabbedAt = decision.GrabbedAt,
                Status = group.ReleaseIds.Any(matchedReleaseIds.Contains)
                    ? ReleaseScopeStatus.Matched
                    : ReleaseScopeStatus.Unmatched,
            });
        }

        rows.Sort((a, b) =>
        {
            var aKey = a.GrabbedAt ?? a.PublishDate;
            var bKey = b.GrabbedAt ?? b.PublishDate;
            return bKey.CompareTo(aKey);
        });

        return rows;
    }

    /// <inheritdoc />
    public async Task<bool> PatchContentReleaseAsync(
        Guid contentHierarchyItemId,
        Guid releaseId,
        ContentReleasePatch patch,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var nodeIds = await ListContentReleaseScopeNodeIdsAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var scopes = (await releaseScopeRepository.QueryAllAsync(
                new ReleaseScopeQuery { ReleaseId = releaseId },
                sort: null,
                cancellationToken).ConfigureAwait(false))
            .Where(scope => nodeIds.Contains(scope.ContentHierarchyItemId))
            .ToList();
        if (scopes.Count == 0)
        {
            return false;
        }

        if (patch.Status is not { Value: var status })
        {
            return true;
        }

        await transactionManager.ExecuteWithTransactionAsync(
            async transactionCancellationToken =>
            {
                foreach (var scope in scopes.Where(scope => scope.Status != status))
                {
                    await releaseScopeRepository.UpdateAsync(
                        scope with { Status = status },
                        userId,
                        transactionCancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<Download?> AcquireAsync(
        Guid releaseId,
        Guid? userId,
        Guid? correlationId = null,
        IProgressReporter? progress = null,
        bool interactive = false,
        Guid? grabTargetContentHierarchyItemId = null,
        CancellationToken cancellationToken = default)
    {
        return AcquireAsync(
            new ReleaseAcquisitionContext(
                releaseId,
                userId,
                correlationId,
                interactive,
                grabTargetContentHierarchyItemId),
            progress,
            cancellationToken);
    }

    public async Task<Release?> SelectBestReleaseAsync(
        Guid contentItemId,
        Guid? userId = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        return (await SelectReleasesAsync(contentItemId, allAcceptable: false, userId, correlationId, cancellationToken)
            .ConfigureAwait(false)).FirstOrDefault();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Release>> SelectAcceptableReleasesAsync(
        Guid contentItemId,
        Guid? userId = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        return SelectReleasesAsync(contentItemId, allAcceptable: true, userId, correlationId, cancellationToken);
    }

    private async Task<IReadOnlyList<Release>> SelectReleasesAsync(
        Guid contentItemId,
        bool allAcceptable,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var page = await contentHierarchyItemRepository
            .QueryAsync(
                new ContentHierarchyItemCriteria { ContentItemId = contentItemId },
                new PagingParameter { PageNumber = 1, PageSize = 1 },
                sort: null,
                cancellationToken)
            .ConfigureAwait(false);
        var item = page.Items.FirstOrDefault();
        if (item?.ContentItem is not { } contentItem)
        {
            return [];
        }

        var candidate = new CandidateContentItem(contentItemId, item.Id, contentItem.Fulfillment);
        return await SelectReleasesForCandidateAsync(
            candidate,
            allAcceptable,
            userId,
            correlationId,
            suppliedContext: null,
            writeAudit: true,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> MissingQualityTierListMessageAsync(
        ContentHierarchyItem item,
        CancellationToken cancellationToken)
    {
        var rootFolder = await rootFolderService
            .GetAsync(item.EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        var rootFolderName = rootFolder?.Name ?? item.EffectiveRootFolderId.ToString();

        return $"Root folder '{rootFolderName}' has no quality tier list configured for level '{item.Level}'.";
    }

    private async Task<Download?> AcquireAsync(
        ReleaseAcquisitionContext request,
        IProgressReporter? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0, "The acquisition will load the release.");

        var release = await releaseRepository.GetByIdAsync(request.ReleaseId, cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            throw new InvalidOperationException($"Release {request.ReleaseId} does not exist.");
        }

        var downloadScopeNodeIds = await ResolveDownloadScopeNodeIdsAsync(
            release.Id,
            request.GrabTargetContentHierarchyItemId,
            cancellationToken).ConfigureAwait(false);

        var existingDownload = await GetCurrentDownloadAsync(release.Id, cancellationToken).ConfigureAwait(false);
        if (existingDownload is not null)
        {
            progress?.Report(1, "The acquisition stopped because another acquisition is active for this release.");
            await QueueDownloadRefreshIfNeededAsync(existingDownload, cancellationToken).ConfigureAwait(false);
            return existingDownload;
        }

        if (!request.Interactive &&
            request.GrabTargetContentHierarchyItemId is { } grabTargetId &&
            (await releaseBlockList.ResolveStatusesAsync([release], grabTargetId, cancellationToken)
                .ConfigureAwait(false))
                .TryGetValue(release.Id, out var acquisition) &&
            acquisition.Status == DownloadStatus.Imported)
        {
            logger.LogInformation(
                "The release service did not grab release {ReleaseId} because a download of it is Imported for " +
                "{ContentHierarchyItemId}",
                release.Id,
                grabTargetId);
            progress?.Report(1, "The acquisition stopped because a download of this release is Imported for the item.");
            return null;
        }

        foreach (var contentHierarchyItemId in downloadScopeNodeIds)
        {
            var item = await contentHierarchyItemRepository
                .GetByIdAsync(contentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);
            if (item is not null && item.EffectiveQualityTierListId is null)
            {
                throw new MagpieException(
                    await MissingQualityTierListMessageAsync(item, cancellationToken).ConfigureAwait(false));
            }
        }

        progress?.Report(null, "The acquisition will select a download client.");

        var contentType = await ResolveContentTypeAsync(release, cancellationToken).ConfigureAwait(false);

        var enabledClients = FilterEligibleClients(
            await downloadClientRepository.ListAllAsync(cancellationToken).ConfigureAwait(false));

        if (enabledClients.Count == 0)
        {
            logger.LogDebug(
                "The release service did not grab release {ReleaseId} because no enabled download client " +
                "is configured",
                release.Id);
            progress?.Report(1, "The acquisition stopped because no enabled download client is configured.");
            return null;
        }

        var sourceCandidates = ReleaseSourceCandidates(release);
        var eligible = sourceCandidates
            .SelectMany(source => enabledClients.Select(client =>
                downloadClientIntegrations.LastOrDefault(
                    candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal))
                    is { } integration &&
                    integration.SupportedProtocols.Contains(
                        source.Release.Protocol,
                        StringComparer.OrdinalIgnoreCase)
                    ? (source.Release, source.SourceId, Client: client)
                    : default))
            .Where(attempt => attempt.Client is not null)
            .ToList();

        if (eligible.Count == 0)
        {
            logger.LogDebug(
                "The release service did not grab release {ReleaseId} because no enabled download client " +
                "supports {Protocol}",
                release.Id,
                release.Protocol);
            progress?.Report(
                1,
                $"The acquisition stopped because no enabled download client supports protocol {release.Protocol}.");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(null, "The download client will acquire the release.");

        Exception? lastFailure = null;
        Guid? lastClientId = null;
        Guid? lastSourceId = null;
        var lastAttemptRelease = release;
        var taintedClientIds = new HashSet<Guid>();
        var ambiguousDispatch = false;
        var allRejectionsTransient = true;
        for (var i = 0; i < eligible.Count; i++)
        {
            var (sourceRelease, sourceId, downloadClient) = eligible[i];
            if (taintedClientIds.Contains(downloadClient.Id))
            {
                continue;
            }

            lastClientId = downloadClient.Id;
            lastSourceId = sourceId;
            lastAttemptRelease = sourceRelease;
            cancellationToken.ThrowIfCancellationRequested();

            if (await DeferForDestinationCapacityAsync(sourceRelease, downloadScopeNodeIds, request, progress,
                cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var previousAttempt = await GetPreviousAttemptAsync(release.Id, downloadClient.Id, cancellationToken)
                .ConfigureAwait(false);
            var decision = EvaluateDownloadBackoff(previousAttempt, downloadClient, timeProvider);
            if (decision.Kind == DownloadDecision.RetryAfter)
            {
                logger.LogInformation(
                    "The download policy deferred release {ReleaseId} on client {DownloadClientId} by " +
                    "{RetryAfter} because {Reason}",
                    release.Id,
                    downloadClient.Id,
                    decision.RetryAfter,
                    decision.Reason);
                throw new InvalidOperationException(decision.Reason ?? "Deferred by download policy.");
            }

            Download? acquiredDownload = null;
            try
            {
                var grabResult = await downloadService
                    .GrabAsync(
                        sourceRelease,
                        downloadClient,
                        downloadClient.Settings,
                        contentType: contentType,
                        interactive: request.Interactive,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (grabResult.AlreadyTainted)
                {
                    taintedClientIds.Add(downloadClient.Id);
                    logger.LogDebug(
                        "The download service suppressed the grab of release {ReleaseId} on client " +
                        "{DownloadClientId} ({DownloadClientName}) because {Reason}",
                        release.Id,
                        downloadClient.Id,
                        downloadClient.Name,
                        grabResult.ErrorMessage ?? "tainted by prior Failed grab");
                    continue;
                }

                if (!grabResult.Success || grabResult.DownloadInfo is null)
                {
                    allRejectionsTransient &= grabResult.Transient;
                    lastFailure = new InvalidOperationException(
                        grabResult.ErrorMessage ?? "Download service refused the grab.");
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(null, "The acquisition will save the download.");

                var download = await transactionManager.ExecuteWithTransactionAsync(
                    async transactionCancellationToken =>
                    {
                        var persisted = await UpsertDownloadAsync(
                            sourceRelease,
                            sourceId,
                            downloadClient,
                            grabResult.DownloadInfo,
                            request.UserId,
                            transactionCancellationToken).ConfigureAwait(false);
                        await EnsureDownloadScopeAsync(
                            persisted.Id,
                            downloadScopeNodeIds,
                            transactionCancellationToken).ConfigureAwait(false);
                        return persisted;
                    },
                    cancellationToken).ConfigureAwait(false);

                progress?.Report(1, "The acquisition completed.");
                acquiredDownload = download;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                ambiguousDispatch = true;
                logger.LogError(
                    ex,
                    "The grab of release {ReleaseId} failed on client {DownloadClientId} " +
                    "({DownloadClientName}) after dispatch",
                    release.Id,
                    downloadClient.Id,
                    downloadClient.Name);
                break;
            }

            if (acquiredDownload is not null)
            {
                return acquiredDownload;
            }
        }

        if (ambiguousDispatch)
        {
            var ambiguousReason =
                $"The grab of release {release.Id} failed after dispatch to a client; the outcome is " +
                "unknown. " + (lastFailure?.Message ?? string.Empty);
            await RecordFailedDownloadAsync(
                lastAttemptRelease,
                lastSourceId,
                lastClientId!.Value,
                DownloadFailureReason.AmbiguousDispatchFailure,
                DownloadStatus.Faulted,
                ambiguousReason,
                downloadScopeNodeIds,
                request.UserId,
                cancellationToken).ConfigureAwait(false);
            await eventBus.PublishAsync(
                ReleaseEventBuilder.BuildReleaseGrabRejectedEvent(
                    lastAttemptRelease.Id,
                    lastAttemptRelease.IndexerId ?? Guid.Empty,
                    ambiguousReason,
                    GrabScopeImpact(downloadScopeNodeIds)),
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(ambiguousReason, lastFailure);
        }

        var taintedClientCount = taintedClientIds.Count;
        if (lastFailure is null && taintedClientCount > 0)
        {
            logger.LogInformation(
                "The release service suppressed the grab of release {ReleaseId} because all {TaintedCount} " +
                "eligible clients have an active Failed taint",
                release.Id,
                taintedClientCount);
            await TryWriteSuppressedAuditAsync(release, taintedClientCount, request.UserId, cancellationToken)
                .ConfigureAwait(false);
            await eventBus.PublishAsync(
                ReleaseEventBuilder.BuildReleaseGrabRejectedEvent(
                    release.Id,
                    release.IndexerId ?? Guid.Empty,
                    $"All {taintedClientCount} eligible download clients have an active Failed taint.",
                    GrabScopeImpact(downloadScopeNodeIds)),
                cancellationToken).ConfigureAwait(false);
            progress?.Report(1, "The acquisition stopped because a prior failed grab marked the release as unusable.");
            return null;
        }

        var allFailedReason = lastFailure?.Message is { Length: > 0 } message
            ? $"All eligible download clients rejected the release: {message}"
            : "All eligible download clients rejected the release.";
        await RecordFailedDownloadAsync(
            lastAttemptRelease,
            lastSourceId,
            lastClientId!.Value,
            DownloadFailureReason.ClientRejected,
            lastFailure is not null && allRejectionsTransient ? DownloadStatus.Faulted : DownloadStatus.Failed,
            allFailedReason,
            downloadScopeNodeIds,
            request.UserId,
            cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            ReleaseEventBuilder.BuildReleaseGrabRejectedEvent(
                lastAttemptRelease.Id,
                lastAttemptRelease.IndexerId ?? Guid.Empty,
                allFailedReason,
                GrabScopeImpact(downloadScopeNodeIds)),
            cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException(allFailedReason, lastFailure);
    }

    private async Task QueueDownloadRefreshIfNeededAsync(Download download, CancellationToken cancellationToken)
    {
        if (download.Status is DownloadStatus.Imported or DownloadStatus.Failed or DownloadStatus.Faulted)
        {
            return;
        }

        try
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.DownloadRefresh,
                    Mode = JobExecutionMode.Queue,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release service failed to enqueue the DownloadRefresh job for download {DownloadId}",
                download.Id);
        }
    }

    private async Task<bool> DeferForDestinationCapacityAsync(
        Release release,
        IReadOnlyList<Guid> downloadScopeNodeIds,
        ReleaseAcquisitionContext request,
        IProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        foreach (var contentHierarchyItemId in downloadScopeNodeIds)
        {
            var item = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);
            var root = item is null ? null : await rootFolderService.GetAsync(item.EffectiveRootFolderId,
                cancellationToken).ConfigureAwait(false);
            var capacity = root is null ? null : RootFolderService.ReadDestinationCapacity(fileSystem, root.Path);
            var reason = RootFolderService.CapacityWaitingReason(release.Size, capacity?.AvailableBytes);
            if (reason is null)
            {
                continue;
            }

            progress?.Report(1, reason);
            var previousDownloads = await downloadRepository.QueryAllAsync(new DownloadQuery
            {
                ReleaseId = release.Id
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var previousDownload = previousDownloads.OrderByDescending(download => download.CreatedAt)
                .ThenByDescending(download => download.Id).FirstOrDefault();
            await jobManager.ExecuteAsync(new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.ReleaseDownloadItem,
                Mode = JobExecutionMode.Queue,
                IsSuccessor = true,
                NotBeforeAt = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(10),
                StatusMessage = reason,
                UserId = request.UserId,
                CorrelationId = request.CorrelationId,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.ReleaseId] = release.Id.ToString("D"),
                    [MagpieJobs.Parameters.ContentHierarchyItemId] =
                        (request.GrabTargetContentHierarchyItemId ?? contentHierarchyItemId).ToString("D"),
                    [MagpieJobs.Parameters.Interactive] = request.Interactive.ToString(),
                    [ReleaseDownloadItemJob.CapacityDeferredAt] = timeProvider.GetUtcNow().UtcDateTime
                        .ToString("O", CultureInfo.InvariantCulture),
                    [ReleaseDownloadItemJob.CapacityPreviousDownloadId] = previousDownload?.Id.ToString("D") ?? ""
                }
            }, cancellationToken).ConfigureAwait(false);
            await eventBus.PublishAsync(ReleaseEventBuilder.BuildReleaseGrabRejectedEvent(
                release.Id, release.IndexerId ?? Guid.Empty, reason, GrabScopeImpact(downloadScopeNodeIds)),
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task<IReadOnlyList<Guid>> ResolveDownloadScopeNodeIdsAsync(
        Guid releaseId,
        Guid? grabTargetContentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var releaseScopes = await releaseScopeRepository.QueryAllAsync(
            new ReleaseScopeQuery { ReleaseId = releaseId },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        var releaseScopeNodeIds = releaseScopes
            .Where(scope => scope.Status == ReleaseScopeStatus.Matched)
            .Select(scope => scope.ContentHierarchyItemId)
            .Distinct()
            .ToList();

        var needyNodeIds = await ListNodeIdsNeedingAcquisitionAsync(releaseScopeNodeIds, cancellationToken)
            .ConfigureAwait(false);
        var scopeNodeIds = releaseScopeNodeIds
            .Where(nodeId => nodeId == grabTargetContentHierarchyItemId || needyNodeIds.Contains(nodeId))
            .ToList();

        if (scopeNodeIds.Count > 0)
        {
            return scopeNodeIds;
        }

        if (grabTargetContentHierarchyItemId is { } target)

        {
            return [target];
        }

        throw new InvalidOperationException(
            $"Release acquisition requires a content hierarchy target for release {releaseId}.");
    }

    private async Task<HashSet<Guid>> ListNodeIdsNeedingAcquisitionAsync(
        IReadOnlyCollection<Guid> nodeIds,
        CancellationToken cancellationToken)
    {
        if (nodeIds.Count == 0)
        {
            return [];
        }

        var result = new HashSet<Guid>();
        foreach (var batch in nodeIds.Chunk(500))
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    Ids = batch,
                    EffectiveMonitoring = true,
                    Fulfillment = new()
                    {
                        And = [new()
                        {
                            Operator = CriteriaValueOperator.In,
                            Values = [FulfillmentStatus.Missing, FulfillmentStatus.Upgradable],
                        }],
                    },
                },
                new() { PageNumber = 1, PageSize = batch.Length },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            result.UnionWith(page.Items.Select(item => item.Id));
        }

        return result;
    }

    private async Task EnsureDownloadScopeAsync(
        Guid downloadId,
        IReadOnlyList<Guid> scopeNodeIds,
        CancellationToken cancellationToken)
    {
        var existingScopeNodeIds = (await downloadScopeRepository.QueryAsync(
            new DownloadScopeQuery { DownloadId = downloadId },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken)
            .ConfigureAwait(false)).Items.Select(scope => scope.ContentHierarchyItemId).Distinct().ToList();
        if (existingScopeNodeIds.Count > 0)
        {
            return;
        }

        foreach (var contentHierarchyItemId in scopeNodeIds.Distinct())
        {
            await downloadScopeRepository.CreateAsync(
                new DownloadScope
                {
                    Id = Guid.NewGuid(),
                    DownloadId = downloadId,
                    ContentHierarchyItemId = contentHierarchyItemId
                },
                null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> ResolveContentTypeAsync(Release release, CancellationToken cancellationToken)
    {
        var releaseScopes = await releaseScopeRepository.QueryAllAsync(
            new ReleaseScopeQuery { ReleaseId = release.Id },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        var scopeNodeIds = releaseScopes
            .Select(scope => scope.ContentHierarchyItemId)
            .Distinct()
            .ToList();
        foreach (var scopeNodeId in scopeNodeIds)
        {
            var node = await contentHierarchyItemRepository
                .GetByIdAsync(scopeNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (node is not null)
            {
                return node.ContentType;
            }
        }

        return null;
    }

    private async Task<DownloadAttempt?> GetPreviousAttemptAsync(
        Guid releaseId,
        Guid downloadClientId,
        CancellationToken cancellationToken)
    {
        var downloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery { ReleaseId = releaseId, DownloadClientId = downloadClientId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var rows = downloads
            .Where(d => !(d.FailureReason is { } reason && NonAttemptFailureReasons.Contains(reason)))
            .ToList();
        var prior = rows
            .OrderByDescending(d => d.ModifiedAt ?? d.CreatedAt)
            .FirstOrDefault();
        if (prior is null)
        {
            return null;
        }

        return new DownloadAttempt(prior.Id, prior.Status, rows.Count, prior.ModifiedAt ?? prior.CreatedAt);
    }

    private async Task<Download?> GetCurrentDownloadAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var downloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery { ReleaseId = releaseId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return downloads.FirstOrDefault(d => IsActiveDownloadStatus(d.Status));
    }

    private async Task<Download> UpsertDownloadAsync(
        Release release,
        Guid? releaseSourceId,
        DownloadClient downloadClient,
        DownloadInfo downloadInfo,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var completed = downloadInfo.Status is DownloadStatus.Downloaded or DownloadStatus.Imported
            ? (DateTime?)timeProvider.GetUtcNow().UtcDateTime
            : null;

        var existingDownloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery { ReleaseId = release.Id },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var existing = existingDownloads.FirstOrDefault(d => !DownloadStatuses.Finished.Contains(d.Status));

        if (existing is null)
        {
            var created = new Download
            {
                ReleaseId = release.Id,
                ReleaseSourceId = releaseSourceId,
                DownloadClientId = downloadClient.Id,
                ExternalId = downloadInfo.ExternalId,
                Title = release.Title,
                Status = downloadInfo.Status,
                StatusReason = downloadInfo.StatusReason,
                OutputPath = downloadInfo.OutputPath,
                DateCompleted = completed,
                HeldByPolicy = downloadInfo.HeldByPolicy,
                Progress = downloadInfo.ProgressPercent ?? 0m,
                LastProgressAt = timeProvider.GetUtcNow().UtcDateTime
            };
            CopyReleaseIdentityKeys(release, created);
            return await downloadRepository.CreateAsync(created, userId, cancellationToken).ConfigureAwait(false);
        }

        var updated = new Download
        {
            Id = existing.Id,
            ReleaseId = release.Id,
            ReleaseSourceId = releaseSourceId,
            DownloadClientId = downloadClient.Id,
            ExternalId = downloadInfo.ExternalId,
            Title = release.Title,
            Status = downloadInfo.Status,
            StatusReason = downloadInfo.StatusReason,
            OutputPath = downloadInfo.OutputPath,
            DateCompleted = completed,
            HeldByPolicy = downloadInfo.HeldByPolicy,
            Progress = downloadInfo.ProgressPercent,
            LastProgressAt = timeProvider.GetUtcNow().UtcDateTime,
            FailureReason = null,
            FailedAt = null,
            ClientRemovedAt = null
        };
        CopyReleaseIdentityKeys(release, updated);
        await downloadRepository.UpdateAsync(updated, userId, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task RecordFailedDownloadAsync(
        Release release,
        Guid? releaseSourceId,
        Guid downloadClientId,
        DownloadFailureReason failureReason,
        DownloadStatus status,
        string reason,
        IReadOnlyList<Guid> downloadScopeNodeIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var row = new Download
            {
                ReleaseId = release.Id,
                ReleaseSourceId = releaseSourceId,
                DownloadClientId = downloadClientId,
                ExternalId = string.Empty,
                Title = release.Title,
                Status = status,
                StatusReason = reason,
                FailureReason = failureReason,
                FailedAt = now
            };
            CopyReleaseIdentityKeys(release, row);

            await transactionManager.ExecuteWithTransactionAsync(
                async transactionCancellationToken =>

                {
                    var created = await downloadRepository
                        .CreateAsync(row, userId, transactionCancellationToken)
                        .ConfigureAwait(false);
                    await EnsureDownloadScopeAsync(
                        created.Id,
                        downloadScopeNodeIds,
                        transactionCancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The release service failed to write a client-rejected download row for release {ReleaseId}",
                release.Id);
        }
    }

    private async Task TryWriteSuppressedAuditAsync(
        Release release,
        int taintedClientCount,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditRecordRepository.CreateAsync(
                new AuditRecord
                {
                    Category = AuditCategories.Jobs,
                    Operation = "GrabSuppressed",
                    EntityType = nameof(Release),
                    EntityId = release.Id.ToString(),
                    Detail = $"All {taintedClientCount} eligible clients have an active Failed taint.",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = userId
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release service failed to write a GrabSuppressed audit row for release {ReleaseId}",
                release.Id);
        }
    }

    private async Task<IReadOnlyList<Guid>> ListContentReleaseScopeNodeIdsAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var descendants = await contentHierarchyItemRepository.QueryAllAsync(
            new ContentHierarchyItemCriteria { AncestorId = contentHierarchyItemId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return [.. descendants.Select(item => item.Id).Prepend(contentHierarchyItemId).Distinct()];
    }

    private async Task<Dictionary<Guid, Download>> LoadDownloadsByReleaseIdAsync(
        IReadOnlyList<Guid> contentHierarchyItemIds,
        IReadOnlyList<Release> releases,
        CancellationToken cancellationToken)
    {
        var downloads = new List<Download>();
        if (contentHierarchyItemIds.Count > 0)
        {
            downloads.AddRange(await downloadRepository.QueryAllAsync(
                new DownloadQuery { ContentHierarchyItemIds = contentHierarchyItemIds },
                new()
                {
                    PropertyName = nameof(Download.CreatedAt),
                    Direction = SortDirection.Descending
                },
                cancellationToken).ConfigureAwait(false));
        }

        var downloadsByReleaseId = LatestDownloadsByReleaseId(downloads);

        var missingReleaseIds = releases
            .Where(release => !downloadsByReleaseId.ContainsKey(release.Id))
            .Select(release => release.Id)
            .Distinct()
            .ToList();
        if (missingReleaseIds.Count > 0)
        {
            var missingDownloads = await downloadRepository.QueryAllAsync(
                new DownloadQuery { ReleaseIds = missingReleaseIds },
                new()
                {
                    PropertyName = nameof(Download.CreatedAt),
                    Direction = SortDirection.Descending
                },
                cancellationToken).ConfigureAwait(false);

            var latestByReleaseId = LatestDownloadsByReleaseId(missingDownloads);
            foreach (var releaseId in missingReleaseIds)
            {
                if (latestByReleaseId.TryGetValue(releaseId, out var download) && IsTerminalDownload(download))
                {
                    downloads.Add(download);
                }
            }
        }

        return LatestDownloadsByReleaseId(downloads);
    }

    private async Task<ReleaseEvaluationContext?> ResolveMatchTargetAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var context = await releaseEvaluationService
            .CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        return context.Target is null ? null : context;
    }

    private (bool? IsMatch, string? RejectionReason) MatchRelease(
        Release release,
        ReleaseEvaluationContext? matchTarget,
        Guid contentHierarchyItemId)
    {
        if (matchTarget is null)
        {
            return (false, "content hierarchy item not found");
        }

        try

        {
            var assessment = releaseEvaluationService.AssessMatch(release, [], matchTarget);
            var isMatch = assessment.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident;
            return (isMatch, isMatch ? null : assessment.MatchResult.ToString());
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "The evaluation service failed to match release {ReleaseId} to content hierarchy item " +
                "{ContentHierarchyItemId}",
                release.Id,
                contentHierarchyItemId);
            return (null, null);
        }
    }

    private async Task<IReadOnlyList<Release>> SelectReleasesForCandidateAsync(
        CandidateContentItem candidate,
        bool allAcceptable,
        Guid? userId,
        Guid? correlationId,
        ReleaseEvaluationContext? suppliedContext,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        var evaluationContext = suppliedContext
            ?? await releaseEvaluationService
                .CreateEvaluationContextAsync(candidate.ContentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);

        var releases = await LoadStoredReleasesAsync(candidate, userId, correlationId, writeAudit, cancellationToken)
            .ConfigureAwait(false);
        if (releases is null)
        {
            return [];
        }

        var releasesById = releases.ToDictionary(release => release.Id);

        var downloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery { ContentItemId = candidate.ContentHierarchyItemId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var acquisitionStatuses = await releaseBlockList.ResolveStatusesAsync(
            releases,
            candidate.ContentHierarchyItemId,
            cancellationToken)
            .ConfigureAwait(false);

        var qualityTierListId = evaluationContext.QualityTierListId;

        var scored = await SelectAcceptableReleasesAsync(
            candidate,
            releases,
            acquisitionStatuses,
            evaluationContext,
            userId,
            correlationId,
            writeAudit,
            cancellationToken)
            .ConfigureAwait(false);
        if (scored is null)
        {
            return [];
        }

        if (scored.Releases.Count == 0)
        {
            logger.LogInformation(
                "Content {ContentItemId} has no acceptable release among {ReleaseCount} evaluated with " +
                "tier list {QualityTierListId}: {NotMatched} target mismatches, {NotAcceptable} tier " +
                "mismatches, {NotAnUpgrade} non-upgrades, {Failed} failed",
                candidate.ContentItemId,
                releases.Count,
                qualityTierListId,
                scored.NotMatched,
                scored.NotAcceptable,
                scored.NotAnUpgrade,
                scored.Failed);
            if (writeAudit)
            {
                await TryWriteAuditAsync(
                    operation: "GrabSkipped",
                    entityType: nameof(ContentItem),
                    entityId: candidate.ContentItemId.ToString(),
                    detail: $"No acceptable release among {releases.Count} evaluated " +
                        $"(not-matched: {scored.NotMatched}, not-acceptable: {scored.NotAcceptable}, " +
                        $"not-an-upgrade: {scored.NotAnUpgrade}, failed-blocked: {scored.Failed}).",
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }

            return [];
        }

        var comparers = releaseComparers.ToList();
        var ranked = new List<(Release Release, ReleaseAssessment Result)>(scored.Releases.Count);
        List<object>? comparisonTrace = logger.IsEnabled(LogLevel.Trace) ? [] : null;
        var primaryGroups = scored.Releases
            .GroupBy(release => (
                Faulted: acquisitionStatuses.GetValueOrDefault(release.Release.Id).Status == DownloadStatus.Faulted,
                release.Result.MatchResult,
                EffectiveQualityRank: releaseEvaluationService.EffectiveQualityRank(release.Result, evaluationContext)))
            .OrderBy(group => group.Key.Faulted)
            .ThenByDescending(group => group.Key.MatchResult)
            .ThenBy(group => group.Key.EffectiveQualityRank);

        foreach (var primaryGroup in primaryGroups)
        {
            if (comparisonTrace is not null)
            {
                comparisonTrace.Add(new
                {
                    Criterion = "FaultedMatchResultAndEffectiveQualityRank",
                    primaryGroup.Key.Faulted,
                    primaryGroup.Key.MatchResult,
                    primaryGroup.Key.EffectiveQualityRank,
                    ReleaseIds = primaryGroup.Select(release => release.Release.Id),
                });
            }

            var tieGroups = new List<List<(Release Release, ReleaseAssessment Result)>>
            {
                primaryGroup.ToList(),
            };
            foreach (var comparer in comparers)
            {
                var refinedGroups = new List<List<(Release Release, ReleaseAssessment Result)>>();
                foreach (var tieGroup in tieGroups)
                {
                    if (tieGroup.Count < 2)
                    {
                        refinedGroups.Add(tieGroup);
                        continue;
                    }

                    var applicable = tieGroup.All(release => comparer.CanCompare(release.Release));
                    if (!applicable)
                    {
                        if (comparisonTrace is not null)
                        {
                            comparisonTrace.Add(new
                            {
                                Criterion = comparer.GetType().FullName,
                                Applied = false,
                                ReleaseIds = tieGroup.Select(release => release.Release.Id),
                            });
                        }

                        refinedGroups.Add(tieGroup);
                        continue;
                    }

                    tieGroup.Sort((left, right) =>
                    {
                        var comparison = comparer.Compare(left.Release, right.Release);
                        if (comparisonTrace is not null)
                        {
                            comparisonTrace.Add(new
                            {
                                LeftReleaseId = left.Release.Id,
                                RightReleaseId = right.Release.Id,
                                Criterion = comparer.GetType().FullName,
                                Applied = true,
                                Result = comparison,
                            });
                        }

                        return comparison;
                    });

                    var start = 0;
                    while (start < tieGroup.Count)
                    {
                        var end = start + 1;
                        while (end < tieGroup.Count &&
                            comparer.Compare(tieGroup[start].Release, tieGroup[end].Release) == 0)
                        {
                            end++;
                        }

                        refinedGroups.Add(tieGroup.GetRange(start, end - start));
                        start = end;
                    }
                }

                tieGroups = refinedGroups;
            }

            foreach (var tieGroup in tieGroups)
            {
                tieGroup.Sort((left, right) =>
                {
                    var comparison = Nullable.Compare(
                        left.Result.MatchedQualityTierId,
                        right.Result.MatchedQualityTierId);
                    return comparison != 0 ? comparison : left.Release.Id.CompareTo(right.Release.Id);
                });
                ranked.AddRange(tieGroup);
            }
        }

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace(
                "RANK ReleaseSearch {RankingJson}",
                JsonSerializer.Serialize(new
                {
                    Mode = "AutomaticSelection",
                    candidate.ContentHierarchyItemId,
                    ComparerOrder = comparers.Select(comparer => comparer.GetType().FullName),
                    Comparisons = comparisonTrace,
                    FinalOrder = ranked.Select((release, order) => new
                    {
                        Order = order,

                        release.Release.Id,
                        release.Release.Title,
                        MatchResult = release.Result.MatchResult.ToString(),
                        ActualQualityRank = release.Result.MatchedRankOrdinal,
                        EffectiveQualityRank = releaseEvaluationService.EffectiveQualityRank(
                            release.Result,
                            evaluationContext),
                    }),
                }));
        }

        if (allAcceptable)
        {
            var activeReleaseIds = downloads
                .Where(download => IsActiveDownloadStatus(download.Status) && download.ReleaseId is not null)
                .Select(download => download.ReleaseId!.Value)
                .ToHashSet();
            return [.. ranked
                .Select(release => release.Release)
                .Where(release => !activeReleaseIds.Contains(release.Id))];
        }

        if (!await CanStartNewDownloadAsync(
            candidate,
            downloads,
            evaluationContext,
            releasesById,
            userId,
            correlationId,
            writeAudit,
            cancellationToken)
            .ConfigureAwait(false))
        {
            return [];
        }

        return [ranked.First().Release];
    }

    private async Task<IReadOnlyList<Release>?> LoadStoredReleasesAsync(
        CandidateContentItem candidate,
        Guid? userId,
        Guid? correlationId,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        var releases = await releaseRepository.QueryAllAsync(
            new ReleaseQuery { ContentHierarchyItemId = candidate.ContentHierarchyItemId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        if (releases.Count != 0)
        {
            return releases;
        }

        logger.LogInformation(
            "The auto-grab found no stored releases for content {ContentItemId}",
            candidate.ContentItemId);
        if (writeAudit)
        {
            await TryWriteAuditAsync(
                operation: "GrabSkipped",
                entityType: nameof(ContentItem),
                entityId: candidate.ContentItemId.ToString(),
                detail: "No stored releases for content item.",
                userId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<bool> CanStartNewDownloadAsync(
        CandidateContentItem candidate,
        IReadOnlyList<Download> contentDownloads,
        ReleaseEvaluationContext evaluationContext,
        IReadOnlyDictionary<Guid, Release> releasesById,
        Guid? userId,
        Guid? correlationId,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        var activeDownloads = contentDownloads
            .Where(download => IsActiveDownloadStatus(download.Status))
            .ToList();
        if (activeDownloads.Count == 0)
        {
            return true;
        }

        foreach (var inFlight in activeDownloads)
        {
            if (inFlight.ReleaseId is { } inFlightReleaseId &&
                releasesById.TryGetValue(inFlightReleaseId, out var inFlightRelease))
            {
                var inFlightMatch = releaseEvaluationService.AssessMatch(inFlightRelease, [], evaluationContext);
                if (inFlightMatch.MatchResult is not (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
                {
                    logger.LogInformation(
                        "The auto-grab of content {ContentItemId} ignored release {InFlightReleaseId} on " +
                        "download {DownloadId} because it does not match",
                        candidate.ContentItemId,
                        inFlight.ReleaseId,
                        inFlight.Id);
                    continue;
                }
            }

            logger.LogInformation(
                "The auto-grab skipped content {ContentItemId} because release {InFlightReleaseId} is " +
                "already in flight on download {DownloadId}",
                candidate.ContentItemId,
                inFlight.ReleaseId,
                inFlight.Id);
            if (writeAudit)
            {
                await TryWriteAuditAsync(
                    operation: "GrabSkipped",
                    entityType: nameof(ContentItem),
                    entityId: candidate.ContentItemId.ToString(),
                    detail: $"Release {inFlight.ReleaseId} already in flight on download {inFlight.Id}.",
                    userId,
                    correlationId,
                    cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        return true;
    }

    private async Task<AcceptableReleaseSet?> SelectAcceptableReleasesAsync(
        CandidateContentItem candidate,
        IReadOnlyList<Release> releases,
        IReadOnlyDictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)> acquisitionStatuses,
        ReleaseEvaluationContext evaluationContext,
        Guid? userId,
        Guid? correlationId,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        var blockedReleaseIds = ReleaseBlockList.Blocked(acquisitionStatuses);
        var skippedFailed = 0;
        var skippedNotMatched = 0;
        var skippedNotAcceptable = 0;
        var skippedNotAnUpgrade = 0;

        var scored = new List<(Release Release, ReleaseAssessment Result)>();
        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (blockedReleaseIds.Contains(release.Id))
            {
                skippedFailed++;
                logger.LogDebug(
                    "The auto-grab skipped release {ReleaseId} for content {ContentItemId} because a download " +
                    "of it is {DownloadStatus}",
                    release.Id,
                    candidate.ContentItemId,
                    acquisitionStatuses[release.Id].Status);
                continue;
            }

            var assessment = releaseEvaluationService.AssessMatch(release, [], evaluationContext);
            if (assessment.MatchResult is not (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
            {
                skippedNotMatched++;
                logger.LogDebug(
                    "The auto-grab skipped release {ReleaseId} for content {ContentItemId} with match " +
                    "result {MatchResult}",
                    release.Id,
                    candidate.ContentItemId,
                    assessment.MatchResult);
                continue;
            }

            if (assessment.IsAcceptable is null)
            {
                logger.LogInformation(
                    "The release service stopped the auto-grab of content {ContentItemId} because no " +
                    "quality tier list exists for {ContentHierarchyItemId}",
                    candidate.ContentItemId,
                    candidate.ContentHierarchyItemId);
                if (writeAudit)
                {
                    await TryWriteAuditAsync(
                        operation: "GrabSkipped",
                        entityType: nameof(ContentItem),
                        entityId: candidate.ContentItemId.ToString(),
                        detail: "No quality tier list configured for content hierarchy.",
                        userId,
                        correlationId,

                        cancellationToken).ConfigureAwait(false);
                }

                return null;
            }

            if (assessment.IsAcceptable != true)
            {
                skippedNotAcceptable++;
                continue;
            }

            if (candidate.Fulfillment == FulfillmentStatus.Upgradable &&
                (evaluationContext.QualityTierList?.UpgradeAllowed != true ||
                    evaluationContext.CurrentFileQualityRank is null ||
                    !releaseEvaluationService.IsAcceptableUpgradeOverCurrentFile(
                        assessment,
                        release,
                        evaluationContext)))
            {
                skippedNotAnUpgrade++;
                continue;
            }

            scored.Add((release, assessment));
        }

        return new AcceptableReleaseSet(
            scored,
            skippedFailed,
            skippedNotMatched,
            skippedNotAcceptable,
            skippedNotAnUpgrade);
    }

    private async Task TryWriteAuditAsync(
        string operation,
        string entityType,
        string entityId,
        string detail,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditRecordRepository.CreateAsync(
                new AuditRecord
                {
                    Category = AuditCategories.Jobs,
                    Operation = operation,
                    EntityType = entityType,
                    EntityId = entityId,
                    Detail = detail,
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = userId,
                    CorrelationId = correlationId
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release service failed to write the {AuditOperation} audit row for {EntityType}/{EntityId}",
                operation,
                entityType,
                entityId);
        }
    }

    private readonly record struct DecisionResult(ReleaseHistoryDecisionKind Kind, string Reason, DateTime? GrabbedAt);

    private readonly record struct CandidateContentItem(
        Guid ContentItemId,
        Guid ContentHierarchyItemId,
        FulfillmentStatus Fulfillment);

    private readonly record struct DownloadDecisionInfo(
        DownloadDecision Kind,
        string? Reason,
        TimeSpan RetryAfter);

    private sealed record DownloadAttempt(
        Guid DownloadId,
        DownloadStatus Status,
        int AttemptNumber,
        DateTime LastAttemptUtc);

    private sealed record LogicalRelease(
        Release Release,
        IReadOnlyList<Guid> ReleaseIds,

        IReadOnlyList<ReleaseSource> Sources);

    private sealed record ReleaseAcquisitionContext(
        Guid ReleaseId,
        Guid? UserId,
        Guid? CorrelationId,
        bool Interactive,
        Guid? GrabTargetContentHierarchyItemId);

    private sealed record AcceptableReleaseSet(
        List<(Release Release, ReleaseAssessment Result)> Releases,
        int Failed,
        int NotMatched,
        int NotAcceptable,
        int NotAnUpgrade);
}
