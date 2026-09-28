using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Core.Content;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class MetadataSearchMembershipTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private MemoryCacheStore _cacheStore = null!;
    private MemoryCacheFactory _cacheFactory = null!;
    private IContentHierarchyItemRepository _repository = null!;
    private IContentTypeRegistry _registry = null!;
    private Mock<IContentHierarchyMetadataAdvancedSearcher> _provider = null!;
    private MetadataSearchService _subject = null!;

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
        _registry = new ContentTypeRegistry([], NullLogger<ContentTypeRegistry>.Instance);
        _repository = new ContentHierarchyItemRepository(
            _context, Mock.Of<IAuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IMetadataContainerRepository>(), _registry, Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, Mock.Of<IContentRepository>());
        _cacheStore = new MemoryCacheStore(TimeProvider.System);
        _cacheFactory = new MemoryCacheFactory(_cacheStore);
        _provider = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        _provider.SetupGet(provider => provider.ProviderType).Returns("provider");
        _provider.SetupGet(provider => provider.SupportedContentTypes).Returns(["movie", "tv"]);
        _provider.Setup(provider => provider.HydrateAsync(It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSearchCandidate candidate, string _, CancellationToken _) =>
                candidate with { Language = "en" });
        _subject = new MetadataSearchService([], [_provider.Object], _registry, _repository,
            TimeProvider.System, NullLogger<MetadataSearchService>.Instance, _cacheFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheFactory.Dispose();
        _cacheStore.Dispose();
        _context.Dispose();
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsCompleteIdentityScopes_WhenIdsRepeatAcrossTypesAndLevels()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new ContentHierarchyItemCriteria
                {
                    ContentType = new() { And = [new()
                    {
                        Operator = CriteriaValueOperator.In, Values = ["movie", "tv"]
                    }] }
                }]
            }
        };
        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            ContentType = "movie",
            Level = "root",
            Slug = "owned",
            Name = "Owned",
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "provider",
                        ExternalIds = { new ExternalId { Source = "source", Value = "shared" } }
                    }
                }
            }
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        _provider.Setup(provider => provider.ContinueSearchAsync("movie",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new()
                {
                    Supported = true,
                    Matches =
                    [
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "root",
                            ExternalIdSource = "source", ExternalId = "shared", Title = "Owned"
                        },
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "special",
                            ExternalIdSource = "source", ExternalId = "shared", Title = "Different level"
                        },
                        .. Enumerable.Range(1, 23).Select(id => new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "root",
                            ExternalIdSource = "source", ExternalId = $"extra-{id}", Title = "Extra"
                        })
                    ],
                    PageInfo = new() { RequestedPage = 1, HasMore = false }
                }
            });
        _provider.Setup(provider => provider.ContinueSearchAsync("tv",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new()
                {
                    Supported = true,
                    Matches =
                    [
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "tv", Level = "root",
                            ExternalIdSource = "source", ExternalId = "shared", Title = "Different type"
                        }
                    ],
                    PageInfo = new() { RequestedPage = 1, HasMore = false }
                }
            });

        // Act
        var result = await _subject.SearchCandidatesAsync(criteria, null, 1).ConfigureAwait(false);
        var continued = await _subject.SearchCandidatesAsync(criteria, null, 2, result.NextContinuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(25));
            Assert.That(result.ExistsInLibraryCount, Is.EqualTo(1));
            Assert.That(result.Matches[0].ExistingContentHierarchyItemId,
                Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000001")));
            Assert.That(result.Matches[1].ExistingContentHierarchyItemId, Is.Null);
            Assert.That(continued.Matches.Single().Candidate.ContentType, Is.EqualTo("tv"));
            Assert.That(continued.Matches.Single().Candidate.ExternalId, Is.EqualTo("shared"));
            Assert.That(continued.Matches.Single().ExistingContentHierarchyItemId, Is.Null);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_KeepsSeparateDetails_WhenAnIdIsReusedAtTwoLevels()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new ContentHierarchyItemCriteria
                {
                    Metadata = new() { And = [new MetadataCriteria
                    {
                        Language = new() { And = [new() { Value = "en" }] }
                    }] }
                }]
            }
        };
        _provider.Setup(provider => provider.ContinueSearchAsync("movie",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new()
                {
                    Supported = true,
                    Matches =
                    [
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "root",
                            ExternalIdSource = "source", ExternalId = "shared", Title = "Root"
                        },
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "special",
                            ExternalIdSource = "source", ExternalId = "shared", Title = "Special"
                        }
                    ],
                    PageInfo = new() { RequestedPage = 1, HasMore = false }
                }
            });

        // Act
        var first = await _subject.SearchCandidatesAsync(criteria, "movie", 1).ConfigureAwait(false);
        var warm = await _subject.SearchCandidatesAsync(criteria, "movie", 1, first.Continuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches.Select(match => match.Candidate.Level), Is.EqualTo(["root", "special"]));
            Assert.That(warm.Matches.Select(match => match.Candidate.Level), Is.EqualTo(["root", "special"]));
            _provider.Verify(provider => provider.HydrateAsync(It.IsAny<MetadataSearchCandidate>(),
                string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [TestCase("tmdb", "42", "tmdb_id", "00042", "movie", "movie", 0)]
    [TestCase("tmdb", "42", "TMDB-ID", "42", "movie", "movie", 0)]
    [TestCase("tmdb", "42", " tmdb ", " 00042 ", "movie", "movie", 0)]
    [TestCase("imdb", "tt42", "IMDB-ID", " TT42 ", "movie", "movie", 0)]
    [TestCase("tmdb", "0", "tmdb", "000", "movie", "movie", 0)]
    [TestCase("source", "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
        "source_id", "A1B2C3D4E5F64A7B8C9D0E1F2A3B4C5D", "movie", "movie", 0)]
    [TestCase("source", "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
        "source", " {a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d} ", "movie", "movie", 0)]
    [TestCase("tmdb", "42", "tmdb_id", "00042", "tv", "movie", 1)]
    [TestCase("tmdb", "42", "tmdb_id", "00042", "movie", "special", 1)]
    [TestCase("tmdb", "42", "tvmaze", "42", "movie", "movie", 1)]
    [TestCase("tmdb", "42", "tmdb", "43", "movie", "movie", 1)]
    public async Task SearchCandidatesAsync_PreservesCanonicalDeliveryScope_WhenIdSpellingsChangeAcrossPages(
        string firstSource,
        string firstValue,
        string laterSource,
        string laterValue,
        string laterType,
        string laterLevel,
        int expectedLaterCount)
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new ContentHierarchyItemCriteria
                {
                    ContentType = new() { And = [new()
                    {
                        Operator = CriteriaValueOperator.In, Values = ["movie", "tv"]
                    }] }
                }]
            }
        };
        _provider.Setup(provider => provider.ContinueSearchAsync("movie",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Continuation = "next",
                Page = new()
                {
                    Supported = true,
                    Matches =
                    [
                        new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "movie",
                            ExternalIdSource = firstSource, ExternalId = firstValue, Title = "First"
                        },
                        .. Enumerable.Range(1, 24).Select(id => new MetadataSearchCandidate
                        {
                            ProviderType = "provider", ContentType = "movie", Level = "movie",
                            ExternalIdSource = "filler", ExternalId = $"filler-{id}", Title = "Filler"
                        })
                    ],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                }
            });
        _provider.Setup(provider => provider.ContinueSearchAsync("tv",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Continuation = "next",
                Page = new() { Supported = true, Matches = [], PageInfo = new() { RequestedPage = 1, HasMore = true } }
            });
        _provider.Setup(provider => provider.ContinueSearchAsync(It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(), string.Empty, "next",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new() { Supported = true, Matches = [], PageInfo = new() { RequestedPage = 2, HasMore = false } }
            });
        _provider.Setup(provider => provider.ContinueSearchAsync(laterType,
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, "next", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new()
                {
                    Supported = true,
                    Matches = [new MetadataSearchCandidate
                    {
                        ProviderType = "provider", ContentType = laterType, Level = laterLevel,
                        ExternalIdSource = laterSource, ExternalId = laterValue, Title = "Later"
                    }],
                    PageInfo = new() { RequestedPage = 2, HasMore = false }
                }
            });

        // Act
        var first = await _subject.SearchCandidatesAsync(criteria, null, 1).ConfigureAwait(false);
        var continued = await _subject.SearchCandidatesAsync(criteria, null, 2, first.NextContinuation)
            .ConfigureAwait(false);
        var retried = await _subject.SearchCandidatesAsync(criteria, null, 2, first.NextContinuation)
            .ConfigureAwait(false);
        var replayed = await _subject.SearchCandidatesAsync(criteria, null, 1, first.Continuation)
            .ConfigureAwait(false);
        var continuedReplay = await _subject.SearchCandidatesAsync(criteria, null, 2, replayed.NextContinuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches, Has.Count.EqualTo(25));
            Assert.That(first.PageInfo.HasMore, Is.True);
            Assert.That(continued.Matches, Has.Count.EqualTo(expectedLaterCount));
            Assert.That(continued.PageInfo.HasMore, Is.False);
            Assert.That(retried.Matches, Has.Count.EqualTo(expectedLaterCount));
            Assert.That(replayed.Matches, Has.Count.EqualTo(25));
            Assert.That(continuedReplay.Matches, Has.Count.EqualTo(expectedLaterCount));
            _provider.Verify(provider => provider.HydrateAsync(It.IsAny<MetadataSearchCandidate>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_RefreshesMembership_WhenCheckpointsAndWarmDetailsAreReused()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new ContentHierarchyItemCriteria
                {
                    Metadata = new() { And = [new MetadataCriteria
                    {
                        Language = new() { And = [new() { Value = "en" }] }
                    }] }
                }]
            }
        };
        _provider.Setup(provider => provider.ContinueSearchAsync("movie",
                It.IsAny<ContentHierarchyItemCriteria>(), 25, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Continuation = "next",
                Page = new()
                {
                    Supported = true,
                    Matches = [.. Enumerable.Range(1, 26).Select(id => new MetadataSearchCandidate
                    {
                        ProviderType = "provider", ContentType = "movie", Level = "movie",
                        ExternalIdSource = "source", ExternalId = $"id-{id}", Title = "Candidate"
                    })],
                    PageInfo = new() { RequestedPage = 1, HasMore = true }
                }
            });
        _provider.Setup(provider => provider.ContinueSearchAsync("movie",
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>(), string.Empty, "next",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataSearchBatch
            {
                Page = new()
                {
                    Supported = true,
                    Matches = [],
                    PageInfo = new() { RequestedPage = 2, HasMore = false }
                }
            });
        var first = await _subject.SearchCandidatesAsync(criteria, "movie", 1).ConfigureAwait(false);
        var warm = await _subject.SearchCandidatesAsync(criteria, "movie", 1, first.Continuation)
            .ConfigureAwait(false);
        _context.ContentHierarchyItems.AddRange(
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                ContentType = "movie",
                Level = "movie",
                Slug = "first",
                Name = "First",
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "provider",
                            ExternalIds = { new ExternalId { Source = "source", Value = "id-1" } }
                        }
                    }
                }
            },
            new ContentHierarchyItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000026"),
                ContentType = "movie",
                Level = "movie",
                Slug = "continued",
                Name = "Continued",
                MetadataContainer = new MetadataContainer
                {
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "provider",
                            ExternalIds = { new ExternalId { Source = "source", Value = "id-26" } }
                        }
                    }
                }
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var continued = await _subject.SearchCandidatesAsync(criteria, "movie", 2, first.NextContinuation)
            .ConfigureAwait(false);
        var revisited = await _subject.SearchCandidatesAsync(criteria, "movie", 1, first.Continuation)
            .ConfigureAwait(false);
        var retried = await _subject.SearchCandidatesAsync(criteria, "movie", 2, first.NextContinuation)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Matches, Has.Count.EqualTo(25));
            Assert.That(first.ExistsInLibraryCount, Is.Zero);
            Assert.That(warm.ExistsInLibraryCount, Is.Zero);
            Assert.That(continued.Matches.Single().ExistingContentHierarchyItemId,
                Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000026")));
            Assert.That(revisited.Matches[0].ExistingContentHierarchyItemId,
                Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000011")));
            Assert.That(retried.Matches.Single().ExistingContentHierarchyItemId,
                Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000026")));
            _provider.Verify(provider => provider.HydrateAsync(It.IsAny<MetadataSearchCandidate>(),
                string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(26));
        }
    }
}
