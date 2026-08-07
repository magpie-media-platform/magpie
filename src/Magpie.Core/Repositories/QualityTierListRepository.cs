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
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class QualityTierListRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IEventBus eventBus,
    ILogger<QualityTierListRepository> logger,
    TimeProvider? timeProvider = null) : IQualityTierListRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static IReadOnlyCollection<string> ChangedFields(QualityTierListPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add(nameof(QualityTierListPatch.Name));
        }

        if (patch.MimeTypes is not null)
        {
            fields.Add(nameof(QualityTierListPatch.MimeTypes));
        }

        if (patch.UpgradeAllowed is not null)
        {
            fields.Add(nameof(QualityTierListPatch.UpgradeAllowed));
        }

        if (patch.Rules is not null)
        {
            fields.Add(nameof(QualityTierListPatch.Rules));
        }

        if (patch.QualityTierRanks is not null)
        {
            fields.Add(nameof(QualityTierListPatch.QualityTierRanks));
        }

        if (patch.Tags is not null)
        {
            fields.Add(nameof(QualityTierListPatch.Tags));
        }

        return fields;
    }

    public async Task<IEnumerable<QualityTierList>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTierLists
                .AsNoTracking()
                .AsSplitQuery()
                .Include(q => q.QualityTierRanks).ThenInclude(r => r.QualityTier)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to list quality tier lists.");
            throw;
        }
    }

    public async Task<QualityTierList?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.QualityTierLists
                .AsNoTracking()
                .AsSplitQuery()
                .Include(q => q.QualityTierRanks).ThenInclude(r => r.QualityTier)
                .FirstOrDefaultAsync(q => q.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to read quality tier list " +
                "{QualityTierListId}.", id);
            throw;
        }
    }

    public async Task<QualityTierList> CreateAsync(
        QualityTierList model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;
                    context.QualityTierLists.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(QualityTierList),
                            Detail = $"Created quality tier list {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    ConfigurationEventBuilder.BuildQualityTierListCreatedEvent(created.Id),
                    token),
                cancellationToken).ConfigureAwait(false);
            await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                .ConfigureAwait(false);

            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to create quality tier list " +
                "{QualityTierListName}.", model.Name);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        QualityTierList model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var updated = await context.QualityTierLists
                        .Where(q => q.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(q => q.Name, model.Name)
                                .SetProperty(q => q.MimeTypes, model.MimeTypes)
                                .SetProperty(q => q.UpgradeAllowed, model.UpgradeAllowed)
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
                                EntityType = nameof(QualityTierList),
                                Detail = $"Updated quality tier list {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
            if (applied)
            {
                await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to update quality tier list " +
                "{QualityTierListId}.", model.Id);
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
                        from q in context.QualityTierLists.AsNoTracking()
                        where q.Id == id
                        select new { q.Name })
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

                    var rootFolders = await context.RootFolders
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var value in rootFolders.SelectMany(folder => folder.Defaults.Values))
                    {
                        if (value.QualityTierListId == id)
                        {
                            value.QualityTierListId = null;
                        }
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var deleted = await context.QualityTierLists
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
                                EntityType = nameof(QualityTierList),
                                Detail = $"Deleted quality tier list {model.Name}",
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
                    token => eventBus.PublishAsync(
                        ConfigurationEventBuilder.BuildQualityTierListDeletedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
                await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to delete quality tier list " +
                "{QualityTierListId}.", id);
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        QualityTierListPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.QualityTierLists
                        .Include(q => q.QualityTierRanks)
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

                    if (patch.MimeTypes is not null)
                    {
                        model.MimeTypes = patch.MimeTypes.Value;
                    }

                    if (patch.UpgradeAllowed is not null)
                    {
                        model.UpgradeAllowed = patch.UpgradeAllowed.Value;
                    }

                    if (patch.Rules is not null)
                    {
                        model.Rules = patch.Rules.Value;
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
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(QualityTierList),
                                Detail = $"Updated quality tier list {model.Name}.",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(QualityTierList),
                                    Detail = $"Added tag {tagName} to quality tier list {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tagName in tagsRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(QualityTierList),
                                    Detail = $"Removed tag {tagName} from quality tier list {model.Name}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tierId in ranksAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "AddRank",
                                    EntityId = patch.Id.ToString(),
                                    ParentEntityId = tierId.ToString(),
                                    EntityType = nameof(QualityTierList),
                                    Detail = $"Added tier {tierId} to quality tier list {model.Name}.",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tierId in ranksRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "RemoveRank",
                                    EntityId = patch.Id.ToString(),
                                    ParentEntityId = tierId.ToString(),
                                    EntityType = nameof(QualityTierList),
                                    Detail = $"Removed tier {tierId} from quality tier list {model.Name}.",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tierId in ranksUpdated)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "UpdateRank",
                                    EntityId = patch.Id.ToString(),
                                    ParentEntityId = tierId.ToString(),
                                    EntityType = nameof(QualityTierList),
                                    Detail = $"Updated tier {tierId} in quality tier list {model.Name}.",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ConfigurationEventBuilder.BuildQualityTierListUpdatedEvent(
                            patch.Id,
                            ChangedFields(patch)), token),
                    cancellationToken).ConfigureAwait(false);
                await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The quality tier list repository failed to patch quality tier list " +
                "{QualityTierListId}.", patch.Id);
            throw;
        }
    }
}
