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
using Magpie.Models.Paging;
using Magpie.Models.Sorting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing releases.
/// </summary>
public sealed class ReleaseRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ReleaseRepository> logger) : IReleaseRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Release>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Releases
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing releases");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Release?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Releases
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting release {ReleaseId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedList<Release>> QueryAsync(
        ReleaseQuery query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var releases = context.Releases.AsNoTracking().AsQueryable();

            if (query.IndexerId is not null)
            {
                releases = releases.Where(r => r.IndexerId == query.IndexerId);
            }
            if (query.ContentItemId is not null)
            {
                releases = releases.Where(r => r.ContentItemId == query.ContentItemId);
            }
            if (query.Title is not null)
            {
                releases = releases.Where(r => r.Title.Contains(query.Title));
            }
            if (query.Category is not null)
            {
                releases = releases.Where(r => r.Categories.Contains(query.Category));
            }
            if (query.ExternalId is not null)
            {
                releases = releases.Where(r => r.ExternalId == query.ExternalId);
            }
            if (query.PublishDate?.Min is not null)
            {
                releases = releases.Where(r => r.PublishDate >= query.PublishDate.Min);
            }
            if (query.PublishDate?.Max is not null)
            {
                releases = releases.Where(r => r.PublishDate <= query.PublishDate.Max);
            }
            if (query.Tags is not null && query.Tags.Any())
            {
                releases = releases.Where(r => context.Tags
                    .Where(t => query.Tags.Contains(t.Name))
                    .SelectMany(t => t.Releases)
                    .Any(tr => tr.Id == r.Id));
            }

            var totalCount = await releases.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                releases = sort.Direction == SortDirection.Ascending
                    ? releases.OrderBy(r => EF.Property<Release>(r, sort.PropertyName))
                    : releases.OrderByDescending(r => EF.Property<Release>(r, sort.PropertyName));
            }
            else
            {
                releases = releases.OrderByDescending(r => r.PublishDate);
            }

            if (paging is not null)
            {
                releases = releases.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var items = await releases.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new(items, paging?.PageNumber, paging?.PageSize, totalCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying releases");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Release> CreateAsync(Release model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.Releases.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Integrations,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Release),
                    Detail = $"Created release {model.Title}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating release {ReleaseTitle}", model.Title);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Release model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.Releases
                    .Where(r => r.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(r => r.IndexerId, model.IndexerId)
                            .SetProperty(r => r.ContentItemId, model.ContentItemId)
                            .SetProperty(r => r.Protocol, model.Protocol)
                            .SetProperty(r => r.ExternalId, model.ExternalId)
                            .SetProperty(r => r.Title, model.Title)
                            .SetProperty(r => r.PublishDate, model.PublishDate)
                            .SetProperty(r => r.Size, model.Size)
                            .SetProperty(r => r.InfoUrl, model.InfoUrl)
                            .SetProperty(r => r.Categories, model.Categories)
                            .SetProperty(r => r.Languages, model.Languages)
                            .SetProperty(r => r.Properties, model.Properties)
                            .SetProperty(r => r.ModifiedAt, now)
                            .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Release),
                        Detail = $"Updated release {model.Title}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating release {ReleaseId}", model.Id);
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
                var model = await context.Releases
                    .AsNoTracking()
                    .Where(r => r.Id == id)
                    .Select(r => new { r.Title })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.Releases
                    .Where(r => r.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Integrations,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Release),
                        Detail = $"Deleted release {model.Title}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting release {ReleaseId}", id);
            throw;
        }
    }
}
