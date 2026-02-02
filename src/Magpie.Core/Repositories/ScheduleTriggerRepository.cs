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
/// Repository for managing schedule triggers.
/// </summary>
public sealed class ScheduleTriggerRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ScheduleTriggerRepository> logger) : IScheduleTriggerRepository
{
    /// <inheritdoc />
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
            logger.LogError(ex, "Error listing schedule triggers");
            throw;
        }
    }

    /// <inheritdoc />
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
            logger.LogError(ex, "Error getting schedule trigger {TriggerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ScheduleTrigger>> ListDueTriggersAsync(
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        try
        {
            var allEnabled = await context.ScheduleTriggers
                .AsNoTracking()
                .Where(t => t.Enabled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. allEnabled.Where(t => t.NextRunAt <= asOf)];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing due schedule triggers");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimNextRunAsync(
        Guid triggerId,
        DateTime expectedNextRun,
        DateTime newNextRun,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var updated = await context.ScheduleTriggers
                .Where(t => t.Id == triggerId && t.NextRunAt == expectedNextRun)
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
            logger.LogError(ex, "Error claiming schedule trigger {TriggerId}", triggerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ScheduleTrigger> CreateAsync(
        ScheduleTrigger model,
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

                context.ScheduleTriggers.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(ScheduleTrigger),
                    Detail = $"Created schedule trigger {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating schedule trigger {TriggerName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        ScheduleTrigger model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(ScheduleTrigger),
                        Detail = $"Updated schedule trigger {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating schedule trigger {TriggerId}", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.ScheduleTriggers
                    .AsNoTracking()
                    .Where(t => t.Id == id)
                    .Select(t => new { t.Name })
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(ScheduleTrigger),
                        Detail = $"Deleted schedule trigger {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting schedule trigger {TriggerId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        ScheduleTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.ScheduleTriggers
                    .FirstOrDefaultAsync(t => t.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(ScheduleTrigger),
                        Detail = $"Patched schedule trigger {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching schedule trigger {TriggerId}", patch.Id);
            throw;
        }
    }
}
