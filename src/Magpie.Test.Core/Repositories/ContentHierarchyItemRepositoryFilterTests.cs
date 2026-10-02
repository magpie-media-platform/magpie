using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.QueryLanguage;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryFilterTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private ITransactionManager _transactionManager = null!;
    private Mock<IEventBus> _eventBus = null!;

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
        _eventBus = new Mock<IEventBus>();
        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _subject = new ContentHierarchyItemRepository(
            _context, Mock.Of<IAuditRecordRepository>(), _transactionManager,
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(), _eventBus.Object,
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByContentType()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new()
            {
                And = [new() { Value = "type-a" }]
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
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.ContentType == "type-a"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyRoots_WhenRootLevelOnlyIsSet()
    {
        // Arrange
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var movieContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(seasonContainer, movieContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var series = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "root-series",
            Name = "Root Series",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(series);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = series.Id,
            ContentType = "type-f",
            Level = "level-c",
            Slug = "child-season",
            Name = "Child Season",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var movie = new ContentHierarchyItem
        {
            MetadataContainerId = movieContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "root-movie",
            Name = "Root Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(season, movie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria { RootLevelOnly = true };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.ParentId == null));
            Assert.That(items.Select(i => i.Slug), Is.EquivalentTo(["root-series", "root-movie"]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsDescendants_WhenAncestorIdMatches()
    {
        // Arrange
        var rootContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var grandchildContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var unrelatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(rootContainer, childContainer, grandchildContainer, unrelatedContainer);
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = rootContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "ancestor-root",
            Name = "Root",
            CreatedAt = DateTime.UtcNow
        };
        var child = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = childContainer,
            ParentId = root.Id,
            ContentType = "type-a",
            Level = "child",
            Slug = "ancestor-child",
            Name = "Child",
            CreatedAt = DateTime.UtcNow
        };
        var grandchild = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = grandchildContainer,
            ParentId = child.Id,
            ContentType = "type-a",
            Level = "leaf",
            Slug = "ancestor-grandchild",
            Name = "Grandchild",
            CreatedAt = DateTime.UtcNow
        };
        var unrelated = new ContentHierarchyItem
        {
            MetadataContainer = unrelatedContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "unrelated-root",
            Name = "Unrelated",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(root, child, grandchild, unrelated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { AncestorId = root.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([child.Id, grandchild.Id]));
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataExternalIdPresence_WhenPresenceFilterIsSet()
    {
        // Arrange
        var withExternalIdContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var withoutExternalIdContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution = new MetadataContribution
        {
            MetadataContainer = withExternalIdContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "With id"
        };
        contribution.ExternalIds.Add(new ExternalId { Source = "test", Value = "external-1" });
        _context.MetadataContainers.AddRange(withExternalIdContainer, withoutExternalIdContainer);
        _context.MetadataContributions.Add(contribution);
        var withExternalId = new ContentHierarchyItem
        {
            MetadataContainer = withExternalIdContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "with-external-id",
            Name = "With id",
            CreatedAt = DateTime.UtcNow
        };
        var withoutExternalId = new ContentHierarchyItem
        {
            MetadataContainer = withoutExternalIdContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "without-external-id",
            Name = "Without id",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(withExternalId, withoutExternalId);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { HasMetadataExternalIds = true },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([withExternalId.Id]));
    }

    [Test]
    public async Task QueryAsync_FiltersBySearchableTitle_WhenTitleSearchIsSet()
    {
        // Arrange
        var pendingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var titledContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var refreshedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(pendingContainer, titledContainer, refreshedContainer);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainer = titledContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Searchable"
        });
        var pending = new ContentHierarchyItem
        {
            MetadataContainer = pendingContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "pending-title",
            Name = "Pending",
            PendingMetadataRefresh = true,
            CreatedAt = DateTime.UtcNow
        };
        var titled = new ContentHierarchyItem
        {
            MetadataContainer = titledContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "metadata-title",
            Name = "Pending",
            PendingMetadataRefresh = true,
            CreatedAt = DateTime.UtcNow
        };
        var refreshed = new ContentHierarchyItem
        {
            MetadataContainer = refreshedContainer,
            ContentType = "type-a",
            Level = "root",
            Slug = "refreshed-title",
            Name = "Refreshed",
            PendingMetadataRefresh = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(pending, titled, refreshed);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { HasSearchableTitle = true },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([titled.Id, refreshed.Id]));
    }

    [TestCase("hasSearchableTitle = true", new[] { "metadata-title", "refreshed-title" })]
    [TestCase("hasSearchableTitle = false", new[] { "pending-title" })]
    public async Task QueryAsync_FiltersBySearchableTitle_WhenQueryTextIsParsed(string query, string[] expectedSlugs)
    {
        // Arrange
        var pendingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var titledContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var refreshedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(pendingContainer, titledContainer, refreshedContainer);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainer = titledContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Searchable"
        });
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                MetadataContainer = pendingContainer,
                ContentType = "type-a",
                Level = "root",
                Slug = "pending-title",
                Name = "Pending",
                PendingMetadataRefresh = true,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = titledContainer,
                ContentType = "type-a",
                Level = "root",
                Slug = "metadata-title",
                Name = "Pending",
                PendingMetadataRefresh = true,
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainer = refreshedContainer,
                ContentType = "type-a",
                Level = "root",
                Slug = "refreshed-title",
                Name = "Refreshed",
                PendingMetadataRefresh = false,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = query.ParseContentFilter().Value!.ContentHierarchyItem.And.Single();

        // Act
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug), Is.EquivalentTo(expectedSlugs));
    }

    [TestCase("22 Jump Street", "2 Fast 2 Furious", "Jump")]
    [TestCase("A Cure for Wellness", "22 Jump Street", "Cure")]
    [TestCase("The Secret To A Happy Marriage", "22 Jump Street", "marriage")]
    public async Task QueryAsync_FiltersByMetadataTitle_WhenSearchMatchesTitle(
        string matchTitle,
        string otherTitle,
        string search)
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = matchTitle
        });
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-match",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = otherTitle
        });
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-other",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Title = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = search }]
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
    public async Task QueryAsync_FiltersByMetadataPublishDate_WhenAPublishDateWindowIsSet()
    {
        // Arrange
        var start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 3, 31, 23, 59, 59, DateTimeKind.Utc);

        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-before",
            PublishDate = new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc)
        });
        var before = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-before",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(before);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-mid",
            PublishDate = new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc)
        });
        var inWindow1 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-mid",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(inWindow1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-late",
            PublishDate = new DateTime(2024, 3, 28, 0, 0, 0, DateTimeKind.Utc)
        });
        var inWindow2 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-late",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(inWindow2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container4.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-after",
            PublishDate = new DateTime(2024, 4, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        var after = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-after",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(after);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        PublishDate = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                    Value = new TemporalValue { Value = start, Precision = TemporalPrecision.Instant }
                                },
                                new()
                                {
                                    Operator = CriteriaValueOperator.LessThanOrEqual,
                                    Value = new TemporalValue { Value = end, Precision = TemporalPrecision.Instant }
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(2));
            Assert.That(ids, Does.Contain(inWindow1.Id));
            Assert.That(ids, Does.Contain(inWindow2.Id));
            Assert.That(ids, Does.Not.Contain(before.Id));
            Assert.That(ids, Does.Not.Contain(after.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataEstablished_WhenAnEstablishedDateRangeIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "item-early",
            Established = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var early = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-early",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(early);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "item-mid",
            Established = new DateTime(2005, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var mid = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-mid",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(mid);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "item-late",
            Established = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var late = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-late",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(late);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new()
                    {
                        Established = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                    Value = new TemporalValue
                                    {
                                        Value = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                        Precision = TemporalPrecision.Instant
                                    }
                                },
                                new()
                                {
                                    Operator = CriteriaValueOperator.LessThanOrEqual,
                                    Value = new TemporalValue
                                    {
                                        Value = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                        Precision = TemporalPrecision.Instant
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids, Does.Contain(mid.Id));
            Assert.That(ids, Does.Not.Contain(early.Id));
            Assert.That(ids, Does.Not.Contain(late.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByMetadataPublishDate_WhenDatePrecisionSelectsAPeriod()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "late-2019",
            PublishDate = new DateTime(2019, 12, 31, 23, 0, 0, DateTimeKind.Utc)
        });
        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "late-2019",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "jan-2020",
            PublishDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "jan-2020",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "may-2020",
            PublishDate = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc)
        });
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "may-2020",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container4.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "dec-2020",
            PublishDate = new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        });
        var item4 = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "dec-2020",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container5);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container5.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "jan-2021",
            PublishDate = new DateTime(2021, 1, 1, 0, 0, 30, DateTimeKind.Utc)
        });
        var item5 = new ContentHierarchyItem
        {
            MetadataContainerId = container5.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "jan-2021",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        async Task<List<string>> MatchingSlugs(TemporalValue requested)
        {
            var criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new()
                {
                    And =
                    [
                        new()
                        {
                            PublishDate = new()
                            {
                                And = [new() { Operator = CriteriaValueOperator.Equals, Value = requested }]
                            }
                        }
                    ]
                }
            };
            var result = await _subject.QueryAsync(criteria,
                new() { PageNumber = 1, PageSize = 100 },
                null).ConfigureAwait(false);
            return [.. result.Items.Select(i => i.Slug).OrderBy(s => s)];
        }

        // Act
        var yearMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Year
        }).ConfigureAwait(false);
        var monthMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Month
        }).ConfigureAwait(false);
        var dayMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Day
        }).ConfigureAwait(false);
        var instantMatches = await MatchingSlugs(new TemporalValue
        {
            Value = new DateTime(2020, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Instant
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yearMatches, Is.EqualTo(["dec-2020", "jan-2020", "may-2020"]));
            Assert.That(monthMatches, Is.EqualTo(["may-2020"]));
            Assert.That(dayMatches, Is.EqualTo(["may-2020"]));
            Assert.That(instantMatches, Is.EqualTo(["may-2020"]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenGenreIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-action"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-action",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Name = "Action"
        });
        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 1,
            Name = "Thriller"
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-comedy"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-comedy",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Name = "Comedy"
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Genres = new()
                        {
                            Some = [[new GenreCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Action" }]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenMetadataTagIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-favorite"
        });
        var match = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-favorite",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(match);
        var favoriteTag = new Tag { Name = "Favorite" };
        favoriteTag.MetadataContainers.Add(container1);
        _context.Tags.Add(favoriteTag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-archive"
        });
        var nonMatch = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-archive",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(nonMatch);
        var archiveTag = new Tag { Name = "Archive" };
        archiveTag.MetadataContainers.Add(container2);
        _context.Tags.Add(archiveTag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Tags = new()
                        {
                            Some = [[new TagCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Favorite" }]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenAwardIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-oscar"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-oscar",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Awards.Add(new Award
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-razzie"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-razzie",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Awards.Add(new Award
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Body = "Razzies",
            Name = "Worst Picture"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Awards = new()
                        {
                            Some = [[new AwardCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Best Picture" }]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenExternalIdIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "provider-a-type-a"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-a-type-a",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Source = "provider-a",
            Value = "603"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "provider-c-type-a"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "provider-c-type-a",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Source = "provider-c",
            Value = "tt0133093"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
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
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "603" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };
        var sourceCriteria = new ContentHierarchyItemCriteria
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
                                Source = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "provider-c" }]
                                }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var sourceResult = await _subject.QueryAsync(sourceCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        var sourceIds = sourceResult.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
            Assert.That(sourceIds, Does.Not.Contain(match.Id));
            Assert.That(sourceIds, Does.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenContentRatingIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-pg13"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-pg13",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentRatings.Add(new ContentRating
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Body = "MPAA",
            Rating = "PG-13"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-r"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-r",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentRatings.Add(new ContentRating
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Body = "MPAA",
            Rating = "R"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ContentRatings = new()
                        {
                            Some = [[new ContentRatingCriteria
                            {
                                Rating = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "PG-13" }]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenImageIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-poster"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-poster",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri($"https://images.test/{"movie-poster"}.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-backdrop"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-backdrop",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Type = "backdrop",
            Url = new Uri($"https://images.test/{"movie-backdrop"}.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Images = new()
                        {
                            Some = [[new ImageCriteria
                            {
                                Type = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "poster" }]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenParsedImageWidthIsNull()
    {
        // Arrange
        var matchingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var matchingContribution = new MetadataContribution
        {
            MetadataContainer = matchingContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "No width"
        };
        var matchingItem = new ContentHierarchyItem
        {
            MetadataContainer = matchingContainer,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "no-width",
            Name = "No width",
            CreatedAt = DateTime.UtcNow
        };
        matchingContribution.Images.Add(new Image
        {
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/no-width.jpg")
        });

        var otherContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var otherContribution = new MetadataContribution
        {
            MetadataContainer = otherContainer,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Has width"
        };
        var otherItem = new ContentHierarchyItem
        {
            MetadataContainer = otherContainer,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "has-width",
            Name = "Has width",
            CreatedAt = DateTime.UtcNow
        };
        otherContribution.Images.Add(new Image
        {
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/has-width.jpg"),
            Width = 1920
        });
        _context.ContentHierarchyItems.AddRange(matchingItem, otherItem);
        _context.MetadataContributions.AddRange(matchingContribution, otherContribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var parsed = "Images has (Width null)".ParseContentFilter();

        // Act
        var result = await _subject.QueryAsync(
            parsed.Value!.ContentHierarchyItem.And.Single(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([matchingItem.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenImageUrlEqualsCriterionIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-b"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-b.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "https://images.test/poster-a.jpg"
                                            }]
                                        }
                                    }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesMatchingItems_WhenImageUrlNotEqualsCriterionIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-b"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-b.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var excluded = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                        [
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new()
                                            {
                                                Operator = CriteriaValueOperator.NotEquals,
                                                Value = "https://images.test/poster-b.jpg"
                                            }]
                                        }
                                    }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(excluded.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingItems_WhenImageUrlInCriterionIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-b"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Images.Add(new Image
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-b.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var nonMatch = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Images = new()
                        {
                            Some = [[new ImageCriteria
                            {
                                Url = new()
                                {
                                    And =
                                    [
                                        new()
                                        {
                                            Operator = CriteriaValueOperator.In,
                                            Values =
                                            [
                                                "https://images.test/poster-a.jpg",
                                                "https://images.test/poster-c.jpg"
                                            ]
                                        }
                                    ]
                                }
                            }]]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(match.Id));
            Assert.That(ids, Does.Not.Contain(nonMatch.Id));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByGenreNone_WhenExcludesMatchingItems()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-horror"
        };
        _context.MetadataContributions.Add(contribution1);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-horror",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Name = "Horror"
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var horror = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution1.MetadataContainerId).ConfigureAwait(false);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-drama"
        };
        _context.MetadataContributions.Add(contribution2);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-drama",
            Name = "Pending refresh",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Name = "Drama"
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var drama = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution2.MetadataContainerId).ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Genres = new()
                        {
                            None = [new GenreCriteria
                            {
                                Name = new()
                                {
                                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Horror" }]
                                }
                            }]
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
        var ids = result.Items.Select(i => i.Id).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(drama.Id));
            Assert.That(ids, Does.Not.Contain(horror.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByLevel()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-c",
            Level = "level-b",
            Slug = "series-1",
            Name = "Series 1",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-1",
            Name = "Season 1",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Level = new()
            {
                And = [new() { Value = "level-b" }]
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
            Assert.That(items, Has.All.Matches<ContentHierarchyItem>(i => i.Level == "level-b"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsCanonicalLevelAItem_WhenContentTypeAndLevelMatch()
    {
        // Arrange
        var movieContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(movieContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var levelAItem = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "Item A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var movie = new ContentHierarchyItem
        {
            MetadataContainerId = movieContainer.Id,
            ContentType = "type-a",
            Level = "level-b",
            Slug = "movie",
            Name = "Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(levelAItem, movie);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "type-a" }]
            },
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "level-a" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([levelAItem.Id]));
            Assert.That(result.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByMonitoring()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "active-movie",
            Name = "Active Movie",
            Monitoring = MonitoringMode.Monitored,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "excluded-movie",
            Name = "Excluded Movie",
            Monitoring = MonitoringMode.Unmonitored,
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria { EffectiveMonitoring = true };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(1).Items);
            Assert.That(
                items,
                Has.All.Matches<ContentHierarchyItem>(i => i.EffectiveMonitoring == MonitoringMode.Monitored));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByFulfillment()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var missingWithoutContentItem = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "missing-without-content-item",
            Name = "Missing Without Content Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var missingWithContentItem = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "missing-with-content-item",
            Name = "Missing With Content Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var fulfilled = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "fulfilled",
            Name = "Fulfilled",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(missingWithoutContentItem, missingWithContentItem, fulfilled);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = missingWithContentItem.Id,
                Fulfillment = FulfillmentStatus.Missing,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = fulfilled.Id,
                Fulfillment = FulfillmentStatus.Fulfilled,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Fulfillment = new()
            {
                And = [new() { Value = FulfillmentStatus.Missing }]
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
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(
                items,
                Has.One.With.Property(nameof(ContentHierarchyItem.Name))
                    .EqualTo("Missing Without Content Item"));
            Assert.That(
                items,
                Has.One.With.Property(nameof(ContentHierarchyItem.Name))
                    .EqualTo("Missing With Content Item"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByTag()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var taggedA = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tagged-a",
            Name = "Tagged A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var taggedB = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tagged-b",
            Name = "Tagged B",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var untagged = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "untagged",
            Name = "Untagged",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(taggedA, taggedB, untagged);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var tag = new Tag { Name = "Favorites" };
        var firstContainer = await _context.MetadataContainers.SingleAsync(c => c.Id == _metadataContainerId)
            .ConfigureAwait(false);
        tag.MetadataContainers.Add(firstContainer);
        tag.MetadataContainers.Add(container2);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Tags = new()
            {
                And = [new() { Value = "Favorites" }]
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
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Tagged A"));
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Tagged B"));
            Assert.That(items, Has.None.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Untagged"));
        }
    }

    [Test]
    public async Task QueryAsync_PreservesResolvedPageOrder_WhenFiltersAndSortAreSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = container1,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-delta",
            Name = "Movie Delta",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = container2,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-alpha",
            Name = "Movie Alpha",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = container3,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-echo",
            Name = "Movie Echo",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = container4,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-bravo",
            Name = "Movie Bravo",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container5);
        _context.ContentHierarchyItems.Add(new()
        {
            MetadataContainer = container5,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-charlie",
            Name = "Movie Charlie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Name = new()
            {
                And = [new() { Value = "Movie", Operator = CriteriaValueOperator.ContainsString }]
            }
        };
        var sort = new SortParameter { PropertyName = "Name", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 2, PageSize = 2 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Select(item => item.Name), Is.EqualTo(["Movie Charlie", "Movie Delta"]));
            Assert.That(result.TotalItems, Is.EqualTo(5));
            Assert.That(result.PageNumber, Is.EqualTo(2));
            Assert.That(result.PageSize, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByName()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "action-movie",
            Name = "Action Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "comedy-movie",
            Name = "Comedy Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Name = new()
            {
                And = [new() { Value = "Action", Operator = CriteriaValueOperator.ContainsString }]
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
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Name)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringBySlug()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "specific-slug",
            Name = "Specific Slug Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "other-slug",
            Name = "Other Slug Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Slug = new()
            {
                And = [new() { Value = "specific-slug" }]
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
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Slug)).EqualTo("specific-slug"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredItems_WhenFilteringByPath()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "path-movie-1",
            Name = "Path Movie 1",
            Monitoring = MonitoringMode.Monitored,
            Path = "/movies/action/test",
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "path-movie-2",
            Name = "Path Movie 2",
            Monitoring = MonitoringMode.Monitored,
            Path = "/movies/comedy/test",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Path = new()
            {
                And = [new() { Value = "action", Operator = CriteriaValueOperator.ContainsString }]
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
            Assert.That(items, Has.One.With.Property(nameof(ContentHierarchyItem.Path)).Contains("action"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTopmostMonitoredNode_WhenFilteringByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria { EffectiveMonitoring = true, HasMonitoredParent = false },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([show.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsOverrideBoundary_WhenFilteringByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var unmonitoredShow = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(unmonitoredShow);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var monitoredSeason = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = unmonitoredShow.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(monitoredSeason);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria { EffectiveMonitoring = true, HasMonitoredParent = false },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([monitoredSeason.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsMonitoredDescendant_WhenFilteringByHasMonitoredParent()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(showContainer, seasonContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var show = new ContentHierarchyItem
        {
            MetadataContainerId = showContainer.Id,
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(show);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var season = new ContentHierarchyItem
        {
            MetadataContainerId = seasonContainer.Id,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "s1",
            Name = "Season 1",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(season);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ContentHierarchyItemCriteria { HasMonitoredParent = true },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(i => i.Id), Is.EquivalentTo([season.Id]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheMatchingItems_WhenAnExternalIdGroupHoldsSixHundredAlternatives()
    {
        // Arrange
        var seededIds = new List<Guid>();
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution1 = new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "id-7"
        };
        _context.MetadataContributions.Add(contribution1);
        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "id-7",
            Name = "id-7",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution1.Id,
            Ordinal = 0,
            Source = "provider-a",
            Value = "id-7"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        seededIds.Add(item1.Id);
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution2 = new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "id-590"
        };
        _context.MetadataContributions.Add(contribution2);
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "id-590",
            Name = "id-590",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution2.Id,
            Ordinal = 0,
            Source = "provider-a",
            Value = "id-590"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        seededIds.Add(item2.Id);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        ExternalIds = new()
                        {
                            Some =
                            [
                                [
                                    .. Enumerable.Range(1, 600).Select(index => new ExternalIdCriteria
                                    {
                                        Source = new() { And = [new() { Value = "provider-a" }] },
                                        Value = new() { And = [new() { Value = $"id-{index}" }] }
                                    })
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, new() { PageNumber = 1, PageSize = 600 }, null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo(seededIds));
    }

    [TestCase("adorable")]
    [TestCase("ADORABLE")]
    public async Task QueryAsync_FiltersByGenreName_WhenTheCaseDiffers(string requested)
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contribution = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Adorable movie"
        };
        _context.MetadataContributions.Add(contribution);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "adorable-movie",
            Name = "Adorable movie",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre { MetadataContributionId = contribution.Id, Ordinal = 0, Name = "Adorable" });
        var otherContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var otherContribution = new MetadataContribution
        {
            MetadataContainerId = otherContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Dull movie"
        };
        _context.MetadataContributions.Add(otherContribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = otherContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "dull-movie",
            Name = "Dull movie",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Genres.Add(new Genre { MetadataContributionId = otherContribution.Id, Ordinal = 0, Name = "Dull" });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Genres = new()
                        {
                            Some = [[new GenreCriteria { Name = new() { And = [new() { Value = requested }] } }]]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, new() { PageNumber = 1, PageSize = 10 }, null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(found => found.Id), Is.EqualTo([item.Id]));
    }

    [Test]
    public async Task QueryAsync_FiltersByContentType_WhenTheCaseDiffers()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "upper-case-query",
            Name = "Upper case query",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        var otherContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(otherContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = otherContainer.Id,
            ContentType = "type-b",
            Level = "level-a",
            Slug = "other-query",
            Name = "Other query",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = "TYPE-A" }] },
            Level = new() { And = [new() { Value = "Level-A" }] },
            Slug = new() { And = [new() { Value = "UPPER-CASE-QUERY" }] }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, new() { PageNumber = 1, PageSize = 10 }, null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(found => found.Id), Is.EqualTo([item.Id]));
    }

    [TestCase(CriteriaValueOperator.StartsWithString, "22 jump", "movie-jump")]
    [TestCase(CriteriaValueOperator.EndsWithString, "STREET", "movie-jump")]
    [TestCase(CriteriaValueOperator.StartsWithString, "2 FAST", "movie-fast")]
    [TestCase(CriteriaValueOperator.EndsWithString, "furious", "movie-fast")]
    public async Task QueryAsync_MatchesMetadataTitleWithoutCase_WhenOperatorIsPrefixOrSuffix(
        CriteriaValueOperator criteriaOperator,
        string value,
        string expectedSlug)
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "22 Jump Street"
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-jump",
            Name = "Pending refresh",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.MetadataContributions.Add(new MetadataContribution
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "2 Fast 2 Furious"
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-fast",
            Name = "Pending refresh",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            Metadata = new()
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new() { And = [new() { Operator = criteriaOperator, Value = value }] }
                    }
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var slugs = result.Items.Select(item => item.Slug).ToList();
        Assert.That(slugs, Is.EqualTo([expectedSlug]));
    }

    [TestCase("mediaFiles has (fileKind = Video and fileSize > 1000)", new[] { "large-video", "single-video" })]
    [TestCase(
        "mediaFiles has (fileKind = Video) and mediaFiles has (fileSize > 1000)",
        new[] { "large-video", "single-video", "split" })]
    [TestCase(
        "mediaFiles !has (fileKind = Subtitle)",
        new[] { "large-video", "no-content-item", "no-files", "single-video" })]
    [TestCase(
        "mediaFiles has (fileSize >= 10000) or mediaFiles has (fileKind = Subtitle)",
        new[] { "large-video", "split" })]
    [TestCase("mediaFiles has (filePath endswith \".SRT\" and mimeType startswith \"text/\")", new[] { "split" })]
    [TestCase("mediaFiles has (fileSize < 1000)", new[] { "split" })]
    [TestCase("mediaFile has \"video\"", new[] { "large-video", "single-video", "split" })]
    public async Task QueryAsync_MatchesConditionsOnTheSameFile_WhenFilteringByMediaFiles(
        string query,
        string[] expectedSlugs)
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        var contentItem1 = new ContentItem { CreatedAt = DateTime.UtcNow };
        contentItem1.MediaFiles.Add(new MediaFile
        {
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = "/lib/a.mkv",
            PathKey = "/lib/a.mkv",
            FileSize = 2000L,
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainer = container1,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "single-video",
            Name = "single-video",
            CreatedAt = DateTime.UtcNow,
            ContentItem = contentItem1
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        var contentItem2 = new ContentItem { CreatedAt = DateTime.UtcNow };
        contentItem2.MediaFiles.Add(new MediaFile
        {
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = "/lib/b.mkv",
            PathKey = "/lib/b.mkv",
            FileSize = 500L,
            CreatedAt = DateTime.UtcNow
        });
        contentItem2.MediaFiles.Add(new MediaFile
        {
            FileKind = "Subtitle",
            MimeType = "text/x-subrip",
            FilePath = "/lib/b.srt",
            PathKey = "/lib/b.srt",
            FileSize = 2000L,
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainer = container2,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "split",
            Name = "split",
            CreatedAt = DateTime.UtcNow,
            ContentItem = contentItem2
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        var contentItem3 = new ContentItem { CreatedAt = DateTime.UtcNow };
        contentItem3.MediaFiles.Add(new MediaFile
        {
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = "/lib/c.mp4",
            PathKey = "/lib/c.mp4",
            FileSize = 10000L,
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainer = container3,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "large-video",
            Name = "large-video",
            CreatedAt = DateTime.UtcNow,
            ContentItem = contentItem3
        });
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        var contentItem4 = new ContentItem { CreatedAt = DateTime.UtcNow };
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainer = container4,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "no-files",
            Name = "no-files",
            CreatedAt = DateTime.UtcNow,
            ContentItem = contentItem4
        });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "no-content-item",
            Name = "no-content-item",
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var criteria = query.ParseContentFilter().Value!.ContentHierarchyItem.And.Single();

        // Act
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug), Is.EquivalentTo(expectedSlugs));
    }

    [Test]
    public async Task BulkPatchAsync_PatchesOnlyMatchingItems_WhenFilterUsesMediaFiles()
    {
        // Arrange
        var subtitled = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "subtitled",
            Name = "Subtitled",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new ContentItem
            {
                CreatedAt = DateTime.UtcNow,
                MediaFiles =
                {
                    new MediaFile
                    {
                        FileKind = "Subtitle",
                        FilePath = "/lib/s.srt",
                        PathKey = "/lib/s.srt",
                        FileSize = 10,
                        CreatedAt = DateTime.UtcNow
                    }
                }
            }
        };
        var plain = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "type-a",
            Level = "level-a",
            Slug = "plain",
            Name = "Plain",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new ContentItem
            {
                CreatedAt = DateTime.UtcNow,
                MediaFiles =
                {
                    new MediaFile
                    {
                        FileKind = "Video",
                        FilePath = "/lib/p.mkv",
                        PathKey = "/lib/p.mkv",
                        FileSize = 10,
                        CreatedAt = DateTime.UtcNow
                    }
                }
            }
        };
        _context.ContentHierarchyItems.AddRange(subtitled, plain);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var patch = new BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>
        {
            Filter = "mediaFiles has (fileKind = Subtitle)".ParseContentFilter().Value!
                .ContentHierarchyItem.And.Single(),
            Patch = new ContentHierarchyItemPatch { Monitoring = new() { Value = MonitoringMode.Unmonitored } }
        };

        // Act
        await _subject.BulkPatchAsync(patch, null).ConfigureAwait(false);

        // Assert
        var monitoring = await _context.ContentHierarchyItems.AsNoTracking()
            .ToDictionaryAsync(item => item.Slug, item => item.Monitoring)
            .ConfigureAwait(false);
        Assert.That(
            monitoring,
            Is.EquivalentTo(new Dictionary<string, MonitoringMode?>
            {
                ["subtitled"] = MonitoringMode.Unmonitored,
                ["plain"] = MonitoringMode.Monitored
            }));
    }
}
