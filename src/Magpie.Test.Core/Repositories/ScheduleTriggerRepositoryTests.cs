using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
public sealed class ScheduleTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IScheduleTriggerRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;

    [Test]
    public async Task PatchAsync_PreservesCommittedParameters_WhenTrackedTriggerIsStale()
    {
        // Arrange
        var created = await _subject.CreateAsync(new ScheduleTrigger
        {
            JobType = "TestJob",
            Scope = JobScope.Administrative,
            Name = "Original",
            Schedule = "0 0 1 1 *",
            Parameters = new() { ["first"] = "old" }
        }, _userId).ConfigureAwait(false);
        var parameters = new Dictionary<string, string> { ["first"] = "new" };
        await _context.ScheduleTriggers.Where(trigger => trigger.Id == created.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(trigger => trigger.Parameters, parameters))
            .ConfigureAwait(false);

        // Act
        var changed = await _subject.PatchAsync(new ScheduleTriggerPatch
        {
            Id = created.Id,
            Parameters = new() { Add = new() { ["second"] = "added" } }
        }, _userId).ConfigureAwait(false);

        // Assert
        var stored = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.True);
            Assert.That(stored!.Parameters["first"], Is.EqualTo("new"));
            Assert.That(stored.Parameters["second"], Is.EqualTo("added"));
        }
    }

    [Test]
    public void LockAsync_RejectsRequest_WhenNoTransactionIsOpen()
    {
        // Act
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => _subject.LockAsync(Guid.Empty));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("A schedule trigger lock requires a transaction."));
    }

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

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new ScheduleTriggerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            eventBus.Object,
            NullLogger<ScheduleTriggerRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsTrigger_WhenExists()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "TestTrigger",
            JobType = "TestJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestTrigger"));
            Assert.That(result, Has.Property(nameof(result.JobType)).EqualTo("TestJob"));
            Assert.That(result, Has.Property(nameof(result.Schedule)).EqualTo("0 0 * * *"));
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
    public async Task ListByCreatedByUserIdAsync_ReturnsOwnedTriggers_WhenUserIdIsProvided()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        _context.ScheduleTriggers.AddRange(
            new ScheduleTrigger
            {
                Name = "Mine",
                JobType = "Job",
                Scope = JobScope.User,
                Schedule = "0 0 * * *",
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = _userId,
            },
            new ScheduleTrigger
            {
                Name = "Theirs",
                JobType = "Job",
                Scope = JobScope.User,
                Schedule = "0 0 * * *",
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
            Assert.That(result, Has.One.With.Property(nameof(ScheduleTrigger.Name)).EqualTo("Mine"));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllTriggers_WhenTriggersExist()
    {
        // Arrange
        var trigger1 = new ScheduleTrigger
        {
            Name = "Trigger1",
            JobType = "Job1",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var trigger2 = new ScheduleTrigger
        {
            Name = "Trigger2",
            JobType = "Job2",
            Scope = JobScope.Administrative,
            Schedule = "0 12 * * *",
            Enabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.AddRange(trigger1, trigger2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListDueTriggersAsync_ReturnsDueTriggers_WhenTriggersAreDue()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var dueTrigger = new ScheduleTrigger
        {
            Name = "DueTrigger",
            JobType = "Job1",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            NextRunAt = now.AddMinutes(-5),
            CreatedAt = now
        };
        var futureTrigger = new ScheduleTrigger
        {
            Name = "FutureTrigger",
            JobType = "Job2",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            NextRunAt = now.AddHours(1),
            CreatedAt = now
        };
        var disabledTrigger = new ScheduleTrigger
        {
            Name = "DisabledTrigger",
            JobType = "Job3",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = false,
            NextRunAt = now.AddMinutes(-5),
            CreatedAt = now
        };
        _context.ScheduleTriggers.AddRange(dueTrigger, futureTrigger, disabledTrigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { DueAsOf = now, Enabled = true },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false)).Items.ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result, Has.One.With.Property(nameof(ScheduleTrigger.Name)).EqualTo("DueTrigger"));
        }
    }

    [Test]
    public async Task TryClaimNextRunAsync_UpdatesTrigger_WhenExpectedNextRunMatches()
    {
        // Arrange
        var expectedNextRun = DateTime.UtcNow.AddMinutes(-5);
        var newNextRun = DateTime.UtcNow.AddHours(1);
        var trigger = new ScheduleTrigger
        {
            Name = "ClaimTrigger",
            JobType = "Job1",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            NextRunAt = expectedNextRun,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.TryClaimNextRunAsync(trigger.Id, expectedNextRun, newNextRun).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.NextRunAt)).EqualTo(newNextRun));
            Assert.That(updated, Has.Property(nameof(updated.LastRunAt)).Not.Null);
        }
    }

    [Test]
    public async Task TryClaimNextRunAsync_ReturnsFalse_WhenExpectedNextRunDoesNotMatch()
    {
        // Arrange
        var actualNextRun = DateTime.UtcNow.AddMinutes(-5);
        var wrongExpectedNextRun = DateTime.UtcNow.AddMinutes(-10);
        var newNextRun = DateTime.UtcNow.AddHours(1);
        var trigger = new ScheduleTrigger
        {
            Name = "ClaimTrigger",
            JobType = "Job1",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            NextRunAt = actualNextRun,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.TryClaimNextRunAsync(trigger.Id, wrongExpectedNextRun, newNextRun)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task TryClaimNextRunAsync_ReturnsFalse_WhenTriggerIsDisabled()
    {
        // Arrange
        var expectedNextRun = DateTime.UtcNow.AddMinutes(-5);
        var newNextRun = DateTime.UtcNow.AddHours(1);
        var trigger = new ScheduleTrigger
        {
            Name = "DisabledClaimTrigger",
            JobType = "Job1",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = false,
            NextRunAt = expectedNextRun,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.TryClaimNextRunAsync(trigger.Id, expectedNextRun, newNextRun).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger_WhenRequestIsValid()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "NewTrigger",
            JobType = "NewJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(trigger, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewTrigger"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenTriggerIsCreated()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "AuditedTrigger",
            JobType = "AuditJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(trigger, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ScheduleTrigger"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesTrigger_WhenExists()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "OriginalTrigger",
            JobType = "OriginalJob",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        trigger.Name = "UpdatedTrigger";
        trigger.Schedule = "0 12 * * *";
        trigger.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(trigger, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Name)).EqualTo("UpdatedTrigger"));
            Assert.That(updated, Has.Property(nameof(updated.Schedule)).EqualTo("0 12 * * *"));
            Assert.That(updated, Has.Property(nameof(updated.Enabled)).False);
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true
        };

        // Act
        var result = await _subject.UpdateAsync(trigger, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesTrigger_WhenExists()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "ToDelete",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(trigger.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenTriggerExists()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "DeleteAudit",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(trigger.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ScheduleTrigger"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesName_WhenNameIsProvided()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "ToPatch",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ScheduleTriggerPatch { Id = trigger.Id, Name = new() { Value = "PatchedName" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesEnabledState_WhenEnabledStateIsProvided()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "ToPatchEnabled",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ScheduleTriggerPatch { Id = trigger.Id, Enabled = new() { Value = false } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
    }

    [Test]
    public async Task PatchAsync_NamesNextRunAtInTheChangedFields_WhenOnlyNextRunAtIsProvided()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "ToReschedule",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            NextRunAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var patch = new ScheduleTriggerPatch
        {
            Id = trigger.Id,
            NextRunAt = new() { Value = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc) }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        var published = _publishedEvents.Single(magpieEvent =>
            magpieEvent.Name == "scheduleTrigger.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched!.NextRunAt, Is.EqualTo(new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                published.Context["scheduleTrigger.changedFields"],
                Is.EqualTo("nextRunAt"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new ScheduleTriggerPatch { Id = Guid.NewGuid(), Name = new() { Value = "Test" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_AddsUpdatesAndRemovesParameters_WhenTheParameterPatchNamesEachOperation()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "ParameterPatch",
            JobType = "Job",
            Scope = JobScope.System,
            Schedule = "0 0 * * *",
            Enabled = true,
            Parameters = new() { ["mode"] = "old", ["stale"] = "drop" },
            CreatedAt = DateTime.UtcNow
        };
        _context.ScheduleTriggers.Add(trigger);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var patch = new ScheduleTriggerPatch
        {
            Id = trigger.Id,
            Parameters = new()
            {
                Add = new() { ["limit"] = "10" },
                Update = new() { ["mode"] = "new", ["absent"] = "ignored" },
                Remove = ["stale"]
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var patched = await _subject.GetByIdAsync(trigger.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(patched!.Parameters["limit"], Is.EqualTo("10"));
            Assert.That(patched.Parameters["mode"], Is.EqualTo("new"));
            Assert.That(patched.Parameters.ContainsKey("stale"), Is.False);
            Assert.That(patched.Parameters.ContainsKey("absent"), Is.False);
        }
    }
}
