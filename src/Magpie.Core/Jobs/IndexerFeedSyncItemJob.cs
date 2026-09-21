using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class IndexerFeedSyncItemJob(
    IEnumerable<IIndexerFeedProvider> indexerFeedProviders,
    IEnumerable<IIndexerIntegration> indexerIntegrations,
    IIndexerFeedRepository indexerFeedRepository,
    IIndexerService indexerService,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<IndexerFeedSyncItemJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.IndexerFeedSyncItem,
        DisplayName = "Indexer feed sync item",
        Description = "Refreshes indexer definitions from one enabled feed.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.FeedId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.FeedId,
                "The indexer feed id to sync.",
                DataType.String,
                true,
                null)
        ],
    };

    private Dictionary<string, IIndexerIntegration>? _integrationsByType;

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        if (!jobContext.Parameters.TryGetValue(MagpieJobs.Parameters.FeedId, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("An indexer feed ID is required.");
        }

        if (!Guid.TryParse(raw, out var feedId))
        {
            throw new ArgumentException("The indexer feed ID has an invalid format.");
        }

        await SyncFeedAsync(feedId, jobContext.ManualTriggerUserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncFeedAsync(Guid feedId, Guid? userId, CancellationToken cancellationToken)
    {
        progressReporter.Report(0, "The sync will load the indexer feed.");

        var feed = await indexerFeedRepository.GetByIdAsync(feedId, cancellationToken).ConfigureAwait(false);
        if (feed is null)
        {
            throw new InvalidOperationException($"Indexer feed {feedId} does not exist.");
        }

        if (!feed.Enabled)
        {
            logger.LogDebug("Indexer feed {FeedName} is disabled, so the job skipped the sync", feed.Name);
            progressReporter.Report(1, "The sync stopped because the indexer feed is disabled.");
            return;
        }

        var providerMap = BuildProviderMap();
        if (!providerMap.TryGetValue(feed.ProviderType, out var provider))
        {
            logger.LogWarning(
                "No indexer feed provider exists for provider type {ProviderType} (feed {FeedName})",
                feed.ProviderType,
                feed.Name);
            progressReporter.Report(1, "The sync stopped because the indexer feed has no provider.");
            return;
        }

        progressReporter.Report(0, $"The sync will process indexer feed {feed.Name}.");

        var discovered = await provider.SyncAsync(feed, cancellationToken).ConfigureAwait(false);
        var indexers = discovered?.ToArray() ?? [];

        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await SyncIndexersAsync(feed, indexers, userId, ct).ConfigureAwait(false);
                await StampLastSyncAsync(feed, userId, ct).ConfigureAwait(false);

                var indexerCountLabel = indexers.Length == 1 ? "1 indexer" : $"{indexers.Length} indexers";
                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Sync",
                        EntityId = feed.Id.ToString(),
                        EntityType = nameof(IndexerFeed),
                        Detail = $"Synced {indexerCountLabel} from feed {feed.Name}.",
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                        UserId = userId
                    },
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The indexer feed sync completed for {FeedName} ({ProviderType}) and found {Count} indexer(s)",
            feed.Name,
            feed.ProviderType,
            indexers.Length);

        progressReporter.Report(1, "The indexer-feed sync completed.");
    }

    private async Task SyncIndexersAsync(
        IndexerFeed feed,
        IReadOnlyList<Indexer> discovered,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (discovered.Count == 0)
        {
            return;
        }

        var integrationMap = ResolveIntegrationMap();
        foreach (var incoming in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (integrationMap.TryGetValue(incoming.ProviderType, out var integration))
            {
                incoming.SupportedContentTypes = [.. integration.SupportedContentTypes];
            }
        }

        await indexerService.SyncFeedIndexersAsync(feed, discovered, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StampLastSyncAsync(IndexerFeed feed, Guid? userId, CancellationToken cancellationToken)
    {
        var updated = feed with { LastSync = timeProvider.GetUtcNow().UtcDateTime };
        await indexerFeedRepository.UpdateAsync(updated, userId, cancellationToken).ConfigureAwait(false);
    }

    private Dictionary<string, IIndexerIntegration> ResolveIntegrationMap()
    {
        _integrationsByType ??= BuildIntegrationMap();
        return _integrationsByType;
    }

    private Dictionary<string, IIndexerIntegration> BuildIntegrationMap()
    {
        var map = new Dictionary<string, IIndexerIntegration>(StringComparer.Ordinal);
        foreach (var group in indexerIntegrations
            .Where(integration => !string.IsNullOrWhiteSpace(integration.ProviderType))
            .GroupBy(integration => integration.ProviderType, StringComparer.Ordinal))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple indexer integrations declared provider type {ProviderType}, so the job will use " +
                    "{Winner} and ignore {Displaced}",
                    group.Key,
                    winner.GetType().FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.GetType().FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }

    private Dictionary<string, IIndexerFeedProvider> BuildProviderMap()
    {
        var map = new Dictionary<string, IIndexerFeedProvider>(StringComparer.Ordinal);
        foreach (var group in indexerFeedProviders
            .Where(provider => !string.IsNullOrWhiteSpace(provider.ProviderType))
            .GroupBy(provider => provider.ProviderType, StringComparer.Ordinal))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple indexer feed providers declared provider type {ProviderType}, so the job will use " +
                    "{Winner} and ignore {Displaced}",
                    group.Key,
                    winner.GetType().FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.GetType().FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }
}
