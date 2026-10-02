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

internal sealed class ReleaseScopeRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    ILogger<ReleaseScopeRepository> logger,
    TimeProvider timeProvider) : IReleaseScopeRepository
{
    private readonly TimeProvider _time = timeProvider;

    public async Task<IEnumerable<ReleaseScope>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ReleaseScopes.AsNoTracking()
                .OrderBy(scope => scope.ReleaseId)
                .ThenBy(scope => scope.ContentHierarchyItemId)
                .ThenBy(scope => scope.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release scope repository failed to list release scopes.");
            throw;
        }
    }

    public async Task<ReleaseScope?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ReleaseScopes.AsNoTracking()
                .FirstOrDefaultAsync(scope => scope.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release scope repository failed to read scope {ReleaseScopeId}.", id);
            throw;
        }
    }

    public async Task<PagedList<ReleaseScope>> QueryAsync(
        ReleaseScopeQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = context.ReleaseScopes.AsNoTracking();
            if (query.ReleaseId is { } releaseId)
            {
                filtered = filtered.Where(scope => scope.ReleaseId == releaseId);
            }

            if (query.ReleaseIds is { Count: > 0 })
            {
                filtered = filtered.Where(scope => query.ReleaseIds.Contains(scope.ReleaseId));
            }

            if (query.ContentHierarchyItemId is { } contentHierarchyItemId)
            {
                filtered = filtered.Where(scope => scope.ContentHierarchyItemId == contentHierarchyItemId);
            }

            var totalItems = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
            var results = filtered
                .OrderBy(scope => scope.ReleaseId)
                .ThenBy(scope => scope.ContentHierarchyItemId)
                .ThenBy(scope => scope.Id);
            var sortProperty = context.Model.FindEntityType(typeof(ReleaseScope))!.GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                results = sort.Direction == SortDirection.Ascending
                    ? filtered.OrderBy(scope => EF.Property<object>(scope, sortProperty)).ThenBy(scope => scope.Id)
                    : filtered.OrderByDescending(scope => EF.Property<object>(scope, sortProperty))
                        .ThenBy(scope => scope.Id);
            }

            var page = results.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await page.ToListAsync(cancellationToken).ConfigureAwait(false),
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release scope repository failed to query release scopes.");
            throw;
        }
    }

    public async Task<ReleaseScope> CreateAsync(
        ReleaseScope model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    context.ReleaseScopes.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync("Create", model, createdByUserId, cancellationToken).ConfigureAwait(false);
                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release scope repository failed to create a release scope.");
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ReleaseScope model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var updated = await context.ReleaseScopes.Where(scope => scope.Id == model.Id)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(scope => scope.ReleaseId, model.ReleaseId)
                                .SetProperty(scope => scope.ContentHierarchyItemId, model.ContentHierarchyItemId)
                                .SetProperty(scope => scope.Status, model.Status),
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
            logger.LogError(ex, "The release scope repository failed to update scope {ReleaseScopeId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.ReleaseScopes.AsNoTracking()
                        .FirstOrDefaultAsync(scope => scope.Id == id, cancellationToken)
                        .ConfigureAwait(false);
                    if (model is null)
                    {
                        return false;
                    }

                    await context.ReleaseScopes.Where(scope => scope.Id == id)
                        .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                    await WriteAuditAsync("Delete", model, deletedByUserId, cancellationToken).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The release scope repository failed to delete scope {ReleaseScopeId}.", id);
            throw;
        }
    }

    private Task<AuditRecord> WriteAuditAsync(
        string operation,
        ReleaseScope model,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        return auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.Integrations,
                Operation = operation,
                EntityId = model.Id.ToString(),
                EntityType = nameof(ReleaseScope),
                Detail = $"{operation}d release scope {model.Id}.",
                Timestamp = _time.GetUtcNow().UtcDateTime,
                UserId = userId
            },
            cancellationToken);
    }
}
