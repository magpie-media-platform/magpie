using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class AuditRecordRepository(
    MagpieDbContext context,
    IEventBus eventBus,
    ILogger<AuditRecordRepository> logger,
    ITransactionManager transactionManager,
    IJobExecutionScopeFactory jobExecutionScopes,
    TimeProvider timeProvider) : IAuditRecordRepository
{
    private readonly TimeProvider _time = timeProvider;

    public async Task<IEnumerable<AuditRecord>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.AuditRecords
                .AsNoTracking()
                .OrderByDescending(r => r.Timestamp)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The audit record repository failed to list the audit records.");
            throw;
        }
    }

    public async Task<AuditRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.AuditRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The audit record repository failed to read audit record {AuditRecordId}.", id);
            throw;
        }
    }

    private static IQueryable<AuditRecord> ApplyQuery(IQueryable<AuditRecord> auditRecords, AuditRecordQuery query)
    {
        if (query.EntityId is not null)
        {
            auditRecords = auditRecords.Where(r => r.EntityId == query.EntityId);
        }

        if (query.ParentEntityId is not null)
        {
            auditRecords = auditRecords.Where(r => r.ParentEntityId == query.ParentEntityId);
        }

        if (query.EntityType is not null)
        {
            auditRecords = auditRecords.Where(r => r.EntityType == query.EntityType);
        }

        if (query.Category is not null)
        {
            auditRecords = auditRecords.Where(r => r.Category == query.Category);
        }

        if (query.Operation is not null)
        {
            auditRecords = auditRecords.Where(r => r.Operation == query.Operation);
        }

        if (query.Detail is not null)
        {
            auditRecords = auditRecords.Where(r => r.Detail != null && r.Detail.Contains(query.Detail));
        }

        if (query.Timestamp?.Min is not null)
        {
            auditRecords = auditRecords.Where(r => r.Timestamp >= query.Timestamp.Min);
        }

        if (query.Timestamp?.Max is not null)
        {
            auditRecords = auditRecords.Where(r => r.Timestamp <= query.Timestamp.Max);
        }

        if (query.TimestampBefore is { } timestampBefore)
        {
            auditRecords = auditRecords.Where(record => record.Timestamp < timestampBefore);
        }

        if (query.UserId is not null)
        {
            auditRecords = auditRecords.Where(r => r.UserId == query.UserId);
        }

        if (query.CorrelationId is not null)
        {
            auditRecords = auditRecords.Where(r => r.CorrelationId == query.CorrelationId);
        }

        if (query.JobExecutionId is not null)
        {
            auditRecords = auditRecords.Where(r => r.JobExecutionId == query.JobExecutionId);
        }

        return auditRecords;
    }

    public async Task<PagedList<AuditRecord>> QueryAsync(
        AuditRecordQuery query,
        PagingParameter page,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditRecords = ApplyQuery(context.AuditRecords.AsNoTracking(), query);

            var totalCount = await auditRecords.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(AuditRecord.Id), nameof(AuditRecord.EntityId), nameof(AuditRecord.ParentEntityId),
                nameof(AuditRecord.EntityType), nameof(AuditRecord.Category), nameof(AuditRecord.Operation),
                nameof(AuditRecord.Detail), nameof(AuditRecord.Timestamp), nameof(AuditRecord.UserId),
                nameof(AuditRecord.CorrelationId), nameof(AuditRecord.JobExecutionId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                auditRecords = sort.Direction == SortDirection.Ascending
                    ? auditRecords.OrderBy(r => EF.Property<object>(r, sortProperty)).ThenBy(r => r.Id)
                    : auditRecords.OrderByDescending(r => EF.Property<object>(r, sortProperty)).ThenBy(r => r.Id);
            }
            else
            {
                auditRecords = auditRecords.OrderByDescending(r => r.Timestamp).ThenBy(r => r.Id);
            }

            auditRecords = auditRecords.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);

            var items = await auditRecords.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The audit record repository failed to query the audit records.");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AuditRecord> CreateAsync(AuditRecord auditRecord, CancellationToken cancellationToken)
    {
        try
        {
            if (auditRecord.Timestamp == default)
            {
                auditRecord.Timestamp = _time.GetUtcNow().UtcDateTime;
            }

            if (auditRecord.JobExecutionId is null && !SubjectIsJobExecution(auditRecord))
            {
                auditRecord.JobExecutionId = jobExecutionScopes.CurrentExecutionId;
            }

            auditRecord.CorrelationId ??= jobExecutionScopes.CurrentCorrelationId;

            var entry = context.AuditRecords.Add(auditRecord);
            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }

                throw;
            }

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    AuditRecordEventBuilder.BuildAuditRecordCreatedEvent(auditRecord.Id), token),
                cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "The audit record repository recorded {AuditCategory} {AuditOperation} on {EntityType} " +
                "{EntityId}",
                auditRecord.Category,
                auditRecord.Operation,
                auditRecord.EntityType ?? "",
                auditRecord.EntityId ?? "");

            return auditRecord;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The audit record repository failed to create audit record {AuditCategory}/{AuditOperation}.",
                auditRecord.Category,
                auditRecord.Operation);
            throw;
        }
    }

    public async Task<int> DeleteWhereAsync(
        AuditRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (query == new AuditRecordQuery())
            {
                return 0;
            }

            var records = ApplyQuery(context.AuditRecords, query);

            return await records.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The audit record repository failed to delete audit records.");
            throw;
        }
    }

    private static bool SubjectIsJobExecution(AuditRecord auditRecord)
        => string.Equals(auditRecord.EntityType, nameof(JobExecution), StringComparison.Ordinal);
}
