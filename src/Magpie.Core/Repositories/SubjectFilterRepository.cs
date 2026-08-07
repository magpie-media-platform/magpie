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

internal sealed class SubjectFilterRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<SubjectFilterRepository> logger,
    TimeProvider? timeProvider = null) : ISubjectFilterRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(SubjectFilterPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.Criteria is not null)
        {
            fields.Add("criteria");
        }

        if (patch.Tags is not null)
        {
            fields.Add("tags");
        }

        return fields;
    }

    public async Task<IEnumerable<SubjectFilter>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to list subject filters.");
            throw;
        }
    }

    public async Task<SubjectFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.SubjectFilters
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to read subject filter {SubjectFilterId}.", id);
            throw;
        }
    }

    public async Task<PagedList<SubjectFilter>> QueryAsync(
        SubjectFilterQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var filters = context.SubjectFilters.AsNoTracking().AsQueryable();

            if (query.Name is not null)
            {
                filters = filters.Where(f => f.Name == query.Name);
            }

            if (query.Tag is not null)
            {
                filters = filters.Where(f => context.Tags
                    .Any(t => t.Name == query.Tag && t.SubjectFilters.Any(sf => sf.Id == f.Id)));
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

            var sortProperty = typeof(SubjectFilter).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                filters = sort.Direction == SortDirection.Ascending
                    ? filters.OrderBy(f => EF.Property<SubjectFilter>(f, sortProperty)).ThenBy(f => f.Id)
                    : filters.OrderByDescending(f => EF.Property<SubjectFilter>(f, sortProperty)).ThenBy(f => f.Id);
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
            logger.LogError(ex, "The subject filter repository failed to query subject filters.");
            throw;
        }
    }

    public async Task<SubjectFilter> CreateAsync(
        SubjectFilter model,
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

                    context.SubjectFilters.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(SubjectFilter),
                            Detail = $"Created subject filter {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(SubjectEventBuilder.BuildSubjectFilterCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to create subject filter " +
                "{SubjectFilterName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        SubjectFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.SubjectFilters
                        .Where(f => f.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(f => f.Name, model.Name)
                                .SetProperty(f => f.Criteria, model.Criteria)
                                .SetProperty(f => f.ModifiedAt, now)
                                .SetProperty(f => f.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(SubjectFilter),
                                Detail = $"Updated subject filter {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to update subject filter " +
                "{SubjectFilterId}.", model.Id);
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
                        from f in context.SubjectFilters.AsNoTracking()
                        where f.Id == id
                        select new { f.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.SubjectFilters
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
                                EntityType = nameof(SubjectFilter),
                                Detail = $"Deleted subject filter {model.Name}",
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
                    token => eventBus.PublishAsync(SubjectEventBuilder.BuildSubjectFilterDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to delete subject filter {SubjectFilterId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        SubjectFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await context.SubjectFilters
                        .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Criteria is not null)
                    {
                        model.Criteria = patch.Criteria.Value;
                    }

                    var tagsAdded = new List<string>();
                    var tagsRemoved = new List<string>();
                    if (patch.Tags is not null)
                    {
                        var currentTags = await context.Tags
                            .Where(t => t.SubjectFilters.Any(f => f.Id == patch.Id))
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var tagName in patch.Tags.Remove ?? [])
                        {
                            var tag = currentTags.FirstOrDefault(t => t.Name == tagName);
                            if (tag is not null)
                            {
                                tag.SubjectFilters.Remove(model);
                                tagsRemoved.Add(tagName);
                            }
                        }

                        foreach (var tagName in patch.Tags.Add ?? [])
                        {
                            if (!currentTags.Any(t => t.Name == tagName))
                            {
                                var tag = await context.Tags
                                    .Include(t => t.SubjectFilters)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);
                                if (tag is null)
                                {
                                    tag = new Tag { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                tag.SubjectFilters.Add(model);
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
                                EntityType = nameof(SubjectFilter),
                                Detail = $"Updated subject filter {model.Name}.",
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
                                    EntityType = nameof(SubjectFilter),
                                    Detail = $"Added tag {tagName} to subject filter {model.Name}",
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
                                    EntityType = nameof(SubjectFilter),
                                    Detail = $"Removed tag {tagName} from subject filter {model.Name}",
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
                        SubjectEventBuilder.BuildSubjectFilterUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject filter repository failed to patch subject filter " +
                "{SubjectFilterId}.", patch.Id);
            throw;
        }
    }
}
