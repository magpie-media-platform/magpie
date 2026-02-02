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
/// Repository for managing quality tier lists.
/// </summary>
public sealed class QualityTierListRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<QualityTierListRepository> logger) : IQualityTierListRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<QualityTierList>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTierLists
                .AsNoTracking()
                .Include(q => q.QualityTierRanks).ThenInclude(r => r.QualityTier)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing quality tier lists");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<QualityTierList?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTierLists
                .AsNoTracking()
                .Include(q => q.QualityTierRanks).ThenInclude(r => r.QualityTier)
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting quality tier list {QualityTierListId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<QualityTierList?>> ListByTagAsync(string tagName, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Tags
                .AsNoTracking()
                .Where(t => t.Name == tagName)
                .SelectMany(t => t.QualityTierLists)
                .Include(q => q.QualityTierRanks).ThenInclude(r => r.QualityTier)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing quality tier lists by tag {TagName}", tagName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<QualityTierList> CreateAsync(
        QualityTierList model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                context.QualityTierLists.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(QualityTierList),
                    Detail = $"Created quality tier list {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating quality tier list {QualityTierListName}", model.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        QualityTierList model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.QualityTierLists
                    .Where(q => q.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(q => q.Name, model.Name)
                            .SetProperty(q => q.UpgradeAllowed, model.UpgradeAllowed)
                            .SetProperty(q => q.ModifiedAt, now)
                            .SetProperty(q => q.ModifiedByUserId, modifiedByUserId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Update",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(QualityTierList),
                        Detail = $"Updated quality tier list {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating quality tier list {QualityTierListId}", model.Id);
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
                var model = await context.QualityTierLists
                    .AsNoTracking()
                    .Where(q => q.Id == id)
                    .Select(q => new { q.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                await context.QualityTierListRanks
                    .Where(r => r.QualityTierListId == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                var deleted = await context.QualityTierLists
                    .Where(q => q.Id == id)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                {
                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Delete",
                        EntityId = id.ToString(),
                        EntityType = nameof(QualityTierList),
                        Detail = $"Deleted quality tier list {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting quality tier list {QualityTierListId}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        QualityTierListPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.QualityTierLists
                    .Include(q => q.QualityTierRanks)
                    .FirstOrDefaultAsync(q => q.Id == patch.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (patch.Name is not null)
                {
                    model.Name = patch.Name.Value;
                }
                if (patch.UpgradeAllowed is not null)
                {
                    model.UpgradeAllowed = patch.UpgradeAllowed.Value;
                }

                var ranksAdded = new List<Guid>();
                var ranksRemoved = new List<Guid>();
                var ranksUpdated = new List<Guid>();

                if (patch.QualityTierRanks is not null)
                {
                    if (patch.QualityTierRanks.Remove is not null)
                    {
                        foreach (var qualityTierId in patch.QualityTierRanks.Remove)
                        {
                            var rank = model.QualityTierRanks.FirstOrDefault(r => r.QualityTierId == qualityTierId);
                            if (rank is not null)
                            {
                                ranksRemoved.Add(qualityTierId);
                                model.QualityTierRanks.Remove(rank);
                            }
                        }
                    }

                    if (patch.QualityTierRanks.Update is not null)
                    {
                        foreach (var rankPatch in patch.QualityTierRanks.Update)
                        {
                            if (rankPatch.QualityTierId is null)
                            {
                                continue;
                            }
                            var rank = model.QualityTierRanks
                                .FirstOrDefault(r => r.QualityTierId == rankPatch.QualityTierId.Value);
                            if (rank is not null)
                            {
                                ranksUpdated.Add(rank.QualityTierId);
                                if (rankPatch.Ordinal is not null)
                                {
                                    rank.Ordinal = rankPatch.Ordinal.Value;
                                }
                                if (rankPatch.IsCutoff is not null)
                                {
                                    rank.IsCutoff = rankPatch.IsCutoff.Value;
                                }
                                rank.ModifiedAt = now;
                                rank.ModifiedByUserId = modifiedByUserId;
                            }
                        }
                    }

                    if (patch.QualityTierRanks.Add is not null)
                    {
                        foreach (var create in patch.QualityTierRanks.Add)
                        {
                            ranksAdded.Add(create.QualityTierId);
                            var newRank = new QualityTierListRank
                            {
                                QualityTierListId = model.Id,
                                QualityTierId = create.QualityTierId,
                                Ordinal = create.Ordinal,
                                IsCutoff = create.IsCutoff,
                                CreatedAt = now,
                                CreatedByUserId = modifiedByUserId
                            };
                            model.QualityTierRanks.Add(newRank);
                        }
                    }
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
                                .Include(t => t.QualityTierLists)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is null)
                            {
                                tag = new() { Name = tagName };
                                context.Tags.Add(tag);
                            }
                            if (!tag.QualityTierLists.Contains(model))
                            {
                                tag.QualityTierLists.Add(model);
                                tagsAdded.Add(tagName);
                            }
                        }
                    }
                    if (patch.Tags.Remove is not null)
                    {
                        foreach (var tagName in patch.Tags.Remove)
                        {
                            var tag = await context.Tags
                                .Include(t => t.QualityTierLists)
                                .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                .ConfigureAwait(false);

                            if (tag is not null && tag.QualityTierLists.Remove(model))
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
                        Category = AuditCategories.System,
                        Operation = "Patch",
                        EntityId = patch.Id.ToString(),
                        EntityType = nameof(QualityTierList),
                        Detail = $"Patched quality tier list {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);

                    foreach (var tagName in tagsAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "AddTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Added tag '{tagName}' to quality tier list {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tagName in tagsRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "RemoveTag",
                            EntityId = patch.Id.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Removed tag '{tagName}' from quality tier list {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tierId in ranksAdded)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "AddRank",
                            EntityId = patch.Id.ToString(),
                            ParentEntityId = tierId.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Added rank for tier {tierId} to quality tier list {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tierId in ranksRemoved)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "RemoveRank",
                            EntityId = patch.Id.ToString(),
                            ParentEntityId = tierId.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Removed rank for tier {tierId} from quality tier list {model.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var tierId in ranksUpdated)
                    {
                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "UpdateRank",
                            EntityId = patch.Id.ToString(),
                            ParentEntityId = tierId.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Updated rank for tier {tierId} in quality tier list {model.Name}",
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
            logger.LogError(ex, "Error patching quality tier list {QualityTierListId}", patch.Id);
            throw;
        }
    }
}
