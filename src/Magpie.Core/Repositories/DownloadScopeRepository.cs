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

internal sealed class DownloadScopeRepository(
    MagpieDbContext context,
    ITransactionManager transactionManager,
    IAuditRecordRepository auditRecordRepository,
    ILogger<DownloadScopeRepository> logger,
    TimeProvider timeProvider) : IDownloadScopeRepository
{
    private readonly TimeProvider _time = timeProvider;

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
            var results = filtered
                .OrderBy(scope => scope.DownloadId)
                .ThenBy(scope => scope.ContentHierarchyItemId)
                .ThenBy(scope => scope.Id);
            var sortProperty = context.Model.FindEntityType(typeof(DownloadScope))!.GetProperties()
                .Where(property => !property.IsShadowProperty() &&
                    (property.ClrType.IsValueType || property.ClrType == typeof(string) ||
                        property.ClrType == typeof(Uri)))
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
                    await RecomputeAcquisitionStatusAsync(
                        [model.ContentHierarchyItemId],
                        createdByUserId,
                        cancellationToken)
                        .ConfigureAwait(false);
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
                    var priorHierarchyItemId = await context.DownloadScopes.AsNoTracking()
                        .Where(scope => scope.Id == model.Id)
                        .Select(scope => (Guid?)scope.ContentHierarchyItemId)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    if (priorHierarchyItemId is null)
                    {
                        return false;
                    }

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
                        await RecomputeAcquisitionStatusAsync(
                            [priorHierarchyItemId.Value, model.ContentHierarchyItemId],
                            modifiedByUserId,
                            cancellationToken)
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

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
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
                    await RecomputeAcquisitionStatusAsync(
                        [model.ContentHierarchyItemId],
                        deletedByUserId,
                        cancellationToken)
                        .ConfigureAwait(false);
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

    private async Task RecomputeAcquisitionStatusAsync(
        IReadOnlyCollection<Guid> hierarchyItemIds,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var contentItemIds = await context.ContentItems.AsNoTracking()
            .Where(item => hierarchyItemIds.Contains(item.ContentHierarchyItemId))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await RecomputeActiveAcquisitionStatusAsync(
            contentItemIds,
            _time.GetUtcNow().UtcDateTime,
            userId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RecomputeActiveAcquisitionStatusAsync(
        IReadOnlyCollection<Guid> contentItemIds,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (contentItemIds.Count == 0)
        {
            return;
        }

        var distinctIds = contentItemIds.Distinct().ToList();

        var inFlight = await (
            from d in context.Downloads.AsNoTracking()
            join scope in context.DownloadScopes.AsNoTracking() on d.Id equals scope.DownloadId
            join ci in context.ContentItems.AsNoTracking()
                on scope.ContentHierarchyItemId equals ci.ContentHierarchyItemId
            where distinctIds.Contains(ci.Id) &&
                !DownloadStatuses.Finished.Contains(d.Status)
            select new { ContentItemId = ci.Id, d.Status, Anchor = d.ModifiedAt ?? d.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statusByContentItem = inFlight
            .GroupBy(x => x.ContentItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Anchor).First().Status);

        foreach (var contentItemId in distinctIds)
        {
            var next = statusByContentItem.TryGetValue(contentItemId, out var status)
                ? (DownloadStatus?)status
                : null;
            await context.ContentItems
                .Where(ci => ci.Id == contentItemId && ci.ActiveAcquisitionStatus != next)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(ci => ci.ActiveAcquisitionStatus, next)
                        .SetProperty(ci => ci.ModifiedAt, now)
                        .SetProperty(ci => ci.ModifiedByUserId, userId),
                    cancellationToken)
                .ConfigureAwait(false);
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
