using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

using Magpie.Models;
using Magpie.Models.Configuration;
using Magpie.Models.Content;
using Magpie.Models.Downloads;
using Magpie.Models.Extensions;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Content;
using Magpie.Models.Filters.Metadata;
using Magpie.Models.Filters.Subject;
using Magpie.Models.History;
using Magpie.Models.Indexers;
using Magpie.Models.Jobs;
using Magpie.Models.MediaServers;
using Magpie.Models.Metadata;
using Magpie.Models.QualityTiers;
using Magpie.Models.RootFolders;
using Magpie.Models.Triggers;
using Magpie.Models.Users;
using Magpie.Models.Wishlists;

using AlternateTextEntity = Magpie.Data.Database.Entities.AlternateText;
using ApiKeyEntity = Magpie.Data.Database.Entities.ApiKey;
using AuthProviderEntity = Magpie.Data.Database.Entities.AuthProvider;
using AwardEntity = Magpie.Data.Database.Entities.Award;
using ContentFilterEntity = Magpie.Data.Database.Entities.ContentFilter;
using ContentHierarchyLevelEntity = Magpie.Data.Database.Entities.ContentHierarchyLevel;
using ContentItemEntity = Magpie.Data.Database.Entities.ContentItem;
using ContentRatingEntity = Magpie.Data.Database.Entities.ContentRating;
using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;
using DownloadEntity = Magpie.Data.Database.Entities.Download;
using EventTriggerEntity = Magpie.Data.Database.Entities.EventTrigger;
using ExtensionEntity = Magpie.Data.Database.Entities.Extension;
using ExternalIdEntity = Magpie.Data.Database.Entities.ExternalId;
using GenreEntity = Magpie.Data.Database.Entities.Genre;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using ImageEntity = Magpie.Data.Database.Entities.Image;
using IndexerDefinitionEntity = Magpie.Data.Database.Entities.IndexerDefinition;
using IndexerEntity = Magpie.Data.Database.Entities.Indexer;
using IndexerFeedEntity = Magpie.Data.Database.Entities.IndexerFeed;
using JobExecutionEntity = Magpie.Data.Database.Entities.JobExecution;
using LanguagePreferenceEntity = Magpie.Data.Database.Entities.LanguagePreference;
using MediaFileEntity = Magpie.Data.Database.Entities.MediaFile;
using MediaInfoEntity = Magpie.Data.Database.Entities.MediaInfo;
using MediaServerEntity = Magpie.Data.Database.Entities.MediaServer;
using MetadataContainerEntity = Magpie.Data.Database.Entities.MetadataContainer;
using MetadataContributionEntity = Magpie.Data.Database.Entities.MetadataContribution;
using MetadataFilterEntity = Magpie.Data.Database.Entities.MetadataFilter;
using PreviewEntity = Magpie.Data.Database.Entities.Preview;
using QualityCriterionEntity = Magpie.Data.Database.Entities.QualityCriterion;
using QualityTierEntity = Magpie.Data.Database.Entities.QualityTier;
using QualityTierListEntity = Magpie.Data.Database.Entities.QualityTierList;
using ReferenceEntity = Magpie.Data.Database.Entities.Reference;
using RelationshipEntity = Magpie.Data.Database.Entities.Relationship;
using ReleaseEntity = Magpie.Data.Database.Entities.Release;
using RootFolderEntity = Magpie.Data.Database.Entities.RootFolder;
using ScheduleTriggerEntity = Magpie.Data.Database.Entities.ScheduleTrigger;
using ServerConfigEntity = Magpie.Data.Database.Entities.ServerConfig;
using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;
using SubjectEntity = Magpie.Data.Database.Entities.Subject;
using SubjectFilterEntity = Magpie.Data.Database.Entities.SubjectFilter;
using TagEntity = Magpie.Data.Database.Entities.Tag;
using UserEntity = Magpie.Data.Database.Entities.User;
using WishlistEntity = Magpie.Data.Database.Entities.Wishlist;

