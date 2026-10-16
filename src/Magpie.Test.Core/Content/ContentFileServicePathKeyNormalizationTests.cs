using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Content;
using Magpie.Core.Repositories;
using Magpie.Core.Subjects;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentFileServicePathKeyNormalizationTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IMediaFileRepository _mediaFiles = null!;
    private ITransactionManager _transactions = null!;
    private IAuditRecordRepository _audit = null!;
    private IContentRepository _content = null!;
    private ContentFileService _files = null!;
    private Mock<IAuditRecordRepository> _fileAudit = null!;

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

        _transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var events = Mock.Of<IEventBus>();
        _audit = new AuditRecordRepository(_context, events, NullLogger<AuditRecordRepository>.Instance,
            _transactions, Mock.Of<IJobExecutionScopeFactory>(), TimeProvider.System);
        _content = new ContentRepository(_context, _transactions, events, TimeProvider.System,
            NullLogger<ContentRepository>.Instance, Mock.Of<IContentTypeRegistry>(), Mock.Of<IFileSystem>(), _audit);
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetByIdAsync(hierarchyItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hierarchyItem);
        hierarchy.Setup(repository => repository.GetLineageAsync(hierarchyItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([hierarchyItem]);
        _mediaFiles = new MediaFileRepository(_context, NullLogger<MediaFileRepository>.Instance,
            _transactions, Mock.Of<IContentRepository>(), hierarchy.Object, events);
        _fileAudit = new Mock<IAuditRecordRepository>();
        _fileAudit.Setup(repository => repository.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Returns((AuditRecord record, CancellationToken cancellationToken) =>
                _audit.CreateAsync(record, cancellationToken));
        _files = new ContentFileService(hierarchy.Object, Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IRootFolderRepository>(), Mock.Of<IContentTypeRegistry>(),
            new SubjectKindRegistry([], NullLogger<SubjectKindRegistry>.Instance),
            Mock.Of<IFileSystem>(),
            _mediaFiles, _content, _fileAudit.Object, _transactions, TimeProvider.System,
            NullLogger<ContentFileService>.Instance);
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
        await _files.NormalizeMediaFilePathKeysAsync().ConfigureAwait(false);
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
    public async Task NormalizeMediaFilePathKeysAsync_RollsBackRowsAndAudit_WhenAuditFails()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync("DROP INDEX IX_content_mediaFile_FilePath;")
            .ConfigureAwait(false);
        var original = await _context.MediaFiles.SingleAsync().ConfigureAwait(false);
        original.PathKey = "legacy:original";
        _context.MediaFiles.Add(original with { Id = Guid.NewGuid(), PathKey = "legacy:duplicate" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _fileAudit.Setup(repository => repository.CreateAsync(
            It.Is<AuditRecord>(record => record.Operation == "FilePathNormalized"),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit record failed."));

        // Act
        var action = () => _files.NormalizeMediaFilePathKeysAsync();

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
