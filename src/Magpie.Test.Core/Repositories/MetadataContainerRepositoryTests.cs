using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class MetadataContainerRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMetadataContainerRepository _subject = null!;
    private IMaterializedMetadataRepository _materializedMetadata = null!;
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

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        var subjectRepository = Mock.Of<ISubjectRepository>();

        _subject = new MetadataContainerRepository(
            _context,
            auditRecordRepository,
            subjectRepository,
            transactionManager,
            loggerFactory.CreateLogger<MetadataContainerRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        _materializedMetadata = new MaterializedMetadataRepository(
            _context,
            loggerFactory.CreateLogger<MaterializedMetadataRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContainer_WhenExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(container.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(container.Id));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContainerWithContributions_WhenContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "TestProvider",
            Title = "Test Title",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(container.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Property(nameof(result.Contributions)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Contributions))
                    .With.One.With.Property(nameof(MetadataContribution.ProviderType)).EqualTo("TestProvider"));
            Assert.That(
                result,
                Has.Property(nameof(result.Contributions))
                    .With.One.With.Property(nameof(MetadataContribution.Title)).EqualTo("Test Title"));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNestedContributionCollections_WhenCollectionsExist()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "TestProvider",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

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

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(container.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            var contributions = result!.Contributions.ToList();
            Assert.That(contributions, Has.Exactly(1).Items);
            Assert.That(
                contributions,
                Has.One.With.Property(nameof(MetadataContribution.AlternateTexts))
                    .With.Exactly(1).Items);
            Assert.That(
                contributions,
                Has.One.With.Property(nameof(MetadataContribution.Genres))
                    .With.Exactly(1).Items);
        }
    }

    [Test]
    public async Task AddContributionAsync_ReturnsAddedContribution_WhenContainerExists()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            ProviderType = "TestProvider",
            Title = "New Title",
            Overview = "Test Overview"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, _userId).ConfigureAwait(false);

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
    public async Task UpdateContributionAsync_RoundTripsPreviews_WhenContributionWasPreviouslyAdded()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "With Trailer"
        };
        contribution.Previews.Add(new Preview
        {
            Kind = "trailer",
            Url = new Uri("https://www.youtube.com/watch?v=initial"),
            Source = "provider-a",
            Title = "Initial Trailer",
            Language = "en",
            Ordinal = 0
        });

        // Act
        var added = await _subject.AddContributionAsync(container.Id, contribution, _userId).ConfigureAwait(false);

        var afterAdd = await _subject.GetByIdAsync(container.Id).ConfigureAwait(false);

        var incoming = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "With Trailer"
        };
        incoming.Previews.Add(new Preview
        {
            Kind = "trailer",
            Url = new Uri("https://www.youtube.com/watch?v=official"),
            Source = "provider-a",
            Ordinal = 0
        });
        incoming.Previews.Add(new Preview
        {
            Kind = "teaser",
            Url = new Uri("https://www.youtube.com/watch?v=teaser"),
            Source = "provider-a",
            Ordinal = 1
        });

        await _subject.UpdateContributionAsync(container.Id, added.Id, incoming, _userId).ConfigureAwait(false);

        var afterUpdate = await _subject.GetByIdAsync(container.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var addedPreviews = afterAdd!.Contributions.Single().Previews.ToList();
            Assert.That(addedPreviews, Has.Exactly(1).Items);
            Assert.That(addedPreviews[0].Url, Is.EqualTo(new Uri("https://www.youtube.com/watch?v=initial")));

            var updatedPreviews = afterUpdate!.Contributions.Single().Previews
                .OrderBy(p => p.Ordinal).ToList();
            Assert.That(updatedPreviews, Has.Exactly(2).Items);
            Assert.That(updatedPreviews[0].Kind, Is.EqualTo("trailer"));
            Assert.That(updatedPreviews[0].Url, Is.EqualTo(new Uri("https://www.youtube.com/watch?v=official")));
            Assert.That(updatedPreviews[1].Kind, Is.EqualTo("teaser"));
        }
    }

    [Test]
    public async Task UpdateContributionAsync_ReplacesCollections_WhenFactsMoveToCustomMetadata()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var existing = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "Rivermist"
        };
        existing.AlternateTexts.Add(new() { Kind = "Name", Text = "Ava Rivermist", Ordinal = 0 });
        existing.AlternateTexts.Add(new() { Kind = "Gender", Text = "Female", Ordinal = 1 });
        existing.AlternateTexts.Add(new() { Kind = "Birthplace", Text = "Neverland", Ordinal = 2 });
        existing.CustomMetadata.Add(new() { Key = "astrology", Value = "Capricorn" });
        var added = await _subject.AddContributionAsync(container.Id, existing, _userId).ConfigureAwait(false);

        var incoming = new MetadataContribution
        {
            ProviderType = "provider-a",
            Title = "Rivermist"
        };
        incoming.AlternateTexts.Add(new() { Kind = "Name", Text = "Ava Rivermist", Ordinal = 0 });
        incoming.CustomMetadata.Add(new() { Key = "gender", Value = "Female" });
        incoming.CustomMetadata.Add(new() { Key = "birthplace", Value = "Australia" });
        incoming.CustomMetadata.Add(new() { Key = "astrology", Value = "Capricorn" });

        // Act
        await _subject.UpdateContributionAsync(container.Id, added.Id, incoming, _userId).ConfigureAwait(false);
        var refreshed = (await _subject.GetByIdAsync(container.Id).ConfigureAwait(false))!.Contributions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                refreshed.AlternateTexts.Select(value => (value.Kind, value.Text, value.Ordinal)),
                Is.EqualTo([("Name", "Ava Rivermist", 0)]));
            Assert.That(
                refreshed.CustomMetadata.Select(value => (value.Key, value.Value)),
                Is.EquivalentTo(
                [
                    ("gender", "Female"),
                    ("birthplace", "Australia"),
                    ("astrology", "Capricorn")
                ]));
        }
    }

    [Test]
    public async Task AddContributionAsync_CreatesAuditRecord_WhenContributionIsAdded()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            ProviderType = "AuditProvider"
        };

        // Act
        await _subject.AddContributionAsync(container.Id, contribution, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(
                auditRecords,
                Has.One.With.Property(nameof(AuditRecord.EntityType))
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
        var act = (AsyncTestDelegate)(async () =>
            await _subject.AddContributionAsync(Guid.NewGuid(), contribution, _userId).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(act);
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
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            ProviderType = "TestProvider"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
    }

    [Test]
    public async Task AddContributionAsync_PersistsNullUserId_WhenUserIdIsNull()
    {
        // Arrange
        var container = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contribution = new MetadataContribution
        {
            ProviderType = "SystemProvider"
        };

        // Act
        var result = await _subject.AddContributionAsync(container.Id, contribution, null).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).Null);
    }

    [Test]
    public async Task PatchAsync_ReplacesThePrimaryImage_WhenANewPrimaryImageIsSet()
    {
        // Arrange
        var first = new Image { Type = "poster", Url = new Uri("https://e/a.jpg"), IsPrimary = true };
        var second = new Image { Type = "poster", Url = new Uri("https://e/b.jpg") };
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(first);
        contribution.Images.Add(second);
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;

        // Act
        var result = await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = containerId,
                PrimaryImageId = new() { Value = second.Id }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var rows = await _context.Images.AsNoTracking()
            .Where(i => i.Id == first.Id || i.Id == second.Id)
            .ToDictionaryAsync(i => i.Id, i => i.IsPrimary).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(rows[second.Id], Is.True);
            Assert.That(rows[first.Id], Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenPrimaryImageNotInContainer()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/a.jpg") });
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;

        // Act
        var result = await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = containerId,
                PrimaryImageId = new() { Value = Guid.NewGuid() }
            },
            _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenPrimaryImageBelongsToDifferentContainer()
    {
        // Arrange
        var firstContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(firstContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var firstContribution = new MetadataContribution
        {
            MetadataContainerId = firstContainer.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        firstContribution.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/a.jpg") });
        _context.MetadataContributions.Add(firstContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var other = new Image { Type = "poster", Url = new Uri("https://e/other.jpg") };
        var otherContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var otherContribution = new MetadataContribution
        {
            MetadataContainerId = otherContainer.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        otherContribution.Images.Add(other);
        _context.MetadataContributions.Add(otherContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var otherContainerId = otherContainer.Id;

        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/c.jpg") });
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;

        // Act
        var result = await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = containerId,
                PrimaryImageId = new() { Value = other.Id }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stillSet = await _context.Images.AsNoTracking().FirstAsync(i => i.Id == other.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(stillSet.IsPrimary, Is.False);
            Assert.That(otherContainerId, Is.Not.EqualTo(containerId));
        }
    }

    [Test]
    public async Task UpdateContributionAsync_CarriesPrimaryMarkerForward_WhenSameUrlPresent()
    {
        // Arrange
        var marked = new Image { Type = "poster", Url = new Uri("https://e/keep.jpg"), IsPrimary = true };
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(marked);
        contribution.Images.Add(new Image { Type = "backdrop", Url = new Uri("https://e/drop.jpg") });
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;
        var contributionId = contribution.Id;

        var incoming = new MetadataContribution { ProviderType = "provider-a", Title = "Refreshed" };
        incoming.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/keep.jpg") });
        incoming.Images.Add(new Image { Type = "backdrop", Url = new Uri("https://e/new.jpg") });

        // Act
        await _subject.UpdateContributionAsync(containerId, contributionId, incoming, _userId).ConfigureAwait(false);

        // Assert
        var images = await _context.Images.AsNoTracking()
            .Where(i => i.MetadataContributionId == contributionId)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(images, Has.Exactly(1).Matches<Image>(i => i.IsPrimary));
            Assert.That(images.Single(i => i.IsPrimary).Url, Is.EqualTo(new Uri("https://e/keep.jpg")));
        }
    }

    [Test]
    public async Task UpdateContributionAsync_MarksOnlyFirstByOrdinal_WhenMarkedUrlDuplicatedIncoming()
    {
        // Arrange
        var marked = new Image { Type = "poster", Url = new Uri("https://e/keep.jpg"), IsPrimary = true };
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(marked);
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;
        var contributionId = contribution.Id;

        var incoming = new MetadataContribution { ProviderType = "provider-a", Title = "Refreshed" };
        incoming.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/keep.jpg"), Ordinal = 1 });
        incoming.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/keep.jpg"), Ordinal = 0 });

        // Act
        await _subject.UpdateContributionAsync(containerId, contributionId, incoming, _userId).ConfigureAwait(false);

        // Assert
        var images = await _context.Images.AsNoTracking()
            .Where(i => i.MetadataContributionId == contributionId)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(images, Has.Exactly(1).Matches<Image>(i => i.IsPrimary));
            Assert.That(images.Single(i => i.IsPrimary).Ordinal, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task UpdateContributionAsync_LapsesPrimaryMarker_WhenUrlAbsent()
    {
        // Arrange
        var marked = new Image { Type = "poster", Url = new Uri("https://e/gone.jpg"), IsPrimary = true };
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(marked);
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var containerId = container.Id;
        var contributionId = contribution.Id;

        var incoming = new MetadataContribution { ProviderType = "provider-a", Title = "Refreshed" };
        incoming.Images.Add(new Image { Type = "poster", Url = new Uri("https://e/different.jpg") });

        // Act
        await _subject.UpdateContributionAsync(containerId, contributionId, incoming, _userId).ConfigureAwait(false);

        // Assert
        var anyPrimary = await _context.Images.AsNoTracking()
            .AnyAsync(i => i.MetadataContributionId == contributionId && i.IsPrimary).ConfigureAwait(false);
        Assert.That(anyPrimary, Is.False);
    }

    [Test]
    public async Task RemoveContainersAsync_RemovesOnlyTargetGraph_WhenSourceRelationshipPointsToTarget()
    {
        // Arrange
        var targetContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var sourceContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(targetContainer, sourceContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var source = new Subject
        {
            MetadataContainerId = sourceContainer.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var targetContribution = new MetadataContribution
        {
            MetadataContainerId = targetContainer.Id,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        var sourceContribution = new MetadataContribution
        {
            MetadataContainerId = sourceContainer.Id,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        var relationship = new Relationship
        {
            RelatedMetadataContainerId = targetContainer.Id,
            Kind = "member",
            Ordinal = 0
        };
        sourceContribution.Relationships.Add(relationship);
        _context.Subjects.Add(source);
        _context.MetadataContributions.AddRange(targetContribution, sourceContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.RemoveContainersAsync([targetContainer.Id], default).ConfigureAwait(false);

        // Assert
        var targetContainerExists = await _context.MetadataContainers.AsNoTracking()
            .AnyAsync(container => container.Id == targetContainer.Id)
            .ConfigureAwait(false);
        var targetContributionExists = await _context.MetadataContributions.AsNoTracking()
            .AnyAsync(contribution => contribution.Id == targetContribution.Id)
            .ConfigureAwait(false);
        var sourceExists = await _context.Subjects.AsNoTracking()
            .AnyAsync(subject => subject.Id == source.Id)
            .ConfigureAwait(false);
        var sourceContainerExists = await _context.MetadataContainers.AsNoTracking()
            .AnyAsync(container => container.Id == sourceContainer.Id)
            .ConfigureAwait(false);
        var remainingRelationship = await _context.Relationships.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == relationship.Id)
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetContainerExists, Is.False);
            Assert.That(targetContributionExists, Is.False);
            Assert.That(sourceExists, Is.True);
            Assert.That(sourceContainerExists, Is.True);
            Assert.That(remainingRelationship.RelatedMetadataContainerId, Is.Null);
        }
    }

    [Test]
    public async Task PatchAsync_PersistsProviderOrder_WhenContributionIdsAreReordered()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var user = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "user",
            Ordinal = 4,
            CreatedAt = DateTime.UtcNow
        };
        var first = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        var second = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-b",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        var third = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-c",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.AddRange(user, first, second, third);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = container.Id,
                ContributionIds = [third.Id, first.Id, second.Id]
            },
            _userId).ConfigureAwait(false);

        // Assert
        var ordinals = await _context.MetadataContributions.AsNoTracking()
            .Where(c => c.MetadataContainerId == container.Id)
            .ToDictionaryAsync(c => c.ProviderType, c => c.Ordinal)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(ordinals["provider-c"], Is.EqualTo(0));
            Assert.That(ordinals["provider-a"], Is.EqualTo(1));
            Assert.That(ordinals["provider-b"], Is.EqualTo(2));
            Assert.That(ordinals["user"], Is.EqualTo(-1));
        }
    }

    [Test]
    public async Task PatchAsync_Throws_WhenContributionOrderOmitsAProvider()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var first = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        var second = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "provider-b",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.AddRange(first, second);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var act = (AsyncTestDelegate)(async () => await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = container.Id,
                ContributionIds = [second.Id]
            },
            _userId).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<MagpieException>(act);
    }

    [Test]
    public void PatchAsync_Throws_WhenARemovedAwardIsNotInTheContribution()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        _context.SaveChanges();
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            ProviderType = "user",
            Source = "user",
            Ordinal = -1,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        _context.SaveChanges();
        var unknownId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var act = async () => await _subject.PatchAsync(
            new MetadataContainerPatch
            {
                ContainerId = container.Id,
                Awards = new() { Remove = [unknownId] }
            },
            _userId).ConfigureAwait(false);

        // Assert
        Assert.That(
            act,
            Throws.TypeOf<MagpieException>().With.Message
                .EqualTo("The metadata changes do not contain Award 11111111-1111-1111-1111-111111111111."));
    }
}
