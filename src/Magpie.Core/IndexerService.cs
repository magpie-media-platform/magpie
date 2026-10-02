using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class IndexerService(
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IIndexerFeedRepository indexerFeedRepository,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IReleaseRepository releaseRepository,
    IReleaseScopeRepository releaseScopeRepository,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IEnumerable<IRuntimeIndexerIntegrationFactory> runtimeIndexerIntegrationFactories,
    IEnumerable<IIndexerFeedProvider> feedProviders,
    ISettingsService settingsService,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    IReleaseSearchQueryExecutor releaseSearchQueryExecutor,
    TimeProvider timeProvider,
    ILogger<IndexerService> logger) : IIndexerService
{
    private const string DefaultConnectionName = "Default";
    private readonly Dictionary<string, IIndexerFeedProvider> _feedProviders = BuildFeedProviderMap(feedProviders);

    private static int NextConsecutiveFailures(ConfigurationTestResult result, int currentFailures) =>
        result.Success ? 0 : Math.Max(0, currentFailures) + 1;

    private static IntegrationHealthStatus ToHealthStatus(ConfigurationFailureKind failureKind, int failures)
    {
        const int unhealthyFailureThreshold = 3;
        var unhealthy = failures >= unhealthyFailureThreshold ||
            failureKind is ConfigurationFailureKind.Auth
                or ConfigurationFailureKind.Fatal
                or ConfigurationFailureKind.NotFound;
        return unhealthy ? IntegrationHealthStatus.Unhealthy : IntegrationHealthStatus.Degraded;
    }

    private static async Task<ConfigurationTestResult> WithConnectionTimeoutAsync(
        Func<CancellationToken, Task<ConfigurationTestResult>> test,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource?.CancelAfter(timeout.GetValueOrDefault());

        try
        {
            var pending = test(timeoutSource?.Token ?? cancellationToken);
            return timeout is { } limit
                ? await pending.WaitAsync(limit, cancellationToken).ConfigureAwait(false)
                : await pending.ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is TimeoutException && timeout is not null) ||
            (ex is OperationCanceledException &&
                !cancellationToken.IsCancellationRequested &&
                timeoutSource is { IsCancellationRequested: true }))
        {
            return new()
            {
                Success = false,
                Message = "Connection test timed out.",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new()
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
    }

    private static Dictionary<string, IIndexerFeedProvider> BuildFeedProviderMap(
        IEnumerable<IIndexerFeedProvider> providers)
    {
        var map = new Dictionary<string, IIndexerFeedProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.ProviderType))
            {
                map[provider.ProviderType] = provider;
            }
        }

        return map;
    }

    private static ConfigurationTestResult ToTestResult(bool succeeded) => new()
    {
        Success = succeeded,
        Message = succeeded ? "Connection succeeded." : "Connection failed.",
    };

    private static ConfigurationTestResult Failure(string message, ConfigurationFailureKind failureKind) => new()
    {
        Success = false,
        Message = message,
        FailureKind = failureKind,
    };

    private static ConfigurationTestResult Aggregate(IReadOnlyList<ConfigurationTestResult> results)
    {
        if (results.Count == 1)
        {
            return results[0];
        }

        var failures = results.Where(result => !result.Success).ToArray();
        if (failures.Length == 0)
        {
            return new() { Success = true, Message = "All connections succeeded." };
        }

        return new()
        {
            Success = false,
            Message = string.Join("; ", failures.Select(result => result.Message)),
            FailureKind = failures[0].FailureKind,
        };
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

    public async Task<IReadOnlyList<Indexer>> ListAsync(CancellationToken cancellationToken = default)
        => [.. await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)];

    public async Task<Indexer?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

    public async Task<Indexer> CreateAsync(
        IndexerCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            ProviderType = create.ProviderType,
            Name = create.Name,
            Description = create.Description,
            Availability = create.Availability,
            Categories = create.Categories ?? [],
            Languages = create.Languages ?? [],
        };

        var (created, connection) = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var persisted = await indexerRepository.CreateAsync(indexer, actingUserId, ct).ConfigureAwait(false);

                var defaultConnection = new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = persisted.Id,
                    Name = DefaultConnectionName,
                    Enabled = true,
                    Status = IntegrationHealthStatus.Unknown,
                    Settings = create.Settings is null
                        ? []
                        : new Dictionary<string, string>(create.Settings),
                };

                var persistedConnection = await indexerConnectionRepository
                    .CreateAsync(defaultConnection, actingUserId, ct)
                    .ConfigureAwait(false);

                return (persisted, persistedConnection);
            },
            cancellationToken).ConfigureAwait(false);

        await ProbeAndPublishAsync(created, connection, null, cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<bool> PatchAsync(
        Guid id,
        IndexerPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return false;
        }

        if (patch.Name is not null && string.IsNullOrWhiteSpace(patch.Name.Value))
        {
            throw new ArgumentException("Name is required.");
        }

        if (patch.ProviderType is not null &&
            !string.Equals(patch.ProviderType.Value, indexer.ProviderType, StringComparison.Ordinal))
        {
            var candidate = indexer with { ProviderType = patch.ProviderType.Value };
            if (string.IsNullOrWhiteSpace(candidate.ProviderType))
            {
                throw new ArgumentException("Provider type is required.");
            }

            var resolved = ResolveIntegration(candidate);
            if (resolved is null ||
                !string.Equals(resolved.ProviderType, candidate.ProviderType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"No indexer integration is available for provider type {candidate.ProviderType}.");
            }
        }

        return await indexerRepository.PatchAsync(patch with { Id = id }, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        var deleted = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var connections = await indexerConnectionRepository.QueryAllAsync(
                    new IndexerConnectionQuery { IndexerId = id },
                    cancellationToken: ct).ConfigureAwait(false);

                foreach (var connection in connections)
                {
                    await indexerConnectionRepository.DeleteAsync(connection.Id, actingUserId, ct)
                        .ConfigureAwait(false);
                }

                return await indexerRepository.DeleteAsync(id, actingUserId, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return deleted;
    }

    public async Task<IReadOnlyList<SettingSchemaDefinition>> GetSettingsSchemaAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        var integration = ResolveIntegration(indexer);

        return integration?.SettingsSchema ?? [];
    }

    public async Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        IIndexerIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(providerType))
        {
            integration = ResolveIntegration(
                new Indexer { ProviderType = providerType, Name = string.Empty });
        }

        Indexer? indexer = null;
        Dictionary<string, string>? storedSettings = null;
        if (id is { } indexerId)
        {
            indexer = await indexerRepository.GetByIdAsync(indexerId, cancellationToken).ConfigureAwait(false);
            integration ??= ResolveIntegration(indexer);

            var connections = await indexerConnectionRepository
                .QueryAsync(
                    new IndexerConnectionQuery { IndexerId = indexerId },
                    new() { PageNumber = 1, PageSize = 1 },
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            storedSettings = connections.Items.FirstOrDefault()?.Settings;
        }

        if (integration is null)
        {
            return Failure("No integration is registered for the requested indexer.", ConfigurationFailureKind.Fatal);
        }

        var merged = new Dictionary<string, string>();
        if (storedSettings is not null)
        {
            foreach (var (key, value) in storedSettings)
            {
                merged[key] = value;
            }
        }

        foreach (var (key, value) in settings)
        {
            merged[key] = value;
        }

        var validation = await settingsService
            .ValidateAsync(integration.SettingsSchema, merged, integration, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Failure(
                string.Join("; ", validation.Issues.Select(issue => issue.Message)),
                ConfigurationFailureKind.Fatal);
        }

        indexer ??= new Indexer
        {
            Id = Guid.NewGuid(),
            ProviderType = providerType ?? integration.ProviderType,
            Name = string.Empty,
        };

        var transientConnection = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexer.Id,
            Name = string.Empty,
            Settings = new(merged),
        };

        return await TestIndexerConnectionCoreAsync(indexer, transientConnection, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestAsync(
        Guid id,
        Guid? connectionId,
        CancellationToken cancellationToken = default)
    {
        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return Failure("The indexer was not found.", ConfigurationFailureKind.NotFound);
        }

        var connections = await indexerConnectionRepository.QueryAllAsync(
            new IndexerConnectionQuery { IndexerId = id },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var targets = connectionId is { } cid
            ? connections.Where(connection => connection.Id == cid).ToList()
            : connections;

        if (connectionId is not null && targets.Count == 0)
        {
            return Failure("The connection was not found for this indexer.", ConfigurationFailureKind.NotFound);
        }

        if (targets.Count == 0)
        {
            return Failure("No connections are configured for this indexer.", ConfigurationFailureKind.Fatal);
        }

        var results = new List<ConfigurationTestResult>();
        foreach (var connection in targets)
        {
            results.Add(await ProbeAndPublishAsync(indexer, connection, null, cancellationToken)
                .ConfigureAwait(false));
        }

        return Aggregate(results);
    }

    public async Task<IReadOnlyList<IndexerConnection>> ListConnectionsAsync(
        Guid indexerId,
        CancellationToken cancellationToken = default)
    {
        return await indexerConnectionRepository.QueryAllAsync(
            new IndexerConnectionQuery { IndexerId = indexerId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexerSearchCapabilities>> ListCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var connectedIndexerIds = (await indexerConnectionRepository.ListAllAsync(cancellationToken)
            .ConfigureAwait(false))
            .Select(connection => connection.IndexerId)
            .ToHashSet();
        var indexers = (await indexerRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(indexer => connectedIndexerIds.Contains(indexer.Id));
        var capabilities = new List<IndexerSearchCapabilities>();
        foreach (var indexer in indexers)
        {
            try
            {
                capabilities.Add(DescribeCapabilities(indexer));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not describe search capabilities for indexer {IndexerId}", indexer.Id);
                capabilities.Add(new IndexerSearchCapabilities { IndexerId = indexer.Id });
            }
        }

        return capabilities;
    }

    public async Task<IndexerSearchResponse?> SearchAsync(
        IndexerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var selectedIds = request.ConnectionIds.ToHashSet();
        var connections = (await indexerConnectionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(connection => selectedIds.Contains(connection.Id))
            .OrderBy(connection => connection.Name, StringComparer.Ordinal)
            .ThenBy(connection => connection.Id)
            .ToArray();
        if (connections.Length == 0)
        {
            return new();
        }

        var indexerIds = connections
            .Select(connection => connection.IndexerId)
            .Distinct()
            .ToHashSet();
        var phases = await releaseSearchQueryExecutor.ExecuteAsync(
            [new ReleaseSearchPhase { Request = request.Query with { IsInteractive = true }, ReleaseFacts = [] }],
            [],
            indexerIds,
            cancellationToken: cancellationToken,
            selectedConnectionIds: selectedIds).ConfigureAwait(false);

        var phase = phases[0];
        var outcomes = phase.Connections.ToDictionary(outcome => outcome.ConnectionId);
        var results = new List<IndexerSearchConnectionResult>(connections.Length);
        foreach (var connection in connections)
        {
            outcomes.TryGetValue(connection.Id, out var outcome);
            results.Add(new IndexerSearchConnectionResult
            {
                ConnectionId = connection.Id,
                IndexerId = connection.IndexerId,
                ConnectionName = connection.Name,
                Enabled = connection.Enabled,
                Status = connection.Status,
                Attempted = outcome?.Attempted ?? false,
                SkippedReason = connection.Enabled ? outcome?.SkippedReason : "disabled",
                Warning = outcome?.Error,
            });
        }

        return new()
        {
            Candidates =
            [
                .. phase.Candidates
                    .OrderByDescending(candidate => candidate.Release.PublishDate)
                    .ThenBy(candidate => candidate.Release.Title, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Release.ExternalId, StringComparer.Ordinal)
                    .Select(candidate => new IndexerSearchCandidate
                    {
                        Key = candidate.Key,
                        Release = candidate.Release,
                        ConnectionIds = candidate.ConnectionIds,
                    }),
            ],
            Connections = results,
            ConnectionsAttempted = results.Count(result => result.Attempted),
            ConnectionsSkipped = results.Count(result => result.SkippedReason is not null),
        };
    }

    public async Task<Guid?> MatchAsync(
        Guid contentHierarchyItemId,
        Release release,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(release.Title);

        var item = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        return await transactionManager.ExecuteWithTransactionAsync(
            async transactionCancellationToken =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                release.Id = Guid.NewGuid();
                release.CreatedAt = now;
                release.CreatedByUserId = actingUserId;
                release.ModifiedAt = null;
                release.ModifiedByUserId = null;
                await StoreReleaseAsync(release, actingUserId, transactionCancellationToken).ConfigureAwait(false);
                await AddScopeAsync(
                    release.Id,
                    contentHierarchyItemId,
                    actingUserId,
                    transactionCancellationToken).ConfigureAwait(false);
                return (Guid?)release.Id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexerConnection> CreateConnectionAsync(
        Guid indexerId,
        IndexerConnectionCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var indexer = await indexerRepository.GetByIdAsync(indexerId, cancellationToken).ConfigureAwait(false);

        var connection = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerId,
            Name = string.IsNullOrWhiteSpace(create.Name) ? indexer?.Name ?? DefaultConnectionName : create.Name,
            Enabled = create.Enabled,
            Status = IntegrationHealthStatus.Unknown,
            Settings = create.Settings is null
                ? []
                : new Dictionary<string, string>(create.Settings),
        };

        return await indexerConnectionRepository.CreateAsync(connection, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        IndexerConnectionPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var connection = await indexerConnectionRepository.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null || connection.IndexerId != indexerId)
        {
            return false;
        }

        var patched = await indexerConnectionRepository
            .PatchAsync(patch with { Id = connectionId }, actingUserId, cancellationToken)
            .ConfigureAwait(false);

        if (patched && patch.Status?.Value is { } toStatus && toStatus != connection.Status)
        {
            await eventBus.PublishAsync(
                IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                    indexerId,
                    "indexer",
                    connection.Status.ToString(),
                    toStatus.ToString()),
                cancellationToken).ConfigureAwait(false);
        }

        return patched;
    }

    public async Task<bool> DeleteConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var connection = await indexerConnectionRepository.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null || connection.IndexerId != indexerId)
        {
            return false;
        }

        return await indexerConnectionRepository.DeleteAsync(connectionId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<ConfigurationTestResult> TestConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        return TestSavedConnectionAsync(indexerId, connectionId, null, cancellationToken);
    }

    public Task<ConfigurationTestResult> TestConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return TestSavedConnectionAsync(indexerId, connectionId, timeout, cancellationToken);
    }

    private async Task<ConfigurationTestResult> TestSavedConnectionAsync(
        Guid indexerId,
        Guid connectionId,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var indexer = await indexerRepository.GetByIdAsync(indexerId, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return Failure("The indexer was not found.", ConfigurationFailureKind.NotFound);
        }

        var connection = await indexerConnectionRepository.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (connection is null || connection.IndexerId != indexerId)
        {
            return Failure("The connection was not found for this indexer.", ConfigurationFailureKind.NotFound);
        }

        return await ProbeAndPublishAsync(indexer, connection, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexerFeed>> ListFeedsAsync(CancellationToken cancellationToken = default)
        => [.. await indexerFeedRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)];

    public Task<IReadOnlyList<SettingSchemaDefinition>> GetFeedSettingsSchemaAsync(
        string providerType,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ResolveFeedProvider(providerType)?.SettingsSchema ?? []);

    public async Task<IndexerFeed> CreateFeedAsync(
        IndexerFeed feed,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await indexerFeedRepository.CreateAsync(feed, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchFeedAsync(
        Guid feedId,
        IndexerFeedPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await indexerFeedRepository.PatchAsync(patch with { Id = feedId }, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteFeedAsync(
        Guid feedId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var indexers = await indexerRepository.QueryAllAsync(
                    new IndexerQuery { FeedId = feedId },
                    cancellationToken: ct).ConfigureAwait(false);

                foreach (var indexer in indexers)
                {
                    await DeleteAsync(indexer.Id, actingUserId, ct).ConfigureAwait(false);
                }

                return await indexerFeedRepository.DeleteAsync(feedId, actingUserId, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfigurationTestResult> TestFeedSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveFeedProvider(providerType);

        IndexerFeed? feed = null;
        if (id is { } feedId)
        {
            feed = await indexerFeedRepository.GetByIdAsync(feedId, cancellationToken).ConfigureAwait(false);
            provider ??= feed is null ? null : ResolveFeedProvider(feed.ProviderType);
        }

        if (provider is null)
        {
            return Failure("No feed provider is registered for the requested feed.", ConfigurationFailureKind.Fatal);
        }

        var merged = new Dictionary<string, string>();
        if (feed?.Settings is not null)
        {
            foreach (var (key, value) in feed.Settings)
            {
                merged[key] = value;
            }
        }

        foreach (var (key, value) in settings)
        {
            merged[key] = value;
        }

        var transientFeed = feed is not null
            ? feed with { Settings = new(merged) }
            : new IndexerFeed
            {
                Id = Guid.NewGuid(),
                ProviderType = providerType ?? provider.ProviderType,
                Name = string.Empty,
                Settings = new(merged),
            };

        var succeeded = await provider.TestConnectionAsync(transientFeed, cancellationToken).ConfigureAwait(false);

        return ToTestResult(succeeded);
    }

    public async Task<ConfigurationTestResult> TestFeedAsync(Guid feedId, CancellationToken cancellationToken = default)
    {
        var feed = await indexerFeedRepository.GetByIdAsync(feedId, cancellationToken).ConfigureAwait(false);
        if (feed is null)
        {
            return Failure("The feed was not found.", ConfigurationFailureKind.NotFound);
        }

        var provider = ResolveFeedProvider(feed.ProviderType);
        if (provider is null)
        {
            return Failure("No feed provider is registered for this feed.", ConfigurationFailureKind.Fatal);
        }

        var succeeded = await provider.TestConnectionAsync(feed, cancellationToken).ConfigureAwait(false);

        return ToTestResult(succeeded);
    }

    public async Task SyncFeedIndexersAsync(
        IndexerFeed feed,
        IReadOnlyList<Indexer> discovered,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (discovered.Count == 0)
        {
            return;
        }

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var feeds = await indexerFeedRepository.ListAllAsync(ct).ConfigureAwait(false);

                var adoptsOrphans = feeds.All(other => other.Id == feed.Id);

                var stored = await indexerRepository.ListAllAsync(ct).ConfigureAwait(false);
                var owned = stored
                    .Where(indexer => indexer.FeedId == feed.Id)
                    .Select(indexer => indexer.Id)
                    .ToHashSet();
                var candidates = stored
                    .Where(indexer => indexer.FeedId == feed.Id ||
                        (adoptsOrphans && indexer.FeedId is null && indexer.DefinitionType is not null))
                    .ToArray();
                var existing = candidates
                    .GroupBy(indexer => indexer.ProviderType, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderBy(indexer => indexer.FeedId is not null)
                            .ThenBy(indexer => indexer.CreatedAt)
                            .ThenBy(indexer => indexer.Id)
                            .First(),
                        StringComparer.OrdinalIgnoreCase);
                var retainedIds = new HashSet<Guid>();

                foreach (var incoming in discovered)
                {
                    ct.ThrowIfCancellationRequested();

                    incoming.FeedId = feed.Id;
                    incoming.ExtensionId = feed.ExtensionId;

                    if (existing.TryGetValue(incoming.ProviderType, out var current))
                    {
                        current.ProviderType = incoming.ProviderType;
                        current.Name = incoming.Name;
                        current.Description = incoming.Description;
                        current.Availability = incoming.Availability;
                        current.Categories = incoming.Categories;
                        current.Languages = incoming.Languages;
                        current.SupportedContentTypes = incoming.SupportedContentTypes;
                        current.DefinitionType = incoming.DefinitionType;
                        current.SchemaData = incoming.SchemaData;
                        current.ExtensionId = incoming.ExtensionId;
                        current.FeedId = feed.Id;

                        await indexerRepository.UpdateAsync(current, userId, ct).ConfigureAwait(false);
                        retainedIds.Add(current.Id);
                        existing[incoming.ProviderType] = current;
                    }
                    else
                    {
                        incoming.Id = incoming.Id == Guid.Empty ? Guid.NewGuid() : incoming.Id;
                        await indexerRepository.CreateAsync(incoming, userId, ct).ConfigureAwait(false);
                        retainedIds.Add(incoming.Id);
                        existing[incoming.ProviderType] = incoming;
                    }
                }

                var obsolete = candidates
                    .Where(indexer => owned.Contains(indexer.Id) && !retainedIds.Contains(indexer.Id))
                    .ToArray();

                foreach (var indexer in obsolete)
                {
                    ct.ThrowIfCancellationRequested();

                    logger.LogInformation(
                        "The indexer service removed indexer {IndexerName} ({ProviderType}) from feed " +
                        "{FeedName} because a non-empty sync did not retain it",
                        indexer.Name,
                        indexer.ProviderType,
                        feed.Name);

                    await DeleteAsync(indexer.Id, userId, ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConfigurationTestResult> ProbeAndPublishAsync(
        Indexer indexer,
        IndexerConnection connection,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var fromStatus = connection.Status;

        var result = await ProbeIndexerConnectionAsync(indexer, connection, timeout, cancellationToken)
            .ConfigureAwait(false);

        var reloaded = await indexerConnectionRepository.GetByIdAsync(connection.Id, cancellationToken)
            .ConfigureAwait(false);
        var toStatus = reloaded?.Status ?? fromStatus;

        if (toStatus != fromStatus)
        {
            ReleaseTelemetry.HealthTransitions.Add(
                1,
                new KeyValuePair<string, object?>("indexer_id", indexer.Id.ToString()),
                new KeyValuePair<string, object?>("to_status", toStatus.ToString()));

            var healthChanged = IntegrationEventBuilder.BuildIntegrationHealthChangedEvent(
                indexer.Id,
                "indexer",
                fromStatus.ToString(),
                toStatus.ToString());

            await eventBus.PublishAsync(healthChanged, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<ConfigurationTestResult> ProbeIndexerConnectionAsync(
        Indexer indexer,
        IndexerConnection connection,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var result = await TestIndexerConnectionCoreAsync(indexer, connection, timeout, cancellationToken)
            .ConfigureAwait(false);

        var failures = NextConsecutiveFailures(result, connection.ConsecutiveFailures);
        var status = result.Success
            ? IntegrationHealthStatus.Healthy
            : ToHealthStatus(result.FailureKind, failures);

        await indexerConnectionRepository.PatchAsync(
            new IndexerConnectionPatch
            {
                Id = connection.Id,
                Status = new() { Value = status },
                LastChecked = new() { Value = timeProvider.GetUtcNow().UtcDateTime },
                LastError = new() { Value = result.Success ? null : result.Message },
                ConsecutiveFailures = new() { Value = failures },
            },
            null,
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task StoreReleaseAsync(Release release, Guid? actingUserId, CancellationToken cancellationToken)
    {
        if (await releaseRepository.UpdateAsync(release, actingUserId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var persisted = await releaseRepository.CreateAsync(release, actingUserId, cancellationToken)
                .ConfigureAwait(false);
            release.Id = persisted.Id;
        }
        catch (DbUpdateException)
        {
            if (!await releaseRepository.UpdateAsync(release, actingUserId, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    private async Task AddScopeAsync(
        Guid releaseId,
        Guid contentHierarchyItemId,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        var existing = (await releaseScopeRepository.QueryAsync(
            new ReleaseScopeQuery { ReleaseId = releaseId, ContentHierarchyItemId = contentHierarchyItemId },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.FirstOrDefault();
        if (existing is not null)
        {
            if (existing.Status == ReleaseScopeStatus.Unmatched)
            {
                await releaseScopeRepository
                    .UpdateAsync(existing with { Status = ReleaseScopeStatus.Matched }, actingUserId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        await releaseScopeRepository.CreateAsync(
            new ReleaseScope
            {
                Id = Guid.NewGuid(),
                ReleaseId = releaseId,
                ContentHierarchyItemId = contentHierarchyItemId,
            },
            actingUserId,
            cancellationToken).ConfigureAwait(false);
    }

    private IndexerSearchCapabilities DescribeCapabilities(Indexer indexer)
    {
        var integration = ResolveIntegration(indexer);
        if (integration is null)
        {
            return new() { IndexerId = indexer.Id };
        }

        return new()
        {
            IndexerId = indexer.Id,
            SearchFacets = [.. integration.SearchFacets.Select(set => (IReadOnlyList<string>)[.. set])],
            SupportsKeywordSearch = integration.SupportsKeywordSearch,
        };
    }

    private async Task<ConfigurationTestResult> TestIndexerConnectionCoreAsync(
        Indexer indexer,
        IndexerConnection connection,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var integration = ResolveIntegration(indexer);
        if (integration is null)
        {
            return new()
            {
                Success = false,
                Message = $"No indexer integration is available for provider type {indexer.ProviderType}.",
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }

        var test = (CancellationToken ct) => integration.TestConnectionAsync(connection.Settings, ct);
        return await WithConnectionTimeoutAsync(test, timeout, cancellationToken).ConfigureAwait(false);
    }

    private IIndexerFeedProvider? ResolveFeedProvider(string? providerType)
        => !string.IsNullOrWhiteSpace(providerType) && _feedProviders.TryGetValue(providerType, out var provider)
            ? provider
            : null;
}
