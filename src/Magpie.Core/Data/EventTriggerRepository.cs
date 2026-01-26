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
/// Repository for managing event triggers.
/// </summary>
public sealed class EventTriggerRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<EventTriggerRepository> logger) : IEventTriggerRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventTrigger>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.EventTriggers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<EventTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting event triggers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<EventTrigger>>(
                "Error retrieving event triggers",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<EventTrigger?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from t in context.EventTriggers.AsNoTracking()
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
                "Error getting event trigger {TriggerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<EventTrigger?>(
                "Error retrieving event trigger",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<EventTrigger?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from t in context.EventTriggers.AsNoTracking()
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
                "Error getting event trigger {TriggerName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<EventTrigger?>(
                "Error retrieving event trigger",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventTrigger>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from t in context.EventTriggers.AsNoTracking()
                where t.JobTemplateId == jobTemplateId
                select t)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<EventTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting event triggers for job template {JobTemplateId} ({ExErrorCode})",
                jobTemplateId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<EventTrigger>>(
                "Error retrieving event triggers",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventTrigger>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from t in context.EventTriggers.AsNoTracking()
                where t.Enabled
                select t)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<EventTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled event triggers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<EventTrigger>>(
                "Error retrieving event triggers",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventTrigger>>> GetByEventNameAsync(
        string eventName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from t in context.EventTriggers.AsNoTracking()
                where t.Enabled && t.EventName == eventName
                select t)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<EventTrigger>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting event triggers for event {EventName} ({ExErrorCode})",
                eventName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<EventTrigger>>(
                "Error retrieving event triggers",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<EventTrigger>> CreateAsync(
        EventTrigger model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.EventTriggers.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(EventTrigger),
                description: $"Created event trigger {model.Name}",
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
                "Error creating event trigger {TriggerName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<EventTrigger>(
                "Error creating event trigger",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        EventTrigger model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var filterJson = model.Filter is not null
                ? JsonSerializer.Serialize(model.Filter)
                : null;

            var parametersJson = model.Parameters is not null
                ? JsonSerializer.Serialize(model.Parameters)
                : null;

            var updated = await context.EventTriggers
                .Where(t => t.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.JobTemplateId, model.JobTemplateId)
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.EventName, model.EventName)
                        .SetProperty(e => e.Filter, filterJson)
                        .SetProperty(e => e.Enabled, model.Enabled)
                        .SetProperty(e => e.Parameters, parametersJson)
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
                    entityType: nameof(EventTrigger),
                    description: $"Updated event trigger {model.Name}",
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
                "Error updating event trigger {TriggerId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating event trigger",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        EventTriggerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.EventTriggers
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

            if (patch.EventName is not null)
            {
                entity.EventName = patch.EventName.Value!;
            }

            if (patch.Filter is not null)
            {
                entity.Filter = patch.Filter.Value is not null
                    ? JsonSerializer.Serialize(patch.Filter.Value)
                    : null;
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
                    entityType: nameof(EventTrigger),
                    description: $"Patched event trigger {entity.Name}",
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
                "Error patching event trigger {TriggerId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching event trigger",
                ErrorCodes.DbEventTriggerError,
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

            var entity = await context.EventTriggers
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var deleted = await context.EventTriggers
                .Where(t => t.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(EventTrigger),
                    description: $"Deleted event trigger {entity.Name}",
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
                "Error deleting event trigger {TriggerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting event trigger",
                ErrorCodes.DbEventTriggerError,
                errorCode);
        }
    }
}
