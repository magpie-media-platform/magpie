using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.History;
using Magpie.Models.Result;
using Magpie.Models.Triggers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing schedule triggers.
/// </summary>
public sealed class ScheduleTriggerRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ScheduleTriggerRepository> logger) : IScheduleTriggerRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ScheduleTrigger>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.ScheduleTriggers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ScheduleTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting schedule triggers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<ScheduleTrigger>>(
                "Error retrieving schedule triggers",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScheduleTrigger?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from t in context.ScheduleTriggers.AsNoTracking()
                where t.Id == id
                select t)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting schedule trigger {TriggerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<ScheduleTrigger?>(
                "Error retrieving schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScheduleTrigger?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from t in context.ScheduleTriggers.AsNoTracking()
                where t.Name == name
                select t)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting schedule trigger {TriggerName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<ScheduleTrigger?>(
                "Error retrieving schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ScheduleTrigger>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from t in context.ScheduleTriggers.AsNoTracking()
                where t.JobTemplateId == jobTemplateId
                select t)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ScheduleTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting schedule triggers for job template {JobTemplateId} ({ExErrorCode})",
                jobTemplateId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ScheduleTrigger>>(
                "Error retrieving schedule triggers",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ScheduleTrigger>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from t in context.ScheduleTriggers.AsNoTracking()
                where t.Enabled
                select t)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ScheduleTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled schedule triggers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<ScheduleTrigger>>(
                "Error retrieving schedule triggers",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ScheduleTrigger>> CreateAsync(
        ScheduleTrigger model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.ScheduleTriggers.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(ScheduleTrigger),
                description: $"Created schedule trigger {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating schedule trigger {TriggerName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<ScheduleTrigger>(
                "Error creating schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        ScheduleTrigger model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var parametersJson = model.Parameters is not null
                ? JsonSerializer.Serialize(model.Parameters)
                : null;

            var updated = await context.ScheduleTriggers
                .Where(t => t.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.JobTemplateId, model.JobTemplateId)
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Schedule, model.Schedule)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.Parameters, parametersJson)
                        .SetProperty(e => e.LastRunAt, model.LastRunAt)
                        .SetProperty(e => e.NextRunAt, model.NextRunAt)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(ScheduleTrigger),
                    description: $"Updated schedule trigger {model.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating schedule trigger {TriggerId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ScheduleTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ScheduleTriggers
                .FirstOrDefaultAsync(t => t.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value!;
            }

            if (patch.Schedule is not null)
            {
                entity.Schedule = patch.Schedule.Value!;
            }

            if (patch.Enabled is not null)
            {
                entity.Enabled = patch.Enabled.Value;
            }

            if (patch.Parameters is not null)
            {
                entity.Parameters = patch.Parameters.Value is not null
                    ? JsonSerializer.Serialize(patch.Parameters.Value)
                    : null;
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(ScheduleTrigger),
                    description: $"Patched schedule trigger {entity.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching schedule trigger {TriggerId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ScheduleTriggers
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var deleted = await context.ScheduleTriggers
                .Where(t => t.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(ScheduleTrigger),
                    description: $"Deleted schedule trigger {entity.Name}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting schedule trigger {TriggerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting schedule trigger",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateRunTimesAsync(
        Guid id,
        DateTimeOffset? lastRunAt,
        DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.ScheduleTriggers
                .Where(t => t.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.LastRunAt, lastRunAt)
                        .SetProperty(e => e.NextRunAt, nextRunAt),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating schedule trigger run times {TriggerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating schedule trigger run times",
                ErrorCodes.DbScheduleTriggerError,
                errorCode);
        }
    }
}
