using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class AuditRecordRepository(
    MagpieDbContext context,
    ILogger<AuditRecordRepository> logger) : IAuditRecordRepository
{
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
            logger.LogError(ex, "Error listing audit records");
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
            logger.LogError(ex, "Error getting audit record {AuditRecordId}", id);
            throw;
        }
    }

    public async Task<PagedList<AuditRecord>> QueryAsync(
        AuditRecordQuery query,
        PagingParameter? page,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var auditRecords = context.AuditRecords.AsNoTracking().AsQueryable();
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
            if (query.UserId is not null)
            {
                auditRecords = auditRecords.Where(r => r.UserId == query.UserId);
            }

            var totalCount = await auditRecords.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                auditRecords = sort.Direction == SortDirection.Ascending
                    ? auditRecords.OrderBy(r => EF.Property<AuditRecord>(r, sort.PropertyName))
                    : auditRecords.OrderByDescending(r => EF.Property<AuditRecord>(r, sort.PropertyName));
            }
            else
            {
                auditRecords = auditRecords.OrderByDescending(r => r.Timestamp);
            }

            if (page is not null)
            {
                auditRecords = auditRecords.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);
            }

            var items = await auditRecords.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = page?.PageNumber,
                PageSize = page?.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying audit records");
            throw;
        }
    }

    public async Task<AuditRecord> CreateAsync(AuditRecord auditRecord, CancellationToken cancellationToken)
    {
        try
        {
            if (auditRecord.Timestamp == default)
            {
                auditRecord.Timestamp = DateTime.UtcNow;
            }

            context.AuditRecords.Add(auditRecord);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Audit:{AuditCategory}:{AuditOperation}:{EntityType}({ParentEntityId}/{EntityId}) " +
                "by UserId {UserId}: {AuditDetail}",
                auditRecord.Category,
                auditRecord.Operation,
                auditRecord.EntityType ?? "",
                auditRecord.ParentEntityId ?? "",
                auditRecord.EntityId ?? "",
                auditRecord.UserId?.ToString() ?? "System",
                auditRecord.Detail ?? "");

            return auditRecord;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating audit record {AuditCategory}/{AuditOperation}",
                auditRecord.Category,
                auditRecord.Operation);
            throw;
        }
    }
}
