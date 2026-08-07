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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class DownloadScopeRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    ILogger<DownloadScopeRepository> logger,
    TimeProvider? timeProvider = null) : IDownloadScopeRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<IEnumerable<DownloadScope>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.DownloadScopes.AsNoTracking()
                .OrderBy(scope => scope.DownloadId)
                .ThenBy(scope => scope.ContentHierarchyItemId)
                .ThenBy(scope => scope.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to list download scopes.");
            throw;
        }
    }

    public async Task<DownloadScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.DownloadScopes.AsNoTracking()
                .FirstOrDefaultAsync(scope => scope.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to read scope {DownloadScopeId}.", id);
            throw;
        }
    }

    public async Task<PagedList<DownloadScope>> QueryAsync(
        DownloadScopeQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = context.DownloadScopes.AsNoTracking();
            if (query.DownloadId is { } downloadId)
            {
                filtered = filtered.Where(scope => scope.DownloadId == downloadId);
            }

            if (query.ContentHierarchyItemId is { } contentHierarchyItemId)
            {
                filtered = filtered.Where(scope => scope.ContentHierarchyItemId == contentHierarchyItemId);
            }

            var totalItems = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
            IQueryable<DownloadScope> results = filtered
                .OrderBy(scope => scope.DownloadId)
                .ThenBy(scope => scope.ContentHierarchyItemId)
                .ThenBy(scope => scope.Id);
            results = results.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await results.ToListAsync(cancellationToken).ConfigureAwait(false),
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to query download scopes.");
            throw;
        }
    }

    public async Task<DownloadScope> CreateAsync(
        DownloadScope model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    context.DownloadScopes.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync("Create", model, createdByUserId, cancellationToken).ConfigureAwait(false);
                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to create a download scope.");
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        DownloadScope model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var updated = await context.DownloadScopes.Where(scope => scope.Id == model.Id)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(scope => scope.DownloadId, model.DownloadId)
                                .SetProperty(scope => scope.ContentHierarchyItemId, model.ContentHierarchyItemId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await WriteAuditAsync("Update", model, modifiedByUserId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to update scope {DownloadScopeId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.DownloadScopes.AsNoTracking()
                        .FirstOrDefaultAsync(scope => scope.Id == id, cancellationToken)
                        .ConfigureAwait(false);
                    if (model is null)
                    {
                        return false;
                    }

                    await context.DownloadScopes.Where(scope => scope.Id == id)
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync("Delete", model, deletedByUserId, cancellationToken).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The download scope repository failed to delete scope {DownloadScopeId}.", id);
            throw;
        }
    }

    private Task<AuditRecord> WriteAuditAsync(
        string operation,
        DownloadScope model,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.Integrations,
                Operation = operation,
                EntityId = model.Id.ToString(),
                EntityType = nameof(DownloadScope),
                Detail = $"{operation}d download scope {model.Id}.",
                Timestamp = _time.GetUtcNow().UtcDateTime,
                UserId = userId
            },
            cancellationToken);
    }
}
