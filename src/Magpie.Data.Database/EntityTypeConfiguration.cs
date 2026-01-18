using Magpie.Data.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Database;

internal sealed class EntityTypeConfiguration :
    IEntityTypeConfiguration<AlternateText>,
    IEntityTypeConfiguration<ApiKey>,
    IEntityTypeConfiguration<AuthProvider>,
    IEntityTypeConfiguration<AuthProviderSetting>,
    IEntityTypeConfiguration<Award>,
    IEntityTypeConfiguration<ContentFilter>,
    IEntityTypeConfiguration<ContentFilterTag>,
    IEntityTypeConfiguration<ContentHierarchyLevel>,
    IEntityTypeConfiguration<ContentItem>,
    IEntityTypeConfiguration<ContentItemTag>,
    IEntityTypeConfiguration<ContentRating>,
    IEntityTypeConfiguration<Download>,
    IEntityTypeConfiguration<DownloadClient>,
    IEntityTypeConfiguration<DownloadClientSetting>,
    IEntityTypeConfiguration<DownloadClientTag>,
    IEntityTypeConfiguration<EventTrigger>,
    IEntityTypeConfiguration<Extension>,
    IEntityTypeConfiguration<ExternalId>,
    IEntityTypeConfiguration<Genre>,
    IEntityTypeConfiguration<HistoryEntry>,
    IEntityTypeConfiguration<SettingsGroup>,
    IEntityTypeConfiguration<Setting>,
    IEntityTypeConfiguration<Image>,
    IEntityTypeConfiguration<Indexer>,
    IEntityTypeConfiguration<IndexerDefinition>,
    IEntityTypeConfiguration<IndexerDefinitionCategory>,
    IEntityTypeConfiguration<IndexerDefinitionLanguage>,
    IEntityTypeConfiguration<IndexerDefinitionTag>,
    IEntityTypeConfiguration<IndexerFeed>,
    IEntityTypeConfiguration<IndexerFeedSetting>,
    IEntityTypeConfiguration<IndexerSetting>,
    IEntityTypeConfiguration<IndexerTag>,
    IEntityTypeConfiguration<JobExecution>,
    IEntityTypeConfiguration<JobExecutionParameter>,
    IEntityTypeConfiguration<LanguagePreference>,
    IEntityTypeConfiguration<MediaFile>,
    IEntityTypeConfiguration<MediaServer>,
    IEntityTypeConfiguration<MediaServerSetting>,
    IEntityTypeConfiguration<MetadataContainer>,
    IEntityTypeConfiguration<MetadataContribution>,
    IEntityTypeConfiguration<CustomMetadataValue>,
    IEntityTypeConfiguration<MetadataFilter>,
    IEntityTypeConfiguration<MetadataFilterTag>,
    IEntityTypeConfiguration<Preview>,
    IEntityTypeConfiguration<QualityCriterion>,
    IEntityTypeConfiguration<QualityTier>,
    IEntityTypeConfiguration<QualityTierList>,
    IEntityTypeConfiguration<QualityTierListTag>,
    IEntityTypeConfiguration<Reference>,
    IEntityTypeConfiguration<Relationship>,
    IEntityTypeConfiguration<Release>,
    IEntityTypeConfiguration<ReleaseCategory>,
    IEntityTypeConfiguration<ReleaseLanguage>,
    IEntityTypeConfiguration<ReleaseProperty>,
    IEntityTypeConfiguration<ReleaseTag>,
    IEntityTypeConfiguration<RootFolder>,
    IEntityTypeConfiguration<ScheduleTrigger>,
    IEntityTypeConfiguration<ServerConfig>,
    IEntityTypeConfiguration<Subject>,
    IEntityTypeConfiguration<SubjectTag>,
    IEntityTypeConfiguration<Tag>,
    IEntityTypeConfiguration<User>,
    IEntityTypeConfiguration<UserProviderData>,
    IEntityTypeConfiguration<Wishlist>,
    IEntityTypeConfiguration<WishlistTag>
{
    public void Configure(EntityTypeBuilder<AlternateText> builder)
    {
        builder.ToTable("metadata_alternateText");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.AlternateTexts)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("auth_apiKey");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.User)
            .WithMany(e => e.ApiKeys)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.KeyPrefix);
        builder.HasIndex(e => e.Enabled);
    }

    public void Configure(EntityTypeBuilder<AuthProvider> builder)
    {
        builder.ToTable("auth_provider");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.IsDefault);
    }

    public void Configure(EntityTypeBuilder<AuthProviderSetting> builder)
    {
        builder.ToTable("auth_providerSetting");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.AuthProvider)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.AuthProviderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.AuthProviderId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Award> builder)
    {
        builder.ToTable("metadata_award");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.Awards)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<ContentFilter> builder)
    {
        builder.ToTable("filter_content");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<ContentFilterTag> builder)
    {
        builder.ToTable("filter_contentTag");
        builder.HasKey(e => new { e.ContentFilterId, e.TagName });
        builder.HasOne(e => e.ContentFilter)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.ContentFilterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ContentHierarchyLevel> builder)
    {
        builder.ToTable("content_hierarchyLevel");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.RootFolder)
            .WithMany(e => e.ContentHierarchyLevels)
            .HasForeignKey(e => e.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentHierarchyLevelDefinitionId);
        builder.HasIndex(e => e.ParentId);
        builder.HasIndex(e => e.RootFolderId);
        builder.HasIndex(e => e.Slug);
    }

    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("content_item");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentHierarchyLevel)
            .WithOne(e => e.ContentItem)
            .HasForeignKey<ContentItem>(e => e.ContentHierarchyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.RequestedByUser)
            .WithMany()
            .HasForeignKey(e => e.RequestedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.RequestedViaWishlist)
            .WithMany()
            .HasForeignKey(e => e.RequestedViaWishlistId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.ImportedByUser)
            .WithMany()
            .HasForeignKey(e => e.ImportedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.ContentHierarchyId).IsUnique();
        builder.HasIndex(e => e.Monitoring);
        builder.HasIndex(e => e.Fulfillment);
        builder.HasIndex(e => e.QualityTierListId);
    }

    public void Configure(EntityTypeBuilder<ContentItemTag> builder)
    {
        builder.ToTable("content_itemTag");
        builder.HasKey(e => new { e.ContentItemId, e.TagName });
        builder.HasOne(e => e.ContentItem)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.ContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ContentRating> builder)
    {
        builder.ToTable("metadata_contentRating");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.ContentRatings)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.ToTable("download_download");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentItem)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ContentItemId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.DownloadClient)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.DownloadClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentItemId);
        builder.HasIndex(e => e.ReleaseId);
        builder.HasIndex(e => e.DownloadClientId);
        builder.HasIndex(e => e.State);
    }

    public void Configure(EntityTypeBuilder<DownloadClient> builder)
    {
        builder.ToTable("download_client");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<DownloadClientSetting> builder)
    {
        builder.ToTable("download_clientSetting");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.DownloadClient)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.DownloadClientId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.DownloadClientId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<DownloadClientTag> builder)
    {
        builder.ToTable("download_clientTag");
        builder.HasKey(e => new { e.DownloadClientId, e.TagName });
        builder.HasOne(e => e.DownloadClient)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.DownloadClientId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<EventTrigger> builder)
    {
        builder.ToTable("job_eventTrigger");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.JobTemplateId);
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.EventName);
    }

    public void Configure(EntityTypeBuilder<Extension> builder)
    {
        builder.ToTable("extension_extension");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.AssemblyName).IsUnique();
    }

    public void Configure(EntityTypeBuilder<SettingsGroup> builder)
    {
        builder.ToTable("configuration_settingsGroup");
        builder.HasKey(sg => sg.Id);
        builder.HasIndex(sg => sg.Name).IsUnique();
        builder.HasOne(sg => sg.Extension)
            .WithMany()
            .HasForeignKey(sg => sg.ExtensionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("configuration_setting");
        builder.HasKey(s => s.Id);
        builder.HasOne(s => s.SettingsGroup)
            .WithMany()
            .HasForeignKey(s => s.SettingsGroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.SettingsGroupId, s.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ExternalId> builder)
    {
        builder.ToTable("metadata_externalId");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.ExternalIds)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("metadata_genre");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.Genres)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("system_history");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Success);
    }

    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("metadata_image");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.Images)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Indexer> builder)
    {
        builder.ToTable("indexer_indexer");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Definition)
            .WithMany(e => e.Indexers)
            .HasForeignKey(e => e.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.DefinitionId);
    }

    public void Configure(EntityTypeBuilder<IndexerDefinition> builder)
    {
        builder.ToTable("indexer_definition");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Feed)
            .WithMany(e => e.Definitions)
            .HasForeignKey(e => e.FeedId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.FeedId);
        builder.HasIndex(e => e.Provider);
    }

    public void Configure(EntityTypeBuilder<IndexerDefinitionCategory> builder)
    {
        builder.ToTable("indexer_definitionCategory");
        builder.HasKey(e => new { e.IndexerDefinitionId, e.Category });
        builder.HasOne(e => e.IndexerDefinition)
            .WithMany(e => e.Categories)
            .HasForeignKey(e => e.IndexerDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<IndexerDefinitionLanguage> builder)
    {
        builder.ToTable("indexer_definitionLanguage");
        builder.HasKey(e => new { e.IndexerDefinitionId, e.Language });
        builder.HasOne(e => e.IndexerDefinition)
            .WithMany(e => e.Languages)
            .HasForeignKey(e => e.IndexerDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<IndexerDefinitionTag> builder)
    {
        builder.ToTable("indexer_definitionTag");
        builder.HasKey(e => new { e.IndexerDefinitionId, e.TagName });
        builder.HasOne(e => e.IndexerDefinition)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.IndexerDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<IndexerFeed> builder)
    {
        builder.ToTable("indexer_feed");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<IndexerFeedSetting> builder)
    {
        builder.ToTable("indexer_feedSetting");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.IndexerFeed)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.IndexerFeedId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.IndexerFeedId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<IndexerSetting> builder)
    {
        builder.ToTable("indexer_setting");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Indexer)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.IndexerId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<IndexerTag> builder)
    {
        builder.ToTable("indexer_tag");
        builder.HasKey(e => new { e.IndexerId, e.TagName });
        builder.HasOne(e => e.Indexer)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.ToTable("job_execution");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ScheduleTrigger)
            .WithMany()
            .HasForeignKey(e => e.ScheduleTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.EventTrigger)
            .WithMany()
            .HasForeignKey(e => e.EventTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.JobTemplateId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.StartedAt);
    }

    public void Configure(EntityTypeBuilder<JobExecutionParameter> builder)
    {
        builder.ToTable("job_executionParameter");
        builder.HasKey(e => new { e.JobExecutionId, e.Key });
        builder.HasOne(e => e.JobExecution)
            .WithMany(e => e.Parameters)
            .HasForeignKey(e => e.JobExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<LanguagePreference> builder)
    {
        builder.ToTable("content_languagePreference");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentHierarchyLevel)
            .WithMany(e => e.LanguagePreferences)
            .HasForeignKey(e => e.ContentHierarchyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.ContentHierarchyId, e.Ordinal });
    }

    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("content_mediaFile");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentItem)
            .WithMany(e => e.MediaFiles)
            .HasForeignKey(e => e.ContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(e => e.MediaInfo);
        builder.HasIndex(e => e.ContentItemId);
        builder.HasIndex(e => e.RelativePath);
    }

    public void Configure(EntityTypeBuilder<MediaServer> builder)
    {
        builder.ToTable("media_server");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<MediaServerSetting> builder)
    {
        builder.ToTable("media_serverSetting");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MediaServer)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.MediaServerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.MediaServerId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<MetadataContainer> builder)
    {
        builder.ToTable("metadata_container");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentHierarchyLevel)
            .WithOne(e => e.MetadataContainer)
            .HasForeignKey<MetadataContainer>(e => e.ContentHierarchyId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
        builder.HasOne(e => e.Subject)
            .WithOne(e => e.MetadataContainer)
            .HasForeignKey<MetadataContainer>(e => e.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.ContentHierarchyId);
        builder.HasIndex(e => e.SubjectId);
    }

    public void Configure(EntityTypeBuilder<MetadataContribution> builder)
    {
        builder.ToTable("metadata_contribution");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContainer)
            .WithMany(e => e.Contributions)
            .HasForeignKey(e => e.MetadataContainerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContainerId);
        builder.HasIndex(e => e.Provider);
    }

    public void Configure(EntityTypeBuilder<CustomMetadataValue> builder)
    {
        builder.ToTable("metadata_customValue");
        builder.HasKey(e => new { e.MetadataContributionId, e.Key });
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.CustomMetadataValues)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<MetadataFilter> builder)
    {
        builder.ToTable("filter_metadata");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<MetadataFilterTag> builder)
    {
        builder.ToTable("filter_metadataTag");
        builder.HasKey(e => new { e.MetadataFilterId, e.TagName });
        builder.HasOne(e => e.MetadataFilter)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.MetadataFilterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Preview> builder)
    {
        builder.ToTable("metadata_preview");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.Previews)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<QualityCriterion> builder)
    {
        builder.ToTable("quality_criterion");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.QualityTier)
            .WithMany(e => e.Criteria)
            .HasForeignKey(e => e.QualityTierId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.QualityTierId);
    }

    public void Configure(EntityTypeBuilder<QualityTier> builder)
    {
        builder.ToTable("quality_tier");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.QualityTierList)
            .WithMany(e => e.Tiers)
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.QualityTierListId, e.Ordinal });
    }

    public void Configure(EntityTypeBuilder<QualityTierList> builder)
    {
        builder.ToTable("quality_tierList");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<QualityTierListTag> builder)
    {
        builder.ToTable("quality_tierListTag");
        builder.HasKey(e => new { e.QualityTierListId, e.TagName });
        builder.HasOne(e => e.QualityTierList)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Reference> builder)
    {
        builder.ToTable("metadata_reference");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.References)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Relationship> builder)
    {
        builder.ToTable("metadata_relationship");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.MetadataContribution)
            .WithMany(e => e.Relationships)
            .HasForeignKey(e => e.MetadataContributionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Subject)
            .WithMany()
            .HasForeignKey(e => e.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.MetadataContributionId);
        builder.HasIndex(e => e.SubjectId);
        builder.HasIndex(e => e.Relation);
    }

    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("indexer_release");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.Indexer)
            .WithMany(e => e.Releases)
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.IndexerId);
        builder.HasIndex(e => e.Provider);
        builder.HasIndex(e => e.InfoHash);
        builder.HasIndex(e => e.PublishDate);
    }

    public void Configure(EntityTypeBuilder<ReleaseCategory> builder)
    {
        builder.ToTable("indexer_releaseCategory");
        builder.HasKey(e => new { e.ReleaseId, e.Category });
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Categories)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ReleaseLanguage> builder)
    {
        builder.ToTable("indexer_releaseLanguage");
        builder.HasKey(e => new { e.ReleaseId, e.Language });
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Languages)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ReleaseProperty> builder)
    {
        builder.ToTable("indexer_releaseProperty");
        builder.HasKey(e => new { e.ReleaseId, e.Key });
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Properties)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<ReleaseTag> builder)
    {
        builder.ToTable("indexer_releaseTag");
        builder.HasKey(e => new { e.ReleaseId, e.TagName });
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<RootFolder> builder)
    {
        builder.ToTable("system_rootFolder");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.ContentTypeId);
        builder.HasIndex(e => e.Path).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ScheduleTrigger> builder)
    {
        builder.ToTable("job_scheduleTrigger");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.JobTemplateId);
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.NextRunAt);
    }

    public void Configure(EntityTypeBuilder<ServerConfig> builder)
    {
        builder.ToTable("system_config");
        builder.HasKey(e => e.Id);
    }

    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("content_subject");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Kind);
    }

    public void Configure(EntityTypeBuilder<SubjectTag> builder)
    {
        builder.ToTable("content_subjectTag");
        builder.HasKey(e => new { e.SubjectId, e.TagName });
        builder.HasOne(e => e.Subject)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("system_tag");
        builder.HasKey(e => e.Name);
    }

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("auth_user");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.AuthProvider)
            .WithMany(e => e.Users)
            .HasForeignKey(e => e.AuthProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.Username).IsUnique();
        builder.HasIndex(e => e.Email);
        builder.HasIndex(e => e.AuthProviderId);
    }

    public void Configure(EntityTypeBuilder<UserProviderData> builder)
    {
        builder.ToTable("auth_userProviderData");
        builder.HasKey(e => new { e.UserId, e.Key });
        builder.HasOne(e => e.User)
            .WithMany(e => e.ProviderData)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("wishlist_wishlist");
        builder.HasKey(e => e.Id);
        builder.HasOne(e => e.ContentFilter)
            .WithMany(e => e.Wishlists)
            .HasForeignKey(e => e.ContentFilterId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTierList)
            .WithMany(e => e.Wishlists)
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.User)
            .WithMany(e => e.Wishlists)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ContentFilterId);
        builder.HasIndex(e => e.QualityTierListId);
    }

    public void Configure(EntityTypeBuilder<WishlistTag> builder)
    {
        builder.ToTable("wishlist_tag");
        builder.HasKey(e => new { e.WishlistId, e.TagName });
        builder.HasOne(e => e.Wishlist)
            .WithMany(e => e.Tags)
            .HasForeignKey(e => e.WishlistId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
