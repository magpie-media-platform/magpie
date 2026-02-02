using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class MetadataContainerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMetadataContainerRepository _subject = null!;
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

        _subject = new MetadataContainerRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<MetadataContainerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetContainerAsync_ReturnsContainer_WhenExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetContainerAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(container.Id));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task GetContainerAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetContainerAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetContainerAsync_ReturnsContainerWithContributions()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "TestProvider",
            Title = "Test Title",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetContainerAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Contributions)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Contributions))
                .With.One.With.Property(nameof(MetadataContribution.ProviderType)).EqualTo("TestProvider"));
            Assert.That(result, Has.Property(nameof(result.Contributions))
                .With.One.With.Property(nameof(MetadataContribution.Title)).EqualTo("Test Title"));
        }
    }

    [Test]
    public async Task GetContainerAsync_ReturnsContributionsWithNestedCollections()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "TestProvider",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync();

        var alternateText = new AlternateText
        {
            MetadataContributionId = contribution.Id,
            Kind = "title",
            Text = "Alternate Title"
        };
        _context.AlternateTexts.Add(alternateText);

        var genre = new Genre
        {
            MetadataContributionId = contribution.Id,
            Name = "Action"
        };
        _context.Genres.Add(genre);

        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetContainerAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            var contributions = result!.Contributions.ToList();
            Assert.That(contributions, Has.Exactly(1).Items);
            Assert.That(contributions, Has.One.With.Property(nameof(MetadataContribution.AlternateTexts))
                .With.Exactly(1).Items);
            Assert.That(contributions, Has.One.With.Property(nameof(MetadataContribution.Genres))
                .With.Exactly(1).Items);
        }
    }

    [Test]
    public async Task AddContributionAsync_AddsContribution()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            ProviderType = "TestProvider",
            Title = "New Title",
            Overview = "Test Overview"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, _userId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.MetadataContainerId)).EqualTo(container.Id));
            Assert.That(result, Has.Property(nameof(result.ProviderType)).EqualTo("TestProvider"));
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("New Title"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task AddContributionAsync_CreatesAuditRecord()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            ProviderType = "AuditProvider"
        };

        // Act
        await _subject.AddContributionAsync(container.Id, contribution, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("MetadataContribution"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public void AddContributionAsync_ThrowsInvalidOperationException_WhenContainerNotExists()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            ProviderType = "TestProvider"
        };

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.AddContributionAsync(Guid.NewGuid(), contribution, _userId));
    }

    [Test]
    public async Task AddContributionAsync_SetsCreatedByUserId_WhenProvided()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            ProviderType = "TestProvider"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, _userId);

        // Assert
        Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
    }

    [Test]
    public async Task AddContributionAsync_AllowsNullUserId()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            ProviderType = "SystemProvider"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, null);

        // Assert
        Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).Null);
    }

    [Test]
    public async Task RemoveContributionAsync_RemovesContribution_WhenExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "TestProvider",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.RemoveContributionAsync(container.Id, contribution.Id, _userId);

        // Assert

        var deleted = await _context.MetadataContributions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contribution.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task RemoveContributionAsync_ReturnsFalse_WhenContributionNotExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.RemoveContributionAsync(container.Id, Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RemoveContributionAsync_ReturnsFalse_WhenContainerNotExists()
    {
        // Act
        var result = await _subject.RemoveContributionAsync(Guid.NewGuid(), Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RemoveContributionAsync_ReturnsFalse_WhenContributionBelongsToDifferentContainer()
    {
        // Arrange
        var container1 = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        var container2 = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            ProviderType = "TestProvider",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.RemoveContributionAsync(container2.Id, contribution.Id, _userId);

        // Assert
        Assert.That(result, Is.False);

        var stillExists = await _context.MetadataContributions.FindAsync(contribution.Id);
        Assert.That(stillExists, Is.Not.Null);
    }

    [Test]
    public async Task RemoveContributionAsync_CreatesAuditRecord()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "AuditDeleteProvider",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync();

        // Act
        await _subject.RemoveContributionAsync(container.Id, contribution.Id, _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType))
                .EqualTo("MetadataContribution"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task RemoveContributionAsync_DoesNotCreateAuditRecord_WhenContributionNotExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        await _subject.RemoveContributionAsync(container.Id, Guid.NewGuid(), _userId);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync();
        Assert.That(auditRecords, Is.Empty);
    }
}
