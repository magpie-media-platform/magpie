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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

/// <summary>
/// Repository for managing wishlists.
/// </summary>
public sealed class WishlistRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<WishlistRepository> logger) : IWishlistRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<Wishlist>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing wishlists");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Wishlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting wishlist {WishlistId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wishlist>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .Where(w => w.OwnerUserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting wishlists for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Wishlist>> GetMonitoredAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.Wishlists
                .AsNoTracking()
                .Where(w => w.Enabled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting monitored wishlists");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Wishlist> CreateAsync(Wishlist model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.Wishlists.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(Wishlist),
                    Detail = $"Created wishlist {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating wishlist {WishlistName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(Wishlist model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.Wishlists
                    .Where(w => w.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(w => w.ContentFilterId, model.ContentFilterId)
                            .SetProperty(w => w.QualityTierListId, model.QualityTierListId)
                            .SetProperty(w => w.OwnerUserId, model.OwnerUserId)
                            .SetProperty(w => w.Name, model.Name)
                            .SetProperty(w => w.OwnerLock, model.OwnerLock)
                            .SetProperty(w => w.Enabled, model.Enabled)
                            .SetProperty(w => w.LastSyncedAt, model.LastSyncedAt)
                            .SetProperty(w => w.ModifiedAt, now)
                            .SetProperty(w => w.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Wishlist),
                        Detail = $"Updated wishlist {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating wishlist {WishlistId}", model.Id);
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
                var model = await context.Wishlists
                    .AsNoTracking()
                    .Where(w => w.Id == id)
                    .Select(w => new { w.Name })
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
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(Wishlist),
                        Detail = $"Deleted wishlist {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting wishlist {WishlistId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(WishlistPatch patch, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
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
                if (patch.ContentFilterId is not null)
                {
                    model.ContentFilterId = patch.ContentFilterId.Value;
                }
                if (patch.QualityTierListId is not null)
                {
                    model.QualityTierListId = patch.QualityTierListId.Value;
                }
                if (patch.OwnerLock is not null)
                {
                    model.OwnerLock = patch.OwnerLock.Value;
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

                                    await auditRecordRepository.CreateAsync(new()
                                    {
                                        Category = AuditCategories.System,
                                        Operation = "RemoveTag",
                                        EntityId = patch.Id.ToString(),
                                        EntityType = nameof(Wishlist),
                                        Detail = $"Removed tag {tagName} from wishlist {model.Name}",
                                        Timestamp = now,
                                        UserId = modifiedByUserId
                                    }, cancellationToken).ConfigureAwait(false);
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

                                await auditRecordRepository.CreateAsync(new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(Wishlist),
                                    Detail = $"Added tag {tagName} to wishlist {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                }, cancellationToken).ConfigureAwait(false);
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
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(Wishlist),
                        Detail = $"Patched wishlist {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching wishlist {WishlistId}", patch.Id);
            throw;
        }
    }
}
