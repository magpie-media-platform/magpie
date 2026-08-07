using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class RootFolderRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IRootFolderRepository _subject = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;
    private string _contentType = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;
        _contentType = "type-a";

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var events = new Mock<IEventBus>(MockBehavior.Strict);
        events
            .Setup(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository>();

        _subject = new RootFolderRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _contentHierarchyItems.Object,
            events.Object,
            loggerFactory.CreateLogger<RootFolderRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRootFolder_WhenRootFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(rootFolder.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo(_contentType));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRootFolders_WhenFoldersExist()
    {
        // Arrange
        var rootFolder1 = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolder2 = new RootFolder
        {
            Name = "TV Shows",
            Path = "/media/tv",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(rootFolder1, rootFolder2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRootFolders()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder_WhenRequestIsValid()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "Movies")
            .SingleOrDefaultAsync().ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo(_contentType));
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRootFolder_WhenFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        const string newContentType = "type-c";
        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = "Updated Movies",
            Path = "/media/movies-updated",
            ContentType = newContentType,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Updated Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies-updated"));
            Assert.That(entity, Has.Property(nameof(entity.ContentType)).EqualTo(newContentType));
            Assert.That(entity, Has.Property(nameof(entity.ModifiedAt)).Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_PersistsRenameAndImportFields_WhenValuesAreProvided()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            ImportMode = ImportMode.Move,
            DefaultMonitoring = true,
            RenameTemplate = null,
            DefaultImportCollisionMode = ImportCollisionMode.Fail,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = rootFolder.Name,
            Path = rootFolder.Path,
            ContentType = rootFolder.ContentType,
            ImportMode = ImportMode.Copy,
            DefaultMonitoring = null,
            RenameTemplate = "{Title}.{Ext}",
            DefaultImportCollisionMode = ImportCollisionMode.Replace,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.RenameTemplate)).EqualTo("{Title}.{Ext}"));
            Assert.That(entity, Has.Property(nameof(entity.ImportMode)).EqualTo(ImportMode.Copy));
            Assert.That(entity, Has.Property(nameof(entity.DefaultMonitoring)).EqualTo(null));
            Assert.That(
                entity,
                Has.Property(nameof(entity.DefaultImportCollisionMode)).EqualTo(ImportCollisionMode.Replace));
        }
    }

    [Test]
    public async Task CreateAsync_DefaultsUpgradeFileHandlingToReplace_WhenValueIsNotProvided()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "Movies")
            .SingleOrDefaultAsync().ConfigureAwait(false);

        Assert.That(entity, Has.Property(nameof(entity.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.Replace));
    }

    [Test]
    public async Task CreateAndUpdateAsync_RoundTripsUpgradeFileHandling_WhenValueChanges()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            UpgradeFileHandling = UpgradeFileHandling.KeepBoth
        };

        var created = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Act
        var afterCreate = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == created.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            afterCreate,
            Has.Property(nameof(afterCreate.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.KeepBoth));

        var update = created with { UpgradeFileHandling = UpgradeFileHandling.Replace };
        var result = await _subject.UpdateAsync(update, _userId).ConfigureAwait(false);
        var afterUpdate = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == created.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                afterUpdate,
                Has.Property(nameof(afterUpdate.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.Replace));
        }
    }

    [Test]
    public async Task UpdateAsync_ClearsRenameTemplate_WhenSetToNull()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            RenameTemplate = "{Title}.{Ext}",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = rootFolder.Name,
            Path = rootFolder.Path,
            ContentType = rootFolder.ContentType,
            RenameTemplate = null,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.RenameTemplate)).Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var model = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Path = "/media/nonexistent",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesRootFolder_WhenFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "ToDelete",
            Path = "/media/to-delete",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(rootFolder.Id, _userId).ConfigureAwait(false);

        // Assert
        var entityExists = await _context.RootFolders.AnyAsync(rf => rf.Id == rootFolder.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateAsync_WithNullUserId_CreatesRootFolder()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "System Folder",
            Path = "/media/system",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, null).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "System Folder")
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("System Folder"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).Null);
        }
    }

    [Test]
    public async Task UpdateAsync_DoesNotCreateAuditRecord_WhenNoRowsUpdated()
    {
        // Arrange
        var model = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Path = "/media/nonexistent",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateAuditRecord_WhenRootFolderDoesNotExist()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_PublishesRootFolderCreatedEvent_WhenCreateSucceeds()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo(MagpieEvents.Names.RootFolderCreated));
            Assert.That(
                _publishedEvents[0].Context[MagpieEvents.Keys.RootFolderId],
                Is.EqualTo(result.Id.ToString("D")));
            Assert.That(_publishedEvents[0].Context[MagpieEvents.Keys.RootFolderPath], Is.EqualTo("/media/movies"));
            Assert.That(_publishedEvents[0].Context[MagpieEvents.Keys.RootFolderContentType], Is.EqualTo(_contentType));
        }
    }

    [Test]
    public async Task CreateAsync_ThrowsAfterCommittedWrite_WhenEventPublishFails()
    {
        // Arrange
        var loggerFactory = NullLoggerFactory.Instance;
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var failing = new Mock<IEventBus>();
        failing
            .Setup(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));
        var subject = new RootFolderRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IContentHierarchyItemRepository>(),
            failing.Object,
            loggerFactory.CreateLogger<RootFolderRepository>());
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await subject.CreateAsync(model, _userId, default).ConfigureAwait(false));

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Path == model.Path)
            .SingleOrDefaultAsync().ConfigureAwait(false);
        failing.Verify(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Has.Message.EqualTo("publish failed"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Movies"));
        }
    }

    [Test]
    public async Task UpdateAsync_InvalidatesImportCandidatesLeftOutsideAllRoots_WhenRootRepointed()
    {
        // Arrange
        var movies = new RootFolder
        {
            Name = "Movies",
            Path = @"\\mediahost\movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var shows = new RootFolder
        {
            Name = "Shows",
            Path = @"D:\shows",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(movies, shows);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var orphaned = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Heat (1995)\heat.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var stillValid = new LibraryImportCandidate
        {
            RootFolderId = shows.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"D:\shows\The Wire\s01e01.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var alreadyResolved = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Old (1999)\old.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.LibraryImportCandidates.AddRange(orphaned, stillValid, alreadyResolved);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var repointed = movies with { Path = @"D:\movies" };
        var updated = await _subject.UpdateAsync(repointed, _userId, default).ConfigureAwait(false);

        // Assert
        var orphanedAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == orphaned.Id).ConfigureAwait(false);
        var stillValidAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == stillValid.Id).ConfigureAwait(false);
        var resolvedAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == alreadyResolved.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(orphanedAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Invalidated));
            Assert.That(orphanedAfter.ModifiedByUserId, Is.EqualTo(_userId));
            Assert.That(stillValidAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(resolvedAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Queued));
            Assert.That(
                _publishedEvents,
                Has.One.Property(nameof(MagpieEvent.Name)).EqualTo(MagpieEvents.Names.LibraryImportCandidateUpdated));
            Assert.That(
                _publishedEvents.Single(magpieEvent =>
                    magpieEvent.Name == MagpieEvents.Names.LibraryImportCandidateUpdated).Context.Values,
                Does.Contain(orphaned.Id.ToString("D")));
        }
    }

    [Test]
    public async Task UpdateAsync_LeavesImportCandidatesAlone_WhenPathStillCoversThem()
    {
        // Arrange
        var movies = new RootFolder
        {
            Name = "Movies",
            Path = @"D:\movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(movies);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var match = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"D:\movies\Heat (1995)\heat.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.LibraryImportCandidates.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var renamed = movies with { Name = "Films" };
        await _subject.UpdateAsync(renamed, _userId, default).ConfigureAwait(false);

        // Assert
        var after = await _context.LibraryImportCandidates.AsNoTracking().SingleAsync(m => m.Id == match.Id)
            .ConfigureAwait(false);
        Assert.That(after.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
    }

    [Test]
    public async Task UpdateAsync_ReplacesLevelDefaultAndRecomputesEffectiveValues_WhenMappingChanges()
    {
        // Arrange
        var firstTierList = new QualityTierList
        {
            Name = "First",
            CreatedAt = DateTime.UtcNow,
        };
        var secondTierList = new QualityTierList
        {
            Name = "Second",
            CreatedAt = DateTime.UtcNow,
        };
        var rootFolder = new RootFolder
        {
            Name = "Books",
            Path = "/books",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
        };
        _context.QualityTierLists.AddRange(firstTierList, secondTierList);
        rootFolder.Defaults = new()
        {
            ["book"] = new RootFolderDefault
            {
                QualityTierListId = firstTierList.Id,
            },
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var updated = rootFolder with
        {
            Defaults = new()
            {
                ["book"] = new RootFolderDefault
                {
                    QualityTierListId = secondTierList.Id,
                },
            },
        };

        // Act
        var result = await _subject.UpdateAsync(updated, _userId).ConfigureAwait(false);

        // Assert
        var stored = (await _context.RootFolders.AsNoTracking().SingleAsync().ConfigureAwait(false))
            .Defaults["book"];
        var published = _publishedEvents.Single(magpieEvent =>
            magpieEvent.Name == MagpieEvents.Names.RootFolderUpdated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.QualityTierListId, Is.EqualTo(secondTierList.Id));
            Assert.That(
                published.Context[MagpieEvents.Keys.RootFolderChangedFields],
                Does.Contain("defaults"));
        }

        _contentHierarchyItems.Verify(
            items => items.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateAsync_AppliesDefaultRadioSemantics_AndTriggersEffectiveRecompute()
    {
        // Arrange
        var first = await _subject.CreateAsync(
            new RootFolder
            {
                Name = "First",
                Path = "/library/first",
                ContentType = _contentType
            },
            _userId).ConfigureAwait(false);

        // Act
        var second = await _subject.CreateAsync(
            new RootFolder
            {
                Name = "Second",
                Path = "/library/second",
                ContentType = _contentType,
                IsDefault = true
            },
            _userId).ConfigureAwait(false);

        // Assert
        var roots = await _context.RootFolders.AsNoTracking()
            .Where(folder => folder.ContentType == _contentType)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roots.Single(folder => folder.Id == first.Id).IsDefault, Is.False);
            Assert.That(roots.Single(folder => folder.Id == second.Id).IsDefault, Is.True);
        }

        _contentHierarchyItems.Verify(
            items => items.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
