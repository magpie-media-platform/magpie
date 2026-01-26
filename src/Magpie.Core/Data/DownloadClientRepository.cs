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
using Magpie.Models.Downloads;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using DownloadClient = Magpie.Models.Downloads.DownloadClient;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing download client configurations.
/// </summary>
public sealed class DownloadClientRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<DownloadClientRepository> logger) : IDownloadClientRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DownloadClient>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clients = await context.DownloadClients
                .AsNoTracking()
                .Include(c => c.Settings)
                .Include(c => c.Tags)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = clients.Select(c => c.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<DownloadClient>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting download clients ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<DownloadClient>>(
                "Error retrieving download clients",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DownloadClient?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var client = await (
                from c in context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
                where c.Id == id
                select c)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(client?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download client {DownloadClientId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<DownloadClient?>(
                "Error retrieving download client",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DownloadClient?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var client = await (
                from c in context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
                where c.Name == name
                select c)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(client?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download client {DownloadClientName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<DownloadClient?>(
                "Error retrieving download client",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DownloadClient>>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var clients = await (
                from c in context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
                where c.Provider == provider
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = clients.Select(c => c.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<DownloadClient>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download clients for provider {Provider} ({ExErrorCode})",
                provider,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<DownloadClient>>(
                "Error retrieving download clients",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DownloadClient>>> GetByStatusAsync(
        DownloadClientStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            var clients = await (
                from c in context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
                where c.Status == status
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = clients.Select(c => c.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<DownloadClient>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting download clients with status {Status} ({ExErrorCode})",
                status,
                errorCode ?? "");
            return Result.Fail<IReadOnlyList<DownloadClient>>(
                "Error retrieving download clients",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DownloadClient>>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clients = await (
                from c in context.DownloadClients.AsNoTracking().Include(c => c.Settings).Include(c => c.Tags)
                where c.Enabled
                select c)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = clients.Select(c => c.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<DownloadClient>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting enabled download clients ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<DownloadClient>>(
                "Error retrieving download clients",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DownloadClient>> CreateAsync(
        DownloadClient model,
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
            context.DownloadClients.Add(entity);

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Integrations,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(DownloadClient),
                description: $"Created download client {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in entity.Settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Integrations,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: entity.Id,
                    entityType: nameof(DownloadClientSetting),
                    description: $"Added setting '{setting.Key}' to download client {model.Name}",
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
                    entityType: nameof(DownloadClientTag),
                    description: $"Added tag '{tag.TagName}' to download client {model.Name}",
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
                "Error creating download client {DownloadClientName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<DownloadClient>(
                "Error creating download client",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        DownloadClient model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.DownloadClients
                .Include(c => c.Settings)
                .Include(c => c.Tags)
                .FirstOrDefaultAsync(c => c.Id == model.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var settingsAdded = new List<DownloadClientSetting>();
            var settingsUpdated = new List<DownloadClientSetting>();
            var settingsDeleted = new List<DownloadClientSetting>();
            var tagsAdded = new List<string>();
            var tagsDeleted = new List<string>();

            entity.Name = model.Name;
            entity.Provider = model.Provider;
            entity.Enabled = model.Enabled;
            entity.Status = model.Status;
            entity.LastChecked = model.LastChecked;
            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            foreach (var kv in model.Settings.Where(kv => !entity.Settings.Any(s => s.Key == kv.Key)).ToList())
            {
                var entry = context.DownloadClientSettings.Add(new()
                {
                    DownloadClientId = entity.Id,
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
                    DownloadClientId = entity.Id,
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
                    description: $"Updated download client {entity.Name}",
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
                "Error updating download client {DownloadClientId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating download client", ErrorCodes.DbDownloadClientError, errorCode);
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

            var entity = await context.DownloadClients
                .Include(c => c.Settings)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.DownloadClientSettings
                .Where(s => s.DownloadClientId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.DownloadClientTags
                .Where(t => t.DownloadClientId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.DownloadClients
                .Where(c => c.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await RecordChangeHistoryAsync(
                    id: id,
                    name: entity.Name,
                    operation: "Delete",
                    description: $"Deleted download client {entity.Name}",
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
                "Error deleting download client {DownloadClientId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting download client", ErrorCodes.DbDownloadClientError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        DownloadClientPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.DownloadClients
                .Include(c => c.Settings)
                .Include(c => c.Tags)
                .FirstOrDefaultAsync(c => c.Id == patch.Id, cancellationToken)
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

            var settingsAdded = new List<DownloadClientSetting>();
            var settingsUpdated = new List<DownloadClientSetting>();
            var settingsDeleted = new List<DownloadClientSetting>();
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
                    var entry = context.DownloadClientSettings.Add(new()
                    {
                        DownloadClientId = patch.Id,
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
                        DownloadClientId = patch.Id,
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
                    description: $"Updated download client {entity.Name}",
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
                "Error patching download client {DownloadClientId} ({ExErrorCode})",
                patch.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error patching download client", ErrorCodes.DbDownloadClientError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateStatusAsync(
        Guid id,
        DownloadClientStatus status,
        DateTimeOffset lastChecked,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await context.DownloadClients
                .Where(c => c.Id == id)
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
                "Error updating download client status {DownloadClientId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>(
                "Error updating download client status",
                ErrorCodes.DbDownloadClientError,
                errorCode);
        }
    }

    private async Task RecordChangeHistoryAsync(
        Guid id,
        string? name,
        string operation,
        string description,
        Guid? modifiedByUserId,
        IEnumerable<DownloadClientSetting> settingsAdded,
        IEnumerable<DownloadClientSetting> settingsUpdated,
        IEnumerable<DownloadClientSetting> settingsDeleted,
        IEnumerable<string> tagsAdded,
        IEnumerable<string> tagsDeleted,
        CancellationToken cancellationToken)
    {
        await historyRepository.RecordEntryAsync(
            category: AuditCategories.Integrations,
            operation: operation,
            entityId: id,
            entityType: nameof(DownloadClient),
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
                entityType: nameof(DownloadClientSetting),
                description: $"Added setting '{settingEntity.Key}' to download client {name}",
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
                entityType: nameof(DownloadClientSetting),
                description: $"Updated setting '{settingEntity.Key}' on download client {name}",
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
                entityType: nameof(DownloadClientSetting),
                description: $"Removed setting '{settingEntity.Key}' from download client {name}",
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
                entityType: nameof(DownloadClientTag),
                description: $"Added tag '{tag}' to download client {name}",
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
                entityType: nameof(DownloadClientTag),
                description: $"Removed tag '{tag}' from download client {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
