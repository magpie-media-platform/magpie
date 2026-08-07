using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class IndexerService(
    IIndexerRepository indexerRepository,
    IIndexerConnectionRepository indexerConnectionRepository,
    IIndexerFeedRepository indexerFeedRepository,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IEnumerable<IRuntimeIndexerIntegrationFactory> runtimeIndexerIntegrationFactories,
    IEnumerable<IIndexerFeedProvider> feedProviders,
    ISettingsService settingsService,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<IndexerService> logger) : IIndexerService
{
    private const string DefaultConnectionName = "Default";
    private const int UnhealthyFailureThreshold = 3;

    private readonly Dictionary<string, IIndexerFeedProvider> _feedProviders = BuildFeedProviderMap(feedProviders);

    private static int ResolveConsecutiveFailures(int currentFailures, ConfigurationTestResult result)
        => result.Success ? 0 : Math.Max(0, currentFailures) + 1;

    private static IntegrationHealthStatus ResolveFailureStatus(ConfigurationFailureKind failureKind, int failures)
    {
        var unhealthy = failures >= UnhealthyFailureThreshold ||
            failureKind is ConfigurationFailureKind.Auth
                or ConfigurationFailureKind.Fatal
                or ConfigurationFailureKind.NotFound;
        return unhealthy ? IntegrationHealthStatus.Unhealthy : IntegrationHealthStatus.Degraded;
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
        ArgumentNullException.ThrowIfNull(patch);

        var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (indexer is null)
        {
            return false;
        }

        if (patch.Name is not null && string.IsNullOrWhiteSpace(patch.Name))
        {
            throw new ArgumentException("Name is required.");
        }

        if (patch.ProviderType is not null &&
            !string.Equals(patch.ProviderType, indexer.ProviderType, StringComparison.Ordinal))
        {
            var candidate = indexer with { ProviderType = patch.ProviderType };
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

            indexer.ProviderType = patch.ProviderType;
        }

        if (patch.Name is not null)
        {
            indexer.Name = patch.Name;
        }

        if (patch.Description is not null)
        {
            indexer.Description = patch.Description;
        }

        if (patch.Availability is { } availability)
        {
            indexer.Availability = availability;
        }

        if (patch.Categories is not null)
        {
            indexer.Categories = patch.Categories;
        }

        if (patch.Languages is not null)
        {
            indexer.Languages = patch.Languages;
        }

        return await indexerRepository.UpdateAsync(indexer, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        var deleted = await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                const int pageSize = 500;
                var connections = new List<IndexerConnection>();
                var pageNumber = 1;
                while (true)
                {
                    var page = await indexerConnectionRepository.QueryAsync(
                        new IndexerConnectionQuery { IndexerId = id },
                        new() { PageNumber = pageNumber, PageSize = pageSize },
                        null,
                        ct).ConfigureAwait(false);
                    var pageItems = page.Items.ToList();
                    connections.AddRange(pageItems);
                    if (pageItems.Count < pageSize)
                    {
                        break;
                    }

                    pageNumber++;
                }

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
        var integration = indexer is null ? null : ResolveIntegration(indexer);

        return integration?.SettingsSchema ?? [];
    }

    public async Task<ConfigurationTestResult> TestSettingsAsync(
        string? providerType,
        Guid? id,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IIndexerIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(providerType))
        {
            integration = ResolveIntegration(new Indexer { ProviderType = providerType, Name = string.Empty });
        }

        Indexer? indexer = null;
        Dictionary<string, string>? storedSettings = null;
        if (id is { } indexerId)
        {
            indexer = await indexerRepository.GetByIdAsync(indexerId, cancellationToken).ConfigureAwait(false);
            integration ??= indexer is null ? null : ResolveIntegration(indexer);

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
            Settings = new Dictionary<string, string>(merged),
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

        const int pageSize = 500;
        var connections = new List<IndexerConnection>();
        var pageNumber = 1;
        while (true)
        {
            var page = await indexerConnectionRepository.QueryAsync(
                new IndexerConnectionQuery { IndexerId = id },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            connections.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

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
        const int pageSize = 500;
        var connections = new List<IndexerConnection>();
        var pageNumber = 1;
        while (true)
        {
            var page = await indexerConnectionRepository.QueryAsync(
                new IndexerConnectionQuery { IndexerId = indexerId },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            connections.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        return connections;
    }

    public async Task<IndexerConnection> CreateConnectionAsync(
        Guid indexerId,
        IndexerConnectionCreate create,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);

        var indexer = await indexerRepository.GetByIdAsync(indexerId, cancellationToken).ConfigureAwait(false);

        var connection = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = indexerId,
            Name = string.IsNullOrWhiteSpace(create.Name) ? indexer?.Name ?? DefaultConnectionName : create.Name!,
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
        ArgumentNullException.ThrowIfNull(patch);

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
        ArgumentNullException.ThrowIfNull(feed);

        return await indexerFeedRepository.CreateAsync(feed, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchFeedAsync(
        Guid feedId,
        IndexerFeedPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var feed = await indexerFeedRepository.GetByIdAsync(feedId, cancellationToken).ConfigureAwait(false);
        if (feed is null)
        {
            return false;
        }

        if (patch.ProviderType is not null)
        {
            feed.ProviderType = patch.ProviderType;
        }

        if (patch.Name is not null)
        {
            feed.Name = patch.Name;
        }

        if (patch.Description is not null)
        {
            feed.Description = patch.Description;
        }

        if (patch.Settings is not null)
        {
            feed.Settings = patch.Settings;
        }

        if (patch.Enabled is { } enabled)
        {
            feed.Enabled = enabled;
        }

        return await indexerFeedRepository.UpdateAsync(feed, actingUserId, cancellationToken)
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
                const int pageSize = 500;
                var indexers = new List<Indexer>();
                var pageNumber = 1;
                while (true)
                {
                    var page = await indexerRepository.QueryAsync(
                        new IndexerQuery { FeedId = feedId },
                        new() { PageNumber = pageNumber, PageSize = pageSize },
                        null,
                        ct).ConfigureAwait(false);
                    var pageItems = page.Items.ToList();
                    indexers.AddRange(pageItems);
                    if (pageItems.Count < pageSize)
                    {
                        break;
                    }

                    pageNumber++;
                }

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
        ArgumentNullException.ThrowIfNull(settings);

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
            ? feed with { Settings = new Dictionary<string, string>(merged) }
            : new IndexerFeed
            {
                Id = Guid.NewGuid(),
                ProviderType = providerType ?? provider.ProviderType,
                Name = string.Empty,
                Settings = new Dictionary<string, string>(merged),
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
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(discovered);

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var feeds = await indexerFeedRepository.ListAllAsync(ct).ConfigureAwait(false);

                var adoptsOrphans = feeds.All(other => other.Id == feed.Id);

                var stored = await indexerRepository.ListAllAsync(ct).ConfigureAwait(false);
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
                    .Where(indexer => !retainedIds.Contains(indexer.Id))
                    .ToArray();

                foreach (var indexer in obsolete)
                {
                    ct.ThrowIfCancellationRequested();

                    logger.LogInformation(
                        "The indexer service removed indexer {IndexerName} ({ProviderType}) from feed {FeedName}. " +
                        "A successful non-empty sync did not retain this definition",
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

        var failures = ResolveConsecutiveFailures(connection.ConsecutiveFailures, result);
        var status = result.Success
            ? IntegrationHealthStatus.Healthy
            : ResolveFailureStatus(result.FailureKind, failures);

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

    private async Task<ConfigurationTestResult> TestIndexerConnectionCoreAsync(
        Indexer indexer,
        IndexerConnection connection,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var integration = ResolveIntegration(indexer);
        if (integration is null)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = $"No indexer integration is available for provider type {indexer.ProviderType}.",
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }

        using var timeoutSource = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource?.CancelAfter(timeout.GetValueOrDefault());
        var testCancellationToken = timeoutSource?.Token ?? cancellationToken;

        try
        {
            var test = integration.TestConnectionAsync(connection.Settings, testCancellationToken);
            return timeout is { } limit
                ? await test.WaitAsync(limit, cancellationToken).ConfigureAwait(false)
                : await test.ConfigureAwait(false);
        }
        catch (TimeoutException) when (timeout is not null)
        {
            return new ConfigurationTestResult
            {
                Success = false,
                Message = "Connection test timed out.",
                FailureKind = ConfigurationFailureKind.Transient
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
            timeoutSource is { IsCancellationRequested: true })
        {
            return new ConfigurationTestResult
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
            return new ConfigurationTestResult
            {
                Success = false,
                Message = ex.Message,
                FailureKind = ConfigurationFailureKind.Fatal
            };
        }
    }

    private IIndexerIntegration? ResolveIntegration(Indexer indexer)
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

    private IIndexerFeedProvider? ResolveFeedProvider(string? providerType)
        => !string.IsNullOrWhiteSpace(providerType) && _feedProviders.TryGetValue(providerType, out var provider)
            ? provider
            : null;
}
