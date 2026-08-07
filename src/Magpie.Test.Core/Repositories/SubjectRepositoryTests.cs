using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class SubjectRepositoryTests
{
    private MagpieDbContext _context = null!;
    private Mock<IEventBus> _eventBus = null!;
    private ISubjectRepository _subject = null!;
    private IMetadataContainerRepository _metadataContainerRepository = null!;
    private ITransactionManager _transactionManager = null!;
    private Guid _userId;
    private Guid _metadataContainerId;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _eventBus = new Mock<IEventBus>();
        _eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var systemUser = new User
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            Username = "system",
            ProviderType = "system",
            Enabled = false,
            Role = Role.Admin,
            CreatedAt = DateTime.UnixEpoch
        };
        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.AddRange(systemUser, user);
        _context.SaveChanges();

        _userId = user.Id;

        var metadataContainer = new MetadataContainer
        {
            CreatedAt = DateTime.UtcNow
        };

        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();

        _metadataContainerId = metadataContainer.Id;

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Moq.Mock.Of<Sdk.Services.IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance));

        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        var subject = new SubjectRepository(
            _context,
            auditRecordRepository,
            _transactionManager,
            _eventBus.Object,
            loggerFactory.CreateLogger<SubjectRepository>());
        _subject = subject;

        _metadataContainerRepository = new MetadataContainerRepository(
            _context,
            auditRecordRepository,
            _subject,
            _transactionManager,
            loggerFactory.CreateLogger<MetadataContainerRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(subject.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).EqualTo(subject.Id));
            Assert.That(result, Has.Property(nameof(result.Kind)).EqualTo("person"));
            Assert.That(result, Has.Property(nameof(result.MetadataContainerId)).EqualTo(_metadataContainerId));
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
    public async Task ListAllAsync_ReturnsAllSubjects_WhenSubjectsExist()
    {
        // Arrange
        var subject1 = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var metadataContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var subject2 = new Subject
        {
            MetadataContainerId = metadataContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.AddRange(subject1, subject2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoSubjects()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsSubject_WhenAllRelationshipFiltersAreFalse()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        _context.Subjects.Add(subject);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                HasContentRelationships = false,
                HasUserContributions = false,
                HasTags = false,
                IsUserCreated = false
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([subject.Id]));
    }

    [Test]
    public async Task QueryAsync_ExcludesSubject_WhenContentLinkFilterIsFalse()
    {
        // Arrange
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution.Relationships.Add(new Relationship
        {
            RelatedMetadataContainerId = _metadataContainerId,
            Kind = "performer",
            Ordinal = 0
        });
        _context.Subjects.Add(subject);
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "movie",
            Level = "scene",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { HasContentRelationships = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Does.Not.Contain(subject.Id));
    }

    [Test]
    public async Task QueryAsync_ExcludesSubject_WhenUserContributionFilterIsFalse()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        _context.Subjects.Add(subject);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            ProviderType = "user",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { HasUserContributions = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Does.Not.Contain(subject.Id));
    }

    [Test]
    public async Task QueryAsync_ExcludesSubject_WhenTagFilterIsFalse()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        var tag = new Tag { Name = "favorite" };
        tag.MetadataContainers.Add(_context.MetadataContainers.Single(c => c.Id == _metadataContainerId));
        _context.Subjects.Add(subject);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { HasTags = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Does.Not.Contain(subject.Id));
    }

    [Test]
    public async Task QueryAsync_ExcludesSubject_WhenUserCreatedFilterIsFalse()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { IsUserCreated = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Does.Not.Contain(subject.Id));
    }

    [Test]
    public async Task QueryAsync_ReturnsSubject_WhenContentLinkFilterIsFalseAndLinkIsFromSubject()
    {
        // Arrange
        var sourceContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(sourceContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var target = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        var source = new Subject
        {
            MetadataContainerId = sourceContainer.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var contribution = new MetadataContribution
        {
            MetadataContainerId = sourceContainer.Id,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution.Relationships.Add(new Relationship
        {
            RelatedMetadataContainerId = _metadataContainerId,
            Kind = "member",
            Ordinal = 0
        });
        _context.Subjects.AddRange(target, source);
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { HasContentRelationships = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Does.Contain(target.Id));
    }

    [Test]
    public async Task QueryAsync_ReturnsRealUserOnly_WhenUserCreatedFilterIsTrue()
    {
        // Arrange
        var userContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(userContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var systemCreated = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        var userCreated = new Subject
        {
            MetadataContainerId = userContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.Subjects.AddRange(systemCreated, userCreated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { IsUserCreated = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([userCreated.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsSubject_WhenRelationshipFiltersMatch()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                HasContentRelationships = false,
                HasUserContributions = false,
                HasTags = false,
                IsUserCreated = false
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.MetadataContainerId), Is.EqualTo([_metadataContainerId]));
    }

    [Test]
    public async Task QueryAsync_ReturnsEmpty_WhenContentRelationshipFilterDoesNotMatch()
    {
        // Arrange
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution.Relationships.Add(new Relationship
        {
            RelatedMetadataContainerId = _metadataContainerId,
            Kind = "performer",
            Ordinal = 0
        });
        _context.Subjects.Add(subject);
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "movie",
            Level = "scene",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { HasContentRelationships = false },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_LoadsContributionTitleAndImages_ForListRendering()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            ProviderType = "user",
            Source = "user",
            Ordinal = 0,
            Title = "Jane Doe",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Type = "profile",
            Url = new Uri("https://example.test/jane.jpg"),
            IsPrimary = true
        });
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync(default).ConfigureAwait(false)).Single();
        var loaded = result.MetadataContainer!.Contributions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Title, Is.EqualTo("Jane Doe"));
            Assert.That(loaded.Images, Has.Exactly(1).Items);
            Assert.That(loaded.Images.Single().Url, Is.EqualTo(new Uri("https://example.test/jane.jpg")));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingSubjects_WhenKindFilterIsSet()
    {
        // Arrange
        var subject1 = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var metadataContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var metadataContainer3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(metadataContainer2, metadataContainer3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var subject2 = new Subject
        {
            MetadataContainerId = metadataContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var subject3 = new Subject
        {
            MetadataContainerId = metadataContainer3.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.AddRange(subject1, subject2, subject3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { Criteria = null, Kind = "person", Tag = null },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(2));
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.All.Matches<Subject>(s => s.Kind == "person"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyPage_WhenKindHasNoMatches()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { Criteria = null, Kind = "studio", Tag = null },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalItems, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingSubjects_WhenTagFilterIsSet()
    {
        // Arrange
        var taggedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(taggedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var taggedSubject = new Subject
        {
            MetadataContainerId = taggedContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(taggedSubject);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = taggedContainer.Id,
            ProviderType = "user",
            Source = "user",
            Ordinal = 0,
            Title = "Bryan Cranston",
            CreatedAt = DateTime.UtcNow
        });
        var favoriteTag = _context.Tags.Local.FirstOrDefault(t => t.Name == "favorite")
            ?? _context.Tags.FirstOrDefault(t => t.Name == "favorite");
        if (favoriteTag is null)
        {
            favoriteTag = new Tag { Name = "favorite" };
            _context.Tags.Add(favoriteTag);
        }

        favoriteTag.MetadataContainers.Add(taggedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var taggedId = taggedSubject.Id;

        var otherContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var otherSubject = new Subject
        {
            MetadataContainerId = otherContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(otherSubject);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = otherContainer.Id,
            ProviderType = "user",
            Source = "user",
            Ordinal = 0,
            Title = "Denis Villeneuve",
            CreatedAt = DateTime.UtcNow
        });
        var otherTag = _context.Tags.Local.FirstOrDefault(t => t.Name == "other")
            ?? _context.Tags.FirstOrDefault(t => t.Name == "other");
        if (otherTag is null)
        {
            otherTag = new Tag { Name = "other" };
            _context.Tags.Add(otherTag);
        }

        otherTag.MetadataContainers.Add(otherContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = "favorite" },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(1));
            Assert.That(result.Items.Single().Id, Is.EqualTo(taggedId));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyPage_WhenTagHasNoMatches()
    {
        // Act
        var result = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = "testtag" },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalItems, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAllSubjectsPaged_WhenNoFilter()
    {
        // Arrange
        foreach (var (kind, title) in new[] { ("person", "Alice"), ("person", "Bob"), ("studio", "Carol") })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = container.Id,
                Kind = kind,
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                ProviderType = "user",
                Source = "user",
                Ordinal = 0,
                Title = title,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var result = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = null },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(3));
            Assert.That(result.Items, Has.Exactly(3).Items);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingSubject_WhenCustomMetadataHasHigherPrecedence()
    {
        // Arrange
        var matchingSubject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var matchingPrimary = new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        matchingPrimary.CustomMetadata.Add(new() { Key = "gender", Value = "female" });
        var matchingSecondary = new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        matchingSecondary.CustomMetadata.Add(new() { Key = "GENDER", Value = "male" });

        var excludedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var excludedSubject = new Subject
        {
            MetadataContainer = excludedContainer,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        var excludedPrimary = new MetadataContribution
        {
            MetadataContainer = excludedContainer,
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        excludedPrimary.CustomMetadata.Add(new() { Key = "gender", Value = "male" });
        var excludedSecondary = new MetadataContribution
        {
            MetadataContainer = excludedContainer,
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        excludedSecondary.CustomMetadata.Add(new() { Key = "gender", Value = "female" });

        _context.AddRange(
            matchingSubject,
            matchingPrimary,
            matchingSecondary,
            excludedSubject,
            excludedPrimary,
            excludedSecondary);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new SubjectFilterCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        CustomMetadata = new()
                        {
                            And =
                            [
                                new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new()
                                    {
                                        ["GENDER"] = new()
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Value = "female"
                                        }
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(new() { Criteria = criteria },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(1));
            Assert.That(result.Items.Single().Id, Is.EqualTo(matchingSubject.Id));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesSkipAndTake_AndReportsUnpagedTotal()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = container.Id,
                Kind = "person",
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                ProviderType = "user",
                Source = "user",
                Ordinal = 0,
                Title = $"Person {i}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var firstPage = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = null },
            new() { PageNumber = 1, PageSize = 2 },
            null).ConfigureAwait(false);
        var secondPage = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = null },
            new() { PageNumber = 2, PageSize = 2 },
            null).ConfigureAwait(false);
        var thirdPage = await _subject.QueryAsync(
            new() { Criteria = null, Kind = null, Tag = null },
            new() { PageNumber = 3, PageSize = 2 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPage.TotalItems, Is.EqualTo(5));
            Assert.That(firstPage.Items, Has.Exactly(2).Items);
            Assert.That(secondPage.Items, Has.Exactly(2).Items);
            Assert.That(thirdPage.Items, Has.Exactly(1).Items);
            var pagedIds = firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items)
                .Select(s => s.Id).ToList();
            Assert.That(pagedIds, Is.Unique);
            Assert.That(pagedIds, Has.Exactly(5).Items);
        }
    }

    [Test]
    public async Task QueryAsync_SortsByKindDescending_AcrossPages()
    {
        // Arrange
        foreach (var (kind, title) in new[] { ("network", "N"), ("person", "P"), ("studio", "S") })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = container.Id,
                Kind = kind,
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                ProviderType = "user",
                Source = "user",
                Ordinal = 0,
                Title = title,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var sort = new SortParameter { PropertyName = nameof(Subject.Kind), Direction = SortDirection.Descending };
        var result = await _subject.QueryAsync(
            new()
            {
                Criteria = null,
                Kind = null,
                Tag = null
            },
            new() { PageNumber = 1, PageSize = 10 },
            sort).ConfigureAwait(false);

        // Assert
        var kinds = result.Items.Select(s => s.Kind).ToList();
        Assert.That(kinds, Is.EqualTo(["studio", "person", "network"]));
    }

    [Test]
    public async Task QueryAsync_SortsByTitleAscendingByDefault_AcrossPages()
    {
        // Arrange
        foreach (var (kind, title) in new[] { ("person", "Charlie"), ("studio", "Alice"), ("person", "Bob") })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = container.Id,
                Kind = kind,
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                ProviderType = "user",
                Source = "user",
                Ordinal = 0,
                Title = title,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var firstPage = await _subject.QueryAsync(
            new()
            {
                Criteria = null,
                Kind = null,
                Tag = null
            },
            new() { PageNumber = 1, PageSize = 2 },
            null).ConfigureAwait(false);
        var secondPage = await _subject.QueryAsync(
            new()
            {
                Criteria = null,
                Kind = null,
                Tag = null
            },
            new() { PageNumber = 2, PageSize = 2 },
            null).ConfigureAwait(false);

        // Assert
        var titles = firstPage.Items.Concat(secondPage.Items)
            .Select(s => s.MetadataContainer!.Contributions.OrderBy(c => c.Ordinal).Select(c => c.Title).First())
            .ToList();
        Assert.That(titles, Is.EqualTo(["Alice", "Bob", "Charlie"]));
    }

    [Test]
    public async Task QueryAsync_SortsByTitleDescending_WhenNameSortRequested()
    {
        // Arrange
        foreach (var (kind, title) in new[] { ("person", "Alice"), ("person", "Bob"), ("studio", "Charlie") })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.Subjects.Add(new Subject
            {
                MetadataContainerId = container.Id,
                Kind = kind,
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContributions.Add(new MetadataContribution
            {
                MetadataContainerId = container.Id,
                ProviderType = "user",
                Source = "user",
                Ordinal = 0,
                Title = title,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var sort = new SortParameter { PropertyName = "name", Direction = SortDirection.Descending };
        var result = await _subject.QueryAsync(
            new()
            {
                Criteria = null,
                Kind = null,
                Tag = null
            },
            new() { PageNumber = 1, PageSize = 10 },
            sort).ConfigureAwait(false);

        // Assert
        var titles = result.Items
            .Select(s => s.MetadataContainer!.Contributions.OrderBy(c => c.Ordinal).Select(c => c.Title).First())
            .ToList();
        Assert.That(titles, Is.EqualTo(["Charlie", "Bob", "Alice"]));
    }

    [Test]
    public async Task CreateAsync_CreatesSubject_WhenRequestIsValid()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person"
        };

        // Act
        var result = await _subject.CreateAsync(subject, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Kind)).EqualTo("person"));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenSubjectIsCreated()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "studio"
        };

        // Act
        await _subject.CreateAsync(subject, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        subject.Kind = "studio";
        subject.DateLastRefreshed = DateTime.UtcNow;

        // Act
        var result = await _subject.UpdateAsync(subject, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(subject.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.Kind)).EqualTo("studio"));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var subject = new Subject
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            Kind = "person"
        };

        // Act
        var result = await _subject.UpdateAsync(subject, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenSubjectExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        subject.Kind = "studio";

        // Act
        await _subject.UpdateAsync(subject, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(subject.Id, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var deleted = await _subject.GetByIdAsync(subject.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_DeletesSubjectAndPublishesEventOnce_WhenCalledTwice()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(subject.Id, null).ConfigureAwait(false);
        await _subject.DeleteAsync(subject.Id, null).ConfigureAwait(false);

        // Assert
        var subjectExists = await _context.Subjects.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == subject.Id)
            .ConfigureAwait(false);
        Assert.That(subjectExists, Is.False);
        _eventBus.Verify(
            bus => bus.PublishAsync(
                It.Is<MagpieEvent>(magpieEvent =>
                    magpieEvent.Name == MagpieEvents.Names.SubjectDeleted &&
                    magpieEvent.Context[MagpieEvents.Keys.SubjectId] == subject.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotExists()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_CreatesAuditRecord_WhenSubjectExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(subject.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_UpdatesSubject_WhenExists()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new TagsPatch { Id = subject.Id };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);

        var updated = await _subject.GetByIdAsync(subject.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var patch = new TagsPatch { Id = Guid.NewGuid() };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task PatchAsync_CreatesAuditRecord_WhenPatchSucceeds()
    {
        // Arrange
        var subject = new Subject
        {
            MetadataContainerId = _metadataContainerId,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new TagsPatch { Id = subject.Id };

        // Act
        await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Patch"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Subject"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesSubjectContainerAndSeedName_WhenRequestIsValid()
    {
        // Arrange
        var container = new MetadataContainer { Id = Guid.NewGuid() };
        container.Contributions.Add(new()
        {
            Id = Guid.NewGuid(),
            ProviderType = "user",
            Source = "user",
            Ordinal = -1,
            Title = "Jane Doe"
        });

        // Act
        var created = await _subject.CreateAsync(
            new Subject { Id = Guid.NewGuid(), Kind = "person", MetadataContainer = container },
            _userId).ConfigureAwait(false);

        // Assert
        var reloaded = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var seedTitle = reloaded!.MetadataContainer!.Contributions
            .OrderBy(c => c.Ordinal)
            .Select(c => c.Title)
            .FirstOrDefault();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.Kind, Is.EqualTo("person"));
            Assert.That(reloaded.MetadataContainerId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(reloaded.CreatedByUserId, Is.EqualTo(_userId));
            Assert.That(seedTitle, Is.EqualTo("Jane Doe"));
        }
    }

    [Test]
    public async Task InlineSubjectCreate_ThenFailingContributionAdd_RollsBackSubjectAndContainer()
    {
        // Arrange
        var subjectsBefore = await _context.Subjects.CountAsync().ConfigureAwait(false);
        var containersBefore = await _context.MetadataContainers.CountAsync().ConfigureAwait(false);
        var missingContainerId = Guid.NewGuid();

        // Act
        Subject? created = null;
        var threw = false;
        try
        {
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                created = await _subject.CreateAsync(
                    new Subject
                    {
                        Id = Guid.NewGuid(),
                        Kind = "person",
                        MetadataContainer = new() { Id = Guid.NewGuid() }
                    },
                    _userId,
                    ct).ConfigureAwait(false);
                await _metadataContainerRepository.AddContributionAsync(
                    missingContainerId,
                    new MetadataContribution { ProviderType = "user" },
                    _userId,
                    ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(threw, Is.True);
            Assert.That(created, Is.Not.Null);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(subjectsBefore));
            Assert.That(
                await _context.MetadataContainers.CountAsync().ConfigureAwait(false),
                Is.EqualTo(containersBefore));
            Assert.That(await _context.Subjects.AnyAsync(s => s.Id == created!.Id).ConfigureAwait(false), Is.False);
            Assert.That(
                await _context.MetadataContainers.AnyAsync(c => c.Id == created!.MetadataContainerId)
                    .ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.MetadataContributions
                    .AnyAsync(c => c.MetadataContainerId == created!.MetadataContainerId).ConfigureAwait(false),
                Is.False);
        }
    }

    [Test]
    public async Task InlineSubjectCreate_ThenSuccessfulContributionAdd_PersistsBoth()
    {
        // Arrange
        var subjectsBefore = await _context.Subjects.CountAsync().ConfigureAwait(false);

        // Act
        Subject created = null!;
        MetadataContribution contribution = null!;
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            created = await _subject.CreateAsync(
                new Subject
                {
                    Id = Guid.NewGuid(),
                    Kind = "person",
                    MetadataContainer = new() { Id = Guid.NewGuid() }
                },
                _userId,
                ct).ConfigureAwait(false);
            contribution = await _metadataContainerRepository.AddContributionAsync(
                _metadataContainerId,
                new MetadataContribution { ProviderType = "user" },
                _userId,
                ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(subjectsBefore + 1));
            Assert.That(await _context.Subjects.AnyAsync(s => s.Id == created.Id).ConfigureAwait(false), Is.True);
            Assert.That(
                await _context.MetadataContainers.AnyAsync(c => c.Id == created.MetadataContainerId)
                    .ConfigureAwait(false),
                Is.True);
            Assert.That(
                await _context.MetadataContributions.AnyAsync(c => c.Id == contribution.Id).ConfigureAwait(false),
                Is.True);
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesLosingExternalId_WhenContributionsDuplicateSource()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var winning = new MetadataContribution
        {
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        winning.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-h", Value = "tt-win" });

        var losing = new MetadataContribution
        {
            ProviderType = "provider-b",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        losing.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-h", Value = "tt-lose" });

        container.Contributions.Add(winning);
        container.Contributions.Add(losing);
        var subject = new Subject
        {
            MetadataContainer = container,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        static SubjectQuery QueryForValue(string value) => new()
        {
            Criteria = new()
            {
                Metadata = new()
                {
                    And =
                    [
                        new MetadataCriteria
                        {
                            ExternalIds = new()
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Value = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = value }]
                                    },
                                    Source = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "provider-h" }]
                                    }
                                }]]
                            }
                        }
                    ]
                }
            }
        };

        // Act
        var winningResult = await _subject.QueryAsync(QueryForValue("tt-win"),
            new() { PageNumber = 1, PageSize = 100 },
            null, default)
            .ConfigureAwait(false);
        var losingResult = await _subject.QueryAsync(QueryForValue("tt-lose"),
            new() { PageNumber = 1, PageSize = 100 },
            null, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(winningResult.Items.Select(s => s.Id), Does.Contain(subject.Id));
            Assert.That(losingResult.Items.Select(s => s.Id), Does.Not.Contain(subject.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsDistinctRelatedSubjects_WhenScopedToRelatedContentHierarchyItemId()
    {
        // Arrange
        var subjectAContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var subjectBContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var subjectCContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(subjectAContainer, subjectBContainer, subjectCContainer, contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.AddRange(
            new Subject { MetadataContainerId = subjectAContainer.Id, Kind = "person", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = subjectBContainer.Id, Kind = "studio", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = subjectCContainer.Id, Kind = "person", CreatedAt = DateTime.UtcNow });

        var contributedLayer = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 1,
            ProviderType = "provider-a",
            Title = "Scene"
        };
        contributedLayer.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = subjectAContainer.Id,
            Ordinal = 0
        });
        var materializedLayer = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 0,
            ProviderType = "provider-b",
            Title = "Scene"
        };
        materializedLayer.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = subjectAContainer.Id,
            Ordinal = 0
        });
        materializedLayer.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = subjectBContainer.Id,
            Ordinal = 1
        });
        _context.MetadataContributions.AddRange(contributedLayer, materializedLayer);

        var content = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "movie",
            Level = "scene",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(content);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new SubjectQuery { RelatedContentHierarchyItemId = content.Id },
                new() { PageNumber = 1, PageSize = 100 },
                null, default)
            .ConfigureAwait(false);

        // Assert
        var containerIds = result.Items.Select(s => s.MetadataContainerId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(2));
            Assert.That(containerIds, Is.EquivalentTo([subjectAContainer.Id, subjectBContainer.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyPage_WhenRelatedContentHierarchyItemIdMissing()
    {
        // Arrange
        var subjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(subjectContainer, contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = subjectContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene"
        };
        contribution.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = subjectContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "movie",
            Level = "scene",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new SubjectQuery { RelatedContentHierarchyItemId = Guid.NewGuid() },
                new() { PageNumber = 1, PageSize = 100 },
                null, default)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalItems, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_CombinesRelatedContentScopeWithKind_WhenBothFiltersAreSet()
    {
        // Arrange
        var personContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(personContainer, studioContainer, contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.AddRange(
            new Subject { MetadataContainerId = personContainer.Id, Kind = "person", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = studioContainer.Id, Kind = "studio", CreatedAt = DateTime.UtcNow });

        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Scene"
        };
        contribution.Relationships.Add(new Relationship
        {
            Kind = "performer",
            RelatedMetadataContainerId = personContainer.Id,
            Ordinal = 0
        });
        contribution.Relationships.Add(new Relationship
        {
            Kind = "studio",
            RelatedMetadataContainerId = studioContainer.Id,
            Ordinal = 1
        });
        _context.MetadataContributions.Add(contribution);

        var content = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "movie",
            Level = "scene",
            Slug = "scene",
            Name = "Scene",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(content);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new SubjectQuery { RelatedContentHierarchyItemId = content.Id, Kind = "studio" },
                new() { PageNumber = 1, PageSize = 100 },
                null, default)
            .ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(1));
            Assert.That(items[0].MetadataContainerId, Is.EqualTo(studioContainer.Id));
        }
    }
}
