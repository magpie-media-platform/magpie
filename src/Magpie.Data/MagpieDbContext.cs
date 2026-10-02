using System;

using Magpie.Data.Configuration;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data;

/// <summary>
/// The Entity Framework Core database context for Magpie.
/// </summary>
/// <param name="options">The options that configure the context.</param>
public sealed class MagpieDbContext(DbContextOptions<MagpieDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The alternate texts attached to metadata entries.
    /// </summary>
    public DbSet<AlternateText> AlternateTexts => Set<AlternateText>();

    /// <summary>
    /// The API keys issued to users.
    /// </summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>
    /// The audit records of system changes.
    /// </summary>
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    /// <summary>
    /// The awards and nominations attached to metadata entries.
    /// </summary>
    public DbSet<Award> Awards => Set<Award>();

    /// <summary>
    /// The blocked file types.
    /// </summary>
    public DbSet<BlockedFileType> BlockedFileTypes => Set<BlockedFileType>();

    /// <summary>
    /// The saved filters for browsing content items.
    /// </summary>
    public DbSet<ContentFilter> ContentFilters => Set<ContentFilter>();

    /// <summary>
    /// The nodes of the content hierarchy.
    /// </summary>
    public DbSet<ContentHierarchyItem> ContentHierarchyItems => Set<ContentHierarchyItem>();

    /// <summary>
    /// The media content items that Magpie tracks for search and download.
    /// </summary>
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();

    /// <summary>
    /// The content ratings attached to metadata entries.
    /// </summary>
    public DbSet<ContentRating> ContentRatings => Set<ContentRating>();

    /// <summary>
    /// The custom metadata fields attached to metadata entries.
    /// </summary>
    public DbSet<CustomMetadataValue> CustomMetadataValues => Set<CustomMetadataValue>();

    /// <summary>
    /// The findings reported by diagnostic checks.
    /// </summary>
    public DbSet<DiagnosticFinding> DiagnosticFindings => Set<DiagnosticFinding>();

    /// <summary>
    /// The downloads in progress or completed by download clients.
    /// </summary>
    public DbSet<Download> Downloads => Set<Download>();

    /// <summary>
    /// The configured download clients.
    /// </summary>
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();

    /// <summary>
    /// The content hierarchy nodes covered by each download.
    /// </summary>
    public DbSet<DownloadScope> DownloadScopes => Set<DownloadScope>();

    /// <summary>
    /// The triggers that start jobs in response to events.
    /// </summary>
    public DbSet<EventTrigger> EventTriggers => Set<EventTrigger>();

    /// <summary>
    /// The single row that tracks the cluster-wide extension activation generation.
    /// </summary>
    public DbSet<ExtensionActivationState> ExtensionActivationStates => Set<ExtensionActivationState>();

    /// <summary>
    /// The per-node heartbeats for extension activation.
    /// </summary>
    public DbSet<ExtensionNodeStatus> ExtensionNodeStatuses => Set<ExtensionNodeStatus>();

    /// <summary>
    /// The configured NuGet sources for extension discovery and installation.
    /// </summary>
    public DbSet<ExtensionPackageSource> ExtensionPackageSources => Set<ExtensionPackageSource>();

    /// <summary>
    /// The extensions installed in Magpie.
    /// </summary>
    public DbSet<Extension> Extensions => Set<Extension>();

    /// <summary>
    /// The external ids that metadata providers report for content and releases.
    /// </summary>
    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();

    /// <summary>
    /// The genre classifications attached to metadata entries.
    /// </summary>
    public DbSet<Genre> Genres => Set<Genre>();

    /// <summary>
    /// The images attached to metadata entries.
    /// </summary>
    public DbSet<Image> Images => Set<Image>();

    /// <summary>
    /// The searchable indexes of releases.
    /// </summary>
    public DbSet<Indexer> Indexers => Set<Indexer>();

    /// <summary>
    /// The configured connections to indexers.
    /// </summary>
    public DbSet<IndexerConnection> IndexerConnections => Set<IndexerConnection>();

    /// <summary>
    /// The feeds that list available indexers.
    /// </summary>
    public DbSet<IndexerFeed> IndexerFeeds => Set<IndexerFeed>();

    /// <summary>
    /// The running and completed job executions.
    /// </summary>
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();

    /// <summary>
    /// The files on disk that belong to content items.
    /// </summary>
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    /// <summary>
    /// The content items, other than the owners, that media files also cover.
    /// </summary>
    public DbSet<MediaFileCoverage> MediaFileCoverages => Set<MediaFileCoverage>();

    /// <summary>
    /// The configured connections to media servers.
    /// </summary>
    public DbSet<MediaServer> MediaServers => Set<MediaServer>();

    /// <summary>
    /// The metadata containers that hold the layered contributions for each item.
    /// </summary>
    public DbSet<MetadataContainer> MetadataContainers => Set<MetadataContainer>();

    /// <summary>
    /// The per-provider metadata contributions to metadata containers.
    /// </summary>
    public DbSet<MetadataContribution> MetadataContributions => Set<MetadataContribution>();

    /// <summary>
    /// The mappings from file extensions to MIME types.
    /// </summary>
    public DbSet<MimeMapping> MimeMappings => Set<MimeMapping>();

    /// <summary>
    /// The per-user routing rules from events to notification providers.
    /// </summary>
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();

    /// <summary>
    /// The library import candidates that library scans found.
    /// </summary>
    public DbSet<LibraryImportCandidate> LibraryImportCandidates => Set<LibraryImportCandidate>();

    /// <summary>
    /// The trailers and preview clips attached to metadata entries.
    /// </summary>
    public DbSet<Preview> Previews => Set<Preview>();

    /// <summary>
    /// The named quality levels that releases can match.
    /// </summary>
    public DbSet<QualityTier> QualityTiers => Set<QualityTier>();

    /// <summary>
    /// The ranked lists of quality tiers used for release selection.
    /// </summary>
    public DbSet<QualityTierList> QualityTierLists => Set<QualityTierList>();

    /// <summary>
    /// The ranks of quality tiers within quality tier lists.
    /// </summary>
    public DbSet<QualityTierListRank> QualityTierListRanks => Set<QualityTierListRank>();

    /// <summary>
    /// The links to external pages for content.
    /// </summary>
    public DbSet<Reference> References => Set<Reference>();

    /// <summary>
    /// The refresh-token sessions of users.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// The links from metadata contributions to related metadata containers.
    /// </summary>
    public DbSet<Relationship> Relationships => Set<Relationship>();

    /// <summary>
    /// The downloadable release instances of content items.
    /// </summary>
    public DbSet<Release> Releases => Set<Release>();

    /// <summary>
    /// The provider-agnostic identity keys for releases and downloads.
    /// </summary>
    public DbSet<ReleaseIdentityKey> ReleaseIdentityKeys => Set<ReleaseIdentityKey>();

    /// <summary>
    /// The indexer observations that supply logical releases.
    /// </summary>
    public DbSet<ReleaseSource> ReleaseSources => Set<ReleaseSource>();

    /// <summary>
    /// The content hierarchy nodes that releases cover.
    /// </summary>
    public DbSet<ReleaseScope> ReleaseScopes => Set<ReleaseScope>();

    /// <summary>
    /// The mappings from remote host paths to local paths.
    /// </summary>
    public DbSet<RemotePathMapping> RemotePathMappings => Set<RemotePathMapping>();

    /// <summary>
    /// The top-level folders that hold imported content.
    /// </summary>
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();

    /// <summary>
    /// The schedule-based triggers for jobs.
    /// </summary>
    public DbSet<ScheduleTrigger> ScheduleTriggers => Set<ScheduleTrigger>();

    /// <summary>
    /// The named values within settings groups.
    /// </summary>
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>
    /// The groups of related settings.
    /// </summary>
    public DbSet<SettingsGroup> SettingsGroups => Set<SettingsGroup>();

    /// <summary>
    /// The entities that content can link to.
    /// </summary>
    public DbSet<Subject> Subjects => Set<Subject>();

    /// <summary>
    /// The saved filters for subjects.
    /// </summary>
    public DbSet<SubjectFilter> SubjectFilters => Set<SubjectFilter>();

    /// <summary>
    /// The labels that group records of different kinds.
    /// </summary>
    public DbSet<Tag> Tags => Set<Tag>();

    /// <summary>
    /// The user accounts.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// The tracked requests for content that matches a content filter.
    /// </summary>
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();

    /// <summary>
    /// The candidates that wishlist refreshes have already reported.
    /// </summary>
    public DbSet<WishlistDiscovered> WishlistDiscovered => Set<WishlistDiscovered>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MagpieDbContext).Assembly);
        modelBuilder.ConfigureJobEnqueueCoalescingLock();
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
