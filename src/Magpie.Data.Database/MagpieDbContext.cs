using System;

using Magpie.Data.Database.Entities;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data.Database;

/// <summary>
/// The database context for Magpie.
/// </summary>
/// <param name="options">The database context options.</param>
public class MagpieDbContext(DbContextOptions<MagpieDbContext> options) : DbContext(options)
{
    /// <summary>Entity set for <see cref="AlternateText"/>.</summary>
    public DbSet<AlternateText> AlternateTexts => Set<AlternateText>();
    /// <summary>Entity set for <see cref="ApiKey"/>.</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    /// <summary>Entity set for <see cref="AuthProvider"/>.</summary>
    public DbSet<AuthProvider> AuthProviders => Set<AuthProvider>();
    /// <summary>Entity set for <see cref="AuthProviderSetting"/>.</summary>
    public DbSet<AuthProviderSetting> AuthProviderSettings => Set<AuthProviderSetting>();
    /// <summary>Entity set for <see cref="Award"/>.</summary>
    public DbSet<Award> Awards => Set<Award>();
    /// <summary>Entity set for <see cref="ContentFilter"/>.</summary>
    public DbSet<ContentFilter> ContentFilters => Set<ContentFilter>();
    /// <summary>Entity set for <see cref="ContentFilterTag"/>.</summary>
    public DbSet<ContentFilterTag> ContentFilterTags => Set<ContentFilterTag>();
    /// <summary>Entity set for <see cref="ContentHierarchyLevel"/>.</summary>
    public DbSet<ContentHierarchyLevel> ContentHierarchyLevels => Set<ContentHierarchyLevel>();
    /// <summary>Entity set for <see cref="ContentItem"/>.</summary>
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    /// <summary>Entity set for <see cref="ContentItemTag"/>.</summary>
    public DbSet<ContentItemTag> ContentItemTags => Set<ContentItemTag>();
    /// <summary>Entity set for <see cref="ContentRating"/>.</summary>
    public DbSet<ContentRating> ContentRatings => Set<ContentRating>();
    /// <summary>Entity set for <see cref="Download"/>.</summary>
    public DbSet<Download> Downloads => Set<Download>();
    /// <summary>Entity set for <see cref="DownloadClient"/>.</summary>
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    /// <summary>Entity set for <see cref="DownloadClientSetting"/>.</summary>
    public DbSet<DownloadClientSetting> DownloadClientSettings => Set<DownloadClientSetting>();
    /// <summary>Entity set for <see cref="DownloadClientTag"/>.</summary>
    public DbSet<DownloadClientTag> DownloadClientTags => Set<DownloadClientTag>();
    /// <summary>Entity set for <see cref="EventTrigger"/>.</summary>
    public DbSet<EventTrigger> EventTriggers => Set<EventTrigger>();
    /// <summary>Entity set for <see cref="Extension"/>.</summary>
    public DbSet<Extension> Extensions => Set<Extension>();
    /// <summary>Entity set for <see cref="ExternalId"/>.</summary>
    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();
    /// <summary>Entity set for <see cref="Genre"/>.</summary>
    public DbSet<Genre> Genres => Set<Genre>();
    /// <summary>Entity set for <see cref="HistoryEntry"/>.</summary>
    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();
    /// <summary>Entity set for <see cref="SettingsGroup"/>.</summary>
    public DbSet<SettingsGroup> SettingsGroups => Set<SettingsGroup>();
    /// <summary>Entity set for <see cref="Setting"/>.</summary>
    public DbSet<Setting> Settings => Set<Setting>();
    /// <summary>Entity set for <see cref="Image"/>.</summary>
    public DbSet<Image> Images => Set<Image>();
    /// <summary>Entity set for <see cref="Indexer"/>.</summary>
    public DbSet<Indexer> Indexers => Set<Indexer>();
    /// <summary>Entity set for <see cref="IndexerDefinition"/>.</summary>
    public DbSet<IndexerDefinition> IndexerDefinitions => Set<IndexerDefinition>();
    /// <summary>Entity set for <see cref="IndexerDefinitionCategory"/>.</summary>
    public DbSet<IndexerDefinitionCategory> IndexerDefinitionCategories => Set<IndexerDefinitionCategory>();
    /// <summary>Entity set for <see cref="IndexerDefinitionLanguage"/>.</summary>
    public DbSet<IndexerDefinitionLanguage> IndexerDefinitionLanguages => Set<IndexerDefinitionLanguage>();
    /// <summary>Entity set for <see cref="IndexerDefinitionTag"/>.</summary>
    public DbSet<IndexerDefinitionTag> IndexerDefinitionTags => Set<IndexerDefinitionTag>();
    /// <summary>Entity set for <see cref="IndexerFeed"/>.</summary>
    public DbSet<IndexerFeed> IndexerFeeds => Set<IndexerFeed>();
    /// <summary>Entity set for <see cref="IndexerFeedSetting"/>.</summary>
    public DbSet<IndexerFeedSetting> IndexerFeedSettings => Set<IndexerFeedSetting>();
    /// <summary>Entity set for <see cref="IndexerSetting"/>.</summary>
    public DbSet<IndexerSetting> IndexerSettings => Set<IndexerSetting>();
    /// <summary>Entity set for <see cref="IndexerTag"/>.</summary>
    public DbSet<IndexerTag> IndexerTags => Set<IndexerTag>();
    /// <summary>Entity set for <see cref="JobExecution"/>.</summary>
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();
    /// <summary>Entity set for <see cref="JobExecutionParameter"/>.</summary>
    public DbSet<JobExecutionParameter> JobExecutionParameters => Set<JobExecutionParameter>();
    /// <summary>Entity set for <see cref="LanguagePreference"/>.</summary>
    public DbSet<LanguagePreference> LanguagePreferences => Set<LanguagePreference>();
    /// <summary>Entity set for <see cref="MediaFile"/>.</summary>
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    /// <summary>Entity set for <see cref="MediaServer"/>.</summary>
    public DbSet<MediaServer> MediaServers => Set<MediaServer>();
    /// <summary>Entity set for <see cref="MediaServerSetting"/>.</summary>
    public DbSet<MediaServerSetting> MediaServerSettings => Set<MediaServerSetting>();
    /// <summary>Entity set for <see cref="MetadataContainer"/>.</summary>
    public DbSet<MetadataContainer> MetadataContainers => Set<MetadataContainer>();
    /// <summary>Entity set for <see cref="MetadataContribution"/>.</summary>
    public DbSet<MetadataContribution> MetadataContributions => Set<MetadataContribution>();
    /// <summary>Entity set for <see cref="CustomMetadataValue"/>.</summary>
    public DbSet<CustomMetadataValue> CustomMetadataValues => Set<CustomMetadataValue>();
    /// <summary>Entity set for <see cref="MetadataFilter"/>.</summary>
    public DbSet<MetadataFilter> MetadataFilters => Set<MetadataFilter>();
    /// <summary>Entity set for <see cref="MetadataFilterTag"/>.</summary>
    public DbSet<MetadataFilterTag> MetadataFilterTags => Set<MetadataFilterTag>();
    /// <summary>Entity set for <see cref="Preview"/>.</summary>
    public DbSet<Preview> Previews => Set<Preview>();
    /// <summary>Entity set for <see cref="QualityCriterion"/>.</summary>
    public DbSet<QualityCriterion> QualityCriteria => Set<QualityCriterion>();
    /// <summary>Entity set for <see cref="QualityTier"/>.</summary>
    public DbSet<QualityTier> QualityTiers => Set<QualityTier>();
    /// <summary>Entity set for <see cref="QualityTierList"/>.</summary>
    public DbSet<QualityTierList> QualityTierLists => Set<QualityTierList>();
    /// <summary>Entity set for <see cref="QualityTierListTag"/>.</summary>
    public DbSet<QualityTierListTag> QualityTierListTags => Set<QualityTierListTag>();
    /// <summary>Entity set for <see cref="RefreshToken"/>.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    /// <summary>Entity set for <see cref="Reference"/>.</summary>
    public DbSet<Reference> References => Set<Reference>();
    /// <summary>Entity set for <see cref="Relationship"/>.</summary>
    public DbSet<Relationship> Relationships => Set<Relationship>();
    /// <summary>Entity set for <see cref="Release"/>.</summary>
    public DbSet<Release> Releases => Set<Release>();
    /// <summary>Entity set for <see cref="ReleaseCategory"/>.</summary>
    public DbSet<ReleaseCategory> ReleaseCategories => Set<ReleaseCategory>();
    /// <summary>Entity set for <see cref="ReleaseLanguage"/>.</summary>
    public DbSet<ReleaseLanguage> ReleaseLanguages => Set<ReleaseLanguage>();
    /// <summary>Entity set for <see cref="ReleaseProperty"/>.</summary>
    public DbSet<ReleaseProperty> ReleaseProperties => Set<ReleaseProperty>();
    /// <summary>Entity set for <see cref="ReleaseTag"/>.</summary>
    public DbSet<ReleaseTag> ReleaseTags => Set<ReleaseTag>();
    /// <summary>Entity set for <see cref="RootFolder"/>.</summary>
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    /// <summary>Entity set for <see cref="ScheduleTrigger"/>.</summary>
    public DbSet<ScheduleTrigger> ScheduleTriggers => Set<ScheduleTrigger>();
    /// <summary>Entity set for <see cref="ServerConfig"/>.</summary>
    public DbSet<ServerConfig> ServerConfigs => Set<ServerConfig>();
    /// <summary>Entity set for <see cref="Subject"/>.</summary>
    public DbSet<Subject> Subjects => Set<Subject>();
    /// <summary>Entity set for <see cref="SubjectFilter"/>.</summary>
    public DbSet<SubjectFilter> SubjectFilters => Set<SubjectFilter>();
    /// <summary>Entity set for <see cref="SubjectFilterTag"/>.</summary>
    public DbSet<SubjectFilterTag> SubjectFilterTags => Set<SubjectFilterTag>();
    /// <summary>Entity set for <see cref="SubjectTag"/>.</summary>
    public DbSet<SubjectTag> SubjectTags => Set<SubjectTag>();
    /// <summary>Entity set for <see cref="Tag"/>.</summary>
    public DbSet<Tag> Tags => Set<Tag>();
    /// <summary>Entity set for <see cref="User"/>.</summary>
    public DbSet<User> Users => Set<User>();
    /// <summary>Entity set for <see cref="UserProviderData"/>.</summary>
    public DbSet<UserProviderData> UserProviderData => Set<UserProviderData>();
    /// <summary>Entity set for <see cref="Wishlist"/>.</summary>
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    /// <summary>Entity set for <see cref="WishlistTag"/>.</summary>
    public DbSet<WishlistTag> WishlistTags => Set<WishlistTag>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MagpieDbContext).Assembly);
    }
}
