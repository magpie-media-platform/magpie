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

using IndexerFeed = Magpie.Models.Indexers.IndexerFeed;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing indexer feed configurations.
/// </summary>
public sealed class IndexerFeedRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<IndexerFeedRepository> logger) : IIndexerFeedRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerFeed>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var feeds = await context.IndexerFeeds
                .AsNoTracking()
                .Include(f => f.Settings)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = feeds.Select(f => f.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerFeed>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting indexer feeds ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerFeed>>(
                "Error retrieving indexer feeds",
                ErrorCodes.DbIndexerFeedError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerFeed?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var feed = await (
                from f in context.IndexerFeeds.AsNoTracking().Include(f => f.Settings)
                where f.Id == id
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(feed?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer feed {IndexerFeedId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<IndexerFeed?>("Error retrieving indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerFeed?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var feed = await (
                from f in context.IndexerFeeds.AsNoTracking().Include(f => f.Settings)
                where f.Name == name
                select f)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(feed?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer feed {IndexerFeedName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<IndexerFeed?>("Error retrieving indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerFeed>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var feeds = await (
                from f in context.IndexerFeeds.AsNoTracking().Include(f => f.Settings)
                where f.Provider == provider
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = feeds.Select(f => f.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerFeed>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting indexer feeds for provider {Provider} ({ExErrorCode})",
                provider,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerFeed>>(
                "Error retrieving indexer feeds",
                ErrorCodes.DbIndexerFeedError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<IndexerFeed>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var feeds = await (
                from f in context.IndexerFeeds.AsNoTracking().Include(f => f.Settings)
                where f.Enabled
                select f)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = feeds.Select(f => f.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<IndexerFeed>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled indexer feeds ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<IndexerFeed>>(
                "Error retrieving indexer feeds",
                ErrorCodes.DbIndexerFeedError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IndexerFeed>> CreateAsync(
        IndexerFeed model,
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
            context.IndexerFeeds.Add(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(IndexerFeed),
                description: $"Created indexer feed {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in entity.Settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: entity.Id,
                    entityType: nameof(IndexerFeedSetting),
                    description: $"Added setting '{setting.Key}' to indexer feed {model.Name}",
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
                "Error creating indexer feed {IndexerFeedName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<IndexerFeed>("Error creating indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        IndexerFeed model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.IndexerFeeds
                .Include(f => f.Settings)
                .FirstOrDefaultAsync(f => f.Id == model.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var settingsAdded = new List<IndexerFeedSetting>();
            var settingsUpdated = new List<IndexerFeedSetting>();
            var settingsDeleted = new List<IndexerFeedSetting>();

            entity.Name = model.Name;
            entity.Description = model.Description;
            entity.Provider = model.Provider;
            entity.Enabled = model.Enabled;
            entity.LastSync = model.LastSync;
            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            foreach (var kv in model.Settings.Where(kv => !entity.Settings.Any(s => s.Key == kv.Key)).ToList())
            {
                var entry = context.IndexerFeedSettings.Add(new()
                {
                    IndexerFeedId = entity.Id,
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

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                await RecordChangeHistoryAsync(
                    id: entity.Id,
                    name: entity.Name,
                    operation: "Update",
                    description: $"Updated indexer feed {entity.Name}",
                    modifiedByUserId: modifiedByUserId,
                    settingsAdded: settingsAdded,
                    settingsUpdated: settingsUpdated,
                    settingsDeleted: settingsDeleted,
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
                "Error updating indexer feed {IndexerFeedId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
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

            var entity = await context.IndexerFeeds
                .Include(f => f.Settings)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.IndexerFeedSettings
                .Where(s => s.IndexerFeedId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.IndexerFeeds
                .Where(f => f.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await RecordChangeHistoryAsync(
                    id: id,
                    name: entity.Name,
                    operation: "Delete",
                    description: $"Deleted indexer feed {entity.Name}",
                    modifiedByUserId: deletedByUserId,
                    settingsAdded: [],
                    settingsUpdated: [],
                    settingsDeleted: entity.Settings,
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
                "Error deleting indexer feed {IndexerFeedId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        IndexerFeedPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.IndexerFeeds
                .Include(f => f.Settings)
                .FirstOrDefaultAsync(f => f.Id == patch.Id, cancellationToken)
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
            if (patch.Description is not null)
            {
                entity.Description = patch.Description.Value;
            }
            if (patch.Enabled is not null)
            {
                entity.Enabled = patch.Enabled.Value;
            }

            var settingsAdded = new List<IndexerFeedSetting>();
            var settingsUpdated = new List<IndexerFeedSetting>();
            var settingsDeleted = new List<IndexerFeedSetting>();

            if (patch.Settings?.Add is not null)
            {
                foreach (var settingPatch in patch.Settings.Add)
                {
                    if (entity.Settings.Any(s => s.Key == settingPatch.Key))
                    {
                        continue;
                    }
                    var entry = context.IndexerFeedSettings.Add(new()
                    {
                        IndexerFeedId = patch.Id,
                        Key = settingPatch.Key,
                        Value = settingPatch.Value,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedByUserId = modifiedByUserId
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

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (updated > 0)
            {
                await RecordChangeHistoryAsync(
                    id: entity.Id,
                    name: entity.Name,
                    operation: "Update",
                    description: $"Updated indexer feed {entity.Name}",
                    modifiedByUserId: modifiedByUserId,
                    settingsAdded: settingsAdded,
                    settingsUpdated: settingsUpdated,
                    settingsDeleted: settingsDeleted,
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
                "Error patching indexer feed {IndexerFeedId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching indexer feed", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateLastSyncAsync(
        Guid id,
        DateTimeOffset lastSync,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.IndexerFeeds
                .Where(f => f.Id == id)
                .ExecuteUpdateAsync(
                    setter => setter.SetProperty(e => e.LastSync, lastSync),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(updated > 0);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error updating indexer feed last sync {IndexerFeedId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating indexer feed last sync", ErrorCodes.DbIndexerFeedError, errorCode);
        }
    }

    private async Task RecordChangeHistoryAsync(
        Guid id,
        string? name,
        string operation,
        string description,
        Guid? modifiedByUserId,
        IEnumerable<IndexerFeedSetting> settingsAdded,
        IEnumerable<IndexerFeedSetting> settingsUpdated,
        IEnumerable<IndexerFeedSetting> settingsDeleted,
        CancellationToken cancellationToken)
    {
        await historyRepository.RecordEntryAsync(
            category: AuditCategories.Integrations,
            operation: operation,
            entityId: id,
            entityType: nameof(IndexerFeed),
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
                entityType: nameof(IndexerFeedSetting),
                description: $"Added setting '{settingEntity.Key}' to indexer feed {name}",
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
                entityType: nameof(IndexerFeedSetting),
                description: $"Updated setting '{settingEntity.Key}' on indexer feed {name}",
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
                entityType: nameof(IndexerFeedSetting),
                description: $"Removed setting '{settingEntity.Key}' from indexer feed {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
