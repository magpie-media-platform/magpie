using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentRepository _subject = null!;
    private FakeTimeProvider _time = null!;
    private ContentHierarchyItem _hierarchyItem = null!;
    private List<MagpieEvent> _events = null!;
    private bool _publishedInTransaction;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var metadata = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainer = metadata,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(_hierarchyItem);
        _context.SaveChanges();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        var registry = new Mock<IContentTypeRegistry>();
        registry.Setup(r => r.GetDefinitionFor("tv")).Returns(new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions =
            [
                new() { Slug = "season", DisplayName = "Season", CustomMetadataFields = [] },
                new() { Slug = "episode", DisplayName = "Episode", CustomMetadataFields = [] }
            ]
        });
        _events = [];
        _publishedInTransaction = false;
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) =>
            {
                _events.Add(magpieEvent);
                _publishedInTransaction |= _context.Database.CurrentTransaction is not null;
            })
            .Returns(Task.CompletedTask);
        _subject = new ContentRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            eventBus.Object,
            _time,
            NullLogger<ContentRepository>.Instance, registry.Object,
            Mock.Of<IFileSystem>(), Mock.Of<IAuditRecordRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase(SortDirection.Ascending, 2020, 2021)]
    [TestCase(SortDirection.Descending, 2021, 2020)]
    public async Task QueryAsync_ReturnsSortedItems_WhenScalarSortIsRequested(
        SortDirection direction,
        int firstYear,
        int secondYear)
    {
        // Arrange
        _hierarchyItem.ContentItem = new()
        {
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = new(),
            ContentType = "test",
            Level = "item",
            Slug = "second",
            Name = "Second",
            ContentItem = new()
            {
                CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "createdAt", Direction = direction }).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.CreatedAt.Year), Is.EqualTo([firstYear, secondYear]));
    }

    [TestCase(FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.FulfilledExternally)]
    public async Task UpdateAsync_PreservesUnknownFulfilledAt_WhenItemRemainsFulfilled(FulfillmentStatus status)
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = status,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That((await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_KeepsUnknownDate_WhenStatusChangesWithoutMediaHistory()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = FulfillmentStatus.Fulfilled;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.Null);
    }

    [TestCase(FulfillmentStatus.Fulfilled, FulfillmentStatus.FulfilledExternally)]
    [TestCase(FulfillmentStatus.FulfilledExternally, FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.Fulfilled, FulfillmentStatus.Upgradable)]
    [TestCase(FulfillmentStatus.Partial, FulfillmentStatus.Fulfilled)]
    [TestCase(FulfillmentStatus.Upgradable, FulfillmentStatus.Partial)]
    public async Task UpdateAsync_KeepsFulfilledAt_WhenFulfillmentStaysFulfilled(
        FulfillmentStatus from,
        FulfillmentStatus to)
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = from,
            FulfilledAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = to;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
    }

    [Test]
    public async Task UpdateAsync_ClearsFulfilledAt_WhenFulfillmentBecomesMissing()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            FulfilledAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.Fulfillment = FulfillmentStatus.Missing;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            (await _subject.GetByIdAsync(item.Id).ConfigureAwait(false))!.FulfilledAt,
            Is.Null);
    }

    [Test]
    public async Task UpdateAsync_PublishesTheChangedFieldsAfterCommit_WhenTheQualityStatusChanges()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            QualityStatus = QualityStatus.Met,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        item.QualityStatus = QualityStatus.UpgradeWanted;

        // Act
        await _subject.UpdateAsync(item, null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedInTransaction, Is.False);
            Assert.That(published.Name, Is.EqualTo(MagpieEvents.Names.ContentHierarchyItemUpdated));
            Assert.That(published.Context[MagpieEvents.Keys.ContentHierarchyItemId],
                Is.EqualTo(_hierarchyItem.Id.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("QualityStatus"));
        }
    }

    [Test]
    public async Task UpdateMediaStateAsync_PublishesTheChangedFieldsAfterCommit_WhenTheQualityChanges()
    {
        // Arrange
        var item = new ContentItem
        {
            ContentHierarchyItemId = _hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            QualityStatus = QualityStatus.UpgradeWanted,
            CurrentFileQualityRank = 2,
            PrimaryMediaFileCount = 1,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateMediaStateAsync(
            item with { QualityStatus = QualityStatus.Met, CurrentFileQualityRank = 1 },
            null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedInTransaction, Is.False);
            Assert.That(published.Name, Is.EqualTo(MagpieEvents.Names.ContentHierarchyItemUpdated));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields],
                Is.EqualTo("QualityStatus,CurrentFileQualityRank"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReconcileMediaStateAsync_FulfillsCoveredItemsAndCountsTheFileOncePerTree_WhenAFileIsShared(
        bool itemsHaveTheSameParent)
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var firstEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var secondEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = itemsHaveTheSameParent ? season.Id : null,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var thirdEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-3",
            Name = "Episode 3",
            Ordinal = 3,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.AddRange(firstEpisode, secondEpisode, thirdEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var mediaFile = new MediaFile
        {
            ContentItemId = firstEpisode.ContentItem.Id,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E02.mkv",
            PathKey = "/TV/S01E01-E02.MKV",
            FileSize = 1_400,
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = secondEpisode.ContentItem.Id });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ReconcileMediaStateAsync(season.Id, null).ConfigureAwait(false);
        await _subject.ReconcileMediaStateAsync(secondEpisode.Id, null).ConfigureAwait(false);

        // Assert
        var states = await _context.ContentItems.AsNoTracking()
            .Join(
                _context.ContentHierarchyItems,
                item => item.ContentHierarchyItemId,
                node => node.Id,
                (item, node) => new { node.Slug, item.Fulfillment, item.PrimaryMediaFileCount, item.PrimaryMediaSize })
            .OrderBy(state => state.Slug)
            .ToListAsync()
            .ConfigureAwait(false);
        Assert.That(
            states.Select(state =>
                (state.Slug, state.Fulfillment, state.PrimaryMediaFileCount, state.PrimaryMediaSize)),
            Is.EqualTo(new (string, FulfillmentStatus, int, long)[]
            {
                ("episode-1", FulfillmentStatus.Fulfilled, 1, 1_400),
                ("episode-2", FulfillmentStatus.Fulfilled, 1, 1_400),
                ("episode-3", FulfillmentStatus.Missing, 0, 0),
                ("season-1", FulfillmentStatus.Partial, 1, 1_400)
            }));
    }
}
