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

internal sealed class EventTriggerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<EventTriggerRepository> logger,
    TimeProvider? timeProvider = null) : IEventTriggerRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static void ValidateDelaySeconds(int delaySeconds)
    {
        if (delaySeconds < 0)
        {
            throw new MagpieException("Delay cannot be negative.");
        }
    }

    private static IReadOnlyCollection<string> ChangedFields(EventTriggerPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.EventFilter is not null)
        {
            fields.Add("eventFilter");
        }

        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.DelaySeconds is not null)
        {
            fields.Add("delaySeconds");
        }

        if (patch.Parameters is not null)
        {
            fields.Add("parameters");
        }

        return fields;
    }

    public async Task<IEnumerable<EventTrigger>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.EventTriggers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The event trigger repository failed to list the event triggers.");
            throw;
        }
    }

    public async Task<EventTrigger?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.EventTriggers
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The event trigger repository failed to read event trigger {EventTriggerId}.", id);
            throw;
        }
    }

    public async Task<PagedList<EventTrigger>> QueryAsync(
        EventTriggerQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var triggers = context.EventTriggers.AsNoTracking().AsQueryable();

            if (query.ExtensionId is not null)
            {
                triggers = triggers.Where(t => t.ExtensionId == query.ExtensionId);
            }

            if (query.Enabled is not null)
            {
                triggers = triggers.Where(t => t.Enabled == query.Enabled);
            }

            if (query.CreatedByUserId is not null)
            {
                triggers = triggers.Where(t => t.CreatedByUserId == query.CreatedByUserId);
            }

            var totalCount = await triggers.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(EventTrigger).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                triggers = sort.Direction == SortDirection.Ascending
                    ? triggers.OrderBy(t => EF.Property<EventTrigger>(t, sortProperty)).ThenBy(t => t.Id)
                    : triggers.OrderByDescending(t => EF.Property<EventTrigger>(t, sortProperty)).ThenBy(t => t.Id);
            }
            else
            {
                triggers = triggers.OrderBy(t => t.Name).ThenBy(t => t.Id);
            }

            triggers = triggers.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await triggers.ToListAsync(cancellationToken).ConfigureAwait(false);

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
            logger.LogError(ex, "The event trigger repository failed to query the event triggers.");
            throw;
        }
    }

    public async Task<EventTrigger> CreateAsync(
        EventTrigger model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        ValidateDelaySeconds(model.DelaySeconds);

        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    context.EventTriggers.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(EventTrigger),
                            Detail = $"Created event trigger {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(TriggerEventBuilder.BuildEventTriggerCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The event trigger repository failed to create event trigger " +
                "{EventTriggerName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(EventTrigger model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        ValidateDelaySeconds(model.DelaySeconds);

        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var updated = await context.EventTriggers
                        .Where(t => t.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(t => t.ExtensionId, model.ExtensionId)
                                .SetProperty(t => t.JobType, model.JobType)
                                .SetProperty(t => t.Scope, model.Scope)
                                .SetProperty(t => t.EventFilter, model.EventFilter)
                                .SetProperty(t => t.Name, model.Name)
                                .SetProperty(t => t.Enabled, model.Enabled)
                                .SetProperty(t => t.DelaySeconds, model.DelaySeconds)
                                .SetProperty(t => t.Parameters, model.Parameters)
                                .SetProperty(t => t.ModifiedAt, now)
                                .SetProperty(t => t.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(EventTrigger),
                                Detail = $"Updated event trigger {model.Name}",
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
            logger.LogError(ex, "The event trigger repository failed to update event trigger " +
                "{EventTriggerId}.", model.Id);
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
                        from t in context.EventTriggers.AsNoTracking()
                        where t.Id == id
                        select new { t.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.EventTriggers
                        .Where(t => t.Id == id)
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
                                EntityType = nameof(EventTrigger),
                                Detail = $"Deleted event trigger {model.Name}",
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
                    token => eventBus.PublishAsync(TriggerEventBuilder.BuildEventTriggerDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The event trigger repository failed to delete event trigger {EventTriggerId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        EventTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        if (patch.DelaySeconds is not null)
        {
            ValidateDelaySeconds(patch.DelaySeconds.Value);
        }

        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;

                    var existing = await context.EventTriggers
                        .AsNoTracking()
                        .Where(t => t.Id == patch.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        return false;
                    }

                    var name = patch.Name?.Value ?? existing.Name;
                    var eventFilter = patch.EventFilter?.Value ?? existing.EventFilter;
                    var enabled = patch.Enabled?.Value ?? existing.Enabled;
                    var delaySeconds = patch.DelaySeconds?.Value ?? existing.DelaySeconds;
                    var parameters = existing.Parameters ?? [];

                    if (patch.Parameters is not null)
                    {
                        if (patch.Parameters.Remove is not null)
                        {
                            foreach (var key in patch.Parameters.Remove)
                            {
                                parameters.Remove(key);
                            }
                        }

                        if (patch.Parameters.Add is not null)
                        {
                            foreach (var (key, value) in patch.Parameters.Add)
                            {
                                parameters[key] = value;
                            }
                        }

                        if (patch.Parameters.Update is not null)
                        {
                            foreach (var (key, value) in patch.Parameters.Update)
                            {
                                if (parameters.ContainsKey(key))
                                {
                                    parameters[key] = value;
                                }
                            }
                        }
                    }

                    var updated = await context.EventTriggers
                        .Where(t => t.Id == patch.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(t => t.Name, name)
                                .SetProperty(t => t.EventFilter, eventFilter)
                                .SetProperty(t => t.Enabled, enabled)
                                .SetProperty(t => t.DelaySeconds, delaySeconds)
                                .SetProperty(t => t.Parameters, parameters)
                                .SetProperty(t => t.ModifiedAt, now)
                                .SetProperty(t => t.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(EventTrigger),
                                Detail = $"Updated event trigger {name}.",
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
                        TriggerEventBuilder.BuildEventTriggerUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The event trigger repository failed to patch event trigger " +
                "{EventTriggerId}.", patch.Id);
            throw;
        }
    }
}
