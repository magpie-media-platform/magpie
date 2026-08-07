using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ScheduleTriggerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IScheduleTriggerRepository _subject = null!;
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

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new ScheduleTriggerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<ScheduleTriggerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
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
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new ScheduleTriggerPatch { Id = Guid.NewGuid(), Name = new() { Value = "Test" } };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }
}
