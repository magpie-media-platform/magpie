using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

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
        _contentTypeId = Guid.NewGuid();

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);

        _subject = new RootFolderRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<RootFolderRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRootFolder()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(rootFolder.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentTypeId)).EqualTo(_contentTypeId));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRootFolders()
    {
        // Arrange
        var rootFolder1 = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolder2 = new RootFolder
        {
            Name = "TV Shows",
            Path = "/media/tv",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(rootFolder1, rootFolder2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRootFolders()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "Movies")
            .SingleOrDefaultAsync();
        var audit = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentTypeId)).EqualTo(_contentTypeId));
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRootFolder()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync();

        var newContentTypeId = Guid.NewGuid();
        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = "Updated Movies",
            Path = "/media/movies-updated",
            ContentTypeId = newContentTypeId,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Updated Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies-updated"));
            Assert.That(entity, Has.Property(nameof(entity.ContentTypeId)).EqualTo(newContentTypeId));
            Assert.That(entity, Has.Property(nameof(entity.ModifiedAt)).Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.ModifiedByUserId)).EqualTo(_userId));
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
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesRootFolder()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "ToDelete",
            Path = "/media/to-delete",
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(rootFolder.Id, _userId);

        // Assert
        var entityExists = await _context.RootFolders.AnyAsync(rf => rf.Id == rootFolder.Id);

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
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

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
            ContentTypeId = _contentTypeId
        };

        // Act
        var result = await _subject.CreateAsync(model, null);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "System Folder")
            .SingleOrDefaultAsync();

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
            ContentTypeId = _contentTypeId,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.UpdateAsync(model, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateAuditRecord_WhenRootFolderDoesNotExist()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }
}
