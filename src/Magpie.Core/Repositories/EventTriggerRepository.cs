using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing event triggers.
/// </summary>
public sealed class EventTriggerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<EventTriggerRepository> logger) : IEventTriggerRepository
{
    /// <inheritdoc />
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
            logger.LogError(ex, "Error listing event triggers");
            throw;
        }
    }

    /// <inheritdoc />
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
            logger.LogError(ex, "Error getting event trigger {EventTriggerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<EventTrigger>> ListByExtensionIdAsync(
        Guid extensionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.EventTriggers
                .AsNoTracking()
                .Where(t => t.ExtensionId == extensionId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing event triggers for extension {ExtensionId}", extensionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<EventTrigger>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.EventTriggers
                .AsNoTracking()
                .Where(t => t.Enabled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing enabled event triggers");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<EventTrigger> CreateAsync(
        EventTrigger model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.EventTriggers.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(EventTrigger),
                    Detail = $"Created event trigger {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating event trigger {EventTriggerName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(EventTrigger model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.EventTriggers
                    .Where(t => t.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(t => t.ExtensionId, model.ExtensionId)
                            .SetProperty(t => t.JobType, model.JobType)
                            .SetProperty(t => t.Scope, model.Scope)
                            .SetProperty(t => t.ExecutionMode, model.ExecutionMode)
                            .SetProperty(t => t.EventFilter, model.EventFilter)
                            .SetProperty(t => t.Name, model.Name)
                            .SetProperty(t => t.Enabled, model.Enabled)
                            .SetProperty(t => t.Parameters, model.Parameters)
                            .SetProperty(t => t.ModifiedAt, now)
                            .SetProperty(t => t.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(EventTrigger),
                        Detail = $"Updated event trigger {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating event trigger {EventTriggerId}", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var model = await context.EventTriggers
                    .AsNoTracking()
                    .Where(t => t.Id == id)
                    .Select(t => new { t.Name })
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(EventTrigger),
                        Detail = $"Deleted event trigger {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting event trigger {EventTriggerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        EventTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var existing = await context.EventTriggers
                    .AsNoTracking()
                    .Where(t => t.Id == patch.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                // Apply patches to build updated values
                var name = patch.Name?.Value ?? existing.Name;
                var eventFilter = patch.EventFilter?.Value ?? existing.EventFilter;
                var executionMode = patch.ExecutionMode?.Value ?? existing.ExecutionMode;
                var enabled = patch.Enabled?.Value ?? existing.Enabled;
                var parameters = existing.Parameters ?? new Dictionary<string, string>();

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
                            .SetProperty(t => t.ExecutionMode, executionMode)
                            .SetProperty(t => t.Enabled, enabled)
                            .SetProperty(t => t.Parameters, parameters)
                            .SetProperty(t => t.ModifiedAt, now)
                            .SetProperty(t => t.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(EventTrigger),
                        Detail = $"Patched event trigger {name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching event trigger {EventTriggerId}", patch.Id);
            throw;
        }
    }
}
