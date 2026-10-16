using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class MediaFileRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private List<MagpieEvent> _events = null!;
    private bool _publishedInTransaction;
    private IMediaFileRepository _subject = null!;
    private Guid _movieItemId;
    private Guid _firstEpisodeItemId;
    private Guid _secondEpisodeItemId;
    private Guid _thirdEpisodeItemId;

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
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

        var metadataContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var hierarchyItem = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = metadataContainer.Id,
            ContentType = "movie",
            Level = "movie",
            Slug = "film",
            Name = "Film",
            EffectiveRootFolderId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = hierarchyItem.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow
        };
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
            ParentId = season.Id,
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
        _context.AddRange(
            metadataContainer,
            hierarchyItem,
            contentItem,
            season,
            firstEpisode,
            secondEpisode,
            thirdEpisode);
        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = "/media/Film.mkv",
            PathKey = "/MEDIA/FILM.MKV",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        _movieItemId = contentItem.Id;
        _firstEpisodeItemId = firstEpisode.ContentItem.Id;
        _secondEpisodeItemId = secondEpisode.ContentItem.Id;
        _thirdEpisodeItemId = thirdEpisode.ContentItem.Id;

        var hierarchy = new Moq.Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        hierarchy.Setup(repository => repository.GetLineageAsync(hierarchyItem.Id,
                Moq.It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync([hierarchyItem]);
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
        _subject = new MediaFileRepository(_context, NullLogger<MediaFileRepository>.Instance,
            new Magpie.Core.TransactionManager(_context, NullLogger<Magpie.Core.TransactionManager>.Instance),
            Moq.Mock.Of<IContentRepository>(), hierarchy.Object, eventBus.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    [Platform("Win", Reason = "Only Windows looks up media file paths without case.")]
    public async Task GetByPathAsync_ReturnsTheStoredFile_WhenThePathDiffersOnlyByCase()
    {
        // Act
        var result = await _subject.GetByPathAsync("/media/FILM.MKV").ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(MediaFile.FilePath)).EqualTo("/media/Film.mkv"));
    }

    [TestCase(SortDirection.Ascending, 100L, 200L)]
    [TestCase(SortDirection.Descending, 200L, 100L)]
    public async Task QueryAsync_ReturnsSortedFiles_WhenScalarSortIsRequested(
        SortDirection direction,
        long firstSize,
        long secondSize)
    {
        // Arrange
        var original = await _context.MediaFiles.SingleAsync().ConfigureAwait(false);
        original.FileSize = 200;
        _context.MediaFiles.Add(original with
        {
            Id = Guid.NewGuid(),
            FilePath = "/media/Second.mkv",
            PathKey = "/MEDIA/SECOND.MKV",
            FileSize = 100
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 },
            new() { PropertyName = "fileSize", Direction = direction }).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(file => file.FileSize), Is.EqualTo([firstSize, secondSize]));
    }

    [Test]
    public async Task UpdateAsync_PublishesContentUpdate_WhenMediaInfoChanges()
    {
        // Arrange
        var original = await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var hierarchyItemId = await _context.ContentItems
            .Where(item => item.Id == original.ContentItemId)
            .Select(item => item.ContentHierarchyItemId)
            .SingleAsync()
            .ConfigureAwait(false);

        // Act
        var result = await _subject.UpdateAsync(original with
        {
            MediaInfo = new() { Container = "matroska", DurationSeconds = 5_400 }
        }, null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(published.Name, Is.EqualTo("content.hierarchyItem.updated"));
            Assert.That(published.Context[MagpieEvents.Keys.ContentHierarchyItemId],
                Is.EqualTo(hierarchyItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentItemId],
                Is.EqualTo(original.ContentItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("MediaInfo"));
        }
    }

    [Test]
    public async Task CreateAsync_PublishesContentUpdateAfterCommit_WhenTheFileHasMediaInfo()
    {
        // Arrange
        var hierarchyItemId = await _context.ContentItems
            .Where(item => item.Id == _movieItemId)
            .Select(item => item.ContentHierarchyItemId)
            .SingleAsync()
            .ConfigureAwait(false);

        // Act
        await _subject.CreateAsync(
            new MediaFile
            {
                ContentItemId = _movieItemId,
                FileKind = "Video",
                FilePath = "/media/Second.mkv",
                MediaInfo = new() { Container = "matroska", DurationSeconds = 5_400 },
                CreatedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedInTransaction, Is.False);
            Assert.That(published.Name, Is.EqualTo("content.hierarchyItem.updated"));
            Assert.That(published.Context[MagpieEvents.Keys.ContentHierarchyItemId],
                Is.EqualTo(hierarchyItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentItemId],
                Is.EqualTo(_movieItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("MediaFiles,MediaInfo"));
        }
    }

    [Test]
    public async Task DeleteAsync_PublishesContentUpdateAfterCommit_WhenTheFileIsDeleted()
    {
        // Arrange
        var original = await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(original.Id, null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedInTransaction, Is.False);
            Assert.That(published.Context[MagpieEvents.Keys.ContentItemId],
                Is.EqualTo(_movieItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("MediaFiles"));
        }
    }

    [Test]
    public async Task UpdateAsync_PublishesContentUpdate_WhenTheFileIsRenamed()
    {
        // Arrange
        var original = await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(original with { FilePath = "/media/Renamed.mkv" }, null).ConfigureAwait(false);

        // Assert
        var published = _events.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedInTransaction, Is.False);
            Assert.That(published.Context[MagpieEvents.Keys.ContentItemId],
                Is.EqualTo(_movieItemId.ToString("D")));
            Assert.That(published.Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("MediaFiles"));
        }
    }

    [Test]
    public async Task UpdateAsync_PublishesContentUpdate_WhenTheFileIsReplacedInPlace()
    {
        // Arrange
        var original = await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(original with { ReplacedAt = DateTime.UtcNow }, null).ConfigureAwait(false);

        // Assert
        Assert.That(_events.Single().Context[MagpieEvents.Keys.ContentChangedFields], Is.EqualTo("MediaFiles"));
    }

    [Test]
    public async Task UpdateAsync_PublishesContentUpdateForBothItems_WhenTheFileMovesToAnotherItem()
    {
        // Arrange
        var original = await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(original with { ContentItemId = _firstEpisodeItemId }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_events.Select(published => published.Context[MagpieEvents.Keys.ContentItemId]),
                Is.EquivalentTo([_movieItemId.ToString("D"), _firstEpisodeItemId.ToString("D")]));
            Assert.That(_events.Select(published => published.Context[MagpieEvents.Keys.ContentChangedFields]),
                Is.All.EqualTo("MediaFiles"));
        }
    }

    [Test]
    public async Task CreateAsync_SavesNextFile_WhenPreviousCreateHitThePathIndex()
    {
        // Arrange
        var contentItemId = (await _context.MediaFiles.AsNoTracking().SingleAsync().ConfigureAwait(false))
            .ContentItemId;
        Assert.ThrowsAsync<DbUpdateException>(() => _subject.CreateAsync(
            new MediaFile
            {
                ContentItemId = contentItemId,
                FileKind = "Video",
                FilePath = "/media/Film.mkv",
                CreatedAt = DateTime.UtcNow
            },
            null));

        // Act
        await _subject.CreateAsync(
            new MediaFile
            {
                ContentItemId = contentItemId,
                FileKind = "Video",
                FilePath = "/media/Second.mkv",
                CreatedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(
            await _context.MediaFiles.AsNoTracking()
                .OrderBy(file => file.FilePath)
                .Select(file => file.FilePath)
                .ToListAsync()
                .ConfigureAwait(false),
            Is.EqualTo(["/media/Film.mkv", "/media/Second.mkv"]));
    }

    [Test]
    public async Task CreateAsync_StoresTheLowestOrdinalItemAsTheOwner_WhenTheFileCoversSiblings()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _secondEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E03.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _thirdEpisodeItemId });
        mediaFile.Coverage.Add(new() { ContentItemId = _firstEpisodeItemId });

        // Act
        await _subject.CreateAsync(mediaFile, null).ConfigureAwait(false);

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking()
            .Include(file => file.Coverage)
            .SingleAsync(file => file.FilePath == "/tv/S01E01-E03.mkv")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.ContentItemId, Is.EqualTo(_firstEpisodeItemId));
            Assert.That(
                stored.Coverage.Select(coverage => coverage.ContentItemId),
                Is.EquivalentTo([_secondEpisodeItemId, _thirdEpisodeItemId]));
        }
    }

    [Test]
    public async Task CreateAsync_ThrowsAndStoresNothing_WhenACoveredItemHasAnotherParent()
    {
        // Arrange
        var otherSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            CreatedAt = DateTime.UtcNow
        };
        var otherEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = otherSeason.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "season-2-episode-1",
            Name = "Episode 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.AddRange(otherSeason, otherEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = otherEpisode.ContentItem.Id });

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.CreateAsync(mediaFile, null));

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking().CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("The items that the media file covers are not siblings."));
            Assert.That(stored, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_ThrowsAndStoresNothing_WhenACoveredItemHasNoParent()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _movieItemId });

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.CreateAsync(mediaFile, null));

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking().CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("The items that the media file covers are not siblings."));
            Assert.That(stored, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_PromotesTheNextCoveredItem_WhenTheOwnerLetsGoOfTheFile()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E03.mkv",
            PathKey = "/TV/S01E01-E03.MKV",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _secondEpisodeItemId });
        mediaFile.Coverage.Add(new() { ContentItemId = _thirdEpisodeItemId });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var current = await _subject.GetByIdAsync(mediaFile.Id).ConfigureAwait(false);

        // Act
        var result = await _subject.UpdateAsync(current! with { ContentItemId = _thirdEpisodeItemId }, null)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking()
            .Include(file => file.Coverage)
            .SingleAsync(file => file.Id == mediaFile.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.ContentItemId, Is.EqualTo(_secondEpisodeItemId));
            Assert.That(stored.Coverage.Select(coverage => coverage.ContentItemId), Is.EqualTo([_thirdEpisodeItemId]));
        }
    }

    [Test]
    public async Task UpdateAsync_LeavesTheNewOwnerWithoutCoverage_WhenTheOwnerOfATwoEpisodeFileLetsGo()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E02.mkv",
            PathKey = "/TV/S01E01-E02.MKV",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _secondEpisodeItemId });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var current = await _subject.GetByIdAsync(mediaFile.Id).ConfigureAwait(false);

        // Act
        var result = await _subject.UpdateAsync(current! with { ContentItemId = _secondEpisodeItemId }, null)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking()
            .Include(file => file.Coverage)
            .SingleAsync(file => file.Id == mediaFile.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.ContentItemId, Is.EqualTo(_secondEpisodeItemId));
            Assert.That(stored.Coverage, Is.Empty);
        }
    }

    [Test]
    public async Task UpdateAsync_KeepsTheCoverage_WhenOnlyFilePropertiesChange()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E03.mkv",
            PathKey = "/TV/S01E01-E03.MKV",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _secondEpisodeItemId });
        mediaFile.Coverage.Add(new() { ContentItemId = _thirdEpisodeItemId });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var current = await _subject.GetByIdAsync(mediaFile.Id).ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(current! with { FileSize = 1_000 }, null).ConfigureAwait(false);

        // Assert
        var stored = await _context.MediaFiles.AsNoTracking()
            .Include(file => file.Coverage)
            .SingleAsync(file => file.Id == mediaFile.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.FileSize, Is.EqualTo(1_000));
            Assert.That(stored.ContentItemId, Is.EqualTo(_firstEpisodeItemId));
            Assert.That(
                stored.Coverage.Select(coverage => coverage.ContentItemId),
                Is.EquivalentTo([_secondEpisodeItemId, _thirdEpisodeItemId]));
        }
    }

    [Test]
    public async Task UpdateAsync_AddsTheCoverage_WhenTheFileWasCreatedInTheSameScope()
    {
        // Arrange
        var created = await _subject.CreateAsync(
            new MediaFile
            {
                ContentItemId = _firstEpisodeItemId,
                FileKind = "Video",
                FilePath = "/tv/S01E01-E02.mkv",
                CreatedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);
        var current = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        current!.Coverage.Add(new() { ContentItemId = _secondEpisodeItemId });

        // Act
        await _subject.UpdateAsync(current, null).ConfigureAwait(false);

        // Assert
        Assert.That(
            await _context.MediaFileCoverages.AsNoTracking()
                .Select(coverage => coverage.ContentItemId)
                .ToListAsync()
                .ConfigureAwait(false),
            Is.EqualTo([_secondEpisodeItemId]));
    }

    [Test]
    public async Task DeleteAsync_RemovesTheCoverage_WhenTheFileIsDeleted()
    {
        // Arrange
        var mediaFile = new MediaFile
        {
            ContentItemId = _firstEpisodeItemId,
            FileKind = "Video",
            FilePath = "/tv/S01E01-E02.mkv",
            PathKey = "/TV/S01E01-E02.MKV",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = _secondEpisodeItemId });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(mediaFile.Id, null).ConfigureAwait(false);

        // Assert
        Assert.That(await _context.MediaFileCoverages.AsNoTracking().CountAsync().ConfigureAwait(false), Is.Zero);
    }
}
