using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.History;
using Magpie.Models.Result;
using Magpie.Models.Wishlists;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using WishlistTagEntity = Magpie.Data.Database.Entities.WishlistTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing wishlists.
/// </summary>
public sealed class WishlistRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<WishlistRepository> logger) : IWishlistRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Wishlist>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.Wishlists
                .AsNoTracking()
                .Include(w => w.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Wishlist>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting wishlists ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Wishlist>>(
                "Error retrieving wishlists",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Wishlist?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.Id == id
                select w)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting wishlist {WishlistId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Wishlist?>(
                "Error retrieving wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Wishlist?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.Name == name
                select w)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting wishlist {WishlistName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<Wishlist?>(
                "Error retrieving wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Wishlist>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.UserId == userId
                select w)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Wishlist>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting wishlists for user {UserId} ({ExErrorCode})",
                userId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Wishlist>>(
                "Error retrieving wishlists",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Wishlist>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.Tags.Any(t => t.TagName == tagName)
                select w)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Wishlist>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting wishlists by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Wishlist>>(
                "Error retrieving wishlists",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Wishlist>>> GetByContentFilterIdAsync(
        Guid contentFilterId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.ContentFilterId == contentFilterId
                select w)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Wishlist>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting wishlists for content filter {ContentFilterId} ({ExErrorCode})",
                contentFilterId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Wishlist>>(
                "Error retrieving wishlists",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Wishlist>>> GetMonitoredAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from w in context.Wishlists.AsNoTracking().Include(w => w.Tags)
                where w.Monitored
                select w)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Wishlist>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting monitored wishlists ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Wishlist>>(
                "Error retrieving wishlists",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Wishlist>> CreateAsync(
        Wishlist model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var tag in model.Tags)
            {
                entity.Tags.Add(new WishlistTagEntity { TagName = tag });
            }

            context.Wishlists.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Wishlist),
                description: $"Created wishlist {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating wishlist {WishlistName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<Wishlist>(
                "Error creating wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Wishlist model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.Wishlists
                .Where(w => w.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.UserId, model.UserId)
                        .SetProperty(e => e.OwnerLock, model.OwnerLock)
                        .SetProperty(e => e.ContentFilterId, model.ContentFilterId)
                        .SetProperty(e => e.QualityTierListId, model.QualityTierListId)
                        .SetProperty(e => e.Monitored, model.Monitored)
                        .SetProperty(e => e.SyncProvider, model.SyncProvider)
                        .SetProperty(e => e.SyncListId, model.SyncListId)
                        .SetProperty(e => e.SyncInterval, model.SyncInterval)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingTags = await context.WishlistTags
                    .Where(t => t.WishlistId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.WishlistTags
                    .Where(t => t.WishlistId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.WishlistTags.Add(new WishlistTagEntity
                        {
                            WishlistId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(Wishlist),
                    description: $"Updated wishlist {model.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating wishlist {WishlistId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Wishlists
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.WishlistTags
                .Where(t => t.WishlistId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Wishlists
                .Where(w => w.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(Wishlist),
                    description: $"Deleted wishlist {entity.Name}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting wishlist {WishlistId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        WishlistPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Wishlists
                .Include(w => w.Tags)
                .FirstOrDefaultAsync(w => w.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value!;
            }

            if (patch.UserId is not null)
            {
                entity.UserId = patch.UserId.Value;
            }

            if (patch.OwnerLock is not null)
            {
                entity.OwnerLock = patch.OwnerLock.Value;
            }

            if (patch.ContentFilterId is not null)
            {
                entity.ContentFilterId = patch.ContentFilterId.Value;
            }

            if (patch.QualityTierListId is not null)
            {
                entity.QualityTierListId = patch.QualityTierListId.Value;
            }

            if (patch.Monitored is not null)
            {
                entity.Monitored = patch.Monitored.Value;
            }

            if (patch.SyncProvider is not null)
            {
                entity.SyncProvider = patch.SyncProvider.Value;
            }

            if (patch.SyncListId is not null)
            {
                entity.SyncListId = patch.SyncListId.Value;
            }

            if (patch.SyncInterval is not null)
            {
                entity.SyncInterval = patch.SyncInterval.Value;
            }

            if (patch.Tags is not null)
            {
                if (patch.Tags.Remove is not null)
                {
                    await context.WishlistTags
                        .Where(t => t.WishlistId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.WishlistTags
                        .Where(t => t.WishlistId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.WishlistTags.Add(new WishlistTagEntity
                            {
                                WishlistId = patch.Id,
                                TagName = tag
                            });
                        }
                    }
                }
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Patch",
                    entityId: patch.Id,
                    entityType: nameof(Wishlist),
                    description: $"Patched wishlist {entity.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching wishlist {WishlistId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching wishlist",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateLastSyncedAsync(
        Guid id,
        DateTimeOffset lastSyncedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Wishlists
                .Where(w => w.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(e => e.LastSyncedAt, lastSyncedAt),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating wishlist last synced time {WishlistId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating wishlist last synced time",
                ErrorCodes.DbWishlistError,
                errorCode);
        }
    }
}
