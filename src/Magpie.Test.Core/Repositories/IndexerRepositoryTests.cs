using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class IndexerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IIndexerRepository _subject = null!;
    private Guid _userId;

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
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

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

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new IndexerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IEventBus>(),
            NullLogger<IndexerRepository>.Instance,
            Mock.Of<IDataProtectionService>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
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
            Categories = ["type-a", "type-c"],
            Languages = ["en", "fr"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllIndexers_WhenIndexersExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexers()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexer_WhenRequestIsValid()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "NewIndexer",
            Description = "A new indexer",
            Availability = IndexerAvailability.SemiPrivate,
            Categories = ["type-a"],
            Languages = ["en"]
        };

        // Act
        var result = await _subject.CreateAsync(indexer, _userId).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesAuditRecord_WhenIndexerIsCreated()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "AuditedIndexer",
            Availability = IndexerAvailability.Public
        };

        // Act
        await _subject.CreateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        indexer.Name = "UpdatedIndexer";
        indexer.Description = "Updated description";
        indexer.Availability = IndexerAvailability.Private;
        indexer.ProviderType = "torznab";

        // Act
        var result = await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
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
        var result = await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenIndexerIsUpdated()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        indexer.Name = "Updated";

        // Act
        await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(indexer.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalseAndWritesNoAuditRecord_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(auditRecords, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenIndexerIsDeleted()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(indexer.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Indexer"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesRelatedConnections_WhenIndexerIsDeleted()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var connection1 = new IndexerConnection
        {
            IndexerId = indexer.Id,
            Name = "Connection1",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = indexer.Id,
            Name = "Connection2",
            Enabled = false,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(indexer.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var remainingConnections = await _context.IndexerConnections
            .Where(c => c.IndexerId == indexer.Id)
            .ToListAsync().ConfigureAwait(false);
        Assert.That(remainingConnections, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_SetsCategories_WhenCategoriesAreProvided()
    {
        // Arrange
        var categories = new[] { "type-a", "type-c", "type-d" }.ToList();
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "CategoriesIndexer",
            Availability = IndexerAvailability.Public,
            Categories = categories
        };

        // Act
        var result = await _subject.CreateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id).ConfigureAwait(false);
        Assert.That(retrieved!.Categories, Is.EqualTo(categories));
    }

    [Test]
    public async Task CreateAsync_SetsLanguages_WhenLanguagesAreProvided()
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
        var result = await _subject.CreateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id).ConfigureAwait(false);
        Assert.That(retrieved!.Languages, Is.EqualTo(languages));
    }

    [Test]
    public async Task UpdateAsync_UpdatesCategories_WhenCategoriesAreProvided()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "UpdateCategoriesIndexer",
            Availability = IndexerAvailability.Public,
            Categories = ["type-a"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var newCategories = new[] { "type-c", "type-d", "type-b" }.ToList();
        indexer.Categories = newCategories;

        // Act
        await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
        Assert.That(updated!.Categories, Is.EqualTo(newCategories));
    }

    [Test]
    public async Task UpdateAsync_UpdatesLanguages_WhenLanguagesAreProvided()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var newLanguages = new[] { "fr", "de", "es" }.ToList();
        indexer.Languages = newLanguages;

        // Act
        await _subject.UpdateAsync(indexer, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
        Assert.That(updated!.Languages, Is.EqualTo(newLanguages));
    }

    [Test]
    public async Task PatchAsync_ClearsDescription_WhenThePatchCarriesANullValue()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "DescribedIndexer",
            Description = "A described indexer",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var patched = await _subject
            .PatchAsync(new IndexerPatch { Id = indexer.Id, Description = new() { Value = null } }, _userId)
            .ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            Assert.That(updated!.Description, Is.Null);
            Assert.That(updated.Name, Is.EqualTo("DescribedIndexer"));
        }
    }

    [Test]
    public async Task PatchAsync_KeepsUnnamedFields_WhenThePatchNamesOneField()
    {
        // Arrange
        var indexer = new Indexer
        {
            ProviderType = "newznab",
            Name = "OriginalName",
            Description = "Original description",
            Availability = IndexerAvailability.Public,
            Categories = ["type-a"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(indexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _subject
            .PatchAsync(new IndexerPatch { Id = indexer.Id, Name = new() { Value = "RenamedIndexer" } }, _userId)
            .ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexer.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated!.Name, Is.EqualTo("RenamedIndexer"));
            Assert.That(updated.Description, Is.EqualTo("Original description"));
            Assert.That(updated.Categories, Is.EqualTo(["type-a"]));
        }
    }
}
