using System;
using System.Collections.Immutable;

using Magpie.Models.Configuration;
using Magpie.Models.History;
using Magpie.Models.Users;

using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;
using AuthProviderEntity = Magpie.Data.Database.Entities.AuthProvider;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Data.Database.Repositories;

internal static class EntityMappingExtensions
{
    public static User ToModel(this UserEntity entity)
    {
        return new User(
            entity.Id,
            entity.Username,
            entity.Email,
            entity.AuthProviderId,
            entity.ExternalId,
            entity.Enabled,
            entity.Role,
            entity.ProviderData.ToImmutableDictionary(pd => pd.Key, pd => pd.Value),
            entity.LastLoginAt,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static UserEntity ToEntity(
        this User model,
        AuthProviderEntity authProvider,
        Guid? createdByUserId)
    {
        return new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = model.Username,
            Email = model.Email,
            AuthProviderId = model.AuthProviderId,
            ExternalId = model.ExternalId,
            Enabled = model.Enabled,
            Role = model.Role,
            PasswordHash = null,
            PasswordSalt = null,
            FailedLoginAttempts = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId,
            AuthProvider = authProvider
        };
    }

    public static ApiKey ToModel(this ApiKeyEntity entity)
    {
        return new ApiKey(
            entity.Id,
            entity.UserId,
            entity.Name,
            entity.KeyPrefix,
            entity.Enabled,
            entity.ExpiresAt,
            entity.LastUsedAt,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static ApiKeyEntity ToEntity(
        this ApiKey model,
        UserEntity user,
        Guid? createdByUserId)
    {
        return new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            UserId = model.UserId,
            Name = model.Name,
            KeyHash = string.Empty,
            KeyPrefix = model.KeyPrefix,
            Enabled = model.Enabled,
            ExpiresAt = model.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId,
            User = user
        };
    }

    public static Setting ToModel(this SettingEntity entity)
    {
        return new Setting(entity.SettingsGroupId, entity.Key, entity.Value);
    }

    public static SettingEntity ToEntity(
        this Setting model,
        SettingsGroupEntity settingsGroup)
    {
        return new SettingEntity
        {
            Id = Guid.NewGuid(),
            SettingsGroupId = model.SettingsGroupId,
            Key = model.Key,
            Value = model.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            SettingsGroup = settingsGroup
        };
    }

    public static SettingsGroup ToModel(this SettingsGroupEntity entity)
    {
        return new SettingsGroup(entity.Id, entity.Name, entity.ExtensionId);
    }

    public static SettingsGroupEntity ToEntity(this SettingsGroup model)
    {
        return new SettingsGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            ExtensionId = model.ExtensionId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static HistoryEntry ToModel(this HistoryEntryEntity entity)
    {
        return new HistoryEntry(
            entity.EntityId,
            entity.EntityType,
            entity.Category,
            entity.Operation,
            entity.Description,
            entity.Success,
            entity.Result,
            entity.Detail,
            entity.Timestamp,
            entity.UserId);
    }

    public static HistoryEntryEntity ToEntity(this HistoryEntry model)
    {
        return new HistoryEntryEntity
        {
            Id = Guid.NewGuid(),
            EntityId = model.EntityId,
            EntityType = model.EntityType,
            Category = model.Category,
            Operation = model.Operation,
            Description = model.Description,
            Success = model.Success,
            Result = model.Result,
            Detail = model.Detail,
            Timestamp = model.Timestamp,
            UserId = model.UserId
        };
    }
}
