using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Filters;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ContentFilterTagEntity = Magpie.Data.Database.Entities.ContentFilterTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing content filters.
/// </summary>
public sealed class ContentFilterRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<ContentFilterRepository> logger) : IContentFilterRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentFilter>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.ContentFilters.AsNoTracking().Include(f => f.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting content filters ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentFilter>>(
                "Error retrieving content filters",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentFilter?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.ContentFilters.AsNoTracking().Include(f => f.Tags)
                where f.Id == id
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content filter {ContentFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<ContentFilter?>(
                "Error retrieving content filter",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.ContentFilters.AsNoTracking().Include(f => f.Tags)
                where f.Name == name
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content filter {ContentFilterName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<ContentFilter?>(
                "Error retrieving content filter",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from f in context.ContentFilters.AsNoTracking().Include(f => f.Tags)
                where f.Tags.Any(t => t.TagName == tagName)
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content filters by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentFilter>>(
                "Error retrieving content filters",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContentFilter>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from f in context.ContentFilters.AsNoTracking().Include(f => f.Tags)
                where f.CreatedByUserId == userId
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<ContentFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting content filters by user {UserId} ({ExErrorCode})",
                userId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<ContentFilter>>(
                "Error retrieving content filters",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContentFilter>> CreateAsync(
        ContentFilter model,
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
                entity.Tags.Add(new ContentFilterTagEntity { TagName = tag });
            }

            context.ContentFilters.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(ContentFilter),
                description: $"Created content filter {model.Name}",
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
                "Error creating content filter {ContentFilterName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<ContentFilter>(
                "Error creating content filter",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        ContentFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.ContentFilters
                .Where(f => f.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.Criteria, JsonSerializer.Serialize(model.Criteria))
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingTags = await (
                    from t in context.ContentFilterTags
                    where t.ContentFilterId == model.Id
                    select t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.ContentFilterTags
                    .Where(t => t.ContentFilterId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.ContentFilterTags.Add(new ContentFilterTagEntity
                        {
                            ContentFilterId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(ContentFilter),
                    description: $"Updated content filter {model.Name}",
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
                "Error updating content filter {ContentFilterId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating content filter",
                ErrorCodes.DbContentFilterError,
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

            var entity = await context.ContentFilters
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.ContentFilterTags
                .Where(t => t.ContentFilterId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.ContentFilters
                .Where(f => f.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(ContentFilter),
                    description: $"Deleted content filter {entity.Name}",
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
                "Error deleting content filter {ContentFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting content filter",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        ContentFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.ContentFilters
                .Include(f => f.Tags)
                .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value!;
            }

            if (patch.Criteria is not null)
            {
                entity.Criteria = JsonSerializer.Serialize(patch.Criteria.Value);
            }

            if (patch.Tags is not null)
            {
                if (patch.Tags.Remove is not null)
                {
                    await context.ContentFilterTags
                        .Where(t => t.ContentFilterId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.ContentFilterTags
                        .Where(t => t.ContentFilterId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.ContentFilterTags.Add(new ContentFilterTagEntity
                            {
                                ContentFilterId = patch.Id,
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
                    entityType: nameof(ContentFilter),
                    description: $"Patched content filter {entity.Name}",
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
                "Error patching content filter {ContentFilterId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching content filter",
                ErrorCodes.DbContentFilterError,
                errorCode);
        }
    }
}
