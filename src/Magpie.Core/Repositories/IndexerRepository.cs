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
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class IndexerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<IndexerRepository> logger,
    IDataProtectionService protector,
    TimeProvider timeProvider) : IIndexerRepository
{
    private static readonly HashSet<string> ConnectionHealthFields =
    [
        nameof(IndexerConnection.Status),
        nameof(IndexerConnection.LastChecked),
        nameof(IndexerConnection.LastError),
        nameof(IndexerConnection.ConsecutiveFailures),
    ];

    private readonly TimeProvider _time = timeProvider;

    private static IReadOnlyCollection<string> ChangedFields(IndexerPatch patch)
    {
        var fields = new List<string>();
        if (patch.ProviderType is not null)
        {
            fields.Add(nameof(Indexer.ProviderType));
        }

        if (patch.Name is not null)
        {
            fields.Add(nameof(Indexer.Name));
        }

        if (patch.Description is not null)
        {
            fields.Add(nameof(Indexer.Description));
        }

        if (patch.Availability is not null)
        {
            fields.Add(nameof(Indexer.Availability));
        }

        if (patch.Categories is not null)
        {
            fields.Add(nameof(Indexer.Categories));
        }

        if (patch.Languages is not null)
        {
            fields.Add(nameof(Indexer.Languages));
        }

        return fields;
    }

    private static IReadOnlyCollection<string> ChangedFields(Indexer current, Indexer model)
    {
        var fields = new List<string>();
        if (current.FeedId != model.FeedId)
        {
            fields.Add(nameof(Indexer.FeedId));
        }

        if (current.ExtensionId != model.ExtensionId)
        {
            fields.Add(nameof(Indexer.ExtensionId));
        }

        if (!string.Equals(current.ProviderType, model.ProviderType, StringComparison.Ordinal))
        {
            fields.Add(nameof(Indexer.ProviderType));
        }

        if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
        {
            fields.Add(nameof(Indexer.Name));
        }

        if (!string.Equals(current.Description, model.Description, StringComparison.Ordinal))
        {
            fields.Add(nameof(Indexer.Description));
        }

        if (current.Availability != model.Availability)
        {
            fields.Add(nameof(Indexer.Availability));
        }

        if (!current.Categories.SequenceEqual(model.Categories))
        {
            fields.Add(nameof(Indexer.Categories));
        }

        if (!current.Languages.SequenceEqual(model.Languages))
        {
            fields.Add(nameof(Indexer.Languages));
        }

        if (!current.SupportedContentTypes.SequenceEqual(model.SupportedContentTypes))
        {
            fields.Add(nameof(Indexer.SupportedContentTypes));
        }

        if (!string.Equals(current.DefinitionType, model.DefinitionType, StringComparison.Ordinal))
        {
            fields.Add(nameof(Indexer.DefinitionType));
        }

        if (!string.Equals(current.SchemaData, model.SchemaData, StringComparison.Ordinal))
        {
            fields.Add(nameof(Indexer.SchemaData));
        }

        return fields;
    }

    public async Task<IEnumerable<Indexer>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Indexers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to list the indexers.");
            throw;
        }
    }

    public async Task<Indexer?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Indexers
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to read indexer {IndexerId}.", id);
            throw;
        }
    }

    public async Task<Indexer> CreateAsync(Indexer model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    context.Indexers.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(Indexer),
                            Detail = $"Created indexer {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(IndexerEventBuilder.BuildIndexerCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to create indexer {IndexerName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Indexer model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyCollection<string> changedFields = [];
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var current = await context.Indexers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    changedFields = ChangedFields(current, model);
                    var updated = await context.Indexers
                        .Where(i => i.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(i => i.FeedId, model.FeedId)
                                .SetProperty(i => i.ExtensionId, model.ExtensionId)
                                .SetProperty(i => i.ProviderType, model.ProviderType)
                                .SetProperty(i => i.Name, model.Name)
                                .SetProperty(i => i.Description, model.Description)
                                .SetProperty(i => i.Availability, model.Availability)
                                .SetProperty(i => i.Categories, model.Categories)
                                .SetProperty(i => i.Languages, model.Languages)
                                .SetProperty(i => i.SupportedContentTypes, model.SupportedContentTypes)
                                .SetProperty(i => i.DefinitionType, model.DefinitionType)
                                .SetProperty(i => i.SchemaData, model.SchemaData)
                                .SetProperty(i => i.ModifiedAt, now)
                                .SetProperty(i => i.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(Indexer),
                                Detail = $"Updated indexer {model.Name}",
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
                        IndexerEventBuilder.BuildIndexerUpdatedEvent(model.Id, changedFields),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to update indexer {IndexerId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        IndexerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.Indexers
                        .FirstOrDefaultAsync(i => i.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.ProviderType is not null)
                    {
                        model.ProviderType = patch.ProviderType.Value;
                    }

                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Description is not null)
                    {
                        model.Description = patch.Description.Value;
                    }

                    if (patch.Availability is not null)
                    {
                        model.Availability = patch.Availability.Value;
                    }

                    if (patch.Categories is not null)
                    {
                        model.Categories = patch.Categories.Value;
                    }

                    if (patch.Languages is not null)
                    {
                        model.Languages = patch.Languages.Value;
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(Indexer),
                                Detail = $"Updated indexer {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        IndexerEventBuilder.BuildIndexerUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to patch indexer {IndexerId}.", patch.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IndexerConnection> CreateConnectionAsync(
        IndexerConnection connection,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    connection.CreatedAt = now;
                    connection.CreatedByUserId = createdByUserId;

                    var plaintext = connection.Settings;
                    connection.Settings = protector.Protect(plaintext);

                    context.IndexerConnections.Add(connection);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    context.Entry(connection).State = EntityState.Detached;
                    connection.Settings = plaintext;

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Integrations,
                            Operation = "Create",
                            EntityId = connection.Id.ToString(),
                            EntityType = nameof(IndexerConnection),
                            Detail = $"Created indexer connection {connection.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return connection;
                },
                cancellationToken).ConfigureAwait(false);

            await PublishConnectionsUpdatedAsync(created.IndexerId, cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The indexer repository failed to create indexer connection {IndexerConnectionName}.",
                connection.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchConnectionAsync(
        IndexerConnectionPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexerId = Guid.Empty;
            var configurationChanged = false;
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.IndexerConnections
                        .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    indexerId = model.IndexerId;
                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Status is not null)
                    {
                        model.Status = patch.Status.Value;
                    }

                    if (patch.LastChecked is not null)
                    {
                        model.LastChecked = patch.LastChecked.Value;
                    }

                    if (patch.LastError is not null)
                    {
                        var error = patch.LastError.Value;
                        model.LastError = error is { Length: > 1024 } ? error[..1024] : error;
                    }

                    if (patch.ConsecutiveFailures is not null)
                    {
                        model.ConsecutiveFailures = patch.ConsecutiveFailures.Value;
                    }

                    if (patch.Settings is not null)
                    {
                        var settings = protector.Unprotect(model.Settings);
                        if (patch.Settings.Add is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Add)
                            {
                                settings[key] = value ?? string.Empty;
                            }
                        }

                        if (patch.Settings.Update is not null)
                        {
                            foreach (var (key, value) in patch.Settings.Update)
                            {
                                if (settings.ContainsKey(key))
                                {
                                    settings[key] = value ?? string.Empty;
                                }
                            }
                        }

                        if (patch.Settings.Remove is not null)
                        {
                            foreach (var key in patch.Settings.Remove)
                            {
                                settings.Remove(key);
                            }
                        }

                        model.Settings = protector.Protect(settings);
                        context.Entry(model).Property(m => m.Settings).IsModified = true;
                    }

                    var tagsAdded = new List<string>();
                    var tagsRemoved = new List<string>();

                    if (patch.Tags is not null)
                    {
                        if (patch.Tags.Add is not null)
                        {
                            foreach (var tagName in patch.Tags.Add)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.IndexerConnections)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is null)
                                {
                                    tag = new() { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                if (!tag.IndexerConnections.Contains(model))
                                {
                                    tag.IndexerConnections.Add(model);
                                    tagsAdded.Add(tagName);
                                }
                            }
                        }

                        if (patch.Tags.Remove is not null)
                        {
                            foreach (var tagName in patch.Tags.Remove)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.IndexerConnections)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is not null && tag.IndexerConnections.Remove(model))
                                {
                                    tagsRemoved.Add(tagName);
                                }
                            }
                        }
                    }

                    // Health has its own event, so only a configuration change updates the indexer.
                    configurationChanged = tagsAdded.Count + tagsRemoved.Count > 0 ||
                        context.Entry(model).Properties.Any(property =>
                            property.IsModified && !ConnectionHealthFields.Contains(property.Metadata.Name));
                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Integrations,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(IndexerConnection),
                                Detail = $"Updated indexer connection {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(IndexerConnection),
                                    Detail = $"Added tag {tagName} to indexer connection {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tagName in tagsRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.Integrations,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(IndexerConnection),
                                    Detail = $"Removed tag {tagName} from indexer connection {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched && configurationChanged)
            {
                await PublishConnectionsUpdatedAsync(indexerId, cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The indexer repository failed to patch indexer connection {IndexerConnectionId}.",
                patch.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteConnectionAsync(
        Guid connectionId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexerId = await transactionManager.ExecuteWithTransactionAsync(
                _ => DeleteConnectionCoreAsync(connectionId, deletedByUserId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (indexerId is null)
            {
                return false;
            }

            await PublishConnectionsUpdatedAsync(indexerId.Value, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The indexer repository failed to delete indexer connection {IndexerConnectionId}.",
                connectionId);
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
                        from i in context.Indexers.AsNoTracking()
                        where i.Id == id
                        select new { i.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var connectionIds = await context.IndexerConnections.AsNoTracking()
                        .Where(c => c.IndexerId == id)
                        .Select(c => c.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var connectionId in connectionIds)
                    {
                        await DeleteConnectionCoreAsync(connectionId, deletedByUserId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var deleted = await context.Indexers
                        .Where(i => i.Id == id)
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
                                EntityType = nameof(Indexer),
                                Detail = $"Deleted indexer {model.Name}",
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
                    token => eventBus.PublishAsync(IndexerEventBuilder.BuildIndexerDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to delete indexer {IndexerId}.", id);
            throw;
        }
    }

    public async Task<PagedList<Indexer>> QueryAsync(
        IndexerQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexers = context.Indexers.AsNoTracking().AsQueryable();

            if (query.FeedId is not null)
            {
                indexers = indexers.Where(i => i.FeedId == query.FeedId);
            }

            var totalCount = await indexers.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(Indexer.Id), nameof(Indexer.FeedId), nameof(Indexer.ExtensionId),
                nameof(Indexer.ProviderType), nameof(Indexer.Name), nameof(Indexer.Description),
                nameof(Indexer.Availability), nameof(Indexer.DefinitionType), nameof(Indexer.CreatedAt),
                nameof(Indexer.CreatedByUserId), nameof(Indexer.ModifiedAt), nameof(Indexer.ModifiedByUserId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                indexers = sort.Direction == SortDirection.Ascending
                    ? indexers.OrderBy(i => EF.Property<object>(i, sortProperty)).ThenBy(i => i.Id)
                    : indexers.OrderByDescending(i => EF.Property<object>(i, sortProperty)).ThenBy(i => i.Id);
            }
            else
            {
                indexers = indexers.OrderBy(i => i.Name).ThenBy(i => i.Id);
            }

            indexers = indexers.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await indexers.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The indexer repository failed to query the indexers.");
            throw;
        }
    }

    private async Task<Guid?> DeleteConnectionCoreAsync(
        Guid connectionId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        var model = await (
            from c in context.IndexerConnections.AsNoTracking()
            where c.Id == connectionId
            select new { c.Name, c.IndexerId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (model is null)
        {
            return null;
        }

        var deleted = await context.IndexerConnections
            .Where(c => c.Id == connectionId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 0)
        {
            return null;
        }

        await auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.Integrations,
                Operation = "Delete",
                EntityId = connectionId.ToString(),
                EntityType = nameof(IndexerConnection),
                Detail = $"Deleted indexer connection {model.Name}",
                Timestamp = _time.GetUtcNow().UtcDateTime,
                UserId = deletedByUserId
            },
            cancellationToken).ConfigureAwait(false);
        return model.IndexerId;
    }

    private Task PublishConnectionsUpdatedAsync(Guid indexerId, CancellationToken cancellationToken) =>
        transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(
                IndexerEventBuilder.BuildIndexerUpdatedEvent(indexerId, [nameof(Indexer.Connections)]),
                token),
            cancellationToken);
}
