using System;
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
public class JobExecutionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IJobExecutionRepository _subject = null!;
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

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);

        _subject = new JobExecutionRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<JobExecutionRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsExecution()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "TestJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(execution.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.JobType)).EqualTo("TestJob"));
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(JobExecutionStatus.Running));
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
    public async Task ListAllAsync_ReturnsAllExecutions()
    {
        // Arrange
        var execution1 = new JobExecution
        {
            JobType = "Job1",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        var execution2 = new JobExecution
        {
            JobType = "Job2",
            Scope = JobScope.Administrative,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(execution1, execution2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoExecutions()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_FiltersByStatus()
    {
        // Arrange
        var execution1 = new JobExecution
        {
            JobType = "Job1",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var execution2 = new JobExecution
        {
            JobType = "Job2",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var execution3 = new JobExecution
        {
            JobType = "Job3",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(execution1, execution2, execution3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { Status = JobExecutionStatus.Running }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByScheduleTriggerId()
    {
        // Arrange
        var trigger1 = new ScheduleTrigger
        {
            Name = "TestScheduleTrigger",
            JobType = "TestJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var trigger2 = new ScheduleTrigger
        {
            Name = "OtherScheduleTrigger",
            JobType = "OtherJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.AddRange(trigger1, trigger2);
        await _context.SaveChangesAsync();

        var execution1 = new JobExecution
        {
            JobType = "Job1",
            Scope = JobScope.System,
            ScheduleTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var execution2 = new JobExecution
        {
            JobType = "Job2",
            Scope = JobScope.System,
            ScheduleTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        var execution3 = new JobExecution
        {
            JobType = "Job3",
            Scope = JobScope.System,
            ScheduleTriggerId = trigger2.Id,
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(execution1, execution2, execution3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { ScheduleTriggerId = trigger1.Id }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(JobExecution.ScheduleTriggerId)).EqualTo(trigger1.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByEventTriggerId()
    {
        // Arrange
        var trigger1 = new EventTrigger
        {
            Name = "TestEventTrigger",
            Scope = JobScope.System,
            EventFilter = new(),
            JobType = "TestJob",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var trigger2 = new EventTrigger
        {
            Name = "OtherEventTrigger",
            Scope = JobScope.System,
            EventFilter = new(),
            JobType = "OtherJob",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.EventTriggers.AddRange(trigger1, trigger2);
        await _context.SaveChangesAsync();

        var execution1 = new JobExecution
        {
            JobType = "Job1",
            Scope = JobScope.System,
            EventTriggerId = trigger1.Id,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var execution2 = new JobExecution
        {
            JobType = "Job2",
            Scope = JobScope.System,
            EventTriggerId = trigger2.Id,
            Status = JobExecutionStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(execution1, execution2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new() { EventTriggerId = trigger1.Id }, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .With.All.Property(nameof(JobExecution.EventTriggerId)).EqualTo(trigger1.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyResult_WhenNoMatches()
    {
        // Act
        var result = await _subject.QueryAsync(new() { Status = JobExecutionStatus.Failed }, null, null);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Items)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesExecution()
    {
        // Arrange
        var model = new JobExecution
        {
            JobType = "NewJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobType == "NewJob");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.JobType)).EqualTo("NewJob"));
            Assert.That(result, Has.Property(nameof(result.Status)).EqualTo(JobExecutionStatus.Queued));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesExecutionWithAllFields()
    {
        // Arrange
        var scheduleTrigger = new ScheduleTrigger
        {
            Name = "TestScheduleTrigger",
            JobType = "FullJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var eventTrigger = new EventTrigger
        {
            Name = "TestEventTrigger",
            JobType = "FullJob",
            EventFilter = new(),
            Scope = JobScope.System,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/extension.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(scheduleTrigger);
        _context.EventTriggers.Add(eventTrigger);
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync();

        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(5);
        var timeoutAt = startedAt.AddHours(1);

        var model = new JobExecution
        {
            ScheduleTriggerId = scheduleTrigger.Id,
            EventTriggerId = eventTrigger.Id,
            ExtensionId = extension.Id,
            JobType = "FullJob",
            Scope = JobScope.Administrative,
            TriggerContext = { ["key1"] = "value1" },
            Parameters = { ["param1"] = "value1" },
            Progress = 0.75,
            Status = JobExecutionStatus.Completed,
            StatusMessage = "Processing complete",
            NodeId = "node-1",
            LastHeartbeat = startedAt.AddMinutes(4),
            TimeoutAt = timeoutAt,
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.JobType == "FullJob");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(entity.ScheduleTriggerId)).EqualTo(scheduleTrigger.Id));
            Assert.That(entity, Has.Property(nameof(entity.EventTriggerId)).EqualTo(eventTrigger.Id));
            Assert.That(entity, Has.Property(nameof(entity.ExtensionId)).EqualTo(extension.Id));
            Assert.That(entity, Has.Property(nameof(entity.Status)).EqualTo(JobExecutionStatus.Completed));
            Assert.That(entity, Has.Property(nameof(entity.Progress)).EqualTo(0.75));
            Assert.That(entity, Has.Property(nameof(entity.StatusMessage)).EqualTo("Processing complete"));
            Assert.That(entity, Has.Property(nameof(entity.NodeId)).EqualTo("node-1"));
            Assert.That(entity, Has.Property(nameof(entity.CompletedAt)).EqualTo(completedAt));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord()
    {
        // Arrange
        var model = new JobExecution
        {
            JobType = "AuditedJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };

        // Act
        await _subject.CreateAsync(model, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Has.One.Matches<AuditRecord>(r =>
            r.Operation == "Create" &&
            r.EntityType == "JobExecution" &&
            r.UserId == _userId));
    }

    [Test]
    public async Task UpdateAsync_UpdatesExecution()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "OriginalJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        var completedAt = DateTime.UtcNow;
        var model = execution with
        {
            Status = JobExecutionStatus.Completed,
            Progress = 1.0,
            StatusMessage = "Done",
            CompletedAt = completedAt
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == execution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Status))
                .EqualTo(JobExecutionStatus.Completed));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Progress)).EqualTo(1.0));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.StatusMessage)).EqualTo("Done"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.CompletedAt)).EqualTo(completedAt));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "NonExistent",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "JobToUpdate",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        var model = execution with { Status = JobExecutionStatus.Running };

        // Act
        await _subject.UpdateAsync(model, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Has.One.Matches<AuditRecord>(r =>
            r.Operation == "Update" &&
            r.EntityType == "JobExecution" &&
            r.UserId == _userId));
    }

    [Test]
    public async Task DeleteAsync_DeletesExecution()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "ToDelete",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(execution.Id, _userId);

        // Assert
        var entityExists = await _context.JobExecutions.AnyAsync(e => e.Id == execution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
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

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "JobToDelete",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(execution.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Has.One.Matches<AuditRecord>(r =>
            r.Operation == "Delete" &&
            r.EntityType == "JobExecution" &&
            r.UserId == _userId));
    }

    [Test]
    public async Task TryDequeueAsync_DequeuesExecution()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "QueuedJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.TryDequeueAsync("node-1");

        // Assert
        var updatedEntity = await _context.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == execution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(execution.Id));
            Assert.That(result, Has.Property(nameof(result.NodeId)).EqualTo("node-1"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.NodeId)).EqualTo("node-1"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.LastHeartbeat)).Not.Null);
        }
    }

    [Test]
    public async Task TryDequeueAsync_ReturnsNull_WhenNoQueuedExecutions()
    {
        // Act
        var result = await _subject.TryDequeueAsync("node-1");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryDequeueAsync_ReturnsNull_WhenAllExecutionsAreClaimed()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "ClaimedJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            NodeId = "other-node",
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.TryDequeueAsync("node-1");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryDequeueAsync_DequeuesOldestFirst()
    {
        // Arrange
        var olderExecution = new JobExecution
        {
            JobType = "OlderJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            NodeId = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var newerExecution = new JobExecution
        {
            JobType = "NewerJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(olderExecution, newerExecution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.TryDequeueAsync("node-1");

        // Assert
        Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(olderExecution.Id));
    }

    [Test]
    public async Task TryDequeueAsync_CreatesAuditRecord()
    {
        // Arrange
        var execution = new JobExecution
        {
            JobType = "AuditedDequeue",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync();

        // Act
        await _subject.TryDequeueAsync("node-1");

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Has.One.Matches<AuditRecord>(r =>
            r.Operation == "Dequeue" &&
            r.EntityType == "JobExecution"));
    }

}
