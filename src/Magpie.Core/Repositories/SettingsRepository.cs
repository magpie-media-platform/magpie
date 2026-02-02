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

/// <summary>
/// Repository for managing settings and settings groups.
/// </summary>
public sealed class SettingsRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    ILogger<SettingsRepository> logger) : ISettingsRepository
{
    /// <inheritdoc />
    public async Task<IEnumerable<SettingsGroup>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SettingsGroups
                .AsNoTracking()
                .Include(sg => sg.Settings)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing settings groups");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SettingsGroup?> GetByIdAsync(SettingsGroupLookupKey id, CancellationToken cancellationToken)
    {
        try
        {
            return await context.SettingsGroups
                .AsNoTracking()
                .Include(sg => sg.Settings)
                .FirstOrDefaultAsync(
                    sg => sg.ExtensionId == id.ExtensionId && sg.Name == id.Name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error getting settings group {SettingsGroupName} for extension {ExtensionId}",
                id.Name,
                id.ExtensionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PatchAsync(
        SettingsGroupPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(async _ =>
            {
                var now = DateTime.UtcNow;
                var settingsGroup = await context.SettingsGroups
                    .Include(sg => sg.Settings)
                    .FirstOrDefaultAsync(
                        sg => sg.ExtensionId == patch.ExtensionId && sg.Name == patch.Name,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (settingsGroup is null)
                {
                    settingsGroup = new SettingsGroup
                    {
                        Name = patch.Name,
                        ExtensionId = patch.ExtensionId,
                        CreatedAt = now,
                        CreatedByUserId = modifiedByUserId
                    };
                    context.SettingsGroups.Add(settingsGroup);
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Create",
                        EntityId = settingsGroup.Id.ToString(),
                        EntityType = nameof(SettingsGroup),
                        Detail = $"Created settings group {patch.Name}",
                        Timestamp = now,
                        UserId = modifiedByUserId
                    }, cancellationToken).ConfigureAwait(false);
                }

                if (patch.Settings.Add is not null)
                {
                    foreach (var (key, value) in patch.Settings.Add)
                    {
                        var setting = new Setting
                        {
                            SettingsGroupId = settingsGroup.Id,
                            Key = key,
                            Value = value ?? string.Empty,
                            CreatedAt = now,
                            CreatedByUserId = modifiedByUserId
                        };
                        settingsGroup.Settings.Add(setting);
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                        await auditRecordRepository.CreateAsync(new()
                        {
                            Category = AuditCategories.System,
                            Operation = "Create",
                            EntityId = setting.Id.ToString(),
                            EntityType = nameof(Setting),
                            Detail = $"Created setting {key} in group {patch.Name}",
                            Timestamp = now,
                            UserId = modifiedByUserId
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (patch.Settings.Update is not null)
                {
                    foreach (var (key, value) in patch.Settings.Update)
                    {
                        var setting = settingsGroup.Settings.FirstOrDefault(s => s.Key == key);
                        if (setting is not null)
                        {
                            setting.Value = value ?? string.Empty;
                            setting.ModifiedAt = now;
                            setting.ModifiedByUserId = modifiedByUserId;
                            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                            await auditRecordRepository.CreateAsync(new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = setting.Id.ToString(),
                                EntityType = nameof(Setting),
                                Detail = $"Updated setting {key} in group {patch.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            }, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                if (patch.Settings.Remove is not null)
                {
                    foreach (var key in patch.Settings.Remove)
                    {
                        var setting = settingsGroup.Settings.FirstOrDefault(s => s.Key == key);
                        if (setting is not null)
                        {
                            var settingId = setting.Id;
                            context.Settings.Remove(setting);
                            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                            await auditRecordRepository.CreateAsync(new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = settingId.ToString(),
                                EntityType = nameof(Setting),
                                Detail = $"Deleted setting {key} from group {patch.Name}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            }, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                settingsGroup.ModifiedAt = now;
                settingsGroup.ModifiedByUserId = modifiedByUserId;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error patching settings group {SettingsGroupName} for extension {ExtensionId}",
                patch.Name,
                patch.ExtensionId);
            throw;
        }
    }
}
