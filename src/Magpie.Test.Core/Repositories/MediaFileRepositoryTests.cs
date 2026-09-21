using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class MediaFileRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMediaFileRepository _subject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        await _context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

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

        _subject = new MediaFileRepository(_context, NullLogger<MediaFileRepository>.Instance);
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
}
