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
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class RootFolderRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IEventBus eventBus,
    ILogger<RootFolderRepository> logger,
    TimeProvider? timeProvider = null) : IRootFolderRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private static bool IsUnderAnyRoot(string filePath, IReadOnlyCollection<string> rootPaths)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedFile = NormalizePath(filePath);
        foreach (var rootPath in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                continue;
            }

            var normalizedRoot = NormalizePath(rootPath);
            if (normalizedFile.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedFile.StartsWith(normalizedRoot + '/', StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static List<string> ChangedFields(RootFolder current, RootFolder model)
    {
        var fields = new List<string>();
        if (!string.Equals(current.Name, model.Name, StringComparison.Ordinal))
        {
            fields.Add("name");
        }

        if (!string.Equals(current.Path, model.Path, StringComparison.Ordinal))
        {
            fields.Add("path");
        }

        if (!string.Equals(current.ContentType, model.ContentType, StringComparison.Ordinal))
        {
            fields.Add("contentType");
        }

        if (current.ImportMode != model.ImportMode)
        {
            fields.Add("importMode");
        }

        if (current.IsDefault != model.IsDefault)
        {
            fields.Add("isDefault");
        }

        if (current.DefaultMonitoring != model.DefaultMonitoring)
        {
            fields.Add("defaultMonitoring");
        }

        if (!string.Equals(current.RenameTemplate, model.RenameTemplate, StringComparison.Ordinal))
        {
            fields.Add("renameTemplate");
        }

        if (current.DefaultImportCollisionMode != model.DefaultImportCollisionMode)
        {
            fields.Add("defaultImportCollisionMode");
        }

        if (current.UpgradeFileHandling != model.UpgradeFileHandling)
        {
            fields.Add("upgradeFileHandling");
        }

        var currentDefaults = current.Defaults
            .Select(value => (LevelSlug: value.Key, value.Value.QualityTierListId))
            .OrderBy(value => value.LevelSlug, StringComparer.Ordinal);
        var nextDefaults = model.Defaults
            .Select(value => (LevelSlug: value.Key, value.Value.QualityTierListId))
            .OrderBy(value => value.LevelSlug, StringComparer.Ordinal);

        if (!currentDefaults.SequenceEqual(nextDefaults))
        {
            fields.Add("defaults");
        }

        return fields;
    }

    public async Task<IEnumerable<RootFolder>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.RootFolders
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to list root folders.");
            throw;
        }
    }

    public async Task<RootFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.RootFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to read root folder {RootFolderId}.", id);
            throw;
        }
    }

    public async Task<RootFolder> CreateAsync(
        RootFolder model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        RootFolder created;
        var unmarkedDefaultIds = new List<Guid>();
        try
        {
            created = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    model.CreatedAt = now;
                    model.CreatedByUserId = createdByUserId;
                    var existingCount = await context.RootFolders
                        .CountAsync(folder => folder.ContentType == model.ContentType, cancellationToken)
                        .ConfigureAwait(false);
                    model.IsDefault = model.IsDefault || existingCount == 0;
                    if (model.IsDefault)
                    {
                        unmarkedDefaultIds.AddRange(await (
                            from folder in context.RootFolders.AsNoTracking()
                            where folder.ContentType == model.ContentType && folder.IsDefault
                            select folder.Id)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false));
                        await context.RootFolders
                            .Where(folder => folder.ContentType == model.ContentType && folder.IsDefault)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(folder => folder.IsDefault, false),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    context.RootFolders.Add(model);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = model.Id.ToString(),
                            EntityType = nameof(RootFolder),
                            Detail = $"Created root folder {model.Name}",
                            Timestamp = now,
                            UserId = createdByUserId
                        },
                        cancellationToken).ConfigureAwait(false);

                    return model;
                },
                cancellationToken).ConfigureAwait(false);

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(RootFolderEventBuilder.BuildRootFolderCreatedEvent(created), token),
                cancellationToken).ConfigureAwait(false);
            foreach (var id in unmarkedDefaultIds)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        RootFolderEventBuilder.BuildRootFolderUpdatedEvent(id, ["isDefault"]),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to create root folder " +
                "{RootFolderName}.", model.Name);
            throw;
        }

        return created;
    }

    public async Task<bool> UpdateAsync(RootFolder model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var changedFields = new List<string>();
            var unmarkedDefaultIds = new List<Guid>();
            var invalidatedCandidateIds = new List<Guid>();
            var applied = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.RootFolders
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    changedFields = ChangedFields(current, model);
                    if (model.IsDefault)
                    {
                        unmarkedDefaultIds.AddRange(await (
                            from folder in context.RootFolders.AsNoTracking()
                            where folder.Id != model.Id &&
                                folder.ContentType == model.ContentType &&
                                folder.IsDefault
                            select folder.Id)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false));
                        await context.RootFolders
                            .Where(folder => folder.Id != model.Id &&
                                folder.ContentType == model.ContentType &&
                                folder.IsDefault)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(folder => folder.IsDefault, false),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var updated = await context.RootFolders
                        .Where(r => r.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.Name, model.Name)
                                .SetProperty(r => r.Path, model.Path)
                                .SetProperty(r => r.ContentType, model.ContentType)
                                .SetProperty(r => r.IsDefault, model.IsDefault)
                                .SetProperty(r => r.ImportMode, model.ImportMode)
                                .SetProperty(r => r.DefaultMonitoring, model.DefaultMonitoring)
                                .SetProperty(r => r.RenameTemplate, model.RenameTemplate)
                                .SetProperty(r => r.DefaultImportCollisionMode, model.DefaultImportCollisionMode)
                                .SetProperty(r => r.UpgradeFileHandling, model.UpgradeFileHandling)
                                .SetProperty(r => r.Defaults, model.Defaults)
                                .SetProperty(r => r.ModifiedAt, now)
                                .SetProperty(r => r.ModifiedByUserId, modifiedByUserId),
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
                                EntityType = nameof(RootFolder),
                                Detail = $"Updated root folder {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        invalidatedCandidateIds.AddRange(await InvalidateOrphanedImportCandidatesAsync(
                            now,
                            modifiedByUserId,
                            cancellationToken).ConfigureAwait(false));
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (applied && changedFields.Count > 0)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        RootFolderEventBuilder.BuildRootFolderUpdatedEvent(model.Id, changedFields),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            if (applied)
            {
                foreach (var id in unmarkedDefaultIds)
                {
                    await transactionManager.OnCommittedAsync(
                        token => eventBus.PublishAsync(
                            RootFolderEventBuilder.BuildRootFolderUpdatedEvent(id, ["isDefault"]),
                            token),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (applied)
            {
                foreach (var id in invalidatedCandidateIds)
                {
                    await transactionManager.OnCommittedAsync(
                        token => eventBus.PublishAsync(
                            ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(id),
                            token),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (applied)
            {
                await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return applied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to update root folder {RootFolderId}.", model.Id);
            throw;
        }
    }

    private async Task<IReadOnlyList<Guid>> InvalidateOrphanedImportCandidatesAsync(
        DateTime now,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        var rootPaths = await context.RootFolders
            .AsNoTracking()
            .Select(r => r.Path)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var pending = await (
            from m in context.LibraryImportCandidates.AsNoTracking()
            where m.Status == LibraryImportCandidateStatus.Pending
            select new { m.Id, m.FilePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orphanedIds = (from m in pending where !IsUnderAnyRoot(m.FilePath, rootPaths) select m.Id)
            .ToList();

        if (orphanedIds.Count == 0)
        {
            return [];
        }

        await context.LibraryImportCandidates
            .Where(m => orphanedIds.Contains(m.Id))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, LibraryImportCandidateStatus.Invalidated)
                    .SetProperty(m => m.ModifiedAt, now)
                    .SetProperty(m => m.ModifiedByUserId, modifiedByUserId),
                cancellationToken)
            .ConfigureAwait(false);

        await auditRecordRepository.CreateAsync(
            new()
            {
                Category = AuditCategories.Content,
                Operation = "Invalidate",
                EntityType = nameof(LibraryImportCandidate),
                Detail = $"Invalidated {orphanedIds.Count} library import candidate(es) " +
                    "left outside all configured root folders after a root-folder change",
                Timestamp = now,
                UserId = modifiedByUserId
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The root folder change invalidated {Count} library import candidates outside all configured roots",
            orphanedIds.Count);

        return orphanedIds;
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
                        from r in context.RootFolders.AsNoTracking()
                        where r.Id == id
                        select new { r.Name, r.ContentType })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var containsContent = await context.ContentHierarchyItems
                        .AsNoTracking()
                        .AnyAsync(item => item.EffectiveRootFolderId == id, cancellationToken)
                        .ConfigureAwait(false);
                    if (containsContent)
                    {
                        throw new MagpieException("A root folder containing content cannot be deleted.");
                    }

                    var deleted = await context.RootFolders
                        .Where(r => r.Id == id)
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
                                EntityType = nameof(RootFolder),
                                Detail = $"Deleted root folder {model.Name}",
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
                    token => eventBus.PublishAsync(RootFolderEventBuilder.BuildRootFolderDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
                await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to delete root folder {RootFolderId}.", id);
            throw;
        }
    }
}
