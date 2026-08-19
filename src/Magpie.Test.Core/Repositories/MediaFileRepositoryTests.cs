using System;
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
        _context.MediaFiles.AddRange(
            new MediaFile
            {
                ContentItemId = contentItem.Id,
                FileKind = "Video",
                FilePath = "/media/Film.mkv",
                PathKey = OperatingSystem.IsWindows() ? "/MEDIA/FILM.MKV" : "/media/Film.mkv",
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
    public async Task GetByPathAsync_ReturnsPlatformMatch_WhenPathDiffersOnlyByCase()
    {
        // Arrange
        const string path = "/media/FILM.MKV";

        // Act
        var result = await _subject.GetByPathAsync(path).ConfigureAwait(false);

        // Assert
        Assert.That(result is not null, Is.EqualTo(OperatingSystem.IsWindows()));
    }
}
