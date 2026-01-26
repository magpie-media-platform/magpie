using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Data.Database;
using Magpie.Models.Configuration;
using Magpie.Models.History;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using static Magpie.Abstractions.Data.ISettingsRepository;

using SettingEntity = Magpie.Data.Database.Entities.Setting;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing settings and settings groups.
/// </summary>
public sealed class SettingsRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<SettingsRepository> logger) : ISettingsRepository
{
    /// <inheritdoc />
    public async Task<Result<Setting?>> GetByIdAsync(SettingKey id, CancellationToken cancellationToken)
    {
        try
        {
            var setting = await (
                from s in context.Settings.AsNoTracking()
                join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
                where s.Key == id.Key
                where g.Name == id.GroupName
                where g.ExtensionId == id.ExtensionId
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(setting?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting setting {SettingKey} [{ExtensionId}/{SettingsGroupName}] ({ExErrorCode})",
                id.Key,
                id.ExtensionId,
                id.GroupName,
                errorCode ?? "");
            return Result.Fail<Setting?>("Error retrieving setting", ErrorCodes.DbSettingError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Setting>> CreateAsync(
        Setting model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var settingsGroup = await context.SettingsGroups.FindAsync([model.SettingsGroupId], cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup is null)
            {
                return Result.Fail<Setting>("Settings group not found", ErrorCodes.DbSettingsGroupValidationError);
            }

            var entity = model.CreateEntity(createdByUserId) with
            {
                SettingsGroupId = settingsGroup.Id
            };

            context.Settings.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                parentEntityId: settingsGroup.Id,
                entityType: nameof(Setting),
                description: $"Created setting {model.Key} in group {settingsGroup.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating setting {SettingKey} [{SettingsGroupId}] ({ExErrorCode})",
                model.Key,
                model.SettingsGroupId,
                errorCode ?? "");
            return Result.Fail<Setting>("Error creating setting", ErrorCodes.DbSettingError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        Setting model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var settingEntity = await (
                from s in context.Settings
                where s.SettingsGroupId == model.SettingsGroupId
                where s.Key == model.Key
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settingEntity is not null)
            {
                settingEntity.Value = model.Value;
                settingEntity.ModifiedAt = DateTimeOffset.UtcNow;
                settingEntity.ModifiedByUserId = modifiedByUserId;
            }

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0 && settingEntity is not null)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: settingEntity.Id,
                    parentEntityId: model.SettingsGroupId,
                    entityType: nameof(Setting),
                    description: $"Updated setting {model.Key}",
                    userId: modifiedByUserId,
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
                "Error updating setting {SettingKey} [{SettingsGroupId}] ({ExErrorCode})",
                model.Key,
                model.SettingsGroupId,
                errorCode ?? "");
            return Result.Fail<bool>("Error saving setting", ErrorCodes.DbSettingError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        SettingKey id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var setting = await (
                from s in context.Settings
                join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
                where g.Name == id.GroupName
                where g.ExtensionId == id.ExtensionId
                where s.Key == id.Key
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (setting is not null)
            {
                context.Settings.Remove(setting);
            }

            var deleted = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: setting?.Id,
                    parentEntityId: setting?.SettingsGroupId,
                    entityType: nameof(Setting),
                    description: $"Deleted setting {id.Key} from group {id.GroupName}",
                    userId: deletedByUserId,
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
                "Error deleting setting {SettingKey} [{ExtensionId}/{SettingsGroupName}] ({ExErrorCode})",
                id.Key,
                id.ExtensionId,
                id.GroupName,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting setting", ErrorCodes.DbSettingError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<SettingsGroup?>> GetByIdAsync(
        SettingsGroupKey id,
        CancellationToken cancellationToken)
    {
        try
        {
            var group = await (
                from g in context.SettingsGroups.AsNoTracking()
                where g.Name == id.Name
                where g.ExtensionId == id.ExtensionId
                select g)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(group?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting settings group {SettingsGroupName} [{ExtensionId}] ({ExErrorCode})",
                id.Name,
                id.ExtensionId,
                errorCode ?? "");
            return Result.Fail<SettingsGroup?>("Error getting settings", ErrorCodes.DbSettingsGroupError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Unit>> UpsertSettingAsync(
        SettingKey key,
        string? value,
        Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var setting = await (
                from s in context.Settings
                join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
                where s.Key == key.Key
                where g.Name == key.GroupName
                where g.ExtensionId == key.ExtensionId
                select s)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            string operation;
            if (value is null)
            {
                if (setting is null)
                {
                    return Result.Success();
                }
                context.Settings.Remove(setting);
                operation = "Delete";
            }
            else if (setting is null)
            {
                var settingsGroup = await (
                    from g in context.SettingsGroups
                    where g.Name == key.GroupName
                    where g.ExtensionId == key.ExtensionId
                    select g)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (settingsGroup is null)
                {
                    settingsGroup = new()
                    {
                        Name = key.GroupName,
                        ExtensionId = key.ExtensionId,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedByUserId = modifiedByUserId,
                    };
                    context.SettingsGroups.Add(settingsGroup);
                }
                setting = new()
                {
                    Key = key.Key,
                    Value = value,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId,
                };
                settingsGroup.Settings.Add(setting);
                operation = "Create";
            }
            else
            {
                setting.Value = value;
                setting.ModifiedAt = DateTimeOffset.UtcNow;
                setting.ModifiedByUserId = modifiedByUserId;
                operation = "Update";
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: operation,
                entityId: setting.Id,
                parentEntityId: setting.SettingsGroupId,
                entityType: nameof(Setting),
                description: $"{operation} setting {key.Key} in group {key.GroupName}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error upserting setting {SettingKey} [{ExtensionId}/{SettingsGroupName}] ({ExErrorCode})",
                key.Key,
                key.ExtensionId,
                key.GroupName,
                errorCode ?? "");
            return Result.Fail<Unit>("Error saving setting", ErrorCodes.DbSettingError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Setting>>> GetSettingsAsync(
        Guid? extensionId,
        string settingsGroupName,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await (
                from s in context.Settings.AsNoTracking()
                join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
                where g.Name == settingsGroupName
                where g.ExtensionId == extensionId
                select s)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = settings.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Setting>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting settings ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Setting>>(
                "Error retrieving settings",
                ErrorCodes.DbSettingError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<SettingsGroup>> CreateAsync(
        SettingsGroup model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = model.CreateEntity(createdByUserId);

            context.SettingsGroups.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(SettingsGroup),
                description: $"Created settings group {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(entity.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error creating settings group {SettingsGroupName} [{ExtensionId}] ({ExErrorCode})",
                model.Name,
                model.ExtensionId,
                errorCode ?? "");
            return Result.Fail<SettingsGroup>(
                "Error creating settings group",
                ErrorCodes.DbSettingsGroupError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        SettingsGroup model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var updated = await context.SettingsGroups
                .Where(sg => sg.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.ExtensionId, model.ExtensionId)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

            if (updated > 0)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: model.Id,
                    entityType: nameof(SettingsGroup),
                    description: $"Updated settings group {model.Name}",
                    userId: modifiedByUserId,
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
                "Error updating settings group {SettingsGroupId} [{ExtensionId}] ({ExErrorCode})",
                model.Id,
                model.ExtensionId,
                errorCode ?? "");
            return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingsGroupError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> DeleteAsync(
        SettingsGroupKey id,
        Guid? deletedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var settingsGroup = await (
                from sg in context.SettingsGroups.Include(sg => sg.Settings)
                where sg.Name == id.Name
                where sg.ExtensionId == id.ExtensionId
                select sg)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup is null)
            {
                return Result.Success(false);
            }

            var settings = settingsGroup.Settings.ToList();

            foreach (var setting in settings)
            {
                context.Settings.Remove(setting);
            }

            context.SettingsGroups.Remove(settingsGroup);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.System,
                operation: "Delete",
                entityId: settingsGroup.Id,
                entityType: nameof(SettingsGroup),
                description: $"Deleted settings group {id.Name}",
                userId: deletedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: setting.Id,
                    parentEntityId: settingsGroup.Id,
                    entityType: nameof(Setting),
                    description: $"Deleted setting {setting.Key} from group {id.Name}",
                    userId: deletedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error deleting settings group {SettingsGroupName} for extension {ExtensionId} ({ExErrorCode})",
                id.Name,
                id.ExtensionId,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting setting", ErrorCodes.DbSettingsGroupError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> PatchAsync(
        SettingsGroupPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var settingsGroup = await (
                from sg in context.SettingsGroups.Include(sg => sg.Settings)
                join s in context.Settings on sg.Id equals s.SettingsGroupId
                where sg.Name == patch.Name
                select sg)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup is null)
            {
                return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingsGroupValidationError);
            }

            var addedSettings = new List<SettingEntity>();
            var updatedSettings = new List<SettingEntity>();
            var deletedSettings = new List<SettingEntity>();

            foreach (var setting in patch.Settings.Add ?? new Dictionary<string, string>())
            {
                if (settingsGroup.Settings.Any(s => s.Key == setting.Key))
                {
                    return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingValidationError);
                }
                var newSetting = new SettingEntity
                {
                    Key = setting.Key,
                    Value = setting.Value,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId,
                    ModifiedAt = DateTimeOffset.UtcNow,
                    ModifiedByUserId = modifiedByUserId,
                };
                settingsGroup.Settings.Add(newSetting);
                addedSettings.Add(newSetting);
            }
            foreach (var setting in patch.Settings.Update ?? new Dictionary<string, string>())
            {
                var settingEntity = settingsGroup.Settings.FirstOrDefault(s => s.Key == setting.Key);
                if (settingEntity is null)
                {
                    return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingValidationError);
                }
                settingEntity.Value = setting.Value;
                settingEntity.ModifiedAt = DateTimeOffset.UtcNow;
                settingEntity.ModifiedByUserId = modifiedByUserId;
                updatedSettings.Add(settingEntity);
            }
            foreach (var setting in patch.Settings.Remove ?? [])
            {
                var settingEntity = settingsGroup.Settings.FirstOrDefault(s => s.Key == setting);
                if (settingEntity is null)
                {
                    return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingValidationError);
                }
                context.Settings.Remove(settingEntity);
                deletedSettings.Add(settingEntity);
            }
            settingsGroup.ModifiedAt = DateTimeOffset.UtcNow;
            settingsGroup.ModifiedByUserId = modifiedByUserId;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (var setting in addedSettings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: settingsGroup.Id,
                    entityType: nameof(Setting),
                    description: $"Created setting {setting.Key} in group {patch.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            foreach (var setting in updatedSettings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Update",
                    entityId: setting.Id,
                    parentEntityId: settingsGroup.Id,
                    entityType: nameof(Setting),
                    description: $"Updated setting {setting.Key} in group {patch.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            foreach (var setting in deletedSettings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.System,
                    operation: "Delete",
                    entityId: setting.Id,
                    parentEntityId: settingsGroup.Id,
                    entityType: nameof(Setting),
                    description: $"Deleted setting {setting.Key} from group {patch.Name}",
                    userId: modifiedByUserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error patching settings group {SettingsGroupName} [{ExtensionId}] ({ExErrorCode})",
                patch.Name,
                patch.ExtensionId,
                errorCode ?? "");
            return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingsGroupError, errorCode);
        }
    }


    async Task<Result<IReadOnlyList<Setting>>> IRepository<Setting>.GetAllAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await context.Settings
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = settings.Select(s => s.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<Setting>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting settings ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<Setting>>(
                "Error retrieving settings",
                ErrorCodes.DbSettingError,
                errorCode);
        }
    }

    async Task<Result<IReadOnlyList<SettingsGroup>>> IRepository<SettingsGroup>.GetAllAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var groups = await context.SettingsGroups
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = groups.Select(g => g.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<SettingsGroup>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting settings groups ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<SettingsGroup>>(
                "Error retrieving settings",
                ErrorCodes.DbSettingsGroupError,
                errorCode);
        }
    }
}
