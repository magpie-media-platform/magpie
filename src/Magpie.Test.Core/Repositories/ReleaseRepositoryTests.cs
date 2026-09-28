using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ReleaseRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IReleaseRepository _subject = null!;
    private Guid _userId;
    private Guid _indexerId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
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
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options);

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        var indexer = new Indexer
        {
            Name = "TestIndexer",
            ProviderType = "torznab",
            Availability = IndexerAvailability.Public,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.Indexers.Add(indexer);
        _context.SaveChanges();

        _userId = user.Id;
        _indexerId = indexer.Id;

        var loggerFactory = NullLoggerFactory.Instance;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));

        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _subject = new ReleaseRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ReleaseRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestRelease",
            PublishDate = DateTime.UtcNow,
            Size = 1024,
            Categories = ["type-a"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(release.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("TestRelease"));
            Assert.That(result, Has.Property(nameof(result.Protocol)).EqualTo("torrent"));
            Assert.That(result, Has.Property(nameof(result.Size)).EqualTo(1024));
        }
    }

    [Test]
    public async Task GetByIdAsync_RoundTripsExternalIds_WhenSet()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestRelease",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        release.ExternalIds.Add(new ExternalId { Ordinal = 0, Source = "provider-c", Value = "tt0903747" });
        release.ExternalIds.Add(new ExternalId { Ordinal = 1, Source = "provider-a", Value = "155" });
        release.ExternalIds.Add(new ExternalId { Ordinal = 2, Source = "provider-d", Value = "81189" });
        release.ExternalIds.Add(new ExternalId { Ordinal = 3, Source = "provider-e", Value = "82" });
        release.ExternalIds.Add(new ExternalId { Ordinal = 4, Source = "provider-f", Value = "1390" });
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(release.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(Release.ExternalIds)).With.Exactly(5).Items);
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.ExternalIds,
                Has.Some.Matches<ExternalId>(id => id.Source == "provider-c" && id.Value == "tt0903747"));
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.ExternalIds,
                Has.Some.Matches<ExternalId>(id => id.Source == "provider-a" && id.Value == "155"));
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.ExternalIds,
                Has.Some.Matches<ExternalId>(id => id.Source == "provider-d" && id.Value == "81189"));
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.ExternalIds,
                Has.Some.Matches<ExternalId>(id => id.Source == "provider-e" && id.Value == "82"));
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.ExternalIds,
                Has.Some.Matches<ExternalId>(id => id.Source == "provider-f" && id.Value == "1390"));
        }
    }

    [Test]
    public async Task GetByIdAsync_LeavesExternalIdsEmpty_WhenUnset()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestRelease",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(release.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(Release.ExternalIds)).Empty);
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
    public async Task ListAllAsync_ReturnsAllReleases_WhenReleasesExist()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "usenet",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoReleases()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenIndexerFilterIsSet()
    {
        // Arrange
        var otherIndexer = new Indexer
        {
            Name = "OtherIndexer",
            ProviderType = "newznab",
            Availability = IndexerAvailability.Private,
            CreatedAt = DateTime.UtcNow
        };
        _context.Indexers.Add(otherIndexer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = otherIndexer.Id,
            Protocol = "usenet",
            Title = "Release3",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { IndexerId = _indexerId },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenTitleFilterIsSet()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestMovie.2024.1080p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "TestMovie.2024.720p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OtherShow.S01E01",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Title = "TestMovie" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .With.All.Property(nameof(Release.Title)).Contains("TestMovie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenCategoryFilterIsSet()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            Categories = ["type-a", "hd"],
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            Categories = ["type-c", "hd"],
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Category = "type-a" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.Title)).EqualTo("Release1"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenExternalIdFilterIsSet()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            ExternalId = "ext-123",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            ExternalId = "ext-456",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ExternalId = "ext-123" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.ExternalId)).EqualTo("ext-123"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenPublishDateRangeIsSet()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = now.AddDays(-5),
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = now.AddDays(-2),
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release3",
            PublishDate = now.AddDays(-10),
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                PublishDate = new(now.AddDays(-6), now.AddDays(-1))
            },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenContentItemFilterIsSet()
    {
        // Arrange
        var metadataContainer1 = new MetadataContainer();
        var metadataContainer2 = new MetadataContainer();
        _context.MetadataContainers.AddRange(metadataContainer1, metadataContainer2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var hierarchyItem1 = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer1.Id,
            ContentType = "type-a",
            Level = "Type A",
            Slug = "test-type-a-1",
            Name = "Type A Item 1",
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItem2 = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer2.Id,
            ContentType = "type-a",
            Level = "Type A",
            Slug = "test-type-a-2",
            Name = "Type A Item 2",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.AddRange(hierarchyItem1, hierarchyItem2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var contentItem1 = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem1.Id,
            CreatedAt = DateTime.UtcNow
        };
        var contentItem2 = new ContentItem
        {
            ContentHierarchyItemId = hierarchyItem2.Id,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentItems.AddRange(contentItem1, contentItem2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ReleaseScopes.AddRange(
            new ReleaseScope
            {
                ReleaseId = release1.Id,
                ContentHierarchyItemId = hierarchyItem1.Id,
            },
            new ReleaseScope
            {
                ReleaseId = release2.Id,
                ContentHierarchyItemId = hierarchyItem2.Id,
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { ContentHierarchyItemId = hierarchyItem1.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.Id)).EqualTo(release1.Id));
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task QueryAsync_ReturnsTheUnmatchedRelease_OnlyWhenIncludeUnmatchedIsSet(
        bool includeUnmatched,
        int expectedCount)
    {
        // Arrange
        var metadataContainer = new MetadataContainer();
        _context.MetadataContainers.Add(metadataContainer);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer.Id,
            ContentType = "type-a",
            Level = "Type A",
            Slug = "unmatched-item",
            Name = "Unmatched Item",
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Unmatched.Release",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ReleaseScopes.Add(new ReleaseScope
        {
            ReleaseId = release.Id,
            ContentHierarchyItemId = hierarchyItem.Id,
            Status = ReleaseScopeStatus.Unmatched,
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
                new() { ContentHierarchyItemId = hierarchyItem.Id, IncludeUnmatched = includeUnmatched },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Count(), Is.EqualTo(expectedCount));
    }

    [Test]
    public async Task UpdateAsync_KeepsTheUnmatchedScope_WhenAnIdentityMergeFoldsAnUnmatchedAlias()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var metadataContainer = new MetadataContainer();
        _context.MetadataContainers.Add(metadataContainer);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer.Id,
            ContentType = "type-a",
            Level = "Type A",
            Slug = "merged-item",
            Name = "Merged Item",
            CreatedAt = now
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        var canonical = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "canonical",
            Title = "Merged.Release",
            PublishDate = now,
            CreatedAt = now,
            ObservedAt = now
        };
        canonical.IdentityKeys.Add(
            new ReleaseIdentityKey { Authority = "btih", Value = "canonical-hash", Ordinal = 0 });
        var alias = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "alias",
            Title = "Merged.Release",
            PublishDate = now,
            CreatedAt = now,
            ObservedAt = now
        };
        alias.IdentityKeys.Add(new ReleaseIdentityKey { Authority = "btih", Value = "alias-hash", Ordinal = 0 });
        _context.Releases.AddRange(canonical, alias);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ReleaseScopes.AddRange(
            new ReleaseScope { ReleaseId = canonical.Id, ContentHierarchyItemId = hierarchyItem.Id },
            new ReleaseScope
            {
                ReleaseId = alias.Id,
                ContentHierarchyItemId = hierarchyItem.Id,
                Status = ReleaseScopeStatus.Unmatched
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        canonical.IdentityKeys.Clear();
        canonical.IdentityKeys.Add(
            new ReleaseIdentityKey { Authority = "btih", Value = "canonical-hash", Ordinal = 0 });
        canonical.IdentityKeys.Add(new ReleaseIdentityKey { Authority = "btih", Value = "alias-hash", Ordinal = 1 });

        // Act
        await _subject.UpdateAsync(canonical, _userId, default).ConfigureAwait(false);

        // Assert
        var scopes = await _context.ReleaseScopes.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scopes, Has.Count.EqualTo(1));
            Assert.That(scopes[0].ReleaseId, Is.EqualTo(canonical.Id));
            Assert.That(scopes[0].Status, Is.EqualTo(ReleaseScopeStatus.Unmatched));
        }
    }

    [Test]
    public async Task DeleteWhereAsync_KeepsTheUnmatchedRelease_WhenRetentionDeletesUnobservedReleases()
    {
        // Arrange
        var observedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var metadataContainer = new MetadataContainer();
        _context.MetadataContainers.Add(metadataContainer);
        var hierarchyItem = new ContentHierarchyItem
        {
            MetadataContainerId = metadataContainer.Id,
            ContentType = "type-a",
            Level = "Type A",
            Slug = "retained-item",
            Name = "Retained Item",
            CreatedAt = observedAt
        };
        _context.ContentHierarchyItems.Add(hierarchyItem);
        var unmatched = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "unmatched",
            Title = "Unmatched.Release",
            PublishDate = observedAt,
            CreatedAt = observedAt,
            ObservedAt = observedAt
        };
        var matched = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "matched",
            Title = "Matched.Release",
            PublishDate = observedAt,
            CreatedAt = observedAt,
            ObservedAt = observedAt
        };
        _context.Releases.AddRange(unmatched, matched);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ReleaseScopes.AddRange(
            new ReleaseScope
            {
                ReleaseId = unmatched.Id,
                ContentHierarchyItemId = hierarchyItem.Id,
                Status = ReleaseScopeStatus.Unmatched
            },
            new ReleaseScope { ReleaseId = matched.Id, ContentHierarchyItemId = hierarchyItem.Id });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var deleted = await _subject.DeleteWhereAsync(
                new ReleaseQuery
                {
                    ObservedAt = new Range<DateTime>(null, new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc))
                })
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.Releases.AsNoTracking().Select(release => release.Id).ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(remaining, Is.EqualTo([unmatched.Id]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingReleases_WhenTagFilterIsSet()
    {
        // Arrange
        var tag = new Tag { Name = "high-quality" };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release2",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        tag.Releases.Add(release1);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Tags = ["high-quality"] },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items, Has.One.With.Property(nameof(Release.Title)).EqualTo("Release1"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Release1",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new() { Title = "NonExistent" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Empty);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(0));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsRequestedPage_WhenPagingIsSet()
    {
        // Arrange
        for (var i = 0; i < 10; i++)
        {
            _context.Releases.Add(new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = $"Release{i}",
                PublishDate = DateTime.UtcNow.AddMinutes(-i),
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(), new() { PageNumber = 2, PageSize = 3 }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(2));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsBoundedPageAndTotalCount_WhenPagingProvided()
    {
        // Arrange
        var now = DateTime.UtcNow;
        for (var i = 0; i < 7; i++)
        {
            _context.Releases.Add(new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = $"Release{i}",
                PublishDate = now.AddMinutes(-i),
                CreatedAt = now
            });
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var paging = new PagingParameter { PageNumber = 1, PageSize = 3 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).With.Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(7));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(1));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAscendingReleases_WhenAscendingSortIsSet()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Zebra",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Alpha",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release3 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Beta",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2, release3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new(),
            new() { PageNumber = 1, PageSize = 100 },
            new()
            {
                PropertyName = nameof(Release.Title),
                Direction = SortDirection.Ascending
            }).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(Release.Title)).EqualTo("Alpha"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(Release.Title)).EqualTo("Beta"));
            Assert.That(items, Has.ItemAt(2).Property(nameof(Release.Title)).EqualTo("Zebra"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsDescendingReleases_WhenDescendingSortIsSet()
    {
        // Arrange
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Alpha",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Zebra",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new(),
            new() { PageNumber = 1, PageSize = 100 },
            new()
            {
                PropertyName = nameof(Release.Title),
                Direction = SortDirection.Descending
            }).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(Release.Title)).EqualTo("Zebra"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(Release.Title)).EqualTo("Alpha"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsByPublishDateDescending_WhenSortIsNotSet()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release1 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OldRelease",
            PublishDate = now.AddDays(-5),
            CreatedAt = DateTime.UtcNow
        };
        var release2 = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NewRelease",
            PublishDate = now,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(release1, release2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.ItemAt(0).Property(nameof(Release.Title)).EqualTo("NewRelease"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(Release.Title)).EqualTo("OldRelease"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesRelease_WhenRequestIsValid()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NewRelease",
            PublishDate = DateTime.UtcNow,
            Size = 2048,
            Categories = ["type-a"],
            Languages = ["en"]
        };

        // Act
        var result = await _subject.CreateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Id)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("NewRelease"));
            Assert.That(result, Has.Property(nameof(result.Protocol)).EqualTo("torrent"));
            Assert.That(result, Has.Property(nameof(result.Size)).EqualTo(2048));
            Assert.That(result, Has.Property(nameof(result.CreatedByUserId)).EqualTo(_userId));
            Assert.That(result, Has.Property(nameof(result.CreatedAt)).Not.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public async Task CreateAsync_PersistsAllFields_WhenAllFieldsAreProvided()
    {
        // Arrange
        var publishDate = DateTime.UtcNow;
        var infoUrl = new Uri("https://example.com/release/123");

        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "usenet",
            ExternalId = "ext-release-123",
            Title = "CompleteRelease",
            PublishDate = publishDate,
            Size = 4096,
            InfoUrl = infoUrl,
            Categories = ["type-a", "hd"],
            Languages = ["en", "fr"],
            Properties = "{\"quality\":\"1080p\"}"
        };

        // Act
        var result = await _subject.CreateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == result.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Property(nameof(persisted.ExternalId)).EqualTo("ext-release-123"));
            Assert.That(persisted, Has.Property(nameof(persisted.InfoUrl)).EqualTo(infoUrl));
            Assert.That(persisted, Has.Property(nameof(persisted.Properties)).EqualTo("{\"quality\":\"1080p\"}"));
            Assert.That(persisted, Has.Property(nameof(persisted.Categories)).EquivalentTo(new[] { "type-a", "hd" }));
            Assert.That(persisted, Has.Property(nameof(persisted.Languages)).EquivalentTo(new[] { "en", "fr" }));
        }
    }

    [Test]
    public async Task CreateAsync_PreservesOpaqueIdentityKey_WhenProvided()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "indexer-release-1",
            Title = "Release",
            PublishDate = DateTime.UtcNow
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey { Authority = "provider", Value = "opaque", Ordinal = 0 });

        // Act
        var result = await _subject.CreateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        var keys = await _context.ReleaseIdentityKeys
            .AsNoTracking()
            .Where(k => k.ReleaseId == result.Id)
            .OrderBy(k => k.Ordinal)
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Count.EqualTo(1));
            Assert.That(keys[0].Authority, Is.EqualTo("provider"));
            Assert.That(keys[0].Value, Is.EqualTo("opaque"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingRelease_WhenIndexerAndExternalIdMatch()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "release",
            Title = "Release",
            PublishDate = now.AddDays(-1),
            CreatedAt = now.AddDays(-1),
            ObservedAt = now.AddDays(-1)
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey
        {
            Authority = "provider",
            Value = "stable-release",
            Ordinal = 0
        });
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.QueryAsync(
                new ReleaseQuery { IndexerId = _indexerId, ExternalId = "release" },
                new() { PageNumber = 1, PageSize = 100 },
                null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Items.Single(),
            Has.Property(nameof(Release.Id)).EqualTo(release.Id));
    }

    [Test]
    public async Task MarkObservedAsync_UpdatesMatchingReleases_WhenReleaseIdsAreProvided()
    {
        // Arrange
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var observed = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "observed",
            Title = "Observed",
            PublishDate = now.AddDays(-2),
            CreatedAt = now.AddDays(-2),
            ObservedAt = now.AddDays(-2)
        };
        var untouched = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "untouched",
            Title = "Untouched",
            PublishDate = now.AddDays(-2),
            CreatedAt = now.AddDays(-2),
            ObservedAt = now.AddDays(-2)
        };
        _context.Releases.AddRange(observed, untouched);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var updated = await _subject.MarkObservedAsync([observed.Id]).ConfigureAwait(false);

        // Assert
        var rows = await _context.Releases.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.EqualTo(1));
            Assert.That(
                rows.Single(r => r.Id == observed.Id).ObservedAt,
                Is.EqualTo(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                rows.Single(r => r.Id == untouched.Id).ObservedAt,
                Is.EqualTo(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task UpdateAsync_MergesIdentityKeys_WhenNewKeysAreProvided()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "release-1",
            Title = "Release",
            PublishDate = now,
            CreatedAt = now,
            ObservedAt = now
        };
        release.IdentityKeys.Add(new ReleaseIdentityKey { Authority = "provider-a", Value = "stable-a", Ordinal = 0 });
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        release.IdentityKeys.Clear();
        release.IdentityKeys.Add(new ReleaseIdentityKey { Authority = "provider-b", Value = "stable-b", Ordinal = 0 });

        // Act
        await _subject.UpdateAsync(release, _userId, default).ConfigureAwait(false);

        // Assert
        var keys = await _context.ReleaseIdentityKeys
            .AsNoTracking()
            .Where(k => k.ReleaseId == release.Id)
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                keys,
                Has.Some.Matches<ReleaseIdentityKey>(k => k.Authority == "provider-a" && k.Value == "stable-a"));
            Assert.That(
                keys,
                Has.Some.Matches<ReleaseIdentityKey>(k => k.Authority == "provider-b" && k.Value == "stable-b"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesAuditRecord_WhenReleaseIsCreated()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "AuditedRelease",
            PublishDate = DateTime.UtcNow
        };

        // Act
        await _subject.CreateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Create"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "OriginalTitle",
            PublishDate = DateTime.UtcNow,
            Size = 1024,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        release.Title = "UpdatedTitle";
        release.Protocol = "usenet";
        release.Size = 2048;

        // Act
        var result = await _subject.UpdateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        var updated = await _subject.GetByIdAsync(release.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(updated, Has.Property(nameof(updated.Title)).EqualTo("UpdatedTitle"));
            Assert.That(updated, Has.Property(nameof(updated.Protocol)).EqualTo("usenet"));
            Assert.That(updated, Has.Property(nameof(updated.Size)).EqualTo(2048));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(updated, Has.Property(nameof(updated.ModifiedAt)).Not.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var release = new Release
        {
            Id = Guid.NewGuid(),
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "NonExistent",
            PublishDate = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UpdateAsync_CreatesAuditRecord_WhenReleaseExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "ToUpdate",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        release.Title = "Updated";

        // Act
        await _subject.UpdateAsync(release, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Update"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesRelease_WhenActiveDownloadReferencesIt()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Protected",
            PublishDate = now.AddDays(-30),
            CreatedAt = now.AddDays(-30),
            ObservedAt = now.AddDays(-30)
        };
        var downloadClient = new DownloadClient
        {
            ProviderType = "test",
            Name = "Client",
            Enabled = true,
            CreatedAt = now
        };
        _context.Releases.Add(release);
        _context.DownloadClients.Add(downloadClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Downloads.Add(new Download
        {
            ReleaseId = release.Id,
            DownloadClientId = downloadClient.Id,
            ExternalId = "download-1",
            Title = "Protected",
            Status = DownloadStatus.Downloading,
            CreatedAt = now
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(release.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _context.Releases.AnyAsync(r => r.Id == release.Id).ConfigureAwait(false), Is.False);
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesRelease_WhenQueuedAcquisitionLockPartitionReferencesIt()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "JobProtected",
            PublishDate = now.AddDays(-30),
            CreatedAt = now.AddDays(-30),
            ObservedAt = now.AddDays(-30)
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.JobExecutions.Add(new JobExecution
        {
            JobType = "ReleaseDownloadItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:ReleaseDownloadItem" +
                $"|releaseId={release.Id.ToString().ToLowerInvariant()}",
            CreatedAt = now
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(release.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _context.Releases.AnyAsync(r => r.Id == release.Id).ConfigureAwait(false), Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesRelease_WhenExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "ToDelete",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(release.Id, _userId).ConfigureAwait(false);

        // Assert
        var deleted = await _subject.GetByIdAsync(release.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(deleted, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_ClearsSourceReference_WhenDownloadReferencesReleaseSource()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "WithDownload",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        var downloadClient = new DownloadClient
        {
            ProviderType = "qbittorrent",
            Name = "TestClient",
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(downloadClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var source = new ReleaseSource
        {
            ReleaseId = release.Id,
            Key = "source",
            IndexerId = _indexerId,
            Protocol = "torrent",
            ExternalId = "ext-source",
            Title = "WithDownload",
            CreatedAt = DateTime.UtcNow,
            ObservedAt = DateTime.UtcNow
        };
        _context.ReleaseSources.Add(source);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var download = new Download
        {
            ReleaseId = release.Id,
            ReleaseSourceId = source.Id,
            DownloadClientId = downloadClient.Id,
            ExternalId = "ext-download",
            Title = "WithDownload",
            Status = DownloadStatus.Downloading,
            CreatedAt = DateTime.UtcNow
        };
        _context.Downloads.Add(download);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(release.Id, _userId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.Downloads.AsNoTracking()
            .SingleAsync(d => d.Id == download.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(persisted.ReleaseId, Is.Null);
            Assert.That(persisted.ReleaseSourceId, Is.Null);
        }
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
    public async Task DeleteAsync_CreatesAuditRecord_WhenReleaseExists()
    {
        // Arrange
        var release = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "DeleteAudit",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.Add(release);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(release.Id, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(auditRecords, Has.Count.EqualTo(1));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.Operation)).EqualTo("Delete"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.EntityType)).EqualTo("Release"));
            Assert.That(auditRecords, Has.One.With.Property(nameof(AuditRecord.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task QueryAsync_SortsReleases_WhenCamelCasePropertyNameIsSet()
    {
        // Arrange
        var older = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Older",
            PublishDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        var newer = new Release
        {
            IndexerId = _indexerId,
            Protocol = "torrent",
            Title = "Newer",
            PublishDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.Releases.AddRange(older, newer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "publishDate", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var items = result.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items, Has.ItemAt(0).Property(nameof(Release.Title)).EqualTo("Newer"));
            Assert.That(items, Has.ItemAt(1).Property(nameof(Release.Title)).EqualTo("Older"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyAgedReleases_WhenTheAgeFilterIsSet()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agedTime = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-30));
        var agedRepository = new ReleaseRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            NullLoggerFactory.Instance.CreateLogger<ReleaseRepository>(),
            agedTime);
        var aged = await agedRepository.CreateAsync(
            new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = "Aged",
                PublishDate = now.AddDays(-30)
            },
            _userId,
            default).ConfigureAwait(false);
        var fresh = await _subject.CreateAsync(
            new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = "Fresh",
                PublishDate = now
            },
            _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(aged.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _context.Releases.AnyAsync(r => r.Id == aged.Id).ConfigureAwait(false), Is.False);
            Assert.That(await _context.Releases.AnyAsync(r => r.Id == fresh.Id).ConfigureAwait(false), Is.True);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAgedReleases_WhenOnlyTerminalDownloadsReferenceThem()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var agedTime = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-30));
        var agedRepository = new ReleaseRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLoggerFactory.Instance.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            NullLoggerFactory.Instance.CreateLogger<ReleaseRepository>(),
            agedTime);
        var failedBacked = await agedRepository.CreateAsync(
            new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = "FailedBacked",
                PublishDate = now.AddDays(-30)
            },
            _userId,
            default).ConfigureAwait(false);
        var cancelledBacked = await agedRepository.CreateAsync(
            new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = "CancelledBacked",
                PublishDate = now.AddDays(-30)
            },
            _userId,
            default).ConfigureAwait(false);
        var importedBacked = await agedRepository.CreateAsync(
            new Release
            {
                IndexerId = _indexerId,
                Protocol = "torrent",
                Title = "ImportedBacked",
                PublishDate = now.AddDays(-30)
            },
            _userId,
            default).ConfigureAwait(false);
        var downloadClient = new DownloadClient
        {
            ProviderType = "test",
            Name = "Client",
            Enabled = true,
            CreatedAt = now
        };
        _context.DownloadClients.Add(downloadClient);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Downloads.AddRange(
            new Download
            {
                ReleaseId = failedBacked.Id,
                DownloadClientId = downloadClient.Id,
                ExternalId = "terminal-failed",
                Title = "FailedBacked",
                Status = DownloadStatus.Failed,
                CreatedAt = now
            },
            new Download
            {
                ReleaseId = cancelledBacked.Id,
                DownloadClientId = downloadClient.Id,
                ExternalId = "terminal-cancelled",
                Title = "CancelledBacked",
                Status = DownloadStatus.Cancelled,
                CreatedAt = now
            },
            new Download
            {
                ReleaseId = importedBacked.Id,
                DownloadClientId = downloadClient.Id,
                ExternalId = "terminal-imported",
                Title = "ImportedBacked",
                Status = DownloadStatus.Imported,
                CreatedAt = now
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var deleted = 0;
        foreach (var releaseId in new[] { failedBacked.Id, cancelledBacked.Id, importedBacked.Id })
        {
            if (await _subject.DeleteAsync(releaseId, null).ConfigureAwait(false))
            {
                deleted++;
            }
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(3));
            Assert.That(await _context.Releases.AnyAsync(r => r.Id == failedBacked.Id).ConfigureAwait(false), Is.False);
            Assert.That(
                await _context.Releases.AnyAsync(r => r.Id == cancelledBacked.Id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await _context.Releases.AnyAsync(r => r.Id == importedBacked.Id).ConfigureAwait(false),
                Is.False);
        }
    }
}
