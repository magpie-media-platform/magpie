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
using Magpie.Models.Patch;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ExtensionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IExtensionRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;

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

        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MagpieEvent, CancellationToken>((magpieEvent, _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var auditRepository = new AuditRecordRepository(
            _context, Mock.Of<IEventBus>(), NullLogger<AuditRecordRepository>.Instance, transactionManager,
            Mock.Of<IJobExecutionScopeFactory>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        _subject = new ExtensionRepository(
            _context,
            transactionManager,
            auditRepository,
            eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<ExtensionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsExtension_WhenExtensionExists()
    {
        // Arrange
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
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(extension.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("TestExtension"));
            Assert.That(result, Has.Property(nameof(result.Version)).EqualTo("1.0.0"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task QueryAsync_ReturnsExtension_WhenPackageIdMatches()
    {
        // Arrange
        var extension = new Extension
        {
            PackageId = "Magpie.Extensions.PackageLookup",
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
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ExtensionQuery
        {
            PackageId = "Magpie.Extensions.PackageLookup"
        }, new() { PageNumber = 1, PageSize = 100 }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items.Single().PackageId, Is.EqualTo("Magpie.Extensions.PackageLookup"));
            Assert.That(result.Items.Single().Name, Is.EqualTo("TestExtension"));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllExtensions_WhenExtensionsExist()
    {
        // Arrange
        var extension1 = new Extension
        {
            PackageId = "Magpie.Extensions.ListOne",
            Name = "Extension1",
            Version = "1.0.0",
            FilePath = "/path/to/ext1.dll",
            AssemblyName = "Extension1, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        var extension2 = new Extension
        {
            PackageId = "Magpie.Extensions.ListTwo",
            Name = "Extension2",
            Version = "2.0.0",
            FilePath = "/path/to/ext2.dll",
            AssemblyName = "Extension2, Version=2.0.0.0",
            Enabled = false,
            LoadOrder = 200,
            Managed = true,
            AutoUpgrade = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.AddRange(extension1, extension2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoExtensions()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesExtension_WhenRequestIsValid()
    {
        // Arrange
        var model = new Extension
        {
            Name = "NewExtension",
            Version = "1.0.0",
            TargetABI = "1.0.0-2.0.0",
            FilePath = "/path/to/new.dll",
            AssemblyName = "NewExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == "NewExtension").ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("NewExtension"));
            Assert.That(result, Has.Property(nameof(result.Version)).EqualTo("1.0.0"));
            Assert.That(result, Has.Property(nameof(result.Enabled)).True);
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesExtensionWithAllFields_WhenAllFieldsAreSet()
    {
        // Arrange
        var lastChecked = DateTime.UtcNow.AddDays(-1);
        var model = new Extension
        {
            Name = "FullExtension",
            Version = "2.0.0",
            TargetABI = "1.0.0-3.0.0",
            FilePath = "/path/to/full.dll",
            AssemblyName = "FullExtension, Version=2.0.0.0",
            Enabled = true,
            LoadOrder = 150,
            Managed = true,
            NuGetId = "FullExtension.Package",
            PackageRepository = "https://nuget.example.com",
            AutoUpgrade = true,
            LastChecked = lastChecked
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == "FullExtension").ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Version)).EqualTo("2.0.0"));
            Assert.That(entity, Has.Property(nameof(entity.TargetABI)).EqualTo("1.0.0-3.0.0"));
            Assert.That(entity, Has.Property(nameof(entity.LoadOrder)).EqualTo(150));
            Assert.That(entity, Has.Property(nameof(entity.Managed)).True);
            Assert.That(entity, Has.Property(nameof(entity.NuGetId)).EqualTo("FullExtension.Package"));
            Assert.That(entity, Has.Property(nameof(entity.PackageRepository)).EqualTo("https://nuget.example.com"));
            Assert.That(entity, Has.Property(nameof(entity.AutoUpgrade)).True);
            Assert.That(entity, Has.Property(nameof(entity.LastChecked)).EqualTo(lastChecked));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExtension_WhenExtensionExists()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "OriginalName",
            Version = "1.0.0",
            FilePath = "/path/to/original.dll",
            AssemblyName = "OriginalName, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new Extension
        {
            Id = extension.Id,
            Name = "UpdatedName",
            Version = "2.0.0",
            TargetABI = "2.0.0-3.0.0",
            FilePath = "/path/to/updated.dll",
            AssemblyName = "UpdatedName, Version=2.0.0.0",
            Enabled = false,
            DisabledReason = "Disabled for testing",
            LoadOrder = 200,
            Managed = true,
            NuGetId = "UpdatedName.Package",
            PackageRepository = "https://nuget.example.com",
            AutoUpgrade = true,
            LastChecked = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Name)).EqualTo("UpdatedName"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Version)).EqualTo("2.0.0"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Enabled)).False);
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.DisabledReason))
                    .EqualTo("Disabled for testing"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.LoadOrder)).EqualTo(200));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.Managed)).True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Extension
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Version = "1.0.0",
            FilePath = "/path/to/nonexistent.dll",
            AssemblyName = "NonExistent, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesExtension_WhenExtensionExists()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "ToDelete",
            Version = "1.0.0",
            FilePath = "/path/to/delete.dll",
            AssemblyName = "ToDelete, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(extension.Id, _userId).ConfigureAwait(false);

        // Assert
        var entityExists = await _context.Extensions.AnyAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSettings_WhenExtensionHasSettings()
    {
        // Arrange
        var extension = new Extension
        {
            Id = Guid.NewGuid(),
            Name = "ConfiguredExtension",
            Version = "1.0.0",
            FilePath = "/path/to/configured.dll",
            AssemblyName = "ConfiguredExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        var settingsGroup = new SettingsGroup
        {
            Id = Guid.NewGuid(),
            Name = "ConfiguredExtensionSettings",
            ExtensionId = extension.Id,
            CreatedAt = DateTime.UtcNow
        };
        var setting = new Setting
        {
            Id = Guid.NewGuid(),
            SettingsGroupId = settingsGroup.Id,
            Key = "api-key",
            Value = "secret",
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        _context.SettingsGroups.Add(settingsGroup);
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(extension.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                await _context.Extensions.AnyAsync(e => e.Id == extension.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.SettingsGroups.AnyAsync(group => group.Id == settingsGroup.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.Settings.AnyAsync(value => value.Id == setting.Id).ConfigureAwait(false),
                Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_PatchesNuGetIdField_WhenNuGetIdIsSet()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ExtensionPatch
        {
            Id = extension.Id,
            NuGetId = new ScalarPatch<string?> { Value = "TestExtension.Package" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.NuGetId)).EqualTo("TestExtension.Package"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesAutoUpgradeField_WhenAutoUpgradeIsSet()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ExtensionPatch
        {
            Id = extension.Id,
            AutoUpgrade = new ScalarPatch<bool> { Value = true }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.AutoUpgrade)).True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesPackageRepositoryField_WhenPackageRepositoryIsSet()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ExtensionPatch
        {
            Id = extension.Id,
            PackageRepository = new ScalarPatch<string?> { Value = "https://custom.nuget.org" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.PackageRepository))
                    .EqualTo("https://custom.nuget.org"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesMultipleFields_WhenSeveralFieldsAreSet()
    {
        // Arrange
        var extension = new Extension
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Extensions.Add(extension);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ExtensionPatch
        {
            Id = extension.Id,
            NuGetId = new ScalarPatch<string?> { Value = "MultiPatch.Package" },
            PackageRepository = new ScalarPatch<string?> { Value = "https://multi.nuget.org" },
            AutoUpgrade = new ScalarPatch<bool> { Value = true },
            LoadOrder = new ScalarPatch<int> { Value = 42 }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == extension.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.NuGetId)).EqualTo("MultiPatch.Package"));
            Assert.That(
                updatedEntity,
                Has.Property(nameof(updatedEntity.PackageRepository))
                    .EqualTo("https://multi.nuget.org"));
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.AutoUpgrade)).True);
            Assert.That(updatedEntity, Has.Property(nameof(updatedEntity.LoadOrder)).EqualTo(42));
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("extension.updated"));
            Assert.That(
                _publishedEvents[0].Context["extension.id"],
                Is.EqualTo(extension.Id.ToString("D")));
            Assert.That(
                _publishedEvents[0].Context["extension.changedFields"],
                Is.EqualTo("NuGetId,PackageRepository,AutoUpgrade,LoadOrder"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new ExtensionPatch
        {
            Id = Guid.NewGuid(),
            NuGetId = new ScalarPatch<string?> { Value = "NonExistent.Package" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WriteAsync_AuditsExtensionAndSettings_WhenLifecycleChanges()
    {
        // Arrange
        var model = new Extension
        {
            Name = "Audited",
            Version = "1.0",
            AssemblyName = "Audited",
            FilePath = "/test/extension.dll"
        };

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);
        await _subject.UpdateAsync(model with { Enabled = false }, _userId).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        await _subject.PatchAsync(new() { Id = model.Id, LoadOrder = new() { Value = 7 } }, _userId)
            .ConfigureAwait(false);
        var group = new SettingsGroup { ExtensionId = model.Id, Name = "settings" };
        group.Settings.Add(new Setting { Key = "token", Value = "never-audit-this-value" });
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _subject.DeleteAsync(model.Id, _userId).ConfigureAwait(false);
        var audits = await _context.AuditRecords.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(audits.Where(a => a.EntityType == nameof(Extension)).Select(a => a.Operation),
                Is.EquivalentTo(["Create", "Update", "Patch", "Delete"]));
            Assert.That(audits.Count(a => a.EntityType == nameof(Setting) && a.Operation == "Delete"), Is.EqualTo(1));
            Assert.That(audits.Count(a => a.EntityType == nameof(SettingsGroup) && a.Operation == "Delete"),
                Is.EqualTo(1));
            Assert.That(audits.Select(a => a.Detail), Has.None.Contains("never-audit-this-value"));
        }
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_RefreshesTheAssemblyValues_WhenThePackageHasNoVersion()
    {
        // Arrange
        _context.Extensions.AddRange(
            new Extension
            {
                Id = Guid.NewGuid(),
                PackageId = "Magpie.Test.Core",
                Name = "Old name",
                Version = "1.2.3",
                TargetABI = null,
                FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
                AssemblyName = "Magpie.Test.Core",
                Installed = true,
                Enabled = true,
            },
            new Extension
            {
                Id = Guid.NewGuid(),
                PackageId = "Magpie.Extensions.NotLoaded",
                Name = "Not loaded",
                Version = "1.0.0",
                FilePath = "/app/data/extensions/not-loaded/Magpie.Extensions.NotLoaded.dll",
                AssemblyName = "Magpie.Extensions.NotLoaded",
                Installed = true,
                Enabled = true,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RegisterLoadedExtensionsAsync([], [new LoadedExtension()], new Dictionary<Guid, string>())
            .ConfigureAwait(false);

        // Assert
        var refreshed = await _context.Extensions.AsNoTracking()
            .SingleAsync(extension => extension.AssemblyName == "Magpie.Test.Core")
            .ConfigureAwait(false);
        var notLoaded = await _context.Extensions.AsNoTracking()
            .SingleAsync(extension => extension.AssemblyName == "Magpie.Extensions.NotLoaded")
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed.Name, Is.EqualTo("Loaded name"));
            Assert.That(refreshed.Version, Is.EqualTo("1.2.4"));
            Assert.That(refreshed.TargetABI, Is.EqualTo("[1.4.0,1.6.0)"));
            Assert.That(notLoaded.Name, Is.EqualTo("Not loaded"));
            Assert.That(notLoaded.Version, Is.EqualTo("1.0.0"));
            Assert.That(notLoaded.TargetABI, Is.Null);
        }
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_KeepsTheExistingRowAndId_WhenAnotherNodeRegisteredThePackage()
    {
        // Arrange
        var winnerId = new Guid("00000000-0000-0000-0000-0000000000a1");
        _context.Extensions.Add(
            new Extension
            {
                Id = winnerId,
                PackageId = "Magpie.Test.Core",
                Name = "Winner",
                Version = "1.0.0",
                FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
                AssemblyName = "Magpie.Test.Core",
                Installed = true,
                Enabled = true,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var loser = new Extension
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000b2"),
            PackageId = "Magpie.Test.Core",
            Name = "Loser",
            Version = "1.0.0",
            FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
            AssemblyName = "Magpie.Test.Core",
            Installed = true,
            Enabled = true,
        };

        // Act
        await _subject.RegisterLoadedExtensionsAsync([loser], [new LoadedExtension()], new Dictionary<Guid, string>())
            .ConfigureAwait(false);

        // Assert
        var rows = await _context.Extensions.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Id, Is.EqualTo(winnerId));
            Assert.That(loser.Id, Is.EqualTo(winnerId));
        }
    }

    [Test]
    public async Task TrySetLifecycleVersionAsync_ClaimsOnceAndResets_WhenTwoNodesClaimTheSameVersion()
    {
        // Arrange
        var extensionId = new Guid("00000000-0000-0000-0000-0000000000d4");
        _context.Extensions.Add(
            new Extension
            {
                Id = extensionId,
                PackageId = "Magpie.Test.Core",
                Name = "Test extension",
                Version = "1.0.1",
                FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
                AssemblyName = "Magpie.Test.Core",
                Installed = true,
                Enabled = true,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var firstClaim = await _subject.TrySetLifecycleVersionAsync(extensionId, null, "1.0.1").ConfigureAwait(false);
        var secondClaim = await _subject.TrySetLifecycleVersionAsync(extensionId, null, "1.0.1").ConfigureAwait(false);
        var reset = await _subject.TrySetLifecycleVersionAsync(extensionId, "1.0.1", null).ConfigureAwait(false);

        // Assert
        var stored = await _context.Extensions.AsNoTracking().SingleAsync(e => e.Id == extensionId)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstClaim, Is.True);
            Assert.That(secondClaim, Is.False);
            Assert.That(reset, Is.True);
            Assert.That(stored.LifecycleVersion, Is.Null);
        }
    }

    [TestCase(null, null, "1.0.1", true, "1.0.1")]
    [TestCase("", null, "1.0.1", true, "1.0.1")]
    [TestCase("1.0.1", null, "1.0.1", false, "1.0.1")]
    [TestCase("1.0.0", "1.0.0", "1.0.1", true, "1.0.1")]
    [TestCase("1.0.1", "1.0.0", "1.0.1", false, "1.0.1")]
    [TestCase("1.0.1", "1.0.1", null, true, null)]
    public async Task TrySetLifecycleVersionAsync_ChangesTheVersion_WhenTheStoredVersionIsTheExpectedVersion(
        string? storedVersion,
        string? expectedVersion,
        string? version,
        bool expectedResult,
        string? expectedStoredVersion)
    {
        // Arrange
        var extensionId = new Guid("00000000-0000-0000-0000-0000000000c3");
        _context.Extensions.Add(
            new Extension
            {
                Id = extensionId,
                PackageId = "Magpie.Test.Core",
                Name = "Test extension",
                Version = "1.0.1",
                LifecycleVersion = storedVersion,
                FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
                AssemblyName = "Magpie.Test.Core",
                Installed = true,
                Enabled = true,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.TrySetLifecycleVersionAsync(extensionId, expectedVersion, version)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.Extensions.AsNoTracking().SingleAsync(e => e.Id == extensionId)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expectedResult));
            Assert.That(stored.LifecycleVersion, Is.EqualTo(expectedStoredVersion));
        }
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task RegisterLoadedExtensionsAsync_KeepsTheStateAndSettings_WhenTheRowIsRefreshed(
        bool installed,
        bool enabled)
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var group = new SettingsGroup { Name = "General", ExtensionId = extensionId };
        group.Settings.Add(new() { Key = "Endpoint", Value = "https://example.test" });
        _context.Extensions.Add(new Extension
        {
            Id = extensionId,
            PackageId = "Magpie.Test.Core",
            Name = "Old name",
            Version = "1.2.3",
            LifecycleVersion = "1.2.3",
            FilePath = "/app/data/extensions/loaded/Magpie.Test.Core.dll",
            AssemblyName = "Magpie.Test.Core",
            Installed = installed,
            Enabled = enabled,
            DisabledReason = "Kept reason",
            LoadOrder = 7,
            Managed = true,
            NuGetId = "Magpie.Test.Core",
            PackageRepository = "https://packages.example.test",
            AutoUpgrade = true,
        });
        _context.SettingsGroups.Add(group);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RegisterLoadedExtensionsAsync([], [new LoadedExtension()], new Dictionary<Guid, string>())
            .ConfigureAwait(false);

        // Assert
        var row = await _context.Extensions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var setting = await _context.Settings.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Enabled, Is.EqualTo(enabled));
            Assert.That(row.Installed, Is.EqualTo(installed));
            Assert.That(row.DisabledReason, Is.EqualTo("Kept reason"));
            Assert.That(row.LoadOrder, Is.EqualTo(7));
            Assert.That(row.Managed, Is.True);
            Assert.That(row.NuGetId, Is.EqualTo("Magpie.Test.Core"));
            Assert.That(row.PackageRepository, Is.EqualTo("https://packages.example.test"));
            Assert.That(row.AutoUpgrade, Is.True);
            Assert.That(row.LifecycleVersion, Is.EqualTo("1.2.3"));
            Assert.That(row.FilePath, Is.EqualTo("/app/data/extensions/loaded/Magpie.Test.Core.dll"));
            Assert.That(setting.Key, Is.EqualTo("Endpoint"));
            Assert.That(setting.Value, Is.EqualTo("https://example.test"));
        }
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_CreatesNoRow_WhenALoadedExtensionHasNoRow()
    {
        // Arrange
        _context.Extensions.Add(new Extension
        {
            Id = Guid.NewGuid(),
            PackageId = "Magpie.Extensions.NotLoaded",
            Name = "Not loaded",
            Version = "1.0.0",
            FilePath = "/app/data/extensions/not-loaded/Magpie.Extensions.NotLoaded.dll",
            AssemblyName = "Magpie.Extensions.NotLoaded",
            Installed = true,
            Enabled = true,
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RegisterLoadedExtensionsAsync([], [new LoadedExtension()], new Dictionary<Guid, string>())
            .ConfigureAwait(false);

        // Assert
        var assemblyNames = await _context.Extensions.AsNoTracking()
            .Select(extension => extension.AssemblyName)
            .ToListAsync()
            .ConfigureAwait(false);
        Assert.That(assemblyNames, Is.EqualTo(["Magpie.Extensions.NotLoaded"]));
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_StoresThePackageVersion_WhenThePackageHasAVersion()
    {
        // Arrange
        var extensionId = new Guid("33333333-3333-3333-3333-333333333333");
        _context.Extensions.Add(new Extension
        {
            Id = extensionId,
            PackageId = "Magpie.Test.Core",
            Name = "Old name",
            Version = "1.2.3",
            FilePath = "/app/data/extensions/Magpie.Test.Core/lib/net10.0/Magpie.Test.Core.dll",
            AssemblyName = "Magpie.Test.Core",
            Installed = true,
            Enabled = true,
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RegisterLoadedExtensionsAsync(
            [],
            [new LoadedExtension()],
            new Dictionary<Guid, string> { [extensionId] = "1.2.4-rc.1" }).ConfigureAwait(false);

        // Assert
        var row = await _context.Extensions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        Assert.That(row.Version, Is.EqualTo("1.2.4-rc.1"));
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_AddsTheDiscoveredRowWithPackageValues_WhenTheInstanceIsNew()
    {
        // Arrange
        var discovered = new Extension
        {
            Id = new Guid("44444444-4444-4444-4444-444444444444"),
            PackageId = "Magpie.Test.Core",
            Name = "Magpie.Test.Core",
            Version = "2.0.0",
            FilePath = "/app/data/extensions/Magpie.Test.Core/lib/net10.0/Magpie.Test.Core.dll",
            AssemblyName = "Magpie.Test.Core",
            Installed = true,
            Enabled = true,
            Managed = true,
            NuGetId = "Magpie.Test.Core",
        };

        // Act
        await _subject.RegisterLoadedExtensionsAsync(
            [discovered],
            [new LoadedExtension()],
            new Dictionary<Guid, string> { [discovered.Id] = "2.0.0" }).ConfigureAwait(false);

        // Assert
        var row = await _context.Extensions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.PackageId, Is.EqualTo("Magpie.Test.Core"));
            Assert.That(row.NuGetId, Is.EqualTo("Magpie.Test.Core"));
            Assert.That(row.Managed, Is.True);
            Assert.That(row.Name, Is.EqualTo("Loaded name"));
            Assert.That(row.Version, Is.EqualTo("2.0.0"));
            Assert.That(row.TargetABI, Is.EqualTo("[1.4.0,1.6.0)"));
            Assert.That(row.Enabled, Is.True);
        }
    }

    [Test]
    public async Task RegisterLoadedExtensionsAsync_AddsNoRow_WhenNothingIsDiscovered()
    {
        // Arrange
        // Act
        await _subject.RegisterLoadedExtensionsAsync([], [new LoadedExtension()], new Dictionary<Guid, string>())
            .ConfigureAwait(false);

        // Assert
        Assert.That(await _context.Extensions.AsNoTracking().AnyAsync().ConfigureAwait(false), Is.False);
    }

    private sealed class LoadedExtension : IExtension
    {
        public Guid Id => Guid.Empty;

        public Version Version => new(1, 2, 4);

        public string Name => "Loaded name";

        public string? TargetABI => "[1.4.0,1.6.0)";

        public void RegisterServices(IServiceCollection services)
        {
        }
    }
}
