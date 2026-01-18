using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Data;
using Magpie.Models.Configuration;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using static Magpie.Abstractions.Data.ISettingsRepository;

using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;

namespace Magpie.Data.Database.Repositories;

/// <summary>
/// Repository for managing settings and settings groups.
/// </summary>
public sealed class SettingsRepository(
    MagpieDbContext context,
    ILogger<SettingsRepository> logger) : ISettingsRepository
{
    /// <inheritdoc />
    public async Task<Result<Setting?>> GetByIdAsync(SettingKey id, CancellationToken cancellationToken)
    {
        try
        {
            var setting = await (
                from s in context.Settings
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
            var settingsGroup = await context.SettingsGroups.FindAsync([model.SettingsGroupId], cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup == null)
            {
                return Result.Fail<Setting>("Settings group not found", ErrorCodes.DbSettingsGroupValidationError);
            }

            var entity = model.ToEntity(settingsGroup);

            context.Settings.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
            var updated = await context.Settings
                .Where(s => s.SettingsGroupId == model.SettingsGroupId)
                .Where(s => s.Key == model.Key)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Value, model.Value)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow)
                        .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                    cancellationToken)
                .ConfigureAwait(false);

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
            var deleted = await (
                from s in context.Settings
                join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
                where g.Name == id.GroupName
                where g.ExtensionId == id.ExtensionId
                where s.Key == id.Key
                select s)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

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
                from g in context.SettingsGroups
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
    public async Task<Result> UpsertSettingAsync(
        SettingKey key,
        string? value,
        Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        var setting = await (
            from s in context.Settings
            join g in context.SettingsGroups on s.SettingsGroupId equals g.Id
            where s.Key == key.Key
            where g.Name == key.GroupName
            where g.ExtensionId == key.ExtensionId
            select s)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (value == null)
        {
            if (setting == null)
            {
                return Result.Success();
            }
            context.Settings.Remove(setting);
        }
        else if (setting == null)
        {
            var settingsGroup = await (
                from g in context.SettingsGroups
                where g.Name == key.GroupName
                where g.ExtensionId == key.ExtensionId
                select g)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup == null)
            {
                settingsGroup = new SettingsGroupEntity
                {
                    Id = Guid.NewGuid(),
                    Name = key.GroupName,
                    ExtensionId = key.ExtensionId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId,
                };
                context.SettingsGroups.Add(settingsGroup);
            }
            setting = new SettingEntity
            {
                Id = Guid.NewGuid(),
                SettingsGroupId = settingsGroup.Id,
                Key = key.Key,
                Value = value,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = modifiedByUserId,
                SettingsGroup = settingsGroup,
            };
            context.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.ModifiedAt = DateTimeOffset.UtcNow;
            setting.ModifiedByUserId = modifiedByUserId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
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
                from s in context.Settings
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
            var entity = model.ToEntity();

            context.SettingsGroups.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
            var updated = await context.SettingsGroups
                .Where(sg => sg.Id == model.Id)
                .ExecuteUpdateAsync(
                    setter => setter
                        .SetProperty(e => e.Name, model.Name)
                        .SetProperty(e => e.ExtensionId, model.ExtensionId)
                        .SetProperty(e => e.ModifiedAt, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);

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
            var deleted = await (
                from sg in context.SettingsGroups
                where sg.Name == id.Name
                where sg.ExtensionId == id.ExtensionId
                select sg)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(deleted > 0);
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
            var settingsGroup = await (
                from sg in context.SettingsGroups.Include(sg => sg.Settings)
                join s in context.Settings on sg.Id equals s.SettingsGroupId
                where sg.Name == patch.Name
                select sg)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settingsGroup == null)
            {
                return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingsGroupValidationError);
            }

            foreach (var setting in patch.Settings.Add ?? new Dictionary<string, string> { })
            {
                if (settingsGroup.Settings.Any(s => s.Key == setting.Key))
                {
                    return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingValidationError);
                }
                context.Settings.Add(new SettingEntity
                {
                    Id = Guid.NewGuid(),
                    Key = setting.Key,
                    Value = setting.Value,
                    SettingsGroupId = settingsGroup.Id,
                    SettingsGroup = settingsGroup,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByUserId = modifiedByUserId,
                    ModifiedAt = DateTimeOffset.UtcNow,
                    ModifiedByUserId = modifiedByUserId,
                });
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
            }
            foreach (var setting in patch.Settings.Remove ?? [])
            {
                var settingEntity = settingsGroup.Settings.FirstOrDefault(s => s.Key == setting);
                if (settingEntity is null)
                {
                    return Result.Fail<bool>("Error saving settings", ErrorCodes.DbSettingValidationError);
                }
                settingsGroup.Settings.Remove(settingEntity);
            }
            settingsGroup.ModifiedAt = DateTimeOffset.UtcNow;
            settingsGroup.ModifiedByUserId = modifiedByUserId;

            var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(updated > 0);
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
            var settings = await context.Settings.ToListAsync(cancellationToken)
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
            var groups = await context.SettingsGroups.ToListAsync(cancellationToken).ConfigureAwait(false);
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
