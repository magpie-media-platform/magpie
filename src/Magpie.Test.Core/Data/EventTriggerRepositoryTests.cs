using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Result;
using Magpie.Models.Triggers;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using EventTriggerEntity = Magpie.Data.Database.Entities.EventTrigger;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class EventTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IEventTriggerRepository _subject = null!;
    private Guid _userId;
    private Guid _jobTemplateId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
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
        _jobTemplateId = Guid.NewGuid();

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new EventTriggerRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<EventTriggerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsTrigger()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.Name))
                .EqualTo("TestTrigger")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.EventName))
                .EqualTo("content.added")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.Enabled))
                .True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsFilterAndParameters()
    {
        // Arrange
        var filter = new Dictionary<string, string?> { ["contentType"] = "movie" };
        var parameters = new Dictionary<string, string?> { ["key1"] = "value1", ["key2"] = "value2" };
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TriggerWithData",
            EventName = "content.added",
            Filter = JsonSerializer.Serialize(filter),
            Enabled = true,
            Parameters = JsonSerializer.Serialize(parameters),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        var success = result as SuccessResult<EventTrigger?>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Is.Not.Null);
            Assert.That(success.Value!.Filter, Is.Not.Null);
            Assert.That(success.Value.Filter!, Has.Count.EqualTo(1));
            Assert.That(success.Value.Filter!["contentType"], Is.EqualTo("movie"));
            Assert.That(success.Value.Parameters, Is.Not.Null);
            Assert.That(success.Value.Parameters!, Has.Count.EqualTo(2));
            Assert.That(success.Value.Parameters!["key1"], Is.EqualTo("value1"));
            Assert.That(success.Value.Parameters!["key2"], Is.EqualTo("value2"));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllTriggers()
    {
        // Arrange
        var entity1 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            EventName = "content.deleted",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<EventTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<EventTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsTrigger()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "UniqueTriggerName",
            EventName = "content.updated",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueTriggerName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.Name))
                .EqualTo("UniqueTriggerName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.JobTemplateId))
                .EqualTo(_jobTemplateId));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentTrigger");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsTriggers()
    {
        // Arrange
        var otherJobTemplateId = Guid.NewGuid();
        var entity1 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            EventName = "content.deleted",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new EventTriggerEntity
        {
            JobTemplateId = otherJobTemplateId,
            Name = "OtherTrigger",
            EventName = "content.updated",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByJobTemplateIdAsync(_jobTemplateId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<EventTrigger>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(EventTrigger.JobTemplateId)).EqualTo(_jobTemplateId));
        }
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByJobTemplateIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<EventTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledTriggers()
    {
        // Arrange
        var entity1 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "EnabledTrigger1",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "EnabledTrigger2",
            EventName = "content.deleted",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "DisabledTrigger",
            EventName = "content.updated",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<EventTrigger>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All.With.Property(nameof(EventTrigger.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledTriggers()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "DisabledTrigger",
            EventName = "content.added",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<EventTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByEventNameAsync_ReturnsEnabledTriggersForEvent()
    {
        // Arrange
        var entity1 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "DisabledTrigger",
            EventName = "content.added",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity4 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "OtherEventTrigger",
            EventName = "content.deleted",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.AddRange(entity1, entity2, entity3, entity4);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByEventNameAsync("content.added");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<EventTrigger>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(EventTrigger.EventName)).EqualTo("content.added")
                .And.Property(nameof(EventTrigger.Enabled)).True);
        }
    }

    [Test]
    public async Task GetByEventNameAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByEventNameAsync("nonexistent.event");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<EventTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger()
    {
        // Arrange
        var model = new EventTrigger(
            Guid.Empty,
            _jobTemplateId,
            "NewTrigger",
            "content.added",
            null,
            true,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "NewTrigger"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.Name))
                    .EqualTo("NewTrigger")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.EventName))
                    .EqualTo("content.added")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(EventTrigger.Enabled)).True);
            Assert.That(entity, Is.InstanceOf<EventTriggerEntity>()
                .With.Property(nameof(EventTriggerEntity.Name)).EqualTo("NewTrigger")
                .And.Property(nameof(EventTriggerEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTriggerWithFilterAndParameters()
    {
        // Arrange
        var filter = new Dictionary<string, string?> { ["contentType"] = "movie" }
            as IReadOnlyDictionary<string, string?>;
        var parameters = new Dictionary<string, string?> { ["param1"] = "value1", ["param2"] = null }
            as IReadOnlyDictionary<string, string?>;

        var model = new EventTrigger(
            Guid.Empty,
            _jobTemplateId,
            "TriggerWithData",
            "content.added",
            filter,
            true,
            parameters,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Name == "TriggerWithData"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<EventTrigger>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Filter, Is.Not.Null);
            Assert.That(entity.Parameters, Is.Not.Null);

            var deserializedFilter = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Filter!);
            Assert.That(deserializedFilter, Is.Not.Null);
            Assert.That(deserializedFilter!, Has.Count.EqualTo(1));
            Assert.That(deserializedFilter!["contentType"], Is.EqualTo("movie"));

            var deserializedParams = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Parameters!);
            Assert.That(deserializedParams, Is.Not.Null);
            Assert.That(deserializedParams!, Has.Count.EqualTo(2));
            Assert.That(deserializedParams!["param1"], Is.EqualTo("value1"));
            Assert.That(deserializedParams!["param2"], Is.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTrigger()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "OriginalName",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var newJobTemplateId = Guid.NewGuid();
        var model = new EventTrigger(
            entity.Id,
            newJobTemplateId,
            "UpdatedName",
            "content.deleted",
            null,
            false,
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from t in _context.EventTriggers.AsNoTracking()
            where t.Id == entity.Id
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<EventTriggerEntity>()
                .With.Property(nameof(EventTriggerEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(EventTriggerEntity.EventName)).EqualTo("content.deleted")
                .And.Property(nameof(EventTriggerEntity.Enabled)).False
                .And.Property(nameof(EventTriggerEntity.JobTemplateId)).EqualTo(newJobTemplateId)
                .And.Property(nameof(EventTriggerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(EventTriggerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new EventTrigger(
            Guid.NewGuid(),
            _jobTemplateId,
            "NonExistent",
            "content.added",
            null,
            true,
            null,
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
    public async Task UpdateAsync_UpdatesFilterAndParameters()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TriggerToUpdate",
            EventName = "content.added",
            Enabled = true,
            Filter = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "filter" }),
            Parameters = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "value" }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var newFilter = new Dictionary<string, string?> { ["new"] = "filter" }
            as IReadOnlyDictionary<string, string?>;
        var newParameters = new Dictionary<string, string?> { ["new"] = "newValue" }
            as IReadOnlyDictionary<string, string?>;

        var model = new EventTrigger(
            entity.Id,
            _jobTemplateId,
            entity.Name,
            entity.EventName,
            newFilter,
            entity.Enabled,
            newParameters,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);

            var deserializedFilter = JsonSerializer.Deserialize<Dictionary<string, string?>>(updatedEntity!.Filter!);
            Assert.That(deserializedFilter, Is.Not.Null);
            Assert.That(deserializedFilter!, Has.Count.EqualTo(1));
            Assert.That(deserializedFilter!["new"], Is.EqualTo("filter"));

            var deserializedParams = JsonSerializer.Deserialize<Dictionary<string, string?>>(updatedEntity.Parameters!);
            Assert.That(deserializedParams, Is.Not.Null);
            Assert.That(deserializedParams!, Has.Count.EqualTo(1));
            Assert.That(deserializedParams!["new"], Is.EqualTo("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "OriginalName",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<EventTriggerEntity>()
                .With.Property(nameof(EventTriggerEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(EventTriggerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(EventTriggerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEventName()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            EventName = new("content.deleted")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<EventTriggerEntity>()
                .With.Property(nameof(EventTriggerEntity.EventName)).EqualTo("content.deleted"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Enabled = new(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<EventTriggerEntity>()
                .With.Property(nameof(EventTriggerEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesFilter()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            EventName = "content.added",
            Enabled = true,
            Filter = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "filter" }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var newFilter = new Dictionary<string, string> { ["new"] = "filter" }
            as IReadOnlyDictionary<string, string>;

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Filter = new(newFilter)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);

            var deserializedFilter = JsonSerializer.Deserialize<Dictionary<string, string?>>(updatedEntity!.Filter!);
            Assert.That(deserializedFilter, Is.Not.Null);
            Assert.That(deserializedFilter!, Has.Count.EqualTo(1));
            Assert.That(deserializedFilter!["new"], Is.EqualTo("filter"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesParameters()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            EventName = "content.added",
            Enabled = true,
            Parameters = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "value" }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        var newParameters = new Dictionary<string, string> { ["new"] = "newValue" }
            as IReadOnlyDictionary<string, string>;

        var patch = new EventTriggerPatch
        {
            Id = entity.Id,
            Parameters = new(newParameters)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.EventTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);

            var deserializedParams = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                updatedEntity!.Parameters!);
            Assert.That(deserializedParams, Is.Not.Null);
            Assert.That(deserializedParams!, Has.Count.EqualTo(1));
            Assert.That(deserializedParams!["new"], Is.EqualTo("newValue"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new EventTriggerPatch
        {
            Id = Guid.NewGuid(),
            Name = new("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesTrigger()
    {
        // Arrange
        var entity = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "ToDelete",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.EventTriggers.AnyAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("EventTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }
}
