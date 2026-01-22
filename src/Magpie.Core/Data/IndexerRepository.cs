using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Data.Database.Entities;
using Magpie.Models.History;
using Magpie.Models.Indexers;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Indexer = Magpie.Models.Indexers.Indexer;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing indexer configurations.
/// </summary>
public sealed class IndexerRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<IndexerRepository> logger) : IIndexerRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Indexer>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var indexers = await context.Indexers
                .AsNoTracking()
                .Include(i => i.Settings)
                .Include(i => i.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = indexers.Select(i => i.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Indexer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting indexers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Indexer>>(
                "Error retrieving indexers",
                ErrorCodes.DbIndexerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Indexer?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var indexer = await (
                from i in context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
                where i.Id == id
                select i)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(indexer?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer {IndexerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<Indexer?>("Error retrieving indexer", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Indexer?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var indexer = await (
                from i in context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
                where i.Name == name
                select i)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(indexer?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer {IndexerName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<Indexer?>("Error retrieving indexer", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Indexer>>> GetByDefinitionIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var indexers = await (
                from i in context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
                where i.DefinitionId == definitionId
                select i)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = indexers.Select(i => i.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Indexer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexers for definition {DefinitionId} ({ExErrorCode})",
                definitionId,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Indexer>>(
                "Error retrieving indexers",
                ErrorCodes.DbIndexerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Indexer>>> GetByStatusAsync(
        IndexerStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            var indexers = await (
                from i in context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
                where i.Status == status
                select i)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = indexers.Select(i => i.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Indexer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexers with status {Status} ({ExErrorCode})",
                status,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<Indexer>>(
                "Error retrieving indexers",
                ErrorCodes.DbIndexerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Indexer>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var indexers = await (
                from i in context.Indexers.AsNoTracking().Include(i => i.Settings).Include(i => i.Tags)
                where i.Enabled
                select i)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = indexers.Select(i => i.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Indexer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled indexers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Indexer>>(
                "Error retrieving indexers",
                ErrorCodes.DbIndexerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Indexer>> CreateAsync(
        Indexer model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            foreach (var kvp in model.Settings)
            {
                entity.Settings.Add(new()
                {
                    Key = kvp.Key,
                    Value = kvp.Value,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = createdByUserId
                });
            }
            foreach (var tag in model.Tags)
            {
                entity.Tags.Add(new()
                {
                    TagName = tag,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = createdByUserId
                });
            }
            context.Indexers.Add(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(Indexer),
                description: $"Created indexer {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in entity.Settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: entity.Id,
                    entityType: nameof(IndexerSetting),
                    description: $"Added setting '{setting.Key}' to indexer {model.Name}",
                    userId: createdByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            foreach (var tag in entity.Tags)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Create",
                    entityId: tag.TagName,
                    parentEntityId: entity.Id,
                    entityType: nameof(IndexerTag),
                    description: $"Added tag '{tag.TagName}' to indexer {model.Name}",
                    userId: createdByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating indexer {IndexerName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<Indexer>("Error creating indexer", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Indexer model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Indexers
                .Include(i => i.Settings)
                .Include(i => i.Tags)
                .FirstOrDefaultAsync(i => i.Id == model.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var settingsAdded = new List<IndexerSetting>();
            var settingsUpdated = new List<IndexerSetting>();
            var settingsDeleted = new List<IndexerSetting>();
            var tagsAdded = new List<string>();
            var tagsDeleted = new List<string>();

            entity.Name = model.Name;
            entity.DefinitionId = model.DefinitionId;
            entity.Enabled = model.Enabled;
            entity.Status = model.Status;
            entity.LastChecked = model.LastChecked;
            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            foreach (var kv in model.Settings.Where(kv => !entity.Settings.Any(s => s.Key == kv.Key)).ToList())
            {
                var entry = context.IndexerSettings.Add(new()
                {
                    IndexerId = entity.Id,
                    Key = kv.Key,
                    Value = kv.Value,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId
                });
                settingsAdded.Add(entry.Entity);
            }
            foreach (var settingEntity in entity.Settings.Where(s => model.Settings.ContainsKey(s.Key)).ToList())
            {
                if (settingEntity.Value == model.Settings[settingEntity.Key])
                {
                    continue;
                }
                settingEntity.Value = model.Settings[settingEntity.Key];
                settingEntity.ModifiedAt = DateTimeOffset.UtcNow;
                settingEntity.ModifiedByUserId = modifiedByUserId;
                settingsUpdated.Add(settingEntity);
            }
            foreach (var settingEntity in entity.Settings.Where(s => !model.Settings.ContainsKey(s.Key)).ToList())
            {
                entity.Settings.Remove(settingEntity);
                settingsDeleted.Add(settingEntity);
            }
            foreach (var tag in model.Tags.Where(t => !entity.Tags.Any(et => et.TagName == t)).ToList())
            {
                entity.Tags.Add(new()
                {
                    IndexerId = entity.Id,
                    TagName = tag,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId
                });
                tagsAdded.Add(tag);
            }
            foreach (var tagEntity in entity.Tags.Where(t => !model.Tags.Contains(t.TagName)).ToList())
            {
                entity.Tags.Remove(tagEntity);
                tagsDeleted.Add(tagEntity.TagName);
            }

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                await RecordChangeHistoryAsync(
                    id: entity.Id,
                    name: entity.Name,
                    operation: "Update",
                    description: $"Updated indexer {entity.Name}",
                    modifiedByUserId: modifiedByUserId,
                    settingsAdded: settingsAdded,
                    settingsUpdated: settingsUpdated,
                    settingsDeleted: settingsDeleted,
                    tagsAdded: tagsAdded,
                    tagsDeleted: tagsDeleted,
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
                "Error updating indexer {IndexerId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating indexer", ErrorCodes.DbIndexerError, errorCode);
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

            var entity = await context.Indexers
                .Include(i => i.Settings)
                .Include(i => i.Tags)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.IndexerSettings
                .Where(s => s.IndexerId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.IndexerTags
                .Where(t => t.IndexerId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.Indexers
                .Where(i => i.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await RecordChangeHistoryAsync(
                    id: id,
                    name: entity.Name,
                    operation: "Delete",
                    description: $"Deleted indexer {entity.Name}",
                    modifiedByUserId: deletedByUserId,
                    settingsAdded: [],
                    settingsUpdated: [],
                    settingsDeleted: entity.Settings,
                    tagsAdded: [],
                    tagsDeleted: entity.Tags.Select(t => t.TagName),
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
                "Error deleting indexer {IndexerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting indexer", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        IndexerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.Indexers
                .Include(i => i.Settings)
                .Include(i => i.Tags)
                .FirstOrDefaultAsync(i => i.Id == patch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            if (patch.Name is not null)
            {
                entity.Name = patch.Name.Value;
            }
            if (patch.Enabled is not null)
            {
                entity.Enabled = patch.Enabled.Value;
            }

            var settingsAdded = new List<IndexerSetting>();
            var settingsUpdated = new List<IndexerSetting>();
            var settingsDeleted = new List<IndexerSetting>();
            var tagsAdded = new List<string>();
            var tagsDeleted = new List<string>();

            if (patch.Settings?.Add is not null)
            {
                foreach (var settingPatch in patch.Settings.Add)
                {
                    if (entity.Settings.Any(s => s.Key == settingPatch.Key))
                    {
                        continue;
                    }
                    var entry = context.IndexerSettings.Add(new()
                    {
                        IndexerId = patch.Id,
                        Key = settingPatch.Key,
                        Value = settingPatch.Value
                    });
                    settingsAdded.Add(entry.Entity);
                }
            }
            if (patch.Settings?.Update is not null)
            {
                foreach (var settingPatch in patch.Settings.Update)
                {
                    var settingsEntity = entity.Settings.FirstOrDefault(s => s.Key == settingPatch.Key);
                    if (settingsEntity is null)
                    {
                        continue;
                    }
                    settingsEntity.Value = settingPatch.Value;
                    settingsEntity.ModifiedAt = DateTimeOffset.UtcNow;
                    settingsEntity.ModifiedByUserId = modifiedByUserId;
                    settingsUpdated.Add(settingsEntity);
                }
            }
            if (patch.Settings?.Remove is not null)
            {
                foreach (var settingKey in patch.Settings.Remove)
                {
                    var settingsEntity = entity.Settings.FirstOrDefault(s => s.Key == settingKey);
                    if (settingsEntity is null)
                    {
                        continue;
                    }
                    entity.Settings.Remove(settingsEntity);
                    settingsDeleted.Add(settingsEntity);
                }
            }
            if (patch.Tags?.Add is not null)
            {
                foreach (var tag in patch.Tags.Add)
                {
                    if (entity.Tags.Any(t => t.TagName == tag))
                    {
                        continue;
                    }
                    entity.Tags.Add(new()
                    {
                        IndexerId = patch.Id,
                        TagName = tag
                    });
                    tagsAdded.Add(tag);
                }
            }
            if (patch.Tags?.Remove is not null)
            {
                foreach (var tag in patch.Tags.Remove)
                {
                    var tagEntity = entity.Tags.FirstOrDefault(t => t.TagName == tag);
                    if (tagEntity is null)
                    {
                        continue;
                    }
                    entity.Tags.Remove(tagEntity);
                    tagsDeleted.Add(tag);
                }
            }

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                await RecordChangeHistoryAsync(
                    id: entity.Id,
                    name: entity.Name,
                    operation: "Update",
                    description: $"Updated indexer {entity.Name}",
                    modifiedByUserId: modifiedByUserId,
                    settingsAdded: settingsAdded,
                    settingsUpdated: settingsUpdated,
                    settingsDeleted: settingsDeleted,
                    tagsAdded: tagsAdded,
                    tagsDeleted: tagsDeleted,
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
                "Error patching indexer {IndexerId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching indexer", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        IndexerStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.Indexers
                .Where(i => i.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Status, status)
                        .SetProperty(e => e.LastChecked, lastChecked),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating indexer status {IndexerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating indexer status", ErrorCodes.DbIndexerError, errorCode);
        }
    }

    private async Task RecordChangeHistoryAsync(
        Guid id,
        string? name,
        string operation,
        string description,
        Guid? modifiedByUserId,
        IEnumerable<IndexerSetting> settingsAdded,
        IEnumerable<IndexerSetting> settingsUpdated,
        IEnumerable<IndexerSetting> settingsDeleted,
        IEnumerable<string> tagsAdded,
        IEnumerable<string> tagsDeleted,
        CancellationToken cancellationToken)
    {
        await historyRepository.RecordEntryAsync(
            category: AuditCategories.Integrations,
            operation: operation,
            entityId: id,
            entityType: nameof(Indexer),
            description: description,
            userId: modifiedByUserId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var settingEntity in settingsAdded)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(IndexerSetting),
                description: $"Added setting '{settingEntity.Key}' to indexer {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var settingEntity in settingsUpdated)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Update",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(IndexerSetting),
                description: $"Updated setting '{settingEntity.Key}' on indexer {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var settingEntity in settingsDeleted)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Delete",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(IndexerSetting),
                description: $"Removed setting '{settingEntity.Key}' from indexer {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var tag in tagsAdded)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: tag,
                parentEntityId: id,
                entityType: nameof(IndexerTag),
                description: $"Added tag '{tag}' to indexer {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var tag in tagsDeleted)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Delete",
                entityId: tag,
                parentEntityId: id,
                entityType: nameof(IndexerTag),
                description: $"Removed tag '{tag}' from indexer {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
