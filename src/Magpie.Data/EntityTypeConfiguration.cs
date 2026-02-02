using System;
using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Magpie.Data;

internal sealed class EntityTypeConfiguration :
    IEntityTypeConfiguration<AlternateText>,
    IEntityTypeConfiguration<ApiKey>,
    IEntityTypeConfiguration<AuditRecord>,
    IEntityTypeConfiguration<Award>,
    IEntityTypeConfiguration<ContentFilter>,
    IEntityTypeConfiguration<ContentHierarchyItem>,
    IEntityTypeConfiguration<ContentItem>,
    IEntityTypeConfiguration<ContentRating>,
    IEntityTypeConfiguration<CustomMetadataValue>,
    IEntityTypeConfiguration<Download>,
    IEntityTypeConfiguration<DownloadClient>,
    IEntityTypeConfiguration<EventTrigger>,
    IEntityTypeConfiguration<Extension>,
    IEntityTypeConfiguration<ExternalId>,
    IEntityTypeConfiguration<Genre>,
    IEntityTypeConfiguration<Image>,
    IEntityTypeConfiguration<Indexer>,
    IEntityTypeConfiguration<IndexerConnection>,
    IEntityTypeConfiguration<IndexerFeed>,
    IEntityTypeConfiguration<JobExecution>,
    IEntityTypeConfiguration<MediaFile>,
    IEntityTypeConfiguration<MediaServer>,
    IEntityTypeConfiguration<MetadataContainer>,
    IEntityTypeConfiguration<MetadataContribution>,
    IEntityTypeConfiguration<Preview>,
    IEntityTypeConfiguration<QualityTier>,
    IEntityTypeConfiguration<QualityTierList>,
    IEntityTypeConfiguration<QualityTierListRank>,
    IEntityTypeConfiguration<Reference>,
    IEntityTypeConfiguration<RefreshToken>,
    IEntityTypeConfiguration<Relationship>,
    IEntityTypeConfiguration<Release>,
    IEntityTypeConfiguration<RootFolder>,
    IEntityTypeConfiguration<ScheduleTrigger>,
    IEntityTypeConfiguration<SettingsGroup>,
    IEntityTypeConfiguration<Setting>,
    IEntityTypeConfiguration<Subject>,
    IEntityTypeConfiguration<SubjectFilter>,
    IEntityTypeConfiguration<Tag>,
    IEntityTypeConfiguration<User>,
    IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<AlternateText> builder)
    {
        builder.ToTable("metadata_alternateText");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.AlternateTexts)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("auth_apiKey");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<User>()
            .WithMany(e => e.ApiKeys)
            .HasForeignKey(e => e.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.KeyPrefix);
        builder.HasIndex(e => e.Enabled);
    }

    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("system_audit");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasValueGenerator<SequentialGuidValueGenerator>();
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.EntityType);
        builder.HasIndex(e => e.EntityId);
        builder.HasIndex(e => e.UserId);
    }

    public void Configure(EntityTypeBuilder<Award> builder)
    {
        builder.ToTable("metadata_award");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Awards)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<ContentFilter> builder)
    {
        builder.ToTable("filter_content");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<ContentFilterCriteria>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasMany<Tag>()
            .WithMany(e => e.ContentFilters)
            .UsingEntity(j => j.ToTable("filter_contentTag"));
    }

    public void Configure(EntityTypeBuilder<ContentHierarchyItem> builder)
    {
        builder.ToTable("content_hierarchyItem");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasOne<ContentHierarchyItem>()
            .WithMany()
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RootFolder)
            .WithMany()
            .HasForeignKey(e => e.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTierList)
            .WithMany()
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.MetadataContainer)
            .WithOne()
            .HasForeignKey<ContentHierarchyItem>(e => e.MetadataContainerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ParentId);
        builder.HasIndex(e => e.MetadataContainerId).IsUnique();
        builder.HasIndex(e => new { e.ContentType, e.Level });
        builder.HasIndex(e => new { e.ContentType, e.Level, e.Slug }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("content_item");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<ContentHierarchyItem>()
            .WithOne(e => e.ContentItem)
            .HasForeignKey<ContentItem>(e => e.ContentHierarchyItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentHierarchyItemId).IsUnique();
        builder.HasIndex(e => e.Fulfillment);
    }

    public void Configure(EntityTypeBuilder<ContentRating> builder)
    {
        builder.ToTable("metadata_contentRating");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.ContentRatings)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<CustomMetadataValue> builder)
    {
        builder.ToTable("metadata_customValue");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.CustomMetadata)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.MetadataContributionId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.ToTable("download_download");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.Release)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ReleaseId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DownloadClient>()
            .WithMany()
            .HasForeignKey(e => e.DownloadClientId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ReleaseId);
        builder.HasIndex(e => e.Status);
    }

    public void Configure(EntityTypeBuilder<DownloadClient> builder)
    {
        builder.ToTable("download_client");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasMany<Tag>()
            .WithMany(e => e.DownloadClients)
            .UsingEntity(j => j.ToTable("download_clientTag"));
    }

    public void Configure(EntityTypeBuilder<EventTrigger> builder)
    {
        builder.ToTable("job_eventTrigger");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.EventFilter).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<EventFilter>(v, JsonSerializerOptions.Default) ?? new());
        builder.Property(e => e.Parameters).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());

        builder.HasIndex(e => e.Enabled);
    }

    public void Configure(EntityTypeBuilder<Extension> builder)
    {
        builder.ToTable("extension_extension");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.AssemblyName).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ExternalId> builder)
    {
        builder.ToTable("metadata_externalId");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.ExternalIds)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("metadata_genre");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Genres)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("metadata_image");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Images)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<Indexer> builder)
    {
        builder.ToTable("indexer_indexer");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.ProviderType);
        builder.HasOne<IndexerFeed>()
            .WithMany()
            .HasForeignKey(e => e.FeedId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<IndexerConnection> builder)
    {
        builder.ToTable("indexer_connection");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasOne<Indexer>()
            .WithMany(e => e.Connections)
            .HasForeignKey(e => e.IndexerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<Tag>()
            .WithMany(e => e.IndexerConnections)
            .UsingEntity(j => j.ToTable("indexer_connectionTag"));
        builder.HasIndex(e => e.IndexerId);
    }

    public void Configure(EntityTypeBuilder<IndexerFeed> builder)
    {
        builder.ToTable("indexer_feed");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
    }

    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.ToTable("job_execution");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Parameters).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.Property(e => e.TriggerContext).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasOne<ScheduleTrigger>()
            .WithMany()
            .HasForeignKey(e => e.ScheduleTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<EventTrigger>()
            .WithMany()
            .HasForeignKey(e => e.EventTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.StartedAt);
        builder.HasIndex(e => e.LastHeartbeat);
    }

    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("content_mediaFile");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.MediaInfo).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<MediaInfo>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasOne<ContentItem>()
            .WithMany(e => e.MediaFiles)
            .HasForeignKey(e => e.ContentItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentItemId);
    }

    public void Configure(EntityTypeBuilder<MediaServer> builder)
    {
        builder.ToTable("media_server");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasMany<Tag>()
            .WithMany(e => e.MediaServers)
            .UsingEntity(j => j.ToTable("media_serverTag"));
    }

    public void Configure(EntityTypeBuilder<MetadataContainer> builder)
    {
        builder.ToTable("metadata_container");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasMany<Tag>()
            .WithMany(e => e.MetadataContainers)
            .UsingEntity(j => j.ToTable("metadata_containerTag"));
    }

    public void Configure(EntityTypeBuilder<MetadataContribution> builder)
    {
        builder.ToTable("metadata_contribution");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.MetadataContainer)
            .WithMany(e => e.Contributions)
            .HasForeignKey(e => e.MetadataContainerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContainerId);
        builder.HasIndex(e => e.ProviderType);
    }

    public void Configure(EntityTypeBuilder<Preview> builder)
    {
        builder.ToTable("metadata_preview");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Previews)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<QualityTier> builder)
    {
        builder.ToTable("quality_tier");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<QualityTierCriteria>(v, JsonSerializerOptions.Default) ?? new());
    }

    public void Configure(EntityTypeBuilder<QualityTierList> builder)
    {
        builder.ToTable("quality_tierList");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasMany<Tag>()
            .WithMany(e => e.QualityTierLists)
            .UsingEntity(j => j.ToTable("quality_tierListTag"));
    }

    public void Configure(EntityTypeBuilder<QualityTierListRank> builder)
    {
        builder.ToTable("quality_tierListRank");
        builder.HasKey(e => new { e.QualityTierListId, e.QualityTierId });
        builder.HasOne<QualityTierList>()
            .WithMany(e => e.QualityTierRanks)
            .HasForeignKey(e => e.QualityTierListId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTier)
            .WithMany()
            .HasForeignKey(e => e.QualityTierId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.QualityTierListId, e.Ordinal });
    }

    public void Configure(EntityTypeBuilder<Reference> builder)
    {
        builder.ToTable("metadata_reference");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.References)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
    }

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("auth_refreshToken");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<User>()
            .WithMany(e => e.RefreshTokens)
            .HasForeignKey(e => e.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ExpiresAt);
    }

    public void Configure(EntityTypeBuilder<Relationship> builder)
    {
        builder.ToTable("metadata_relationship");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<MetadataContribution>()
            .WithMany(e => e.Relationships)
            .HasForeignKey(e => e.MetadataContributionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MetadataContainer>()
            .WithMany()
            .HasForeignKey(e => e.RelatedMetadataContainerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.MetadataContributionId);
        builder.HasIndex(e => e.RelatedMetadataContainerId);
        builder.HasIndex(e => e.Kind);
    }

    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("indexer_release");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(e => e.IndexerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(e => e.ContentItemId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany<Tag>()
            .WithMany(e => e.Releases)
            .UsingEntity(j => j.ToTable("indexer_releaseTag"));
        builder.HasIndex(e => e.IndexerId);
        builder.HasIndex(e => e.ContentItemId);
        builder.HasIndex(e => e.PublishDate);
    }

    public void Configure(EntityTypeBuilder<RootFolder> builder)
    {
        builder.ToTable("system_rootFolder");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.ContentTypeId);
        builder.HasIndex(e => e.Path).IsUnique();
    }

    public void Configure(EntityTypeBuilder<ScheduleTrigger> builder)
    {
        builder.ToTable("job_scheduleTrigger");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Parameters).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.NextRunAt);
    }

    public void Configure(EntityTypeBuilder<SettingsGroup> builder)
    {
        builder.ToTable("configuration_settingsGroup");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => new { e.ExtensionId, e.Name }).IsUnique();
        builder.HasOne<Extension>()
            .WithMany()
            .HasForeignKey(e => e.ExtensionId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("configuration_setting");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne<SettingsGroup>()
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.SettingsGroupId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SettingsGroupId, e.Key }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("content_subject");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.MetadataContainer)
            .WithMany()
            .HasForeignKey(e => e.MetadataContainerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.Kind);
        builder.HasIndex(e => e.MetadataContainerId).IsUnique();
    }

    public void Configure(EntityTypeBuilder<SubjectFilter> builder)
    {
        builder.ToTable("filter_subject");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<SubjectFilterCriteria>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasMany<Tag>()
            .WithMany(e => e.SubjectFilters)
            .UsingEntity(j => j.ToTable("filter_subjectTag"));
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
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());
        builder.HasIndex(e => e.Username).IsUnique();
        builder.HasIndex(e => e.Email);

        builder.HasData(new User
        {
            Id = new Guid("11111111-1111-1111-1111-111111111112"),
            Username = "root",
            ProviderType = "system",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UnixEpoch
        });
    }

    public void Configure(EntityTypeBuilder<Wishlist> builder)
    {
        builder.ToTable("wishlist_wishlist");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasOne(e => e.ContentFilter)
            .WithMany()
            .HasForeignKey(e => e.ContentFilterId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.QualityTierList)
            .WithMany()
            .HasForeignKey(e => e.QualityTierListId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<Tag>()
            .WithMany(e => e.Wishlists)
            .UsingEntity(j => j.ToTable("wishlist_tag"));
        builder.HasIndex(e => e.OwnerUserId);
        builder.HasIndex(e => e.ContentFilterId);
        builder.HasIndex(e => e.QualityTierListId);
    }
}
