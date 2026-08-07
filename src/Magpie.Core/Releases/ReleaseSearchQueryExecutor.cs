using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Releases;

internal sealed class ReleaseSearchQueryExecutor(
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IIndexerService indexerService,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IEnumerable<IRuntimeIndexerIntegrationFactory> runtimeIndexerIntegrationFactories,
    TimeProvider timeProvider,
    ILogger<ReleaseSearchQueryExecutor> logger) : IReleaseSearchQueryExecutor
{
    public async Task<IReadOnlyList<ReleaseSearchPhase>> ExecuteAsync(
        IReadOnlyList<ReleaseSearchPhase> phases,
        IReadOnlyList<string> categories,
        ISet<Guid> applicableIndexerIds,
        Action<int, string>? onConnectionStarted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(applicableIndexerIds);

        var indexers = (await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(i => i.Id);
        var connections = (await indexerConnectionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.Enabled)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToArray();

        var executedPhases = new List<ReleaseSearchPhase>();
        var failuresByConnection = new Dictionary<Guid, string>();

        for (var phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = phases[phaseIndex];

            var results = await Task.WhenAll(connections.Select((connection, index) => RunPhaseConnectionAsync(
                connection,
                index,
                phaseIndex,
                indexers,
                phase.Request,
                categories,
                applicableIndexerIds,
                onConnectionStarted,
                cancellationToken))).ConfigureAwait(false);

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
                    failuresByConnection[failure.ConnectionId] = failure.Error;
                }

                releases.AddRange(result.Releases);
            }

            executedPhases.Add(phase with
            {
                Releases = releases,
                ConnectionsAttempted = attempted,
                ConnectionsSucceeded = succeeded,
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

        ShareIdentifiersAndSourcesAmongMatchingReleases(executedPhases.SelectMany(phase => phase.Releases));
        return executedPhases;
    }

    private static ReleaseSearchRequest ResolveAdvertisedSpellings(
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

    private static string ResolveAdvertisedFacetKey(string key, IReadOnlyList<IReadOnlyCollection<string>> searchFacets)
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

    private static bool HaveKnownNonOverlappingIdentityKeys(Release left, Release right)
    {
        var leftKeys = GetNormalizedIdentityKeys(left);
        var rightKeys = GetNormalizedIdentityKeys(right);
        return leftKeys.Count > 0 && rightKeys.Count > 0 && !leftKeys.Overlaps(rightKeys);
    }

    private static HashSet<(string Authority, string Value)> GetNormalizedIdentityKeys(Release release) =>
        release.IdentityKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.Authority) && !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => (key.Authority.Trim().ToLowerInvariant(), key.Value.Trim().ToLowerInvariant()))
            .ToHashSet();

    private static bool RefersToSameRelease(Release left, Release right)
    {
        var leftKeys = GetNormalizedIdentityKeys(left);
        var rightKeys = GetNormalizedIdentityKeys(right);
        if (leftKeys.Overlaps(rightKeys))
        {
            return true;
        }

        if (leftKeys.Count > 0 && rightKeys.Count > 0)
        {
            return false;
        }

        return left.IndexerId is { } indexerId &&
            right.IndexerId == indexerId &&
            !string.IsNullOrWhiteSpace(left.ExternalId) &&
            string.Equals(left.ExternalId, right.ExternalId, StringComparison.Ordinal);
    }

    private void ShareIdentifiersAndSourcesAmongMatchingReleases(IEnumerable<Release> releases)
    {
        var groups = new List<List<Release>>();
        foreach (var release in releases)
        {
            var matchingGroupIndexes = Enumerable.Range(0, groups.Count)
                .Where(index => RefersToSameRelease(release, groups[index][0]))
                .ToArray();
            if (matchingGroupIndexes.Length == 0)
            {
                groups.Add([release]);
                continue;
            }

            var group = groups[matchingGroupIndexes[0]];
            MergeIdentifiersAndSources(group[0], release);
            group.Add(release);
            foreach (var index in matchingGroupIndexes.Skip(1).Reverse())
            {
                MergeIdentifiersAndSources(group[0], groups[index][0]);
                group.AddRange(groups[index]);
                groups.RemoveAt(index);
            }
        }

        foreach (var group in groups.Where(group => group.Count > 1))
        {
            var aggregate = group[0];
            foreach (var release in group.Skip(1))
            {
                MergeIdentifiersAndSources(release, aggregate);
            }
        }
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
        (Guid ConnectionId, string Error)? Failure)> RunPhaseConnectionAsync(
            IndexerConnection connection,
            int index,
            int phaseIndex,
            IReadOnlyDictionary<Guid, Indexer> indexers,
            ReleaseSearchRequest request,
            IReadOnlyList<string> categories,
            ISet<Guid> applicableIndexerIds,
            Action<int, string>? onConnectionStarted,
            CancellationToken cancellationToken)
    {
        var attempted = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (connection.Status == IntegrationHealthStatus.Unhealthy)
            {
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    "unhealthy",
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null);
            }

            if (!indexers.TryGetValue(connection.IndexerId, out var indexer))
            {
                throw new InvalidOperationException($"Indexer {connection.IndexerId} does not exist.");
            }

            var integration = ResolveIndexerIntegration(indexer);
            if (integration is null)
            {
                throw new InvalidOperationException(
                    $"No indexer integration is available for provider type {indexer.ProviderType}.");
            }

            if (!applicableIndexerIds.Contains(indexer.Id))
            {
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    "content-type-not-applicable",
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null);
            }

            var resolved = ResolveAdvertisedSpellings(request, integration.SearchFacets);
            if (!IntegrationSupportsRequest(integration, resolved))
            {
                return (
                    index,
                    connection.Id,
                    connection.Name,
                    connection.Status,
                    false,
                    "request-not-supported",
                    [],
                    stopwatch.ElapsedMilliseconds,
                    null);
            }

            onConnectionStarted?.Invoke(phaseIndex, connection.Name);
            attempted = true;

            var query = resolved with { Categories = categories };
            var found = await integration
                .SearchAsync(query, connection.Settings, cancellationToken)
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

                if (!string.IsNullOrWhiteSpace(release.ExternalId) &&
                    releasesByExternalId.TryGetValue(release.ExternalId, out var existing))
                {
                    if (HaveKnownNonOverlappingIdentityKeys(existing, release))
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

            return (
                index,
                connection.Id,
                connection.Name,
                connection.Status,
                true,
                null,
                releases,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The indexer search failed for connection {ConnectionName} ({ConnectionId}). " +
                "The query executor skipped this connection and will continue with the other indexers",
                connection.Name,
                connection.Id);
            return (
                index,
                connection.Id,
                connection.Name,
                connection.Status,
                attempted,
                null,
                [],
                stopwatch.ElapsedMilliseconds,
                (connection.Id, Truncate(ex.Message, 1024)));
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
                await indexerService
                    .PatchConnectionAsync(
                        connection.IndexerId,
                        connectionId,
                        new IndexerConnectionPatch
                        {
                            Status = new() { Value = IntegrationHealthStatus.Degraded },
                            LastChecked = new() { Value = failedAt },
                            LastError = new() { Value = error },
                        },
                        null,
                        cancellationToken)
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
                    "The query executor failed to write the search error on indexer connection {ConnectionId}",
                    connectionId);
            }
        }
    }

    private IIndexerIntegration? ResolveIndexerIntegration(Indexer indexer)
    {
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

    private void MergeIdentifiersAndSources(Release target, Release source)
    {
        foreach (var externalId in source.ExternalIds)
        {
            if (string.IsNullOrWhiteSpace(externalId.Source) || string.IsNullOrWhiteSpace(externalId.Value))
            {
                continue;
            }

            AddExternalId(target, externalId.Source.Trim(), externalId.Value.Trim());
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
            }
        }

        foreach (var candidate in source.Sources)
        {
            if (!target.Sources.Any(existing => existing.IndexerId == candidate.IndexerId &&
                string.Equals(existing.ExternalId, candidate.ExternalId, StringComparison.Ordinal) &&
                Equals(existing.InfoUrl, candidate.InfoUrl)))
            {
                target.Sources.Add(candidate);
            }
        }
    }

    private void AddExternalId(Release release, string source, string value)
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
                return;
            }
        }

        release.ExternalIds.Add(new ExternalId
        {
            Source = source,
            Value = value,
            Ordinal = release.ExternalIds.Count,
        });
    }

    private string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
    }
}
