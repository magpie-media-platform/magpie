using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryIdentityTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;

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
        _subject = new ContentHierarchyItemRepository(
            _context, Mock.Of<IAuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IMetadataContainerRepository>(), Mock.Of<IContentTypeRegistry>(), Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                ContentType = "movie",
                Level = "root",
                Slug = "old-movie",
                Name = "Old movie",
                CreatedAt = new DateTime(2026, 1, 1),
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                            ProviderType = "first", Ordinal = 0,
                            ExternalIds =
                            {
                                new ExternalId { Source = "SOURCE", Value = "SHARED" },
                                new ExternalId { Source = "source", Value = "alternate" },
                                new ExternalId { Source = "second", Value = "other" }
                            }
                        },
                        new MetadataContribution
                        {
                            ProviderType = "second", Ordinal = 1,
                            ExternalIds =
                            {
                                new ExternalId { Source = "source", Value = "obsolete" },
                                new ExternalId { Source = "third", Value = "independent" }
                            }
                        },
                        new MetadataContribution
                        {
                            Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                            ProviderType = "third", Ordinal = 0,
                            ExternalIds = { new ExternalId { Source = "source", Value = "tied-obsolete" } }
                        }
                    }
                }
            },
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                ContentType = "movie",
                Level = "root",
                Slug = "new-movie",
                Name = "New movie",
                CreatedAt = new DateTime(2026, 2, 1),
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "first",
                            ExternalIds = { new ExternalId { Source = "source", Value = "shared" } }
                        }
                    }
                }
            },
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                ContentType = "tv",
                Level = "root",
                Slug = "show",
                Name = "Show",
                CreatedAt = new DateTime(2026, 1, 1),
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "first",
                            ExternalIds = { new ExternalId { Source = "source", Value = "shared" } }
                        }
                    }
                }
            },
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                ContentType = "tv",
                Level = "special",
                Slug = "special",
                Name = "Special",
                CreatedAt = new DateTime(2026, 1, 1),
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "first",
                            ExternalIds = { new ExternalId { Source = "source", Value = "shared" } }
                        }
                    }
                }
            },
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
                ParentId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                ContentType = "tv",
                Level = "child",
                Slug = "child",
                Name = "Child",
                CreatedAt = new DateTime(2026, 1, 1),
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "first",
                            ExternalIds = { new ExternalId { Source = "source", Value = "shared" } }
                        }
                    }
                }
            });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase("movie", "root", "source", "shared", "00000000-0000-0000-0000-000000000001")]
    [TestCase("MOVIE", "ROOT", "SOURCE", "SHARED", "00000000-0000-0000-0000-000000000001")]
    [TestCase("tv", "root", "source", "shared", "00000000-0000-0000-0000-000000000003")]
    [TestCase("tv", "special", "source", "shared", "00000000-0000-0000-0000-000000000004")]
    [TestCase("movie", "root", "source", "alternate", "00000000-0000-0000-0000-000000000001")]
    [TestCase("movie", "root", "second", "other", "00000000-0000-0000-0000-000000000001")]
    [TestCase("movie", "root", "third", "independent", "00000000-0000-0000-0000-000000000001")]
    public async Task FindByExternalIdentitiesAsync_ReturnsTheHydrationMatch_WhenAnEffectiveIdentityExists(
        string contentType, string level, string source, string value, string expected)
    {
        // Arrange
        var criteria = new ContentHierarchyItemCriteria
        {
            ContentType = new() { And = [new() { Value = contentType }] },
            Level = new() { And = [new() { Value = level }] },
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
                                Source = new() { And = [new() { Value = source }] },
                                Value = new() { And = [new() { Value = value }] }
                            }]]
                        }
                    }
                ]
            }
        };

        // Act
        var matches = await _subject.FindByExternalIdentitiesAsync([(contentType, level, source, value)])
            .ConfigureAwait(false);
        var hydration = await _subject.QueryAsync(criteria, new() { PageNumber = 1, PageSize = 1 },
            new() { PropertyName = nameof(ContentHierarchyItem.CreatedAt), Direction = SortDirection.Ascending })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(matches.Values.Single().ToString(), Is.EqualTo(expected));
            Assert.That(hydration.Items.Single().Id.ToString(), Is.EqualTo(expected));
            Assert.That(_context.ChangeTracker.Entries(), Is.Empty);
        }
    }

    [TestCase("movie", "root", "source", "obsolete")]
    [TestCase("movie", "root", "source", "tied-obsolete")]
    [TestCase("movie", "root", "source", "other")]
    [TestCase("tv", "child", "source", "shared")]
    [TestCase("movie", "special", "source", "shared")]
    [TestCase("movie", "root", "", "shared")]
    [TestCase("movie", "root", "source", "")]
    [TestCase("movie", "root", "source", "missing")]
    public async Task FindByExternalIdentitiesAsync_ReturnsNoMatch_WhenTheIdentityIsNotAnEffectiveRootIdentity(
        string contentType, string level, string source, string value)
    {
        // Arrange
        var identities = new[] { (contentType, level, source, value) };

        // Act
        var matches = await _subject.FindByExternalIdentitiesAsync(identities).ConfigureAwait(false);

        // Assert
        Assert.That(matches, Is.Empty);
    }

    [Test]
    public void FindByExternalIdentitiesAsync_ThrowsCancellation_WhenTheLookupIsCanceled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = Assert.ThrowsAsync<OperationCanceledException>(() =>
            _subject.FindByExternalIdentitiesAsync([("movie", "root", "source", "shared")], cancellation.Token));

        // Assert
        Assert.That(exception, Is.Not.Null);
    }
}
