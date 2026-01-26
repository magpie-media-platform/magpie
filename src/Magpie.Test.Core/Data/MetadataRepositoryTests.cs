using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Metadata;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using MetadataContainerEntity = Magpie.Data.Database.Entities.MetadataContainer;
using MetadataContributionEntity = Magpie.Data.Database.Entities.MetadataContribution;
using SubjectEntity = Magpie.Data.Database.Entities.Subject;
using UserEntity = Magpie.Data.Database.Entities.User;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class MetadataRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMetadataRepository _repository = null!;
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

        _repository = new MetadataRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<MetadataRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private MetadataContainerEntity CreateContainer()
    {
        var container = new MetadataContainerEntity
        {
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContainers.Add(container);
        _context.SaveChanges();
        return container;
    }

    private SubjectEntity CreateSubject()
    {
        var subject = new SubjectEntity
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(subject);
        _context.SaveChanges();
        return subject;
    }

    [Test]
    public async Task GetContainerAsync_ReturnsContainer()
    {
        // Arrange
        var container = CreateContainer();

        // Act
        var result = await _repository.GetContainerAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MetadataContainer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MetadataContainer.Id)).EqualTo(container.Id));
    }

    [Test]
    public async Task GetContainerAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _repository.GetContainerAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MetadataContainer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetContainerAsync_IncludesContributions()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "TestProvider",
            Title = "Test Title",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        // Act
        var result = await _repository.GetContainerAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MetadataContainer?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MetadataContainer.Contributions))
                .With.Count.EqualTo(1));
    }

    [Test]
    public async Task AddContributionAsync_AddsContribution()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContribution(
            Guid.Empty,
            0,
            false,
            "TestProvider",
            DateTimeOffset.UtcNow,
            null,
            null,
            "Test Title",
            null,
            null,
            null,
            "Test Overview",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ImmutableDictionary<string, string?>.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        // Act
        var result = await _repository.AddContributionAsync(container.Id, contribution, _userId);

        // Assert
        var entity = await _context.MetadataContributions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MetadataContainerId == container.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MetadataContribution>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MetadataContribution.Title))
                    .EqualTo("Test Title"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Provider, Is.EqualTo("TestProvider"));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MetadataContribution"));
        }
    }

    [Test]
    public async Task AddContributionAsync_Fails_WhenContainerNotFound()
    {
        // Arrange
        var contribution = new MetadataContribution(
            Guid.Empty,
            0,
            false,
            "TestProvider",
            DateTimeOffset.UtcNow,
            null,
            null,
            "Test Title",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ImmutableDictionary<string, string?>.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        // Act
        var result = await _repository.AddContributionAsync(Guid.NewGuid(), contribution, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<MetadataContribution>>());
    }

    [Test]
    public async Task AddContributionAsync_AddsCollectionEntities()
    {
        // Arrange
        var container = CreateContainer();
        var subject = CreateSubject();

        var contribution = new MetadataContribution(
            Guid.Empty,
            0,
            false,
            "TestProvider",
            DateTimeOffset.UtcNow,
            null,
            null,
            "Test Title",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string?> { ["testKey"] = "testValue" },
            [new ExternalIdMetadata(Guid.Empty, 0, "ext-123", "Test")],
            null,
            [new RelationshipMetadata(Guid.Empty, subject.Id, 0, "Actor", "Character")],
            null,
            null,
            null,
            null,
            null,
            [new GenreMetadata(Guid.Empty, 0, "Drama")]);

        // Act
        var result = await _repository.AddContributionAsync(container.Id, contribution, _userId);

        // Assert
        var entity = await _context.MetadataContributions
            .AsNoTracking()
            .Include(c => c.CustomMetadataValues)
            .Include(c => c.ExternalIds)
            .Include(c => c.Relationships)
            .Include(c => c.Genres)
            .FirstOrDefaultAsync(c => c.MetadataContainerId == container.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MetadataContribution>>());
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.CustomMetadataValues, Has.Count.EqualTo(1));
            Assert.That(entity.ExternalIds, Has.Count.EqualTo(1));
            Assert.That(entity.Relationships, Has.Count.EqualTo(1));
            Assert.That(entity.Genres, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PatchContributionAsync_PatchesScalarFields()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Locked = false,
            Provider = "OriginalProvider",
            Title = "Original Title",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        var patch = new MetadataContributionPatch
        {
            Id = contribution.Id,
            Title = new("Updated Title"),
            Ordinal = new(1),
            Locked = new(true)
        };

        // Act
        var result = await _repository.PatchContributionAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.MetadataContributions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contribution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.Title, Is.EqualTo("Updated Title"));
            Assert.That(updatedEntity.Ordinal, Is.EqualTo(1));
            Assert.That(updatedEntity.Locked, Is.True);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MetadataContribution"));
        }
    }

    [Test]
    public async Task PatchContributionAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new MetadataContributionPatch
        {
            Id = Guid.NewGuid(),
            Title = new("Updated Title")
        };

        // Act
        var result = await _repository.PatchContributionAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task PatchContributionAsync_PatchesCollections()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "TestProvider",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution.Genres.Add(new()
        {
            Ordinal = 0,
            GenreName = "ExistingGenre"
        });
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        var existingGenreId = contribution.Genres.First().Id;

        var patch = new MetadataContributionPatch
        {
            Id = contribution.Id,
            Genres = new(
                Add: [new GenreMetadata(Guid.Empty, 1, "NewGenre")],
                Remove: null,
                Update: [new GenreMetadataPatch { Id = existingGenreId, Genre = new("UpdatedGenre") }])
        };

        // Act
        var result = await _repository.PatchContributionAsync(patch, _userId);

        // Assert
        var genres = await _context.Genres
            .AsNoTracking()
            .Where(g => g.MetadataContributionId == contribution.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(genres, Has.Count.EqualTo(2));
            Assert.That(genres, Has.One.With.Property("GenreName").EqualTo("UpdatedGenre"));
            Assert.That(genres, Has.One.With.Property("GenreName").EqualTo("NewGenre"));
        }
    }

    [Test]
    public async Task PatchContributionAsync_RemovesCollectionItems()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "TestProvider",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution.Genres.Add(new()
        {
            Ordinal = 0,
            GenreName = "ToRemove"
        });
        contribution.Genres.Add(new()
        {
            Ordinal = 1,
            GenreName = "ToKeep"
        });
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        var genreToRemoveId = contribution.Genres.First(g => g.GenreName == "ToRemove").Id;

        var patch = new MetadataContributionPatch
        {
            Id = contribution.Id,
            Genres = new(
                Add: null,
                Remove: [genreToRemoveId],
                Update: null)
        };

        // Act
        var result = await _repository.PatchContributionAsync(patch, _userId);

        // Assert
        var genres = await _context.Genres
            .AsNoTracking()
            .Where(g => g.MetadataContributionId == contribution.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(genres, Has.Count.EqualTo(1));
            Assert.That(genres, Has.One.With.Property("GenreName").EqualTo("ToKeep"));
        }
    }

    [Test]
    public async Task DeleteContributionAsync_DeletesContribution()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Locked = false,
            Provider = "TestProvider",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        // Act
        var result = await _repository.DeleteContributionAsync(contribution.Id, _userId);

        // Assert
        var entityExists = await _context.MetadataContributions.AnyAsync(c => c.Id == contribution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("MetadataContribution"));
        }
    }

    [Test]
    public async Task DeleteContributionAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _repository.DeleteContributionAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteContributionAsync_Fails_WhenLocked()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Locked = true,
            Provider = "TestProvider",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        // Act
        var result = await _repository.DeleteContributionAsync(contribution.Id, _userId);

        // Assert
        var entityExists = await _context.MetadataContributions.AnyAsync(c => c.Id == contribution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<FailureResult<bool>>());
            Assert.That(entityExists, Is.True);
        }
    }

    [Test]
    public async Task DeleteContributionAsync_CascadesToCollections()
    {
        // Arrange
        var container = CreateContainer();
        var contribution = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Locked = false,
            Provider = "TestProvider",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution.Genres.Add(new()
        {
            Ordinal = 0,
            GenreName = "TestGenre"
        });
        contribution.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Value = "ext-123"
        });
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();

        var contributionId = contribution.Id;

        // Act
        var result = await _repository.DeleteContributionAsync(contributionId, _userId);

        // Assert
        var genresExist = await _context.Genres.AnyAsync(g => g.MetadataContributionId == contributionId);
        var externalIdsExist = await _context.ExternalIds.AnyAsync(e => e.MetadataContributionId == contributionId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(genresExist, Is.False);
            Assert.That(externalIdsExist, Is.False);
        }
    }
}
