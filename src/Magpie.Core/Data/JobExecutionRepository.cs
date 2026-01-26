using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Data.Database.Entities;
using Magpie.Models.History;
using Magpie.Models.Jobs;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using JobExecution = Magpie.Models.Jobs.JobExecution;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing job executions.
/// </summary>
public sealed class JobExecutionRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<JobExecutionRepository> logger) : IJobExecutionRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobExecution>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.JobExecutions
                .AsNoTracking()
                .Include(e => e.Parameters)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<JobExecution>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting job executions ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<JobExecution>>(
                "Error retrieving job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<JobExecution?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.Id == id
                select e)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting job execution {ExecutionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<JobExecution?>(
                "Error retrieving job execution",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobExecution>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.JobTemplateId == jobTemplateId
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<JobExecution>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting job executions for job template {JobTemplateId} ({ExErrorCode})",
                jobTemplateId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<JobExecution>>(
                "Error retrieving job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobExecution>>> GetByScheduleTriggerIdAsync(
        Guid scheduleTriggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.ScheduleTriggerId == scheduleTriggerId
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<JobExecution>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting job executions for schedule trigger {ScheduleTriggerId} ({ExErrorCode})",
                scheduleTriggerId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<JobExecution>>(
                "Error retrieving job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobExecution>>> GetByEventTriggerIdAsync(
        Guid eventTriggerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.EventTriggerId == eventTriggerId
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<JobExecution>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting job executions for event trigger {EventTriggerId} ({ExErrorCode})",
                eventTriggerId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<JobExecution>>(
                "Error retrieving job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobExecution>>> GetByStatusAsync(
        JobExecutionStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.Status == status
                select e)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<JobExecution>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting job executions by status {Status} ({ExErrorCode})",
                status,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<JobExecution>>(
                "Error retrieving job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<JobExecution>> CreateAsync(
        JobExecution model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.JobExecutions.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var (key, value) in model.Parameters)
            {
                context.JobExecutionParameters.Add(new JobExecutionParameter
                {
                    JobExecutionId = entity.Id,
                    Key = key,
                    Value = value
                });
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(JobExecution),
                description: $"Created job execution for template {model.JobTemplateId}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var created = await (
                from e in context.JobExecutions.AsNoTracking().Include(x => x.Parameters)
                where e.Id == entity.Id
                select e)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(created.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating job execution for template {JobTemplateId} ({ExErrorCode})",
                model.JobTemplateId,
                errorCode ?? "");
            return Result.Fail<JobExecution>(
                "Error creating job execution",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        JobExecution model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.JobExecutions
                .Where(e => e.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.JobTemplateId, model.JobTemplateId)
                        .SetProperty(e => e.ScheduleTriggerId, model.ScheduleTriggerId)
                        .SetProperty(e => e.EventTriggerId, model.EventTriggerId)
                        .SetProperty(e => e.EventId, model.EventId)
                        .SetProperty(e => e.Status, model.Status)
                        .SetProperty(e => e.Progress, model.Progress)
                        .SetProperty(e => e.Message, model.Message)
                        .SetProperty(e => e.StartedAt, model.StartedAt)
                        .SetProperty(e => e.CompletedAt, model.CompletedAt)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await context.JobExecutionParameters
                    .Where(p => p.JobExecutionId == model.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var (key, value) in model.Parameters)
                {
                    context.JobExecutionParameters.Add(new JobExecutionParameter
                    {
                        JobExecutionId = model.Id,
                        Key = key,
                        Value = value
                    });
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(JobExecution),
                    description: $"Updated job execution {model.Id}",
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
                "Error updating job execution {ExecutionId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating job execution",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        JobExecutionStatus status,
        double? progress,
        string? message,
        DateTimeOffset? completedAt,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.JobExecutions
                .Where(e => e.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Status, status)
                        .SetProperty(e => e.Progress, progress)
                        .SetProperty(e => e.Message, message)
                        .SetProperty(e => e.CompletedAt, completedAt)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating job execution status {ExecutionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating job execution status",
                ErrorCodes.DbJobExecutionError,
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

            var entity = await context.JobExecutions
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.JobExecutionParameters
                .Where(p => p.JobExecutionId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.JobExecutions
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(JobExecution),
                    description: $"Deleted job execution {id}",
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
                "Error deleting job execution {ExecutionId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting job execution",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> DeleteByJobTemplateIdAsync(
        Guid jobTemplateId,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var executionIds = await (
                from e in context.JobExecutions
                where e.JobTemplateId == jobTemplateId
                select e.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.JobExecutionParameters
                .Where(p => executionIds.Contains(p.JobExecutionId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.JobExecutions
                .Where(e => e.JobTemplateId == jobTemplateId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: jobTemplateId,
                    entityType: nameof(JobExecution),
                    description: $"Deleted {deleted} job executions for job template",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting job executions for job template {JobTemplateId} ({ExErrorCode})",
                jobTemplateId,
                errorCode ?? "");
            return Result.Fail<int>(
                "Error deleting job executions",
                ErrorCodes.DbJobExecutionError,
                errorCode);
        }
    }
}
