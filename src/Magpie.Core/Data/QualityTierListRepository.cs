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
using Magpie.Models.QualityTiers;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using QualityTierListTag = Magpie.Data.Database.Entities.QualityTierListTag;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing quality tier lists.
/// </summary>
public sealed class QualityTierListRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<QualityTierListRepository> logger) : IQualityTierListRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<QualityTierList>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.QualityTierLists
                .AsNoTracking()
                .Include(q => q.Tags)
                .Include(q => q.Tiers)
                    .ThenInclude(t => t.Criteria)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(e => e.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<QualityTierList>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting quality tier lists ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<QualityTierList>>(
                "Error retrieving quality tier lists",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<QualityTierList?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from q in context.QualityTierLists
                    .AsNoTracking()
                    .Include(q => q.Tags)
                    .Include(q => q.Tiers)
                        .ThenInclude(t => t.Criteria)
                where q.Id == id
                select q)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting quality tier list {QualityTierListId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<QualityTierList?>(
                "Error retrieving quality tier list",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<QualityTierList?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from q in context.QualityTierLists
                    .AsNoTracking()
                    .Include(q => q.Tags)
                    .Include(q => q.Tiers)
                        .ThenInclude(t => t.Criteria)
                where q.Name == name
                select q)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting quality tier list {QualityTierListName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<QualityTierList?>(
                "Error retrieving quality tier list",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<QualityTierList>> CreateAsync(
        QualityTierList model,
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
                entity.Tags.Add(new QualityTierListTag { TagName = tag });
            }

            context.QualityTierLists.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var tier in model.Tiers)
            {
                var tierEntity = tier.CreateEntity(entity.Id);
                entity.Tiers.Add(tierEntity);
                context.QualityTiers.Add(tierEntity);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                foreach (var criterion in tier.Criteria)
                {
                    var criterionEntity = criterion.CreateEntity(tierEntity.Id);
                    tierEntity.Criteria.Add(criterionEntity);
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(QualityTierList),
                description: $"Created quality tier list {model.Name}",
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
                "Error creating quality tier list {QualityTierListName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<QualityTierList>(
                "Error creating quality tier list",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        QualityTierList model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.QualityTierLists
                .Where(q => q.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.UpgradeAllowed, model.UpgradeAllowed)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                var existingTags = await context.QualityTierListTags
                    .Where(t => t.QualityTierListId == model.Id)
                    .Select(t => t.TagName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                await context.QualityTierListTags
                    .Where(t => t.QualityTierListId == model.Id)
                    .Where(t => !model.Tags.Contains(t.TagName))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tag in model.Tags)
                {
                    if (!existingTags.Contains(tag))
                    {
                        context.QualityTierListTags.Add(new QualityTierListTag
                        {
                            QualityTierListId = model.Id,
                            TagName = tag
                        });
                    }
                }
                var existingTierIds = await context.QualityTiers
                    .Where(t => t.QualityTierListId == model.Id)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tierId in existingTierIds)
                {
                    await context.QualityCriteria
                        .Where(c => c.QualityTierId == tierId)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                await context.QualityTiers
                    .Where(t => t.QualityTierListId == model.Id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var tier in model.Tiers)
                {
                    var tierEntity = tier.CreateEntity(model.Id);
                    context.QualityTiers.Add(tierEntity);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var criterion in tier.Criteria)
                    {
                        var criterionEntity = criterion.CreateEntity(tierEntity.Id);
                        context.QualityCriteria.Add(criterionEntity);
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(QualityTierList),
                    description: $"Updated quality tier list {model.Name}",
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
                "Error updating quality tier list {QualityTierListId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating quality tier list",
                ErrorCodes.DbQualityTierListError,
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

            var entity = await context.QualityTierLists
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var tierIds = await context.QualityTiers
                .Where(t => t.QualityTierListId == id)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var tierId in tierIds)
            {
                await context.QualityCriteria
                    .Where(c => c.QualityTierId == tierId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            await context.QualityTiers
                .Where(t => t.QualityTierListId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.QualityTierListTags
                .Where(t => t.QualityTierListId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.QualityTierLists
                .Where(q => q.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: id,
                    entityType: nameof(QualityTierList),
                    description: $"Deleted quality tier list {entity.Name}",
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
                "Error deleting quality tier list {QualityTierListId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error deleting quality tier list",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        QualityTierListPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.QualityTierLists
                .Include(q => q.Tags)
                .Include(q => q.Tiers)
                    .ThenInclude(t => t.Criteria)
                .FirstOrDefaultAsync(q => q.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value!;
            }

            if (patch.UpgradeAllowed is not null)
            {
                entity.UpgradeAllowed = patch.UpgradeAllowed.Value;
            }

            if (patch.Tags is not null)
            {
                if (patch.Tags.Remove is not null)
                {
                    await context.QualityTierListTags
                        .Where(t => t.QualityTierListId == patch.Id)
                        .Where(t => patch.Tags.Remove.Contains(t.TagName))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (patch.Tags.Add is not null)
                {
                    var existingTags = await context.QualityTierListTags
                        .Where(t => t.QualityTierListId == patch.Id)
                        .Select(t => t.TagName)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var tag in patch.Tags.Add)
                    {
                        if (!existingTags.Contains(tag))
                        {
                            context.QualityTierListTags.Add(new QualityTierListTag
                            {
                                QualityTierListId = patch.Id,
                                TagName = tag
                            });
                        }
                    }
                }
            }

            if (patch.Tiers is not null)
            {
                if (patch.Tiers.Remove is not null)
                {
                    foreach (var tierId in patch.Tiers.Remove)
                    {
                        await context.QualityCriteria
                            .Where(c => c.QualityTierId == tierId)
                            .ExecuteDeleteAsync(cancellationToken)
                            .ConfigureAwait(false);

                        await context.QualityTiers
                            .Where(t => t.Id == tierId)
                            .Where(t => t.QualityTierListId == patch.Id)
                            .ExecuteDeleteAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (patch.Tiers.Update is not null)
                {
                    foreach (var tierPatch in patch.Tiers.Update)
                    {
                        var tierEntity = entity.Tiers.FirstOrDefault(t => t.Id == tierPatch.Id);
                        if (tierEntity is not null)
                        {
                            if (tierPatch.Name is not null)
                            {
                                tierEntity.Name = tierPatch.Name.Value!;
                            }

                            if (tierPatch.Ordinal is not null)
                            {
                                tierEntity.Ordinal = tierPatch.Ordinal.Value;
                            }

                            if (tierPatch.IsCutoff is not null)
                            {
                                tierEntity.IsCutoff = tierPatch.IsCutoff.Value;
                            }

                            if (tierPatch.Criteria is not null)
                            {
                                if (tierPatch.Criteria.Remove is not null)
                                {
                                    await context.QualityCriteria
                                        .Where(c => tierPatch.Criteria.Remove.Contains(c.Id))
                                        .Where(c => c.QualityTierId == tierEntity.Id)
                                        .ExecuteDeleteAsync(cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                if (tierPatch.Criteria.Update is not null)
                                {
                                    foreach (var criterionPatch in tierPatch.Criteria.Update)
                                    {
                                        var criterionEntity = tierEntity.Criteria
                                            .FirstOrDefault(c => c.Id == criterionPatch.Id);

                                        if (criterionEntity is not null)
                                        {
                                            if (criterionPatch.Field is not null)
                                            {
                                                criterionEntity.Field = criterionPatch.Field.Value!;
                                            }

                                            if (criterionPatch.Operator is not null)
                                            {
                                                criterionEntity.Operator = criterionPatch.Operator.Value;
                                            }

                                            if (criterionPatch.Value is not null)
                                            {
                                                criterionEntity.Value = criterionPatch.Value.Value!;
                                            }
                                        }
                                    }
                                }
                                if (tierPatch.Criteria.Add is not null)
                                {
                                    foreach (var criterion in tierPatch.Criteria.Add)
                                    {
                                        var criterionEntity = criterion.CreateEntity(tierEntity.Id);
                                        tierEntity.Criteria.Add(criterionEntity);
                                    }
                                }
                            }
                        }
                    }
                }

                if (patch.Tiers.Add is not null)
                {
                    foreach (var tier in patch.Tiers.Add)
                    {
                        var tierEntity = tier.CreateEntity(patch.Id);
                        context.QualityTiers.Add(tierEntity);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                        foreach (var criterion in tier.Criteria)
                        {
                            var criterionEntity = criterion.CreateEntity(tierEntity.Id);
                            context.QualityCriteria.Add(criterionEntity);
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
                    entityType: nameof(QualityTierList),
                    description: $"Patched quality tier list {entity.Name}",
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
                "Error patching quality tier list {QualityTierListId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error patching quality tier list",
                ErrorCodes.DbQualityTierListError,
                errorCode);
        }
    }
}
