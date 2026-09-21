using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Events;
using Magpie.Core.Jobs;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Releases;
using Magpie.Sdk.Events;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Releases;

internal sealed class ReleaseSearchService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IContentRepository contentRepository,
    IContentService contentService,
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IEnumerable<IRuntimeIndexerIntegrationFactory> runtimeIndexerIntegrationFactories,
    IReleaseRepository releaseRepository,
    IReleaseScopeRepository releaseScopeRepository,
    ReleaseBlockList releaseBlockList,
    IDownloadClientRepository downloadClientRepository,
    IContentTypeRegistry contentTypeRegistry,
    IRootFolderService rootFolderService,
    IAuditRecordRepository auditRecordRepository,
    IJobExecutionRepository jobExecutionRepository,
    IJobManager jobManager,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    IReleaseSearchQueryExecutor queryExecutor,
    IReleaseEvaluationService releaseEvaluationService,
    IEnumerable<IReleaseComparer> releaseComparers,
    JobRuntimeOptions jobRuntimeOptions,
    SearchOptions searchOptions,
    TimeProvider timeProvider,
    ILogger<ReleaseSearchService> logger) : IReleaseSearchService
{
    private const int PageSize = 200;

    private const int InteractiveIndexerResultBudget = 200;

    private const int AutomaticIndexerResultBudget = 20;

    private enum SearchSkipReason
    {
        Active,
        RecentlyCompleted
    }

    private static string StableKey(Release release)
    {
        var identity = release.IdentityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .OrderBy(key => key.Authority, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key.Value, StringComparer.OrdinalIgnoreCase)
            .Select(key => $"{key.Authority!.Trim().ToUpperInvariant()}:{key.Value!.Trim().ToUpperInvariant()}")
            .FirstOrDefault();
        return identity ?? $"{release.IndexerId:D}:{release.ExternalId}:{release.Title}";
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

    private static Dictionary<Guid, string> IndexerNamesById(IEnumerable<Indexer> indexers) =>
        indexers.ToDictionary(i => i.Id, i => i.Name);

    private static bool IsStrictlyOlderThanSelf(JobExecution other, JobExecution? self)
    {
        if (self is null)
        {
            return true;
        }

        if (other.CreatedAt != self.CreatedAt)
        {
            return other.CreatedAt < self.CreatedAt;
        }

        return other.Id.CompareTo(self.Id) < 0;
    }

    private static bool IndexerSupportsContentType(Indexer indexer, string contentType)
    {
        if (indexer.SupportedContentTypes is null || indexer.SupportedContentTypes.Count == 0)
        {
            return true;
        }

        foreach (var supported in indexer.SupportedContentTypes)
        {
            if (supported == contentType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnfulfilledLeaf(ContentHierarchyItem item) =>
        item.ContentItem is null ||
        item.ContentItem.Fulfillment is FulfillmentStatus.Missing or FulfillmentStatus.Upgradable;

    private static Guid? ResolveDirectChild(Guid nodeId, Guid leafId, IReadOnlyDictionary<Guid, Guid?> parentById)
    {
        var visited = new HashSet<Guid>();
        var current = leafId;
        while (visited.Add(current) && parentById.TryGetValue(current, out var parent) && parent is { } parentId)
        {
            if (parentId == nodeId)
            {
                return current;
            }

            current = parentId;
        }

        return null;
    }

    private static InteractiveSearchCandidateScore? ToScore(
        ReleaseAssessment result,
        Release release,
        Guid? qualityTierListId)
    {
        if (result.IsAcceptable is null || qualityTierListId is not { } tierListId)
        {
            return null;
        }

        return new()
        {
            ReleaseId = release.Id,
            QualityTierListId = tierListId,
            MatchedQualityTierId = result.MatchedQualityTierId,
            MatchedRankOrdinal = result.MatchedRankOrdinal,
            IsAcceptable = result.IsAcceptable ?? false,
            IsExcluded = result.IsExcluded ?? false,
            IsCutoffMet = result.IsCutoffMet ?? false,
            Reasons = result.QualityReasons,
        };
    }

    private static IReadOnlyList<ReleaseSearchPhase> BuildPlan(
        IReadOnlyList<IReadOnlyList<ContentFact>> factSets)
    {
        var phases = new List<ReleaseSearchPhase>();
        foreach (var factSet in factSets)
        {
            if (TryBuildPhase(factSet) is not { } phase ||
                ContainsEquivalentPhase(phases, phase))
            {
                continue;
            }

            phases.Add(phase);
        }

        return [.. phases.OrderBy(phase => string.IsNullOrWhiteSpace(phase.Request.IdSearchParameter) ? 1 : 0)];
    }

    private static ReleaseSearchPhase? TryBuildPhase(IReadOnlyList<ContentFact> factSet)
    {
        var values = new List<string>();
        var hasAnyFacet = false;
        var idFactIndex = -1;
        string? idSearchParameter = null;
        string? idSearchValue = null;

        for (var i = 0; i < factSet.Count; i++)
        {
            var fact = factSet[i];
            if (!string.IsNullOrWhiteSpace(fact.Value))
            {
                values.Add(fact.Value);
            }

            if (fact.Facets.Count == 0)
            {
                continue;
            }

            hasAnyFacet = true;
            if (!string.IsNullOrWhiteSpace(fact.Value) || fact.Facets.Count != 1)
            {
                continue;
            }

            var pair = fact.Facets.Single();
            if (idFactIndex >= 0)
            {
                continue;
            }

            idFactIndex = i;
            idSearchParameter = pair.Key;
            idSearchValue = pair.Value;
        }

        if (values.Count == 0 && !hasAnyFacet)
        {
            return null;
        }

        var criteriaBuilder = new Dictionary<string, List<string>>();
        for (var i = 0; i < factSet.Count; i++)
        {
            if (i == idFactIndex)
            {
                continue;
            }

            foreach (var pair in factSet[i].Facets)
            {
                if (!criteriaBuilder.TryGetValue(pair.Key, out var list))
                {
                    list = [];
                    criteriaBuilder[pair.Key] = list;
                }

                list.Add(pair.Value);
            }
        }

        var criteria = criteriaBuilder.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
        var releaseFacts = factSet.Select(fact => new ContentFact
        {
            Value = fact.Value,
            Facets = fact.Facets,
        }).ToArray();

        return new ReleaseSearchPhase
        {
            Request = new ReleaseSearchRequest
            {
                Title = values.Count > 0 ? string.Join(" ", values) : null,
                IdSearchParameter = idSearchParameter,
                IdSearchValue = idSearchValue,
                IdSearchSource = idSearchParameter,
                IdSearchIdentityValue = idSearchValue,
                Criteria = criteria,
            },
            ReleaseFacts = releaseFacts,
        };
    }

    private static bool ContainsEquivalentPhase(
        IReadOnlyList<ReleaseSearchPhase> phases,
        ReleaseSearchPhase candidate) => phases.Any(existing => PhasesEqual(existing, candidate));

    private static bool PhasesEqual(ReleaseSearchPhase left, ReleaseSearchPhase right)
    {
        var leftRequest = left.Request;
        var rightRequest = right.Request;
        return string.Equals(leftRequest.Title, rightRequest.Title, StringComparison.Ordinal) &&
            string.Equals(leftRequest.IdSearchParameter, rightRequest.IdSearchParameter, StringComparison.Ordinal) &&
            string.Equals(leftRequest.IdSearchValue, rightRequest.IdSearchValue, StringComparison.Ordinal) &&
            string.Equals(leftRequest.IdSearchSource, rightRequest.IdSearchSource, StringComparison.Ordinal) &&
            string.Equals(
                leftRequest.IdSearchIdentityValue,
                rightRequest.IdSearchIdentityValue,
                StringComparison.Ordinal) &&
            CriteriaEqual(leftRequest.Criteria, rightRequest.Criteria) &&
            left.ReleaseFacts.Count == right.ReleaseFacts.Count &&
            left.ReleaseFacts.Zip(right.ReleaseFacts).All(pair => pair.First.ContentEquals(pair.Second));
    }

    private static bool CriteriaEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>> left,
        IReadOnlyDictionary<string, IReadOnlyList<string>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        using var leftEnumerator = left.GetEnumerator();
        using var rightEnumerator = right.GetEnumerator();
        while (leftEnumerator.MoveNext())
        {
            if (!rightEnumerator.MoveNext())
            {
                return false;
            }

            var leftPair = leftEnumerator.Current;
            var rightPair = rightEnumerator.Current;
            if (!string.Equals(leftPair.Key, rightPair.Key, StringComparison.Ordinal) ||
                !leftPair.Value.SequenceEqual(rightPair.Value, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return !rightEnumerator.MoveNext();
    }

    public async Task<IReadOnlyList<InteractiveSearchCandidate>?> SearchInteractiveAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ReleaseTelemetry.Source.StartActivity("ReleaseSearch");
        activity?.SetTag("content_hierarchy_item_id", contentHierarchyItemId.ToString());
        activity?.SetTag("trigger", "interactive");
        activity?.SetTag("escalation_turn", false);

        static void RecordTurn(string outcome) =>
            ReleaseTelemetry.SearchTurns.Add(
                1,
                new KeyValuePair<string, object?>("trigger", "interactive"),
                new KeyValuePair<string, object?>("escalation", false),
                new KeyValuePair<string, object?>("outcome", outcome));

        var context = await releaseEvaluationService
            .CreateEvaluationContextAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (context.Target is not { } target)
        {
            RecordTurn("no_target");
            return null;
        }

        var factSets = context.ContentFacts;
        if (factSets.Count == 0)
        {
            RecordTurn("no_plan");
            logger.LogInformation(
                "The interactive search found no terms for content hierarchy item {ContentHierarchyItemId}",
                contentHierarchyItemId);
            return [];
        }

        var contentType = target.Item.ContentType;
        var (searchFacets, supportsKeywordSearch, connectionCount) = await AggregateSearchCapabilitiesAsync(
            cancellationToken)
            .ConfigureAwait(false);
        var plan = BuildPlan(factSets);

        activity?.SetTag("phase_count", plan.Count);
        activity?.SetTag("indexer_connection_count", connectionCount);
        if (plan.Count == 0)
        {
            RecordTurn("no_plan");
            logger.LogInformation(
                "The interactive search built no query plan for content hierarchy item " +
                "{ContentHierarchyItemId} because no indexer accepted its search terms",
                contentHierarchyItemId);
            return [];
        }

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace(
                "PLAN ReleaseSearch {PlanJson}",
                JsonSerializer.Serialize(new
                {
                    Mode = "Interactive",
                    ContentHierarchyItemId = contentHierarchyItemId,
                    ContentType = contentType,
                    FactSets = factSets,
                    SearchFacets = searchFacets,
                    SupportsKeywordSearch = supportsKeywordSearch,
                    Phases = plan.Select((phase, order) => new
                    {
                        Order = order,
                        phase.Request,
                        phase.ReleaseFacts,
                    }),
                }));
        }

        var categories = ResolveCategories(contentType);
        var applicableIndexerIds = await ResolveApplicableIndexerIdsAsync(contentType, cancellationToken)
            .ConfigureAwait(false);

        var coalesced =
            new List<(Release Release, IReadOnlyList<ContentFact> ReleaseFacts, ReleaseAssessment Assessment)>();
        var execution = await queryExecutor.ExecutePlanAsync(
            plan,
            categories,
            applicableIndexerIds,
            context,
            automatic: false,
            maximumResults: InteractiveIndexerResultBudget,
            cancellationToken: cancellationToken,
            useCache: false).ConfigureAwait(false);
        var executedPhases = execution.ExecutedRoutes;
        coalesced.AddRange(execution.Results.Select(result =>
            (result.Release, result.ReleaseFacts, result.Assessment)));

        var candidates = new List<(InteractiveSearchCandidate Candidate, int EffectiveQualityRank)>();
        foreach (var result in coalesced)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = result.Release;
            release.Id = Guid.NewGuid();
            var candidate = new InteractiveSearchCandidate(
                release,
                result.Assessment.MatchResult,
                ToScore(result.Assessment, release, context.QualityTierListId));
            candidates.Add((candidate, releaseEvaluationService.EffectiveQualityRank(result.Assessment, context)));
        }

        var orderedCandidates = Rank(candidates);
        activity?.SetTag("release_count", orderedCandidates.Count);
        var enriched = await EnrichInteractiveCandidatesAsync(orderedCandidates, cancellationToken)
            .ConfigureAwait(false);
        RecordTurn("completed");

        return enriched;
    }

    public async Task SearchAsync(
        Guid contentHierarchyItemId,
        Guid? userId,
        Guid? correlationId,
        Guid? currentExecutionId,
        IProgressReporter? progress,
        bool immediateOverride = false,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0, "The search will load the content item.");

        var skipReason = await ResolveRecentSearchSkipReasonAsync(
            contentHierarchyItemId,
            currentExecutionId,
            cancellationToken).ConfigureAwait(false);
        if (skipReason is not null)
        {
            ReleaseTelemetry.SearchSkipped.Add(
                1,
                new KeyValuePair<string, object?>(
                    "reason",
                    skipReason is SearchSkipReason.Active ? "active" : "recently_completed"));
            ReleaseTelemetry.SearchTurns.Add(
                1,
                new KeyValuePair<string, object?>("trigger", "automatic"),
                new KeyValuePair<string, object?>("escalation", false),
                new KeyValuePair<string, object?>("outcome", "skipped"));
            progress?.Report(
                1,
                skipReason is SearchSkipReason.Active
                    ? "Skipped: a recent search for this item is queued or active."
                    : "Skipped: a recent search for this item completed inside the coalescing window.");
            return;
        }

        var item = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            logger.LogInformation(
                "The release search skipped content hierarchy item {ContentHierarchyItemId} because it " +
                "no longer exists",
                contentHierarchyItemId);
            progress?.Report(1, "The search stopped because the content item does not exist.");
            return;
        }

        var childCounts = await contentHierarchyItemRepository
            .GetChildCountsByParentAsync([item.Id], cancellationToken)
            .ConfigureAwait(false);
        if (childCounts.TryGetValue(item.Id, out var childCount) && childCount > 0)
        {
            var subtree = await LoadSubtreeAsync(item.Id, cancellationToken).ConfigureAwait(false);
            var fanOut = await FanOutNodeAsync(item, subtree, userId, correlationId, cancellationToken)
                .ConfigureAwait(false);
            if (await CanEscalateAsync(item, subtree, fanOut.ChildNodeIds, cancellationToken).ConfigureAwait(false))
            {
                await RunSearchTurnAsync(
                    item,
                    userId,
                    correlationId,
                    progress,
                    escalationTurn: true,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                progress?.Report(
                    1,
                    fanOut.QueuedCount switch
                    {
                        0 => "The search did not queue any child searches.",
                        1 => "The search queued 1 child search.",
                        _ => $"The search queued {fanOut.QueuedCount} child searches."
                    });
            }

            return;
        }

        if (!immediateOverride && !NeedsAcquisition(item))
        {
            logger.LogInformation(
                "The release search skipped content hierarchy item {ContentHierarchyItemId} because it " +
                "does not need acquisition",
                item.Id);
            progress?.Report(1, "The search stopped because the content item does not need an acquisition.");
            return;
        }

        await RunSearchTurnAsync(
            item,
            userId,
            correlationId,
            progress,
            escalationTurn: false,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool NeedsAcquisition(ContentHierarchyItem item) =>
        item.EffectiveMonitoring && IsUnfulfilledLeaf(item);

    private IIndexerIntegration? ResolveIntegration(Indexer? indexer)
    {
        if (indexer is null)
        {
            return null;
        }

        var native = indexerIntegrations.LastOrDefault(integration =>
            string.Equals(integration.ProviderType, indexer.ProviderType, StringComparison.Ordinal));
        if (native is not null)
        {
            return native;
        }

        if (string.IsNullOrWhiteSpace(indexer.DefinitionType) || string.IsNullOrWhiteSpace(indexer.SchemaData))
        {
            return null;
        }

        return runtimeIndexerIntegrationFactories.LastOrDefault(factory =>
            string.Equals(factory.DefinitionType, indexer.DefinitionType, StringComparison.Ordinal))?.Create(indexer);
    }

    private IReadOnlyList<InteractiveSearchCandidate> Rank(
        IReadOnlyList<(InteractiveSearchCandidate Candidate, int EffectiveQualityRank)> candidates)
    {
        var comparers = releaseComparers.ToList();
        var ranked = new List<(InteractiveSearchCandidate Candidate, int EffectiveQualityRank)>(candidates.Count);
        List<object>? comparisonTrace = logger.IsEnabled(LogLevel.Trace) ? [] : null;
        var primaryGroups = candidates
            .GroupBy(candidate => (candidate.Candidate.MatchResult, candidate.EffectiveQualityRank))
            .OrderByDescending(group => group.Key.MatchResult)
            .ThenBy(group => group.Key.EffectiveQualityRank);

        foreach (var primaryGroup in primaryGroups)
        {
            if (comparisonTrace is not null)
            {
                comparisonTrace.Add(new
                {
                    Criterion = "MatchResultAndEffectiveQualityRank",
                    primaryGroup.Key.MatchResult,
                    primaryGroup.Key.EffectiveQualityRank,
                    ReleaseIds = primaryGroup.Select(candidate => candidate.Candidate.Release.Id),
                });
            }

            var tieGroups =
                new List<List<(InteractiveSearchCandidate Candidate, int EffectiveQualityRank)>>
                {
                    primaryGroup.ToList(),
                };
            foreach (var comparer in comparers)
            {
                var refinedGroups = new List<List<(InteractiveSearchCandidate Candidate, int EffectiveQualityRank)>>();
                foreach (var tieGroup in tieGroups)
                {
                    if (tieGroup.Count < 2)
                    {
                        refinedGroups.Add(tieGroup);
                        continue;
                    }

                    var applicable = tieGroup.All(candidate => comparer.CanCompare(candidate.Candidate.Release));
                    if (!applicable)
                    {
                        if (comparisonTrace is not null)
                        {
                            comparisonTrace.Add(new
                            {
                                Criterion = comparer.GetType().FullName,
                                Applied = false,
                                ReleaseIds = tieGroup.Select(candidate => candidate.Candidate.Release.Id),
                            });
                        }

                        refinedGroups.Add(tieGroup);
                        continue;
                    }

                    tieGroup.Sort((left, right) =>
                    {
                        var comparison = comparer.Compare(left.Candidate.Release, right.Candidate.Release);
                        if (comparisonTrace is not null)
                        {
                            comparisonTrace.Add(new
                            {
                                LeftReleaseId = left.Candidate.Release.Id,
                                RightReleaseId = right.Candidate.Release.Id,
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
                            comparer.Compare(
                                tieGroup[start].Candidate.Release,
                                tieGroup[end].Candidate.Release) == 0)
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
                    var comparison = Nullable.Compare<DateTime>(
                        right.Candidate.Release.PublishDate,
                        left.Candidate.Release.PublishDate);
                    return comparison != 0
                        ? comparison
                        : string.CompareOrdinal(
                            StableKey(left.Candidate.Release),
                            StableKey(right.Candidate.Release));
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
                    Mode = "Interactive",
                    ComparerOrder = comparers.Select(comparer => comparer.GetType().FullName),
                    Comparisons = comparisonTrace,
                    FinalOrder = ranked.Select((candidate, order) => new
                    {
                        Order = order,
                        candidate.Candidate.Release.Id,
                        candidate.Candidate.Release.Title,
                        MatchResult = candidate.Candidate.MatchResult.ToString(),
                        ActualQualityRank = candidate.Candidate.Score?.MatchedRankOrdinal,
                        candidate.EffectiveQualityRank,
                        StableIdentity = candidate.Candidate.Release.IdentityKeys
                            .Where(key =>
                                !string.IsNullOrWhiteSpace(key.Authority) &&
                                !string.IsNullOrWhiteSpace(key.Value))
                            .Select(key => new
                            {
                                key.Authority,
                                key.Value,
                            }),
                        ExternalFallback = new
                        {
                            candidate.Candidate.Release.IndexerId,
                            candidate.Candidate.Release.ExternalId,
                        },
                    }),
                }));
        }

        return [.. ranked.Select(candidate => candidate.Candidate)];
    }

    private async Task RunSearchTurnAsync(
        ContentHierarchyItem item,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter? progress,
        bool escalationTurn,
        CancellationToken cancellationToken)
    {
        using var activity = ReleaseTelemetry.Source.StartActivity("ReleaseSearch");
        activity?.SetTag("content_hierarchy_item_id", item.Id.ToString());
        activity?.SetTag("trigger", "automatic");
        activity?.SetTag("escalation_turn", escalationTurn);

        void RecordTurn(string outcome) =>
            ReleaseTelemetry.SearchTurns.Add(
                1,
                new KeyValuePair<string, object?>("trigger", "automatic"),
                new KeyValuePair<string, object?>("escalation", escalationTurn),
                new KeyValuePair<string, object?>("outcome", outcome));

        ReleaseEvaluationContext? evaluationContext = null;
        try
        {
            evaluationContext = await releaseEvaluationService
                .CreateEvaluationContextAsync(item.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release search failed to resolve the evaluation context for content hierarchy " +
                "item {ContentHierarchyItemId}",
                item.Id);
        }

        if (evaluationContext is not { Target: { } target } context)
        {
            RecordTurn("no_target");
            logger.LogInformation(
                "The release search skipped content hierarchy item {ContentHierarchyItemId} because it " +
                "resolved no target",
                item.Id);
            progress?.Report(1, "The search stopped because the content item has no search target.");
            return;
        }

        var factSets = context.ContentFacts;
        if (factSets.Count == 0)
        {
            RecordTurn("no_plan");
            logger.LogInformation(
                "The release search skipped content hierarchy item {ContentHierarchyItemId} because it " +
                "resolved no search terms",
                item.Id);
            progress?.Report(1, "The search stopped because the content item has no search terms.");
            return;
        }

        var contentType = target.Item.ContentType;
        var (searchFacets, supportsKeywordSearch, connectionCount) = await AggregateSearchCapabilitiesAsync(
            cancellationToken)
            .ConfigureAwait(false);
        var plan = BuildPlan(factSets);

        activity?.SetTag("phase_count", plan.Count);
        activity?.SetTag("indexer_connection_count", connectionCount);
        if (plan.Count == 0)
        {
            RecordTurn("no_plan");
            logger.LogInformation(
                "The release search skipped content hierarchy item {ContentHierarchyItemId} because no " +
                "indexer accepted its search terms",
                item.Id);
            progress?.Report(1, "The search stopped because the content item has no search terms.");
            return;
        }

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace(
                "PLAN ReleaseSearch {PlanJson}",
                JsonSerializer.Serialize(new
                {
                    Mode = "Automatic",
                    ContentHierarchyItemId = item.Id,
                    ContentType = contentType,
                    FactSets = factSets,
                    SearchFacets = searchFacets,
                    SupportsKeywordSearch = supportsKeywordSearch,
                    Phases = plan.Select((phase, order) => new
                    {
                        Order = order,
                        phase.Request,
                        phase.ReleaseFacts,
                    }),
                }));
        }

        var categories = ResolveCategories(contentType);
        var applicableIndexerIds = await ResolveApplicableIndexerIdsAsync(contentType, cancellationToken)
            .ConfigureAwait(false);

        var contentItem = item.ContentItem ??
            await contentService
                .GetOrCreateContentItemAsync(
                    item.Id,
                    timeProvider.GetUtcNow().UtcDateTime,
                    userId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var contentItemId = contentItem.Id;

        var retainedReleaseIds = new HashSet<Guid>();
        var rejectedReleaseIds = new HashSet<Guid>();
        var actionableHierarchyItemIds = new HashSet<Guid>();
        var engineExecution = await queryExecutor.ExecutePlanAsync(
            plan,
            categories,
            applicableIndexerIds,
            context,
            automatic: true,
            maximumResults: AutomaticIndexerResultBudget,
            (phaseIndex, name) => progress?.ReportFraction(
                phaseIndex,
                Math.Max(plan.Count, 1),
                $"The search will query indexer {name}."),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = engineExecution.ExecutedRoutes;
        if (engineExecution.ConnectionsAttempted == 0)
        {
            activity?.SetTag("terminal_reason", engineExecution.Warning ?? "no-execution");
            RecordTurn("completed");
            logger.LogInformation(
                "The release search for {ContentHierarchyItemId} issued no queries ({Reason})",
                item.Id,
                engineExecution.Warning ?? "no-execution");
            progress?.Report(1, "The search completed without changing saved results because no query executed.");
            return;
        }

        var releaseCount = engineExecution.Results.Count;
        activity?.SetTag("release_count", releaseCount);

        using (var persistActivity = ReleaseTelemetry.Source.StartActivity("PersistResults"))
        {
            persistActivity?.SetTag("release_count", releaseCount);
            await PersistReleasesAsync(
                result,
                item,
                context,
                retainedReleaseIds,
                rejectedReleaseIds,
                actionableHierarchyItemIds,
                userId,
                engineExecution.Results,
                cancellationToken).ConfigureAwait(false);
            persistActivity?.SetTag("retained_count", retainedReleaseIds.Count);
            persistActivity?.SetTag("rejected_count", rejectedReleaseIds.Count);
        }

        var searchCompleted = engineExecution.ConnectionsSucceeded > 0;
        var searchPartial = engineExecution.Partial;
        if (!searchCompleted)
        {
            var connectionsAttempted = result.Sum(phase => phase.ConnectionsAttempted);
            if (connectionsAttempted > 0)
            {
                var connectionsSucceeded = result.Sum(phase => phase.ConnectionsSucceeded);
                throw new InvalidOperationException(
                    $"Search for {item.Id} incomplete; {connectionsSucceeded}/{connectionsAttempted} succeeded");
            }

            RecordTurn("completed");
            logger.LogInformation(
                "The release search for {ContentHierarchyItemId} completed with no queries issued",
                item.Id);
            progress?.Report(1, "The search completed without a query because no indexer was applicable.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(null, "The search will save the results.");

        var actionableTargets = escalationTurn ? new HashSet<Guid>() : actionableHierarchyItemIds;
        await transactionManager.ExecuteWithTransactionAsync(
            async _ =>
            {
                var scopedReleases = await releaseScopeRepository.QueryAllAsync(
                    new ReleaseScopeQuery { ContentHierarchyItemId = item.Id },
                    sort: null,
                    cancellationToken).ConfigureAwait(false);

                foreach (var scope in scopedReleases.Where(scope =>
                    rejectedReleaseIds.Contains(scope.ReleaseId) ||
                    (!searchPartial && !retainedReleaseIds.Contains(scope.ReleaseId))))
                {
                    await releaseScopeRepository.DeleteAsync(scope.Id, null, cancellationToken)
                        .ConfigureAwait(false);
                }

                await MarkContentItemSearchedAsync(item, contentItemId, userId, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                await transactionManager.OnCommittedAsync(
                    postCommitCancellationToken => FinalizeSearchAsync(
                        item,
                        contentItemId,
                        actionableTargets,
                        releaseCount,
                        correlationId,
                        postCommitCancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        RecordTurn("completed");
        progress?.Report(1, "The release search completed.");
    }

    private async Task<(HashSet<Guid> ChildNodeIds, int QueuedCount)> FanOutNodeAsync(
        ContentHierarchyItem node,
        IReadOnlyList<ContentHierarchyItem> subtree,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var leafSearchVolume = await CountActiveLeafSearchesAsync(cancellationToken).ConfigureAwait(false);
        var underLeafBudget = leafSearchVolume <= searchOptions.BackpressureThreshold;

        var parentById = subtree.ToDictionary(item => item.Id, item => item.ParentId);
        var parentIds = subtree
            .Where(item => item.ParentId is not null)
            .Select(item => item.ParentId!.Value)
            .ToHashSet();
        var monitoredChildIds = subtree
            .Where(item => item.ParentId == node.Id && item.EffectiveMonitoring)
            .Select(item => item.Id)
            .ToHashSet();
        var unfulfilledLeaves = subtree
            .Where(item => !parentIds.Contains(item.Id) && item.EffectiveMonitoring && IsUnfulfilledLeaf(item));
        var residualChildIds = new HashSet<Guid>();
        foreach (var leaf in unfulfilledLeaves)
        {
            if (ResolveDirectChild(node.Id, leaf.Id, parentById) is { } childId &&
                parentIds.Contains(childId) &&
                monitoredChildIds.Contains(childId))
            {
                residualChildIds.Add(childId);
            }
        }

        foreach (var childId in residualChildIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnqueueChildSearchAsync(
                childId,
                MagpieJobs.ParameterValues.NodeSearchKind,
                userId,
                correlationId,
                cancellationToken)
                .ConfigureAwait(false);
        }

        if (!underLeafBudget)
        {
            logger.LogWarning(
                "The fan-out for content hierarchy item {ContentHierarchyItemId} deferred leaf searches " +
                "because volume {LeafSearchVolume} exceeds threshold {Threshold}",
                node.Id,
                leafSearchVolume,
                searchOptions.BackpressureThreshold);
            return (residualChildIds, residualChildIds.Count);
        }

        var queuedCount = residualChildIds.Count;
        var searchedBefore = timeProvider.GetUtcNow().UtcDateTime - searchOptions.MinimumSearchAge;
        var leafChildren = await LoadUnfulfilledLeafChildrenAsync(node.Id, cancellationToken).ConfigureAwait(false);
        foreach (var leaf in leafChildren)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsLeafDueAsync(leaf, searchedBefore, cancellationToken).ConfigureAwait(false) &&
                !await HasCompletedSinceSettlementAsync(leaf, cancellationToken).ConfigureAwait(false))
            {
                await EnqueueChildSearchAsync(
                    leaf.Id,
                    MagpieJobs.ParameterValues.LeafSearchKind,
                    userId,
                    correlationId,
                    cancellationToken)
                    .ConfigureAwait(false);
                queuedCount++;
            }
        }

        return (residualChildIds, queuedCount);
    }

    private async Task<int> CountActiveLeafSearchesAsync(CancellationToken cancellationToken)
    {
        var liveSince = timeProvider.GetUtcNow().UtcDateTime - jobRuntimeOptions.RunningStaleCutoff;
        var executions = await jobExecutionRepository.QueryAllAsync(
            new JobExecutionQuery
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
            },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return executions.Count(execution =>
            !(execution.Parameters.TryGetValue(MagpieJobs.Parameters.SearchKind, out var kind) &&
                kind == MagpieJobs.ParameterValues.NodeSearchKind) &&
            (execution.Status != JobExecutionStatus.Running || execution.LastHeartbeat >= liveSince));
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> LoadUnfulfilledLeafChildrenAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var items = await contentHierarchyItemRepository.QueryAllAsync(
            new()
            {
                ParentId = new()
                {
                    And = [new()
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Value = nodeId
                    }],
                },
                LeafOnly = true,
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
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return items;
    }

    private Task<bool> HasCompletedSinceSettlementAsync(
        ContentHierarchyItem leaf,
        CancellationToken cancellationToken) =>
        HasCompletedExecutionSinceAsync(leaf.Id, leaf.ContentItem?.DateLastSearched, cancellationToken);

    private async Task<bool> HasCompletedExecutionSinceAsync(
        Guid nodeId,
        DateTime? since,
        CancellationToken cancellationToken)
    {
        var pageNumber = 1;
        while (true)
        {
            var page = await jobExecutionRepository.QueryAsync(
                new()
                {
                    JobType = MagpieJobs.Types.ReleaseSearchItem,
                    Status = JobExecutionStatus.Completed,
                    Timestamp = since is { } s ? new(s, null) : null,
                    ParameterValueReferences = [nodeId.ToString("D")],
                },
                new() { PageNumber = pageNumber, PageSize = PageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            if (pageItems.Any(execution =>
                execution.StatusMessage?.StartsWith("Skipped:", StringComparison.Ordinal) != true))
            {
                return true;
            }

            if (pageItems.Count < PageSize)
            {
                return false;
            }

            pageNumber++;
        }
    }

    private async Task<bool> CanEscalateAsync(
        ContentHierarchyItem node,
        IReadOnlyList<ContentHierarchyItem> subtree,
        IReadOnlySet<Guid> selfEnqueuedChildIds,
        CancellationToken cancellationToken)
    {
        var searchedBefore = timeProvider.GetUtcNow().UtcDateTime - searchOptions.MinimumSearchAge;
        if (node.ContentItem?.DateLastSearched is { } nodeLastSearched && nodeLastSearched >= searchedBefore)
        {
            return false;
        }

        var parentById = subtree.ToDictionary(item => item.Id, item => item.ParentId);
        var parentIds = subtree
            .Where(item => item.ParentId is not null)
            .Select(item => item.ParentId!.Value)
            .ToHashSet();
        var leaves = subtree
            .Where(item => !parentIds.Contains(item.Id) && item.EffectiveMonitoring && IsUnfulfilledLeaf(item))
            .ToList();
        if (leaves.Count == 0)
        {
            return false;
        }

        var pendingSearchTargets = await LoadJobTargetIdsAsync(
            MagpieJobs.Types.ReleaseSearchItem,
            MagpieJobs.Parameters.ContentHierarchyItemId,
            cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<ContentHierarchyItem>();
        var settledLeafIds = new HashSet<Guid>();
        foreach (var leaf in leaves)
        {
            if (pendingSearchTargets.Contains(leaf.Id))
            {
                return false;
            }

            if (leaf.ContentItem?.DateLastSearched is not null)
            {
                settledLeafIds.Add(leaf.Id);
                candidates.Add(leaf);
                continue;
            }

            if (await HasCompletedSinceSettlementAsync(leaf, cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(leaf);
                continue;
            }

            return false;
        }

        var autoGrabTargets = await LoadJobTargetIdsAsync(
            MagpieJobs.Types.ReleaseAutoGrabItem,
            MagpieJobs.Parameters.ContentItemId,
            cancellationToken)
            .ConfigureAwait(false);
        var downloadTargets = await LoadJobTargetIdsAsync(
            MagpieJobs.Types.ReleaseDownloadItem,
            MagpieJobs.Parameters.ContentHierarchyItemId,
            cancellationToken)
            .ConfigureAwait(false);

        var residual = new List<ContentHierarchyItem>();
        foreach (var leaf in candidates)
        {
            if (leaf.ContentItem?.ActiveAcquisitionStatus is not null)
            {
                continue;
            }

            if (leaf.ContentItem is { } contentItem && autoGrabTargets.Contains(contentItem.Id))
            {
                continue;
            }

            if (downloadTargets.Contains(leaf.Id))
            {
                continue;
            }

            residual.Add(leaf);
        }

        if (residual.Count == 0)
        {
            return false;
        }

        var leafBaseline = residual
            .Where(leaf => settledLeafIds.Contains(leaf.Id) && leaf.ContentItem?.DateLastSearched is not null)
            .Select(leaf => leaf.ContentItem!.DateLastSearched!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        var residualDirectChildIds = new HashSet<Guid>();
        foreach (var leaf in residual)
        {
            if (ResolveDirectChild(node.Id, leaf.Id, parentById) is { } directChildId)
            {
                residualDirectChildIds.Add(directChildId);
            }
        }

        foreach (var childId in residualDirectChildIds)
        {
            if (!parentIds.Contains(childId))
            {
                continue;
            }

            if (pendingSearchTargets.Contains(childId) && !selfEnqueuedChildIds.Contains(childId))
            {
                return false;
            }

            if (await HasCompletedExecutionSinceAsync(childId, leafBaseline, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var child = subtree.First(item => item.Id == childId);
            if (child.ContentItem?.DateLastSearched is not { } childSettledAt || childSettledAt < leafBaseline)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> LoadSubtreeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var items = await contentHierarchyItemRepository.QueryAllAsync(
            new() { AncestorId = nodeId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        return items;
    }

    private async Task<HashSet<Guid>> LoadJobTargetIdsAsync(
        string jobType,
        string parameterName,
        CancellationToken cancellationToken)
    {
        var executions = await jobExecutionRepository.QueryAllAsync(
            new JobExecutionQuery
            {
                JobType = jobType,
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
            },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var ids = new HashSet<Guid>();
        foreach (var execution in executions)
        {
            if (execution.Parameters.TryGetValue(parameterName, out var raw) && Guid.TryParse(raw, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private async Task<bool> IsLeafDueAsync(
        ContentHierarchyItem leaf,
        DateTime searchedBefore,
        CancellationToken cancellationToken)
    {
        var contentItem = leaf.ContentItem;
        var lastSearched = contentItem?.DateLastSearched;
        if (contentItem is null || lastSearched is null || lastSearched < searchedBefore)
        {
            return true;
        }

        try
        {
            return await HasExhaustedStoredReleaseCandidatesAsync(
                contentItem,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release search service failed to evaluate stored release exhaustion for " +
                "{ContentHierarchyItemId} and falls back to the recent-search age gate",
                leaf.Id);
            return false;
        }
    }

    public async Task<InteractiveReleaseGrabOutcome> GrabInteractiveAsync(
        Guid contentHierarchyItemId,
        Release release,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (release is null || string.IsNullOrWhiteSpace(release.Title))
        {
            ReleaseTelemetry.Grabs.Add(
                1,
                new KeyValuePair<string, object?>("mode", "interactive"),
                new KeyValuePair<string, object?>("outcome", "invalid_release"));
            return new InteractiveReleaseGrabOutcome(
                InteractiveReleaseGrabResult.InvalidRelease, null, "A release with a title is required.");
        }

        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchyItem is null)
        {
            ReleaseTelemetry.Grabs.Add(
                1,
                new KeyValuePair<string, object?>("mode", "interactive"),
                new KeyValuePair<string, object?>("outcome", "content_not_found"));
            return new InteractiveReleaseGrabOutcome(InteractiveReleaseGrabResult.ContentNotFound, null, null);
        }

        if (hierarchyItem.EffectiveQualityTierListId is null)
        {
            var rootFolder = await rootFolderService
                .GetAsync(hierarchyItem.EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false);
            var rootFolderName = rootFolder?.Name ?? hierarchyItem.EffectiveRootFolderId.ToString();
            ReleaseTelemetry.Grabs.Add(
                1,
                new KeyValuePair<string, object?>("mode", "interactive"),
                new KeyValuePair<string, object?>("outcome", "invalid_release"));
            return new InteractiveReleaseGrabOutcome(
                InteractiveReleaseGrabResult.InvalidRelease,
                null,
                $"Root folder '{rootFolderName}' has no quality tier list configured for level " +
                $"'{hierarchyItem.Level}'.");
        }

        var contentItem = hierarchyItem.ContentItem ??
            await contentService
                .GetOrCreateContentItemAsync(
                    contentHierarchyItemId,
                    timeProvider.GetUtcNow().UtcDateTime,
                    actingUserId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var clients = await downloadClientRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (!clients.Any(client => client.Enabled))
        {
            return new InteractiveReleaseGrabOutcome(
                InteractiveReleaseGrabResult.NoDownloadClient, null, "No enabled download client is configured.");
        }

        Release persisted = null!;
        await transactionManager.ExecuteWithTransactionAsync(
            async transactionCancellationToken =>
            {
                release.Id = Guid.NewGuid();
                release.CreatedAt = timeProvider.GetUtcNow().UtcDateTime;
                release.CreatedByUserId = actingUserId;
                release.ModifiedAt = null;
                release.ModifiedByUserId = null;
                await UpsertReleaseAsync(
                    release,
                    [],
                    null,
                    null,
                    contentHierarchyItemId,
                    actingUserId,
                    actingUserId,
                    transactionCancellationToken)
                    .ConfigureAwait(false);
                persisted = release;
            },
            cancellationToken).ConfigureAwait(false);

        var releaseIdParameter = persisted.Id.ToString();
        var activeAcquisitions = await jobExecutionRepository.QueryAllAsync(
            new()
            {
                JobType = MagpieJobs.Types.ReleaseDownloadItem,
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running],
            },
            sort: null,
            cancellationToken).ConfigureAwait(false);
        if (activeAcquisitions.Any(execution =>
            execution.Parameters.TryGetValue(MagpieJobs.Parameters.ReleaseId, out var activeReleaseId) &&
            string.Equals(activeReleaseId, releaseIdParameter, StringComparison.OrdinalIgnoreCase)))
        {
            ReleaseTelemetry.Grabs.Add(
                1,
                new KeyValuePair<string, object?>("mode", "interactive"),
                new KeyValuePair<string, object?>("outcome", "already_in_progress"));
            return new InteractiveReleaseGrabOutcome(
                InteractiveReleaseGrabResult.AlreadyInProgress,
                null,
                $"Release {persisted.Id} is already being acquired.");
        }

        var correlationId = Guid.NewGuid();
        var executionId = await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.ReleaseDownloadItem,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.ReleaseId] = releaseIdParameter,
                    [MagpieJobs.Parameters.ContentHierarchyItemId] = contentHierarchyItemId.ToString(),
                    [MagpieJobs.Parameters.IgnoreFailedTaint] = bool.TrueString,
                },
                Mode = JobExecutionMode.Queue,
                UserId = actingUserId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);
        var execution = await jobExecutionRepository.GetByIdAsync(executionId, cancellationToken).ConfigureAwait(false);
        if (execution is not null && execution.CorrelationId != correlationId)
        {
            ReleaseTelemetry.Grabs.Add(
                1,
                new KeyValuePair<string, object?>("mode", "interactive"),
                new KeyValuePair<string, object?>("outcome", "already_in_progress"));
            return new InteractiveReleaseGrabOutcome(
                InteractiveReleaseGrabResult.AlreadyInProgress,
                executionId,
                $"Release {persisted.Id} is already being acquired.");
        }

        return new InteractiveReleaseGrabOutcome(InteractiveReleaseGrabResult.Accepted, executionId, null);
    }

    private async Task<bool> HasExhaustedStoredReleaseCandidatesAsync(
        ContentItem candidate,
        CancellationToken cancellationToken = default)
    {
        var releases = await releaseRepository.QueryAllAsync(
            new ReleaseQuery { ContentHierarchyItemId = candidate.ContentHierarchyItemId },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        if (releases.Count == 0)
        {
            return false;
        }

        var blockedReleaseIds = await releaseBlockList.ResolveAsync(
            releases,
            candidate.ContentHierarchyItemId,
            cancellationToken).ConfigureAwait(false);
        if (blockedReleaseIds.Count == 0)
        {
            return false;
        }

        if (blockedReleaseIds.Count == releases.Count)
        {
            return true;
        }

        var evaluationContext = await releaseEvaluationService.CreateEvaluationContextAsync(
            candidate.ContentHierarchyItemId,
            cancellationToken).ConfigureAwait(false);
        foreach (var release in releases.Where(release => !blockedReleaseIds.Contains(release.Id)))
        {
            var assessment = releaseEvaluationService.AssessMatch(release, [], evaluationContext);
            if (assessment.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident &&
                assessment.IsAcceptable == true &&
                (candidate.Fulfillment != FulfillmentStatus.Upgradable ||
                    releaseEvaluationService.IsAcceptableUpgradeOverCurrentFile(
                        assessment,
                        release,
                        evaluationContext)))
            {
                return false;
            }
        }

        return true;
    }

    private async Task EnqueueChildSearchAsync(
        Guid contentHierarchyItemId,
        string searchKind,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.ContentHierarchyItemId] =
                        contentHierarchyItemId.ToString("D"),
                    [MagpieJobs.Parameters.SearchKind] = searchKind,
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The fan-out enqueued a release search for content hierarchy item {ContentHierarchyItemId}",
            contentHierarchyItemId);
    }

    private async Task<Release?> FindStoredReleaseAsync(Release release, CancellationToken cancellationToken)
    {
        if (release.IndexerId is not { } indexerId || string.IsNullOrWhiteSpace(release.ExternalId))
        {
            return null;
        }

        var releases = await releaseRepository.QueryAsync(
            new() { IndexerId = indexerId, ExternalId = release.ExternalId },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);

        return releases.Items.FirstOrDefault();
    }

    private async Task PersistReleasesAsync(
        IReadOnlyList<ReleaseSearchPhase> phases,
        ContentHierarchyItem item,
        ReleaseEvaluationContext evaluationContext,
        HashSet<Guid> retainedReleaseIds,
        HashSet<Guid> rejectedReleaseIds,
        HashSet<Guid> actionableHierarchyItemIds,
        Guid? userId,
        IReadOnlyList<EvaluatedRelease>? evaluatedResults,
        CancellationToken cancellationToken)
    {
        await transactionManager.ExecuteWithTransactionAsync(
            async _ =>
            {
                var observedReleaseIds = new HashSet<Guid>();
                var resolved = new List<(
                    Release Release,
                    IReadOnlyList<ContentFact> ReleaseFacts,
                    ReleaseAssessment Assessment)>();
                if (evaluatedResults is not null)
                {
                    resolved.AddRange(evaluatedResults.Select(result =>
                        (result.Release, result.ReleaseFacts, result.Assessment)));
                }
                else
                {
                    foreach (var phase in phases)
                    {
                        foreach (var release in phase.Releases)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            ReleaseAssessment assessment;
                            try
                            {
                                assessment = releaseEvaluationService.AssessMatch(
                                    release,
                                    phase.ReleaseFacts,
                                    evaluationContext);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(
                                    ex,
                                    "The evaluation of release {ReleaseTitle} failed, " +
                                    "so the search dropped this release",
                                    release.Title);
                                if (logger.IsEnabled(LogLevel.Trace))
                                {
                                    logger.LogTrace(
                                        "EVAL ReleaseSearch {EvaluationJson}",
                                        JsonSerializer.Serialize(new
                                        {
                                            Mode = "Automatic",
                                            ContentHierarchyItemId = item.Id,
                                            ReleaseFacts = phase.ReleaseFacts,
                                            MatchResult = (string?)null,
                                            Disposition = "dropped-evaluation-failed",
                                            Failure = ex.Message,
                                        }));
                                }

                                if (await FindStoredReleaseAsync(release, cancellationToken).ConfigureAwait(false) is
                                    { } failedRelease)
                                {
                                    rejectedReleaseIds.Add(failedRelease.Id);
                                }

                                continue;
                            }

                            var existingIndex = resolved.FindIndex(existing =>
                                RefersToSameRelease(release, existing.Release));
                            var disposition = "held";
                            if (existingIndex < 0)
                            {
                                resolved.Add((release, phase.ReleaseFacts, assessment));
                            }
                            else if (assessment.MatchResult > resolved[existingIndex].Assessment.MatchResult)
                            {
                                resolved[existingIndex] = (release, phase.ReleaseFacts, assessment);
                                disposition = "replaced";
                            }
                            else
                            {
                                disposition = "duplicate-weaker";
                            }

                            if (logger.IsEnabled(LogLevel.Trace))
                            {
                                logger.LogTrace(
                                    "EVAL ReleaseSearch {EvaluationJson}",
                                    JsonSerializer.Serialize(new
                                    {
                                        Mode = "Automatic",
                                        ContentHierarchyItemId = item.Id,
                                        StableIdentity = release.IdentityKeys
                                            .Where(key =>
                                                !string.IsNullOrWhiteSpace(key.Authority) &&
                                                !string.IsNullOrWhiteSpace(key.Value))
                                            .Select(key => new
                                            {
                                                key.Authority,
                                                key.Value,
                                            }),
                                        ExternalFallback = new
                                        {
                                            release.IndexerId,
                                            release.ExternalId,
                                        },
                                        ReleaseFacts = phase.ReleaseFacts,
                                        MatchResult = assessment.MatchResult.ToString(),
                                        Disposition = disposition,
                                    }));
                            }
                        }
                    }
                }

                foreach (var acceptedObservation in resolved)
                {
                    if (acceptedObservation.Assessment.MatchResult is not
                        (ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
                    {
                        if (await FindStoredReleaseAsync(acceptedObservation.Release, cancellationToken)
                            .ConfigureAwait(false) is
                            { } rejectedRelease)
                        {
                            rejectedReleaseIds.Add(rejectedRelease.Id);
                        }

                        if (logger.IsEnabled(LogLevel.Trace))
                        {
                            logger.LogTrace(
                                "EVAL ReleaseSearch {EvaluationJson}",
                                JsonSerializer.Serialize(new
                                {
                                    Mode = "Automatic",
                                    ContentHierarchyItemId = item.Id,
                                    StableIdentity = acceptedObservation.Release.IdentityKeys
                                        .Where(key =>
                                            !string.IsNullOrWhiteSpace(key.Authority) &&
                                            !string.IsNullOrWhiteSpace(key.Value))
                                        .Select(key => new
                                        {
                                            key.Authority,
                                            key.Value,
                                        }),
                                    ExternalFallback = new
                                    {
                                        acceptedObservation.Release.IndexerId,
                                        acceptedObservation.Release.ExternalId,
                                    },
                                    ReleaseFacts = acceptedObservation.ReleaseFacts,
                                    MatchResult = acceptedObservation.Assessment.MatchResult.ToString(),
                                    Disposition = "dropped",
                                }));
                        }

                        continue;
                    }

                    var upsert = await UpsertReleaseAsync(
                        acceptedObservation.Release,
                        acceptedObservation.ReleaseFacts,
                        evaluationContext,
                        acceptedObservation.Assessment,
                        item.Id,
                        userId,
                        null,
                        cancellationToken).ConfigureAwait(false);
                    if (upsert.ActionableNodeIds.Count > 0)
                    {
                        retainedReleaseIds.Add(acceptedObservation.Release.Id);
                    }

                    if (upsert.Rediscovered)
                    {
                        observedReleaseIds.Add(acceptedObservation.Release.Id);
                    }

                    foreach (var scopedHierarchyItemId in upsert.ActionableNodeIds)
                    {
                        actionableHierarchyItemIds.Add(scopedHierarchyItemId);
                    }

                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace(
                            "EVAL ReleaseSearch {EvaluationJson}",
                            JsonSerializer.Serialize(new
                            {
                                Mode = "Automatic",
                                ContentHierarchyItemId = item.Id,
                                acceptedObservation.Release.Id,
                                StableIdentity = acceptedObservation.Release.IdentityKeys
                                    .Where(key =>
                                        !string.IsNullOrWhiteSpace(key.Authority) &&
                                        !string.IsNullOrWhiteSpace(key.Value))
                                    .Select(key => new
                                    {
                                        key.Authority,
                                        key.Value,
                                    }),
                                ExternalFallback = new
                                {
                                    acceptedObservation.Release.IndexerId,
                                    acceptedObservation.Release.ExternalId,
                                },
                                ReleaseFacts = acceptedObservation.ReleaseFacts,
                                MatchResult = acceptedObservation.Assessment.MatchResult.ToString(),
                                Disposition = upsert.ActionableNodeIds.Count > 0
                                    ? "scoped"
                                    : "persisted-without-scope",
                                ScopeNodeIds = upsert.ActionableNodeIds,
                            }));
                    }
                }

                await releaseRepository.MarkObservedAsync(observedReleaseIds, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeSearchAsync(
        ContentHierarchyItem item,
        Guid contentItemId,
        HashSet<Guid> actionableHierarchyItemIds,
        int releaseCount,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var impact = new ContentEventImpact
        {
            HierarchyItemId = item.Id,
            ItemId = contentItemId,
            MetadataContainerId = item.MetadataContainerId,
            ParentId = item.ParentId,
            ContentType = item.ContentType,
            Level = item.Level,
            TopLevel = item.ParentId is null,
            RootFolderId = item.RootFolderId,
            QualityTierListId = item.QualityTierListId,
            Monitoring = item.EffectiveMonitoring
        };
        var scopeHierarchyItemIds = actionableHierarchyItemIds
            .Where(hierarchyItemId => hierarchyItemId != item.Id)
            .ToList();

        try
        {
            await eventBus
                .PublishAsync(
                    ReleaseEventBuilder.BuildReleaseSearchCompletedEvent(impact, releaseCount),
                    cancellationToken)
                .ConfigureAwait(false);

            if (actionableHierarchyItemIds.Contains(item.Id))
            {
                await eventBus
                    .PublishAsync(
                        ReleaseEventBuilder.BuildReleaseMatchedEvent(impact) with
                        {
                            CorrelationId = correlationId
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (scopeHierarchyItemIds.Count > 0)
            {
                await eventBus
                    .PublishAsync(
                        ReleaseEventBuilder.BuildReleaseScopeMatchedEvent(scopeHierarchyItemIds) with
                        {
                            CorrelationId = correlationId
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The release search failed to publish its completion events for content " +
                "hierarchy item {ContentHierarchyItemId}",
                item.Id);
        }
    }

    private IReadOnlyList<string> ResolveCategories(string contentType) =>
        contentTypeRegistry.GetDefinitionFor(contentType)?.Categories ?? [];

    private async Task<HashSet<Guid>> ResolveApplicableIndexerIdsAsync(
        string contentType,
        CancellationToken cancellationToken)
    {
        var indexers = await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. indexers
            .Where(indexer => IndexerSupportsContentType(indexer, contentType))
            .Select(indexer => indexer.Id)];
    }

    private async Task<(
        IReadOnlyList<IReadOnlyCollection<string>> SearchFacets,
        bool SupportsKeywordSearch,
        int ConnectionCount)>
        AggregateSearchCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var indexers = (await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(indexer => indexer.Id);
        var connections = await indexerConnectionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        var distinctFacetSets = new List<HashSet<string>>();
        var supportsKeywordSearch = false;
        var connectionCount = 0;

        foreach (var connection in connections)
        {
            connectionCount++;
            if (!indexers.TryGetValue(connection.IndexerId, out var indexer))
            {
                continue;
            }

            var integration = ResolveIntegration(indexer);
            if (integration is null)
            {
                continue;
            }

            supportsKeywordSearch |= integration.SupportsKeywordSearch;

            foreach (var facetSet in integration.SearchFacets)
            {
                var normalized = new HashSet<string>(facetSet, StringComparer.OrdinalIgnoreCase);
                if (distinctFacetSets.Any(existing => existing.SetEquals(normalized)))
                {
                    continue;
                }

                distinctFacetSets.Add(normalized);
            }
        }

        return (distinctFacetSets, supportsKeywordSearch, connectionCount);
    }

    private async Task<IReadOnlyList<InteractiveSearchCandidate>> EnrichInteractiveCandidatesAsync(
        IReadOnlyList<InteractiveSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var indexers = await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var indexerNamesById = IndexerNamesById(indexers);

        var enriched = new InteractiveSearchCandidate[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var release = candidate.Release;

            string? indexerName = null;
            if (release.IndexerId is { } indexerId)
            {
                indexerNamesById.TryGetValue(indexerId, out indexerName);
            }

            enriched[i] = candidate with
            {
                IndexerName = indexerName,
            };
        }

        return enriched;
    }

    private async Task<ReleaseUpsertResult> UpsertReleaseAsync(
        Release release,
        IReadOnlyList<ContentFact> releaseFacts,
        ReleaseEvaluationContext? searchedContext,
        ReleaseAssessment? searchedAssessment,
        Guid searchedHierarchyItemId,
        Guid? userId,
        Guid? scopeUserId,
        CancellationToken cancellationToken)
    {
        var created = false;
        if (!await releaseRepository.UpdateAsync(release, userId, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var persisted = await releaseRepository.CreateAsync(release, userId, cancellationToken)
                    .ConfigureAwait(false);
                release.Id = persisted.Id;
                created = true;
            }
            catch (DbUpdateException)
            {
                if (!await releaseRepository.UpdateAsync(release, userId, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }

        var releaseScope = releaseEvaluationService is ReleaseEvaluationService implementation &&
            searchedContext is not null && searchedAssessment is not null
                ? await implementation.ResolveScopeAsync(
                    release,
                    releaseFacts,
                    searchedContext,
                    searchedAssessment,
                    cancellationToken).ConfigureAwait(false)
                : await releaseEvaluationService
                    .ResolveScopeAsync(release, releaseFacts, searchedHierarchyItemId, cancellationToken)
                    .ConfigureAwait(false);
        var existingScopes = await releaseScopeRepository.QueryAllAsync(
            new ReleaseScopeQuery { ReleaseId = release.Id },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var existingScopeNodeIds = existingScopes.Select(scope => scope.ContentHierarchyItemId).ToList();
        if (!releaseScope.ScopeNodeIds.Contains(searchedHierarchyItemId))
        {
            foreach (var scope in existingScopes.Where(scope =>
                scope.ContentHierarchyItemId == searchedHierarchyItemId))
            {
                await releaseScopeRepository.DeleteAsync(scope.Id, scopeUserId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var scopeNodeId in releaseScope.ScopeNodeIds.Except(existingScopeNodeIds))
        {
            await releaseScopeRepository.CreateAsync(
                new ReleaseScope
                {
                    Id = Guid.NewGuid(),
                    ReleaseId = release.Id,
                    ContentHierarchyItemId = scopeNodeId
                },
                scopeUserId,
                cancellationToken).ConfigureAwait(false);
        }

        return new ReleaseUpsertResult(releaseScope.ActionableNodeIds, !created);
    }

    private async Task MarkContentItemSearchedAsync(
        ContentHierarchyItem item,
        Guid contentItemId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var contentItem = await contentRepository.GetByIdAsync(contentItemId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Content item {contentItemId} does not exist.");
        contentItem.DateLastSearched = now;
        contentItem.ModifiedAt = now;
        await contentRepository.UpdateAsync(contentItem, userId, cancellationToken).ConfigureAwait(false);

        await auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.Content,
                Operation = "ReleaseSearchItem",
                EntityId = item.Id.ToString(),
                EntityType = nameof(ContentHierarchyItem),
                Detail = $"Searched for releases matching content item {item.Name}.",
                Timestamp = now,
                UserId = userId,
                CorrelationId = correlationId
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SearchSkipReason?> ResolveRecentSearchSkipReasonAsync(
        Guid contentHierarchyItemId,
        Guid? currentExecutionId,
        CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime - searchOptions.CoalescingWindow;

        var recent = await jobExecutionRepository.QueryAllAsync(
            new()
            {
                JobType = MagpieJobs.Types.ReleaseSearchItem,
                Timestamp = new(since, null),
                Statuses = [JobExecutionStatus.Queued, JobExecutionStatus.Running, JobExecutionStatus.Completed],
            },
            sort: null,
            cancellationToken).ConfigureAwait(false);

        var self = currentExecutionId is { } selfId
            ? recent.FirstOrDefault(e => e.Id == selfId)
            : null;

        var expectedItemId = contentHierarchyItemId.ToString();
        foreach (var execution in recent)
        {
            if (currentExecutionId is { } excluded && execution.Id == excluded)
            {
                continue;
            }

            if (!execution.Parameters.TryGetValue(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                out var raw))
            {
                continue;
            }

            if (!string.Equals(raw, expectedItemId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (execution.Status == JobExecutionStatus.Completed &&
                execution.StatusMessage?.StartsWith("Skipped:", StringComparison.Ordinal) == true)
            {
                continue;
            }

            if (execution.Status != JobExecutionStatus.Completed && !IsStrictlyOlderThanSelf(execution, self))
            {
                continue;
            }

            logger.LogInformation(
                "The release search skipped ReleaseSearchItem for content hierarchy item " +
                "{ContentHierarchyItemId} because execution {ExecutionId} ({Status}, created {CreatedAt:o}) " +
                "is active or completed within {Window}",
                contentHierarchyItemId,
                execution.Id,
                execution.Status,
                execution.CreatedAt,
                searchOptions.CoalescingWindow);
            return execution.Status == JobExecutionStatus.Completed
                ? SearchSkipReason.RecentlyCompleted
                : SearchSkipReason.Active;
        }

        return null;
    }

    private sealed record ReleaseUpsertResult(IReadOnlyList<Guid> ActionableNodeIds, bool Rediscovered);
}
