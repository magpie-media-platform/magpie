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
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using AuthProvider = Magpie.Models.Users.AuthProvider;

namespace Magpie.Core.Data;

/// <summary>
/// Repository for managing authentication provider configurations.
/// </summary>
public sealed class AuthProviderRepository(
    MagpieDbContext context,
    IHistoryRepository historyRepository,
    ILogger<AuthProviderRepository> logger) : IAuthProviderRepository
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AuthProvider>>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entities = await context.AuthProviders
                .AsNoTracking()
                .Include(p => p.Settings)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = entities.Select(p => p.ToModel()).ToImmutableList();

            return Result.Success<IReadOnlyList<AuthProvider>>(result);
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(ex, "Error getting auth providers ({ExErrorCode})", errorCode ?? "");
            return Result.Fail<IReadOnlyList<AuthProvider>>(
                "Error retrieving auth providers",
                ErrorCodes.DbAuthProviderError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AuthProvider?>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from p in context.AuthProviders.AsNoTracking().Include(p => p.Settings)
                where p.Id == id
                select p)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting auth provider {AuthProviderId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<AuthProvider?>(
                "Error retrieving auth provider",
                ErrorCodes.DbAuthProviderError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AuthProvider?>> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var entity = await (
                from p in context.AuthProviders.AsNoTracking().Include(p => p.Settings)
                where p.Name == name
                select p)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(entity?.ToModel());
        }
        catch (Exception ex)
        {
            ex.TryGetErrorCode(out var errorCode);
            logger.LogError(
                ex,
                "Error getting auth provider {AuthProviderName} ({ExErrorCode})",
                name,
                errorCode ?? "");
            return Result.Fail<AuthProvider?>(
                "Error retrieving auth provider",
                ErrorCodes.DbAuthProviderError,
                errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<AuthProvider>> CreateAsync(
        AuthProvider model,
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
            context.AuthProviders.Add(entity);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Create",
                entityId: entity.Id,
                entityType: nameof(AuthProvider),
                description: $"Created auth provider {model.Name}",
                userId: createdByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var setting in entity.Settings)
            {
                await historyRepository.RecordEntryAsync(
                    category: AuditCategories.Security,
                    operation: "Create",
                    entityId: setting.Id,
                    parentEntityId: entity.Id,
                    entityType: nameof(AuthProviderSetting),
                    description: $"Added setting '{setting.Key}' to auth provider {model.Name}",
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
                "Error creating auth provider {AuthProviderName} ({ExErrorCode})",
                model.Name,
                errorCode ?? "");
            return Result.Fail<AuthProvider>("Error creating auth provider", ErrorCodes.DbAuthProviderError, errorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> UpdateAsync(
        AuthProvider model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var entity = await context.AuthProviders
                .Include(p => p.Settings)
                .FirstOrDefaultAsync(p => p.Id == model.Id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            var settingsAdded = new List<AuthProviderSetting>();
            var settingsUpdated = new List<AuthProviderSetting>();
            var settingsDeleted = new List<AuthProviderSetting>();

            entity.Name = model.Name;
            entity.Provider = model.Provider;
            entity.Enabled = model.Enabled;
            entity.IsDefault = model.IsDefault;
            entity.ModifiedAt = DateTimeOffset.UtcNow;
            entity.ModifiedByUserId = modifiedByUserId;

            foreach (var kv in model.Settings.Where(kv => !entity.Settings.Any(s => s.Key == kv.Key)).ToList())
            {
                var entry = context.AuthProviderSettings.Add(new()
                {
                    AuthProviderId = entity.Id,
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
                    description: $"Updated auth provider {entity.Name}",
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
                "Error updating auth provider {AuthProviderId} ({ExErrorCode})",
                model.Id,
                errorCode ?? "");
            return Result.Fail<bool>("Error updating auth provider", ErrorCodes.DbAuthProviderError, errorCode);
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

            var entity = await context.AuthProviders
                .Include(p => p.Settings)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result.Success(false);
            }

            await context.AuthProviderSettings
                .Where(s => s.AuthProviderId == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.AuthProviders
                .Where(p => p.Id == id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                await RecordChangeHistoryAsync(
                    id: id,
                    name: entity.Name,
                    operation: "Delete",
                    description: $"Deleted auth provider {entity.Name}",
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
                "Error deleting auth provider {AuthProviderId} ({ExErrorCode})",
                id,
                errorCode ?? "");
            return Result.Fail<bool>("Error deleting auth provider", ErrorCodes.DbAuthProviderError, errorCode);
        }
    }

    private async Task RecordChangeHistoryAsync(
        Guid id,
        string? name,
        string operation,
        string description,
        Guid? modifiedByUserId,
        IEnumerable<AuthProviderSetting> settingsAdded,
        IEnumerable<AuthProviderSetting> settingsUpdated,
        IEnumerable<AuthProviderSetting> settingsDeleted,
        CancellationToken cancellationToken)
    {
        await historyRepository.RecordEntryAsync(
            category: AuditCategories.Security,
            operation: operation,
            entityId: id,
            entityType: nameof(AuthProvider),
            description: description,
            userId: modifiedByUserId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var settingEntity in settingsAdded)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Create",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(AuthProviderSetting),
                description: $"Added setting '{settingEntity.Key}' to auth provider {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var settingEntity in settingsUpdated)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Update",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(AuthProviderSetting),
                description: $"Updated setting '{settingEntity.Key}' on auth provider {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        foreach (var settingEntity in settingsDeleted)
        {
            await historyRepository.RecordEntryAsync(
                category: AuditCategories.Security,
                operation: "Delete",
                entityId: settingEntity.Id,
                parentEntityId: id,
                entityType: nameof(AuthProviderSetting),
                description: $"Removed setting '{settingEntity.Key}' from auth provider {name}",
                userId: modifiedByUserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
