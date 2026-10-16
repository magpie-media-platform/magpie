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
    IIndexerService indexerService,
    IReleaseRepository releaseRepository,
    IReleaseScopeRepository releaseScopeRepository,
    IDownloadScopeRepository downloadScopeRepository,
    IDownloadRepository downloadRepository,
    ReleaseBlockList releaseBlockList,
    ISettingsRepository settingsRepository,
    IDownloadClientRepository downloadClientRepository,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    IDownloadService downloadService,
    IRootFolderService rootFolderService,
    IFileSystem fileSystem,
    IAuditRecordRepository auditRecordRepository,
    IJobManager jobManager,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    IReleaseEvaluationService releaseEvaluationService,
    IEnumerable<IReleaseComparer> releaseComparers,
    TimeProvider timeProvider,
    ILogger<ReleaseService> logger) : IReleaseService
{
    private const string MonitoringSettingsGroup = "monitoring";

    private const string MaxSpreadKey = "max-spread";

    private const int PageSize = 200;

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

    private static void AddDownloadScope(Download download, IReadOnlyList<Guid> scopeNodeIds)
    {
        foreach (var contentHierarchyItemId in scopeNodeIds.Distinct())
        {
            download.Scope.Add(new DownloadScope
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = contentHierarchyItemId
            });
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

    private static IReadOnlyList<(Release Release, ReleaseSource? Source)> ReleaseSourceCandidates(
        Release release,
        IReadOnlySet<Guid> disabledConnectionIds)
    {
        if (release.Sources.Count == 0)
        {
            return [(release, null)];
        }

        return
        [
            ..release.Sources
                .Where(source => source.IndexerConnectionId is { } connectionId
                    ? !disabledConnectionIds.Contains(connectionId)
                    : !release.Sources.Any(other => other.IndexerConnectionId is not null &&
                        other.IndexerId == source.IndexerId &&
                        string.Equals(other.ExternalId, source.ExternalId, StringComparison.Ordinal)))
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
                    return (candidate, (ReleaseSource?)source);
                })
        ];
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

    private static DecisionResult? ResolveDecision(
        LogicalRelease group,
        IReadOnlyDictionary<Guid, Download> downloadsByReleaseId,
        bool isGrabCandidate,
        AcceptableReleaseSet? selection)
    {
        if (downloadsByReleaseId.TryGetValue(group.Release.Id, out var download))
        {
            return new DecisionResult(
                ReleaseHistoryDecisionKind.Grabbed,
                download.StatusReason ?? "Grabbed",
                download.CreatedAt);
        }

        if (!isGrabCandidate)
        {
            return null;
        }

        if (selection is null)
        {
            return new DecisionResult(ReleaseHistoryDecisionKind.Stalled, "No quality tier list configured", null);
        }

        if (selection.Releases.Any(accepted => group.ReleaseIds.Contains(accepted.Release.Id)))
        {
            return new DecisionResult(ReleaseHistoryDecisionKind.Stalled, "Eligible but not yet grabbed", null);
        }

        var skipReason = group.ReleaseIds
            .Where(selection.SkipReasons.ContainsKey)
            .Select(releaseId => selection.SkipReasons[releaseId])
            .FirstOrDefault();
        return skipReason is null
            ? null
            : new DecisionResult(ReleaseHistoryDecisionKind.Skipped, skipReason, null);
    }

    private static string? JoinReasons(IReadOnlyList<string>? reasons)
    {
        if (reasons is null || reasons.Count == 0)
        {
            return null;
        }

        return string.Join("; ", reasons);
    }

    private static Dictionary<Guid, string> IndexerNamesById(IEnumerable<Indexer> indexers)
    {
        return indexers.ToDictionary(i => i.Id, i => i.Name);
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

        var downloadsByReleaseId = await releaseBlockList.LoadDownloadsByReleaseIdAsync(
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

        var downloadsByIndexerExternalId = ReleaseBlockList.BuildDownloadsByIndexerExternalId(
            releases,
            downloadsByReleaseId);

        var evaluationContext = await releaseEvaluationService
            .CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);

        CandidateContentItem? grabCandidate = contentHierarchyItemIds.Count == 1 &&
            hierarchyItem.ContentItem is { } contentItem
            ? new(contentItem.Id, contentHierarchyItemId, contentItem.Fulfillment)
            : null;
        AcceptableReleaseSet? selection = null;
        if (grabCandidate is { } candidate)
        {
            var candidateReleases = releases.Where(release => matchedReleaseIds.Contains(release.Id)).ToList();
            var candidateStatuses = await releaseBlockList.ResolveStatusesAsync(
                candidateReleases,
                contentHierarchyItemId,
                cancellationToken)
                .ConfigureAwait(false);
            selection = await SelectAcceptableReleasesAsync(
                candidate,
                candidateReleases,
                candidateStatuses,
                evaluationContext,
                cancellationToken)
                .ConfigureAwait(false);
        }

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

            var (acquisitionStatus, failureReason) = ReleaseBlockList.ResolveAcquisition(
                logicalDownloadsByReleaseId,
                downloadsByIndexerExternalId,
                release);
            var decision = ResolveDecision(group, logicalDownloadsByReleaseId, grabCandidate is not null, selection);

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
                AcquisitionStatus = acquisitionStatus,
                FailureReason = failureReason,
                Decision = decision?.Kind,
                DecisionReason = decision?.Reason,
                GrabbedAt = decision?.GrabbedAt,
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
            release,
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

        var disabledConnectionIds = await ListDisabledConnectionIdsAsync([release], cancellationToken)
            .ConfigureAwait(false);
        var sourceCandidates = ReleaseSourceCandidates(release, disabledConnectionIds);
        if (sourceCandidates.Count == 0)
        {
            logger.LogDebug(
                "The release service did not grab release {ReleaseId} because only disabled indexer connections " +
                "found it",
                release.Id);
            progress?.Report(1, "The acquisition stopped because only disabled indexer connections found the release.");
            return null;
        }

        var eligible = sourceCandidates
            .SelectMany(source => enabledClients.Select(client =>
                downloadClientIntegrations.LastOrDefault(
                    candidate => string.Equals(candidate.ProviderType, client.ProviderType, StringComparison.Ordinal))
                    is { } integration &&
                    integration.SupportedProtocols.Contains(source.Release.Protocol, StringComparer.OrdinalIgnoreCase)
                    ? (source.Release, source.Source, Client: client)
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
            var (sourceRelease, source, downloadClient) = eligible[i];
            if (taintedClientIds.Contains(downloadClient.Id))
            {
                continue;
            }

            lastClientId = downloadClient.Id;
            lastSourceId = source?.Id;
            lastAttemptRelease = sourceRelease;
            cancellationToken.ThrowIfCancellationRequested();

            await CheckDestinationCapacityAsync(
                sourceRelease,
                source?.Id,
                downloadClient.Id,
                downloadScopeNodeIds,
                request,
                progress,
                cancellationToken).ConfigureAwait(false);

            Download? acquiredDownload = null;
            try
            {
                var grabResult = await downloadService
                    .GrabAsync(
                        sourceRelease,
                        downloadClient,
                        downloadClient.Settings,
                        new ReleaseGrabContext
                        {
                            ContentType = contentType,
                            IndexerConnectionId = source?.IndexerConnectionId,
                        },
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
                    transactionCancellationToken => UpsertDownloadAsync(
                        sourceRelease,
                        source?.Id,
                        downloadClient,
                        grabResult.DownloadInfo,
                        downloadScopeNodeIds,
                        request.UserId,
                        transactionCancellationToken),
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

    private async Task CheckDestinationCapacityAsync(
        Release release,
        Guid? releaseSourceId,
        Guid downloadClientId,
        IReadOnlyList<Guid> downloadScopeNodeIds,
        ReleaseAcquisitionContext request,
        IProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        if (release.Size is null or < 0)
        {
            return;
        }

        foreach (var contentHierarchyItemId in downloadScopeNodeIds)
        {
            var item = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
                .ConfigureAwait(false);
            var root = item is null ? null : await rootFolderService.GetAsync(
                item.EffectiveRootFolderId,
                cancellationToken).ConfigureAwait(false);
            var available = root is null ? null : fileSystem.GetStorageCapacity(root.Path)?.AvailableBytes;
            if (available is null or < 0 || available >= release.Size)
            {
                continue;
            }

            var reason = "Insufficient destination space. " + FormattableString.Invariant(
                $"Required: {release.Size} bytes. Available: {available} bytes.");
            await RecordFailedDownloadAsync(
                release,
                releaseSourceId,
                downloadClientId,
                DownloadFailureReason.InsufficientDestinationSpace,
                DownloadStatus.Faulted,
                reason,
                downloadScopeNodeIds,
                request.UserId,
                cancellationToken).ConfigureAwait(false);
            progress?.Report(1, reason);
            await eventBus.PublishAsync(
                ReleaseEventBuilder.BuildReleaseGrabRejectedEvent(
                    release.Id,
                    release.IndexerId ?? Guid.Empty,
                    reason,
                    GrabScopeImpact(downloadScopeNodeIds)),
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(reason);
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveDownloadScopeNodeIdsAsync(
        Release release,
        Guid? grabTargetContentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var releaseScopes = await releaseScopeRepository.QueryAllAsync(
            new ReleaseScopeQuery { ReleaseId = release.Id },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        var releaseScopeNodeIds = releaseScopes
            .Where(scope => scope.Status == ReleaseScopeStatus.Matched)
            .Select(scope => scope.ContentHierarchyItemId)
            .Distinct()
            .ToList();

        var needyNodeIds = await ListNodeIdsNeedingAcquisitionAsync(releaseScopeNodeIds, cancellationToken)
            .ConfigureAwait(false);
        var downloads = await downloadRepository.ListByReleaseAsync([release], cancellationToken).ConfigureAwait(false);
        var importedDownloads = downloads.Where(download => download.Status == DownloadStatus.Imported).ToList();
        var scopeNodeIds = releaseScopeNodeIds
            .Where(nodeId => nodeId == grabTargetContentHierarchyItemId ||
                needyNodeIds.Contains(nodeId) && !importedDownloads.Any(download =>
                    download.Scope.Count == 0 || download.Scope.Any(scope => scope.ContentHierarchyItemId == nodeId)))
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
            $"Release acquisition requires a content hierarchy target for release {release.Id}.");
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
        IReadOnlyList<Guid> downloadScopeNodeIds,
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
            AddDownloadScope(created, downloadScopeNodeIds);
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
        await EnsureDownloadScopeAsync(updated.Id, downloadScopeNodeIds, cancellationToken).ConfigureAwait(false);
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
            AddDownloadScope(row, downloadScopeNodeIds);

            await downloadRepository.CreateAsync(row, userId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The release service failed to write a failed download attempt for release {ReleaseId}",
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

        var downloads = await downloadRepository.QueryAllAsync(
            new DownloadQuery { ContentItemId = candidate.ContentHierarchyItemId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var acquisitionStatuses = await releaseBlockList.ResolveStatusesAsync(
            releases,
            candidate.ContentHierarchyItemId,
            cancellationToken)
            .ConfigureAwait(false);
        foreach (var blockedReleaseId in ReleaseBlockList.Blocked(acquisitionStatuses))
        {
            logger.LogDebug(
                "The auto-grab skipped release {ReleaseId} for content {ContentItemId} because a download " +
                "of it is {DownloadStatus}",
                blockedReleaseId,
                candidate.ContentItemId,
                acquisitionStatuses[blockedReleaseId].Status);
        }

        var qualityTierListId = evaluationContext.QualityTierListId;

        var scored = await SelectAcceptableReleasesAsync(
            candidate,
            releases,
            acquisitionStatuses,
            evaluationContext,
            cancellationToken)
            .ConfigureAwait(false);
        if (scored is null)
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

            return [];
        }

        if (scored.Releases.Count == 0)
        {
            logger.LogInformation(
                "Content {ContentItemId} has no acceptable release among {ReleaseCount} evaluated with " +
                "tier list {QualityTierListId}: {NotAcceptable} tier mismatches, {NotAnUpgrade} non-upgrades, " +
                "{Failed} failed",
                candidate.ContentItemId,
                releases.Count,
                qualityTierListId,
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
                        $"(not-acceptable: {scored.NotAcceptable}, " +
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
                EffectiveQualityRank: releaseEvaluationService.EffectiveQualityRank(release.Result, evaluationContext)))
            .OrderBy(group => group.Key.Faulted)
            .ThenBy(group => group.Key.EffectiveQualityRank);

        foreach (var primaryGroup in primaryGroups)
        {
            if (comparisonTrace is not null)
            {
                comparisonTrace.Add(new
                {
                    Criterion = "FaultedAndEffectiveQualityRank",
                    primaryGroup.Key.Faulted,
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
                        ActualQualityRank = release.Result.MatchedRankOrdinal,
                        EffectiveQualityRank = releaseEvaluationService.EffectiveQualityRank(
                            release.Result,
                            evaluationContext),
                    }),
                }));
        }

        if (allAcceptable)
        {
            var activeDownloads = downloads.Where(download => IsActiveDownloadStatus(download.Status)).ToList();
            var activeReleaseIds = activeDownloads
                .Where(download => download.ReleaseId is not null)
                .Select(download => download.ReleaseId!.Value)
                .ToHashSet();
            var eligible = ranked
                .Select(release => release.Release)
                .Where(release => !activeReleaseIds.Contains(release.Id));
            var maxSpread = await ReadMaxSpreadAsync(cancellationToken).ConfigureAwait(false);
            return maxSpread == 0
                ? [.. eligible]
                : [.. eligible.Take(Math.Max(0, maxSpread - activeDownloads.Count))];
        }

        if (!await CanStartNewDownloadAsync(
            candidate,
            downloads,
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

    private async Task<HashSet<Guid>> ListDisabledConnectionIdsAsync(
        IEnumerable<Release> releases,
        CancellationToken cancellationToken)
    {
        var disabledConnectionIds = new HashSet<Guid>();
        foreach (var indexerId in releases
            .SelectMany(release => release.Sources)
            .Where(source => source.IndexerConnectionId is not null)
            .Select(source => source.IndexerId)
            .OfType<Guid>()
            .Distinct())
        {
            var connections = await indexerService.ListConnectionsAsync(indexerId, cancellationToken)
                .ConfigureAwait(false);
            disabledConnectionIds.UnionWith(
                connections.Where(connection => !connection.Enabled).Select(connection => connection.Id));
        }

        return disabledConnectionIds;
    }

    private async Task<bool> CanStartNewDownloadAsync(
        CandidateContentItem candidate,
        IReadOnlyList<Download> contentDownloads,
        Guid? userId,
        Guid? correlationId,
        bool writeAudit,
        CancellationToken cancellationToken)
    {
        var inFlight = contentDownloads.FirstOrDefault(download => IsActiveDownloadStatus(download.Status));
        if (inFlight is null)
        {
            return true;
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

    private async Task<int> ReadMaxSpreadAsync(CancellationToken cancellationToken)
    {
        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(null, MonitoringSettingsGroup), cancellationToken)
            .ConfigureAwait(false);
        var raw = group?.Settings.FirstOrDefault(setting => setting.Key == MaxSpreadKey)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSpread) &&
            maxSpread >= 0)
        {
            return maxSpread;
        }

        logger.LogWarning(
            "The release service cannot read the max spread {MaxSpread} and queues every acceptable release",
            raw);
        return 0;
    }

    private async Task<AcceptableReleaseSet?> SelectAcceptableReleasesAsync(
        CandidateContentItem candidate,
        IReadOnlyList<Release> releases,
        IReadOnlyDictionary<Guid, (DownloadStatus Status, DownloadFailureReason? FailureReason)> acquisitionStatuses,
        ReleaseEvaluationContext evaluationContext,
        CancellationToken cancellationToken)
    {
        var blockedReleaseIds = ReleaseBlockList.Blocked(acquisitionStatuses);
        var disabledConnectionIds = await ListDisabledConnectionIdsAsync(releases, cancellationToken)
            .ConfigureAwait(false);
        var skippedFailed = 0;
        var skippedNotAcceptable = 0;
        var skippedNotAnUpgrade = 0;

        var scored = new List<(Release Release, ReleaseAssessment Result)>();
        var skipReasons = new Dictionary<Guid, string>();
        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (blockedReleaseIds.Contains(release.Id))
            {
                skippedFailed++;
                continue;
            }

            var assessment = await releaseEvaluationService
                .ScoreForContentAsync(
                    release,
                    candidate.ContentHierarchyItemId,
                    evaluationContext.CurrentFileQualityRank,
                    cancellationToken)
                .ConfigureAwait(false);
            if (assessment is null)
            {
                return null;
            }

            if (assessment.IsAcceptable != true)
            {
                skippedNotAcceptable++;
                skipReasons[release.Id] = JoinReasons(assessment.QualityReasons) ?? "No matching quality tier";
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
                skipReasons[release.Id] = "not an upgrade over the current file";
                continue;
            }

            if (ReleaseSourceCandidates(release, disabledConnectionIds).Count == 0)
            {
                skipReasons[release.Id] = "found only by disabled indexer connections";
                continue;
            }

            scored.Add((release, assessment));
        }

        return new AcceptableReleaseSet(scored, skipReasons, skippedFailed, skippedNotAcceptable, skippedNotAnUpgrade);
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
        IReadOnlyDictionary<Guid, string> SkipReasons,
        int Failed,
        int NotAcceptable,
        int NotAnUpgrade);
}
