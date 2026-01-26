using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Extensions;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using ExtensionEntity = Magpie.Data.Database.Entities.Extension;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class ExtensionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IExtensionRepository _subject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new ExtensionRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<ExtensionRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsExtension()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/extension.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Name)).EqualTo("TestExtension")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Version)).EqualTo("1.0.0")
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Enabled)).True);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllExtensions()
    {
        // Arrange
        var entity1 = new ExtensionEntity
        {
            Name = "Extension1",
            Version = "1.0.0",
            FilePath = "/path/to/ext1.dll",
            AssemblyName = "Extension1, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ExtensionEntity
        {
            Name = "Extension2",
            Version = "2.0.0",
            FilePath = "/path/to/ext2.dll",
            AssemblyName = "Extension2, Version=2.0.0.0",
            Enabled = false,
            LoadOrder = 200,
            Managed = true,
            AutoUpgrade = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Extension>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoExtensions()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Extension>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsExtension()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "UniqueExtension",
            Version = "1.0.0",
            FilePath = "/path/to/unique.dll",
            AssemblyName = "UniqueExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueExtension");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Name)).EqualTo("UniqueExtension"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByAssemblyNameAsync_ReturnsExtension()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension.Assembly, Version=1.0.0.0, Culture=neutral",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByAssemblyNameAsync("TestExtension.Assembly, Version=1.0.0.0, Culture=neutral");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.AssemblyName))
            .EqualTo("TestExtension.Assembly, Version=1.0.0.0, Culture=neutral"));
    }

    [Test]
    public async Task GetByAssemblyNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByAssemblyNameAsync("NonExistent.Assembly");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Extension?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEnabledExtensions()
    {
        // Arrange
        var entity1 = new ExtensionEntity
        {
            Name = "EnabledExt1",
            Version = "1.0.0",
            FilePath = "/path/to/enabled1.dll",
            AssemblyName = "EnabledExt1, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ExtensionEntity
        {
            Name = "EnabledExt2",
            Version = "1.0.0",
            FilePath = "/path/to/enabled2.dll",
            AssemblyName = "EnabledExt2, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 200,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ExtensionEntity
        {
            Name = "DisabledExt",
            Version = "1.0.0",
            FilePath = "/path/to/disabled.dll",
            AssemblyName = "DisabledExt, Version=1.0.0.0",
            Enabled = false,
            LoadOrder = 300,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Extension>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All.With.Property(nameof(Extension.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEnabledAsync_ReturnsEmptyList_WhenNoEnabledExtensions()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "DisabledExt",
            Version = "1.0.0",
            FilePath = "/path/to/disabled.dll",
            AssemblyName = "DisabledExt, Version=1.0.0.0",
            Enabled = false,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetEnabledAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Extension>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetManagedAsync_ReturnsManagedExtensions()
    {
        // Arrange
        var entity1 = new ExtensionEntity
        {
            Name = "ManagedExt1",
            Version = "1.0.0",
            FilePath = "/path/to/managed1.dll",
            AssemblyName = "ManagedExt1, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            NuGetId = "ManagedExt1.Package",
            AutoUpgrade = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new ExtensionEntity
        {
            Name = "ManagedExt2",
            Version = "2.0.0",
            FilePath = "/path/to/managed2.dll",
            AssemblyName = "ManagedExt2, Version=2.0.0.0",
            Enabled = false,
            LoadOrder = 200,
            Managed = true,
            NuGetId = "ManagedExt2.Package",
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new ExtensionEntity
        {
            Name = "UnmanagedExt",
            Version = "1.0.0",
            FilePath = "/path/to/unmanaged.dll",
            AssemblyName = "UnmanagedExt, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 300,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetManagedAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Extension>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(2));
            Assert.That(success.Value, Has.All.With.Property(nameof(Extension.Managed)).True);
        }
    }

    [Test]
    public async Task GetManagedAsync_ReturnsEmptyList_WhenNoManagedExtensions()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "UnmanagedExt",
            Version = "1.0.0",
            FilePath = "/path/to/unmanaged.dll",
            AssemblyName = "UnmanagedExt, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetManagedAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Extension>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesExtension()
    {
        // Arrange
        var model = new Extension(
            Guid.Empty,
            "1.0.0",
            "NewExtension",
            "1.0.0-2.0.0",
            "/path/to/new.dll",
            "NewExtension, Version=1.0.0.0",
            true,
            null,
            100,
            false,
            null,
            null,
            false,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from e in _context.Extensions.AsNoTracking()
            where e.Name == "NewExtension"
            select e).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Extension>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Name)).EqualTo("NewExtension")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Version)).EqualTo("1.0.0")
                .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(Extension.Enabled)).True);
            Assert.That(entity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.Name)).EqualTo("NewExtension")
                .And.Property(nameof(ExtensionEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Extension")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesExtensionWithAllFields()
    {
        // Arrange
        var lastChecked = DateTimeOffset.UtcNow.AddDays(-1);
        var model = new Extension(
            Guid.Empty,
            "2.0.0",
            "FullExtension",
            "1.0.0-3.0.0",
            "/path/to/full.dll",
            "FullExtension, Version=2.0.0.0",
            true,
            null,
            150,
            true,
            "FullExtension.Package",
            "https://nuget.example.com",
            true,
            lastChecked,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from e in _context.Extensions.AsNoTracking()
            where e.Name == "FullExtension"
            select e).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Extension>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Version, Is.EqualTo("2.0.0"));
            Assert.That(entity.TargetABI, Is.EqualTo("1.0.0-3.0.0"));
            Assert.That(entity.LoadOrder, Is.EqualTo(150));
            Assert.That(entity.Managed, Is.True);
            Assert.That(entity.NuGetId, Is.EqualTo("FullExtension.Package"));
            Assert.That(entity.PackageRepository, Is.EqualTo("https://nuget.example.com"));
            Assert.That(entity.AutoUpgrade, Is.True);
            Assert.That(entity.LastChecked, Is.EqualTo(lastChecked));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesExtension()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "OriginalName",
            Version = "1.0.0",
            FilePath = "/path/to/original.dll",
            AssemblyName = "OriginalName, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var model = new Extension(
            entity.Id,
            "2.0.0",
            "UpdatedName",
            "2.0.0-3.0.0",
            "/path/to/updated.dll",
            "UpdatedName, Version=2.0.0.0",
            false,
            "Disabled for testing",
            200,
            true,
            "UpdatedName.Package",
            "https://nuget.example.com",
            true,
            DateTimeOffset.UtcNow,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from e in _context.Extensions.AsNoTracking()
            where e.Id == entity.Id
            select e).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(ExtensionEntity.Version)).EqualTo("2.0.0")
                .And.Property(nameof(ExtensionEntity.Enabled)).False
                .And.Property(nameof(ExtensionEntity.DisabledReason)).EqualTo("Disabled for testing")
                .And.Property(nameof(ExtensionEntity.LoadOrder)).EqualTo(200)
                .And.Property(nameof(ExtensionEntity.Managed)).True
                .And.Property(nameof(ExtensionEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ExtensionEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Extension")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Extension(
            Guid.NewGuid(),
            "1.0.0",
            "NonExistent",
            null,
            "/path/to/nonexistent.dll",
            "NonExistent, Version=1.0.0.0",
            true,
            null,
            100,
            false,
            null,
            null,
            false,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchAsync_PatchesVersion()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var patch = new ExtensionPatch
        {
            Id = entity.Id,
            Version = new("2.0.0")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.Version)).EqualTo("2.0.0")
                .And.Property(nameof(ExtensionEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(ExtensionEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Extension")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesEnabled()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var patch = new ExtensionPatch
        {
            Id = entity.Id,
            Enabled = new(false)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.Enabled)).False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesDisabledReason()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = false,
            DisabledReason = null,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var patch = new ExtensionPatch
        {
            Id = entity.Id,
            DisabledReason = new("Incompatible with current version")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.DisabledReason)).EqualTo("Incompatible with current version"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesLoadOrder()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var patch = new ExtensionPatch
        {
            Id = entity.Id,
            LoadOrder = new(500)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.LoadOrder)).EqualTo(500));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesAutoUpgrade()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var patch = new ExtensionPatch
        {
            Id = entity.Id,
            AutoUpgrade = new(true)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.AutoUpgrade)).True);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new ExtensionPatch
        {
            Id = Guid.NewGuid(),
            Version = new("2.0.0")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_DeletesExtension()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "ToDelete",
            Version = "1.0.0",
            FilePath = "/path/to/delete.dll",
            AssemblyName = "ToDelete, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = false,
            AutoUpgrade = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Extensions.AnyAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Extension")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastCheckedAsync_UpdatesLastChecked()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = true,
            LastChecked = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        var lastChecked = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateLastCheckedAsync(entity.Id, lastChecked);

        // Assert
        var updatedEntity = await _context.Extensions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<ExtensionEntity>()
                .With.Property(nameof(ExtensionEntity.LastChecked)).EqualTo(lastChecked));
        }
    }

    [Test]
    public async Task UpdateLastCheckedAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateLastCheckedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastCheckedAsync_DoesNotCreateHistoryEntry()
    {
        // Arrange
        var entity = new ExtensionEntity
        {
            Name = "TestExtension",
            Version = "1.0.0",
            FilePath = "/path/to/test.dll",
            AssemblyName = "TestExtension, Version=1.0.0.0",
            Enabled = true,
            LoadOrder = 100,
            Managed = true,
            AutoUpgrade = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Extensions.Add(entity);
        _context.SaveChanges();

        // Act
        await _subject.UpdateLastCheckedAsync(entity.Id, DateTimeOffset.UtcNow);

        // Assert
        var historyEntries = await _context.HistoryEntries.ToListAsync();
        Assert.That(historyEntries, Is.Empty);
    }
}
