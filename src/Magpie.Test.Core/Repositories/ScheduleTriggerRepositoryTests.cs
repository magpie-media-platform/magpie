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
using Magpie.Models.Patch;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new ScheduleTriggerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByIdAsync(trigger.Id);

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
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllTriggers()
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTriggers()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListDueTriggersAsync_ReturnsDueTriggers()
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListDueTriggersAsync(now);

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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.TryClaimNextRunAsync(trigger.Id, expectedNextRun, newNextRun);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(trigger.Id);
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.TryClaimNextRunAsync(trigger.Id, wrongExpectedNextRun, newNextRun);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateAsync_CreatesTrigger()
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
        var result = await _subject.CreateAsync(trigger, _userId);

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
    public async Task CreateAsync_CreatesAuditRecord()
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
        await _subject.CreateAsync(trigger, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
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
        await _context.SaveChangesAsync();

        trigger.Name = "UpdatedTrigger";
        trigger.Schedule = "0 12 * * *";
        trigger.Enabled = false;

        // Act
        var result = await _subject.UpdateAsync(trigger, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(trigger.Id);
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
        var result = await _subject.UpdateAsync(trigger, _userId);

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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.DeleteAsync(trigger.Id, _userId);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(trigger.Id);
        Assert.That(deleted, Is.Null);
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
        await _context.SaveChangesAsync();

        // Act
        await _subject.DeleteAsync(trigger.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("ScheduleTrigger"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesNameField()
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
        await _context.SaveChangesAsync();

        var patch = new ScheduleTriggerPatch(trigger.Id, Name: new ScalarPatch<string>("PatchedName"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(trigger.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Has.Property(nameof(patched.Name)).EqualTo("PatchedName"));
            Assert.That(patched, Has.Property(nameof(patched.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabledField()
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
        await _context.SaveChangesAsync();

        var patch = new ScheduleTriggerPatch(trigger.Id, Enabled: new ScalarPatch<bool>(false));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var patched = await _subject.GetByIdAsync(trigger.Id);
        Assert.That(patched, Has.Property(nameof(patched.Enabled)).False);
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new ScheduleTriggerPatch(Guid.NewGuid(), Name: new ScalarPatch<string>("Test"));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.False);
    }
}
