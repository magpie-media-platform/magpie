using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Core.Security;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class NotificationRuleRepositoryTests
{
    private MagpieDbContext _context = null!;
    private string _dataRoot = null!;
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
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _dataRoot = Path.Combine(Path.GetTempPath(), "magpie-secret-repo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        var hostEnvironment = new MagpieHostEnvironment(
            NodeId: "test-node",
            IsClustered: false,
            IsSupervised: false,
            SystemRoot: _dataRoot,
            ExtensionsRoot: _dataRoot,
            ConfigRoot: _dataRoot,
            DataRoot: _dataRoot,
            StartedAt: DateTime.UtcNow);
        var protector = new DataProtectionService(new DefaultDataProtectionProvider(
            hostEnvironment,
            loggerFactory.CreateLogger<DefaultDataProtectionProvider>()));

        _subject = new NotificationRuleRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<NotificationRuleRepository>(),
            protector,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        Directory.Delete(_dataRoot, recursive: true);
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
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(NotificationRule.Name)).EqualTo("first"));
            Assert.That(persisted!.Settings, Does.ContainKey("__magpieSecret"));
            Assert.That(persisted.Settings, Does.Not.ContainKey("url"));
            Assert.That(created.Settings, Does.ContainKey("url").WithValue("https://example.test/hook"));
            Assert.That(reloaded!.Settings, Does.ContainKey("url").WithValue("https://example.test/hook"));
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

    [Test]
    public async Task UpdateAsync_EncryptsStoredSettings_WhenSettingsAreReplaced()
    {
        // Arrange
        var model = new NotificationRule
        {
            Name = "Protected",
            ProviderType = "test-provider",
            Settings = { ["token"] = "old-secret" }
        };
        _context.NotificationRules.Add(model);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _subject.UpdateAsync(model with
        {
            Settings = new() { ["token"] = "new-secret" }
        }, _operatorId).ConfigureAwait(false);
        var stored = await _context.NotificationRules.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var listed = (await _subject.ListAllAsync().ConfigureAwait(false)).Single();
        var page = await _subject.QueryAsync(new(), new() { PageNumber = 1, PageSize = 10 }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Settings.Keys, Is.EqualTo(["__magpieSecret"]));
            Assert.That(stored.Settings.Values.Single(), Does.Not.Contain("new-secret"));
            Assert.That(listed.Settings["token"], Is.EqualTo("new-secret"));
            Assert.That(page.Items.Single().Settings["token"], Is.EqualTo("new-secret"));
        }
    }

    [Test]
    public async Task PatchAsync_KeepsUnnamedFields_WhenThePatchOnlyFlipsEnabled()
    {
        // Arrange
        var model = new NotificationRule
        {
            Name = "Original",
            ProviderType = "test-provider",
            Enabled = true,
            Settings = { ["token"] = "stored-secret" }
        };
        _context.NotificationRules.Add(model);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var patched = await _subject
            .PatchAsync(new NotificationRulePatch { Id = model.Id, Enabled = new() { Value = false } }, _operatorId)
            .ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(model.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            Assert.That(updated!.Enabled, Is.False);
            Assert.That(updated.Name, Is.EqualTo("Original"));
            Assert.That(updated.Settings["token"], Is.EqualTo("stored-secret"));
        }
    }

    [Test]
    public async Task PatchAsync_AddsUpdatesAndRemovesSettings_WhenTheSettingsPatchNamesEachOperation()
    {
        // Arrange
        var model = new NotificationRule
        {
            Name = "SettingsPatch",
            ProviderType = "test-provider",
            Settings = { ["token"] = "old", ["stale"] = "drop" }
        };
        _context.NotificationRules.Add(model);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        var patch = new NotificationRulePatch
        {
            Id = model.Id,
            Settings = new()
            {
                Add = new() { ["channel"] = "alerts" },
                Update = new() { ["token"] = "new", ["absent"] = "ignored" },
                Remove = ["stale"]
            }
        };

        // Act
        var patched = await _subject.PatchAsync(patch, _operatorId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(model.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            Assert.That(updated!.Settings["channel"], Is.EqualTo("alerts"));
            Assert.That(updated.Settings["token"], Is.EqualTo("new"));
            Assert.That(updated.Settings.ContainsKey("stale"), Is.False);
            Assert.That(updated.Settings.ContainsKey("absent"), Is.False);
        }
    }
}
