using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Configuration;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using static Magpie.Abstractions.Data.ISettingsRepository;

using ExtensionEntity = Magpie.Data.Database.Entities.Extension;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using SettingEntity = Magpie.Data.Database.Entities.Setting;
using SettingsGroupEntity = Magpie.Data.Database.Entities.SettingsGroup;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class SettingsRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ISettingsRepository _subject = null!;
    private SettingsGroupEntity _settingsGroupTemplate = null!;
    private SettingEntity _settingTemplate = null!;
    private Guid _extensionId;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var extension = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/extension/path",
            AssemblyName = "Test.Assembly",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.Extensions.Add(extension);
        _context.SaveChanges();

        _userId = user.Id;
        _extensionId = extension.Id;

        _settingsGroupTemplate = new SettingsGroupEntity
        {
            Name = "PLACEHOLDER",
            ExtensionId = _extensionId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };
        _settingTemplate = new SettingEntity
        {
            Key = "PLACEHOLDER",
            Value = "PLACEHOLDER",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = _userId
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new SettingsRepository(_context, historyRepo, loggerFactory.CreateLogger<SettingsRepository>());

    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task CreateAsync_CreatesSetting()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var model = new Setting(groupEntity.Id, "TestKey", "TestValue");

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var settingEntity = await (
            from s in _context.Settings.AsNoTracking()
            where s.Key == model.Key
            where s.SettingsGroupId == groupEntity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Setting>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Setting.Key))
                    .EqualTo(model.Key)
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Setting.Value))
                    .EqualTo(model.Value)
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Setting.SettingsGroupId))
                    .EqualTo(model.SettingsGroupId));
            Assert.That(settingEntity, Is.InstanceOf<SettingEntity>()
                .With.Property(nameof(SettingEntity.Key)).EqualTo(model.Key)
                .And.Property(nameof(SettingEntity.Value)).EqualTo(model.Value)
                .And.Property(nameof(SettingEntity.SettingsGroupId)).EqualTo(groupEntity.Id)
                .And.Property(nameof(SettingEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSetting()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntity = _settingTemplate with { Key = "TestKey", Value = "TestValue", SettingsGroup = groupEntity };
        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.Add(settingEntity);
        _context.SaveChanges();

        var model = new Setting(settingEntity.SettingsGroupId, settingEntity.Key, "NewValue");

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var entity = await (
            from s in _context.Settings.AsNoTracking()
            where s.Id == settingEntity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<SettingEntity>()
                .With.Property(nameof(SettingEntity.Value)).EqualTo("NewValue")
                .And.Property(nameof(SettingEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SettingEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSetting()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntity = _settingTemplate with { Key = "TestKey", Value = "TestValue", SettingsGroup = groupEntity };
        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.Add(settingEntity);
        _context.SaveChanges();

        var key = new SettingKey(_extensionId, groupEntity.Name, settingEntity.Key);

        // Act
        var result = await _subject.DeleteAsync(key, _userId);

        // Assert
        var entityExists = await (
            from s in _context.Settings
            where s.Id == settingEntity.Id
            select s).AnyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task GetAllSettingsAsync_ReturnsSettings()
    {
        // Arrange
        var groupEntityA = _settingsGroupTemplate with { Name = "GroupA" };
        var groupEntityB = _settingsGroupTemplate with { Name = "GroupB" };
        var settingEntityA1 = _settingTemplate with { Key = "SettingA1", SettingsGroup = groupEntityA };
        var settingEntityA2 = _settingTemplate with { Key = "SettingA2", SettingsGroup = groupEntityA };
        var settingEntityB1 = _settingTemplate with { Key = "SettingB1", SettingsGroup = groupEntityB };
        var settingEntityB2 = _settingTemplate with { Key = "SettingB2", SettingsGroup = groupEntityB };

        _context.SettingsGroups.AddRange(groupEntityA, groupEntityB);
        _context.Settings.AddRange(settingEntityA1, settingEntityA2, settingEntityB1, settingEntityB2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllSettingsAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Setting>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(4));
    }

    [Test]
    public async Task GetAllSettingsAsync_ReturnsEmptyList_WhenNoSettings()
    {
        // Act
        var result = await _subject.GetAllSettingsAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Setting>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesSettingsGroup()
    {
        // Arrange
        var model = new SettingsGroup(Guid.Empty, "TestGroup", _extensionId);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from g in _context.SettingsGroups
            where g.Name == "TestGroup"
            select g).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<SettingsGroup>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.Name)).EqualTo(model.Name));
            Assert.That(entity, Is.InstanceOf<SettingsGroupEntity>()
                .With.Property(nameof(SettingsGroupEntity.Name)).EqualTo(model.Name)
                .And.Property(nameof(SettingsGroupEntity.ExtensionId)).EqualTo(_extensionId)
                .And.Property(nameof(SettingsGroupEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SettingsGroup")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesSystemSettingsGroup_WhenExtensionIdIsNull()
    {
        // Arrange
        var model = new SettingsGroup(Guid.Empty, "TestGroup", null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from g in _context.SettingsGroups
            where g.Name == "TestGroup"
            select g).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<SettingsGroup>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.Name)).EqualTo(model.Name));
            Assert.That(entity, Is.InstanceOf<SettingsGroupEntity>()
                .With.Property(nameof(SettingsGroupEntity.Name)).EqualTo(model.Name)
                .And.Property(nameof(SettingsGroupEntity.ExtensionId)).Null
                .And.Property(nameof(SettingsGroupEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SettingsGroup")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_ReturnsFailure_WhenGroupAlreadyExists()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "ExistingGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var model = new SettingsGroup(Guid.Empty, "ExistingGroup", _extensionId);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<SettingsGroup>>());
    }

    [Test]
    public async Task UpdateAsync_UpdatesSettingsGroup()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var model = new SettingsGroup(groupEntity.Id, "UpdatedName", _extensionId);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var entity = await (
            from g in _context.SettingsGroups.AsNoTracking()
            where g.Id == groupEntity.Id
            select g).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
                .With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entity, Is.InstanceOf<SettingsGroupEntity>()
                .With.Property(nameof(SettingsGroupEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(SettingsGroupEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SettingsGroupEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SettingsGroup")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenGroupDoesNotExist()
    {
        // Arrange
        var model = new SettingsGroup(Guid.NewGuid(), "NonExistent", _extensionId);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesSettingsGroup()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "ToDelete" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var key = new SettingsGroupKey(_extensionId, "ToDelete");

        // Act
        var result = await _subject.DeleteAsync(key, _userId);

        // Assert
        var entityExists = await (
            from g in _context.SettingsGroups
            where g.Name == "ToDelete"
            where g.ExtensionId == _extensionId
            select g).AnyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
                .With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SettingsGroup")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenGroupDoesNotExist()
    {
        // Arrange
        var key = new SettingsGroupKey(_extensionId, "NonExistent");

        // Act
        var result = await _subject.DeleteAsync(key, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_CascadesDeleteSettings()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "GroupWithSettings" };
        var settingEntityA = _settingTemplate with { Key = "SettingA", SettingsGroup = groupEntity };
        var settingEntityB = _settingTemplate with { Key = "SettingB", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.AddRange(settingEntityA, settingEntityB);
        _context.SaveChanges();

        var key = new SettingsGroupKey(_extensionId, "GroupWithSettings");

        // Act
        var result = await _subject.DeleteAsync(key, _userId);

        // Assert
        var settingsExist = await (
            from s in _context.Settings
            where s.SettingsGroupId == groupEntity.Id
            select s).AnyAsync();

        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingsExist, Is.False);
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("SettingsGroup"));
            Assert.That(historyEntities, Has.Exactly(2)
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task GetAllSettingsGroupsAsync_ReturnsSettingsGroups()
    {
        // Arrange
        _context.SettingsGroups.AddRange(
            _settingsGroupTemplate with { Name = "GroupA" },
            _settingsGroupTemplate with { Name = "GroupB" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllSettingsGroupsAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<SettingsGroup>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllSettingsGroupsAsync_ReturnsEmptyList_WhenNoGroups()
    {
        // Act
        var result = await _subject.GetAllSettingsGroupsAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<SettingsGroup>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task PatchAsync_PatchesSettingsGroup()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntityA = _settingTemplate with { Key = "SettingA", Value = "ValueA", SettingsGroup = groupEntity };
        var settingEntityB = _settingTemplate with { Key = "SettingB", Value = "ValueB", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.AddRange(settingEntityA, settingEntityB);
        _context.SaveChanges();

        var patch = new SettingsGroupPatch(
            _extensionId,
            "TestGroup",
            new DictionaryPatch<string, string, string>(
                Add: new Dictionary<string, string> { ["SettingC"] = "ValueC" },
                Remove: ["SettingB"],
                Update: new Dictionary<string, string> { ["SettingA"] = "UpdatedValueA" }));

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var settingEntities = await (
            from s in _context.Settings
            where s.SettingsGroupId == groupEntity.Id
            select s).ToListAsync();

        var historyEntities = await _context.HistoryEntries.ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>()
                .With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(settingEntities, Has.Count.EqualTo(2));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(SettingEntity.Key)).EqualTo("SettingA")
                .And.Property(nameof(SettingEntity.Value)).EqualTo("UpdatedValueA"));
            Assert.That(settingEntities, Has.One
                .With.Property(nameof(SettingEntity.Key)).EqualTo("SettingC")
                .And.Property(nameof(SettingEntity.Value)).EqualTo("ValueC"));
            Assert.That(historyEntities, Has.Count.EqualTo(3));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
            Assert.That(historyEntities, Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsSettings()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntityA = _settingTemplate with { Key = "SettingA", Value = "ValueA", SettingsGroup = groupEntity };
        var settingEntityB = _settingTemplate with { Key = "SettingB", Value = "ValueB", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.AddRange(settingEntityA, settingEntityB);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingsAsync(_extensionId, "TestGroup", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Setting>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsSystemSettings_WhenExtensionIdIsNull()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "SystemGroup", ExtensionId = null };
        var settingEntityA = _settingTemplate with { Key = "SettingA", Value = "ValueA", SettingsGroup = groupEntity };
        var settingEntityB = _settingTemplate with { Key = "SettingB", Value = "ValueB", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.AddRange(settingEntityA, settingEntityB);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingsAsync(null, "SystemGroup", default);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Setting>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task UpsertSettingAsync_UpdatesSetting_WhenExists()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntity = _settingTemplate with { Key = "TestKey", Value = "OldValue", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.Add(settingEntity);
        _context.SaveChanges();

        var key = new SettingKey(_extensionId, "TestGroup", "TestKey");

        // Act
        var result = await _subject.UpsertSettingAsync(key, "NewValue", _userId);

        // Assert
        var entity = await (
            from s in _context.Settings
            where s.Key == "TestKey"
            where s.SettingsGroupId == groupEntity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<SettingEntity>()
                .With.Property(nameof(SettingEntity.Value)).EqualTo("NewValue")
                .And.Property(nameof(SettingEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(SettingEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task UpsertSettingAsync_CreatesSetting_WhenGroupExists()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "ExistingGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var key = new SettingKey(_extensionId, "ExistingGroup", "NewSetting");

        // Act
        var result = await _subject.UpsertSettingAsync(key, "NewValue", _userId);

        // Assert
        var entity = await (
            from s in _context.Settings
            where s.Key == "NewSetting"
            where s.SettingsGroupId == groupEntity.Id
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entity, Is.InstanceOf<SettingEntity>()
                .With.Property(nameof(SettingEntity.Value)).EqualTo("NewValue")
                .And.Property(nameof(SettingEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task UpsertSettingAsync_CreatesGroupAndSetting_WhenGroupDoesNotExist()
    {
        // Arrange
        var key = new SettingKey(_extensionId, "TestGroup", "TestKey");

        // Act
        var result = await _subject.UpsertSettingAsync(key, "NewValue", _userId);

        // Assert
        var settingEntity = await (
            from s in _context.Settings.Include(s => s.SettingsGroup)
            join g in _context.SettingsGroups on s.SettingsGroupId equals g.Id
            where s.Key == key.Key
            where g.Name == key.GroupName
            where g.ExtensionId == _extensionId
            select s).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(settingEntity, Is.InstanceOf<SettingEntity>()
                .With.Property(nameof(SettingEntity.Value)).EqualTo("NewValue")
                .And.Property(nameof(SettingEntity.SettingsGroup))
                    .Property(nameof(SettingsGroupEntity.Name)).EqualTo(key.GroupName)
                .And.Property(nameof(SettingEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpsertSettingAsync_DeletesSetting_WhenValueIsNull()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntity = _settingTemplate with { Key = "TestKey", Value = "OldValue", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.Add(settingEntity);
        _context.SaveChanges();

        var key = new SettingKey(_extensionId, groupEntity.Name, settingEntity.Key);

        // Act
        var result = await _subject.UpsertSettingAsync(key, null, _userId);

        // Assert
        var entityExists = await (
            from s in _context.Settings
            where s.Key == "DeleteSetting"
            where s.SettingsGroupId == groupEntity.Id
            select s).AnyAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Setting"));
        }
    }

    [Test]
    public async Task UpsertSettingAsync_NoOp_WhenValueIsNullAndSettingDoesNotExist()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        var key = new SettingKey(_extensionId, "TestGroup", "NonExistent");

        // Act
        var result = await _subject.UpsertSettingAsync(key, null, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult>());
            Assert.That(await _context.HistoryEntries.ToListAsync(), Is.Empty);
        }
    }

    [Test]
    public async Task GetSettingAsync_ReturnsSetting()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };
        var settingEntity = _settingTemplate with { Key = "TestKey", Value = "TestValue", SettingsGroup = groupEntity };

        _context.SettingsGroups.Add(groupEntity);
        _context.Settings.Add(settingEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingAsync(_extensionId, groupEntity.Name, settingEntity.Key);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Setting?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Setting.Value)).EqualTo("TestValue"));
    }

    [Test]
    public async Task GetSettingAsync_ReturnsNull_WhenSettingDoesNotExist()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "EmptyGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingAsync(_extensionId, "EmptyGroup", "NonExistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Setting?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }
    [Test]
    public async Task GetSettingsGroupAsync_ReturnsGroup()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup" };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingsGroupAsync(_extensionId, groupEntity.Name);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SettingsGroup?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.Name))
                .EqualTo(groupEntity.Name)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.ExtensionId))
                .EqualTo(groupEntity.ExtensionId));
    }

    [Test]
    public async Task GetSettingsGroupAsync_ReturnsSystemGroup_WhenExtensionIdIsNull()
    {
        // Arrange
        var groupEntity = _settingsGroupTemplate with { Name = "TestGroup", ExtensionId = null };

        _context.SettingsGroups.Add(groupEntity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetSettingsGroupAsync(null, groupEntity.Name);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SettingsGroup?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.Name)).EqualTo(groupEntity.Name)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(SettingsGroup.ExtensionId)).Null);
    }

    [Test]
    public async Task GetSettingsGroupAsync_ReturnsNull_WhenGroupDoesNotExist()
    {
        // Act
        var result = await _subject.GetSettingsGroupAsync(_extensionId, "NonExistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<SettingsGroup?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }
}
