using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class IndexerFeedRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerFeedRepository _subject = null!;
    private ITransactionManager _transactionManager = null!;
    private Mock<IEventBus> _eventBus = null!;
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

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _eventBus = new Mock<IEventBus>();

        var protector = new Mock<IDataProtectionService>();
        protector
            .Setup(p => p.Protect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> settings) => new Dictionary<string, string>(settings));
        protector
            .Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> stored) => new Dictionary<string, string>(stored));

        _subject = new IndexerFeedRepository(
            _context,
            auditRecordRepository,
            _transactionManager,
            _eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<IndexerFeedRepository>(),
            protector.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllIndexerFeeds_WhenFeedsExist()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexerFeeds()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerFeed_WhenRequestIsValid()
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
        var result = await _subject.CreateAsync(indexerFeed, _userId).ConfigureAwait(false);

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
    public async Task CreateAsync_CreatesAuditRecord_WhenFeedIsCreated()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "AuditedFeed",
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_PersistsSettings_WhenSettingsIsProvided()
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
        var result = await _subject.CreateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        var retrieved = await _subject.GetByIdAsync(result.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retrieved, Has.Property(nameof(retrieved.Settings)).With.Count.EqualTo(2));
            Assert.That(
                retrieved,
                Has.Property(nameof(retrieved.Settings))
                    .ContainKey("url").WithValue("https://example.com/feed"));
            Assert.That(retrieved, Has.Property(nameof(retrieved.Settings)).ContainKey("interval").WithValue("60"));
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        indexerFeed.Name = "UpdatedFeed";
        indexerFeed.Description = "Updated description";
        indexerFeed.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
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
        var result = await _subject.UpdateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenFeedIsUpdated()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        indexerFeed.Name = "Updated";

        // Act
        await _subject.UpdateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesLastSync_WhenTimestampIsProvided()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var syncTime = DateTime.UtcNow;
        indexerFeed.LastSync = syncTime;

        // Act
        await _subject.UpdateAsync(indexerFeed, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(indexerFeed.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
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
    public async Task DeleteAsync_CreatesAuditRecord_WhenFeedIsDeleted()
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(indexerFeed.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("IndexerFeed"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_PublishesDeletedEvent_WhenTheDeleteCommits()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "PublishedDelete",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async token =>
        {
            await _subject.DeleteAsync(indexerFeed.Id, _userId, token).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.Null);
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(published => published.Name == "indexerFeed.deleted"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task DeleteAsync_DoesNotPublishDeletedEvent_WhenTheOuterTransactionRollsBack()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "rss",
            Name = "RolledBackDelete",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(() => _transactionManager.ExecuteWithTransactionAsync(
            async token =>
            {
                await _subject.DeleteAsync(indexerFeed.Id, _userId, token).ConfigureAwait(false);
                throw new InvalidOperationException("The later step failed.");
            }));

        // Assert
        var survivor = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(survivor, Is.Not.Null);
            _eventBus.Verify(
                bus => bus.PublishAsync(
                    It.Is<MagpieEvent>(published => published.Name == "indexerFeed.deleted"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsStoredSettings_WhenThePatchAddsOneKey()
    {
        // Arrange
        var indexerFeed = new IndexerFeed
        {
            ProviderType = "cardigann",
            Name = "Cardigann",
            Description = "A feed",
            Enabled = true,
            Settings = new() { ["url"] = "https://feeds.example/definitions", ["apiKey"] = "stored-secret" },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerFeeds.Add(indexerFeed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var patched = await _subject
            .PatchAsync(
                new IndexerFeedPatch
                {
                    Id = indexerFeed.Id,
                    Settings = new() { Add = new() { ["url"] = "https://feeds.example/other" } },
                    Description = new() { Value = null }
                },
                _userId)
            .ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(indexerFeed.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            Assert.That(updated!.Settings["url"], Is.EqualTo("https://feeds.example/other"));
            Assert.That(updated.Settings["apiKey"], Is.EqualTo("stored-secret"));
            Assert.That(updated.Description, Is.Null);
            Assert.That(updated.Name, Is.EqualTo("Cardigann"));
        }
    }
}
