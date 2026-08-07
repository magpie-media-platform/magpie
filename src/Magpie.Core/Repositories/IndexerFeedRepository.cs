using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class IndexerFeedRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<IndexerFeedRepository> logger,
    IDataProtectionService protector,
    TimeProvider? timeProvider = null) : IIndexerFeedRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(IndexerFeed current, IndexerFeed model)
    {
        var fields = new List<string>();
        if (current.ExtensionId != model.ExtensionId)
        {
            fields.Add("extensionId");
        }

        if (!string.Equals(current.ProviderType, model.ProviderType, StringComparison.Ordinal))
        {
            fields.Add("providerType");
        }

        if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
        {
            fields.Add("name");
        }

        if (!string.Equals(current.Description, model.Description, StringComparison.Ordinal))
        {
            fields.Add("description");
        }

        if (!SettingsEqual(current.Settings, model.Settings))
        {
            fields.Add("settings");
        }

        if (current.Enabled != model.Enabled)
        {
            fields.Add("enabled");
        }

        if (current.LastSync != model.LastSync)
        {
            fields.Add("lastSync");
        }

        return fields;
    }

    private static bool SettingsEqual(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> model)
    {
        return current.Count == model.Count &&
            current.All(pair => model.TryGetValue(pair.Key, out var value) &&
                string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    public async Task<IEnumerable<IndexerFeed>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var feeds = await context.IndexerFeeds
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var feed in feeds)
            {
                feed.Settings = protector.Unprotect(feed.Settings);
            }

            return feeds;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer feed repository failed to list the indexer feeds.");
            throw;
        }
    }

    public async Task<IndexerFeed?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var feed = await context.IndexerFeeds
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (feed is not null)
            {
                feed.Settings = protector.Unprotect(feed.Settings);
            }

            return feed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer feed repository failed to read indexer feed {IndexerFeedId}.", id);
            throw;
        }
    }

    public async Task<IndexerFeed> CreateAsync(
        IndexerFeed model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    var plaintext = model.Settings;
                    model.Settings = protector.Protect(plaintext);

                    context.IndexerFeeds.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    context.Entry(model).State = EntityState.Detached;
                    model.Settings = plaintext;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(IndexerFeed),
                            Detail = $"Created indexer feed {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(IndexerEventBuilder.BuildIndexerFeedCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer feed repository failed to create indexer feed " +
                "{IndexerFeedName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(IndexerFeed model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyCollection<string> changedFields = [];
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var encryptedSettings = protector.Protect(model.Settings);

                    var current = await context.IndexerFeeds
                        .AsNoTracking()
                        .FirstOrDefaultAsync(f => f.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    current.Settings = protector.Unprotect(current.Settings);
                    changedFields = ChangedFields(current, model);
                    var updated = await context.IndexerFeeds
                        .Where(f => f.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(f => f.ExtensionId, model.ExtensionId)
                                .SetProperty(f => f.ProviderType, model.ProviderType)
                                .SetProperty(f => f.Name, model.Name)
                                .SetProperty(f => f.Description, model.Description)
                                .SetProperty(f => f.Settings, encryptedSettings)
                                .SetProperty(f => f.Enabled, model.Enabled)
                                .SetProperty(f => f.LastSync, model.LastSync)
                                .SetProperty(f => f.ModifiedAt, now)
                                .SetProperty(f => f.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(IndexerFeed),
                                Detail = $"Updated indexer feed {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (applied && changedFields.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        IndexerEventBuilder.BuildIndexerFeedUpdatedEvent(model.Id, changedFields), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer feed repository failed to update indexer feed " +
                "{IndexerFeedId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var model = await (
                        from f in context.IndexerFeeds.AsNoTracking()
                        where f.Id == id
                        select new { f.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.IndexerFeeds
                        .Where(f => f.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(IndexerFeed),
                                Detail = $"Deleted indexer feed {model.Name}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(IndexerEventBuilder.BuildIndexerFeedDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer feed repository failed to delete indexer feed {IndexerFeedId}.", id);
            throw;
        }
    }
}
