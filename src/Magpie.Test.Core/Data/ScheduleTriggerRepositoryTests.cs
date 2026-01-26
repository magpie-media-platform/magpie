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

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using ScheduleTriggerEntity = Magpie.Data.Database.Entities.ScheduleTrigger;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ScheduleTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IScheduleTriggerRepository _subject = null!;
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

        _subject = new ScheduleTriggerRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<ScheduleTriggerRepository>());
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
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Name)).EqualTo("TestTrigger")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Schedule)).EqualTo("0 * * * *")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Enabled)).True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string?> { ["key1"] = "value1", ["key2"] = "value2" };
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TriggerWithParams",
            Schedule = "0 0 * * *",
            Enabled = true,
            Parameters = JsonSerializer.Serialize(parameters),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        var success = result as SuccessResult<ScheduleTrigger?>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Is.Not.Null);
            Assert.That(success.Value!.Parameters, Is.Not.Null);
            Assert.That(success.Value.Parameters!, Has.Count.EqualTo(2));
            Assert.That(success.Value.Parameters!["key1"], Is.EqualTo("value1"));
            Assert.That(success.Value.Parameters!["key2"], Is.EqualTo("value2"));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllTriggers()
    {
        // Arrange
        var entity1 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            Schedule = "0 0 * * *",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ScheduleTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ScheduleTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsTrigger()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "UniqueTriggerName",
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueTriggerName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Name))
                .EqualTo("UniqueTriggerName")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.JobTemplateId))
                .EqualTo(_jobTemplateId));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentTrigger");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsTriggers()
    {
        // Arrange
        var otherJobTemplateId = Guid.NewGuid();
        var entity1 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            Schedule = "0 0 * * *",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ScheduleTriggerEntity
        {
            JobTemplateId = otherJobTemplateId,
            Name = "OtherTrigger",
            Schedule = "0 0 0 * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByJobTemplateIdAsync(_jobTemplateId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ScheduleTrigger>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(ScheduleTrigger.JobTemplateId)).EqualTo(_jobTemplateId));
        }
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByJobTemplateIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ScheduleTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledTriggers()
    {
        // Arrange
        var entity1 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "EnabledTrigger1",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "EnabledTrigger2",
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "DisabledTrigger",
            Schedule = "0 0 0 * *",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<ScheduleTrigger>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All.With.Property(nameof(ScheduleTrigger.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledTriggers()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "DisabledTrigger",
            Schedule = "0 * * * *",
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<ScheduleTrigger>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger()
    {
        // Arrange
        var model = new ScheduleTrigger(
            Guid.Empty,
            _jobTemplateId,
            "NewTrigger",
            "0 * * * *",
            true,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.ScheduleTriggers.AsNoTracking()
            where t.Name == "NewTrigger"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Name))
                    .EqualTo("NewTrigger")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Schedule))
                    .EqualTo("0 * * * *")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(ScheduleTrigger.Enabled)).True);
            Assert.That(entity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.Name)).EqualTo("NewTrigger")
                .And.Property(nameof(ScheduleTriggerEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ScheduleTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTriggerWithParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string?> { ["param1"] = "value1", ["param2"] = null }
            as IReadOnlyDictionary<string, string?>;

        var model = new ScheduleTrigger(
            Guid.Empty,
            _jobTemplateId,
            "TriggerWithParams",
            "0 0 * * *",
            true,
            parameters,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.ScheduleTriggers.AsNoTracking()
            where t.Name == "TriggerWithParams"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Parameters, Is.Not.Null);

            var deserializedParams = JsonSerializer.Deserialize<Dictionary<string, string?>>(entity.Parameters!);
            Assert.That(deserializedParams, Is.Not.Null);
            Assert.That(deserializedParams!, Has.Count.EqualTo(2));
            Assert.That(deserializedParams!["param1"], Is.EqualTo("value1"));
            Assert.That(deserializedParams!["param2"], Is.Null);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesTriggerWithRunTimes()
    {
        // Arrange
        var lastRunAt = DateTimeOffset.UtcNow.AddHours(-1);
        var nextRunAt = DateTimeOffset.UtcNow.AddHours(1);

        var model = new ScheduleTrigger(
            Guid.Empty,
            _jobTemplateId,
            "TriggerWithTimes",
            "0 * * * *",
            true,
            null,
            lastRunAt,
            nextRunAt,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from t in _context.ScheduleTriggers.AsNoTracking()
            where t.Name == "TriggerWithTimes"
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<ScheduleTrigger>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.LastRunAt, Is.EqualTo(lastRunAt));
            Assert.That(entity.NextRunAt, Is.EqualTo(nextRunAt));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTrigger()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "OriginalName",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var newJobTemplateId = Guid.NewGuid();
        var model = new ScheduleTrigger(
            entity.Id,
            newJobTemplateId,
            "UpdatedName",
            "0 0 * * *",
            false,
            null,
            null,
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from t in _context.ScheduleTriggers.AsNoTracking()
            where t.Id == entity.Id
            select t).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(ScheduleTriggerEntity.Schedule)).EqualTo("0 0 * * *")
                .And.Property(nameof(ScheduleTriggerEntity.Enabled)).False
                .And.Property(nameof(ScheduleTriggerEntity.JobTemplateId)).EqualTo(newJobTemplateId)
                .And.Property(nameof(ScheduleTriggerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ScheduleTriggerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ScheduleTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new ScheduleTrigger(
            Guid.NewGuid(),
            _jobTemplateId,
            "NonExistent",
            "0 * * * *",
            true,
            null,
            null,
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
    public async Task UpdateAsync_UpdatesParameters()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TriggerToUpdate",
            Schedule = "0 * * * *",
            Enabled = true,
            Parameters = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "value" }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var newParameters = new Dictionary<string, string?> { ["new"] = "newValue" }
            as IReadOnlyDictionary<string, string?>;

        var model = new ScheduleTrigger(
            entity.Id,
            _jobTemplateId,
            entity.Name,
            entity.Schedule,
            entity.Enabled,
            newParameters,
            null,
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
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
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "OriginalName",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new ScheduleTriggerPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(ScheduleTriggerEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ScheduleTriggerEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ScheduleTrigger")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesSchedule()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new ScheduleTriggerPatch
        {
            Id = entity.Id,
            Schedule = new("0 0 * * *")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.Schedule)).EqualTo("0 0 * * *"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var patch = new ScheduleTriggerPatch
        {
            Id = entity.Id,
            Enabled = new(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesParameters()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            Parameters = JsonSerializer.Serialize(new Dictionary<string, string?> { ["old"] = "value" }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var newParameters = new Dictionary<string, string> { ["new"] = "newValue" }
            as IReadOnlyDictionary<string, string>;

        var patch = new ScheduleTriggerPatch
        {
            Id = entity.Id,
            Parameters = new(newParameters)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
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
        var patch = new ScheduleTriggerPatch
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
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "ToDelete",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.ScheduleTriggers.AnyAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("ScheduleTrigger")
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

    [Test]
    public async Task UpdateRunTimesAsync_UpdatesRunTimes()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            LastRunAt = null,
            NextRunAt = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        var lastRunAt = DateTimeOffset.UtcNow;
        var nextRunAt = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var result = await _subject.UpdateRunTimesAsync(entity.Id, lastRunAt, nextRunAt);

        // Assert
        var updatedEntity = await _context.ScheduleTriggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ScheduleTriggerEntity>()
                .With.Property(nameof(ScheduleTriggerEntity.LastRunAt)).EqualTo(lastRunAt)
                .And.Property(nameof(ScheduleTriggerEntity.NextRunAt)).EqualTo(nextRunAt));
        }
    }

    [Test]
    public async Task UpdateRunTimesAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateRunTimesAsync(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateRunTimesAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestTrigger",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateRunTimesAsync(entity.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
