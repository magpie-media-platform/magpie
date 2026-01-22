using System.Collections.Immutable;
using System.Linq;

using Magpie.Models;
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
using TagEntity = Magpie.Data.Database.Entities.Tag;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Core.Data;

internal static class ModelMappingExtensions
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

    public static Setting ToModel(this SettingEntity entity)
    {
        return new Setting(entity.SettingsGroupId, entity.Key, entity.Value);
    }

    public static SettingsGroup ToModel(this SettingsGroupEntity entity)
    {
        return new SettingsGroup(entity.Id, entity.Name, entity.ExtensionId);
    }

    public static HistoryEntry ToModel(this HistoryEntryEntity entity)
    {
        return new HistoryEntry(
            entity.EntityId,
            entity.ParentEntityId,
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

    public static Tag ToModel(this TagEntity entity)
    {
        return new Tag(entity.Name);
    }

    public static AuthProvider ToModel(this AuthProviderEntity entity)
    {
        return new AuthProvider(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.IsDefault,
            entity.Settings.ToImmutableDictionary(s => s.Key, s => s.Value),
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static RootFolder ToModel(this RootFolderEntity entity)
    {
        return new RootFolder(
            entity.Id,
            entity.Name,
            entity.Path,
            entity.ContentTypeId,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static MediaServer ToModel(this MediaServerEntity entity)
    {
        return new MediaServer(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            entity.Settings.ToImmutableDictionary(s => s.Key, s => s.Value),
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static DownloadClient ToModel(this DownloadClientEntity entity)
    {
        return new DownloadClient(
            entity.Id,
            entity.Name,
            entity.Provider,
            entity.Enabled,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.Settings.ToImmutableDictionary(s => s.Key, s => s.Value),
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static IndexerFeed ToModel(this IndexerFeedEntity entity)
    {
        return new IndexerFeed(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Provider,
            entity.Enabled,
            entity.Settings.ToImmutableDictionary(s => s.Key, s => s.Value),
            entity.LastSync,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static Indexer ToModel(this IndexerEntity entity)
    {
        return new Indexer(
            entity.Id,
            entity.DefinitionId,
            entity.Name,
            entity.Enabled,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.Settings.ToImmutableDictionary(s => s.Key, s => s.Value),
            entity.Status,
            entity.LastChecked,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }
}
