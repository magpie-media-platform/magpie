using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class IndexerConnectionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private Mock<IEventBus> _eventBus = null!;
    private IIndexerConnectionRepository _subject = null!;
    private IIndexerRepository _indexers = null!;
    private Guid _userId;
    private Guid _indexerId;

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

        var indexer = new Indexer
        {
            Name = "TestIndexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.Indexers.Add(indexer);
        _context.SaveChanges();

        _userId = user.Id;
        _indexerId = indexer.Id;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        var protector = new Mock<IDataProtectionService>();
        protector
            .Setup(p => p.Protect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> settings) => new Dictionary<string, string>(settings));
        protector
            .Setup(p => p.Unprotect(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((IReadOnlyDictionary<string, string> stored) => new Dictionary<string, string>(stored));

        _subject = new IndexerConnectionRepository(
            _context,
            NullLogger<IndexerConnectionRepository>.Instance,
            protector.Object);

        _eventBus = new Mock<IEventBus>();
        _indexers = new IndexerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _eventBus.Object,
            NullLogger<IndexerRepository>.Instance,
            protector.Object,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsIndexerConnection_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestConnection"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(IntegrationHealthStatus.Healthy));
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
    public async Task PatchAsync_PersistsStatusAndLastChecked_WhenHealthFieldsAreSet()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ProbeConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var checkedAt = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var updated = await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Status = new() { Value = IntegrationHealthStatus.Healthy },
                LastChecked = new() { Value = checkedAt }
            },
            null).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(reloaded, Has.Property(nameof(reloaded.Status)).EqualTo(IntegrationHealthStatus.Healthy));
            Assert.That(reloaded, Has.Property(nameof(reloaded.LastChecked)).EqualTo(checkedAt));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsUtcKindTimestamps_WhenRoundTrippedThroughSqlite()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "UtcConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var checkedAt = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);
        await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Status = new() { Value = IntegrationHealthStatus.Healthy },
                LastChecked = new() { Value = checkedAt }
            },
            null).ConfigureAwait(false);

        _context.ChangeTracker.Clear();

        // Act
        var reloaded = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded!.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(reloaded.LastChecked!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenConnectionDoesNotExist()
    {
        // Act
        var updated = await _indexers.PatchConnectionAsync(
            new()
            {
                Id = Guid.NewGuid(),
                Status = new() { Value = IntegrationHealthStatus.Healthy },
                LastChecked = new() { Value = DateTime.UtcNow }
            },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(updated, Is.False);
    }

    [Test]
    public async Task PatchAsync_WritesAndClearsLastError_WhenPatched()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ErrorConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var checkedAt = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

        // Act
        await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Status = new() { Value = IntegrationHealthStatus.Unhealthy },
                LastChecked = new() { Value = checkedAt },
                LastError = new() { Value = "Cloudflare challenge" }
            },
            null).ConfigureAwait(false);
        var afterError = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        // Act
        await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Status = new() { Value = IntegrationHealthStatus.Healthy },
                LastChecked = new() { Value = checkedAt },
                LastError = new() { Value = null }
            },
            null).ConfigureAwait(false);
        var afterClear = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterError, Has.Property(nameof(afterError.Status)).EqualTo(IntegrationHealthStatus.Unhealthy));
            Assert.That(afterError, Has.Property(nameof(afterError.LastError)).EqualTo("Cloudflare challenge"));
            Assert.That(afterClear, Has.Property(nameof(afterClear.Status)).EqualTo(IntegrationHealthStatus.Healthy));
            Assert.That(afterClear, Has.Property(nameof(afterClear.LastError)).Null);
        }
    }

    [Test]
    public async Task PatchAsync_TruncatesLastError_WhenTheErrorExceedsColumnLength()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "LongErrorConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var longMessage = new string('x', 2000);

        // Act
        await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Status = new() { Value = IntegrationHealthStatus.Unhealthy },
                LastChecked = new() { Value = DateTime.UtcNow },
                LastError = new() { Value = longMessage }
            },
            null).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);
        Assert.That(reloaded!.LastError, Has.Length.EqualTo(1024));
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllIndexerConnections_WhenConnectionsExist()
    {
        // Arrange
        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection1",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection2",
            Enabled = false,
            Status = IntegrationHealthStatus.Unhealthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexerConnections()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByIndexerIdAsync_ReturnsConnectionsForIndexer_WhenIndexerHasConnections()
    {
        // Arrange
        var otherIndexer = new Indexer
        {
            Name = "OtherIndexer",
            ProviderType = "torznab",
            Availability = IndexerAvailability.Private,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(otherIndexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection1",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection2",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection3 = new IndexerConnection
        {
            IndexerId = otherIndexer.Id,
            Name = "Connection3",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2, connection3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { IndexerId = _indexerId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<IndexerConnection>(c => c.IndexerId == _indexerId));
        }
    }

    [Test]
    public async Task ListByIndexerIdAsync_ReturnsEmptyList_WhenNoConnectionsForIndexer()
    {
        // Act
        var result = (await _subject.QueryAsync(new() { IndexerId = _indexerId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByTagAsync_ReturnsConnectionsWithTag_WhenConnectionsShareTag()
    {
        // Arrange
        var tag = new Tag { Name = "TestTag" };
        _context.Tags.Add(tag);

        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TaggedConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "UntaggedConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        tag.IndexerConnections.Add(connection1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Tag = "TestTag" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result, Has.One.With.Property(nameof(IndexerConnection.Name)).EqualTo("TaggedConnection"));
        }
    }

    [Test]
    public async Task ListByTagAsync_ReturnsEmptyList_WhenTagDoesNotExist()
    {
        // Act
        var result = (await _subject.QueryAsync(new() { Tag = "NonExistentTag" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerConnection_WhenRequestIsValid()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "NewConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            Settings = new() { ["apiKey"] = "secret" }
        };

        // Act
        var result = await _indexers.CreateConnectionAsync(connection, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewConnection"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.IndexerId)).EqualTo(_indexerId));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenConnectionIsCreated()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "AuditedConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy
        };

        // Act
        await _indexers.CreateConnectionAsync(connection, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("IndexerConnection"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [TestCase("create")]
    [TestCase("patch")]
    [TestCase("delete")]
    public async Task IndexerConnectionWrite_PublishesIndexerUpdatedOnceAfterCommit_WhenAConnectionChanges(
        string write)
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Evented",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        if (write != "create")
        {
            _context.IndexerConnections.Add(connection);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.ChangeTracker.Clear();
        }

        var publishedInsideTransaction = false;
        _eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => publishedInsideTransaction |= _context.Database.CurrentTransaction is not null)
            .Returns(Task.CompletedTask);

        // Act
        Task operation = write switch
        {
            "create" => _indexers.CreateConnectionAsync(connection, _userId),
            "patch" => _indexers.PatchConnectionAsync(
                new() { Id = connection.Id, Enabled = new() { Value = false } },
                _userId),
            _ => _indexers.DeleteConnectionAsync(connection.Id, _userId),
        };
        await operation.ConfigureAwait(false);

        // Assert
        Assert.That(publishedInsideTransaction, Is.False);
        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(e => e.Name == "indexer.updated" &&
                    e.Context["indexer.id"] == _indexerId.ToString() &&
                    e.Context["indexer.changedFields"] == "Connections"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task PatchConnectionAsync_PublishesIndexerUpdatedOnlyForConfiguration_WhenAProbeWritesHealth(
        bool rename,
        int expectedEvents)
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Probed",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var patched = await _indexers.PatchConnectionAsync(
            new()
            {
                Id = connection.Id,
                Name = new() { Value = rename ? "Renamed" : "Probed" },
                Status = new() { Value = IntegrationHealthStatus.Unhealthy },
                LastChecked = new() { Value = DateTime.UtcNow },
                LastError = new() { Value = "Timed out" },
                ConsecutiveFailures = new() { Value = 1 }
            },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(patched, Is.True);
        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(e => e.Name == "indexer.updated"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedEvents));
    }

    [Test]
    public async Task DeleteAsync_OfTheIndexer_DeletesAndAuditsConnectionsWithOnlyTheDeletedEvent()
    {
        // Arrange
        _context.IndexerConnections.Add(new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Removed with the indexer",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var deleted = await _indexers.DeleteAsync(_indexerId, _userId).ConfigureAwait(false);

        // Assert
        var connections = await _context.IndexerConnections.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var audits = await _context.AuditRecords.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(connections, Is.Empty);
            Assert.That(
                audits,
                Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(IndexerConnection)));
        }

        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(e => e.Name == "indexer.deleted"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _eventBus.Verify(
            bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DeleteAsync_DeletesIndexerConnection_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ToDelete",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _indexers.DeleteConnectionAsync(connection.Id, _userId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _indexers.DeleteConnectionAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenConnectionIsDeleted()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "DeleteAudit",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _indexers.DeleteConnectionAsync(connection.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("IndexerConnection"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "OriginalName",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesEnabled_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Enabled = new() { Value = false }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_AddsSettings_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new() { ["existing"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Settings = new()
            {
                Add = new() { ["newKey"] = "newValue" }
            }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("existing"));
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("newKey").WithValue("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_RemovesSettings_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new()
            {
                ["keepKey"] = "keepValue",
                ["removeKey"] = "removeValue"
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Settings = new()
            {
                Remove = ["removeKey"]
            }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("keepKey"));
            Assert.That(patched, Has.Property(nameof(patched.Settings)).Not.ContainKey("removeKey"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSettings_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            Settings = new() { ["updateKey"] = "oldValue" },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Settings = new()
            {
                Update = new() { ["updateKey"] = "newValue" }
            }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched, Has.Property(nameof(patched.Settings)).ContainKey("updateKey").WithValue("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_AddsTags_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Tags = new() { Add = ["NewTag"] }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tags = await _context.Tags
            .Include(t => t.IndexerConnections)
            .Where(t => t.Name == "NewTag")
            .ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tags, Has.Exactly(1).Items);
            Assert.That(tags, Has.One.With.Property(nameof(Tag.IndexerConnections)).With.Exactly(1).Items);
        }
    }

    [Test]
    public async Task PatchAsync_RemovesTags_WhenExists()
    {
        // Arrange
        var tag = new Tag { Name = "ExistingTag" };
        _context.Tags.Add(tag);

        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        tag.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Tags = new() { Remove = ["ExistingTag"] }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var tagAfter = await _context.Tags
            .Include(t => t.IndexerConnections)
            .FirstOrDefaultAsync(t => t.Name == "ExistingTag").ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(tagAfter!.IndexerConnections, Is.Empty);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new IndexerConnectionPatch
        {
            Id = Guid.NewGuid(),
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenConnectionIsPatched()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ToPatch",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Name = new() { Value = "Patched" }
        };

        // Act
        await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
                    .EqualTo("IndexerConnection"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordForAddedTag_WhenTagIsAdded()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Tags = new() { Add = ["AuditTag"] }
        };

        // Act
        await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordForRemovedTag_WhenTagIsRemoved()
    {
        // Arrange
        var tag = new Tag { Name = "RemoveAuditTag" };
        _context.Tags.Add(tag);

        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        tag.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new IndexerConnectionPatch
        {
            Id = connection.Id,
            Tags = new() { Remove = ["RemoveAuditTag"] }
        };

        // Act
        await _indexers.PatchConnectionAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("RemoveTag"));
        }
    }
}
