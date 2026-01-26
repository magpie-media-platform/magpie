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

using MetadataFilterTagEntity = Magpie.Data.Database.Entities.MetadataFilterTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing metadata filters.
/// </summary>
public sealed class MetadataFilterRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<MetadataFilterRepository> logger) : IMetadataFilterRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MetadataFilter>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.MetadataFilters
                .AsNoTracking()
                .Include(f => f.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MetadataFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting metadata filters ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<MetadataFilter>>(
                "Error retrieving metadata filters",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MetadataFilter?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.MetadataFilters.AsNoTracking().Include(f => f.Tags)
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
                "Error getting metadata filter {MetadataFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<MetadataFilter?>(
                "Error retrieving metadata filter",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MetadataFilter?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from f in context.MetadataFilters.AsNoTracking().Include(f => f.Tags)
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
                "Error getting metadata filter {MetadataFilterName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<MetadataFilter?>(
                "Error retrieving metadata filter",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MetadataFilter>>> GetByTagAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = await (
                from f in context.MetadataFilters.AsNoTracking().Include(f => f.Tags)
                where f.Tags.Any(t => t.TagName == tagName)
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MetadataFilter>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting metadata filters by tag {TagName} ({ExErrorCode})",
                tagName,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<MetadataFilter>>(
                "Error retrieving metadata filters",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MetadataFilter>> CreateAsync(
        MetadataFilter model,
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
                entity.Tags.Add(new MetadataFilterTagEntity { TagName = tag });
            }

            context.MetadataFilters.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(MetadataFilter),
                description: $"Created metadata filter {model.Name}",
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
                "Error creating metadata filter {MetadataFilterName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<MetadataFilter>(
                "Error creating metadata filter",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        MetadataFilter model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.MetadataFilters
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
                var existingTags = await context.MetadataFilterTags
                    .Where(t => t.MetadataFilterId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.MetadataFilterTags
                    .Where(t => t.MetadataFilterId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.MetadataFilterTags.Add(new MetadataFilterTagEntity
                        {
                            MetadataFilterId = model.Id,
                            TagName = tag
                        });
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(MetadataFilter),
                    description: $"Updated metadata filter {model.Name}",
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
                "Error updating metadata filter {MetadataFilterId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating metadata filter",
                ErrorCodes.DbMetadataFilterError,
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

            var entity = await context.MetadataFilters
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.MetadataFilterTags
                .Where(t => t.MetadataFilterId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.MetadataFilters
                .Where(f => f.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(MetadataFilter),
                    description: $"Deleted metadata filter {entity.Name}",
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
                "Error deleting metadata filter {MetadataFilterId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting metadata filter",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        MetadataFilterPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.MetadataFilters
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
                    await context.MetadataFilterTags
                        .Where(t => t.MetadataFilterId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.MetadataFilterTags
                        .Where(t => t.MetadataFilterId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.MetadataFilterTags.Add(new MetadataFilterTagEntity
                            {
                                MetadataFilterId = patch.Id,
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
                    entityType: nameof(MetadataFilter),
                    description: $"Patched metadata filter {entity.Name}",
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
                "Error patching metadata filter {MetadataFilterId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching metadata filter",
                ErrorCodes.DbMetadataFilterError,
                errorCode);
        }
    }
}
