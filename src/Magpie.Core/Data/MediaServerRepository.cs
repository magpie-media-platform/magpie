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
using Magpie.Models.MediaServers;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MediaServer = Magpie.Models.MediaServers.MediaServer;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing media server configurations.
/// </summary>
public sealed class MediaServerRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<MediaServerRepository> logger) : IMediaServerRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MediaServer>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var servers = await context.MediaServers
                .AsNoTracking()
                .Include(s => s.Settings)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = servers.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MediaServer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting media servers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<MediaServer>>(
                "Error retrieving media servers",
                ErrorCodes.DbMediaServerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MediaServer?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var server = await (
                from s in context.MediaServers.AsNoTracking().Include(s => s.Settings)
                where s.Id == id
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(server?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting media server {MediaServerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<MediaServer?>("Error retrieving media server", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MediaServer?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var server = await (
                from s in context.MediaServers.AsNoTracking().Include(s => s.Settings)
                where s.Name == name
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(server?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting media server {MediaServerName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<MediaServer?>("Error retrieving media server", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MediaServer>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var servers = await (
                from s in context.MediaServers.AsNoTracking().Include(s => s.Settings)
                where s.Provider == provider
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = servers.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MediaServer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting media servers for provider {Provider} ({ExErrorCode})",
                provider,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<MediaServer>>(
                "Error retrieving media servers",
                ErrorCodes.DbMediaServerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MediaServer>>> GetByStatusAsync(
        MediaServerStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            var servers = await (
                from s in context.MediaServers.AsNoTracking().Include(s => s.Settings)
                where s.Status == status
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = servers.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MediaServer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting media servers with status {Status} ({ExErrorCode})",
                status,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<MediaServer>>(
                "Error retrieving media servers",
                ErrorCodes.DbMediaServerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MediaServer>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var servers = await (
                from s in context.MediaServers.AsNoTracking().Include(s => s.Settings)
                where s.Enabled
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = servers.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<MediaServer>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled media servers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<MediaServer>>(
                "Error retrieving media servers",
                ErrorCodes.DbMediaServerError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<MediaServer>> CreateAsync(
        MediaServer model,
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
            context.MediaServers.Add(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(MediaServer),
                description: $"Created media server {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in entity.Settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: entity.Id,
                    entityType: nameof(MediaServerSetting),
                    description: $"Added setting '{setting.Key}' to media server {model.Name}",
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
                "Error creating media server {MediaServerName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<MediaServer>("Error creating media server", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        MediaServer model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.MediaServers
                .Include(s => s.Settings)
                .FirstOrDefaultAsync(s => s.Id == model.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var settingsAdded = new List<MediaServerSetting>();
            var settingsUpdated = new List<MediaServerSetting>();
            var settingsDeleted = new List<MediaServerSetting>();

            entity.Name = model.Name;
            entity.Provider = model.Provider;
            entity.Enabled = model.Enabled;
            entity.Status = model.Status;
            entity.LastChecked = model.LastChecked;
            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            foreach (var kv in model.Settings.Where(kv => !entity.Settings.Any(s => s.Key == kv.Key)).ToList())
            {
                var entry = context.MediaServerSettings.Add(new()
                {
                    MediaServerId = entity.Id,
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
                    description: $"Updated media server {entity.Name}",
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
                "Error updating media server {MediaServerId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating media server", ErrorCodes.DbMediaServerError, errorCode);
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

            var entity = await context.MediaServers
                .Include(s => s.Settings)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.MediaServerSettings
                .Where(s => s.MediaServerId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.MediaServers
                .Where(s => s.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await RecordChangeHistoryAsync(
                    id: id,
                    name: entity.Name,
                    operation: "Delete",
                    description: $"Deleted media server {entity.Name}",
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
                "Error deleting media server {MediaServerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting media server", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        MediaServerPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.MediaServers
                .Include(s => s.Settings)
                .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
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

            var settingsAdded = new List<MediaServerSetting>();
            var settingsUpdated = new List<MediaServerSetting>();
            var settingsDeleted = new List<MediaServerSetting>();

            if (patch.Settings?.Add is not null)
            {
                foreach (var settingPatch in patch.Settings.Add)
                {
                    if (entity.Settings.Any(s => s.Key == settingPatch.Key))
                    {
                        continue;
                    }
                    var entry = context.MediaServerSettings.Add(new()
                    {
                        MediaServerId = patch.Id,
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
                    description: $"Updated media server {entity.Name}",
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
                "Error patching media server {MediaServerId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching media server", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        MediaServerStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.MediaServers
                .Where(s => s.Id == id)
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
                "Error updating media server status {MediaServerId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating media server status", ErrorCodes.DbMediaServerError, errorCode);
        }
    }

    private async Task RecordChangeHistoryAsync(
        Guid id,
        string? name,
        string operation,
        string description,
        Guid? modifiedByUserId,
        IEnumerable<MediaServerSetting> settingsAdded,
        IEnumerable<MediaServerSetting> settingsUpdated,
        IEnumerable<MediaServerSetting> settingsDeleted,
        CancellationToken cancellationToken)
    {
        await historyRepository.RecordEntryAsync(
            category: AuditCategories.Integrations,
            operation: operation,
            entityId: id,
            entityType: nameof(MediaServer),
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
                entityType: nameof(MediaServerSetting),
                description: $"Added setting '{settingEntity.Key}' to media server {name}",
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
                entityType: nameof(MediaServerSetting),
                description: $"Updated setting '{settingEntity.Key}' on media server {name}",
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
                entityType: nameof(MediaServerSetting),
                description: $"Removed setting '{settingEntity.Key}' from media server {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
