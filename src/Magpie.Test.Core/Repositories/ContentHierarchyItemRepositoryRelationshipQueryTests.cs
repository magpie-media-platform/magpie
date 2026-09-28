using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryRelationshipQueryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        var metadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(metadataContainer);
        _context.SaveChanges();
        _metadataContainerId = metadataContainer.Id;
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _subject = new ContentHierarchyItemRepository(
            _context, Mock.Of<IAuditRecordRepository>(), transactions,
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(), Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipKind()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var directorItem = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(directorItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "director" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(directorItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyLinkedItems_WhenFilteringByRelationshipRelatedMetadataContainerId()
    {
        // Arrange
        var scopedSubjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(scopedSubjectContainer);
        var otherSubjectContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherSubjectContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = scopedSubjectContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Kevin Heffernan"
        });
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = otherSubjectContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Kevin Heffernan"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = scopedSubjectContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = otherSubjectContainer.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var scopedContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(scopedContentContainer);
        var otherContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var scopedContribution = new MetadataContribution
        {
            MetadataContainerId = scopedContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Super Troopers"
        };
        scopedContribution.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = scopedSubjectContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(scopedContribution);
        var otherContribution = new MetadataContribution
        {
            MetadataContainerId = otherContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Night Shift"
        };
        otherContribution.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = otherSubjectContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(otherContribution);
        var scopedItem = new ContentHierarchyItem
        {
            MetadataContainerId = scopedContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "super-troopers",
            Name = "Super Troopers",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(scopedItem);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = otherContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "night-shift",
            Name = "Night Shift",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [
                                    new()
                                    {
                                        RelatedMetadataContainerId = new()
                                        {
                                            And =
                                            [
                                                new()
                                                {
                                                    Operator = CriteriaValueOperator.Equals,
                                                    Value = scopedSubjectContainer.Id
                                                }
                                            ]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(scopedItem.Id));
            Assert.That(items[0].Name, Is.EqualTo("Super Troopers"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipKindCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var directorItem = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(directorItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Director" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(directorItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipSubjectName()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "delphine-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Subject = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Delphine"
                                            }
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipSubjectNameCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Sable Vance"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "sable-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Subject = new()
                                    {
                                        And =
                                        [
                                            new() { Operator = CriteriaValueOperator.Equals, Value = "sable vance" }
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipSubjectNameAlias()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Odessandra Kerr"
        };
        var relationshipAliases1 = new[] { "Odessa Kerr" };
        for (var i = 0; i < relationshipAliases1.Length; i++)
        {
            relationshipSubjectContribution1.AlternateTexts.Add(new AlternateText
            {
                Ordinal = i,
                Kind = "Name",
                Text = relationshipAliases1[i]
            });
        }

        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "kerr-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var queryCriteria = new[]
        {
            (Operator: CriteriaValueOperator.ContainsString, Value: "odessa kerr"),
            (Operator: CriteriaValueOperator.Equals, Value: "Odessa Kerr"),
            (Operator: CriteriaValueOperator.ContainsString, Value: "Odessandra")
        };

        // Act
        var queryResults = new List<List<ContentHierarchyItem>>();
        foreach (var criterion in queryCriteria)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            Relationships = new()
                            {
                                Some =
                                [
                                    [
                                        new()
                                        {
                                            Subject = new()
                                            {
                                                And = [new()
                                                {
                                                    Operator = criterion.Operator,
                                                    Value = criterion.Value
                                                }]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            queryResults.Add([.. result.Items]);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queryResults[0], Has.Exactly(1).Items);
            Assert.That(queryResults[0][0].Id, Is.EqualTo(match.Id));
            Assert.That(queryResults[1], Has.Exactly(1).Items);
            Assert.That(queryResults[1][0].Id, Is.EqualTo(match.Id));
            Assert.That(queryResults[2], Has.Exactly(1).Items);
            Assert.That(queryResults[2][0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_NegativeSubjectOperatorExcludesAliasMatch_WhenNegativeSubjectOperatorIsSet()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bob Smith"
        };
        var relationshipAliases1 = new[] { "Nova" };
        for (var i = 0; i < relationshipAliases1.Length; i++)
        {
            relationshipSubjectContribution1.AlternateTexts.Add(new AlternateText
            {
                Ordinal = i,
                Kind = "Name",
                Text = relationshipAliases1[i]
            });
        }

        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bob-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Marlowe Quinn"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var kept = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "marlowe-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(kept);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var queryCriteria = new[]
        {
            (Operator: CriteriaValueOperator.NotContainsString, Value: "nova"),
            (Operator: CriteriaValueOperator.NotEquals, Value: "Nova")
        };

        // Act
        var queryResults = new List<List<Guid>>();
        foreach (var criterion in queryCriteria)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            Relationships = new()
                            {
                                Some =
                                [
                                    [
                                        new()
                                        {
                                            Subject = new()
                                            {
                                                And = [new()
                                                {
                                                    Operator = criterion.Operator,
                                                    Value = criterion.Value
                                                }]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            queryResults.Add([.. result.Items.Select(i => i.Id)]);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queryResults[0], Is.EquivalentTo([kept.Id]));
            Assert.That(queryResults[1], Is.EquivalentTo([kept.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipSubjectExternalId()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        relationshipSubjectContribution1.ExternalIds.Add(new ExternalId
        {
            Ordinal = 0,
            Source = "provider-b.director",
            Value = "12345"
        });
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "pinned-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        relationshipSubjectContribution2.ExternalIds.Add(new ExternalId
        {
            Ordinal = 0,
            Source = "provider-b.director",
            Value = "99999"
        });
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    ExternalIds = new()
                                    {
                                        Some =
                                        [
                                            [
                                                new()
                                                {
                                                    Value = new()
                                                    {
                                                        And =
                                                        [
                                                            new()
                                                            {
                                                                Operator = CriteriaValueOperator.Equals,
                                                                Value = "12345"
                                                            }
                                                        ]
                                                    },
                                                    Source = new()
                                                    {
                                                        And =
                                                        [
                                                            new()
                                                            {
                                                                Operator = CriteriaValueOperator.Equals,
                                                                Value = "provider-b.director"
                                                            }
                                                        ]
                                                    }
                                                }
                                            ]
                                        ]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_IntersectsRelationshipRows_WhenHasClausesAreAnded()
    {
        // Arrange
        var subjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(subjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = subjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Subject A"
        });
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = subjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var subjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(subjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = subjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Subject B"
        });
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = subjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = contentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Only A"
        };
        contribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = subjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(contribution1);
        var onlyA = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "only-a",
            Name = "Only A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(onlyA);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = contentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Only B"
        };
        contribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = subjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(contribution2);
        var onlyB = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "only-b",
            Name = "Only B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(onlyB);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentContainer3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution3 = new MetadataContribution
        {
            MetadataContainerId = contentContainer3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Both"
        };
        contribution3.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = subjectContainer1.Id,
            Ordinal = 0
        });
        contribution3.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = subjectContainer2.Id,
            Ordinal = 1
        });
        _context.MetadataContributions.Add(contribution3);
        var both = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "both",
            Name = "Both",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(both);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var andCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [new RelationshipCriteria
                                {
                                    Subject = new()
                                    {
                                        And = [new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Value = "Subject A"
                                        }]
                                    }
                                }],
                                [new RelationshipCriteria
                                {
                                    Subject = new()
                                    {
                                        And = [new()
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Value = "Subject B"
                                        }]
                                    }
                                }]
                            ]
                        }
                    }
                ]
            }
        };

        var orCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
                            [
                                [
                                    new RelationshipCriteria
                                    {
                                        Subject = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Subject A"
                                            }]
                                        }
                                    },
                                    new RelationshipCriteria
                                    {
                                        Subject = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Subject B"
                                            }]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var andResult = await _subject.QueryAsync(andCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var orResult = await _subject.QueryAsync(orCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(andResult.Items.Select(i => i.Id), Is.EquivalentTo([both.Id]));
            Assert.That(orResult.Items.Select(i => i.Id), Is.EquivalentTo([onlyA.Id, onlyB.Id, both.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesItems_WhenFilteringByRelationshipNone()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Delphine Ashworth"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var kept = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(kept);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            None =
                            [
                                new()
                                {
                                    Kind = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "publisher" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(kept.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipTypePersonSubject()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var personItem = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(personItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(personItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipTypeCaseInsensitively()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var personItem = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(personItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(personItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipTypeContentLevel()
    {
        // Arrange
        var targetContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(targetContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var targetItem = new ContentHierarchyItem
        {
            MetadataContainerId = targetContainer.Id,
            ContentType = "type-e",
            Level = "level-e",
            Slug = "movie-content-target",
            Name = "Movie A Target",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(targetItem);
        var contentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(contentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = contentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie A"
        };
        contribution.Relationships.Add(new Relationship
        {
            Kind = "related",
            RelatedMetadataContainerId = targetContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(contribution);
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = contentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-content",
            Name = "Movie A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "level-e" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(match.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesItems_WhenFilteringByRelationshipTypeNone()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var kept = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(kept);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            None =
                            [
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(kept.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByRelationshipTypeNotEquals()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var relationshipItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(relationshipItem1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Big Publisher"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer2.Id,
            Kind = "studio",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution2 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        relationshipContribution2.Relationships.Add(new Relationship
        {
            Kind = "publisher",
            RelatedMetadataContainerId = relationshipSubjectContainer2.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution2);
        var publisherItem = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(publisherItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.NotEquals, Value = "person" }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(items[0].Id, Is.EqualTo(publisherItem.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersItems_WhenFilteringByRelationshipTypeIsNullAndIsNotNull()
    {
        // Arrange
        var relationshipSubjectContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipSubjectContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipSubjectContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Bella Hadid"
        };
        _context.MetadataContributions.Add(relationshipSubjectContribution1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = relationshipSubjectContainer1.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });
        var relationshipContentContainer1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relationshipContentContainer1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var relationshipContribution1 = new MetadataContribution
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Alpha"
        };
        relationshipContribution1.Relationships.Add(new Relationship
        {
            Kind = "director",
            RelatedMetadataContainerId = relationshipSubjectContainer1.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(relationshipContribution1);
        var materialized = new ContentHierarchyItem
        {
            MetadataContainerId = relationshipContentContainer1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "materialized-content",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(materialized);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var bareTargetContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        bareTargetContainer.Contributions.Add(new MetadataContribution
        {
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta Bare"
        });
        _context.MetadataContainers.Add(bareTargetContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var bareContentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(bareContentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var bareContribution = new MetadataContribution
        {
            MetadataContainerId = bareContentContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie Beta"
        };
        bareContribution.Relationships.Add(new Relationship
        {
            Kind = "related",
            RelatedMetadataContainerId = bareTargetContainer.Id,
            Ordinal = 0
        });
        _context.MetadataContributions.Add(bareContribution);
        var bare = new ContentHierarchyItem
        {
            MetadataContainerId = bareContentContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "bare-content",
            Name = "Item B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(bare);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var notNullCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.IsNotNull }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        var nullCriteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Relationships = new()
                        {
                            Some =
[
[
                                new()
                                {
                                    Type = new()
                                    {
                                        And = [new() { Operator = CriteriaValueOperator.IsNull }]
                                    }
                                }
]
]
                        }
                    }
                ]
            }
        };

        // Act
        var notNullResult = await _subject.QueryAsync(notNullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var nullResult = await _subject.QueryAsync(nullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var notNullItems = notNullResult.Items.ToList();
        var nullItems = nullResult.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notNullItems, Has.Exactly(1).Items);
            Assert.That(notNullItems[0].Id, Is.EqualTo(materialized.Id));
            Assert.That(nullItems, Has.Exactly(1).Items);
            Assert.That(nullItems[0].Id, Is.EqualTo(bare.Id));
        }
    }
}
