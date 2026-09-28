using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Content;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Content;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentServiceCrossProviderHydrationTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private TransactionManager _transactions = null!;
    private AuditRecordRepository _audit = null!;
    private MetadataContainerRepository _metadata = null!;
    private ContentRepository _content = null!;
    private ContentHierarchyItemRepository _hierarchy = null!;
    private Mock<IContentTypeRegistry> _registry = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public async Task SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).Options);
        _context.RootFolders.AddRange(
            new RootFolder { Id = Guid.NewGuid(), Name = "TV", Path = "tv", ContentType = "tv" },
            new RootFolder { Id = Guid.NewGuid(), Name = "Books", Path = "books", ContentType = "book" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        _transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var events = Mock.Of<IEventBus>();
        _audit = new AuditRecordRepository(
            _context,
            events,
            NullLogger<AuditRecordRepository>.Instance,
            _transactions,
            Mock.Of<IJobExecutionScopeFactory>(),
            _time);
        _registry = new Mock<IContentTypeRegistry>();
        _registry
            .Setup(registry => registry.GetRootLevelSlug(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string _, string? levelSlug) => levelSlug!);
        _registry
            .Setup(registry => registry.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([]);
        _metadata = new MetadataContainerRepository(
            _context,
            _audit,
            Mock.Of<ISubjectRepository>(),
            _transactions,
            NullLogger<MetadataContainerRepository>.Instance,
            _time);
        _content = new ContentRepository(
            _context,
            _transactions,
            events,
            _time,
            NullLogger<ContentRepository>.Instance,
            _registry.Object,
            Mock.Of<IFileSystem>(),
            _audit);
        _hierarchy = new ContentHierarchyItemRepository(
            _context,
            _audit,
            _transactions,
            _metadata,
            _registry.Object,
            events,
            NullLogger<ContentHierarchyItemRepository>.Instance,
            _time,
            _content);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_ReturnsTheExistingItemUnchanged_WhenAFetchedIdentityMatches()
    {
        // Arrange
        var existing = await SeedAsync("tv", "show", "tvmaze-180", ("tvmaze", "180"), ("imdb", "tt0303461"))
            .ConfigureAwait(false);
        var subject = CreateSubject(Provider("wikidata", "tv", ("wikidata", "Q11622"), ("imdb", "tt0303461")));

        // Act
        var result = await subject.HydrateFromExternalIdAsync(
            "wikidata",
            "wikidata",
            "Q11622",
            "tv",
            "show",
            rootFolderId: null,
            monitoring: false,
            qualityTierListId: null,
            requestedViaWishlistId: null,
            tags: ["added"],
            actingUserId: null).ConfigureAwait(false);

        // Assert
        _context.ChangeTracker.Clear();
        var items = await _context.ContentHierarchyItems.ToListAsync().ConfigureAwait(false);
        var contributions = await _context.MetadataContributions
            .Where(contribution => contribution.MetadataContainerId == existing.MetadataContainerId)
            .CountAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Id, Is.EqualTo(existing.Id));
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Slug, Is.EqualTo("tvmaze-180"));
            Assert.That(items[0].Monitoring, Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(items[0].Path, Is.EqualTo("Firefly (2002)"));
            Assert.That(contributions, Is.EqualTo(1));
        }
    }

    [TestCase("tvmaze-180", "wikidata", "Q11622", "tvmaze-180")]
    [TestCase("TVmaze-180", "tvmaze", "180", "tvmaze-180")]
    public async Task HydrateFromExternalIdAsync_ChangesTheSlugOnlyForTheSameProvider_WhenTheItemIsAddedAgain(
        string storedSlug,
        string providerType,
        string externalId,
        string expectedSlug)
    {
        // Arrange
        var existing = await SeedAsync("tv", "show", storedSlug, ("tvmaze", "180"), ("wikidata", "Q11622"))
            .ConfigureAwait(false);
        var subject = CreateSubject(Provider(providerType, "tv", (providerType, externalId)));

        // Act
        var result = await AddAsync(subject, providerType, externalId, "tv", "show").ConfigureAwait(false);

        // Assert
        _context.ChangeTracker.Clear();
        var items = await _context.ContentHierarchyItems.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Id, Is.EqualTo(existing.Id));
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Slug, Is.EqualTo(expectedSlug));
        }
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_CreatesASeparateItem_WhenTheSharedIdentityIsOnAnotherLevel()
    {
        // Arrange
        var existing = await SeedAsync("book", "ebook", "openlibrary-ol1w", ("openlibrary", "OL1W"), ("goodreads", "1"))
            .ConfigureAwait(false);
        var subject = CreateSubject(Provider("other", "book", ("other", "9"), ("goodreads", "1")));

        // Act
        var result = await AddAsync(subject, "other", "9", "book", "audiobook").ConfigureAwait(false);

        // Assert
        Assert.That(result?.Id, Is.Not.Null.And.Not.EqualTo(existing.Id));
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_CreatesASeparateItem_WhenTheSharedValueHasAnotherSource()
    {
        // Arrange
        var existing = await SeedAsync("tv", "show", "tvmaze-180", ("tvmaze", "180")).ConfigureAwait(false);
        var subject = CreateSubject(Provider("wikidata", "tv", ("wikidata", "Q1"), ("tvdb", "180")));

        // Act
        var result = await AddAsync(subject, "wikidata", "Q1", "tv", "show").ConfigureAwait(false);

        // Assert
        Assert.That(result?.Id, Is.Not.Null.And.Not.EqualTo(existing.Id));
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_CreatesASeparateItem_WhenSourcesAndValuesMatchOnlyAcrossPairs()
    {
        // Arrange
        var existing = await SeedAsync("tv", "show", "tvmaze-180", ("tvmaze", "180"), ("imdb", "tt1"))
            .ConfigureAwait(false);
        var subject = CreateSubject(Provider("wikidata", "tv", ("wikidata", "Q3"), ("tvmaze", "tt1"), ("imdb", "180")));

        // Act
        var result = await AddAsync(subject, "wikidata", "Q3", "tv", "show").ConfigureAwait(false);

        // Assert
        Assert.That(result?.Id, Is.Not.Null.And.Not.EqualTo(existing.Id));
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_CreatesASeparateItem_WhenOnlyTheTitleMatches()
    {
        // Arrange
        var existing = await SeedAsync("tv", "show", "tvmaze-180", ("tvmaze", "180")).ConfigureAwait(false);
        var subject = CreateSubject(Provider("wikidata", "tv", ("wikidata", "Q2")));

        // Act
        var result = await AddAsync(subject, "wikidata", "Q2", "tv", "show").ConfigureAwait(false);

        // Assert
        Assert.That(result?.Id, Is.Not.Null.And.Not.EqualTo(existing.Id));
    }

    [Test]
    public async Task HydrateFromExternalIdAsync_ReturnsTheOriginalItem_WhenTheSameProviderAddsAgain()
    {
        // Arrange
        var subject = CreateSubject(Provider("tvmaze", "tv", ("tvmaze", "180"), ("imdb", "tt0303461")));
        var first = await AddAsync(subject, "tvmaze", "180", "tv", "show").ConfigureAwait(false);

        // Act
        var second = await AddAsync(subject, "tvmaze", "180", "tv", "show").ConfigureAwait(false);

        // Assert
        _context.ChangeTracker.Clear();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second?.Id, Is.EqualTo(first?.Id));
            Assert.That(await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    private static IContentHierarchyMetadataProvider Provider(
        string providerType,
        string contentType,
        params (string Source, string Value)[] identities)
    {
        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.SetupGet(item => item.ProviderType).Returns(providerType);
        provider.SetupGet(item => item.SupportedContentTypes).Returns([contentType]);
        provider
            .Setup(item => item.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var contribution = new MetadataContribution { ProviderType = providerType, Title = "Firefly" };
                foreach (var (source, value) in identities)
                {
                    contribution.ExternalIds.Add(new ExternalId
                    {
                        Source = source,
                        Value = value,
                        Ordinal = contribution.ExternalIds.Count
                    });
                }

                return new ContentHierarchyMetadataResult { Contribution = contribution };
            });
        return provider.Object;
    }

    private static Task<ContentHierarchyItem?> AddAsync(
        ContentService subject,
        string providerType,
        string externalId,
        string contentType,
        string level)
    {
        return subject.HydrateFromExternalIdAsync(
            providerType,
            providerType,
            externalId,
            contentType,
            level,
            rootFolderId: null,
            monitoring: false,
            qualityTierListId: null,
            requestedViaWishlistId: null,
            tags: null,
            actingUserId: null);
    }

    private ContentService CreateSubject(IContentHierarchyMetadataProvider provider)
    {
        return new ContentService(
            _hierarchy,
            _content,
            _audit,
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            _metadata,
            [provider],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            _transactions,
            _registry.Object,
            Mock.Of<IContentFileService>(),
            _time,
            NullLogger<ContentService>.Instance);
    }

    private async Task<ContentHierarchyItem> SeedAsync(
        string contentType,
        string level,
        string slug,
        params (string Source, string Value)[] identities)
    {
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            Name = "Firefly",
            Slug = slug,
            ContentType = contentType,
            Level = level,
            Monitoring = MonitoringMode.Monitored,
            Path = "Firefly (2002)",
            CreatedAt = _time.GetUtcNow().UtcDateTime,
            MetadataContainer = new() { Id = Guid.NewGuid() }
        };
        var contribution = new MetadataContribution { ProviderType = "seed", Title = "Firefly" };
        foreach (var (source, value) in identities)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = source,
                Value = value,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        item.MetadataContainer.Contributions.Add(contribution);
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        item.MetadataContainerId = item.MetadataContainer.Id;
        return item;
    }
}
