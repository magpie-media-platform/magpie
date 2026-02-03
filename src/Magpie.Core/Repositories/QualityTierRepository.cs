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
using Magpie.Models.Filtering;
using Magpie.Models.Patch;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class QualityTierRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<QualityTierRepository> logger) : IQualityTierRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<QualityTier>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTiers
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing quality tiers");
            throw;
        }
    }

    public async Task<QualityTier?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTiers
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting quality tier {QualityTierId}", id);
            throw;
        }
    }

    public async Task<QualityTier> CreateAsync(
        QualityTier model,
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

                context.QualityTiers.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(new()
                {
                    Category = AuditCategories.System,
                    Operation = "Create",
                    EntityId = model.Id.ToString(),
                    EntityType = nameof(QualityTier),
                    Detail = $"Created quality tier {model.Name}",
                    Timestamp = now,
                    UserId = createdByUserId
                }, cancellationToken).ConfigureAwait(false);

                return model;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating quality tier {QualityTierName}", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        QualityTier model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var updated = await context.QualityTiers
                    .Where(q => q.Id == model.Id)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(q => q.Name, model.Name)
                            .SetProperty(q => q.Criteria, model.Criteria)
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
                        EntityType = nameof(QualityTier),
                        Detail = $"Updated quality tier {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating quality tier {QualityTierId}", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var model = await context.QualityTiers
                    .AsNoTracking()
                    .Where(q => q.Id == id)
                    .Select(q => new { q.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (model is null)
                {
                    return false;
                }

                var deleted = await context.QualityTiers
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
                        EntityType = nameof(QualityTier),
                        Detail = $"Deleted quality tier {model.Name}",
                        Timestamp = now,
                        UserId = deletedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return deleted > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting quality tier {QualityTierId}", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        QualityTierPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var model = await context.QualityTiers
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

                if (patch.Criteria is not null)
                {
                    model.Criteria = ApplyCriteriaPatch(model.Criteria, patch.Criteria);
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
                        EntityType = nameof(QualityTier),
                        Detail = $"Patched quality tier {model.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                return updated > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching quality tier {QualityTierId}", patch.Id);
            throw;
        }
    }

    private static QualityTierCriteria ApplyCriteriaPatch(QualityTierCriteria current, QualityTierCriteriaPatch patch)
    {
        var criteria = current.And.ToList();

        if (patch.And is not null)
        {
            if (patch.And.Remove is not null)
            {
                foreach (var index in patch.And.Remove.OrderByDescending(i => i))
                {
                    if (index >= 0 && index < criteria.Count)
                    {
                        criteria.RemoveAt(index);
                    }
                }
            }

            if (patch.And.Update is not null)
            {
                foreach (var (index, itemPatch) in patch.And.Update)
                {
                    if (index >= 0 && index < criteria.Count)
                    {
                        criteria[index] = ApplyPropertyDictionaryCriteriaPatch(criteria[index], itemPatch);
                    }
                }
            }

            if (patch.And.Insert is not null)
            {
                foreach (var (index, item) in patch.And.Insert.OrderBy(kvp => kvp.Key))
                {
                    var insertIndex = Math.Min(index, criteria.Count);
                    criteria.Insert(insertIndex, item);
                }
            }
        }

        return new() { And = criteria };
    }

    private static DictionaryCriteria<PropertyCriterion<string>> ApplyPropertyDictionaryCriteriaPatch(
        DictionaryCriteria<PropertyCriterion<string>> current,
        DictionaryPatch<string, PropertyCriterion<string>, PropertyCriterionPatch> patch)
    {
        var criteria = current.Items?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? [];

        if (patch.Remove is not null)
        {
            foreach (var key in patch.Remove)
            {
                criteria.Remove(key);
            }
        }

        if (patch.Update is not null)
        {
            foreach (var (key, valuePatch) in patch.Update)
            {
                if (criteria.TryGetValue(key, out var existing))
                {
                    criteria[key] = ApplyPropertyCriterionPatch(existing, valuePatch);
                }
            }
        }

        if (patch.Add is not null)
        {
            foreach (var (key, value) in patch.Add)
            {
                criteria[key] = value;
            }
        }

        return new() { Items = criteria };
    }

    private static PropertyCriterion<string> ApplyPropertyCriterionPatch(
        PropertyCriterion<string> current,
        PropertyCriterionPatch patch)
    {
        var op = patch.Operator is not null ? patch.Operator.Value : current.Operator;
        var values = patch.Value is not null ? patch.Value.Value : current.Values ?? [];

        return new() { Operator = op, Values = [.. values] };
    }
}
