using System;
using System.Text.Json;

using Magpie.Models.Configuration;
using Magpie.Models.Content;
using Magpie.Models.Downloads;
using Magpie.Models.Filters;
using Magpie.Models.History;
using Magpie.Models.Indexers;
using Magpie.Models.Jobs;
using Magpie.Models.MediaServers;
using Magpie.Models.QualityTiers;
using Magpie.Models.RootFolders;
using Magpie.Models.Triggers;
using Magpie.Models.Users;
using Magpie.Models.Wishlists;

using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;
using AuthProviderEntity = Magpie.Data.Database.Entities.AuthProvider;
using ContentFilterEntity = Magpie.Data.Database.Entities.ContentFilter;
using ContentHierarchyLevelEntity = Magpie.Data.Database.Entities.ContentHierarchyLevel;
using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;
using DownloadEntity = Magpie.Data.Database.Entities.Download;
using EventTriggerEntity = Magpie.Data.Database.Entities.EventTrigger;
using Extension = Magpie.Models.Extensions.Extension;
using ExtensionEntity = Magpie.Data.Database.Entities.Extension;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using IndexerDefinitionEntity = Magpie.Data.Database.Entities.IndexerDefinition;
using IndexerEntity = Magpie.Data.Database.Entities.Indexer;
using IndexerFeedEntity = Magpie.Data.Database.Entities.IndexerFeed;
using JobExecutionEntity = Magpie.Data.Database.Entities.JobExecution;
using LanguagePreferenceEntity = Magpie.Data.Database.Entities.LanguagePreference;
using MediaServerEntity = Magpie.Data.Database.Entities.MediaServer;
using MetadataFilterEntity = Magpie.Data.Database.Entities.MetadataFilter;
using QualityCriterionEntity = Magpie.Data.Database.Entities.QualityCriterion;
using QualityTierEntity = Magpie.Data.Database.Entities.QualityTier;
using QualityTierListEntity = Magpie.Data.Database.Entities.QualityTierList;
using ReleaseEntity = Magpie.Data.Database.Entities.Release;
using RootFolderEntity = Magpie.Data.Database.Entities.RootFolder;
using ScheduleTriggerEntity = Magpie.Data.Database.Entities.ScheduleTrigger;
using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;
using SubjectEntity = Magpie.Data.Database.Entities.Subject;
using SubjectFilterEntity = Magpie.Data.Database.Entities.SubjectFilter;
using UserEntity = Magpie.Data.Database.Entities.User;
using WishlistEntity = Magpie.Data.Database.Entities.Wishlist;

namespace Magpie.Core.Data;

