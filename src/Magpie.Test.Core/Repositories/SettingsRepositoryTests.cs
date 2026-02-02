using System;
using System.Collections.Generic;
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
public class SettingsRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISettingsRepository _subject = null!;
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

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());

        var transactionManager = new TransactionManager(_context);

        _subject = new SettingsRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<SettingsRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllSettingsGroups()
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSettingsGroups()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_IncludesSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupWithSettings",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "TestKey",
            Value = "TestValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        var resultGroup = result.First();
        var settings = resultGroup.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(settings, Has.One.With.Property(nameof(Setting.Key)).EqualTo("TestKey")
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
        await _context.SaveChangesAsync();

        var lookupKey = new SettingsGroupLookupKey(_extensionId, "TestGroup");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey);

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
        var result = await _subject.GetByIdAsync(lookupKey);

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
        await _context.SaveChangesAsync();

        var lookupKey = new SettingsGroupLookupKey(null, "SystemGroup");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("SystemGroup"));
            Assert.That(result, Has.Property(nameof(result.ExtensionId)).Null);
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupWithSettings",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "SettingKey",
            Value = "SettingValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        var lookupKey = new SettingsGroupLookupKey(null, "GroupWithSettings");

        // Act
        var result = await _subject.GetByIdAsync(lookupKey);

        // Assert
        Assert.That(result, Is.Not.Null);
        var settings = result!.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(settings, Has.One.With.Property(nameof(Setting.Key)).EqualTo("SettingKey")
                .And.Property(nameof(Setting.Value)).EqualTo("SettingValue"));
        }
    }

    [Test]
    public async Task PatchAsync_CreatesSettingsGroup_WhenNotExists()
    {
        // Arrange
        var patch = new SettingsGroupPatch(
            null,
            "NewGroup",
            new DictionaryPatch<string, string, string>(null, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var created = await _context.SettingsGroups.FirstOrDefaultAsync(sg => sg.Name == "NewGroup");
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
        var patch = new SettingsGroupPatch(
            _extensionId,
            "ExtensionGroup",
            new DictionaryPatch<string, string, string>(null, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var created = await _context.SettingsGroups.FirstOrDefaultAsync(
            sg => sg.Name == "ExtensionGroup" && sg.ExtensionId == _extensionId);
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
        var patch = new SettingsGroupPatch(
            null,
            "GroupWithNewSettings",
            new DictionaryPatch<string, string, string>(settingsToAdd, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var group = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "GroupWithNewSettings");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(group, Is.Not.Null);
            Assert.That(group, Has.Property(nameof(group.Settings)).With.Count.EqualTo(2));
            Assert.That(group, Has.Property(nameof(group.Settings)).With.One.With.Property(nameof(Setting.Key))
                .EqualTo("Key1"));
            Assert.That(group, Has.Property(nameof(group.Settings)).With.One.With.Property(nameof(Setting.Key))
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
        await _context.SaveChangesAsync();

        var settingsToAdd = new Dictionary<string, string>
        {
            { "NewKey", "NewValue" }
        };
        var patch = new SettingsGroupPatch(
            null,
            "ExistingGroup",
            new DictionaryPatch<string, string, string>(settingsToAdd, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "ExistingGroup");
        Assert.That(updated, Is.Not.Null);
        var settings = updated!.Settings.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings, Has.Count.EqualTo(1));
            Assert.That(settings, Has.One.With.Property(nameof(Setting.Key)).EqualTo("NewKey")
                .And.Property(nameof(Setting.Value)).EqualTo("NewValue"));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupToUpdate",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "UpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "UpdateKey", "UpdatedValue" }
        };
        var patch = new SettingsGroupPatch(
            null,
            "GroupToUpdate",
            new DictionaryPatch<string, string, string>(null, null, settingsToUpdate));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "UpdateKey");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Value)).EqualTo("UpdatedValue"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_RemovesSettings()
    {
        // Arrange
        var group = new SettingsGroup
        {
            Name = "GroupToRemoveFrom",
            ExtensionId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync();

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
        await _context.SaveChangesAsync();

        var patch = new SettingsGroupPatch(
            null,
            "GroupToRemoveFrom",
            new DictionaryPatch<string, string, string>(null, ["RemoveKey"], null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updatedGroup = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "GroupToRemoveFrom");
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
        await _context.SaveChangesAsync();

        var patch = new SettingsGroupPatch(
            null,
            "GroupToModify",
            new DictionaryPatch<string, string, string>(null, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.SettingsGroups.FirstOrDefaultAsync(sg => sg.Name == "GroupToModify");
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
        var patch = new SettingsGroupPatch(
            null,
            "AuditedNewGroup",
            new DictionaryPatch<string, string, string>(null, null, null));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
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
        var patch = new SettingsGroupPatch(
            null,
            "GroupWithAuditedSetting",
            new DictionaryPatch<string, string, string>(settingsToAdd, null, null));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Setting"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
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
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "AuditUpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "AuditUpdateKey", "NewValue" }
        };
        var patch = new SettingsGroupPatch(
            null,
            "GroupForUpdateAudit",
            new DictionaryPatch<string, string, string>(null, null, settingsToUpdate));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
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
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "AuditDeleteKey",
            Value = "DeleteValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        var patch = new SettingsGroupPatch(
            null,
            "GroupForDeleteAudit",
            new DictionaryPatch<string, string, string>(null, ["AuditDeleteKey"], null));

        // Act
        await _subject.PatchAsync(patch, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
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
        var patch = new SettingsGroupPatch(
            null,
            "GroupWithNullValue",
            new DictionaryPatch<string, string, string>(settingsToAdd!, null, null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "NullValueKey");
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
        await _context.SaveChangesAsync();

        var setting = new Setting
        {
            SettingsGroupId = group.Id,
            Key = "NullUpdateKey",
            Value = "OriginalValue",
            CreatedAt = DateTime.UtcNow
        };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        var settingsToUpdate = new Dictionary<string, string?>
        {
            { "NullUpdateKey", null }
        };
        var patch = new SettingsGroupPatch(
            null,
            "GroupForNullUpdate",
            new DictionaryPatch<string, string, string>(null, null, settingsToUpdate!));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _context.Settings.FirstOrDefaultAsync(s => s.Key == "NullUpdateKey");
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
        await _context.SaveChangesAsync();

        var settingsToUpdate = new Dictionary<string, string>
        {
            { "NonExistentKey", "SomeValue" }
        };
        var patch = new SettingsGroupPatch(
            null,
            "GroupForNonExistentUpdate",
            new DictionaryPatch<string, string, string>(null, null, settingsToUpdate));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var settings = await _context.Settings.Where(s => s.SettingsGroupId == group.Id).ToListAsync();
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
        await _context.SaveChangesAsync();

        var patch = new SettingsGroupPatch(
            null,
            "GroupForNonExistentRemove",
            new DictionaryPatch<string, string, string>(null, ["NonExistentKey"], null));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

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
        await _context.SaveChangesAsync();

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
        await _context.SaveChangesAsync();

        var patch = new SettingsGroupPatch(
            null,
            "GroupForCombinedPatch",
            new DictionaryPatch<string, string, string>(
                new Dictionary<string, string> { { "AddMe", "AddValue" } },
                ["DeleteMe"],
                new Dictionary<string, string> { { "UpdateMe", "NewValue" } }));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.True);

        var updatedGroup = await _context.SettingsGroups
            .Include(sg => sg.Settings)
            .FirstOrDefaultAsync(sg => sg.Name == "GroupForCombinedPatch");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updatedGroup, Has.Property(nameof(updatedGroup.Settings)).With.Count.EqualTo(2));
            Assert.That(updatedGroup, Has.Property(nameof(updatedGroup.Settings))
                .With.One.With.Property(nameof(Setting.Key)).EqualTo("AddMe")
                .And.Property(nameof(Setting.Value)).EqualTo("AddValue"));
            Assert.That(updatedGroup, Has.Property(nameof(updatedGroup.Settings))
                .With.One.With.Property(nameof(Setting.Key)).EqualTo("UpdateMe")
                .And.Property(nameof(Setting.Value)).EqualTo("NewValue"));
            Assert.That(updatedGroup, Has.Property(nameof(updatedGroup.Settings))
                .With.None.With.Property(nameof(Setting.Key)).EqualTo("DeleteMe"));
        }
    }
}
