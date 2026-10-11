using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class WishlistDiscoveredRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IWishlistDiscoveredRepository _subject = null!;
    private Guid _wishlistId;

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
    public async Task SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

        _loggerFactory = NullLoggerFactory.Instance;
        _subject = new WishlistDiscoveredRepository(
            _context,
            TimeProvider.System,
            _loggerFactory.CreateLogger<WishlistDiscoveredRepository>());

        var contentFilter = new ContentFilter
        {
            Name = $"Filter-{Guid.NewGuid():N}",
            Criteria = new(),
            CreatedAt = DateTime.UtcNow,
        };
        _context.ContentFilters.Add(contentFilter);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var wishlist = new Wishlist
        {
            Name = $"Wishlist-{Guid.NewGuid():N}",
            ContentFilterId = contentFilter.Id,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        };
        _context.Wishlists.Add(wishlist);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        _wishlistId = wishlist.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task RecordDiscoveredAsync_IsIdempotent_WhenTheUniqueKeyAlreadyExists()
    {
        // Arrange

        // Act
        await _subject.RecordDiscoveredAsync(
            _wishlistId,
            [new WishlistDiscovered
            {
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            }])
            .ConfigureAwait(false);
        await _subject.RecordDiscoveredAsync(
            _wishlistId,
            [new WishlistDiscovered
            {
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club (again)"
            }])
            .ConfigureAwait(false);

        // Assert
        var rows = await _context.WishlistDiscovered.AsNoTracking()
            .Where(s => s.WishlistId == _wishlistId)
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Title, Is.EqualTo("Fight Club"));
        }
    }

    [Test]
    public async Task RecordDiscoveredAsync_DeduplicatesCandidates_WhenBatchContainsDuplicates()
    {
        // Arrange

        // Act
        await _subject.RecordDiscoveredAsync(
            _wishlistId,
            [
                    new WishlistDiscovered { ExternalIdSource = "provider-a.type-a", ExternalId = "1", Title = "One" },
                    new WishlistDiscovered
                    {
                        ExternalIdSource = "provider-a.type-a",
                        ExternalId = "1",
                        Title = "One duplicate"
                    },
                    new WishlistDiscovered { ExternalIdSource = "provider-a.type-a", ExternalId = "2", Title = "Two" },
            ]).ConfigureAwait(false);

        // Assert
        var count = await _context.WishlistDiscovered.AsNoTracking()
            .CountAsync(s => s.WishlistId == _wishlistId)
            .ConfigureAwait(false);
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task ListAsync_ReturnsRecordedCandidates_WhenCandidatesExist()
    {
        // Arrange
        await _subject.RecordDiscoveredAsync(
            _wishlistId,
            [
                    new WishlistDiscovered { ExternalIdSource = "provider-a.type-a", ExternalId = "1", Title = "One" },
                    new WishlistDiscovered { ExternalIdSource = "provider-a.type-a", ExternalId = "2", Title = "Two" },
            ]).ConfigureAwait(false);

        // Act
        var candidates = await _subject.ListAsync(_wishlistId).ConfigureAwait(false);

        // Assert
        Assert.That(
            candidates.Select(candidate => (candidate.ExternalIdSource, candidate.ExternalId)),
            Is.EquivalentTo([("provider-a.type-a", "1"), ("provider-a.type-a", "2")]));
    }

    [Test]
    public async Task ListEnrichedAsync_SortsByTitleWithNullsLast_WhenDirectionChanges()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        _context.WishlistDiscovered.AddRange(
            new WishlistDiscovered
            {
                Id = Guid.NewGuid(),
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "1",
                Title = "Banana",
                DiscoveredAt = baseTime,
            },
            new WishlistDiscovered
            {
                Id = Guid.NewGuid(),
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "2",
                Title = "Apple",
                DiscoveredAt = baseTime.AddMinutes(1),
            },
            new WishlistDiscovered
            {
                Id = Guid.NewGuid(),
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "3",
                Title = "Cherry",
                DiscoveredAt = baseTime.AddMinutes(2),
            },
            new WishlistDiscovered
            {
                Id = Guid.NewGuid(),
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "4",
                Title = null,
                DiscoveredAt = baseTime.AddMinutes(3),
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var ascendingQuery = new WishlistDiscoveredQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 50 },
            Sort = WishlistDiscoveredSort.Title,
            SortDescending = false,
        };
        var asc = await _subject
            .ListEnrichedAsync(_wishlistId, ascendingQuery)
            .ConfigureAwait(false);
        var descendingQuery = new WishlistDiscoveredQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 50 },
            Sort = WishlistDiscoveredSort.Title,
            SortDescending = true,
        };
        var desc = await _subject
            .ListEnrichedAsync(_wishlistId, descendingQuery)
            .ConfigureAwait(false);

        // Assert
        var ascIds = asc.Items.Select(r => r.ExternalId).ToList();
        var descIds = desc.Items.Select(r => r.ExternalId).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascIds, Is.EqualTo(["2", "1", "3", "4"]));
            Assert.That(descIds, Is.EqualTo(["3", "1", "2", "4"]));
        }
    }

    [Test]
    public async Task ListEnrichedAsync_SortsByDiscoveryTimeWithStableTies_WhenDirectionChanges()
    {
        // Arrange
        var shared = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tieLow = new Guid("e622fea6-36da-47e7-96e8-026bb65771a9");
        var tieHigh = new Guid("1f2d2977-cd70-4e74-8105-ff12e3bb5957");
        _context.WishlistDiscovered.AddRange(
            new WishlistDiscovered
            {
                Id = tieLow,
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "a",
                Title = "A",
                DiscoveredAt = shared,
            },
            new WishlistDiscovered
            {
                Id = tieHigh,
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "b",
                Title = "B",
                DiscoveredAt = shared,
            },
            new WishlistDiscovered
            {
                Id = Guid.NewGuid(),
                WishlistId = _wishlistId,
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "c",
                Title = "C",
                DiscoveredAt = shared.AddMinutes(5),
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var ascendingQuery = new WishlistDiscoveredQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 50 },
            Sort = WishlistDiscoveredSort.Discovered,
            SortDescending = false,
        };
        var asc = await _subject
            .ListEnrichedAsync(_wishlistId, ascendingQuery)
            .ConfigureAwait(false);
        var descendingQuery = new WishlistDiscoveredQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 50 },
            Sort = WishlistDiscoveredSort.Discovered,
            SortDescending = true,
        };
        var desc = await _subject
            .ListEnrichedAsync(_wishlistId, descendingQuery)
            .ConfigureAwait(false);

        // Assert
        var ascItems = asc.Items.ToList();
        var descItems = desc.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascItems.Select(r => r.ExternalId), Is.EqualTo(["b", "a", "c"]));
            Assert.That(ascItems[0].DiscoveredId, Is.EqualTo(tieHigh));
            Assert.That(ascItems[1].DiscoveredId, Is.EqualTo(tieLow));
            Assert.That(descItems.Select(r => r.ExternalId), Is.EqualTo(["c", "b", "a"]));
            Assert.That(descItems[1].DiscoveredId, Is.EqualTo(tieHigh));
            Assert.That(descItems[2].DiscoveredId, Is.EqualTo(tieLow));
        }
    }

    [Test]
    public async Task ExecuteDeleteAsync_DeletesDiscoveredRows_WhenWishlistIsDeleted()
    {
        // Arrange
        await _subject.RecordDiscoveredAsync(
            _wishlistId,
            [new WishlistDiscovered
            {
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            }])
            .ConfigureAwait(false);

        // Act
        await _context.Wishlists
            .Where(w => w.Id == _wishlistId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.WishlistDiscovered
            .CountAsync(s => s.WishlistId == _wishlistId)
            .ConfigureAwait(false);
        Assert.That(remaining, Is.EqualTo(0));
    }

    [Test]
    public async Task ListEnrichedAsync_PrefersPoster_WhenBackdropHasLowerOrdinal()
    {
        // Arrange
        var container = new MetadataContainer();
        var contribution = new MetadataContribution { ProviderType = "test-provider", MetadataContainer = container };
        contribution.ExternalIds.Add(new() { Source = "test-source", Value = "one" });
        contribution.Images.Add(new()
        {
            Type = "backdrop",
            Ordinal = 0,
            Url = new Uri("https://example.test/backdrop")
        });
        contribution.Images.Add(new() { Type = "poster", Ordinal = 5, Url = new Uri("https://example.test/poster") });
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new()
        {
            ContentType = "test-type",
            Level = "test-level",
            Slug = "test",
            Name = "Test",
            MetadataContainer = container
        });
        _context.WishlistDiscovered.Add(new()
        {
            WishlistId = _wishlistId,
            ExternalIdSource = "test-source",
            ExternalId = "one"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.ListEnrichedAsync(_wishlistId, new()
        {
            Paging = new() { PageNumber = 1, PageSize = 10 }
        }).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Single().PosterUrl, Is.EqualTo("https://example.test/poster"));
    }

    [Test]
    public async Task ListEnrichedAsync_ReturnsPrecisionOfEarliestContribution_WhenContributionsDiffer()
    {
        // Arrange
        var container = new MetadataContainer();
        var later = new MetadataContribution
        {
            ProviderType = "test-provider",
            MetadataContainer = container,
            Ordinal = 0,
            PublishDate = new DateTime(2005, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            PublishDatePrecision = TemporalPrecision.Day,
        };
        later.ExternalIds.Add(new() { Source = "test-source", Value = "one" });
        var earlier = new MetadataContribution
        {
            ProviderType = "other-provider",
            MetadataContainer = container,
            Ordinal = 1,
            PublishDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishDatePrecision = TemporalPrecision.Year,
        };
        _context.MetadataContributions.AddRange(later, earlier);
        _context.ContentHierarchyItems.Add(new()
        {
            ContentType = "test-type",
            Level = "test-level",
            Slug = "test",
            Name = "Test",
            MetadataContainer = container
        });
        _context.WishlistDiscovered.Add(new()
        {
            WishlistId = _wishlistId,
            ExternalIdSource = "test-source",
            ExternalId = "one"
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.ListEnrichedAsync(
            _wishlistId,
            new() { Paging = new() { PageNumber = 1, PageSize = 10 } }).ConfigureAwait(false);

        // Assert
        var row = page.Items.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.PublishDate, Is.EqualTo(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(row.PublishDatePrecision, Is.EqualTo(TemporalPrecision.Year));
        }
    }

    [Test]
    public async Task ListEnrichedAsync_ReturnsEarliestRowOfLibraryItem_WhenRowsLinkToOneLibraryItem()
    {
        // Arrange
        var container = new MetadataContainer();
        var contribution = new MetadataContribution { ProviderType = "tmdb", MetadataContainer = container };
        contribution.ExternalIds.Add(new() { Source = "tmdb", Value = "617126", Ordinal = 0 });
        contribution.ExternalIds.Add(new() { Source = "wikidata", Value = "Q105105219", Ordinal = 1 });
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new()
        {
            ContentType = "movie",
            Level = "movie",
            Slug = "the-fantastic-4-first-steps",
            Name = "The Fantastic 4: First Steps",
            MetadataContainer = container
        });
        _context.WishlistDiscovered.AddRange(
            new()
            {
                WishlistId = _wishlistId,
                ExternalIdSource = "wikidata",
                ExternalId = "Q105105219",
                Title = "The Fantastic Four: First Steps",
                DiscoveredAt = new DateTime(2026, 8, 24, 2, 47, 57, DateTimeKind.Utc)
            },
            new()
            {
                WishlistId = _wishlistId,
                ExternalIdSource = "tmdb",
                ExternalId = "617126",
                Title = "The Fantastic 4: First Steps",
                DiscoveredAt = new DateTime(2026, 8, 24, 2, 42, 30, DateTimeKind.Utc)
            },
            new()
            {
                WishlistId = _wishlistId,
                ExternalIdSource = "tmdb",
                ExternalId = "1979",
                Title = "Fantastic Four: Rise of the Silver Surfer",
                DiscoveredAt = new DateTime(2026, 8, 24, 3, 0, 36, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.ListEnrichedAsync(
            _wishlistId,
            new() { Paging = new() { PageNumber = 1, PageSize = 10 } }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(row => row.Title),
                Is.EqualTo(["Fantastic Four: Rise of the Silver Surfer", "The Fantastic 4: First Steps"]));
            Assert.That(page.TotalItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetCountsAsync_CountsLibraryItemOnce_WhenRowsLinkToOneLibraryItem()
    {
        // Arrange
        var container = new MetadataContainer();
        var contribution = new MetadataContribution { ProviderType = "tmdb", MetadataContainer = container };
        contribution.ExternalIds.Add(new() { Source = "tmdb", Value = "617126", Ordinal = 0 });
        contribution.ExternalIds.Add(new() { Source = "wikidata", Value = "Q105105219", Ordinal = 1 });
        _context.MetadataContributions.Add(contribution);
        _context.ContentHierarchyItems.Add(new()
        {
            ContentType = "movie",
            Level = "movie",
            Slug = "the-fantastic-4-first-steps",
            Name = "The Fantastic 4: First Steps",
            MetadataContainer = container
        });
        _context.WishlistDiscovered.AddRange(
            new()
            {
                WishlistId = _wishlistId,
                ExternalIdSource = "tmdb",
                ExternalId = "617126",
                Title = "The Fantastic 4: First Steps",
                DiscoveredAt = new DateTime(2026, 8, 24, 2, 42, 30, DateTimeKind.Utc)
            },
            new()
            {
                WishlistId = _wishlistId,
                ExternalIdSource = "wikidata",
                ExternalId = "Q105105219",
                Title = "The Fantastic Four: First Steps",
                DiscoveredAt = new DateTime(2026, 8, 24, 2, 47, 57, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var counts = await _subject.GetCountsAsync(_wishlistId, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(counts.TotalDiscovered, Is.EqualTo(1));
            Assert.That(counts.InLibrary, Is.EqualTo(1));
            Assert.That(counts.Missing, Is.EqualTo(1));
        }
    }
}
