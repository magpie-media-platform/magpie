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
public class IndexerFeedRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerFeedRepository _subject = null!;
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

        _subject = new IndexerFeedRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<IndexerFeedRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIndexerFeed_WhenExists()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "TestFeed",
            Description = "Test description",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(indexerFeed.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestFeed"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("rss"));
            Assert.That(result, Has.Property(nameof(result.Description)).EqualTo("Test description"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
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
    public async Task ListAllAsync_ReturnsAllIndexerFeeds()
    {
        // Arrange
        var feed1 = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "Feed1",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var feed2 = new IndexerFeed
        {
            ProviderType = "json",
            Name = "Feed2",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.AddRange(feed1, feed2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexerFeeds()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerFeed()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "NewFeed",
            Description = "New feed description",
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(indexerFeed, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewFeed"));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("rss"));
            Assert.That(result, Has.Property(nameof(result.Description)).EqualTo("New feed description"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "AuditedFeed",
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(indexerFeed, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_WithSettings_PersistsSettings()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "FeedWithSettings",
            Enabled = true,
            Settings = { ["url"] = "https://example.com/feed", ["interval"] = "60" }
        };

        // Act
        var result = await _subject.CreateAsync(indexerFeed, _userId);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved, Has.Property(nameof(retrieved.Settings)).With.Count.EqualTo(2));
            Assert.That(retrieved, Has.Property(nameof(retrieved.Settings))
                .ContainKey("url").WithValue("https://example.com/feed"));
            Assert.That(retrieved, Has.Property(nameof(retrieved.Settings))
                .ContainKey("interval").WithValue("60"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesIndexerFeed_WhenExists()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "OriginalFeed",
            Description = "Original description",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        indexerFeed.Name = "UpdatedFeed";
        indexerFeed.Description = "Updated description";
        indexerFeed.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(indexerFeed, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(indexerFeed.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedFeed"));
            Assert.That(updated, Has.Property(nameof(updated.Description)).EqualTo("Updated description"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "rss",
            Name = "NonExistent",
            Enabled = true
        };

        // Act
        var result = await _subject.UpdateAsync(indexerFeed, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "ToUpdate",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        indexerFeed.Name = "Updated";

        // Act
        await _subject.UpdateAsync(indexerFeed, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesLastSync()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "SyncFeed",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        var syncTime = DateTime.UtcNow;
        indexerFeed.LastSync = syncTime;

        // Act
        await _subject.UpdateAsync(indexerFeed, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(indexerFeed.Id);
        Assert.That(updated, Has.Property(nameof(updated.LastSync)).EqualTo(syncTime));
    }

    [Test]
    public async Task DeleteAsync_DeletesIndexerFeed_WhenExists()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "ToDelete",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(indexerFeed.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(indexerFeed.Id);
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
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "DeleteAudit",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(indexerFeed.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
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
}
