using System;
using System.Collections.Generic;
using System.Data.Common;
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
public sealed class ContentHierarchyItemRepositoryQueryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;
    private Guid _metadataContainerId;
    private Mock<DbCommandInterceptor> _commands = null!;

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
        _commands = new Mock<DbCommandInterceptor> { CallBase = true };
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options
            .UseSqlite(connection, contextOwnsConnection: true)
            .AddInterceptors(_commands.Object)
            .Options);
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
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsContentHierarchyItem_WhenExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "test-movie",
            Name = "Test Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("type-a"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("level-a"));
            Assert.That(result, Has.Property(nameof(result.Monitoring)).EqualTo(MonitoringMode.Monitored));
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
    public async Task QueryAsync_OmitsTheItem_WhenItIsDeletedBetweenThePagedReads()
    {
        // Arrange
        var vanishingContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(vanishingContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                MetadataContainerId = _metadataContainerId,
                ContentType = "type-a",
                Level = "level-a",
                Slug = "keeper",
                Name = "Keeper",
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainerId = vanishingContainer.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = "vanishing",
                Name = "Vanishing",
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var deleted = 0;
        var pageReads = 0;
        _commands.Setup(interceptor => interceptor.DataReaderClosingAsync(
                It.Is<DbCommand>(command => command.CommandText.Contains("LIMIT", StringComparison.Ordinal)),
                It.Is<DataReaderClosingEventData>(data =>
                    data.DataReader.FieldCount == 1 && data.DataReader.GetName(0) == "Id"),
                It.IsAny<InterceptionResult>()))
            .Callback((DbCommand _, DataReaderClosingEventData data, InterceptionResult _) =>
            {
                pageReads++;
                deleted = _context.ContentHierarchyItems.Where(item => item.Slug == "vanishing").ExecuteDelete();
            })
            .ReturnsAsync(default(InterceptionResult));

        // Act
        var result = await _subject.QueryAsync(
            new ContentHierarchyItemCriteria(),
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Slug), Is.EqualTo(["keeper"]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(pageReads, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheAncestorFields_WhenSelectingByIds()
    {
        // Arrange
        var childContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(childContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var parent = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "parent",
            Name = "Parent",
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(parent);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var child = new ContentHierarchyItem
        {
            MetadataContainerId = childContainer.Id,
            ParentId = parent.Id,
            ContentType = "type-b",
            Level = "level-b",
            Slug = "child",
            Name = "Child",
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(child);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(new ContentHierarchyItemCriteria { Ids = [child.Id] },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        var node = result.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.Id, Is.EqualTo(child.Id));
            Assert.That(node.ParentId, Is.EqualTo(parent.Id));
            Assert.That(node.ContentType, Is.EqualTo("type-b"));
            Assert.That(node.Level, Is.EqualTo("level-b"));
            Assert.That(node.Name, Is.EqualTo("Child"));
            Assert.That(node.EffectiveMonitoring, Is.EqualTo(MonitoringMode.Unmonitored));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheTagNamesOfEachItem_WhenItemsHaveTags()
    {
        // Arrange
        var untaggedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(untaggedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var tagged = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "tagged",
            Name = "Tagged",
            CreatedAt = DateTime.UtcNow
        };
        var untagged = new ContentHierarchyItem
        {
            MetadataContainerId = untaggedContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "untagged",
            Name = "Untagged",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(tagged, untagged);
        var container = await _context.MetadataContainers
            .SingleAsync(c => c.Id == _metadataContainerId)
            .ConfigureAwait(false);
        var tag1 = new Tag { Name = "zeta" };
        tag1.MetadataContainers.Add(container);
        _context.Tags.Add(tag1);
        var tag2 = new Tag { Name = "alpha" };
        tag2.MetadataContainers.Add(container);
        _context.Tags.Add(tag2);

        _context.Tags.Add(new Tag { Name = "unused" });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria(),
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        var tagsById = result.Items.ToDictionary(
            item => item.Id,
            item => item.Tags);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tagsById[tagged.Id], Is.EqualTo((string[])["alpha", "zeta"]));
            Assert.That(tagsById[untagged.Id], Is.Empty);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsContentHierarchyItem_WhenSlugExists()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "test-movie-slug",
            Name = "Test Movie Slug",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new()
            {
                ContentType = new() { And = [new() { Value = "type-a" }] },
                Level = new() { And = [new() { Value = "level-a" }] },
                Slug = new() { And = [new() { Value = "test-movie-slug" }] }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Test Movie Slug"));
            Assert.That(result, Has.Property(nameof(result.Slug)).EqualTo("test-movie-slug"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo("type-a"));
            Assert.That(result, Has.Property(nameof(result.Level)).EqualTo("level-a"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsNull_WhenSlugDoesNotExist()
    {
        // Act
        var result = (await _subject.QueryAsync(
            new()
            {
                ContentType = new() { And = [new() { Value = "type-a" }] },
                Level = new() { And = [new() { Value = "level-a" }] },
                Slug = new() { And = [new() { Value = "nonexistent-slug" }] }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false)).Items.SingleOrDefault();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllItems_WhenItemsExist()
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
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-2",
            Name = "Movie 2",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoItems()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_OrdersChildrenBySlugAscending_WhenNamesAndSlugsHaveDifferentOrders()
    {
        // Arrange
        var parentContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(parentContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var series = new ContentHierarchyItem
        {
            MetadataContainerId = parentContainer.Id,
            ContentType = "type-f",
            Level = "level-b",
            Slug = "ordering-series",
            Name = "Ordering Series",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(series);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ParentId = series.Id,
            ContentType = "type-f",
            Level = "level-d",
            Slug = "s01e02",
            Name = "Cancer Man",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ParentId = series.Id,
            ContentType = "type-f",
            Level = "level-d",
            Slug = "s01e03",
            Name = "Cat's in the Bag",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ParentId = series.Id,
            ContentType = "type-f",
            Level = "level-d",
            Slug = "s01e01",
            Name = "And the Bag's in the River",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new ContentHierarchyItemCriteria
        {
            ParentId = new() { And = [new() { Value = series.Id }] }
        };
        var sort = new SortParameter
        {
            PropertyName = nameof(ContentHierarchyItem.Slug),
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var slugs = result.Items.Select(i => i.Slug).ToList();
        Assert.That(slugs, Is.EqualTo(["s01e01", "s01e02", "s01e03"]));
    }

    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString)]
    public async Task QueryAsync_ThrowsTypedRejection_WhenImageUrlSubstringOperatorIsSet(CriteriaValueOperator op)
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
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
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
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _ = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);

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
                                        Url = new() { And = [new() { Operator = op, Value = "images.test" }] }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Act
        var act = (AsyncTestDelegate)(async () => await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<ArgumentException>(act);
    }

    [TestCase("images.test")]
    [TestCase("not a url")]
    public async Task QueryAsync_ThrowsTypedRejection_WhenImageUrlValueIsNotAbsolute(string value)
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
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
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
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _ = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == contribution.MetadataContainerId).ConfigureAwait(false);

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
                                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = value }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        // Act
        var act = (AsyncTestDelegate)(async () => await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Test]
    public async Task QueryAsync_ReturnsItemsWithImages_WhenImageUrlIsNotNull()
    {
        // Arrange
        var imageContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(imageContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var imageContribution = new MetadataContribution
        {
            MetadataContainerId = imageContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-a"
        };
        _context.MetadataContributions.Add(imageContribution);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = imageContainer.Id,
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
            MetadataContributionId = imageContribution.Id,
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://images.test/poster-a.jpg")
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var match = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == imageContribution.MetadataContainerId).ConfigureAwait(false);

        var noImageContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(noImageContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var noImage = new MetadataContribution
        {
            MetadataContainerId = noImageContainer.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "movie-no-image"
        };
        _context.MetadataContributions.Add(noImage);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = noImageContainer.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-no-image",
            Name = "Pending refresh",
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
                            new MetadataCriteria
                            {
                                Images = new()
                                {
                                    Some = [[new ImageCriteria
                                    {
                                        Url = new()
                                        {
                                            And = [new() { Operator = CriteriaValueOperator.IsNotNull, Value = null }]
                                        }
                                    }]]
                                }
                            }
                        ]
            }
        };

        var excluded = await _context.ContentHierarchyItems.AsNoTracking()
            .FirstAsync(i => i.MetadataContainerId == noImage.MetadataContainerId).ConfigureAwait(false);

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
    public async Task QueryAsync_ReturnsPagedItems_WhenPagingIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-0",
            Name = "Movie 0",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item1);
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-1",
            Name = "Movie 1",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item2);
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-2",
            Name = "Movie 2",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item3);
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item4 = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-3",
            Name = "Movie 3",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item4);
        var container5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item5 = new ContentHierarchyItem
        {
            MetadataContainerId = container5.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-4",
            Name = "Movie 4",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item5);

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(5));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(1));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryIdsAsync_ReturnsEveryRemainingId_WhenAnEarlierItemIsDeletedBetweenCursorPages()
    {
        // Arrange
        var id0 = Guid.Parse("27f65cd0-25de-48b2-98d1-e510e9371339");
        var id1 = Guid.Parse("2ab79b34-816f-46d4-a516-2f8ce05995dc");
        var id2 = Guid.Parse("5471cd4e-c856-4e65-9f8f-69a52f8e8e6d");
        var id3 = Guid.Parse("902867a0-6825-4b33-8982-a336478ea265");
        var id4 = Guid.Parse("b53dd6ab-80d1-47af-9495-7096cae03857");
        var container0 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container0);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id0,
            MetadataContainerId = container0.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-0",
            Name = "Item 5",
            CreatedAt = DateTime.UtcNow
        });
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id1,
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-1",
            Name = "Item 4",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id2,
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-2",
            Name = "Item 3",
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id3,
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-3",
            Name = "Item 2",
            CreatedAt = DateTime.UtcNow
        });
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id4,
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-4",
            Name = "Item 1",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var sort = new SortParameter { PropertyName = "Id", Direction = SortDirection.Ascending };
        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var first = await _subject.QueryIdsAsync(new(), paging, sort).ConfigureAwait(false);
        await _context.ContentHierarchyItems.Where(item => item.Id == id0).ExecuteDeleteAsync().ConfigureAwait(false);
        var second = await _subject.QueryIdsAsync(new() { AfterId = id1 }, paging, sort).ConfigureAwait(false);
        var third = await _subject.QueryIdsAsync(new() { AfterId = id3 }, paging, sort).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo([id0, id1]));
            Assert.That(second, Is.EqualTo([id2, id3]));
            Assert.That(third, Is.EqualTo([id4]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheItemsAfterTheId_WhenAfterIdIsSet()
    {
        // Arrange
        var id0 = Guid.Parse("27f65cd0-25de-48b2-98d1-e510e9371339");
        var id1 = Guid.Parse("2ab79b34-816f-46d4-a516-2f8ce05995dc");
        var id2 = Guid.Parse("5471cd4e-c856-4e65-9f8f-69a52f8e8e6d");
        var container0 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container0);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id0,
            MetadataContainerId = container0.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-0",
            Name = "Item 3",
            CreatedAt = DateTime.UtcNow
        });
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id1,
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-1",
            Name = "Item 2",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = id2,
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-2",
            Name = "Item 1",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync().ConfigureAwait(false);
        var sort = new SortParameter { PropertyName = "Id", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(
            new() { AfterId = id0 },
            new() { PageNumber = 1, PageSize = 25 },
            sort).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([id1, id2]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsSortedItems_WhenSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-b",
            Name = "B Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-a",
            Name = "A Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "movie-c",
            Name = "C Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "Name", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("A Movie"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("B Movie"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("C Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByCamelCasePropertyName_WhenCamelCaseSortPropertyIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "first",
            Name = "First",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "third",
            Name = "Third",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "second",
            Name = "Second",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "createdAt", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Third"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Second"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("First"));
        }
    }

    [Test]
    public async Task QueryAsync_FallsBackToDefaultOrder_WhenUnknownSortPropertyIsRequested()
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
            Slug = "beta",
            Name = "Beta",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "alpha",
            Name = "Alpha",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "doesNotExist",
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Alpha"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Beta"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByMaterializedMetadataPublishDate_WhenPublishDateSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            PublishDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            PublishDate = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "later",
            Name = "Later",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "earlier",
            Name = "Earlier",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "publishDate", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Earlier"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Later"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsNullSequenceNumbersLast_WhenSequenceNumberSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            SequenceNumber = 10,
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            SequenceNumber = 2,
            CreatedAt = DateTime.UtcNow
        });
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container4.Contributions.Add(new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Unnumbered",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container2, container3, container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var seasonTen = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-10",
            Name = "Season 10",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var seasonTwo = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "season-2",
            Name = "Season 2",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var unnumbered = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-c",
            Level = "level-c",
            Slug = "special",
            Name = "Special",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(seasonTen, seasonTwo, unnumbered);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = nameof(MaterializedMetadata.SequenceNumber),
            Direction = SortDirection.Ascending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Season 2"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Season 10"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Special"));
        }
    }

    [TestCase(SortDirection.Ascending, "Early", "Late")]
    [TestCase(SortDirection.Descending, "Late", "Early")]
    public async Task QueryAsync_SortedByFulfilledAt_WhenDatesAreUnknown(
        SortDirection direction,
        string first,
        string second)
    {
        // Arrange
        var containers = new[]
        {
            new MetadataContainer { CreatedAt = DateTime.UtcNow },
            new MetadataContainer { CreatedAt = DateTime.UtcNow }
        };
        _context.MetadataContainers.AddRange(containers);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var items = new[]
        {
            new ContentHierarchyItem
            {
                MetadataContainerId = _metadataContainerId,
                ContentType = "type-a",
                Level = "level-a",
                Slug = "early",
                Name = "Early",
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainerId = containers[0].Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = "late",
                Name = "Late",
                CreatedAt = DateTime.UtcNow
            },
            new ContentHierarchyItem
            {
                MetadataContainerId = containers[1].Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = "unknown",
                Name = "Unknown",
                CreatedAt = DateTime.UtcNow
            }
        };
        _context.ContentHierarchyItems.AddRange(items);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = items[0].Id,
                Fulfillment = FulfillmentStatus.Fulfilled,
                FulfilledAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = items[1].Id,
                Fulfillment = FulfillmentStatus.Fulfilled,
                FulfilledAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new(),
            new() { PageNumber = 1, PageSize = 100 },
            new() { PropertyName = nameof(ContentItem.FulfilledAt), Direction = direction }).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Name), Is.EqualTo([first, second, "Unknown"]));
    }

    [Test]
    public async Task QueryAsync_SortsByContentItemDateLastSearched_WhenLastSearchedSortIsSet()
    {
        // Arrange
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container5 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3, container4, container5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item1 = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "middle",
            Name = "Middle",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item2 = new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "latest",
            Name = "Latest",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item3 = new ContentHierarchyItem
        {
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "earliest",
            Name = "Earliest",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item4 = new ContentHierarchyItem
        {
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "no-content-item",
            Name = "No Content Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var item5 = new ContentHierarchyItem
        {
            MetadataContainerId = container5.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched",
            Name = "Unsearched",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(item1, item2, item3, item4, item5);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = item1.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item2.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item3.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = item5.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "dateLastSearched",
            Direction = SortDirection.Descending
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(5));
            Assert.That(items, Has.ItemAt(0).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Latest"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Middle"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Earliest"));
            Assert.That(items, Has.ItemAt(3).Property(nameof(ContentHierarchyItem.Name)).EqualTo("No Content Item"));
            Assert.That(items, Has.ItemAt(4).Property(nameof(ContentHierarchyItem.Name)).EqualTo("Unsearched"));
        }
    }

    [TestCase(SortDirection.Ascending)]
    [TestCase(SortDirection.Descending)]
    public async Task QueryAsync_UsesIdAsFinalTieBreaker_WhenLastSearchedDatesMatch(SortDirection direction)
    {
        // Arrange
        var searchedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var searchedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var unsearchedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var unsearchedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var container4 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(container2, container3, container4);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var searchedHigher = new ContentHierarchyItem
        {
            Id = searchedHigherId,
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "searched-higher",
            Name = "Duplicate",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var searchedLower = new ContentHierarchyItem
        {
            Id = searchedLowerId,
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "searched-lower",
            Name = "Duplicate",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var unsearchedHigher = new ContentHierarchyItem
        {
            Id = unsearchedHigherId,
            MetadataContainerId = container3.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched-higher",
            Name = "Duplicate",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var unsearchedLower = new ContentHierarchyItem
        {
            Id = unsearchedLowerId,
            MetadataContainerId = container4.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "unsearched-lower",
            Name = "Duplicate",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(searchedHigher, searchedLower, unsearchedHigher, unsearchedLower);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var searchedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.ContentItems.AddRange(
            new ContentItem
            {
                ContentHierarchyItemId = searchedHigher.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = searchedAt,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = searchedLower.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = searchedAt,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = unsearchedHigher.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            },
            new ContentItem
            {
                ContentHierarchyItemId = unsearchedLower.Id,
                Fulfillment = FulfillmentStatus.Missing,
                DateLastSearched = null,
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter
        {
            PropertyName = "dateLastSearched",
            Direction = direction
        };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.That(ids, Is.EqualTo([searchedLowerId, searchedHigherId, unsearchedLowerId, unsearchedHigherId]));
    }

    [Test]
    public async Task QueryAsync_ReturnsTheDescendantsWithFiles_WhenFilesHaveAnEmptyCriterion()
    {
        // Arrange
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "level",
            Slug = "show",
            Name = "show",
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "level",
            Slug = "season",
            Name = "season",
            CreatedAt = DateTime.UtcNow
        };
        var emptyEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = season.Id,
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "level",
            Slug = "empty-episode",
            Name = "empty-episode",
            CreatedAt = DateTime.UtcNow
        };
        var otherShow = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            MetadataContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow },
            ContentType = "tv",
            Level = "level",
            Slug = "other-show",
            Name = "other-show",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(show, season, emptyEpisode, otherShow);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var contentItem1 = new ContentItem { ContentHierarchyItemId = show.Id, CreatedAt = DateTime.UtcNow };
        contentItem1.MediaFiles.Add(new MediaFile
        {
            FileKind = "video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{show.Slug}.mp4",
            PathKey = $"/tv/{show.Slug}.mp4",
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentItems.Add(contentItem1);
        var contentItem2 = new ContentItem { ContentHierarchyItemId = season.Id, CreatedAt = DateTime.UtcNow };
        contentItem2.MediaFiles.Add(new MediaFile
        {
            FileKind = "video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{season.Slug}.mp4",
            PathKey = $"/tv/{season.Slug}.mp4",
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentItems.Add(contentItem2);
        var contentItem3 = new ContentItem { ContentHierarchyItemId = otherShow.Id, CreatedAt = DateTime.UtcNow };
        contentItem3.MediaFiles.Add(new MediaFile
        {
            FileKind = "video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{otherShow.Slug}.mp4",
            PathKey = $"/tv/{otherShow.Slug}.mp4",
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentItems.Add(contentItem3);

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    AncestorId = show.Id,
                    MediaFiles = new() { Some = [[new MediaFileCriteria()]] }
                },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Id), Is.EqualTo([season.Id]));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsOrderedFiles_WhenAudiobookHasSeveralFiles()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "book",
            Level = "audiobook",
            Slug = "multi-file-audiobook",
            Name = "Multi-file Audiobook",
            CreatedAt = DateTime.UtcNow
        };
        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            CreatedAt = DateTime.UtcNow
        };
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/10 - Chapter 10.mp3",
            PathKey = "/library/audiobooks/book/10 - Chapter 10.mp3",
            CreatedAt = DateTime.UtcNow
        });
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/2 - Chapter 2.mp3",
            PathKey = "/library/audiobooks/book/2 - Chapter 2.mp3",
            CreatedAt = DateTime.UtcNow
        });
        contentItem.MediaFiles.Add(new MediaFile
        {
            FileKind = "audio",
            MimeType = "audio/mpeg",
            FilePath = "/library/audiobooks/book/1 - Chapter 1.mp3",
            PathKey = "/library/audiobooks/book/1 - Chapter 1.mp3",
            CreatedAt = DateTime.UtcNow
        });
        _context.ContentHierarchyItems.Add(item);
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ContentItem, Is.Not.Null);
            Assert.That(
                result.ContentItem!.MediaFiles.Select(file => file.FilePath),
                Is.EqualTo(
                [
                    "/library/audiobooks/book/1 - Chapter 1.mp3",
                    "/library/audiobooks/book/2 - Chapter 2.mp3",
                    "/library/audiobooks/book/10 - Chapter 10.mp3"
                ]));
        }
    }

    [TestCase("episode-1", 1, 0)]
    [TestCase("episode-2", 0, 1)]
    public async Task GetByIdAsync_ReturnsTheSharedFileWithItsCoverageAndNoPendingImport_WhenAFileCoversTwoEpisodes(
        string slug,
        int ownedFiles,
        int coveringFiles)
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow
        };
        var firstEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var secondEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.AddRange(season, firstEpisode, secondEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var mediaFile = new MediaFile
        {
            ContentItemId = firstEpisode.ContentItem.Id,
            FileKind = "video",
            FilePath = "/tv/S01E01-E02.mkv",
            PathKey = "/tv/S01E01-E02.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = secondEpisode.ContentItem.Id });
        _context.MediaFiles.Add(mediaFile);
        var rootFolder = new RootFolder
        {
            Name = "TV",
            Path = "/tv",
            ContentType = "tv",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.LibraryImportCandidates.AddRange(
            new LibraryImportCandidate
            {
                RootFolderId = rootFolder.Id,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/tv/S01E01.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = firstEpisode.Id
            },
            new LibraryImportCandidate
            {
                RootFolderId = rootFolder.Id,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/tv/S01E02.mkv",
                ContentType = "tv",
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = secondEpisode.Id
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var targetId = await _context.ContentHierarchyItems
            .Where(item => item.Slug == slug)
            .Select(item => item.Id)
            .SingleAsync()
            .ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(targetId).ConfigureAwait(false);

        // Assert
        var shared = result!.ContentItem!.MediaFiles.Concat(result.ContentItem.CoveringMediaFiles).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ContentItem.MediaFiles, Has.Count.EqualTo(ownedFiles));
            Assert.That(result.ContentItem.CoveringMediaFiles, Has.Count.EqualTo(coveringFiles));
            Assert.That(shared.FilePath, Is.EqualTo("/tv/S01E01-E02.mkv"));
            Assert.That(
                shared.Coverage.Select(coverage => coverage.ContentItemId),
                Is.EqualTo([secondEpisode.ContentItem.Id]));
            Assert.That(result.ImportPending, Is.False);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTheCoveredEpisode_WhenFilesHaveAnEmptyCriterion()
    {
        // Arrange
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = _metadataContainerId,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow
        };
        var firstEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var secondEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-2",
            Name = "Episode 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        var thirdEpisode = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-3",
            Name = "Episode 3",
            Ordinal = 3,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { CreatedAt = DateTime.UtcNow }
        };
        _context.ContentHierarchyItems.AddRange(season, firstEpisode, secondEpisode, thirdEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var mediaFile = new MediaFile
        {
            ContentItemId = firstEpisode.ContentItem.Id,
            FileKind = "video",
            FilePath = "/tv/S01E01-E02.mkv",
            PathKey = "/tv/S01E01-E02.mkv",
            CreatedAt = DateTime.UtcNow
        };
        mediaFile.Coverage.Add(new() { ContentItemId = secondEpisode.ContentItem.Id });
        _context.MediaFiles.Add(mediaFile);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject
            .QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    AncestorId = season.Id,
                    MediaFiles = new() { Some = [[new MediaFileCriteria()]] }
                },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug), Is.EquivalentTo(["episode-1", "episode-2"]));
    }

    [Test]
    public async Task GetStatsAsync_ReturnsZeroes_WhenLibraryIsEmpty()
    {
        // Act
        var stats = await _subject.GetStatsAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats, Has.Property(nameof(stats.Total)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Missing)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Upgradable)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Fulfilled)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.FulfilledExternally)).EqualTo(0));
            Assert.That(stats, Has.Property(nameof(stats.Partial)).EqualTo(0));
        }
    }

    [Test]
    public async Task GetStatsAsync_AggregatesAcrossEntireLibrary_WhenLibraryHasItems()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container1);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container1.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-a",
            Name = "item-a",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container2.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "item-b",
            Name = "item-b",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        foreach (var (slug, fulfillment) in new[]
        {
            ("item-c", FulfillmentStatus.Missing),
            ("item-d", FulfillmentStatus.Upgradable),
            ("item-e", FulfillmentStatus.Upgradable),
            ("item-f", FulfillmentStatus.Upgradable),
            ("item-g", FulfillmentStatus.Fulfilled),
            ("item-h", FulfillmentStatus.Fulfilled),
            ("item-i", FulfillmentStatus.Fulfilled),
            ("item-j", FulfillmentStatus.Fulfilled),
            ("item-k", FulfillmentStatus.FulfilledExternally),
            ("item-l", FulfillmentStatus.Partial),
            ("item-m", FulfillmentStatus.Partial)
        })
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            _context.MetadataContainers.Add(container);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            var hierarchy = new ContentHierarchyItem
            {
                MetadataContainerId = container.Id,
                ContentType = "type-a",
                Level = "level-a",
                Slug = slug,
                Name = slug,
                Monitoring = MonitoringMode.Monitored,
                CreatedAt = DateTime.UtcNow
            };
            _context.ContentHierarchyItems.Add(hierarchy);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _context.ContentItems.Add(new ContentItem
            {
                ContentHierarchyItemId = hierarchy.Id,
                Fulfillment = fulfillment,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Act
        var stats = await _subject.GetStatsAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats, Has.Property(nameof(stats.Total)).EqualTo(13));
            Assert.That(stats, Has.Property(nameof(stats.Missing)).EqualTo(3));
            Assert.That(stats, Has.Property(nameof(stats.Upgradable)).EqualTo(3));
            Assert.That(stats, Has.Property(nameof(stats.Fulfilled)).EqualTo(4));
            Assert.That(stats, Has.Property(nameof(stats.FulfilledExternally)).EqualTo(1));
            Assert.That(stats, Has.Property(nameof(stats.Partial)).EqualTo(2));
        }
    }

    [Test]
    public async Task GetLineageAsync_ReturnsLeafToRoot_WhenLineageExists()
    {
        // Arrange
        var rootContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var parentContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = rootContainer.Id,
            ContentType = "test",
            Level = "level",
            Slug = "root",
            Name = "root",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var parent = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = parentContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "parent",
            Name = "parent",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = leafContainer.Id,
            ParentId = parent.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(rootContainer, parentContainer, leafContainer);
        _context.ContentHierarchyItems.AddRange(root, parent, leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id, parent.Id, root.Id]));
    }

    [Test]
    public async Task GetAncestryAsync_ReturnsEveryDistinctAncestorOnce_WhenTwoLeavesShareAChain()
    {
        // Arrange
        var rootContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var firstLeafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var secondLeafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = rootContainer.Id,
            ContentType = "test",
            Level = "level",
            Slug = "root",
            Name = "root",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var firstLeaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = firstLeafContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "first",
            Name = "first",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        var secondLeaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = secondLeafContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "second",
            Name = "second",
            Monitoring = null,
            EffectiveMonitoring = MonitoringMode.Unmonitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(rootContainer, firstLeafContainer, secondLeafContainer);
        _context.ContentHierarchyItems.AddRange(root, firstLeaf, secondLeaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var ancestry = await _subject.GetAncestryAsync([firstLeaf.Id, secondLeaf.Id]).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ancestry, Has.Count.EqualTo(3));
            Assert.That(ancestry[firstLeaf.Id].ParentId, Is.EqualTo(root.Id));
            Assert.That(ancestry[firstLeaf.Id].EffectiveMonitoring, Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(ancestry[secondLeaf.Id].EffectiveMonitoring, Is.EqualTo(MonitoringMode.Unmonitored));
            Assert.That(ancestry[root.Id].ParentId, Is.Null);
        }
    }

    [Test]
    public async Task GetLineageAsync_StopsBeforeRepeat_WhenCycleExists()
    {
        // Arrange
        var rootContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leafContainer = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var root = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = rootContainer.Id,
            ContentType = "test",
            Level = "level",
            Slug = "root",
            Name = "root",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = leafContainer.Id,
            ParentId = root.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.AddRange(rootContainer, leafContainer);
        _context.ContentHierarchyItems.AddRange(root, leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        root.ParentId = leaf.Id;
        _context.ContentHierarchyItems.Update(root);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id, root.Id]));
    }

    [Test]
    public async Task GetLineageAsync_StopsBeforeMissingAncestor_WhenAncestorMissing()
    {
        // Arrange
        var container = new MetadataContainer { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var leaf = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = container.Id,
            ContentType = "test",
            Level = "level",
            Slug = "leaf",
            Name = "leaf",
            Monitoring = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContainers.Add(container);
        _context.ContentHierarchyItems.Add(leaf);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var missingParentId = Guid.NewGuid();
        await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF").ConfigureAwait(false);
        await _context.Database
            .ExecuteSqlInterpolatedAsync(
                $"UPDATE content_hierarchyItem SET ParentId = {missingParentId} WHERE Id = {leaf.Id}")
            .ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON").ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var nodes = await _subject.GetLineageAsync(leaf.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nodes.Select(n => n.Id), Is.EqualTo([leaf.Id]));
            Assert.That(nodes[0].ParentId, Is.EqualTo(missingParentId));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEpisodeRowsOnly_WhenLevelInCriteriaAccompaniesAncestorAndLeafOnly()
    {
        // Arrange
        var showContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var childlessSeasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var seasonContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var firstEpisodeContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var secondEpisodeContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(
            showContainer,
            childlessSeasonContainer,
            seasonContainer,
            firstEpisodeContainer,
            secondEpisodeContainer);
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = showContainer,
            ContentType = "tv",
            Level = "show",
            Slug = "provider-a-1438",
            Name = "The Wire",
            CreatedAt = DateTime.UtcNow
        };
        var childlessSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = childlessSeasonContainer,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = seasonContainer,
            ParentId = show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow
        };
        var firstEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = firstEpisodeContainer,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s02e01",
            Name = "Ebb Tide",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        var secondEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = secondEpisodeContainer,
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "s02e02",
            Name = "Collateral Damage",
            Ordinal = 2,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(show, childlessSeason, season, firstEpisode, secondEpisode);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                AncestorId = show.Id,
                LeafOnly = true,
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And =
                    [
                        new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.In,
                            Values = ["episode"]
                        }
                    ]
                }
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(item => item.Id), Is.EquivalentTo([firstEpisode.Id, secondEpisode.Id]));
            Assert.That(result.Items.Select(item => item.Slug), Is.EquivalentTo(["s02e01", "s02e02"]));
            Assert.That(result.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetByIdAsync_IncludesDetailMetadataCollections_WhenReadingOneItem()
    {
        // Arrange
        var contribution = new MetadataContribution
        {
            MetadataContainerId = _metadataContainerId,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Detailed Movie",
            CreatedAt = DateTime.UtcNow
        };
        _context.MetadataContributions.Add(contribution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Genres.Add(new Genre
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Name = "Drama"
        });
        _context.ExternalIds.Add(new ExternalId
        {
            MetadataContributionId = contribution.Id,
            Ordinal = 0,
            Source = "tmdb",
            Value = "42"
        });

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = _metadataContainerId,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "detailed-movie",
            Name = "Detailed Movie",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentItem = new ContentItem
        {
            ContentHierarchyItemId = item.Id,
            Fulfillment = FulfillmentStatus.Fulfilled,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.Add(contentItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.MediaFiles.Add(new MediaFile
        {
            ContentItemId = contentItem.Id,
            FileKind = "Video",
            FilePath = "/movies/detailed-movie/detailed.mkv",
            PathKey = "/movies/detailed-movie/detailed.mkv",
            FileSize = 1024,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(item.Id).ConfigureAwait(false);

        // Assert
        var detailedContribution = result!.MetadataContainer!.Contributions.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detailedContribution.Genres, Has.Exactly(1).Items);
            Assert.That(detailedContribution.ExternalIds, Has.Exactly(1).Items);
            Assert.That(result.ContentItem!.MediaFiles, Has.Exactly(1).Items);
        }
    }

    [Test]
    public async Task QueryAsync_KeepsTheSourcelessExternalId_WhenAnEarlierContributionSuppliesTheSameValue()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var sourced = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            ProviderType = "test",
            Title = "Movie"
        };
        var sourceless = new MetadataContribution
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            ProviderType = "other",
            Title = "Movie"
        };
        _context.MetadataContributions.AddRange(sourced, sourceless);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ExternalIds.AddRange(
            new ExternalId { MetadataContributionId = sourced.Id, Ordinal = 0, Source = "imdb", Value = "shared" },
            new ExternalId { MetadataContributionId = sourceless.Id, Ordinal = 0, Source = null, Value = "shared" });
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "level-a",
            Slug = "shared-ids",
            Name = "Shared",
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
                        ExternalIds = new()
                        {
                            Some =
                            [
                                [
                                    new ExternalIdCriteria
                                    {
                                        Source = new()
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
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(item => item.Slug), Is.EqualTo(["shared-ids"]));
    }
}
