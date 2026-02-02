using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class IndexerConnectionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IIndexerConnectionRepository _subject = null!;
    private Guid _userId;
    private Guid _indexerId;

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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new IndexerConnectionRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<IndexerConnectionRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(connection.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestConnection"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(IndexerStatus.Healthy));
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
    public async Task ListAllAsync_ReturnsAllIndexerConnections()
    {
        // Arrange
        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection1",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection2",
            Enabled = false,
            Status = IndexerStatus.Unhealthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoIndexerConnections()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByIndexerIdAsync_ReturnsConnectionsForIndexer()
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
        await _context.SaveChangesAsync();

        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection1",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "Connection2",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection3 = new IndexerConnection
        {
            IndexerId = otherIndexer.Id,
            Name = "Connection3",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2, connection3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByIndexerIdAsync(_indexerId);

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
        var result = await _subject.ListByIndexerIdAsync(_indexerId);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByTagAsync_ReturnsConnectionsWithTag()
    {
        // Arrange
        var tag = new Tag { Name = "TestTag" };
        _context.Tags.Add(tag);

        var connection1 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TaggedConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        var connection2 = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "UntaggedConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.AddRange(connection1, connection2);
        await _context.SaveChangesAsync();

        tag.IndexerConnections.Add(connection1);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListByTagAsync("TestTag");

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
        var result = await _subject.ListByTagAsync("NonExistentTag");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesIndexerConnection()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "NewConnection",
            Enabled = true,
            Status = IndexerStatus.Unknown,
            Settings = new Dictionary<string, string> { ["apiKey"] = "secret" }
        };

        // Act
        var result = await _subject.CreateAsync(connection, _userId);

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
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "AuditedConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy
        };

        // Act
        await _subject.CreateAsync(connection, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("IndexerConnection"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesIndexerConnection_WhenExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "OriginalConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        connection.Name = "UpdatedConnection";
        connection.Enabled = false;
        connection.Status = IndexerStatus.Unhealthy;

        // Act
        var result = await _subject.UpdateAsync(connection, _userId);

        // Assert
        var updated = await _subject.GetByIdAsync(connection.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedConnection"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.Status)).EqualTo(IndexerStatus.Unhealthy));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            Id = Guid.NewGuid(),
            IndexerId = _indexerId,
            Name = "NonExistent",
            Enabled = true,
            Status = IndexerStatus.Healthy
        };

        // Act
        var result = await _subject.UpdateAsync(connection, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ToUpdate",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        connection.Name = "Updated";

        // Act
        await _subject.UpdateAsync(connection, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("IndexerConnection"));
        }
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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(connection.Id, _userId);

        // Assert
        var deleted = await _subject.GetByIdAsync(connection.Id);
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
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "DeleteAudit",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(connection.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id);

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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id);

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
            Status = IndexerStatus.Healthy,
            Settings = new Dictionary<string, string> { ["existing"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: new Dictionary<string, string?> { ["newKey"] = "newValue" },
                Remove: null,
                Update: null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id);

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
            Status = IndexerStatus.Healthy,
            Settings = new Dictionary<string, string>
            {
                ["keepKey"] = "keepValue",
                ["removeKey"] = "removeValue"
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: null,
                Remove: ["removeKey"],
                Update: null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id);

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
            Status = IndexerStatus.Healthy,
            Settings = new Dictionary<string, string> { ["updateKey"] = "oldValue" },
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Settings: new DictionaryPatch<string, string?, string?>(
                Add: null,
                Remove: null,
                Update: new Dictionary<string, string?> { ["updateKey"] = "newValue" }));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var patched = await _subject.GetByIdAsync(connection.Id);

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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Tags: new SetPatch<string, string>(Add: ["NewTag"]));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tags = await _context.Tags
            .Include(t => t.IndexerConnections)
            .Where(t => t.Name == "NewTag")
            .ToListAsync();

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
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        tag.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Tags: new SetPatch<string, string>(Remove: ["ExistingTag"]));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagAfter = await _context.Tags
            .Include(t => t.IndexerConnections)
            .FirstOrDefaultAsync(t => t.Name == "ExistingTag");

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
        var patch = new IndexerConnectionPatch(
            Guid.NewGuid(),
            Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "ToPatch",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Name: new ScalarPatch<string>("Patched"));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("IndexerConnection"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordForAddedTag()
    {
        // Arrange
        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Tags: new SetPatch<string, string>(Add: ["AuditTag"]));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("AddTag"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecordForRemovedTag()
    {
        // Arrange
        var tag = new Tag { Name = "RemoveAuditTag" };
        _context.Tags.Add(tag);

        var connection = new IndexerConnection
        {
            IndexerId = _indexerId,
            Name = "TestConnection",
            Enabled = true,
            Status = IndexerStatus.Healthy,
            CreatedAt = DateTime.UtcNow
        };
        _context.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        tag.IndexerConnections.Add(connection);
        await _context.SaveChangesAsync();

        var patch = new IndexerConnectionPatch(
            connection.Id,
            Tags: new SetPatch<string, string>(Remove: ["RemoveAuditTag"]));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(2));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("RemoveTag"));
        }
    }
}
