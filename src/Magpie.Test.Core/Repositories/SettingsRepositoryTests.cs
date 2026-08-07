using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class SettingsRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISettingsRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;
    private Guid _extensionId;

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

        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/extension.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.Extensions.Add(extension);
        _context.SaveChanges();

        _userId = user.Id;
        _extensionId = extension.Id;

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new SettingsRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IContentHierarchyItemRepository>(),
            eventBus.Object,
            loggerFactory.CreateLogger<SettingsRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllSettingsGroups_WhenGroupsExist()
    {
        // Arrange
        var group1 = new SettingsGroup
        {
            Name = "Group1",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        var group2 = new SettingsGroup
        {
            Name = "Group2",
            ExtensionId = _extensionId,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.AddRange(group1, group2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSettingsGroups()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_IncludesSettings_WhenGroupsHaveSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupWithSettings",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "TestKey",
            Value = "TestValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        var resultGroup = result.First();
        var settings = resultGroup.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(
                settings,
                Has.One.With.Property(nameof(Setting.Key)).EqualTo("TestKey")
                    .And.Property(nameof(Setting.Value)).EqualTo("TestValue"));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSettingsGroup_WhenExists()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "TestGroup",
            ExtensionId = _extensionId,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var lookupKey = new SettingsGroupLookupKey(_extensionId, "TestGroup");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestGroup"));
            Assert.That(result, Has.Property(nameof(result.ExtensionId)).EqualTo(_extensionId));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var lookupKey = new SettingsGroupLookupKey(Guid.NewGuid(), "NonExistent");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSettingsGroup_WhenExtensionIdIsNull()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "SystemGroup",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var lookupKey = new SettingsGroupLookupKey(null, "SystemGroup");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("SystemGroup"));
            Assert.That(result, Has.Property(nameof(result.ExtensionId)).Null);
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettings_WhenGroupHasSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupWithSettings",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "SettingKey",
            Value = "SettingValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var lookupKey = new SettingsGroupLookupKey(null, "GroupWithSettings");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        var settings = result!.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(
                settings,
                Has.One.With.Property(nameof(Setting.Key)).EqualTo("SettingKey")
                    .And.Property(nameof(Setting.Value)).EqualTo("SettingValue"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesSettingsGroup_WhenNotExists()
    {
        // Arrange
        var patch = new SettingsGroupPatch()
        {
            ExtensionId = null,
            Name = "NewGroup",
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var created = await _context.SettingsGroups.FirstOrDefaultAsync(sg => sg.Name == "NewGroup")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.Not.Null);
            Assert.That(created, Has.Property(nameof(created.Name)).EqualTo("NewGroup"));
            Assert.That(created, Has.Property(nameof(created.CreatedByUserId)).EqualTo(_userId));
            Assert.That(created, Has.Property(nameof(created.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesSettingsGroup_WithExtensionId()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = _extensionId,
            Name = "ExtensionGroup",
            Settings = new()
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var created = await _context.SettingsGroups.FirstOrDefaultAsync(
            sg => sg.Name == "ExtensionGroup" && sg.ExtensionId == _extensionId).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.Not.Null);
            Assert.That(created, Has.Property(nameof(created.ExtensionId)).EqualTo(_extensionId));
        }
    }

    [Test]
    public async Task PatchAsync_AddsSettings_ToNewGroup()
    {
        // Arrange
        var settingsToAdd = new Dictionary<string, string>
        {
            { "Key1", "Value1" },
            { "Key2", "Value2" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupWithNewSettings",
            Settings = new() { Add = settingsToAdd }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var group = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "GroupWithNewSettings").ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(group, Is.Not.Null);
            Assert.That(group, Has.Property(nameof(group.Settings)).With.Count.EqualTo(2));
            Assert.That(
                group,
                Has.Property(nameof(group.Settings)).With.One.With.Property(nameof(Setting.Key))
                    .EqualTo("Key1"));
            Assert.That(
                group,
                Has.Property(nameof(group.Settings)).With.One.With.Property(nameof(Setting.Key))
                    .EqualTo("Key2"));
        }
    }

    [Test]
    public async Task PatchAsync_AddsSettings_ToExistingGroup()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "ExistingGroup",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var settingsToAdd = new Dictionary<string, string>
        {
            { "NewKey", "NewValue" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "ExistingGroup",
            Settings = new() { Add = settingsToAdd }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "ExistingGroup").ConfigureAwait(false);
        Assert.That(updated, Is.Not.Null);
        var settings = updated!.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(
                settings,
                Has.One.With.Property(nameof(Setting.Key)).EqualTo("NewKey")
                    .And.Property(nameof(Setting.Value)).EqualTo("NewValue"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSettings_WhenUpdatesAreProvided()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupToUpdate",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "UpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "UpdateKey", "UpdatedValue" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupToUpdate",
            Settings = new() { Update = settingsToUpdate }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "UpdateKey").ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Value)).EqualTo("UpdatedValue"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_RemovesSettings_WhenRemovalsAreProvided()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupToRemoveFrom",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting1 = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "KeepKey",
            Value = "KeepValue",
            CreatedAt = DateTime.UtcNow
        };
        var setting2 = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "RemoveKey",
            Value = "RemoveValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.AddRange(setting1, setting2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupToRemoveFrom",
            Settings = new() { Remove = ["RemoveKey"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updatedGroup = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "GroupToRemoveFrom").ConfigureAwait(false);
        Assert.That(updatedGroup, Is.Not.Null);
        var settings = updatedGroup!.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(settings, Has.One.With.Property(nameof(Setting.Key)).EqualTo("KeepKey"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesModifiedFields_OnExistingGroup()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupToModify",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupToModify",
            Settings = new()
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.SettingsGroups.FirstOrDefaultAsync(sg => sg.Name == "GroupToModify")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_ForNewGroup()
    {
        // Arrange
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "AuditedNewGroup",
            Settings = new()
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("SettingsGroup"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_ForAddedSetting()
    {
        // Arrange
        var settingsToAdd = new Dictionary<string, string>
        {
            { "AuditKey", "AuditValue" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupWithAuditedSetting",
            Settings = new() { Add = settingsToAdd }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Setting"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_ForUpdatedSetting()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForUpdateAudit",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "AuditUpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "AuditUpdateKey", "NewValue" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForUpdateAudit",
            Settings = new() { Update = settingsToUpdate }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_ForDeletedSetting()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForDeleteAudit",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "AuditDeleteKey",
            Value = "DeleteValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForDeleteAudit",
            Settings = new() { Remove = ["AuditDeleteKey"] }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task PatchAsync_HandlesNullValue_InAddedSetting()
    {
        // Arrange
        var settingsToAdd = new Dictionary<string, string?>
        {
            { "NullValueKey", null }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupWithNullValue",
            Settings = new() { Add = settingsToAdd! }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "NullValueKey").ConfigureAwait(false);
        Assert.That(setting, Has.Property(nameof(setting.Value)).EqualTo(string.Empty));
    }

    [Test]
    public async Task PatchAsync_HandlesNullValue_InUpdatedSetting()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForNullUpdate",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "NullUpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var settingsToUpdate = new Dictionary<string, string?>
        {
            { "NullUpdateKey", null }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForNullUpdate",
            Settings = new() { Update = settingsToUpdate! }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "NullUpdateKey").ConfigureAwait(false);
        Assert.That(updated, Has.Property(nameof(updated.Value)).EqualTo(string.Empty));
    }

    [Test]
    public async Task PatchAsync_IgnoresUpdate_WhenSettingNotExists()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForNonExistentUpdate",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "NonExistentKey", "SomeValue" }
        };
        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForNonExistentUpdate",
            Settings = new() { Update = settingsToUpdate }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var settings = await _context.Settings.Where(s => s.SettingsGroupId == group.Id).ToListAsync()
            .ConfigureAwait(false);
        Assert.That(settings, Is.Empty);
    }

    [Test]
    public async Task PatchAsync_IgnoresRemove_WhenSettingNotExists()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForNonExistentRemove",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForNonExistentRemove",
            Settings = new() { Remove = ["NonExistentKey"] }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task PatchAsync_PerformsAllOperations_InSinglePatch()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupForCombinedPatch",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var setting1 = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "UpdateMe",
            Value = "OldValue",
            CreatedAt = DateTime.UtcNow
        };
        var setting2 = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "DeleteMe",
            Value = "DeleteValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.AddRange(setting1, setting2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            ExtensionId = null,
            Name = "GroupForCombinedPatch",
            Settings = new()
            {
                Add = new() { { "AddMe", "AddValue" } },
                Remove = ["DeleteMe"],
                Update = new() { { "UpdateMe", "NewValue" } }
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updatedGroup = await _context.SettingsGroups
        .Include(sg => sg.Settings)
        .FirstOrDefaultAsync(sg => sg.Name == "GroupForCombinedPatch").ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updatedGroup, Has.Property(nameof(updatedGroup.Settings)).With.Count.EqualTo(2));
            Assert.That(
                updatedGroup,
                Has.Property(nameof(updatedGroup.Settings))
                    .With.One.With.Property(nameof(Setting.Key)).EqualTo("AddMe")
                    .And.Property(nameof(Setting.Value)).EqualTo("AddValue"));
            Assert.That(
                updatedGroup,
                Has.Property(nameof(updatedGroup.Settings))
                    .With.One.With.Property(nameof(Setting.Key)).EqualTo("UpdateMe")
                    .And.Property(nameof(Setting.Value)).EqualTo("NewValue"));
            Assert.That(
                updatedGroup,
                Has.Property(nameof(updatedGroup.Settings))
                    .With.None.With.Property(nameof(Setting.Key)).EqualTo("DeleteMe"));
        }
    }

    [Test]
    public async Task PatchAsync_PublishesUpdatedEvent_WithSortedDistinctChangedFields()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "sorted-keys",
            CreatedAt = DateTime.UtcNow
        };
        group.Settings.Add(new Setting { Key = "middle", Value = "before", CreatedAt = DateTime.UtcNow });
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new SettingsGroupPatch
        {
            Name = group.Name,
            Settings = new()
            {
                Add = new Dictionary<string, string>
                {
                    ["zeta"] = "last",
                    ["alpha"] = "temporary"
                },
                Update = new Dictionary<string, string> { ["middle"] = "after" },
                Remove = ["alpha"]
            }
        };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo(MagpieEvents.Names.SettingsGroupUpdated));
            Assert.That(_publishedEvents[0].Context.Values, Does.Contain(group.Name));
            Assert.That(_publishedEvents[0].Context.Values, Does.Contain("alpha,middle,zeta"));
        }
    }
}
