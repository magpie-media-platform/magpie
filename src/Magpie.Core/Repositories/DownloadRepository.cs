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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing downloads.
/// </summary>
public sealed class DownloadRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<DownloadRepository> logger) : IDownloadRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Download>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Downloads
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing downloads");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Download?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Downloads
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting download {DownloadId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedList<Download>> QueryAsync(
        DownloadQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var downloads = context.Downloads.AsNoTracking().AsQueryable();

            if (query.ReleaseId is not null)
            {
                downloads = downloads.Where(d => d.ReleaseId == query.ReleaseId);
            }
            if (query.DownloadClientId is not null)
            {
                downloads = downloads.Where(d => d.DownloadClientId == query.DownloadClientId);
            }
            if (query.Status is not null)
            {
                downloads = downloads.Where(d => d.Status == query.Status);
            }
            if (query.DateCompleted?.Min is not null)
            {
                downloads = downloads.Where(d => d.DateCompleted >= query.DateCompleted.Min);
            }
            if (query.DateCompleted?.Max is not null)
            {
                downloads = downloads.Where(d => d.DateCompleted <= query.DateCompleted.Max);
            }

            var totalCount = await downloads.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                downloads = sort.Direction == SortDirection.Ascending
                    ? downloads.OrderBy(d => EF.Property<Download>(d, sort.PropertyName))
                    : downloads.OrderByDescending(d => EF.Property<Download>(d, sort.PropertyName));
            }
            if (paging is not null)
            {
                downloads = downloads.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var items = await downloads.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging?.PageNumber,
                PageSize = paging?.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying downloads");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Download> CreateAsync(
        Download model,
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

                context.Downloads.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Download),
                    Detail = $"Created download {model.Title}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating download {DownloadTitle}", model.Title);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Download model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.Downloads
                    .Where(d => d.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(d => d.ReleaseId, model.ReleaseId)
                            .SetProperty(d => d.DownloadClientId, model.DownloadClientId)
                            .SetProperty(d => d.ExternalId, model.ExternalId)
                            .SetProperty(d => d.Title, model.Title)
                            .SetProperty(d => d.Status, model.Status)
                            .SetProperty(d => d.OutputPath, model.OutputPath)
                            .SetProperty(d => d.StatusReason, model.StatusReason)
                            .SetProperty(d => d.DateCompleted, model.DateCompleted)
                            .SetProperty(d => d.ModifiedAt, now)
                            .SetProperty(d => d.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Download),
                        Detail = $"Updated download {model.Title}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating download {DownloadId}", model.Id);
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

                var model = await context.Downloads
                    .AsNoTracking()
                    .Where(d => d.Id == id)
                    .Select(d => new { d.Title })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.Downloads
                    .Where(d => d.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Download),
                        Detail = $"Deleted download {model.Title}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting download {DownloadId}", id);
            throw;
        }
    }
}