internal static class EntityMappingExtensions
{
    public static UserEntity CreateEntity(this User model, Guid? createdByUserId)
    {
        return new()
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
        return new()
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
        return new()
        {
            Key = model.Key,
            Value = model.Value,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }
    public static SettingsGroupEntity CreateEntity(this SettingsGroup model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            ExtensionId = model.ExtensionId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static HistoryEntryEntity CreateEntity(this HistoryEntry model)
    {
        return new()
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
        return new()
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
        return new()
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
        return new()
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
        return new()
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
        return new()
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
        return new()
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

    public static IndexerDefinitionEntity CreateEntity(this IndexerDefinition model, Guid? createdByUserId)
    {
        return new()
        {
            FeedId = model.FeedId,
            Provider = model.Provider,
            Name = model.Name,
            Access = model.Access,
            Description = model.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static QualityTierListEntity CreateEntity(this QualityTierList model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            UpgradeAllowed = model.UpgradeAllowed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static QualityTierEntity CreateEntity(this QualityTier model, Guid qualityTierListId)
    {
        return new()
        {
            QualityTierListId = qualityTierListId,
            Ordinal = model.Ordinal,
            Name = model.Name,
            IsCutoff = model.IsCutoff
        };
    }

    public static QualityCriterionEntity CreateEntity(this QualityCriterion model, Guid qualityTierId)
    {
        return new()
        {
            QualityTierId = qualityTierId,
            Field = model.Field,
            Operator = model.Operator,
            Value = model.Value
        };
    }

    public static ContentFilterEntity CreateEntity(this ContentFilter model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            Criteria = JsonSerializer.Serialize(model.Criteria),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static MetadataFilterEntity CreateEntity(this MetadataFilter model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            Criteria = JsonSerializer.Serialize(model.Criteria),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static WishlistEntity CreateEntity(this Wishlist model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            UserId = model.UserId,
            OwnerLock = model.OwnerLock,
            ContentFilterId = model.ContentFilterId,
            QualityTierListId = model.QualityTierListId,
            Monitored = model.Monitored,
            SyncProvider = model.SyncProvider,
            SyncListId = model.SyncListId,
            SyncInterval = model.SyncInterval,
            LastSyncedAt = model.LastSyncedAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static ContentHierarchyLevelEntity CreateEntity(
        this ContentHierarchyLevel model,
        Guid? createdByUserId)
    {
        return new()
        {
            ContentHierarchyLevelDefinitionId = model.ContentHierarchyLevelDefinitionId,
            RootFolderId = model.RootFolderId,
            Name = model.Name,
            Slug = model.Slug,
            RelativePath = model.RelativePath,
            ParentId = model.ParentId,
            DateLastRefreshed = model.DateLastRefreshed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static LanguagePreferenceEntity CreateEntity(
        this LanguagePreference model,
        Guid contentHierarchyId)
    {
        return new()
        {
            ContentHierarchyId = contentHierarchyId,
            Ordinal = model.Ordinal,
            Language = model.Language
        };
    }

    public static SubjectEntity CreateEntity(this Subject model, Guid? createdByUserId)
    {
        return new()
        {
            Kind = model.Kind,
            DateLastRefreshed = model.DateLastRefreshed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static SubjectFilterEntity CreateEntity(this SubjectFilter model, Guid? createdByUserId)
    {
        return new()
        {
            Name = model.Name,
            Criteria = JsonSerializer.Serialize(model.Criteria),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static ReleaseEntity CreateEntity(this Release model, Guid? createdByUserId)
    {
        return new()
        {
            IndexerId = model.IndexerId,
            ExternalId = model.ExternalId,
            Provider = model.Provider,
            Title = model.Title,
            PublishDate = model.PublishDate,
            Size = model.Size,
            InfoHash = model.InfoHash,
            DownloadUrl = model.DownloadUrl,
            InfoUrl = model.InfoUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static DownloadEntity CreateEntity(this Download model, Guid? createdByUserId)
    {
        return new()
        {
            ContentItemId = model.ContentItemId,
            ReleaseId = model.ReleaseId,
            DownloadClientId = model.DownloadClientId,
            ExternalId = model.ExternalId,
            Title = model.Title,
            State = model.State,
            OutputPath = model.OutputPath,
            FailureReason = model.FailureReason,
            DateCompleted = model.DateCompleted,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static ScheduleTriggerEntity CreateEntity(this ScheduleTrigger model, Guid? createdByUserId)
    {
        return new()
        {
            JobTemplateId = model.JobTemplateId,
            Name = model.Name,
            Schedule = model.Schedule,
            Enabled = model.Enabled,
            Parameters = model.Parameters is not null ? JsonSerializer.Serialize(model.Parameters) : null,
            LastRunAt = model.LastRunAt,
            NextRunAt = model.NextRunAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }


    public static EventTriggerEntity CreateEntity(this EventTrigger model, Guid? createdByUserId)
    {
        return new()
        {
            JobTemplateId = model.JobTemplateId,
            Name = model.Name,
            EventName = model.EventName,
            Filter = model.Filter is not null ? JsonSerializer.Serialize(model.Filter) : null,
            Enabled = model.Enabled,
            Parameters = model.Parameters is not null ? JsonSerializer.Serialize(model.Parameters) : null,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static JobExecutionEntity CreateEntity(this JobExecution model, Guid? createdByUserId)
    {
        return new()
        {
            JobTemplateId = model.JobTemplateId,
            ScheduleTriggerId = model.ScheduleTriggerId,
            EventTriggerId = model.EventTriggerId,
            EventId = model.EventId,
            Status = model.Status,
            Progress = model.Progress,
            Message = model.Message,
            StartedAt = model.StartedAt,
            CompletedAt = model.CompletedAt,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }

    public static ExtensionEntity CreateEntity(this Extension model, Guid? createdByUserId)
    {
        return new()
        {
            Version = model.Version,
            Name = model.Name,
            TargetABI = model.TargetABI,
            FilePath = model.FilePath,
            AssemblyName = model.AssemblyName,
            Enabled = model.Enabled,
            DisabledReason = model.DisabledReason,
            LoadOrder = model.LoadOrder,
            Managed = model.Managed,
            NuGetId = model.NuGetId,
            PackageRepository = model.PackageRepository,
            AutoUpgrade = model.AutoUpgrade,
            LastChecked = model.LastChecked,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };
    }
}
