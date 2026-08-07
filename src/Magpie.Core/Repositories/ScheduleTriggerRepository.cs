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

internal sealed class ScheduleTriggerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<ScheduleTriggerRepository> logger,
    TimeProvider? timeProvider = null) : IScheduleTriggerRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(ScheduleTriggerPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.Schedule is not null)
        {
            fields.Add("schedule");
        }

        if (patch.ExecutionMode is not null)
        {
            fields.Add("executionMode");
        }

        if (patch.Enabled is not null)
        {
            fields.Add("enabled");
        }

        if (patch.Parameters is not null)
        {
            fields.Add("parameters");
        }

        return fields;
    }

    public async Task<IEnumerable<ScheduleTrigger>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.ScheduleTriggers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to list schedule triggers.");
            throw;
        }
    }

    public async Task<ScheduleTrigger?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.ScheduleTriggers
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to read schedule trigger {TriggerId}.", id);
            throw;
        }
    }

    public async Task<PagedList<ScheduleTrigger>> QueryAsync(
        ScheduleTriggerQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var triggers = context.ScheduleTriggers.AsNoTracking().AsQueryable();

            if (query.DueAsOf is not null)
            {
                triggers = triggers.Where(t => t.NextRunAt != null && t.NextRunAt <= query.DueAsOf);
            }

            if (query.CreatedByUserId is not null)
            {
                triggers = triggers.Where(t => t.CreatedByUserId == query.CreatedByUserId);
            }

            if (query.JobType is not null)
            {
                triggers = triggers.Where(t => t.JobType == query.JobType);
            }

            if (query.Enabled is not null)
            {
                triggers = triggers.Where(t => t.Enabled == query.Enabled);
            }

            if (query.HasNextRun is not null)
            {
                triggers = query.HasNextRun.Value
                    ? triggers.Where(t => t.NextRunAt != null)
                    : triggers.Where(t => t.NextRunAt == null);
            }

            var totalCount = await triggers.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortProperty = typeof(ScheduleTrigger).GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                triggers = sort.Direction == SortDirection.Ascending
                    ? triggers.OrderBy(t => EF.Property<ScheduleTrigger>(t, sortProperty)).ThenBy(t => t.Id)
                    : triggers.OrderByDescending(t => EF.Property<ScheduleTrigger>(t, sortProperty)).ThenBy(t => t.Id);
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
            logger.LogError(ex, "The schedule trigger repository failed to query schedule triggers.");
            throw;
        }
    }

    public async Task<bool> TryClaimNextRunAsync(
        Guid triggerId,
        DateTime expectedNextRun,
        DateTime newNextRun,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var updated = await context.ScheduleTriggers
                .Where(t => t.Id == triggerId && t.Enabled && t.NextRunAt == expectedNextRun)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(t => t.LastRunAt, now)
                        .SetProperty(t => t.NextRunAt, newNextRun),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to claim schedule trigger " +
                "{TriggerId}.", triggerId);
            throw;
        }
    }

    public async Task<ScheduleTrigger> CreateAsync(
        ScheduleTrigger model,
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

                    context.ScheduleTriggers.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(ScheduleTrigger),
                            Detail = $"Created schedule trigger {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(TriggerEventBuilder.BuildScheduleTriggerCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to create schedule trigger " +
                "{TriggerName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ScheduleTrigger model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.ScheduleTriggers
                        .Where(t => t.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(t => t.ExtensionId, model.ExtensionId)
                                .SetProperty(t => t.JobType, model.JobType)
                                .SetProperty(t => t.Scope, model.Scope)
                                .SetProperty(t => t.Name, model.Name)
                                .SetProperty(t => t.Schedule, model.Schedule)
                                .SetProperty(t => t.ExecutionMode, model.ExecutionMode)
                                .SetProperty(t => t.Enabled, model.Enabled)
                                .SetProperty(t => t.Parameters, model.Parameters)
                                .SetProperty(t => t.LastRunAt, model.LastRunAt)
                                .SetProperty(t => t.NextRunAt, model.NextRunAt)
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
                                EntityType = nameof(ScheduleTrigger),
                                Detail = $"Updated schedule trigger {model.Name}",
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
            logger.LogError(ex, "The schedule trigger repository failed to update schedule trigger " +
                "{TriggerId}.", model.Id);
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
                        from t in context.ScheduleTriggers.AsNoTracking()
                        where t.Id == id
                        select new { t.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.ScheduleTriggers
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
                                EntityType = nameof(ScheduleTrigger),
                                Detail = $"Deleted schedule trigger {model.Name}",
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
                    token => eventBus.PublishAsync(TriggerEventBuilder.BuildScheduleTriggerDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to delete schedule trigger {TriggerId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ScheduleTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.ScheduleTriggers
                        .FirstOrDefaultAsync(t => t.Id == patch.Id, cancellationToken)
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

                    if (patch.Schedule is not null)
                    {
                        model.Schedule = patch.Schedule.Value;
                    }

                    if (patch.ExecutionMode is not null)
                    {
                        model.ExecutionMode = patch.ExecutionMode.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Parameters is not null)
                    {
                        if (patch.Parameters.Add is not null)
                        {
                            foreach (var (key, value) in patch.Parameters.Add)
                            {
                                model.Parameters[key] = value ?? string.Empty;
                            }
                        }

                        if (patch.Parameters.Update is not null)
                        {
                            foreach (var (key, value) in patch.Parameters.Update)
                            {
                                if (model.Parameters.ContainsKey(key))
                                {
                                    model.Parameters[key] = value ?? string.Empty;
                                }
                            }
                        }

                        if (patch.Parameters.Remove is not null)
                        {
                            foreach (var key in patch.Parameters.Remove)
                            {
                                model.Parameters.Remove(key);
                            }
                        }

                        context.Entry(model).Property(t => t.Parameters).IsModified = true;
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
                                EntityType = nameof(ScheduleTrigger),
                                Detail = $"Updated schedule trigger {model.Name}.",
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
                        TriggerEventBuilder.BuildScheduleTriggerUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The schedule trigger repository failed to patch schedule trigger " +
                "{TriggerId}.", patch.Id);
            throw;
        }
    }
}
