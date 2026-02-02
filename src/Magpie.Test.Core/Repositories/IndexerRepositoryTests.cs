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
public class IndexerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerRepository _subject = null!;
    private Guid _userId;

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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new IndexerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<IndexerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIndexer_WhenExists()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "TestIndexer",
            Description = "Test indexer description",
            Availability = IndexerAvailability.Public,
            Categories = ["movies", "tv"],
            Languages = ["en", "fr"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(indexer.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestIndexer"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("newznab"));
            Assert.That(result, Has.Property(nameof(result.Description)).EqualTo("Test indexer description"));
            Assert.That(result, Has.Property(nameof(result.Availability)).EqualTo(IndexerAvailability.Public));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllIndexers()
    {
        // Arrange
        var indexer1 = new Indexer
        {
            ProviderType = "newznab",
            Name = "Indexer1",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        var indexer2 = new Indexer
        {
            ProviderType = "torznab",
            Name = "Indexer2",
            Availability = IndexerAvailability.Private,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.AddRange(indexer1, indexer2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexers()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexer()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "NewIndexer",
            Description = "A new indexer",
            Availability = IndexerAvailability.SemiPrivate,
            Categories = ["movies"],
            Languages = ["en"]
        };

        // Act
        var result = await _subject.CreateAsync(indexer, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewIndexer"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("newznab"));
            Assert.That(result, Has.Property(nameof(result.Description)).EqualTo("A new indexer"));
            Assert.That(result, Has.Property(nameof(result.Availability)).EqualTo(IndexerAvailability.SemiPrivate));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "AuditedIndexer",
            Availability = IndexerAvailability.Public
        };

        // Act
        await _subject.CreateAsync(indexer, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Indexer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesIndexer_WhenExists()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "OriginalIndexer",
            Description = "Original description",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        indexer.Name = "UpdatedIndexer";
        indexer.Description = "Updated description";
        indexer.Availability = IndexerAvailability.Private;
        indexer.ProviderType = "torznab";

        // Act
        var result = await _subject.UpdateAsync(indexer, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(indexer.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedIndexer"));
            Assert.That(updated, Has.Property(nameof(updated.Description)).EqualTo("Updated description"));
            Assert.That(updated, Has.Property(nameof(updated.Availability)).EqualTo(IndexerAvailability.Private));
            Assert.That(updated, Has.Property(nameof(updated.ProviderType)).EqualTo("torznab"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            ProviderType = "newznab",
            Name = "NonExistent",
            Availability = IndexerAvailability.Public
        };

        // Act
        var result = await _subject.UpdateAsync(indexer, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "ToUpdate",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        indexer.Name = "Updated";

        // Act
        await _subject.UpdateAsync(indexer, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Indexer"));
        }
    }

    [Test]
    public async Task UpdateAsync_DoesNotCreateAuditRecord_WhenNotExists()
    {
        // Arrange
        var indexer = new Indexer
        {
            Id = Guid.NewGuid(),
            ProviderType = "newznab",
            Name = "NonExistent",
            Availability = IndexerAvailability.Public
        };

        // Act
        await _subject.UpdateAsync(indexer, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesIndexer_WhenExists()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "ToDelete",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(indexer.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(indexer.Id);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "DeleteAudit",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(indexer.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Indexer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateAuditRecord_WhenNotExists()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesRelatedConnections()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "IndexerWithConnections",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        var connection1 = new IndexerConnection
        {
            IndexerId = indexer.Id,
            Name = "Connection1",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = indexer.Id,
            Name = "Connection2",
            Enabled = false,
            Status = IndexerStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(indexer.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var remainingConnections = await _context.IndexerConnections
            .Where(c => c.IndexerId == indexer.Id)
            .ToListAsync();
        Assert.That(remainingConnections, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_SetsCategories()
    {
        // Arrange
        var categories = new[] { "movies", "tv", "music" }.ToList();
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "CategoriesIndexer",
            Availability = IndexerAvailability.Public,
            Categories = categories
        };

        // Act
        var result = await _subject.CreateAsync(indexer, _userId);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id);
        Assert.That(retrieved!.Categories, Is.EqualTo(categories));
    }

    [Test]
    public async Task CreateAsync_SetsLanguages()
    {
        // Arrange
        var languages = new[] { "en", "fr", "de" }.ToList();
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "LanguagesIndexer",
            Availability = IndexerAvailability.Public,
            Languages = languages
        };

        // Act
        var result = await _subject.CreateAsync(indexer, _userId);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id);
        Assert.That(retrieved!.Languages, Is.EqualTo(languages));
    }

    [Test]
    public async Task UpdateAsync_UpdatesCategories()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "UpdateCategoriesIndexer",
            Availability = IndexerAvailability.Public,
            Categories = ["movies"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        var newCategories = new[] { "tv", "music", "books" }.ToList();
        indexer.Categories = newCategories;

        // Act
        await _subject.UpdateAsync(indexer, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id);
        Assert.That(updated!.Categories, Is.EqualTo(newCategories));
    }

    [Test]
    public async Task UpdateAsync_UpdatesLanguages()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "UpdateLanguagesIndexer",
            Availability = IndexerAvailability.Public,
            Languages = ["en"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync();

        var newLanguages = new[] { "fr", "de", "es" }.ToList();
        indexer.Languages = newLanguages;

        // Act
        await _subject.UpdateAsync(indexer, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id);
        Assert.That(updated!.Languages, Is.EqualTo(newLanguages));
    }
}
