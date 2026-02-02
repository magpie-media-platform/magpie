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
public class EventTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IEventTriggerRepository _subject = null!;
    private Guid _userId;
    private Guid _extensionId;
    private Guid _otherExtensionId;

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

        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/test/path",
            AssemblyName = "Test.Assembly",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var otherExtension = new Extension
        {
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

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRepo = new AuditRecordRepository(_context, loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);

        _subject = new EventTriggerRepository(
            _context,
            auditRepo,
            transactionManager,
            loggerFactory.CreateLogger<EventTriggerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsTrigger()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsParameters()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

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
    public async Task ListAllAsync_ReturnsAllTriggers()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Not.Null.With.Count.EqualTo(2));
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListByExtensionIdAsync_ReturnsTriggers()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.ListByExtensionIdAsync(_extensionId);

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
        var result = await _subject.ListByExtensionIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListEnabledAsync_ReturnsEnabledTriggers()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.ListEnabledAsync();

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
        _context.SaveChanges();

        // Act
        var result = await _subject.ListEnabledAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger()
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
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "NewTrigger"
            select t).SingleOrDefaultAsync();
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewTrigger"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("NewTrigger"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(auditRecords, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTriggerWithParameters()
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
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true,
            Parameters = parameters
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "TriggerWithData"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.Count.EqualTo(2));
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.ItemAt("param1").EqualTo("value1"));
            Assert.That(entity, Has.Property(nameof(entity.Parameters)).With.ItemAt("param2").EqualTo("value2"));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTrigger()
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
        _context.SaveChanges();

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
            ExecutionMode = JobExecutionMode.Immediate,
            Enabled = false,
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Id == entity.Id
            select t).SingleOrDefaultAsync();
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Enabled)).False);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ExtensionId)).EqualTo(_otherExtensionId));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(auditRecords, Has.One
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
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_UpdatesParameters()
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
            Parameters = new Dictionary<string, string> { ["old"] = "value" },
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var newParameters = new Dictionary<string, string> { ["new"] = "newValue" };

        var model = new EventTrigger
        {
            Id = entity.Id,
            ExtensionId = _extensionId,
            JobType = entity.JobType,
            Scope = entity.Scope,
            Name = entity.Name,
            EventFilter = entity.EventFilter,
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = entity.Enabled,
            Parameters = newParameters,
            CreatedAt = entity.CreatedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters))
                .With.Count.EqualTo(1));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters))
                .With.ItemAt("new").EqualTo("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
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
        _context.SaveChanges();

        var patch = new EventTriggerPatch(
            entity.Id,
            Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("PatchedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(auditRecords, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
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
        _context.SaveChanges();

        var patch = new EventTriggerPatch(
            entity.Id,
            Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesParameters()
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
            Parameters = new Dictionary<string, string> { ["old"] = "value", ["keep"] = "this" },
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new EventTriggerPatch(
            entity.Id,
            Parameters: new DictionaryPatch<string, string, string>(
                Add: new Dictionary<string, string> { ["new"] = "newValue" },
                Remove: ["old"],
                Update: new Dictionary<string, string> { ["keep"] = "updated" }));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.Count.EqualTo(2));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.ItemAt("new")
                .EqualTo("newValue"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.ItemAt("keep")
                .EqualTo("updated"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Parameters)).With.Not.ContainKey("old"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new EventTriggerPatch(
            Guid.NewGuid(),
            Name: new ScalarPatch<string>("NewName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesTrigger()
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
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.EventTriggers.AnyAsync(t => t.Id == entity.Id);
        var auditRecords = await _context.AuditRecords.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
            Assert.That(auditRecords, Has.One
                .With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete")
                .And.Property(nameof(AuditRecord.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }
}
