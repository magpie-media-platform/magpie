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

internal sealed class QualityTierRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<QualityTierRepository> logger,
    TimeProvider timeProvider) : IQualityTierRepository
{
    private readonly TimeProvider _time = timeProvider;

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
            logger.LogError(ex, "The quality tier repository failed to list quality tiers.");
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
            logger.LogError(ex, "The quality tier repository failed to read quality tier {QualityTierId}.", id);
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
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;

                    context.QualityTiers.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(QualityTier),
                            Detail = $"Created quality tier {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The quality tier repository failed to create quality tier {QualityTierName}.",
                model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(QualityTier model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.QualityTiers
                        .Where(q => q.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(q => q.Name, model.Name)
                                .SetProperty(q => q.Criteria, model.Criteria)
                                .SetProperty(q => q.Rules, model.Rules)
                                .SetProperty(q => q.ModifiedAt, now)
                                .SetProperty(q => q.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(QualityTier),
                                Detail = $"Updated quality tier {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier repository failed to update quality tier {QualityTierId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from q in context.QualityTiers.AsNoTracking()
                        where q.Id == id
                        select new { q.Name })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    if (await context.QualityTierListRanks.AsNoTracking()
                        .AnyAsync(rank => rank.QualityTierId == id, cancellationToken).ConfigureAwait(false))
                    {
                        throw new MagpieException("A quality tier ranked in a list cannot be deleted.");
                    }

                    var deleted = await context.QualityTiers
                        .Where(q => q.Id == id)
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
                                EntityType = nameof(QualityTier),
                                Detail = $"Deleted quality tier {model.Name}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier repository failed to delete quality tier {QualityTierId}.", id);
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
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.QualityTiers
                        .FirstOrDefaultAsync(q => q.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;

                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Criteria is not null)
                    {
                        model.Criteria = patch.Criteria.Value;
                    }

                    if (patch.Rules is not null)
                    {
                        model.Rules = patch.Rules.Value;
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
                                EntityType = nameof(QualityTier),
                                Detail = $"Updated quality tier {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier repository failed to patch quality tier {QualityTierId}.", patch.Id);
            throw;
        }
    }
}
