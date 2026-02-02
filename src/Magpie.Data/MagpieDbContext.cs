using System;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data;

public class MagpieDbContext(DbContextOptions<MagpieDbContext> options) : DbContext(options)
{
    public DbSet<AlternateText> AlternateTexts => Set<AlternateText>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<ContentFilter> ContentFilters => Set<ContentFilter>();
    public DbSet<ContentHierarchyItem> ContentHierarchyItems => Set<ContentHierarchyItem>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentRating> ContentRatings => Set<ContentRating>();
    public DbSet<CustomMetadataValue> CustomMetadataValues => Set<CustomMetadataValue>();
    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    public DbSet<EventTrigger> EventTriggers => Set<EventTrigger>();
    public DbSet<Extension> Extensions => Set<Extension>();
    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<IndexerConnection> IndexerConnections => Set<IndexerConnection>();
    public DbSet<IndexerFeed> IndexerFeeds => Set<IndexerFeed>();
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaServer> MediaServers => Set<MediaServer>();
    public DbSet<MetadataContainer> MetadataContainers => Set<MetadataContainer>();
    public DbSet<MetadataContribution> MetadataContributions => Set<MetadataContribution>();
    public DbSet<Preview> Previews => Set<Preview>();
    public DbSet<QualityTier> QualityTiers => Set<QualityTier>();
    public DbSet<QualityTierList> QualityTierLists => Set<QualityTierList>();
    public DbSet<QualityTierListRank> QualityTierListRanks => Set<QualityTierListRank>();
    public DbSet<Reference> References => Set<Reference>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Relationship> Relationships => Set<Relationship>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<ScheduleTrigger> ScheduleTriggers => Set<ScheduleTrigger>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<SettingsGroup> SettingsGroups => Set<SettingsGroup>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SubjectFilter> SubjectFilters => Set<SubjectFilter>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MagpieDbContext).Assembly);
    }
}
