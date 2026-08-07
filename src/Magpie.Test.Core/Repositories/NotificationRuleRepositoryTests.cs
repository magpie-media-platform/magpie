using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class NotificationRuleRepositoryTests
{
    private MagpieDbContext _context = null!;
    private INotificationRuleRepository _subject = null!;
    private Guid _operatorId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var admin = new User
        {
            Username = "admin",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        var op = new User
        {
            Username = "operator",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Manager,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.AddRange(admin, op);
        _context.SaveChanges();

        _operatorId = op.Id;

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new NotificationRuleRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<NotificationRuleRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_PersistsRuleAndAuditRecord_WhenRequestIsValid()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Name = "first",
            ProviderType = "webhook",
            OwnerUserId = _operatorId,
            Enabled = true,
            Settings = { ["url"] = "https://example.test/hook" },
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string> { Value = "download.completed" }
            }
        };

        // Act
        var created = await _subject.CreateAsync(rule, _operatorId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.NotificationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == created.Id)
            .ConfigureAwait(false);
        var audits = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(NotificationRule.Name)).EqualTo("first"));
            Assert.That(persisted!.Settings, Does.ContainKey("url").WithValue("https://example.test/hook"));
            Assert.That(persisted.EventFilter.EventName!.Value, Is.EqualTo("download.completed"));
            Assert.That(persisted.CreatedByUserId, Is.EqualTo(_operatorId));
            Assert.That(audits, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("NotificationRule"));
            Assert.That(audits, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
        }
    }

    [Test]
    public async Task ListEnabledAsync_ReturnsEnabledRules_WhenDisabledRulesExist()
    {
        // Arrange
        var enabledRule = new NotificationRule
        {
            Name = "enabled",
            OwnerUserId = _operatorId,
            ProviderType = "webhook",
            Enabled = true,
            Settings = { ["url"] = "https://example.test/hook" },
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string> { Value = "x.y" }
            }
        };
        var enabled = await _subject.CreateAsync(enabledRule, _operatorId).ConfigureAwait(false);
        var disabledRule = new NotificationRule
        {
            Name = "disabled",
            OwnerUserId = _operatorId,
            ProviderType = "webhook",
            Enabled = false,
            Settings = { ["url"] = "https://example.test/hook" },
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string> { Value = "x.y" }
            }
        };
        var disabled = await _subject.CreateAsync(disabledRule, _operatorId).ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(new() { Enabled = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items
            .ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.Single().Id, Is.EqualTo(enabled.Id));
    }

    [Test]
    public async Task UpdateAsync_UpdatesRuleAndAuditRecord_WhenRuleExists()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Name = "first",
            OwnerUserId = _operatorId,
            ProviderType = "webhook",
            Enabled = true,
            Settings = { ["url"] = "https://example.test/hook" },
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string> { Value = "x.y" }
            }
        };
        var created = await _subject.CreateAsync(rule, _operatorId).ConfigureAwait(false);

        created.Name = "renamed";
        created.Enabled = false;
        created.Settings["url"] = "https://example.test/changed";

        // Act
        var result = await _subject.UpdateAsync(created, _operatorId).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var audits = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(reloaded, Has.Property(nameof(NotificationRule.Name)).EqualTo("renamed"));
            Assert.That(reloaded, Has.Property(nameof(NotificationRule.Enabled)).False);
            Assert.That(reloaded!.Settings["url"], Is.EqualTo("https://example.test/changed"));
            Assert.That(audits, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesRuleAndCreatesAuditRecord_WhenRuleExists()
    {
        // Arrange
        var rule = new NotificationRule
        {
            Name = "first",
            OwnerUserId = _operatorId,
            ProviderType = "webhook",
            Enabled = true,
            Settings = { ["url"] = "https://example.test/hook" },
            EventFilter = new EventFilter
            {
                EventName = new PropertyCriterion<string> { Value = "x.y" }
            }
        };
        var created = await _subject.CreateAsync(rule, _operatorId).ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(created.Id, _operatorId).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var audits = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(reloaded, Is.Null);
            Assert.That(audits, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _operatorId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }
}
