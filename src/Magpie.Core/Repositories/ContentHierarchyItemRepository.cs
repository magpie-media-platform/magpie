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

internal sealed class ContentHierarchyItemRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<ContentHierarchyItemRepository> logger) : IContentHierarchyItemRepository
{
    public async Task<IEnumerable<ContentHierarchyItem>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentHierarchyItems
                .AsNoTracking()
                .Include(i => i.ContentItem)
                .Include(i => i.MetadataContainer)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing content hierarchy items");
            throw;
        }
    }

    public async Task<ContentHierarchyItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentHierarchyItems
                .AsNoTracking()
                .Include(i => i.ContentItem)
                .Include(i => i.MetadataContainer)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting content hierarchy item {ContentHierarchyItemId}", id);
            throw;
        }
    }

    public async Task<ContentHierarchyItem?> GetBySlugAsync(
        string contentType,
        string level,
        string slug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.ContentHierarchyItems
                .AsNoTracking()
                .Include(i => i.ContentItem)
                .Include(i => i.MetadataContainer)
                .FirstOrDefaultAsync(
                    i => i.ContentType == contentType && i.Level == level && i.Slug == slug,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error getting content hierarchy item by slug {ContentType}/{Level}/{Slug}",
                contentType, level, slug);
            throw;
        }
    }

    public async Task<PagedList<ContentHierarchyItem>> QueryAsync(
        ContentHierarchyItemCriteria query,
        PagingParameter? paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = context.ContentHierarchyItems
                .AsNoTracking()
                .Include(i => i.ContentItem)
                .Include(i => i.MetadataContainer)
                .AsQueryable();

            items = ApplyCriteria(items, query);

            var totalCount = await items.CountAsync(cancellationToken).ConfigureAwait(false);

            if (sort is not null)
            {
                items = sort.Direction == SortDirection.Ascending
                    ? items.OrderBy(i => EF.Property<ContentHierarchyItem>(i, sort.PropertyName))
                    : items.OrderByDescending(i => EF.Property<ContentHierarchyItem>(i, sort.PropertyName));
            }
            else
            {
                items = items.OrderBy(i => i.Name);
            }

            if (paging is not null)
            {
                items = items.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);
            }

            var result = await items.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = result,
                PageNumber = paging?.PageNumber,
                PageSize = paging?.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying content hierarchy items");
            throw;
        }
    }

    public async Task<ContentHierarchyItem> CreateAsync(
        ContentHierarchyItem model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.ContentHierarchyItems.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.Content,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(ContentHierarchyItem),
                    Detail = $"Created content hierarchy item {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating content hierarchy item {ContentHierarchyItemName}", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        ContentHierarchyItem model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;

                var updated = await context.ContentHierarchyItems
                    .Where(i => i.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(i => i.ParentId, model.ParentId)
                            .SetProperty(i => i.QualityTierListId, model.QualityTierListId)
                            .SetProperty(i => i.RootFolderId, model.RootFolderId)
                            .SetProperty(i => i.ContentType, model.ContentType)
                            .SetProperty(i => i.Level, model.Level)
                            .SetProperty(i => i.Slug, model.Slug)
                            .SetProperty(i => i.Name, model.Name)
                            .SetProperty(i => i.Monitoring, model.Monitoring)
                            .SetProperty(i => i.Path, model.Path)
                            .SetProperty(i => i.LanguagePreferences, model.LanguagePreferences)
                            .SetProperty(i => i.Settings, model.Settings)
                            .SetProperty(i => i.DateLastRefreshed, model.DateLastRefreshed)
                            .SetProperty(i => i.ModifiedAt, now)
                            .SetProperty(i => i.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Updated content hierarchy item {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating content hierarchy item {ContentHierarchyItemId}", model.Id);
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
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.ContentHierarchyItems
                    .AsNoTracking()
                    .Where(i => i.Id == id)
                    .Select(i => new { i.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.ContentHierarchyItems
                    .Where(i => i.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Deleted content hierarchy item {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting content hierarchy item {ContentHierarchyItemId}", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        ContentHierarchyItemPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.ContentHierarchyItems
                    .Include(i => i.MetadataContainer)
                    .FirstOrDefaultAsync(i => i.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null || model.MetadataContainer is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (patch.RootFolderId is not null)
                {
                    model.RootFolderId = patch.RootFolderId.Value;
                }
                if (patch.Monitoring is not null)
                {
                    model.Monitoring = patch.Monitoring.Value;
                }
                if (patch.Path is not null)
                {
                    model.Path = patch.Path.Value;
                }
                if (patch.LanguagePreferences is not null)
                {
                    var languageList = model.LanguagePreferences.ToList();

                    if (patch.LanguagePreferences.Remove is not null)
                    {
                        var indexesToRemove = patch.LanguagePreferences.Remove.OrderByDescending(i => i).ToList();
                        foreach (var index in indexesToRemove)
                        {
                            if (index >= 0 && index < languageList.Count)
                            {
                                languageList.RemoveAt(index);
                            }
                        }
                    }

                    if (patch.LanguagePreferences.Update is not null)
                    {
                        foreach (var (index, value) in patch.LanguagePreferences.Update)
                        {
                            if (index >= 0 && index < languageList.Count)
                            {
                                languageList[index] = value;
                            }
                        }
                    }

                    if (patch.LanguagePreferences.Insert is not null)
                    {
                        foreach (var (index, value) in patch.LanguagePreferences.Insert.OrderBy(kvp => kvp.Key))
                        {
                            if (index >= 0 && index <= languageList.Count)
                            {
                                languageList.Insert(index, value);
                            }
                        }
                    }

                    model.LanguagePreferences = languageList;
                }

                var tagsAdded = new List<string>();
                var tagsRemoved = new List<string>();

                if (patch.Tags is not null)
                {
                    if (patch.Tags.Add is not null)
                    {
                        foreach (var tagName in patch.Tags.Add)
                        {
                            var tag = await context.Tags
                                .Include(t => t.MetadataContainers)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is null)
                            {
                                tag = new() { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            if (!tag.MetadataContainers.Contains(model.MetadataContainer))
                            {
                                tag.MetadataContainers.Add(model.MetadataContainer);
                                tagsAdded.Add(tagName);
                            }
                        }
                    }
                    if (patch.Tags.Remove is not null)
                    {
                        foreach (var tagName in patch.Tags.Remove)
                        {
                            var tag = await context.Tags
                                .Include(t => t.MetadataContainers)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is not null && tag.MetadataContainers.Remove(model.MetadataContainer))
                            {
                                tagsRemoved.Add(tagName);
                            }
                        }
                    }
                }

                model.ModifiedAt = now;
                model.ModifiedByUserId = modifiedByUserId;

                var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.Content,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(ContentHierarchyItem),
                        Detail = $"Patched content hierarchy item {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);

                    foreach (var tagName in tagsAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "AddTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(ContentHierarchyItem),
                            Detail = $"Added tag '{tagName}' to content hierarchy item {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tagName in tagsRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "RemoveTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(ContentHierarchyItem),
                            Detail = $"Removed tag '{tagName}' from content hierarchy item {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching content hierarchy item {ContentHierarchyItemId}", patch.Id);
            throw;
        }
    }

    private static IQueryable<ContentHierarchyItem> ApplyCriteria(
        IQueryable<ContentHierarchyItem> query,
        ContentHierarchyItemCriteria criteria)
    {
        query = query.ApplyCriteriaSet(criteria.ParentId, nameof(ContentHierarchyItem.ParentId));
        query = query.ApplyCriteriaSet(criteria.RootFolderId, nameof(ContentHierarchyItem.RootFolderId));
        query = query.ApplyCriteriaSet(criteria.ContentType, nameof(ContentHierarchyItem.ContentType));
        query = query.ApplyCriteriaSet(criteria.Level, nameof(ContentHierarchyItem.Level));
        query = query.ApplyCriteriaSet(criteria.Slug, nameof(ContentHierarchyItem.Slug));
        query = query.ApplyCriteriaSet(criteria.Name, nameof(ContentHierarchyItem.Name));
        query = query.ApplyEnumCriteriaSet(criteria.Monitoring, nameof(ContentHierarchyItem.Monitoring));
        query = query.ApplyCriteriaSet(criteria.Path, nameof(ContentHierarchyItem.Path));

        return query;
    }
}