namespace Magpie.Core.Data;

internal static class ModelMappingExtensions
{
    public static User ToModel(this UserEntity entity)
    {
        return new(
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

    public static UserCredentials ToCredentialsModel(this UserEntity entity)
    {
        return new(
            entity.Id,
            entity.PasswordHash,
            entity.PasswordSalt,
            entity.Enabled,
            entity.LockedUntil);
    }

    public static ApiKey ToModel(this ApiKeyEntity entity)
    {
        return new(
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
        return new(entity.SettingsGroupId, entity.Key, entity.Value);
    }

    public static SettingsGroup ToModel(this SettingsGroupEntity entity)
    {
        return new(entity.Id, entity.Name, entity.ExtensionId);
    }

    public static HistoryEntry ToModel(this HistoryEntryEntity entity)
    {
        return new(
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
        return new(entity.Name);
    }

    public static AuthProvider ToModel(this AuthProviderEntity entity)
    {
        return new(
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
        return new(
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
        return new(
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
        return new(
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
        return new(
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
        return new(
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

    public static IndexerDefinition ToModel(this IndexerDefinitionEntity entity)
    {
        return new(
            entity.Id,
            entity.FeedId,
            entity.Provider,
            entity.Name,
            entity.Access,
            entity.Description,
            [.. entity.Languages.Select(l => l.Language)],
            [.. entity.Categories.Select(c => c.Category)],
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static QualityTierList ToModel(this QualityTierListEntity entity)
    {
        return new(
            entity.Id,
            entity.Name,
            [.. entity.Tiers.OrderBy(t => t.Ordinal).Select(t => t.ToModel())],
            entity.UpgradeAllowed,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static QualityTier ToModel(this QualityTierEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Name,
            entity.IsCutoff,
            [.. entity.Criteria.Select(c => c.ToModel())]);
    }


    public static QualityCriterion ToModel(this QualityCriterionEntity entity)
    {
        return new(
            entity.Id,
            entity.Field,
            entity.Operator,
            entity.Value);
    }


    public static ContentFilter ToModel(this ContentFilterEntity entity)
    {
        var criteria = JsonSerializer.Deserialize<ContentItemCriteria>(entity.Criteria)
            ?? new ContentItemCriteria { TargetLevel = new ContentHierarchyLevelCriteria() };

        return new(
            entity.Id,
            entity.Name,
            criteria,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }


    public static MetadataFilter ToModel(this MetadataFilterEntity entity)
    {
        var criteria = JsonSerializer.Deserialize<MetadataContributionCriteria>(entity.Criteria)
            ?? new MetadataContributionCriteria();

        return new(
            entity.Id,
            entity.Name,
            criteria,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }


    public static Wishlist ToModel(this WishlistEntity entity)
    {
        return new(
            entity.Id,
            entity.Name,
            entity.UserId,
            entity.OwnerLock,
            entity.ContentFilterId,
            entity.QualityTierListId,
            entity.Monitored,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.SyncProvider,
            entity.SyncListId,
            entity.SyncInterval,
            entity.LastSyncedAt,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static ContentHierarchyLevel ToModel(this ContentHierarchyLevelEntity entity)
    {
        return new ContentHierarchyLevel(
            entity.Id,
            entity.ContentHierarchyLevelDefinitionId,
            entity.RootFolderId,
            entity.Name,
            entity.Slug,
            entity.RelativePath,
            entity.ParentId,
            entity.ContentItem?.ToModel(),
            entity.DateLastRefreshed,
            entity.MetadataContainer?.ToModel() ?? new MetadataContainer(Guid.Empty, DateTimeOffset.MinValue, []),
            [.. entity.LanguagePreferences.OrderBy(l => l.Ordinal).Select(l => l.ToModel())],
            entity.ContentItem?.Tags.Select(t => t.TagName).ToImmutableList() ?? [],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static ContentItem ToModel(this ContentItemEntity entity)
    {
        return new(
            entity.Id,
            entity.QualityTierListId,
            entity.Monitoring,
            entity.Fulfillment,
            entity.DateLastSearched,
            [.. entity.MediaFiles.Select(f => f.ToModel())],
            [.. entity.Tags.Select(t => t.TagName)],
            entity.RequestedByUserId,
            entity.RequestedViaWishlistId,
            entity.RequestedAt,
            entity.ImportedAt,
            entity.ImportedByUserId,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static MediaFile ToModel(this MediaFileEntity entity)
    {
        return new(
            entity.Id,
            entity.Kind,
            entity.MimeType,
            entity.RelativePath,
            entity.Size,
            entity.MediaInfo?.ToModel(),
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static MediaInfo ToModel(this MediaInfoEntity entity)
    {
        return new(
            entity.Container,
            entity.Duration,
            entity.VideoCodec,
            entity.VideoWidth,
            entity.VideoHeight,
            entity.VideoBitrate,
            entity.AudioCodec,
            entity.AudioChannels,
            entity.AudioBitrate);
    }

    public static LanguagePreference ToModel(this LanguagePreferenceEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Language);
    }


    public static MetadataContainer ToModel(this MetadataContainerEntity entity)
    {
        return new(
            entity.Id,
            entity.DateAdded,
            [.. entity.Contributions.OrderBy(c => c.Ordinal).Select(c => c.ToModel())]);
    }

    public static MetadataContribution ToModel(this MetadataContributionEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Locked,
            entity.Provider,
            entity.DateAdded,
            entity.DateLastRefreshed,
            entity.Attribution,
            entity.Title,
            entity.OriginalTitle,
            entity.SortTitle,
            entity.Language,
            entity.Overview,
            entity.PublishDate,
            entity.Duration,
            entity.ProductionStatus,
            entity.Established,
            entity.Dissolved,
            entity.BirthDate,
            entity.DeathDate,
            entity.CustomMetadataValues.ToImmutableDictionary(c => c.Key, c => c.Value),
            [.. entity.ExternalIds.Select(e => e.ToModel())],
            [.. entity.References.Select(r => r.ToModel())],
            [.. entity.Relationships.Select(r => r.ToModel())],
            [.. entity.Images.Select(i => i.ToModel())],
            [.. entity.Previews.Select(p => p.ToModel())],
            [.. entity.AlternateTexts.Select(a => a.ToModel())],
            [.. entity.ContentRatings.Select(c => c.ToModel())],
            [.. entity.Awards.Select(a => a.ToModel())],
            [.. entity.Genres.Select(g => g.ToModel())]);
    }

    public static ExternalIdMetadata ToModel(this ExternalIdEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Value,
            entity.Attribution);
    }

    public static ReferenceMetadata ToModel(this ReferenceEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Kind,
            entity.Url,
            entity.DisplayName,
            entity.Language);
    }

    public static RelationshipMetadata ToModel(this RelationshipEntity entity)
    {
        return new(
            entity.Id,
            entity.SubjectId,
            entity.Ordinal,
            entity.Relation,
            entity.Qualifier);
    }

    public static ImageMetadata ToModel(this ImageEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.ImageType,
            entity.MimeType,
            entity.Url,
            entity.Attribution,
            entity.Language,
            entity.Width,
            entity.Height);
    }

    public static PreviewMetadata ToModel(this PreviewEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.PreviewType,
            entity.MimeType,
            entity.Url,
            entity.Attribution,
            entity.Title,
            entity.ReleaseDate,
            entity.Language);
    }

    public static AlternateTextMetadata ToModel(this AlternateTextEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.Type,
            entity.Language,
            entity.Text);
    }

    public static ContentRatingMetadata ToModel(this ContentRatingEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.RatingsBody,
            entity.Value);
    }

    public static AwardMetadata ToModel(this AwardEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.AwardBody,
            entity.Name,
            entity.Category,
            entity.Status,
            entity.Year,
            entity.Subject,
            entity.Language,
            entity.Country);
    }

    public static GenreMetadata ToModel(this GenreEntity entity)
    {
        return new(
            entity.Id,
            entity.Ordinal,
            entity.GenreName);
    }

    public static Subject ToModel(this SubjectEntity entity)
    {
        return new(
            entity.Id,
            entity.Kind,
            entity.DateLastRefreshed,
            entity.MetadataContainer?.ToModel() ?? new MetadataContainer(Guid.Empty, DateTimeOffset.MinValue, []),
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static SubjectFilter ToModel(this SubjectFilterEntity entity)
    {
        var criteria = JsonSerializer.Deserialize<SubjectCriteria>(entity.Criteria)
            ?? new SubjectCriteria();

        return new(
            entity.Id,
            entity.Name,
            criteria,
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static Release ToModel(this ReleaseEntity entity)
    {
        return new(
            entity.Id,
            entity.IndexerId,
            entity.ExternalId,
            entity.Provider,
            entity.Title,
            entity.PublishDate,
            entity.Size,
            entity.InfoHash,
            [.. entity.Categories.Select(c => c.Category)],
            entity.DownloadUrl,
            entity.InfoUrl,
            entity.Properties.ToImmutableDictionary(p => p.Key, p => p.Value),
            [.. entity.Languages.Select(l => l.Language)],
            [.. entity.Tags.Select(t => t.TagName)],
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static Download ToModel(this DownloadEntity entity)
    {
        return new(
            entity.Id,
            entity.ContentItemId,
            entity.ReleaseId,
            entity.DownloadClientId,
            entity.ExternalId,
            entity.Title,
            entity.State,
            entity.OutputPath,
            entity.FailureReason,
            entity.DateCompleted,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static ScheduleTrigger ToModel(this ScheduleTriggerEntity entity)
    {
        var parameters = default(IReadOnlyDictionary<string, string?>);

        if (!string.IsNullOrEmpty(entity.Parameters))
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Parameters);
        }

        return new(
            entity.Id,
            entity.JobTemplateId,
            entity.Name,
            entity.Schedule,
            entity.Enabled,
            parameters,
            entity.LastRunAt,
            entity.NextRunAt,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }


    public static EventTrigger ToModel(this EventTriggerEntity entity)
    {
        var filter = default(IReadOnlyDictionary<string, string?>);
        var parameters = default(IReadOnlyDictionary<string, string?>);

        if (!string.IsNullOrEmpty(entity.Filter))
        {
            filter = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Filter);
        }
        if (!string.IsNullOrEmpty(entity.Parameters))
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Parameters);
        }

        return new(
            entity.Id,
            entity.JobTemplateId,
            entity.Name,
            entity.EventName,
            filter,
            entity.Enabled,
            parameters,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static JobExecution ToModel(this JobExecutionEntity entity)
    {
        return new(
            entity.Id,
            entity.JobTemplateId,
            entity.ScheduleTriggerId,
            entity.EventTriggerId,
            entity.EventId,
            entity.Status,
            entity.Progress,
            entity.Message,
            entity.Parameters.ToImmutableDictionary(p => p.Key, p => p.Value),
            entity.StartedAt,
            entity.CompletedAt,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static Extension ToModel(this ExtensionEntity entity)
    {
        return new(
            entity.Id,
            entity.Version,
            entity.Name,
            entity.TargetABI,
            entity.FilePath,
            entity.AssemblyName,
            entity.Enabled,
            entity.DisabledReason,
            entity.LoadOrder,
            entity.Managed,
            entity.NuGetId,
            entity.PackageRepository,
            entity.AutoUpgrade,
            entity.LastChecked,
            entity.CreatedAt,
            entity.CreatedByUserId,
            entity.ModifiedAt,
            entity.ModifiedByUserId);
    }

    public static ServerConfig ToModel(this ServerConfigEntity entity)
    {
        return new(
            entity.DefaultLanguagePreference,
            entity.AutoUpdateEnabled,
            entity.BackupSchedule,
            entity.BackupRetentionPeriod);
    }
}
