using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

internal sealed class ContentFilterRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<ContentFilterRepository> logger,
    TimeProvider timeProvider) : IContentFilterRepository
{
    private readonly TimeProvider _time = timeProvider;

    private static IReadOnlyCollection<string> ChangedFields(ContentFilterPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add(nameof(ContentFilter.Name));
        }

        if (patch.Criteria is not null)
        {
            fields.Add(nameof(ContentFilter.Criteria));
        }

        if (patch.OwnerLock is not null)
        {
            fields.Add(nameof(ContentFilter.OwnerLock));
        }

        if (patch.Tags is not null)
        {
            fields.Add(nameof(ContentFilterPatch.Tags));
        }

        return fields;
    }

    public async Task<IEnumerable<ContentFilter>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content filter repository failed to list the content filters.");
            throw;
        }
    }

    public async Task<ContentFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ContentFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content filter repository failed to read content filter {ContentFilterId}.", id);
            throw;
        }
    }

    public async Task<PagedList<ContentFilter>> QueryAsync(
        ContentFilterQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var filters = context.ContentFilters.AsNoTracking().AsQueryable();

            if (query.Name is not null)
            {
                filters = filters.Where(f => f.Name == query.Name);
            }

            if (query.Tag is not null)
            {
                filters = filters.Where(f => context.Tags
                    .Any(t => t.Name == query.Tag && t.ContentFilters.Any(cf => cf.Id == f.Id)));
            }

            if (query.OwnerUserId is not null)
            {
                filters = filters.Where(f => f.OwnerUserId == query.OwnerUserId);
            }

            if (query.SharedOnly is not null)
            {
                filters = query.SharedOnly.Value
                    ? filters.Where(f => f.OwnerUserId == null)
                    : filters.Where(f => f.OwnerUserId != null);
            }

            var totalCount = await filters.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(ContentFilter.Id), nameof(ContentFilter.OwnerUserId), nameof(ContentFilter.Name),
                nameof(ContentFilter.OwnerLock), nameof(ContentFilter.CreatedAt), nameof(ContentFilter.CreatedByUserId),
                nameof(ContentFilter.ModifiedAt), nameof(ContentFilter.ModifiedByUserId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                filters = sort.Direction == SortDirection.Ascending
                    ? filters.OrderBy(f => EF.Property<object>(f, sortProperty)).ThenBy(f => f.Id)
                    : filters.OrderByDescending(f => EF.Property<object>(f, sortProperty)).ThenBy(f => f.Id);
            }
            else
            {
                filters = filters.OrderBy(f => f.Name).ThenBy(f => f.Id);
            }

            filters = filters.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await filters.ToListAsync(cancellationToken).ConfigureAwait(false);

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
            logger.LogError(ex, "The content filter repository failed to query the content filters.");
            throw;
        }
    }

    public async Task<ContentFilter> CreateAsync(
        ContentFilter model,
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

                    context.ContentFilters.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(ContentFilter),
                            Detail = $"Created content filter {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ConfigurationEventBuilder.BuildContentFilterCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content filter repository failed to create content filter {ContentFilterName}.",
                model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ContentFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var changedFields = new List<string>();
            var updated = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var current = await context.ContentFilters
                        .AsNoTracking()
                        .Where(f => f.Id == model.Id)
                        .Select(f => new { f.Name, f.Criteria, f.OwnerUserId, f.OwnerLock })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
                    {
                        changedFields.Add(nameof(ContentFilter.Name));
                    }

                    if (!string.Equals(
                        JsonSerializer.Serialize(current.Criteria),
                        JsonSerializer.Serialize(model.Criteria),
                        StringComparison.Ordinal))
                    {
                        changedFields.Add(nameof(ContentFilter.Criteria));
                    }

                    if (current.OwnerUserId != model.OwnerUserId)
                    {
                        changedFields.Add(nameof(ContentFilter.OwnerUserId));
                    }

                    if (current.OwnerLock != model.OwnerLock)
                    {
                        changedFields.Add(nameof(ContentFilter.OwnerLock));
                    }

                    var count = await context.ContentFilters
                        .Where(f => f.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(f => f.Name, model.Name)
                                .SetProperty(f => f.Criteria, model.Criteria)
                                .SetProperty(f => f.OwnerUserId, model.OwnerUserId)
                                .SetProperty(f => f.OwnerLock, model.OwnerLock)
                                .SetProperty(f => f.ModifiedAt, now)
                                .SetProperty(f => f.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (count > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(ContentFilter),
                                Detail = $"Updated content filter {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return count > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (updated && changedFields.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ConfigurationEventBuilder.BuildContentFilterUpdatedEvent(model.Id, changedFields),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return updated;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content filter repository failed to update content filter {ContentFilterId}.",
                model.Id);
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
                        from f in context.ContentFilters.AsNoTracking()
                        where f.Id == id
                        select new { f.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.ContentFilters
                        .Where(f => f.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(ContentFilter),
                                Detail = $"Deleted content filter {model.Name}",
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
                    token => eventBus.PublishAsync(ConfigurationEventBuilder.BuildContentFilterDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The content filter repository failed to delete content filter {ContentFilterId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ContentFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.ContentFilters
                        .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Criteria is not null)
                    {
                        model.Criteria = patch.Criteria.Value;
                    }

                    if (patch.OwnerLock is not null)
                    {
                        model.OwnerLock = patch.OwnerLock.Value;
                    }

                    var tagsAdded = new List<string>();
                    var tagsRemoved = new List<string>();
                    if (patch.Tags is not null)
                    {
                        var currentTags = await context.Tags
                            .Include(t => t.ContentFilters)
                            .Where(t => t.ContentFilters.Any(f => f.Id == patch.Id))
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var tagName in patch.Tags.Remove ?? [])
                        {
                            var tag = currentTags.FirstOrDefault(t => t.Name == tagName);
                            if (tag is not null)
                            {
                                tag.ContentFilters.Remove(model);
                                tagsRemoved.Add(tagName);
                            }
                        }

                        foreach (var tagName in patch.Tags.Add ?? [])
                        {
                            if (!currentTags.Any(t => t.Name == tagName))
                            {
                                var tag = await context.Tags
                                    .Include(t => t.ContentFilters)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);
                                if (tag is null)
                                {
                                    tag = new Tag { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                tag.ContentFilters.Add(model);
                                tagsAdded.Add(tagName);
                            }
                        }
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(ContentFilter),
                                Detail = $"Updated content filter {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(ContentFilter),
                                    Detail = $"Added tag {tagName} to content filter {model.Name}",
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
                                    Category = AuditCategories.System,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(ContentFilter),
                                    Detail = $"Removed tag {tagName} from content filter {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ConfigurationEventBuilder.BuildContentFilterUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content filter repository failed to patch content filter {ContentFilterId}.",
                patch.Id);
            throw;
        }
    }
}
