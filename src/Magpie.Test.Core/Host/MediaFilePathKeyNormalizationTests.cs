using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.App.Hosting;
using Magpie.Core;
using Magpie.Core.Content;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class MediaFilePathKeyNormalizationTests
{
    private MagpieDbContext _context = null!;
    private IMediaFileRepository _mediaFiles = null!;
    private ITransactionManager _transactions = null!;
    private IAuditRecordRepository _audit = null!;
    private IContentRepository _content = null!;
    private ContentFileService _files = null!;
    private Mock<IContentService> _fulfillment = null!;

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

        _mediaFiles = new MediaFileRepository(_context, NullLogger<MediaFileRepository>.Instance);
        _transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var events = Mock.Of<IEventBus>();
        _audit = new AuditRecordRepository(_context, events, NullLogger<AuditRecordRepository>.Instance,
            _transactions, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        _content = new ContentRepository(_context, _transactions, events, TimeProvider.System,
            NullLogger<ContentRepository>.Instance);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetByIdAsync(hierarchyItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hierarchyItem);
        hierarchy.Setup(repository => repository.GetLineageAsync(hierarchyItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([hierarchyItem]);
        _files = new ContentFileService(hierarchy.Object, Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IRootFolderRepository>(), Mock.Of<IContentTypeRegistry>(), Mock.Of<IFileSystem>(),
            _mediaFiles, _content, _audit, _transactions, TimeProvider.System,
            NullLogger<ContentFileService>.Instance);
        _fulfillment = new Mock<IContentService>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task NormalizeMediaFilePathKeysAsync_PreservesOneAssociationAndAuditsChanges_WhenLegacyPathsCollide()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync("DROP INDEX IX_content_mediaFile_FilePath;")
            .ConfigureAwait(false);
        var original = await _context.MediaFiles.SingleAsync().ConfigureAwait(false);
        original.PathKey = "legacy:original";
        original.CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        original.ModifiedByUserId = new Guid("afe8b71f-9284-4854-aefa-a7facfb32288");
        _context.MediaFiles.Add(original with
        {
            Id = Guid.NewGuid(),
            PathKey = "legacy:duplicate",
            CreatedAt = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await DatabaseStartupService.NormalizeMediaFilePathKeysAsync(_mediaFiles, _content, _files,
            _fulfillment.Object, _transactions, _audit, TimeProvider.System).ConfigureAwait(false);
        var survivor = await _mediaFiles.GetByPathAsync("/media/Film.mkv").ConfigureAwait(false);
        var operations = await _context.AuditRecords.OrderBy(record => record.Operation)
            .Select(record => record.Operation).ToArrayAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(survivor?.CreatedAt, Is.EqualTo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(survivor?.ModifiedByUserId,
                Is.EqualTo(new Guid("afe8b71f-9284-4854-aefa-a7facfb32288")));
            Assert.That(operations, Is.EqualTo(["FilePathNormalized", "FileUnlinked"]));
            Assert.That(await _context.ContentItems.Select(item => item.Fulfillment).SingleAsync()
                .ConfigureAwait(false),
                Is.EqualTo(FulfillmentStatus.Fulfilled));
        }
    }

    [Test]
    public async Task NormalizeMediaFilePathKeysAsync_RollsBackRowsAndAudit_WhenFulfillmentFails()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync("DROP INDEX IX_content_mediaFile_FilePath;")
            .ConfigureAwait(false);
        var original = await _context.MediaFiles.SingleAsync().ConfigureAwait(false);
        original.PathKey = "legacy:original";
        _context.MediaFiles.Add(original with { Id = Guid.NewGuid(), PathKey = "legacy:duplicate" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _fulfillment.Setup(service => service.MaterializeFulfillmentStatusAsync(
            It.IsAny<Guid>(), null, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The reconciliation failed."));

        // Act
        var action = () => DatabaseStartupService.NormalizeMediaFilePathKeysAsync(_mediaFiles, _content, _files,
            _fulfillment.Object, _transactions, _audit, TimeProvider.System);

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await action().ConfigureAwait(false));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.MediaFiles.CountAsync(file => file.PathKey.StartsWith("legacy:"))
                .ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(0));
        }
    }
}
