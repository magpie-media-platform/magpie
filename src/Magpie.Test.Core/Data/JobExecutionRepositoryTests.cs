using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Jobs;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using EventTriggerEntity = Magpie.Data.Database.Entities.EventTrigger;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using JobExecutionEntity = Magpie.Data.Database.Entities.JobExecution;
using JobExecutionParameterEntity = Magpie.Data.Database.Entities.JobExecutionParameter;
using ScheduleTriggerEntity = Magpie.Data.Database.Entities.ScheduleTrigger;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class JobExecutionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IJobExecutionRepository _subject = null!;
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

        _subject = new JobExecutionRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<JobExecutionRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsExecution()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<JobExecution?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(JobExecution.JobTemplateId))
                .EqualTo(_jobTemplateId)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(JobExecution.Status))
                .EqualTo(JobExecutionStatus.Running));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<JobExecution?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsParameters()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        _context.JobExecutionParameters.Add(new()
        {
            JobExecutionId = entity.Id,
            Key = "key1",
            Value = "value1"
        });
        _context.JobExecutionParameters.Add(new()
        {
            JobExecutionId = entity.Id,
            Key = "key2",
            Value = "value2"
        });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        var success = result as SuccessResult<JobExecution?>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Is.Not.Null);
            Assert.That(success.Value!.Parameters, Has.Count.EqualTo(2));
            Assert.That(success.Value.Parameters["key1"], Is.EqualTo("value1"));
            Assert.That(success.Value.Parameters["key2"], Is.EqualTo("value2"));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllExecutions()
    {
        // Arrange
        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoExecutions()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsExecutions()
    {
        // Arrange
        var otherJobTemplateId = Guid.NewGuid();
        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new JobExecutionEntity
        {
            JobTemplateId = otherJobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByJobTemplateIdAsync(_jobTemplateId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<JobExecution>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(JobExecution.JobTemplateId)).EqualTo(_jobTemplateId));
        }
    }

    [Test]
    public async Task GetByJobTemplateIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByJobTemplateIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByScheduleTriggerIdAsync_ReturnsExecutions()
    {
        // Arrange
        var trigger1 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var trigger2 = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.AddRange(trigger1, trigger2);
        _context.SaveChanges();

        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            ScheduleTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            ScheduleTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            ScheduleTriggerId = trigger2.Id,
            Status = JobExecutionStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByScheduleTriggerIdAsync(trigger1.Id);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<JobExecution>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(JobExecution.ScheduleTriggerId)).EqualTo(trigger1.Id));
        }
    }

    [Test]
    public async Task GetByScheduleTriggerIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByScheduleTriggerIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByEventTriggerIdAsync_ReturnsExecutions()
    {
        // Arrange
        var trigger1 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger1",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var trigger2 = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "Trigger2",
            EventName = "content.updated",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.EventTriggers.AddRange(trigger1, trigger2);
        _context.SaveChanges();

        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            EventTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            EventTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            EventTriggerId = trigger2.Id,
            Status = JobExecutionStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByEventTriggerIdAsync(trigger1.Id);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<JobExecution>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(JobExecution.EventTriggerId)).EqualTo(trigger1.Id));
        }
    }

    [Test]
    public async Task GetByEventTriggerIdAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByEventTriggerIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsExecutions()
    {
        // Arrange
        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(JobExecutionStatus.Running);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<JobExecution>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All
                .With.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
        }
    }

    [Test]
    public async Task GetByStatusAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByStatusAsync(JobExecutionStatus.Failed);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<JobExecution>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesExecution()
    {
        // Arrange
        var startedAt = DateTimeOffset.UtcNow;
        var model = new JobExecution(
            Guid.Empty,
            _jobTemplateId,
            null,
            null,
            null,
            JobExecutionStatus.Queued,
            null,
            null,
            new Dictionary<string, string?>(),
            startedAt,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from e in _context.JobExecutions.AsNoTracking()
            where e.JobTemplateId == _jobTemplateId
            select e).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<JobExecution>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(JobExecution.JobTemplateId))
                    .EqualTo(_jobTemplateId)
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(JobExecution.Status))
                    .EqualTo(JobExecutionStatus.Queued));
            Assert.That(entity, Is.InstanceOf<JobExecutionEntity>()
                .With.Property(nameof(JobExecutionEntity.JobTemplateId)).EqualTo(_jobTemplateId)
                .And.Property(nameof(JobExecutionEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("JobExecution")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesExecutionWithParameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string?>
        {
            ["param1"] = "value1",
            ["param2"] = "value2",
            ["param3"] = null
        };

        var model = new JobExecution(
            Guid.Empty,
            _jobTemplateId,
            null,
            null,
            null,
            JobExecutionStatus.Queued,
            null,
            null,
            parameters,
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var success = result as SuccessResult<JobExecution>;
        Assert.That(success, Is.Not.Null);

        var paramEntities = await _context.JobExecutionParameters
            .Where(p => p.JobExecutionId == success!.Value.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paramEntities, Has.Count.EqualTo(3));
            Assert.That(paramEntities, Has.One.With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("param1")
                .And.Property(nameof(JobExecutionParameterEntity.Value)).EqualTo("value1"));
            Assert.That(paramEntities, Has.One.With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("param2")
                .And.Property(nameof(JobExecutionParameterEntity.Value)).EqualTo("value2"));
            Assert.That(paramEntities, Has.One.With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("param3")
                .And.Property(nameof(JobExecutionParameterEntity.Value)).Null);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesExecutionWithAllFields()
    {
        // Arrange
        var scheduleTrigger = new ScheduleTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestSchedule",
            Schedule = "0 * * * *",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var eventTrigger = new EventTriggerEntity
        {
            JobTemplateId = _jobTemplateId,
            Name = "TestEvent",
            EventName = "content.added",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ScheduleTriggers.Add(scheduleTrigger);
        _context.EventTriggers.Add(eventTrigger);
        _context.SaveChanges();

        var eventId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var completedAt = startedAt.AddMinutes(5);

        var model = new JobExecution(
            Guid.Empty,
            _jobTemplateId,
            scheduleTrigger.Id,
            eventTrigger.Id,
            eventId,
            JobExecutionStatus.Completed,
            0.75,
            "Processing complete",
            new Dictionary<string, string?>(),
            startedAt,
            completedAt,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobTemplateId == _jobTemplateId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<JobExecution>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.ScheduleTriggerId, Is.EqualTo(scheduleTrigger.Id));
            Assert.That(entity.EventTriggerId, Is.EqualTo(eventTrigger.Id));
            Assert.That(entity.EventId, Is.EqualTo(eventId));
            Assert.That(entity.Status, Is.EqualTo(JobExecutionStatus.Completed));
            Assert.That(entity.Progress, Is.EqualTo(0.75));
            Assert.That(entity.Message, Is.EqualTo("Processing complete"));
            Assert.That(entity.CompletedAt, Is.EqualTo(completedAt));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExecution()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        var newJobTemplateId = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow;
        var model = new JobExecution(
            entity.Id,
            newJobTemplateId,
            null,
            null,
            null,
            JobExecutionStatus.Completed,
            1.0,
            "Done",
            new Dictionary<string, string?>(),
            entity.StartedAt,
            completedAt,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from e in _context.JobExecutions.AsNoTracking()
            where e.Id == entity.Id
            select e).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<JobExecutionEntity>()
                .With.Property(nameof(JobExecutionEntity.JobTemplateId)).EqualTo(newJobTemplateId)
                .And.Property(nameof(JobExecutionEntity.Status)).EqualTo(JobExecutionStatus.Completed)
                .And.Property(nameof(JobExecutionEntity.Progress)).EqualTo(1.0)
                .And.Property(nameof(JobExecutionEntity.Message)).EqualTo("Done")
                .And.Property(nameof(JobExecutionEntity.CompletedAt)).EqualTo(completedAt)
                .And.Property(nameof(JobExecutionEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(JobExecutionEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("JobExecution")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new JobExecution(
            Guid.NewGuid(),
            _jobTemplateId,
            null,
            null,
            null,
            JobExecutionStatus.Running,
            null,
            null,
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow,
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
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        _context.JobExecutionParameters.Add(new()
        {
            JobExecutionId = entity.Id,
            Key = "oldKey",
            Value = "oldValue"
        });
        _context.SaveChanges();

        var newParams = new Dictionary<string, string?>
        {
            ["newKey1"] = "newValue1",
            ["newKey2"] = "newValue2"
        };

        var model = new JobExecution(
            entity.Id,
            _jobTemplateId,
            null,
            null,
            null,
            JobExecutionStatus.Completed,
            1.0,
            null,
            newParams,
            entity.StartedAt,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var paramEntities = await _context.JobExecutionParameters
            .Where(p => p.JobExecutionId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(paramEntities, Has.Count.EqualTo(2));
            Assert.That(paramEntities, Has.None
                .With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("oldKey"));
            Assert.That(paramEntities, Has.One
                .With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("newKey1"));
            Assert.That(paramEntities, Has.One
                .With.Property(nameof(JobExecutionParameterEntity.Key)).EqualTo("newKey2"));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesStatusAndRelatedFields()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        var completedAt = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateStatusAsync(
            entity.Id,
            JobExecutionStatus.Completed,
            1.0,
            "Success",
            completedAt,
            _userId);

        // Assert
        var updatedEntity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<JobExecutionEntity>()
                .With.Property(nameof(JobExecutionEntity.Status)).EqualTo(JobExecutionStatus.Completed)
                .And.Property(nameof(JobExecutionEntity.Progress)).EqualTo(1.0)
                .And.Property(nameof(JobExecutionEntity.Message)).EqualTo("Success")
                .And.Property(nameof(JobExecutionEntity.CompletedAt)).EqualTo(completedAt)
                .And.Property(nameof(JobExecutionEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(JobExecutionEntity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesProgress()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            Progress = 0.0,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdateStatusAsync(
            entity.Id,
            JobExecutionStatus.Running,
            0.5,
            "Processing...",
            null,
            _userId);

        // Assert
        var updatedEntity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<JobExecutionEntity>()
                .With.Property(nameof(JobExecutionEntity.Status)).EqualTo(JobExecutionStatus.Running)
                .And.Property(nameof(JobExecutionEntity.Progress)).EqualTo(0.5)
                .And.Property(nameof(JobExecutionEntity.Message)).EqualTo("Processing..."));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_UpdatesFailedStatus()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.UpdateStatusAsync(
            entity.Id,
            JobExecutionStatus.Failed,
            null,
            "Connection timeout",
            DateTimeOffset.UtcNow,
            _userId);

        // Assert
        var updatedEntity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<JobExecutionEntity>()
                .With.Property(nameof(JobExecutionEntity.Status)).EqualTo(JobExecutionStatus.Failed)
                .And.Property(nameof(JobExecutionEntity.Message)).EqualTo("Connection timeout"));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateStatusAsync(
            Guid.NewGuid(),
            JobExecutionStatus.Completed,
            1.0,
            null,
            DateTimeOffset.UtcNow,
            _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateStatusAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateStatusAsync(
            entity.Id,
            JobExecutionStatus.Completed,
            1.0,
            null,
            DateTimeOffset.UtcNow,
            _userId);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DeletesExecution()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.JobExecutions.AnyAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("JobExecution")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesExecutionWithParameters()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        _context.JobExecutionParameters.Add(new()
        {
            JobExecutionId = entity.Id,
            Key = "key1",
            Value = "value1"
        });
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.JobExecutions.AnyAsync(e => e.Id == entity.Id);
        var paramsExist = await _context.JobExecutionParameters.AnyAsync(p => p.JobExecutionId == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(paramsExist, Is.False);
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
    public async Task DeleteByJobTemplateIdAsync_DeletesExecutionsForTemplate()
    {
        // Arrange
        var otherJobTemplateId = Guid.NewGuid();
        var entity1 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new JobExecutionEntity
        {
            JobTemplateId = otherJobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteByJobTemplateIdAsync(_jobTemplateId, _userId);

        // Assert
        var templateExecutionsExist = await _context.JobExecutions.AnyAsync(e => e.JobTemplateId == _jobTemplateId);
        var otherExecutionExists = await _context.JobExecutions.AnyAsync(e => e.JobTemplateId == otherJobTemplateId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
                .With.Property(nameof(SuccessResult<>.Value)).EqualTo(2));
            Assert.That(templateExecutionsExist, Is.False);
            Assert.That(otherExecutionExists, Is.True);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("JobExecution"));
        }
    }

    [Test]
    public async Task DeleteByJobTemplateIdAsync_DeletesExecutionParameters()
    {
        // Arrange
        var entity = new JobExecutionEntity
        {
            JobTemplateId = _jobTemplateId,
            Status = JobExecutionStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.JobExecutions.Add(entity);
        _context.SaveChanges();

        _context.JobExecutionParameters.Add(new()
        {
            JobExecutionId = entity.Id,
            Key = "key1",
            Value = "value1"
        });
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteByJobTemplateIdAsync(_jobTemplateId, _userId);

        // Assert
        var paramsExist = await _context.JobExecutionParameters.AnyAsync(p => p.JobExecutionId == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
                .With.Property(nameof(SuccessResult<>.Value)).EqualTo(1));
            Assert.That(paramsExist, Is.False);
        }
    }

    [Test]
    public async Task DeleteByJobTemplateIdAsync_ReturnsZero_WhenNoMatches()
    {
        // Act
        var result = await _subject.DeleteByJobTemplateIdAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<int>>()
            .With.Property(nameof(SuccessResult<>.Value)).EqualTo(0));
    }
}
