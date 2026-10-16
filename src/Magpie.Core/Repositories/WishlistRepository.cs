using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class WishlistRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<WishlistRepository> logger,
    TimeProvider timeProvider) : IWishlistRepository
{
    private static readonly (string Name, Func<Wishlist, Wishlist, bool> Changed)[] UpdateFields =
    [
        (nameof(Wishlist.ContentFilterId), (current, model) =>
            !Equals(current.ContentFilterId, model.ContentFilterId)),
        (nameof(Wishlist.QualityTierListId), (current, model) =>
            !Equals(current.QualityTierListId, model.QualityTierListId)),
        (nameof(Wishlist.RootFolderId), (current, model) => !Equals(current.RootFolderId, model.RootFolderId)),
        (nameof(Wishlist.OwnerUserId), (current, model) => !Equals(current.OwnerUserId, model.OwnerUserId)),
        (nameof(Wishlist.Name), (current, model) => !Equals(current.Name, model.Name)),
        (nameof(Wishlist.OwnerLock), (current, model) => !Equals(current.OwnerLock, model.OwnerLock)),
        (nameof(Wishlist.Enabled), (current, model) => !Equals(current.Enabled, model.Enabled)),
        (nameof(Wishlist.Monitoring), (current, model) => !Equals(current.Monitoring, model.Monitoring)),
        (nameof(Wishlist.LastSyncedAt), (current, model) => !Equals(current.LastSyncedAt, model.LastSyncedAt)),
    ];

    private readonly TimeProvider _time = timeProvider;

    private static IReadOnlyCollection<string> ChangedFields(WishlistPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add(nameof(Wishlist.Name));
        }

        if (patch.ContentFilterId is not null)
        {
            fields.Add(nameof(Wishlist.ContentFilterId));
        }

        if (patch.QualityTierListId is not null)
        {
            fields.Add(nameof(Wishlist.QualityTierListId));
        }

        if (patch.RootFolderId is not null)
        {
            fields.Add(nameof(Wishlist.RootFolderId));
        }

        if (patch.OwnerLock is not null)
        {
            fields.Add(nameof(Wishlist.OwnerLock));
        }

        if (patch.Enabled is not null)
        {
            fields.Add(nameof(Wishlist.Enabled));
        }

        if (patch.Monitoring is not null)
        {
            fields.Add(nameof(Wishlist.Monitoring));
        }

        if (patch.LastSyncedAt is not null)
        {
            fields.Add(nameof(Wishlist.LastSyncedAt));
        }

        if (patch.Tags is not null)
        {
            fields.Add(nameof(Wishlist.Tags));
        }

        return fields;
    }

    public async Task<IEnumerable<Wishlist>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .Include(w => w.ContentFilter)
                .Include(w => w.QualityTierList)
                .Include(w => w.RootFolder)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to list wishlists.");
            throw;
        }
    }

    public async Task<Wishlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .Include(w => w.ContentFilter)
                .Include(w => w.QualityTierList)
                .Include(w => w.RootFolder)
                .Include(w => w.Tags)
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to read wishlist {WishlistId}.", id);
            throw;
        }
    }

    public async Task<PagedList<Wishlist>> QueryAsync(
        WishlistQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var wishlists = context.Wishlists
                .AsNoTracking()
                .Include(w => w.ContentFilter)
                .Include(w => w.QualityTierList)
                .Include(w => w.RootFolder)
                .Include(w => w.Tags)
                .AsQueryable();

            if (query.OwnerUserId is not null)
            {
                wishlists = wishlists.Where(w => w.OwnerUserId == query.OwnerUserId);
            }

            if (query.Monitored is not null)
            {
                wishlists = wishlists.Where(w => w.Enabled == query.Monitored);
            }

            var totalCount = await wishlists.CountAsync(cancellationToken).ConfigureAwait(false);

            var sortableProperties = new[]
            {
                nameof(Wishlist.Id), nameof(Wishlist.ContentFilterId), nameof(Wishlist.QualityTierListId),
                nameof(Wishlist.RootFolderId), nameof(Wishlist.OwnerUserId), nameof(Wishlist.Name),
                nameof(Wishlist.OwnerLock), nameof(Wishlist.Enabled), nameof(Wishlist.Monitoring),
                nameof(Wishlist.LastSyncedAt), nameof(Wishlist.CreatedAt), nameof(Wishlist.CreatedByUserId),
                nameof(Wishlist.ModifiedAt), nameof(Wishlist.ModifiedByUserId),
            };
            var sortProperty = sortableProperties
                .FirstOrDefault(name => string.Equals(name, sort?.PropertyName, StringComparison.OrdinalIgnoreCase));
            if (sort is not null && sortProperty is not null)
            {
                wishlists = sort.Direction == SortDirection.Ascending
                    ? wishlists.OrderBy(w => EF.Property<object>(w, sortProperty)).ThenBy(w => w.Id)
                    : wishlists.OrderByDescending(w => EF.Property<object>(w, sortProperty)).ThenBy(w => w.Id);
            }
            else
            {
                wishlists = wishlists.OrderBy(w => w.Name).ThenBy(w => w.Id);
            }

            wishlists = wishlists.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var items = await wishlists.ToListAsync(cancellationToken).ConfigureAwait(false);

            return new()
            {
                Items = items,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalCount
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to query wishlists.");
            throw;
        }
    }

    public async Task<Wishlist> CreateAsync(Wishlist model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    await CreateCoreAsync(model, createdByUserId, now, cancellationToken).ConfigureAwait(false);
                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(WishlistEventBuilder.BuildWishlistCreatedEvent(created.Id), token),
                cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to create wishlist {WishlistName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Wishlist model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.Wishlists.AsNoTracking()
                        .FirstOrDefaultAsync(entity => entity.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    var changedFields = new List<string>();
                    foreach (var (name, changed) in UpdateFields)
                    {
                        if (changed(current, model))
                        {
                            changedFields.Add(name);
                        }
                    }

                    var updated = await context.Wishlists
                        .Where(w => w.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(w => w.ContentFilterId, model.ContentFilterId)
                                .SetProperty(w => w.QualityTierListId, model.QualityTierListId)
                                .SetProperty(w => w.RootFolderId, model.RootFolderId)
                                .SetProperty(w => w.OwnerUserId, model.OwnerUserId)
                                .SetProperty(w => w.Name, model.Name)
                                .SetProperty(w => w.OwnerLock, model.OwnerLock)
                                .SetProperty(w => w.Enabled, model.Enabled)
                                .SetProperty(w => w.Monitoring, model.Monitoring)
                                .SetProperty(w => w.LastSyncedAt, model.LastSyncedAt)
                                .SetProperty(w => w.ModifiedAt, now)
                                .SetProperty(w => w.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(Wishlist),
                                Detail = $"Updated wishlist {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (updated > 0 && changedFields.Count > 0)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                WishlistEventBuilder.BuildWishlistUpdatedEvent(model.Id, changedFields),
                                token),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to update wishlist {WishlistId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from w in context.Wishlists.AsNoTracking()
                        where w.Id == id
                        select new { w.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.Wishlists
                        .Where(w => w.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(Wishlist),
                                Detail = $"Deleted wishlist {model.Name}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(WishlistEventBuilder.BuildWishlistDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to delete wishlist {WishlistId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(WishlistPatch patch, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await context.Wishlists
                        .FirstOrDefaultAsync(w => w.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.ContentFilterId?.Value is { } filterId)
                    {
                        model.ContentFilterId = filterId;
                    }

                    if (patch.QualityTierListId is not null)
                    {
                        model.QualityTierListId = patch.QualityTierListId.Value;
                    }

                    if (patch.RootFolderId is not null)
                    {
                        model.RootFolderId = patch.RootFolderId.Value;
                    }

                    if (patch.OwnerLock is not null)
                    {
                        model.OwnerLock = patch.OwnerLock.Value;
                    }

                    if (patch.Enabled is not null)
                    {
                        model.Enabled = patch.Enabled.Value;
                    }

                    if (patch.Monitoring is not null)
                    {
                        model.Monitoring = patch.Monitoring.Value;
                    }

                    if (patch.LastSyncedAt is not null)
                    {
                        model.LastSyncedAt = patch.LastSyncedAt.Value;
                    }

                    if (patch.Tags is not null)
                    {
                        var tags = await context.Tags
                            .Include(t => t.Wishlists)
                            .Where(t => t.Wishlists.Any(w => w.Id == patch.Id))
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);

                        if (patch.Tags.Remove is not null)
                        {
                            foreach (var tagName in patch.Tags.Remove)
                            {
                                var tag = tags.FirstOrDefault(t => t.Name == tagName);
                                if (tag is not null)
                                {
                                    var wishlist = tag.Wishlists.FirstOrDefault(w => w.Id == patch.Id);
                                    if (wishlist is not null)
                                    {
                                        tag.Wishlists.Remove(wishlist);

                                        await auditRecordRepository.CreateAsync(
                                            new()
                                            {
                                                Category = AuditCategories.System,
                                                Operation = "RemoveTag",
                                                EntityId = patch.Id.ToString(),
                                                EntityType = nameof(Wishlist),
                                                Detail = $"Removed tag {tagName} from wishlist {model.Name}",
                                                Timestamp = now,
                                                UserId = modifiedByUserId
                                            },
                                            cancellationToken).ConfigureAwait(false);
                                    }
                                }
                            }
                        }

                        if (patch.Tags.Add is not null)
                        {
                            var existingTagNames = tags.Select(t => t.Name).ToHashSet();
                            foreach (var tagName in patch.Tags.Add)
                            {
                                if (!existingTagNames.Contains(tagName))
                                {
                                    var tag = await context.Tags
                                        .Include(t => t.Wishlists)
                                        .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                        .ConfigureAwait(false);

                                    if (tag is null)
                                    {
                                        tag = new Tag { Name = tagName };
                                        context.Tags.Add(tag);
                                    }

                                    tag.Wishlists.Add(model);

                                    await auditRecordRepository.CreateAsync(
                                        new()
                                        {
                                            Category = AuditCategories.System,
                                            Operation = "AddTag",
                                            EntityId = patch.Id.ToString(),
                                            EntityType = nameof(Wishlist),
                                            Detail = $"Added tag {tagName} to wishlist {model.Name}",
                                            Timestamp = now,
                                            UserId = modifiedByUserId
                                        },
                                        cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(Wishlist),
                                Detail = $"Updated wishlist {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        WishlistEventBuilder.BuildWishlistUpdatedEvent(patch.Id, ChangedFields(patch)),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The wishlist repository failed to patch wishlist {WishlistId}.", patch.Id);
            throw;
        }
    }

    private async Task CreateCoreAsync(
        Wishlist model,
        Guid? createdByUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        model.CreatedAt = now;
        model.CreatedByUserId = createdByUserId;

        context.Wishlists.Add(model);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.System,
                Operation = "Create",
                EntityId = model.Id.ToString(),
                EntityType = nameof(Wishlist),
                Detail = $"Created wishlist {model.Name}",
                Timestamp = now,
                UserId = createdByUserId
            },
            cancellationToken).ConfigureAwait(false);
    }
}
