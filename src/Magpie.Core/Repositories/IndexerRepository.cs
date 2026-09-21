using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
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
    TimeProvider timeProvider) : IIndexerRepository
{
    private readonly TimeProvider _time = timeProvider;

    private static IReadOnlyCollection<string> ChangedFields(IndexerPatch patch)
    {
        var fields = new List<string>();
        if (patch.ProviderType is not null)
        {
            fields.Add("providerType");
        }

        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.Description is not null)
        {
            fields.Add("description");
        }

        if (patch.Availability is not null)
        {
            fields.Add("availability");
        }

        if (patch.Categories is not null)
        {
            fields.Add("categories");
        }

        if (patch.Languages is not null)
        {
            fields.Add("languages");
        }

        return fields;
    }

    private static IReadOnlyCollection<string> ChangedFields(Indexer current, Indexer model)
    {
        var fields = new List<string>();
        if (current.FeedId != model.FeedId)
        {
            fields.Add("feedId");
        }

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

        if (current.Availability != model.Availability)
        {
            fields.Add("availability");
        }

        if (!current.Categories.SequenceEqual(model.Categories))
        {
            fields.Add("categories");
        }

        if (!current.Languages.SequenceEqual(model.Languages))
        {
            fields.Add("languages");
        }

        if (!current.SupportedContentTypes.SequenceEqual(model.SupportedContentTypes))
        {
            fields.Add("supportedContentTypes");
        }

        if (!string.Equals(current.DefinitionType, model.DefinitionType, StringComparison.Ordinal))
        {
            fields.Add("definitionType");
        }

        if (!string.Equals(current.SchemaData, model.SchemaData, StringComparison.Ordinal))
        {
            fields.Add("schemaData");
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

                    await context.IndexerConnections
                        .Where(c => c.IndexerId == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

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
}
