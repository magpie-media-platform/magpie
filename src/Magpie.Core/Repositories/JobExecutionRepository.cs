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
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Sorting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class JobExecutionRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<JobExecutionRepository> logger) : IJobExecutionRepository
{
    public async Task<IEnumerable<JobExecution>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.JobExecutions
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing job executions");
            throw;
        }
    }

    public async Task<JobExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.JobExecutions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting job execution {ExecutionId}", id);
            throw;
        }
    }

    public async Task<PagedList<JobExecution>> QueryAsync(
        JobExecutionQuery query,
        PagingParameter? page,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var jobExecutions = context.JobExecutions.AsNoTracking().AsQueryable();

            if (query.ScheduleTriggerId is not null)
            {
                jobExecutions = jobExecutions.Where(e => e.ScheduleTriggerId == query.ScheduleTriggerId);
            }
            if (query.EventTriggerId is not null)
            {
                jobExecutions = jobExecutions.Where(e => e.EventTriggerId == query.EventTriggerId);
            }
            if (query.Status is not null)
            {
                jobExecutions = jobExecutions.Where(e => e.Status == query.Status);
            }
            if (query.Timestamp?.Min is not null)
            {
                jobExecutions = jobExecutions.Where(e => e.CreatedAt >= query.Timestamp.Min);
            }
            if (query.Timestamp?.Max is not null)
            {
                jobExecutions = jobExecutions.Where(e => e.CreatedAt <= query.Timestamp.Max);
            }

            var totalCount = await jobExecutions.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                jobExecutions = sort.Direction == SortDirection.Ascending
                    ? jobExecutions.OrderBy(e => EF.Property<JobExecution>(e, sort.PropertyName))
                    : jobExecutions.OrderByDescending(e => EF.Property<JobExecution>(e, sort.PropertyName));
            }
            else
            {
                jobExecutions = jobExecutions.OrderByDescending(e => e.CreatedAt);
            }

            if (page is not null)
            {
                jobExecutions = jobExecutions.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);
            }

            var items = await jobExecutions.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, page?.PageNumber, page?.PageSize, totalCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying job executions");
            throw;
        }
    }

    public async Task<JobExecution> CreateAsync(
        JobExecution model,
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

                context.JobExecutions.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Jobs,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(JobExecution),
                    Detail = $"Created job execution for {model.JobType}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating job execution for {JobType}", model.JobType);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(JobExecution model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.JobExecutions
                    .Where(e => e.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(e => e.ScheduleTriggerId, model.ScheduleTriggerId)
                            .SetProperty(e => e.EventTriggerId, model.EventTriggerId)
                            .SetProperty(e => e.ExtensionId, model.ExtensionId)
                            .SetProperty(e => e.JobType, model.JobType)
                            .SetProperty(e => e.Scope, model.Scope)
                            .SetProperty(e => e.TriggerContext, model.TriggerContext)
                            .SetProperty(e => e.Parameters, model.Parameters)
                            .SetProperty(e => e.Progress, model.Progress)
                            .SetProperty(e => e.Status, model.Status)
                            .SetProperty(e => e.StatusMessage, model.StatusMessage)
                            .SetProperty(e => e.NodeId, model.NodeId)
                            .SetProperty(e => e.LastHeartbeat, model.LastHeartbeat)
                            .SetProperty(e => e.TimeoutAt, model.TimeoutAt)
                            .SetProperty(e => e.StartedAt, model.StartedAt)
                            .SetProperty(e => e.CompletedAt, model.CompletedAt)
                            .SetProperty(e => e.ModifiedAt, now),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Jobs,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(JobExecution),
                        Detail = $"Updated job execution {model.Id}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating job execution {ExecutionId}", model.Id);
            throw;
        }
    }

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

                var deleted = await context.JobExecutions
                    .Where(e => e.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Jobs,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(JobExecution),
                        Detail = $"Deleted job execution {id}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting job execution {ExecutionId}", id);
            throw;
        }
    }

    public async Task<JobExecution?> TryDequeueAsync(string nodeId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var candidateId = await (
                    from e in context.JobExecutions
                    where e.Status == JobExecutionStatus.Queued && e.NodeId == null
                    orderby e.CreatedAt
                    select e.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (candidateId == Guid.Empty)
                {
                    return null;
                }

                var updated = await context.JobExecutions
                    .Where(e => e.Id == candidateId && e.NodeId == null)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(e => e.NodeId, nodeId)
                            .SetProperty(e => e.LastHeartbeat, now)
                            .SetProperty(e => e.ModifiedAt, now),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated == 0)
                {
                    return null;
                }

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Jobs,
                    Operation = "Dequeue",
                    EntityId = candidateId.ToString(),
                    EntityType = nameof(JobExecution),
                    Detail = $"Job execution {candidateId} claimed by node {nodeId}",
                    Timestamp = now
                }, cancellationToken).ConfigureAwait(false);

                return await context.JobExecutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == candidateId, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dequeuing job execution for node {NodeId}", nodeId);
            throw;
        }
    }
}
