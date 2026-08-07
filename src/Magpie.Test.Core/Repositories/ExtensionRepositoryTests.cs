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
using Magpie.Models.Patch;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ExtensionRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IExtensionRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
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

        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MagpieEvent, CancellationToken>((magpieEvent, _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new ExtensionRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            eventBus.Object,
            NullLoggerFactory.Instance.CreateLogger<ExtensionRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
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
            Assert.That(_publishedEvents[0].Name, Is.EqualTo(MagpieEvents.Names.ExtensionUpdated));
            Assert.That(
                _publishedEvents[0].Context[MagpieEvents.Keys.ExtensionId],
                Is.EqualTo(extension.Id.ToString("D")));
            Assert.That(
                _publishedEvents[0].Context[MagpieEvents.Keys.ExtensionChangedFields],
                Is.EqualTo("nuGetId,packageRepository,autoUpgrade,loadOrder"));
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
}
