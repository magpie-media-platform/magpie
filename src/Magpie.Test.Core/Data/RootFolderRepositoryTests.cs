using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;
using Magpie.Models.RootFolders;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using RootFolderEntity = Magpie.Data.Database.Entities.RootFolder;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class RootFolderRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IRootFolderRepository _subject = null!;
    private Guid _userId;
    private Guid _contentTypeId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;
        _contentTypeId = Guid.NewGuid();

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new RootFolderRepository(_context, historyRepo, loggerFactory.CreateLogger<RootFolderRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRootFolder()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(rootFolderEntity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Name))
                .EqualTo("Movies")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Path))
                .EqualTo("/media/movies")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.ContentTypeId))
                .EqualTo(_contentTypeId));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllRootFolders()
    {
        // Arrange
        var rootFolderEntity1 = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolderEntity2 = new RootFolderEntity
        {
            Name = "TV Shows",
            Path = "/media/tv",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(rootFolderEntity1, rootFolderEntity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<RootFolder>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoRootFolders()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<RootFolder>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder()
    {
        // Arrange
        var model = new RootFolder(
            Guid.Empty,
            "Movies",
            "/media/movies",
            _contentTypeId,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Name == "Movies"
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Name))
                    .EqualTo("Movies")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Path))
                    .EqualTo("/media/movies")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.ContentTypeId))
                    .EqualTo(_contentTypeId));
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.Name)).EqualTo("Movies")
                .And.Property(nameof(RootFolderEntity.Path)).EqualTo("/media/movies")
                .And.Property(nameof(RootFolderEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("RootFolder")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRootFolder()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        var newContentTypeId = Guid.NewGuid();
        var model = new RootFolder(
            rootFolderEntity.Id,
            "Updated Movies",
            "/media/movies-updated",
            newContentTypeId,
            rootFolderEntity.CreatedAt,
            rootFolderEntity.CreatedByUserId,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Id == rootFolderEntity.Id
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.Name)).EqualTo("Updated Movies")
                .And.Property(nameof(RootFolderEntity.Path)).EqualTo("/media/movies-updated")
                .And.Property(nameof(RootFolderEntity.ContentTypeId)).EqualTo(newContentTypeId)
                .And.Property(nameof(RootFolderEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(RootFolderEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("RootFolder")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var model = new RootFolder(
            Guid.NewGuid(),
            "NonExistent",
            "/media/nonexistent",
            _contentTypeId,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesRootFolder()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "ToDelete",
            Path = "/media/to-delete",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(rootFolderEntity.Id, _userId);

        // Assert
        var entityExists = await _context.RootFolders.AnyAsync(rf => rf.Id == rootFolderEntity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("RootFolder")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchAsync_PatchesNameField()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "OriginalName",
            Path = "/media/original",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        var patch = new RootFolderPatch
        {
            Id = rootFolderEntity.Id,
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Id == rootFolderEntity.Id
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(RootFolderEntity.Path)).EqualTo("/media/original")
                .And.Property(nameof(RootFolderEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(RootFolderEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("RootFolder")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesPathField()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/original",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        var patch = new RootFolderPatch
        {
            Id = rootFolderEntity.Id,
            Path = new FieldPatch<string>("/media/patched")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Id == rootFolderEntity.Id
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.Name)).EqualTo("Movies")
                .And.Property(nameof(RootFolderEntity.Path)).EqualTo("/media/patched"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesContentTypeIdField()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        var newContentTypeId = Guid.NewGuid();
        var patch = new RootFolderPatch
        {
            Id = rootFolderEntity.Id,
            ContentTypeId = new FieldPatch<Guid>(newContentTypeId)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Id == rootFolderEntity.Id
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.ContentTypeId)).EqualTo(newContentTypeId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesMultipleFields()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "OriginalName",
            Path = "/media/original",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        var newContentTypeId = Guid.NewGuid();
        var patch = new RootFolderPatch
        {
            Id = rootFolderEntity.Id,
            Name = new FieldPatch<string>("PatchedName"),
            Path = new FieldPatch<string>("/media/patched"),
            ContentTypeId = new FieldPatch<Guid>(newContentTypeId)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Id == rootFolderEntity.Id
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(RootFolderEntity.Path)).EqualTo("/media/patched")
                .And.Property(nameof(RootFolderEntity.ContentTypeId)).EqualTo(newContentTypeId));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var patch = new RootFolderPatch
        {
            Id = Guid.NewGuid(),
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task GetByContentTypeIdAsync_ReturnsRootFoldersForContentType()
    {
        // Arrange
        var otherContentTypeId = Guid.NewGuid();
        var rootFolderEntity1 = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolderEntity2 = new RootFolderEntity
        {
            Name = "More Movies",
            Path = "/media/movies2",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolderEntity3 = new RootFolderEntity
        {
            Name = "TV Shows",
            Path = "/media/tv",
            ContentTypeId = otherContentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(rootFolderEntity1, rootFolderEntity2, rootFolderEntity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByContentTypeIdAsync(_contentTypeId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<RootFolder>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByContentTypeIdAsync_ReturnsEmptyList_WhenNoRootFoldersForContentType()
    {
        // Arrange
        var otherContentTypeId = Guid.NewGuid();
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = otherContentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByContentTypeIdAsync(_contentTypeId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<RootFolder>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByPathAsync_ReturnsRootFolder()
    {
        // Arrange
        var rootFolderEntity = new RootFolderEntity
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolderEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByPathAsync("/media/movies");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Name)).EqualTo("Movies")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Path)).EqualTo("/media/movies"));
    }

    [Test]
    public async Task GetByPathAsync_ReturnsNull_WhenPathDoesNotExist()
    {
        // Act
        var result = await _subject.GetByPathAsync("/nonexistent/path");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task CreateAsync_WithNullUserId_CreatesRootFolder()
    {
        // Arrange
        var model = new RootFolder(
            Guid.Empty,
            "System Folder",
            "/media/system",
            _contentTypeId,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, null);

        // Assert
        var entity = await (
            from rf in _context.RootFolders.AsNoTracking()
            where rf.Name == "System Folder"
            select rf).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<RootFolder>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(RootFolder.Name))
                    .EqualTo("System Folder"));
            Assert.That(entity, Is.InstanceOf<RootFolderEntity>()
                .With.Property(nameof(RootFolderEntity.CreatedByUserId)).Null);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.UserId)).Null);
        }
    }

    [Test]
    public async Task UpdateAsync_DoesNotCreateHistoryEntry_WhenNoRowsUpdated()
    {
        // Arrange
        var model = new RootFolder(
            Guid.NewGuid(),
            "NonExistent",
            "/media/nonexistent",
            _contentTypeId,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        await _subject.UpdateAsync(model, _userId);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateHistoryEntry_WhenRootFolderDoesNotExist()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_DoesNotCreateHistoryEntry_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var patch = new RootFolderPatch
        {
            Id = Guid.NewGuid(),
            Name = new FieldPatch<string>("PatchedName")
        };

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
