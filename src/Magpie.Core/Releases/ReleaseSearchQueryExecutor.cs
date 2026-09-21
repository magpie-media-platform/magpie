using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Releases;

internal sealed class ReleaseSearchQueryExecutor(
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IEventBus eventBus,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IEnumerable<IRuntimeIndexerIntegrationFactory> runtimeIndexerIntegrationFactories,
    TimeProvider timeProvider,
    ILogger<ReleaseSearchQueryExecutor> logger,
    IReleaseEvaluationService releaseEvaluationService,
    ICacheFactory cacheFactory,
    IEnumerable<IReleaseComparer> releaseComparers,
    SearchOptions searchOptions) : IReleaseSearchQueryExecutor
{
    private const string RequestNotSupportedReason = "request-not-supported";

    private static readonly TimeSpan ConnectionPhaseDeadline = TimeSpan.FromSeconds(120);

    private enum ContributionKind
    {
        New,
        Enriched,
        Unchanged,
    }

    private static HashSet<(string Authority, string Value)> GetNormalizedIdentityKeys(Release release) =>
        release.IdentityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => (key.Authority.Trim().ToLowerInvariant(), key.Value.Trim().ToLowerInvariant()))
            .ToHashSet();

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
        var leftKeys = GetNormalizedIdentityKeys(left);
        var rightKeys = GetNormalizedIdentityKeys(right);
        var leftImmutableKeys = ImmutableIdentityKeys(leftKeys);
        var rightImmutableKeys = ImmutableIdentityKeys(rightKeys);
        if (leftImmutableKeys.Count > 0 && rightImmutableKeys.Count > 0)
        {
            return leftImmutableKeys.Overlaps(rightImmutableKeys);
        }

        return leftKeys.Overlaps(rightKeys) || SourceIdentities(left).Overlaps(SourceIdentities(right));
    }

    private static bool HasConflictingImmutableIdentity(Release left, Release right)
    {
        var leftKeys = ImmutableIdentityKeys(GetNormalizedIdentityKeys(left));
        var rightKeys = ImmutableIdentityKeys(GetNormalizedIdentityKeys(right));
        return leftKeys.Count > 0 && rightKeys.Count > 0 && !leftKeys.Overlaps(rightKeys);
    }

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

    private static string BuildCandidateKey(Release release)
    {
        var identity = GetNormalizedIdentityKeys(release)
            .OrderBy(key => key.Authority, StringComparer.Ordinal)
            .ThenBy(key => key.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        var parts = identity == default
            ? new[] { "source", release.IndexerId?.ToString("D"), release.ExternalId }
            : ["identity", identity.Authority, identity.Value];
        return JsonSerializer.Serialize(parts);
    }

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

    public async Task<IReadOnlyList<ReleaseSearchPhase>> ExecuteAsync(
        IReadOnlyList<ReleaseSearchPhase> phases,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        Action<int, string>? onConnectionStarted = null,
        CancellationToken cancellationToken = default,
        ISet<Guid>? selectedConnectionIds = null)
    {
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(applicableIndexerIds);

        var indexers = (await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(i => i.Id);
        var connections = (await indexerConnectionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.Enabled && (selectedConnectionIds is null || selectedConnectionIds.Contains(c.Id)))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToArray();

        var executedPhases = new List<ReleaseSearchPhase>();
        var failuresByConnection = new Dictionary<Guid, string>();
        var foundReleases = new List<Release>();
        var foundOrigins = new List<(int PhaseIndex, Guid ConnectionId)>();

        for (var phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = phases[phaseIndex];

            using var phaseActivity = ReleaseTelemetry.Source.StartActivity("SearchPhase");
            phaseActivity?.SetTag("phase_index", phaseIndex);

            var phaseConnections = connections
                .Where(connection => !failuresByConnection.ContainsKey(connection.Id))
                .ToArray();
            var results = await Task.WhenAll(phaseConnections.Select((connection, index) => RunPhaseConnectionAsync(
                connection,
                index,
                phaseIndex,
                indexers,
                phase.Request,
                categories,
                applicableIndexerIds,
                onConnectionStarted,
                preserveDuplicateObservations: false,
                cancellationToken: cancellationToken))).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                foreach (var connectionResult in results.OrderBy(result => result.Index))
                {
                    logger.LogTrace(
                        "EXEC ReleaseSearch {ExecutionJson}",
                        JsonSerializer.Serialize(new
                        {
                            Phase = phaseIndex,
                            ConnectionId = connectionResult.ConnectionId,
                            ConnectionName = connectionResult.ConnectionName,
                            ConnectionStatus = connectionResult.ConnectionStatus.ToString(),
                            Request = phase.Request with { Categories = categories },
                            ReleaseFacts = phase.ReleaseFacts,
                            Ran = connectionResult.Attempted,
                            SkippedReason = connectionResult.SkippedReason,
                            ResultCount = connectionResult.Releases.Count,
                            connectionResult.ElapsedMilliseconds,
                            Failure = connectionResult.Failure is { } failure ? failure.Error : null,
                        }));
                }
            }

            var releases = new List<Release>();
            var outcomes = new List<ReleaseSearchConnectionOutcome>();
            var attempted = 0;
            var succeeded = 0;
            foreach (var result in results.OrderBy(r => r.Index))
            {
                if (result.Attempted)
                {
                    attempted++;
                    if (result.Failure is null)
                    {
                        succeeded++;
                    }
                }

                if (result.Failure is { } failure)
                {
                    failuresByConnection[failure.ConnectionId] = result.Warning ?? failure.Error;
                }

                releases.AddRange(result.Releases);
                foreach (var release in result.Releases)
                {
                    foundReleases.Add(release);
                    foundOrigins.Add((phaseIndex, result.ConnectionId));
                }
            }

            var resultsByConnection = results.ToDictionary(result => result.ConnectionId);
            foreach (var connection in connections)
            {
                outcomes.Add(resultsByConnection.TryGetValue(connection.Id, out var result)
                    ? new ReleaseSearchConnectionOutcome
                    {
                        ConnectionId = result.ConnectionId,
                        ConnectionName = result.ConnectionName,
                        Attempted = result.Attempted,
                        SkippedReason = result.SkippedReason,
                        Error = result.Warning ?? result.Failure?.Error,
                    }
                    : new ReleaseSearchConnectionOutcome
                    {
                        ConnectionId = connection.Id,
                        ConnectionName = connection.Name,
                        Attempted = false,
                        SkippedReason = "failed-earlier-phase",
                        Error = failuresByConnection[connection.Id],
                    });
            }

            executedPhases.Add(phase with
            {
                Releases = releases,
                ConnectionsAttempted = attempted,
                ConnectionsSucceeded = succeeded,
                Connections = outcomes,
            });

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "EXEC ReleaseSearch {ExecutionJson}",
                    JsonSerializer.Serialize(new
                    {
                        Phase = phaseIndex,
                        Request = phase.Request with { Categories = categories },
                        ReleaseFacts = phase.ReleaseFacts,
                        ConnectionsAttempted = attempted,
                        ConnectionsSucceeded = succeeded,
                        ConnectionsTotal = connections.Length,
                        ResultCount = releases.Count,
                    }));
            }
        }

        if (failuresByConnection.Count > 0)
        {
            await RecordSearchFailuresAsync(connections, failuresByConnection, cancellationToken)
                .ConfigureAwait(false);
        }

        var groups = ShareIdentifiersAndSourcesAmongMatchingReleases(foundReleases);
        var candidatesByPhase = executedPhases.Select(_ => new List<ReleaseSearchCandidate>()).ToArray();
        foreach (var group in groups)
        {
            candidatesByPhase[foundOrigins[group[0]].PhaseIndex].Add(new ReleaseSearchCandidate
            {
                Key = BuildCandidateKey(foundReleases[group[0]]),
                Release = foundReleases[group[0]],
                ConnectionIds = [.. group.Select(index => foundOrigins[index].ConnectionId).Distinct()],
            });
        }

        for (var phaseIndex = 0; phaseIndex < executedPhases.Count; phaseIndex++)
        {
            executedPhases[phaseIndex] = executedPhases[phaseIndex] with
            {
                Candidates = candidatesByPhase[phaseIndex],
            };
        }

        return executedPhases;
    }

    public async Task<ReleaseQueryExecution> ExecutePlanAsync(
        IReadOnlyList<ReleaseSearchPhase> routes,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        ReleaseEvaluationContext evaluationContext,
        bool automatic,
        int maximumResults,
        Action<int, string>? onConnectionStarted = null,
        bool useCache = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(applicableIndexerIds);
        ArgumentNullException.ThrowIfNull(evaluationContext);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);

        var indexers = (await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(indexer => indexer.Id);
        var connections = (await indexerConnectionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(connection => connection.Name, StringComparer.Ordinal)
            .ThenBy(connection => connection.Id)
            .ToArray();
        var planned = connections.Select((connection, connectionIndex) =>
        {
            indexers.TryGetValue(connection.IndexerId, out var indexer);
            var integration = ResolveIntegration(indexer);
            var plannedRoutes = routes.Select((route, routeIndex) =>
            {
                var resolved = integration is null
                    ? route.Request with { Categories = categories, MaximumResults = maximumResults }
                    : ResolveAdvertisedSpellings(route.Request, integration.SearchFacets) with
                    {
                        Categories = categories,
                        MaximumResults = maximumResults,
                    };
                return new PlannedRoute(
                    $"connection-{connectionIndex + 1}-request-{routeIndex + 1}",
                    routeIndex,
                    route,
                    resolved,
                    integration is not null && IntegrationSupportsRequest(integration, resolved));
            }).ToArray();
            return new PlannedConnection(
                $"connection-{connectionIndex + 1}",
                connectionIndex,
                connection,
                indexer,
                integration,
                applicableIndexerIds.Contains(connection.IndexerId),
                plannedRoutes);
        }).ToArray();

        var available = planned.Where(connection => connection.BaseCanExecute && connection.Applicable).ToArray();
        var warning = connections.Length == 0
            ? "no-indexers-configured"
            : available.Length == 0
                ? "no-indexers-available"
                : available.All(connection => connection.Routes.All(route => !route.Supported))
                    ? "query-not-supported"
                    : null;
        var warnings = warning is null ? Array.Empty<string>() : [warning];
        var planFingerprint = Fingerprint(new
        {
            automatic,
            maximumResults,
            evaluationContext.ContentHierarchyItemId,
            Categories = categories,
            Connections = planned.Select(connection => new
            {
                connection.Connection.Id,
                connection.Connection.IndexerId,
                connection.Connection.Enabled,
                connection.Connection.Status,
                connection.Applicable,
                Routes = connection.Routes.Select(route => new
                {
                    route.ResolvedRequest,
                    route.Phase.ReleaseFacts,
                    route.Supported,
                }),
            }),
        });
        var serializedPlan = SerializePlan(
            planned,
            evaluationContext,
            automatic,
            maximumResults,
            planFingerprint,
            warnings);

        using var activity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.execute");
        activity?.SetTag("magpie.query.node.id", "root");
        activity?.SetTag("magpie.query.node.kind", "union");
        activity?.SetTag("magpie.query.maximum_results", maximumResults);
        activity?.SetTag("magpie.query.connection_count", planned.Length);
        activity?.SetTag("magpie.query.plan_json", serializedPlan);
        if (activity?.IsAllDataRequested == true)
        {
            activity.AddEvent(new ActivityEvent(
                "plan",
                tags: new ActivityTagsCollection
                {
                    ["magpie.query.plan_json"] = serializedPlan,
                    ["magpie.query.warnings"] = warnings,
                }));
        }

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("PLAN ReleaseSearch {PlanJson}", serializedPlan);
        }

        ConnectionExecution[] branches;
        try
        {
            branches = await Task.WhenAll(planned.Select(connection => ExecuteConnectionPlanAsync(
                connection,
                indexers,
                categories,
                applicableIndexerIds,
                releaseEvaluationService,
                evaluationContext,
                automatic,
                maximumResults,
                useCache,
                onConnectionStarted,
                cancellationToken))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("magpie.query.terminal_reason", "canceled");
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            throw;
        }

        var failures = branches
            .Where(branch => branch.Failure is not null)
            .ToDictionary(branch => branch.Connection.Connection.Id, branch => branch.Failure!);
        if (failures.Count > 0)
        {
            try
            {
                await RecordSearchFailuresAsync(connections, failures, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetTag("magpie.query.terminal_reason", "canceled");
                activity?.SetStatus(ActivityStatusCode.Error, "canceled");
                throw;
            }
        }

        var rootUnion = UnionRoundRobin(branches.OrderBy(branch => branch.Connection.Order)
            .Select(branch => branch.Results).ToArray(), evaluationContext);
        var combined = RankResults(rootUnion.Results, evaluationContext);
        var eligible = automatic
            ? combined.Where(result => result.Assessment.MatchResult is
                ReleaseMatchResult.Identity or ReleaseMatchResult.Confident).ToList()
            : combined;
        var accepted = eligible;
        var attempted = branches.Count(branch => branch.Attempted);
        var succeeded = branches.Count(branch => branch.Attempted && branch.Failure is null);
        var partial = failures.Count > 0;

        activity?.SetTag("magpie.query.raw_results", branches.Sum(branch => branch.RawCount));
        activity?.SetTag("magpie.query.evaluated_results", combined.Count);
        activity?.SetTag("magpie.query.accepted_results", accepted.Count);
        activity?.SetTag("magpie.query.rejected_results", combined.Count - eligible.Count);
        activity?.SetTag("magpie.query.partial", partial);
        RecordContributions(activity, rootUnion.Contributions);

        if (attempted > 0 && succeeded == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Every attempted connection failed.");
            activity?.SetTag("magpie.query.terminal_reason", "failed");
            throw new InvalidOperationException(
                $"Release search failed; every one of the {attempted} attempted connections failed.");
        }

        activity?.SetTag(
            "magpie.query.terminal_reason",
            attempted == 0 ? warning ?? "no-execution" : partial ? "partial-success" : "success");
        return new ReleaseQueryExecution
        {
            Results = accepted,
            ExecutedRoutes = branches.SelectMany(branch => branch.ExecutedRoutes).ToArray(),
            ConnectionsAttempted = attempted,
            ConnectionsSucceeded = succeeded,
            Partial = partial,
            Warning = warning,
        };
    }

    private async Task<ConnectionExecution> ExecuteConnectionPlanAsync(
        PlannedConnection planned,
        IReadOnlyDictionary<Guid, Indexer> indexers,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        IReleaseEvaluationService evaluator,
        ReleaseEvaluationContext evaluationContext,
        bool automatic,
        int maximumResults,
        bool useCache,
        Action<int, string>? onConnectionStarted,
        CancellationToken cancellationToken)
    {
        using var activity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.node.coalesce");
        activity?.SetTag("magpie.query.node.id", planned.NodeId);
        activity?.SetTag("magpie.query.node.kind", "coalesce");
        activity?.SetTag("indexer.connection.id", planned.Connection.Id.ToString());
        activity?.SetTag("indexer.connection.name", planned.Connection.Name);

        if (!planned.CanExecute)
        {
            var reason = planned.SkippedReason!;
            activity?.SetTag("magpie.query.skipped", reason);
            activity?.SetTag("magpie.query.terminal_reason", "skipped");
            RecordContributions(activity, new ContributionCounts());
            return new ConnectionExecution(planned, [], [], false, null, 0);
        }

        var results = new List<EvaluatedRelease>();
        var executedRoutes = new List<ReleaseSearchPhase>();
        var coalesceContributions = new ContributionCounts();
        var evaluationCounts = new EvaluationCounts(automatic);
        var rawCount = 0;
        var evaluatedCount = 0;
        var attempted = false;
        string? failure = null;

        try
        {
            foreach (var route in planned.Routes.Where(route => route.IsIdentity))
            {
                if (!route.Supported)
                {
                    TraceSkippedRoute(route, "unsupported");
                    continue;
                }

                var leaf = await FetchRouteAsync(route).ConfigureAwait(false);
                attempted |= leaf.Attempted;
                rawCount += leaf.RawCount;
                executedRoutes.Add(leaf.ExecutedRoute);
                if (leaf.Failure is not null)
                {
                    failure = leaf.Failure;
                    break;
                }

                var routeObservations = new List<PlannedObservation>();
                foreach (var release in leaf.Releases)
                {
                    AddObservation(routeObservations, route, release);
                }

                var routeResults = new List<EvaluatedRelease>();
                foreach (var observation in routeObservations)
                {
                    var existingIndex = results.FindIndex(existing =>
                        RefersToSameRelease(existing.Release, observation.Release));
                    if (existingIndex >= 0)
                    {
                        var existing = results[existingIndex];
                        MergeIdentifiersAndSources(existing.Release, observation.Release);
                        var enriched = new PlannedObservation(
                            existing.Release,
                            MergeReleaseFacts(existing.ReleaseFacts, observation.ReleaseFacts),
                            MergePlanNodeIds(existing.PlanNodeIds, observation.PlanNodeIds));
                        var reevaluated = Evaluate(enriched);
                        evaluationCounts.Add(reevaluated.Assessment);
                        results.RemoveAt(existingIndex);
                        var reevaluationContribution = AddUnique(results, reevaluated, evaluationContext);
                        coalesceContributions.Add(reevaluationContribution == ContributionKind.New
                            ? ContributionKind.Enriched
                            : reevaluationContribution);
                        routeResults.Add(reevaluated);
                        continue;
                    }

                    if (evaluatedCount >= maximumResults)
                    {
                        break;
                    }

                    var evaluated = Evaluate(observation);
                    evaluationCounts.Add(evaluated.Assessment);
                    var contribution = AddUnique(results, evaluated, evaluationContext);
                    coalesceContributions.Add(contribution);
                    routeResults.Add(evaluated);
                    if (contribution == ContributionKind.New)
                    {
                        evaluatedCount++;
                    }
                }

                if (routeResults.Any(result => result.Assessment.MatchResult is
                    ReleaseMatchResult.Identity or ReleaseMatchResult.Confident))
                {
                    foreach (var remaining in planned.Routes.Where(candidate =>
                        candidate.IsIdentity && candidate.Order > route.Order))
                    {
                        RecordSkippedNode(activity, remaining.NodeId, "external-id", "coalesce-satisfied");
                    }

                    RecordSkippedNode(
                        activity,
                        planned.NodeId + "-keywords",
                        "union",
                        "coalesce-satisfied");
                    foreach (var remaining in planned.Routes.Where(candidate => !candidate.IsIdentity))
                    {
                        RecordSkippedNode(activity, remaining.NodeId, "keyword", "coalesce-satisfied");
                    }

                    break;
                }
            }

            if (failure is null && !results.Any(result => IsUseful(result.Assessment)) &&
                evaluatedCount < maximumResults)
            {
                using var keywordActivity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.node.union");
                keywordActivity?.SetTag("magpie.query.node.id", planned.NodeId + "-keywords");
                keywordActivity?.SetTag("magpie.query.node.parent_id", planned.NodeId);
                keywordActivity?.SetTag("magpie.query.node.kind", "union");
                var keywordLeaves = new List<(PlannedRoute Route, RouteExecution Execution)>();
                var keywordEvaluationCounts = new EvaluationCounts(automatic);
                try
                {
                    foreach (var route in planned.Routes.Where(route => !route.IsIdentity))
                    {
                        if (!route.Supported)
                        {
                            TraceSkippedRoute(route, "unsupported");
                            continue;
                        }

                        var leaf = await FetchRouteAsync(route).ConfigureAwait(false);
                        attempted |= leaf.Attempted;
                        rawCount += leaf.RawCount;
                        executedRoutes.Add(leaf.ExecutedRoute);
                        keywordLeaves.Add((route, leaf));
                        if (leaf.Failure is not null)
                        {
                            failure = leaf.Failure;
                            break;
                        }
                    }

                    var keywordContributions = new ContributionCounts();
                    var observations = new List<PlannedObservation>();
                    for (var offset = 0;
                        keywordLeaves.Any(leaf => offset < leaf.Execution.Releases.Count);
                        offset++)
                    {
                        foreach (var leaf in keywordLeaves)
                        {
                            if (offset >= leaf.Execution.Releases.Count)
                            {
                                continue;
                            }

                            AddObservation(observations, leaf.Route, leaf.Execution.Releases[offset]);
                        }
                    }

                    foreach (var observation in observations)
                    {
                        var existingIndex = results.FindIndex(existing =>
                            RefersToSameRelease(existing.Release, observation.Release));
                        if (existingIndex >= 0)
                        {
                            var existing = results[existingIndex];
                            MergeIdentifiersAndSources(existing.Release, observation.Release);
                            var enriched = new PlannedObservation(
                                existing.Release,
                                MergeReleaseFacts(existing.ReleaseFacts, observation.ReleaseFacts),
                                MergePlanNodeIds(existing.PlanNodeIds, observation.PlanNodeIds));
                            var reevaluated = Evaluate(enriched);
                            evaluationCounts.Add(reevaluated.Assessment);
                            keywordEvaluationCounts.Add(reevaluated.Assessment);
                            results.RemoveAt(existingIndex);
                            var duplicateContribution = AddUnique(results, reevaluated, evaluationContext);
                            if (duplicateContribution == ContributionKind.New)
                            {
                                duplicateContribution = ContributionKind.Enriched;
                            }

                            keywordContributions.Add(duplicateContribution);
                            coalesceContributions.Add(duplicateContribution);
                            continue;
                        }

                        if (evaluatedCount >= maximumResults)
                        {
                            break;
                        }

                        var evaluated = Evaluate(observation);
                        evaluationCounts.Add(evaluated.Assessment);
                        keywordEvaluationCounts.Add(evaluated.Assessment);
                        keywordContributions.Add(ContributionKind.New);
                        var contribution = AddUnique(results, evaluated, evaluationContext);
                        coalesceContributions.Add(contribution);
                        evaluatedCount++;
                    }

                    RecordContributions(keywordActivity, keywordContributions);
                    RecordEvaluation(
                        keywordActivity,
                        keywordLeaves.Sum(leaf => leaf.Execution.RawCount),
                        keywordEvaluationCounts);
                    keywordActivity?.SetTag("magpie.query.result_count", observations.Count);
                    keywordActivity?.SetTag("magpie.query.terminal_reason", failure is null ? "complete" : "failed");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    keywordActivity?.SetTag("magpie.query.terminal_reason", "canceled");
                    keywordActivity?.SetStatus(ActivityStatusCode.Error, "canceled");
                    throw;
                }
            }

            activity?.SetTag("magpie.query.raw_results", rawCount);
            RecordEvaluation(activity, rawCount, evaluationCounts);
            RecordContributions(activity, coalesceContributions);
            if (failure is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "provider-error");
            }

            activity?.SetTag("magpie.query.terminal_reason", failure is not null
                ? "failed"
                : results.Any(result => IsAccepted(result.Assessment))
                    ? "accepted"
                    : "exhausted");

            return new ConnectionExecution(planned, results, executedRoutes, attempted, failure, rawCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("magpie.query.terminal_reason", "canceled");
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            throw;
        }

        async Task<RouteExecution> FetchRouteAsync(PlannedRoute route)
        {
            var nodeKind = route.IsIdentity ? "external-id" : "keyword";
            using var requestActivity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.node." + nodeKind);
            requestActivity?.SetTag("magpie.query.node.id", route.NodeId);
            requestActivity?.SetTag(
                "magpie.query.node.parent_id",
                route.IsIdentity ? planned.NodeId : planned.NodeId + "-keywords");
            requestActivity?.SetTag("magpie.query.node.kind", nodeKind);
            requestActivity?.SetTag(
                "magpie.query.request_json",
                JsonSerializer.Serialize(route.ResolvedRequest, JsonSerializerOptions.Web));
            requestActivity?.SetTag("indexer.connection.id", planned.Connection.Id.ToString());
            var cache = useCache ? cacheFactory.GetOrCreate(
                "release-query",
                () => new CachePolicy
                {
                    MaximumLevel = CacheLevel.Memory,
                    Freshness = searchOptions.CoalescingWindow,
                    Retention = searchOptions.CoalescingWindow,
                    MaximumEntries = 1000,
                    ConcurrencyMode = CacheConcurrencyMode.Pessimistic,
                }) : null;
            var cacheIdentity = JsonSerializer.Serialize(new
            {
                planned.Connection.Id,
                planned.Connection.IndexerId,
                Settings = planned.Connection.Settings.OrderBy(setting => setting.Key, StringComparer.Ordinal),
                Request = route.ResolvedRequest with { MaximumResults = 0 },
            });
            var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheIdentity)));
            CachedRouteResult routeResult;
            var populated = false;
            var refreshed = false;
            try
            {
                if (cache is null)
                {
                    routeResult = await PopulateAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var state = await cache.GetOrAddAsync(
                        cacheKey,
                        async token =>
                        {
                            populated = true;
                            return new CachedRouteState(await PopulateAsync(token).ConfigureAwait(false));
                        },
                        cancellationToken).ConfigureAwait(false);
                    if (populated)
                    {
                        routeResult = state.Result;
                    }
                    else
                    {
                        (routeResult, refreshed) = await state.GetOrRefreshAsync(
                            maximumResults,
                            PopulateAsync,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (refreshed)
                    {
                        cache.Set(cacheKey, state);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                requestActivity?.SetTag("magpie.query.terminal_reason", "canceled");
                requestActivity?.SetStatus(ActivityStatusCode.Error, "canceled");
                throw;
            }
            catch (RouteCachePopulationException ex)
            {
                routeResult = new CachedRouteResult([], ex.Attempted, ex.Failure, 0);
            }

            var cacheOutcome = cache is null
                ? "miss"
                : requestActivity?.GetTagItem("magpie.cache.outcome") as string ?? (populated ? "miss" : "hit");
            if (refreshed)
            {
                requestActivity?.SetTag("magpie.cache.outcome", "refresh");
                requestActivity?.SetTag("magpie.cache.hit", false);
                DataTelemetry.CacheRefresh.Add(
                    1,
                    new KeyValuePair<string, object?>("cache", "release-query"),
                    new KeyValuePair<string, object?>("lifetime", "shared"),
                    new KeyValuePair<string, object?>("backend", "memory"),
                    new KeyValuePair<string, object?>("level", "memory"));
            }

            requestActivity?.SetTag("magpie.query.cache", refreshed ? "refresh" : cacheOutcome);
            if (cache is null)
            {
                requestActivity?.SetTag("magpie.cache.outcome", cacheOutcome);
                requestActivity?.SetTag("magpie.cache.hit", false);
                requestActivity?.SetTag("magpie.cache.name", "release-query");
                requestActivity?.SetTag("magpie.cache.lifetime", "none");
                requestActivity?.SetTag("magpie.cache.backend", "none");
                requestActivity?.SetTag("magpie.cache.level", "none");
            }

            var rawReleases = routeResult.Releases.Select(CloneRelease).ToArray();
            var releases = DeduplicateReleases(rawReleases);
            var routeAttempted = routeResult.Attempted;
            var routeFailure = routeResult.Failure;

            requestActivity?.SetTag("magpie.query.raw_results", rawReleases.Length);
            if (routeFailure is not null)
            {
                requestActivity?.SetStatus(ActivityStatusCode.Error, "provider-error");
            }

            requestActivity?.SetTag("magpie.query.terminal_reason", routeFailure is null ? "complete" : "failed");

            return new RouteExecution(
                releases,
                route.Phase with
                {
                    Releases = releases,
                    ConnectionsAttempted = routeAttempted ? 1 : 0,
                    ConnectionsSucceeded = routeAttempted && routeFailure is null ? 1 : 0,
                },
                routeAttempted,
                routeFailure,
                rawReleases.Length);

            async ValueTask<CachedRouteResult> PopulateAsync(CancellationToken token)
            {
                var result = await RunPhaseConnectionAsync(
                    planned.Connection,
                    planned.Order,
                    route.Order,
                    indexers,
                    route.ResolvedRequest,
                    categories,
                    applicableIndexerIds,
                    onConnectionStarted,
                    preserveDuplicateObservations: true,
                    cancellationToken: token).ConfigureAwait(false);
                if (result.Failure is { } failure)
                {
                    throw new RouteCachePopulationException(result.Attempted, failure.Error);
                }

                return new CachedRouteResult(
                    result.Releases,
                    result.Attempted,
                    null,
                    maximumResults);
            }
        }

        EvaluatedRelease Evaluate(PlannedObservation observation)
        {
            try
            {
                var assessment = evaluator.AssessMatch(
                    observation.Release,
                    observation.ReleaseFacts,
                    evaluationContext);
                return new EvaluatedRelease(
                    observation.Release,
                    observation.ReleaseFacts,
                    assessment,
                    observation.PlanNodeIds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Release evaluation failed with failure type {FailureType}",
                    ex.GetType().Name);
                return new EvaluatedRelease(
                    observation.Release,
                    observation.ReleaseFacts,
                    new ReleaseAssessment { MatchResult = ReleaseMatchResult.NoMatch },
                    observation.PlanNodeIds);
            }
        }

        bool IsAccepted(ReleaseAssessment assessment) =>
            !automatic || assessment.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident;

        static bool IsUseful(ReleaseAssessment assessment) =>
            assessment.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident;
    }

    private void TraceSkippedRoute(PlannedRoute route, string reason)
    {
        var nodeKind = route.IsIdentity ? "external-id" : "keyword";
        using var activity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.node." + nodeKind);
        activity?.SetTag("magpie.query.node.id", route.NodeId);
        activity?.SetTag("magpie.query.node.kind", nodeKind);
        activity?.SetTag(
            "magpie.query.request_json",
            JsonSerializer.Serialize(route.ResolvedRequest, JsonSerializerOptions.Web));
        activity?.SetTag("magpie.query.skipped", reason);
        activity?.SetTag("magpie.query.terminal_reason", "skipped");
    }

    private List<Release> DeduplicateReleases(IEnumerable<Release> releases)
    {
        var result = new List<Release>();
        foreach (var candidate in releases)
        {
            var matchingIndexes = result
                .Select((existing, index) => (existing, index))
                .Where(pair => RefersToSameRelease(pair.existing, candidate))
                .Select(pair => pair.index)
                .ToList();
            if (matchingIndexes.Count == 0)
            {
                result.Add(candidate);
                continue;
            }

            var primaryIndex = matchingIndexes[0];
            var primary = result[primaryIndex];
            MergeIdentifiersAndSources(primary, candidate);
            while (true)
            {
                matchingIndexes = result
                    .Select((existing, index) => (existing, index))
                    .Where(pair => pair.index != primaryIndex &&
                        RefersToSameRelease(primary, pair.existing))
                    .Select(pair => pair.index)
                    .ToList();
                if (matchingIndexes.Count == 0)
                {
                    break;
                }

                foreach (var index in matchingIndexes.OrderDescending())
                {
                    MergeIdentifiersAndSources(primary, result[index]);
                    result.RemoveAt(index);
                    if (index < primaryIndex)
                    {
                        primaryIndex--;
                    }
                }
            }
        }

        return result;
    }

    private void AddObservation(
        List<PlannedObservation> observations,
        PlannedRoute route,
        Release release)
    {
        var matchingIndexes = observations
            .Select((existing, index) => (existing, index))
            .Where(pair => RefersToSameRelease(pair.existing.Release, release))
            .Select(pair => pair.index)
            .ToList();
        if (matchingIndexes.Count == 0)
        {
            observations.Add(new PlannedObservation(release, route.Phase.ReleaseFacts, [route.NodeId]));
            return;
        }

        var primaryIndex = matchingIndexes[0];
        var primary = observations[primaryIndex];
        MergeIdentifiersAndSources(primary.Release, release);
        primary = primary with
        {
            ReleaseFacts = MergeReleaseFacts(primary.ReleaseFacts, route.Phase.ReleaseFacts),
            PlanNodeIds = MergePlanNodeIds(primary.PlanNodeIds, [route.NodeId]),
        };
        observations[primaryIndex] = primary;
        while (true)
        {
            matchingIndexes = observations
                .Select((existing, index) => (existing, index))
                .Where(pair => pair.index != primaryIndex &&
                    RefersToSameRelease(primary.Release, pair.existing.Release))
                .Select(pair => pair.index)
                .ToList();
            if (matchingIndexes.Count == 0)
            {
                break;
            }

            foreach (var index in matchingIndexes.OrderDescending())
            {
                var duplicate = observations[index];
                MergeIdentifiersAndSources(primary.Release, duplicate.Release);
                primary = primary with
                {
                    ReleaseFacts = MergeReleaseFacts(primary.ReleaseFacts, duplicate.ReleaseFacts),
                    PlanNodeIds = MergePlanNodeIds(primary.PlanNodeIds, duplicate.PlanNodeIds),
                };
                observations[primaryIndex] = primary;
                observations.RemoveAt(index);
                if (index < primaryIndex)
                {
                    primaryIndex--;
                }
            }
        }
    }

    private ContributionKind AddUnique(
        List<EvaluatedRelease> target,
        EvaluatedRelease candidate,
        ReleaseEvaluationContext evaluationContext)
    {
        var matchingIndexes = target
            .Select((existing, index) => (existing, index))
            .Where(pair => RefersToSameRelease(pair.existing.Release, candidate.Release))
            .Select(pair => pair.index)
            .ToList();
        if (matchingIndexes.Count == 0)
        {
            target.Add(candidate);
            return ContributionKind.New;
        }

        var primaryIndex = matchingIndexes[0];
        var primary = target[primaryIndex];
        var planNodeIds = MergePlanNodeIds(primary.PlanNodeIds, candidate.PlanNodeIds);
        var changed = MergeIdentifiersAndSources(primary.Release, candidate.Release);
        if (IsStronger(candidate, primary, evaluationContext))
        {
            changed |= MergeIdentifiersAndSources(candidate.Release, primary.Release);
            primary = candidate with { PlanNodeIds = planNodeIds };
            changed = true;
        }
        else
        {
            changed |= planNodeIds.Count != primary.PlanNodeIds.Count;
            primary = primary with { PlanNodeIds = planNodeIds };
        }

        target[primaryIndex] = primary;

        while (true)
        {
            matchingIndexes = target
                .Select((existing, index) => (existing, index))
                .Where(pair => pair.index != primaryIndex &&
                    RefersToSameRelease(primary.Release, pair.existing.Release))
                .Select(pair => pair.index)
                .ToList();
            if (matchingIndexes.Count == 0)
            {
                break;
            }

            foreach (var index in matchingIndexes.OrderDescending())
            {
                var duplicate = target[index];
                changed |= MergeIdentifiersAndSources(primary.Release, duplicate.Release);
                planNodeIds = MergePlanNodeIds(primary.PlanNodeIds, duplicate.PlanNodeIds);
                if (IsStronger(duplicate, primary, evaluationContext))
                {
                    MergeIdentifiersAndSources(duplicate.Release, primary.Release);
                    primary = duplicate with { PlanNodeIds = planNodeIds };
                }
                else
                {
                    primary = primary with { PlanNodeIds = planNodeIds };
                }

                target[primaryIndex] = primary;
                target.RemoveAt(index);
                if (index < primaryIndex)
                {
                    primaryIndex--;
                }
            }

            changed = true;
        }

        return changed ? ContributionKind.Enriched : ContributionKind.Unchanged;
    }

    private IReadOnlyList<string> MergePlanNodeIds(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second) =>
        first.Concat(second).Distinct(StringComparer.Ordinal).ToArray();

    private IReadOnlyList<ContentFact> MergeReleaseFacts(
        IReadOnlyList<ContentFact> first,
        IReadOnlyList<ContentFact> second)
    {
        var merged = new List<ContentFact>(first);
        foreach (var fact in second)
        {
            if (!merged.Any(existing => existing.ContentEquals(fact)))
            {
                merged.Add(fact);
            }
        }

        return merged;
    }

    private bool IsStronger(
        EvaluatedRelease candidate,
        EvaluatedRelease current,
        ReleaseEvaluationContext evaluationContext)
    {
        if (candidate.Assessment.MatchResult != current.Assessment.MatchResult)
        {
            return candidate.Assessment.MatchResult > current.Assessment.MatchResult;
        }

        var candidateRank = releaseEvaluationService.EffectiveQualityRank(candidate.Assessment, evaluationContext);
        var currentRank = releaseEvaluationService.EffectiveQualityRank(current.Assessment, evaluationContext);
        if (candidateRank != currentRank)
        {
            return candidateRank < currentRank;
        }

        foreach (var comparer in releaseComparers)
        {
            if (!comparer.CanCompare(candidate.Release) || !comparer.CanCompare(current.Release))
            {
                continue;
            }

            var comparison = comparer.Compare(candidate.Release, current.Release);
            if (comparison != 0)
            {
                return comparison < 0;
            }
        }

        return candidate.Release.PublishDate > current.Release.PublishDate;
    }

    private UnionExecution UnionRoundRobin(
        IReadOnlyList<IReadOnlyList<EvaluatedRelease>> children,
        ReleaseEvaluationContext evaluationContext)
    {
        var result = new List<EvaluatedRelease>();
        var contributions = new ContributionCounts();
        for (var offset = 0; children.Any(child => offset < child.Count); offset++)
        {
            foreach (var child in children)
            {
                if (offset < child.Count)
                {
                    contributions.Add(AddUnique(result, child[offset], evaluationContext));
                }
            }
        }

        return new UnionExecution(result, contributions);
    }

    private void RecordContributions(Activity? activity, ContributionCounts contributions)
    {
        activity?.SetTag("magpie.query.contributions.new", contributions.New);
        activity?.SetTag("magpie.query.contributions.enriched", contributions.Enriched);
        activity?.SetTag("magpie.query.contributions.unchanged", contributions.Unchanged);
    }

    private void RecordEvaluation(Activity? activity, int rawCount, EvaluationCounts counts)
    {
        activity?.SetTag("magpie.query.raw_results", rawCount);
        activity?.SetTag("magpie.query.evaluated_results", counts.Evaluated);
        activity?.SetTag("magpie.query.accepted_results", counts.Accepted);
        activity?.SetTag("magpie.query.rejected_results", counts.Rejected);
    }

    private void RecordSkippedNode(Activity? activity, string nodeId, string nodeKind, string reason)
    {
        activity?.AddEvent(new ActivityEvent(
            "node.skipped",
            tags: new ActivityTagsCollection
            {
                ["magpie.query.node.id"] = nodeId,
                ["magpie.query.node.kind"] = nodeKind,
                ["magpie.query.node.reason"] = reason,
            }));
    }

    private string SerializePlan(
        IReadOnlyList<PlannedConnection> connections,
        ReleaseEvaluationContext evaluationContext,
        bool automatic,
        int maximumResults,
        string fingerprint,
        IReadOnlyList<string> warnings)
    {
        static object Precondition(string kind, string? nodeId = null) => new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["nodeId"] = nodeId,
        };

        object RouteNode(
            PlannedConnection connection,
            PlannedRoute route,
            object precondition) => new Dictionary<string, object?>
            {
                ["id"] = route.NodeId,
                ["kind"] = route.IsIdentity ? "external-id" : "keyword",
                ["provider"] = connection.Indexer?.ProviderType,
                ["fingerprint"] = Fingerprint(new
                {
                    connection.Connection.Id,
                    route.ResolvedRequest,
                    route.Phase.ReleaseFacts,
                }),
                ["warnings"] = route.Supported ? Array.Empty<string>() : ["query-not-supported"],
                ["precondition"] = precondition,
                ["contributes"] = "evaluated release observations",
                ["executable"] = connection.BaseCanExecute && connection.Applicable && route.Supported,
                ["routePlan"] = new Dictionary<string, object?>
                {
                    ["request"] = route.ResolvedRequest,
                    ["releaseFacts"] = route.Phase.ReleaseFacts,
                    ["canExecute"] = route.Supported,
                    ["maximumResults"] = maximumResults,
                    ["warnings"] = route.Supported ? Array.Empty<string>() : ["query-not-supported"],
                },
                ["children"] = Array.Empty<object>(),
            };

        object ConnectionNode(PlannedConnection connection)
        {
            var identityRoutes = connection.Routes.Where(route => route.IsIdentity).ToArray();
            var identityChildren = identityRoutes
                .Select((route, index) => RouteNode(
                    connection,
                    route,
                    index == 0
                        ? Precondition("always")
                        : Precondition("nodeNotAccepted", identityRoutes[index - 1].NodeId)))
                .ToList();
            var keywordChildren = connection.Routes
                .Where(route => !route.IsIdentity)
                .Select(route => RouteNode(connection, route, Precondition("always")))
                .ToList();
            identityChildren.Add(new Dictionary<string, object?>
            {
                ["id"] = connection.NodeId + "-keywords",
                ["kind"] = "union",
                ["provider"] = connection.Indexer?.ProviderType,
                ["fingerprint"] = Fingerprint(keywordChildren),
                ["warnings"] = Array.Empty<string>(),
                ["precondition"] = identityRoutes.Length == 0
                    ? Precondition("always")
                    : Precondition("nodeNotAccepted", identityRoutes[^1].NodeId),
                ["contributes"] = "round-robin keyword observations",
                ["children"] = keywordChildren,
            });
            return new Dictionary<string, object?>
            {
                ["id"] = connection.NodeId,
                ["kind"] = "coalesce",
                ["provider"] = connection.Indexer?.ProviderType,
                ["fingerprint"] = Fingerprint(new
                {
                    connection.Connection.Id,
                    connection.Connection.IndexerId,
                    connection.Applicable,
                    connection.Connection.Enabled,
                    connection.Connection.Status,
                }),
                ["warnings"] = connection.SkippedReason is null
                    ? Array.Empty<string>()
                    : [connection.SkippedReason],
                ["precondition"] = Precondition("always"),
                ["contributes"] = "first useful identity route or keyword union",
                ["executable"] = connection.CanExecute,
                ["skippedReason"] = connection.SkippedReason,
                ["connectionId"] = connection.Connection.Id,
                ["name"] = connection.Connection.Name,
                ["enabled"] = connection.Connection.Enabled,
                ["health"] = connection.Connection.Status.ToString(),
                ["indexerId"] = connection.Indexer?.Id,
                ["children"] = identityChildren,
            };
        }

        var plan = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["provider"] = "magpie.release-search",
            ["fingerprint"] = fingerprint,
            ["mode"] = automatic ? "automatic" : "manual",
            ["maximumResults"] = maximumResults,
            ["warnings"] = warnings,
            ["target"] = new Dictionary<string, object?>
            {
                ["contentHierarchyItemId"] = evaluationContext.ContentHierarchyItemId,
                ["contentType"] = evaluationContext.Target?.Item.ContentType,
                ["lineage"] = evaluationContext.Target?.Lineage.Select(item => new
                {
                    item.Id,
                    item.Level,
                    item.Name,
                }),
            },
            ["root"] = new Dictionary<string, object?>
            {
                ["id"] = "root",
                ["kind"] = "union",
                ["provider"] = "magpie.release-search",
                ["fingerprint"] = fingerprint,
                ["warnings"] = warnings,
                ["precondition"] = Precondition("always"),
                ["contributes"] = "deduplicated connection candidates",
                ["children"] = connections.Select(ConnectionNode).ToList(),
            },
        };
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        return JsonSerializer.Serialize(plan, options);
    }

    private string Fingerprint(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private List<EvaluatedRelease> RankResults(
        List<EvaluatedRelease> results,
        ReleaseEvaluationContext context)
    {
        var comparers = releaseComparers.ToArray();
        var candidates = results.Select((result, inputOrder) => (
            Result: result,
            EffectiveQualityRank: releaseEvaluationService.EffectiveQualityRank(result.Assessment, context),
            InputOrder: inputOrder));
        var ranked = new List<(EvaluatedRelease Result, int EffectiveQualityRank, int InputOrder)>(results.Count);
        var primaryGroups = candidates
            .GroupBy(candidate => (candidate.Result.Assessment.MatchResult, candidate.EffectiveQualityRank))
            .OrderByDescending(group => group.Key.MatchResult)
            .ThenBy(group => group.Key.EffectiveQualityRank);
        foreach (var primaryGroup in primaryGroups)
        {
            var tieGroups = new List<List<(EvaluatedRelease Result, int EffectiveQualityRank, int InputOrder)>>
            {
                primaryGroup.ToList(),
            };
            foreach (var comparer in comparers)
            {
                var refinedGroups = new List<List<(
                    EvaluatedRelease Result,
                    int EffectiveQualityRank,
                    int InputOrder)>>();
                foreach (var tieGroup in tieGroups)
                {
                    if (tieGroup.Count < 2 || !tieGroup.All(candidate => comparer.CanCompare(candidate.Result.Release)))
                    {
                        refinedGroups.Add(tieGroup);
                        continue;
                    }

                    tieGroup.Sort((left, right) => comparer.Compare(left.Result.Release, right.Result.Release));
                    var start = 0;
                    while (start < tieGroup.Count)
                    {
                        var end = start + 1;
                        while (end < tieGroup.Count && comparer.Compare(
                            tieGroup[start].Result.Release,
                            tieGroup[end].Result.Release) == 0)
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
                    var comparison = right.Result.Release.PublishDate.CompareTo(left.Result.Release.PublishDate);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    comparison = string.CompareOrdinal(
                        StableKey(left.Result.Release),
                        StableKey(right.Result.Release));
                    return comparison != 0 ? comparison : left.InputOrder.CompareTo(right.InputOrder);
                });
                ranked.AddRange(tieGroup);
            }
        }

        return [.. ranked.Select(candidate => candidate.Result)];
    }

    private ReleaseSearchRequest ResolveAdvertisedSpellings(
        ReleaseSearchRequest request,
        IReadOnlyList<IReadOnlyCollection<string>> searchFacets)
    {
        var criteria = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in request.Criteria)
        {
            var advertised = ResolveAdvertisedFacetKey(key, searchFacets);
            criteria[advertised] = criteria.TryGetValue(advertised, out var existing)
                ? [.. existing, .. values]
                : values;
        }

        return request with
        {
            IdSearchParameter = request.IdSearchParameter is { } parameter
                ? ResolveAdvertisedFacetKey(parameter, searchFacets)
                : null,
            Criteria = criteria,
        };
    }

    private string ResolveAdvertisedFacetKey(string key, IReadOnlyList<IReadOnlyCollection<string>> searchFacets)
    {
        foreach (var set in searchFacets)
        {
            foreach (var advertised in set)
            {
                if (string.Equals(
                    advertised.NormalizeExternalIdentifierName(),
                    key.NormalizeExternalIdentifierName(),
                    StringComparison.Ordinal))
                {
                    return advertised;
                }
            }
        }

        return key;
    }

    private List<List<int>> ShareIdentifiersAndSourcesAmongMatchingReleases(IReadOnlyList<Release> releases)
    {
        var groups = new List<List<int>>();
        for (var releaseIndex = 0; releaseIndex < releases.Count; releaseIndex++)
        {
            var release = releases[releaseIndex];
            var matchingGroupIndexes = Enumerable.Range(0, groups.Count)
                .Where(index => RefersToSameRelease(release, releases[groups[index][0]]))
                .ToArray();
            if (matchingGroupIndexes.Length == 0)
            {
                groups.Add([releaseIndex]);
                continue;
            }

            var group = groups[matchingGroupIndexes[0]];
            MergeIdentifiersAndSources(releases[group[0]], release);
            group.Add(releaseIndex);
            foreach (var index in matchingGroupIndexes.Skip(1).Reverse())
            {
                MergeIdentifiersAndSources(releases[group[0]], releases[groups[index][0]]);
                group.AddRange(groups[index]);
                groups.RemoveAt(index);
            }
        }

        foreach (var group in groups.Where(group => group.Count > 1))
        {
            var aggregate = releases[group[0]];
            foreach (var index in group.Skip(1))
            {
                MergeIdentifiersAndSources(releases[index], aggregate);
            }
        }

        return groups;
    }

    private async Task<(
        int Index,
        Guid ConnectionId,
        string ConnectionName,
        IntegrationHealthStatus ConnectionStatus,
        bool Attempted,
        string? SkippedReason,
        IReadOnlyList<Release> Releases,
        long ElapsedMilliseconds,
        string? Warning,
        (Guid ConnectionId, string Error)? Failure)> RunPhaseConnectionAsync(
            IndexerConnection connection,
            int index,
            int phaseIndex,
            IReadOnlyDictionary<Guid, Indexer> indexers,
            ReleaseSearchRequest request,
            IReadOnlyList<string> categories,
            ISet<Guid> applicableIndexerIds,
            Action<int, string>? onConnectionStarted,
            bool preserveDuplicateObservations,
            CancellationToken cancellationToken)
    {
        var attempted = false;
        var stopwatch = Stopwatch.StartNew();
        indexers.TryGetValue(connection.IndexerId, out var indexer);

        using var activity = ReleaseTelemetry.Source.StartActivity("magpie.release-query.request");
        var indexerIdTag = new KeyValuePair<string, object?>("indexer_id", connection.IndexerId.ToString());
        activity?.SetTag("indexer.id", connection.IndexerId.ToString());
        activity?.SetTag("indexer.name", indexer?.Name);
        activity?.SetTag("indexer.connection.id", connection.Id.ToString());
        activity?.SetTag("indexer.connection.name", connection.Name);
        activity?.SetTag("connection.status", connection.Status.ToString());

        void Record(string outcome, int resultCount, bool failed)
        {
            activity?.SetTag("outcome", outcome);
            activity?.SetTag("result_count", resultCount);
            ReleaseTelemetry.IndexerQueries.Add(
                1,
                indexerIdTag,
                new KeyValuePair<string, object?>("outcome", outcome));
            if (attempted)
            {
                ReleaseTelemetry.IndexerQueryDuration.Record(
                    stopwatch.Elapsed.TotalMilliseconds,
                    indexerIdTag,
                    new KeyValuePair<string, object?>("outcome", failed ? "failure" : "success"));
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (connection.Status == IntegrationHealthStatus.Unhealthy)
            {
                Record("skipped_unhealthy", 0, failed: false);
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    "unhealthy",
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null,
                    null);
            }

            if (indexer is null)
            {
                throw new InvalidOperationException($"Indexer {connection.IndexerId} does not exist.");
            }

            var integration = ResolveIntegration(indexer);
            if (integration is null)
            {
                throw new InvalidOperationException(
                    $"No indexer integration is available for provider type {indexer.ProviderType}.");
            }

            if (!applicableIndexerIds.Contains(indexer.Id))
            {
                Record("skipped_not_applicable", 0, failed: false);
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    "content-type-not-applicable",
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null,
                    null);
            }

            var resolved = ResolveAdvertisedSpellings(request, integration.SearchFacets);
            if (!IntegrationSupportsRequest(integration, resolved))
            {
                Record("skipped_not_supported", 0, failed: false);
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    RequestNotSupportedReason,
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null,
                    null);
            }

            onConnectionStarted?.Invoke(phaseIndex, connection.Name);
            attempted = true;

            var query = resolved with { Categories = categories };
            activity?.SetTag("magpie.request.role", query.IdSearchParameter is null ? "keyword" : "external-id");
            activity?.SetTag("magpie.request.target", JsonSerializer.Serialize(query, JsonSerializerOptions.Web));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ConnectionPhaseDeadline);
            var found = await integration
                .SearchAsync(query, connection.Settings, deadline.Token)
                .ConfigureAwait(false);

            var releases = new List<Release>();
            var releasesByExternalId = new Dictionary<string, Release>(StringComparer.Ordinal);
            foreach (var release in found)
            {
                release.IndexerId = indexer.Id;
                release.Sources.Add(new ReleaseSource
                {
                    Key = string.Empty,
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
                });
                AttachServedIdentifier(release, query);

                if (preserveDuplicateObservations)
                {
                    releases.Add(release);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(release.ExternalId) &&
                    releasesByExternalId.TryGetValue(release.ExternalId, out var existing))
                {
                    if (HasConflictingImmutableIdentity(existing, release))
                    {
                        releases.Add(release);
                        releasesByExternalId.Remove(release.ExternalId);
                        continue;
                    }

                    MergeIdentifiersAndSources(existing, release);
                    continue;
                }

                releases.Add(release);
                if (!string.IsNullOrWhiteSpace(release.ExternalId))
                {
                    releasesByExternalId.Add(release.ExternalId, release);
                }
            }

            Record("attempted", releases.Count, failed: false);
            return (
                index,
                connection.Id,
                connection.Name,
                connection.Status,
                true,
                null,
                releases,
                stopwatch.ElapsedMilliseconds,
                null,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("magpie.query.terminal_reason", "canceled");
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "provider-error");
            Record(attempted ? "attempted" : "error", 0, failed: true);
            ReleaseTelemetry.IndexerQueryFailures.Add(
                1,
                indexerIdTag,
                new KeyValuePair<string, object?>("kind", "exception"));
            logger.LogWarning(
                "The indexer search failed for connection {ConnectionName} ({ConnectionId}) with {FailureType}",
                connection.Name,
                connection.Id,
                ex.GetType().Name);
            return (
                index,
                connection.Id,
                connection.Name,
                connection.Status,
                attempted,
                null,
                [],
                stopwatch.ElapsedMilliseconds,
                ClassifyProviderFailure(ex),
                (connection.Id, ClassifyProviderFailure(ex)));
        }
    }

    private bool IntegrationSupportsRequest(IIndexerIntegration integration, ReleaseSearchRequest request)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(request.Title);
        var facetKeys = new List<string>();
        foreach (var key in request.Criteria.Keys)
        {
            facetKeys.Add(key);
        }

        if (!string.IsNullOrWhiteSpace(request.IdSearchParameter))
        {
            facetKeys.Add(request.IdSearchParameter);
        }

        if (!hasTitle && facetKeys.Count == 0)
        {
            return false;
        }

        if (hasTitle && !integration.SupportsKeywordSearch)
        {
            return false;
        }

        if (facetKeys.Count > 0 && !FitsAnyFacetSet(facetKeys, requiresQ: hasTitle, integration.SearchFacets))
        {
            return false;
        }

        return true;
    }

    private bool FitsAnyFacetSet(
        IReadOnlyList<string> requiredFacetKeys,
        bool requiresQ,
        IReadOnlyList<IReadOnlyCollection<string>> searchFacets)
    {
        foreach (var set in searchFacets)
        {
            var available = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
            if (requiresQ && !available.Contains("q"))
            {
                continue;
            }

            var fits = true;
            foreach (var key in requiredFacetKeys)
            {
                if (!available.Contains(key))
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
            {
                return true;
            }
        }

        return false;
    }

    private async Task RecordSearchFailuresAsync(
        IReadOnlyList<IndexerConnection> connections,
        IReadOnlyDictionary<Guid, string> failures,
        CancellationToken cancellationToken)
    {
        var failedAt = timeProvider.GetUtcNow().UtcDateTime;
        var connectionsById = connections.ToDictionary(connection => connection.Id);
        foreach (var (connectionId, error) in failures)
        {
            if (!connectionsById.TryGetValue(connectionId, out var connection) ||
                connection.Status == IntegrationHealthStatus.Stabilizing)
            {
                continue;
            }

            try
            {
                var patched = await indexerConnectionRepository
                    .PatchAsync(
                        new IndexerConnectionPatch
                        {
                            Id = connectionId,
                            Status = new() { Value = IntegrationHealthStatus.Degraded },
                            LastChecked = new() { Value = failedAt },
                            LastError = new() { Value = error },
                        },
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (patched && connection.Status != IntegrationHealthStatus.Degraded)
                {
                    await eventBus.PublishAsync(
                        IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                            connection.IndexerId,
                            "indexer",
                            connection.Status.ToString(),
                            IntegrationHealthStatus.Degraded.ToString()),
                        cancellationToken).ConfigureAwait(false);
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
                    "The query executor failed to write the search error on indexer connection {ConnectionId}",
                    connectionId);
            }
        }
    }

    private void AttachServedIdentifier(Release release, ReleaseSearchRequest request)
    {
        if (request.IdSearchParameter is null ||
            string.IsNullOrWhiteSpace(request.IdSearchSource) ||
            string.IsNullOrWhiteSpace(request.IdSearchIdentityValue))
        {
            return;
        }

        AddExternalId(release, request.IdSearchSource.Trim(), request.IdSearchIdentityValue.Trim());
    }

    private bool MergeIdentifiersAndSources(Release target, Release source)
    {
        var changed = false;
        foreach (var externalId in source.ExternalIds)
        {
            if (string.IsNullOrWhiteSpace(externalId.Source) || string.IsNullOrWhiteSpace(externalId.Value))
            {
                continue;
            }

            changed |= AddExternalId(target, externalId.Source.Trim(), externalId.Value.Trim());
        }

        foreach (var identity in source.IdentityKeys)
        {
            if (!target.IdentityKeys.Any(existing =>
                string.Equals(existing.Authority, identity.Authority, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Value, identity.Value, StringComparison.OrdinalIgnoreCase)))
            {
                target.IdentityKeys.Add(new ReleaseIdentityKey
                {
                    Authority = identity.Authority,
                    Value = identity.Value,
                    Ordinal = target.IdentityKeys.Count,
                });
                changed = true;
            }
        }

        foreach (var candidate in source.Sources)
        {
            if (!target.Sources.Any(existing => existing.IndexerId == candidate.IndexerId &&
                string.Equals(existing.ExternalId, candidate.ExternalId, StringComparison.Ordinal) &&
                Equals(existing.InfoUrl, candidate.InfoUrl)))
            {
                target.Sources.Add(candidate);
                changed = true;
            }
        }

        return changed;
    }

    private Release CloneRelease(Release source)
    {
        var clone = new Release
        {
            Id = source.Id,
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
            CreatedAt = source.CreatedAt,
            ObservedAt = source.ObservedAt,
            CreatedByUserId = source.CreatedByUserId,
            ModifiedAt = source.ModifiedAt,
            ModifiedByUserId = source.ModifiedByUserId,
        };
        foreach (var externalId in source.ExternalIds)
        {
            clone.ExternalIds.Add(new ExternalId
            {
                Id = externalId.Id,
                MetadataContributionId = externalId.MetadataContributionId,
                ReleaseId = externalId.ReleaseId,
                Ordinal = externalId.Ordinal,
                Value = externalId.Value,
                Source = externalId.Source,
            });
        }

        foreach (var identity in source.IdentityKeys)
        {
            clone.IdentityKeys.Add(new ReleaseIdentityKey
            {
                Id = identity.Id,
                ReleaseId = identity.ReleaseId,
                DownloadId = identity.DownloadId,
                Ordinal = identity.Ordinal,
                Authority = identity.Authority,
                Value = identity.Value,
            });
        }

        foreach (var releaseSource in source.Sources)
        {
            clone.Sources.Add(new ReleaseSource
            {
                Id = releaseSource.Id,
                ReleaseId = releaseSource.ReleaseId,
                Key = releaseSource.Key,
                IndexerId = releaseSource.IndexerId,
                Protocol = releaseSource.Protocol,
                ExternalId = releaseSource.ExternalId,
                Title = releaseSource.Title,
                PublishDate = releaseSource.PublishDate,
                Size = releaseSource.Size,
                InfoUrl = releaseSource.InfoUrl,
                Categories = [.. releaseSource.Categories],
                Languages = [.. releaseSource.Languages],
                Properties = releaseSource.Properties,
                CreatedAt = releaseSource.CreatedAt,
                ObservedAt = releaseSource.ObservedAt,
            });
        }

        foreach (var scope in source.Scope)
        {
            clone.Scope.Add(new ReleaseScope
            {
                Id = scope.Id,
                ReleaseId = scope.ReleaseId,
                ContentHierarchyItemId = scope.ContentHierarchyItemId,
            });
        }

        return clone;
    }

    private bool AddExternalId(Release release, string source, string value)
    {
        foreach (var existing in release.ExternalIds)
        {
            if (existing.Source is { } existingSource &&
                string.Equals(
                    existingSource.NormalizeExternalIdentifierName(),
                    source.NormalizeExternalIdentifierName(),
                    StringComparison.Ordinal) &&
                string.Equals(
                    existing.Value.NormalizeExternalIdentifierValue(),
                    value.NormalizeExternalIdentifierValue(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        release.ExternalIds.Add(new ExternalId
        {
            Source = source,
            Value = value,
            Ordinal = release.ExternalIds.Count,
        });
        return true;
    }

    private string ClassifyProviderFailure(Exception exception) =>
        $"provider-error:{exception.GetType().Name}";

    private sealed record PlannedRoute(
        string NodeId,
        int Order,
        ReleaseSearchPhase Phase,
        ReleaseSearchRequest ResolvedRequest,
        bool Supported)
    {
        public bool IsIdentity => !string.IsNullOrWhiteSpace(Phase.Request.IdSearchParameter);
    }

    private sealed record PlannedObservation(
        Release Release,
        IReadOnlyList<ContentFact> ReleaseFacts,
        IReadOnlyList<string> PlanNodeIds);

    private sealed record PlannedConnection(
        string NodeId,
        int Order,
        IndexerConnection Connection,
        Indexer? Indexer,
        IIndexerIntegration? Integration,
        bool Applicable,
        IReadOnlyList<PlannedRoute> Routes)
    {
        public bool BaseCanExecute => Connection.Enabled &&
            Connection.Status != IntegrationHealthStatus.Unhealthy &&
            Indexer is not null &&
            Integration is not null;

        public bool CanExecute => BaseCanExecute && Applicable && Routes.Any(route => route.Supported);

        public string? SkippedReason => !Connection.Enabled
            ? "disabled"
            : Connection.Status == IntegrationHealthStatus.Unhealthy
                ? "unhealthy"
                : Indexer is null || Integration is null
                    ? "provider-unavailable"
                    : !Applicable
                        ? "content-type-not-applicable"
                        : !Routes.Any(route => route.Supported)
                            ? "query-not-supported"
                            : null;
    }

    private sealed record RouteExecution(
        IReadOnlyList<Release> Releases,
        ReleaseSearchPhase ExecutedRoute,
        bool Attempted,
        string? Failure,
        int RawCount);

    private sealed class ContributionCounts
    {
        internal int Enriched { get; private set; }

        internal int New { get; private set; }

        internal int Unchanged { get; private set; }

        internal void Add(ContributionKind contribution)
        {
            switch (contribution)
            {
                case ContributionKind.New:
                    New++;
                    break;
                case ContributionKind.Enriched:
                    Enriched++;
                    break;
                case ContributionKind.Unchanged:
                    Unchanged++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(contribution));
            }
        }
    }

    private sealed class EvaluationCounts(bool automatic)
    {
        internal int Accepted { get; private set; }

        internal int Evaluated => Accepted + Rejected;

        internal int Rejected { get; private set; }

        internal void Add(ReleaseAssessment assessment)
        {
            if (!automatic || assessment.MatchResult is ReleaseMatchResult.Identity or ReleaseMatchResult.Confident)
            {
                Accepted++;
            }
            else
            {
                Rejected++;
            }
        }
    }

    private sealed record UnionExecution(List<EvaluatedRelease> Results, ContributionCounts Contributions);

    private sealed record ConnectionExecution(
        PlannedConnection Connection,
        IReadOnlyList<EvaluatedRelease> Results,
        IReadOnlyList<ReleaseSearchPhase> ExecutedRoutes,
        bool Attempted,
        string? Failure,
        int RawCount);

    private sealed record CachedRouteResult(
        IReadOnlyList<Release> Releases,
        bool Attempted,
        string? Failure,
        int Coverage)
    {
        internal bool Satisfies(int maximumResults) => Coverage >= maximumResults;
    }

    private sealed class CachedRouteState(CachedRouteResult result)
    {
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private CachedRouteResult _result = result;

        internal CachedRouteResult Result => _result;

        internal async ValueTask<(CachedRouteResult Result, bool Refreshed)> GetOrRefreshAsync(
            int maximumResults,
            Func<CancellationToken, ValueTask<CachedRouteResult>> populate,
            CancellationToken cancellationToken)
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_result.Satisfies(maximumResults))
                {
                    return (_result, false);
                }

                var refreshed = await populate(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _result = refreshed;
                return (refreshed, true);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }

    private sealed class RouteCachePopulationException(bool attempted, string failure) : Exception
    {
        internal bool Attempted { get; } = attempted;

        internal string Failure { get; } = failure;
    }
}
