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
    private IMediaFileRepository _subject = null!;

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
        _context.AddRange(metadataContainer, hierarchyItem, contentItem);
        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = "/media/Film.mkv",
            PathKey = "/MEDIA/FILM.MKV",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var hierarchy = new Moq.Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetLineageAsync(hierarchyItem.Id,
                Moq.It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync([hierarchyItem]);
        _events = [];
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _events.Add(magpieEvent))
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
}
