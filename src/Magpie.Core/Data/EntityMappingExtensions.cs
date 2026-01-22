using System;

using Magpie.Models.Configuration;
using Magpie.Models.Downloads;
using Magpie.Models.History;
using Magpie.Models.Indexers;
using Magpie.Models.MediaServers;
using Magpie.Models.RootFolders;
using Magpie.Models.Users;

using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;
using AuthProviderEntity = Magpie.Data.Database.Entities.AuthProvider;
using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using IndexerEntity = Magpie.Data.Database.Entities.Indexer;
using IndexerFeedEntity = Magpie.Data.Database.Entities.IndexerFeed;
using MediaServerEntity = Magpie.Data.Database.Entities.MediaServer;
using RootFolderEntity = Magpie.Data.Database.Entities.RootFolder;
using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Core.Data;

internal static class EntityMappingExtensions
{
    public static UserEntity CreateEntity(this User model, Guid? createdByUserId)
    {
        return new UserEntity
        {
            Username = model.Username,
            Email = model.Email,
            ExternalId = model.ExternalId,
            Enabled = model.Enabled,
            Role = model.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static ApiKeyEntity CreateEntity(this ApiKey model, Guid? createdByUserId)
    {
        return new ApiKeyEntity
        {
            Name = model.Name,
            KeyPrefix = model.KeyPrefix,
            Enabled = model.Enabled,
            ExpiresAt = model.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static SettingEntity CreateEntity(this Setting model, Guid? createdByUserId)
    {
        return new SettingEntity
        {
            Key = model.Key,
            Value = model.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }
    public static SettingsGroupEntity CreateEntity(this SettingsGroup model, Guid? createdByUserId)
    {
        return new SettingsGroupEntity
        {
            Name = model.Name,
            ExtensionId = model.ExtensionId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static HistoryEntryEntity CreateEntity(this HistoryEntry model)
    {
        return new HistoryEntryEntity
        {
            EntityId = model.EntityId,
            ParentEntityId = model.ParentEntityId,
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

    public static AuthProviderEntity CreateEntity(
        this AuthProvider model,
        Guid? createdByUserId)
    {
        return new AuthProviderEntity
        {
            Name = model.Name,
            Provider = model.Provider,
            Enabled = model.Enabled,
            IsDefault = model.IsDefault,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static RootFolderEntity CreateEntity(this RootFolder model, Guid? createdByUserId)
    {
        return new RootFolderEntity
        {
            Name = model.Name,
            Path = model.Path,
            ContentTypeId = model.ContentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static MediaServerEntity CreateEntity(this MediaServer model, Guid? createdByUserId)
    {
        return new MediaServerEntity
        {
            Name = model.Name,
            Provider = model.Provider,
            Enabled = model.Enabled,
            Status = model.Status,
            LastChecked = model.LastChecked,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static DownloadClientEntity CreateEntity(this DownloadClient model, Guid? createdByUserId)
    {
        return new DownloadClientEntity
        {
            Name = model.Name,
            Provider = model.Provider,
            Enabled = model.Enabled,
            Status = model.Status,
            LastChecked = model.LastChecked,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static IndexerFeedEntity CreateEntity(this IndexerFeed model, Guid? createdByUserId)
    {
        return new IndexerFeedEntity
        {
            Name = model.Name,
            Description = model.Description,
            Provider = model.Provider,
            Enabled = model.Enabled,
            LastSync = model.LastSync,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static IndexerEntity CreateEntity(this Indexer model, Guid? createdByUserId)
    {
        return new IndexerEntity
        {
            Name = model.Name,
            DefinitionId = model.DefinitionId,
            Enabled = model.Enabled,
            Status = model.Status,
            LastChecked = model.LastChecked,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }
}
