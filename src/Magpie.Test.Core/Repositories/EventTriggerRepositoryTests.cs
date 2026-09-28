using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
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
public sealed class EventTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private Mock<IEventBus> _eventBus = null!;
    private IEventTriggerRepository _subject = null!;
    private Guid _userId;
    private Guid _extensionId;
    private Guid _otherExtensionId;

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

        var extension = new Extension
        {
            PackageId = "Test.Assembly",
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/test/path",
            AssemblyName = "Test.Assembly",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var otherExtension = new Extension
        {
            PackageId = "Other.Assembly",
            Name = "OtherExtension",
            Version = "1.0.0",
            FilePath = "/other/path",
            AssemblyName = "Other.Assembly",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.AddRange(extension, otherExtension);
        _context.SaveChanges();

        _extensionId = extension.Id;
        _otherExtensionId = otherExtension.Id;

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRepo = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _eventBus = new Mock<IEventBus>();
        _eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _subject = new EventTriggerRepository(
            _context,
            auditRepo,
            transactionManager,
            _eventBus.Object,
            loggerFactory.CreateLogger<EventTriggerRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListByCreatedByUserIdAsync_ReturnsOnlyTriggersOwnedByUser_WhenUserOwnsTriggers()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        _context.EventTriggers.AddRange(
            new EventTrigger
            {
                JobType = "Job",
                Scope = JobScope.User,
                Name = "Mine",
                EventFilter = new(),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = _userId,
            },
            new EventTrigger
            {
                JobType = "Job",
                Scope = JobScope.User,
                Name = "Theirs",
                EventFilter = new(),
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = otherUserId,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { CreatedByUserId = _userId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result, Has.One.With.Property(nameof(EventTrigger.Name)).EqualTo("Mine"));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsTrigger_WhenTriggerExists()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TestTrigger",
            EventFilter = new()
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(entity.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestTrigger"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsParameters_WhenTriggerHasParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string> { ["key1"] = "value1", ["key2"] = "value2" };
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TriggerWithData",
            EventFilter = new()
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            Parameters = parameters,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(entity.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Parameters)).Not.Null);
            Assert.That(result, Has.Property(nameof(result.Parameters)).With.Count.EqualTo(2));
            Assert.That(result, Has.Property(nameof(result.Parameters)).ContainKey("key1").WithValue("value1"));
            Assert.That(result, Has.Property(nameof(result.Parameters)).ContainKey("key2").WithValue("value2"));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllTriggers_WhenTriggersExist()
    {
        // Arrange
        var entity1 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "Trigger1",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "Trigger2",
            EventFilter = new()
            {
                EventName = new() { Value = "content.deleted" },
            },
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null.With.Count.EqualTo(2));
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByExtensionIdAsync_ReturnsTriggers_WhenExtensionHasTriggers()
    {
        // Arrange
        var entity1 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "Trigger1",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" },
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "Trigger2",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.deleted" },
            },
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new EventTrigger
        {
            ExtensionId = _otherExtensionId,
            JobType = "OtherJob",
            Scope = JobScope.Content,
            Name = "OtherTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.updated" },
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2, entity3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { ExtensionId = _extensionId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false))
            .Items
            .ToList();

        // Assert
        var resultList = result.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultList, Has.Count.EqualTo(2));
            Assert.That(resultList, Has.All.With.Property(nameof(EventTrigger.ExtensionId)).EqualTo(_extensionId));
        }
    }

    [Test]
    public async Task ListByExtensionIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = (await _subject.QueryAsync(new() { ExtensionId = Guid.NewGuid() },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false)).Items.ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListEnabledAsync_ReturnsEnabledTriggers_WhenEnabledTriggersExist()
    {
        // Arrange
        var entity1 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "EnabledTrigger1",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var entity2 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "EnabledTrigger2",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.deleted" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var entity3 = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "DisabledTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.updated" }
            },
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2, entity3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Enabled = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        var resultList = result.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resultList, Has.Count.EqualTo(2));
            Assert.That(resultList, Has.All.With.Property(nameof(EventTrigger.Enabled)).True);
        }
    }

    [Test]
    public async Task ListEnabledAsync_ReturnsEmptyList_WhenNoEnabledTriggers()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "DisabledTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Enabled = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger_WhenRequestIsValid()
    {
        // Arrange
        var model = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "NewJob",
            Scope = JobScope.Content,
            Name = "NewTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "NewTrigger"
            select t).SingleOrDefaultAsync().ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewTrigger"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("NewTrigger"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTriggerWithParameters_WhenParametersAreProvided()
    {
        // Arrange
        var parameters = new Dictionary<string, string> { ["param1"] = "value1", ["param2"] = "value2" };

        var model = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "NewJob",
            Scope = JobScope.Content,
            Name = "TriggerWithData",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            Parameters = parameters
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "TriggerWithData"
            select t).SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.Count.EqualTo(2));
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.ItemAt("param1").EqualTo("value1"));
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.ItemAt("param2").EqualTo("value2"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTrigger_WhenTriggerExists()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "OriginalName",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new EventTrigger
        {
            Id = entity.Id,
            ExtensionId = _otherExtensionId,
            JobType = "UpdatedJob",
            Scope = JobScope.Administrative,
            Name = "UpdatedName",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.deleted" }
            },
            Enabled = false,
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Id == entity.Id
            select t).SingleOrDefaultAsync().ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Enabled)).False);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ExtensionId)).EqualTo(_otherExtensionId));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Update")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new EventTrigger
        {
            Id = Guid.NewGuid(),
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "NonExistent",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_UpdatesParameters_WhenParametersAreProvided()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TriggerToUpdate",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            Parameters = new() { ["old"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var newParameters = new Dictionary<string, string> { ["new"] = "newValue" };

        var model = new EventTrigger
        {
            Id = entity.Id,
            ExtensionId = _extensionId,
            JobType = entity.JobType,
            Scope = entity.Scope,
            Name = entity.Name,
            EventFilter = entity.EventFilter,
            Enabled = entity.Enabled,
            Parameters = newParameters,
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.Count.EqualTo(1));
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.Parameters))
                    .With.ItemAt("new").EqualTo("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName_WhenNameIsSet()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "OriginalName",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Name = new() { Value = "PatchedName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("PatchedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled_WhenEnabledIsSet()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TestTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Enabled = new() { Value = false }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesParameters_WhenParametersAreSet()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TestTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            Parameters = new() { ["old"] = "value", ["keep"] = "this" },
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Parameters = new()
            {
                Add = new() { ["new"] = "newValue", ["old"] = "re-added" },
                Remove = ["old"],
                Update = new() { ["keep"] = "updated", ["old"] = "updated-old", ["absent"] = "ignored" }
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.Count.EqualTo(2));
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.Parameters)).With.ItemAt("new")
                    .EqualTo("newValue"));
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.Parameters)).With.ItemAt("keep")
                    .EqualTo("updated"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.Not.ContainKey("old"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new EventTriggerPatch
        {
            Id = Guid.NewGuid(),
            Name = new() { Value = "NewName" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesTrigger_WhenTriggerExists()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "ToDelete",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId).ConfigureAwait(false);

        // Assert
        var entityExists = await _context.EventTriggers.AnyAsync(t => t.Id == entity.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateAsync_PersistsDelaySeconds_WhenDelayIsProvided()
    {
        // Arrange
        var model = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "NewJob",
            Scope = JobScope.Content,
            Name = "DelayedTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            DelaySeconds = 15
        };

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.EventTriggers
            .AsNoTracking()
            .SingleAsync(t => t.Name == "DelayedTrigger").ConfigureAwait(false);
        Assert.That(entity, Has.Property(nameof(entity.DelaySeconds)).EqualTo(15));
    }

    [Test]
    public void CreateAsync_Throws_WhenDelaySecondsNegative()
    {
        // Arrange
        var model = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "NewJob",
            Scope = JobScope.Content,
            Name = "NegativeDelay",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            DelaySeconds = -1
        };

        // Assert
        Assert.That(
            async () => await _subject.CreateAsync(model, _userId).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>());
    }

    [Test]
    public async Task PatchAsync_PatchesDelaySeconds_WhenDelayIsSet()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TestTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            DelaySeconds = 0,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            DelaySeconds = new() { Value = 30 }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.DelaySeconds)).EqualTo(30));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                    .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_Throws_WhenDelaySecondsNegative()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TestTrigger",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            DelaySeconds = new() { Value = -5 }
        };

        // Assert
        Assert.That(
            async () => await _subject.PatchAsync(patch, _userId).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>());
    }

    [Test]
    public async Task UpdateAsync_UpdatesDelaySeconds_WhenDelayIsProvided()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "TriggerToUpdate",
            EventFilter = new EventFilter
            {
                EventName = new() { Value = "content.added" }
            },
            Enabled = true,
            DelaySeconds = 0,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new EventTrigger
        {
            Id = entity.Id,
            ExtensionId = entity.ExtensionId,
            JobType = entity.JobType,
            Scope = entity.Scope,
            Name = entity.Name,
            EventFilter = entity.EventFilter,
            Enabled = entity.Enabled,
            DelaySeconds = 45,
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.DelaySeconds)).EqualTo(45));
        }
    }

    [Test]
    public async Task UpdateAsync_PublishesUpdatedEvent_WhenTriggerExists()
    {
        // Arrange
        var entity = new EventTrigger
        {
            ExtensionId = _extensionId,
            JobType = "TestJob",
            Scope = JobScope.Content,
            Name = "OriginalName",
            EventFilter = new EventFilter { EventName = new() { Value = "content.added" } },
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.UpdateAsync(
            new EventTrigger
            {
                Id = entity.Id,
                ExtensionId = _extensionId,
                JobType = "TestJob",
                Scope = JobScope.Content,
                Name = "OriginalName",
                EventFilter = new EventFilter { EventName = new() { Value = "content.deleted" } },
                Enabled = false,
                CreatedAt = entity.CreatedAt
            },
            _userId).ConfigureAwait(false);

        // Assert
        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent => magpieEvent.Name == "eventTrigger.updated"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
