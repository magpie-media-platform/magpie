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
    TimeProvider timeProvider) : IRootFolderRepository
{
    private readonly TimeProvider _time = timeProvider;

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

    private static List<string> ChangedFields(RootFolderPatch patch)
    {
        var fields = new List<string>();
        if (patch.Name is not null)
        {
            fields.Add("name");
        }

        if (patch.Path is not null)
        {
            fields.Add("path");
        }

        if (patch.ContentType is not null)
        {
            fields.Add("contentType");
        }

        if (patch.IsDefault is not null)
        {
            fields.Add("isDefault");
        }

        if (patch.DefaultMonitoring is not null)
        {
            fields.Add("defaultMonitoring");
        }

        if (patch.RenameTemplate is not null)
        {
            fields.Add("renameTemplate");
        }

        if (patch.UpgradeFileHandling is not null)
        {
            fields.Add("upgradeFileHandling");
        }

        if (patch.AutoImport is not null)
        {
            fields.Add("autoImport");
        }

        if (patch.Defaults is not null)
        {
            fields.Add("defaults");
        }

        return fields;
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

        if (current.AutoImport != model.AutoImport)
        {
            fields.Add("autoImport");
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

    private async Task<Guid?> PromoteDefaultIfMissingAsync(
        string contentType,
        DateTime now,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        var hasDefault = await context.RootFolders
            .AsNoTracking()
            .AnyAsync(folder => folder.ContentType == contentType && folder.IsDefault, cancellationToken)
            .ConfigureAwait(false);
        if (hasDefault)
        {
            return null;
        }

        var successorId = await context.RootFolders
            .AsNoTracking()
            .Where(folder => folder.ContentType == contentType)
            .OrderBy(folder => folder.CreatedAt)
            .ThenBy(folder => folder.Id)
            .Select(folder => (Guid?)folder.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (successorId is null)
        {
            return null;
        }

        await context.RootFolders
            .Where(folder => folder.Id == successorId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(folder => folder.IsDefault, true)
                    .SetProperty(folder => folder.ModifiedAt, now)
                    .SetProperty(folder => folder.ModifiedByUserId, modifiedByUserId),
                cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug(
            "The root folder {RootFolderId} became the default of content type {ContentType} because the type " +
            "was left without one.",
            successorId,
            contentType);
        return successorId;
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

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, long>> GetContentSizesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await (
                from file in context.MediaFiles.AsNoTracking()
                join item in context.ContentItems.AsNoTracking() on file.ContentItemId equals item.Id
                join node in context.ContentHierarchyItems.AsNoTracking()
                    on item.ContentHierarchyItemId equals node.Id
                group file.FileSize by node.EffectiveRootFolderId into sizes
                select new { RootFolderId = sizes.Key, Bytes = sizes.Sum() })
                .ToDictionaryAsync(row => row.RootFolderId, row => row.Bytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to total the media file sizes.");
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
            logger.LogError(
                ex,
                "The root folder repository failed to create root folder {RootFolderName}.",
                model.Name);
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
            var promotedDefaultIds = new List<Guid>();
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
                                .SetProperty(r => r.AutoImport, model.AutoImport)
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

                        foreach (var contentType in new[] { current.ContentType, model.ContentType }.Distinct())
                        {
                            var promotedId = await PromoteDefaultIfMissingAsync(
                                contentType,
                                now,
                                modifiedByUserId,
                                cancellationToken).ConfigureAwait(false);
                            if (promotedId is { } id)
                            {
                                promotedDefaultIds.Add(id);
                            }
                        }

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
                foreach (var id in unmarkedDefaultIds.Concat(promotedDefaultIds))
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

    public async Task<bool> PatchAsync(
        RootFolderPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var unmarkedDefaultIds = new List<Guid>();
            var promotedDefaultIds = new List<Guid>();
            var invalidatedCandidateIds = new List<Guid>();
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var model = await context.RootFolders
                        .FirstOrDefaultAsync(r => r.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (model is null)
                    {
                        return false;
                    }

                    var now = _time.GetUtcNow().UtcDateTime;
                    var previousContentType = model.ContentType;
                    if (patch.Name is not null)
                    {
                        model.Name = patch.Name.Value;
                    }

                    if (patch.Path is not null)
                    {
                        model.Path = patch.Path.Value;
                    }

                    if (patch.ContentType is not null)
                    {
                        model.ContentType = patch.ContentType.Value;
                    }

                    if (patch.IsDefault is not null)
                    {
                        model.IsDefault = patch.IsDefault.Value;
                    }

                    if (patch.DefaultMonitoring is not null)
                    {
                        model.DefaultMonitoring = patch.DefaultMonitoring.Value;
                    }

                    if (patch.RenameTemplate is not null)
                    {
                        model.RenameTemplate = patch.RenameTemplate.Value;
                    }

                    if (patch.UpgradeFileHandling is not null)
                    {
                        model.UpgradeFileHandling = patch.UpgradeFileHandling.Value;
                    }

                    if (patch.AutoImport is not null)
                    {
                        model.AutoImport = patch.AutoImport.Value;
                    }

                    if (patch.Defaults is not null)
                    {
                        model.Defaults = patch.Defaults.Value;
                        context.Entry(model).Property(r => r.Defaults).IsModified = true;
                    }

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
                                EntityType = nameof(RootFolder),
                                Detail = $"Updated root folder {model.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var contentType in new[] { previousContentType, model.ContentType }.Distinct())
                        {
                            var promotedId = await PromoteDefaultIfMissingAsync(
                                contentType,
                                now,
                                modifiedByUserId,
                                cancellationToken).ConfigureAwait(false);
                            if (promotedId is { } id)
                            {
                                promotedDefaultIds.Add(id);
                            }
                        }

                        invalidatedCandidateIds.AddRange(await InvalidateOrphanedImportCandidatesAsync(
                            now,
                            modifiedByUserId,
                            cancellationToken).ConfigureAwait(false));
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (!patched)
            {
                return false;
            }

            await transactionManager.OnCommittedAsync(
                token => eventBus.PublishAsync(
                    RootFolderEventBuilder.BuildRootFolderUpdatedEvent(patch.Id, ChangedFields(patch)),
                    token),
                cancellationToken).ConfigureAwait(false);

            foreach (var id in unmarkedDefaultIds.Concat(promotedDefaultIds))
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        RootFolderEventBuilder.BuildRootFolderUpdatedEvent(id, ["isDefault"]),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var id in invalidatedCandidateIds)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        ContentEventBuilder.BuildLibraryImportCandidateUpdatedEvent(id),
                        token),
                    cancellationToken).ConfigureAwait(false);
            }

            await contentHierarchyItemRepository.RecomputeAllEffectiveValuesAsync(cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The root folder repository failed to patch root folder {RootFolderId}.", patch.Id);
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
            where m.Status == LibraryImportCandidateStatus.Pending ||
                m.Status == LibraryImportCandidateStatus.MatchingPending
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
                Detail = $"Invalidated {orphanedIds.Count} library import candidates " +
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
            var promotedDefaultIds = new List<Guid>();
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

                        var promotedId = await PromoteDefaultIfMissingAsync(
                            model.ContentType,
                            now,
                            deletedByUserId,
                            cancellationToken).ConfigureAwait(false);
                        if (promotedId is { } successorId)
                        {
                            promotedDefaultIds.Add(successorId);
                        }
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(RootFolderEventBuilder.BuildRootFolderDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
                foreach (var promotedId in promotedDefaultIds)
                {
                    await transactionManager.OnCommittedAsync(
                        token => eventBus.PublishAsync(
                            RootFolderEventBuilder.BuildRootFolderUpdatedEvent(promotedId, ["isDefault"]),
                            token),
                        cancellationToken).ConfigureAwait(false);
                }

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
