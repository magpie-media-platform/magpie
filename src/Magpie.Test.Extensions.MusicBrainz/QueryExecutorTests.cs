using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class QueryExecutorTests
{
    private static readonly PlanPrecondition NoPrecondition = new() { Kind = PlanPreconditionKind.None };

    private static readonly FetchPlanNode ScanFetch = new()
    {
        Id = 1,
        Precondition = NoPrecondition,
        Contributes = "candidate rows",
        Target = QueryTarget.Artist,
        AccessMode = AccessMode.Search,
        AnchorKind = AnchorKind.UnfieldedText,
        CombineMode = CombineMode.And,
        Filters = [],
        OrdinaryTerm = "Wanted"
    };

    private static readonly FilterPlanNode ScanRoot = new()
    {
        Id = 2,
        Precondition = NoPrecondition,
        Contributes = "post-residual survivor rows",
        Child = ScanFetch,
        Filters =
        [
            new QueryFilter
            {
                Kind = QueryFilterKind.Title,
                Operator = CriteriaValueOperator.ContainsString,
                Negate = false,
                StringValues = ["Keep"],
                VerifyLocally = true
            }
        ],
        Residuals = []
    };

    private static readonly QueryPlan ScanPlan = new()
    {
        RequestedPage = 1,
        MaxResults = 25,
        Settings = new Settings(),
        Warnings = [],
        Fingerprint = "fp-scan",
        Root = ScanRoot
    };

    private static readonly FetchPlanNode AlbumScanFetch = new()
    {
        Id = 1,
        Precondition = NoPrecondition,
        Contributes = "candidate rows",
        Target = QueryTarget.Album,
        AccessMode = AccessMode.Search,
        AnchorKind = AnchorKind.UnfieldedText,
        CombineMode = CombineMode.And,
        Filters = [],
        OrdinaryTerm = "Low"
    };

    private static readonly FilterPlanNode AlbumScanRoot = new()
    {
        Id = 2,
        Precondition = NoPrecondition,
        Contributes = "post-residual survivor rows",
        Child = AlbumScanFetch,
        Filters = [],
        Residuals = []
    };

    private static readonly QueryPlan AlbumScanPlan = new()
    {
        RequestedPage = 1,
        MaxResults = 25,
        Settings = new Settings(),
        Warnings = [],
        Fingerprint = "fp-album-scan",
        Root = AlbumScanRoot
    };

    private Mock<IMusicBrainzClient> _client = null!;
    private Mock<IQueryCoordinator> _cache = null!;
    private Mock<ICoverArtArchiveClient> _coverClient = null!;
    private Dictionary<string, VerifiedScan> _scans = null!;
    private IQueryExecutor _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IMusicBrainzClient>();
        _cache = new Mock<IQueryCoordinator>();
        _coverClient = new Mock<ICoverArtArchiveClient>();
        _scans = [];
        _cache.Setup(cache => cache.GetSearchPageAsync(
                It.IsAny<SearchCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<SearchPage>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                SearchCacheKey _,
                Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
                CancellationToken token) => loader(token));
        _cache.Setup(cache => cache.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                EntityCacheKey _,
                Func<CancellationToken, Task<FetchResult<EntityResponseDto>>> loader,
                CancellationToken token) => loader(token));
        _cache.Setup(cache => cache.GetIsrcAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<SearchPage>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
                CancellationToken token) => loader(token));
        _cache.Setup(cache => cache.GetArtistResolutionAsync(
                It.IsAny<ArtistResolutionCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<ArtistResolutionResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                ArtistResolutionCacheKey _,
                Func<CancellationToken, Task<FetchResult<ArtistResolutionResponse>>> loader,
                CancellationToken token) => loader(token));
        _cache.Setup(cache => cache.GetVerifiedScanAsync(
                It.IsAny<string>(),
                It.IsAny<Func<VerifiedScan, bool>>(),
                It.IsAny<Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string fingerprint,
                Func<VerifiedScan, bool> isSufficient,
                Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>> loader,
                CancellationToken token) =>
            {
                while (true)
                {
                    _scans.TryGetValue(fingerprint, out var existing);
                    if (existing is not null && isSufficient(existing))
                    {
                        return new FetchResult<VerifiedScan> { Outcome = FetchOutcome.Success, Value = existing };
                    }

                    var result = await loader(existing, token).ConfigureAwait(false);
                    if (result is { Outcome: FetchOutcome.Success, Value: { } scan })
                    {
                        _scans[fingerprint] = scan;
                    }

                    if (result.Outcome != FetchOutcome.Success ||
                        result.Value is null ||
                        isSufficient(result.Value))
                    {
                        return result;
                    }
                }
            });
        _cache.Setup(cache => cache.GetArtistBrowsePageAsync(
                It.IsAny<ArtistBrowseCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<ArtistBrowseResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                ArtistBrowseCacheKey _,
                Func<CancellationToken, Task<FetchResult<ArtistBrowseResponseDto>>> loader,
                CancellationToken token) => loader(token));
        _cache.Setup(cache => cache.GetCoverAsync(
                It.IsAny<CoverCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<Uri>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                CoverCacheKey _,
                Func<CancellationToken, Task<FetchResult<Uri>>> loader,
                CancellationToken token) => loader(token));
        _subject = new QueryExecutor(_client.Object, _cache.Object, _coverClient.Object);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmpty_WhenPlanHasNoRoot()
    {
        // Arrange
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-no-root",
            Root = null
        };

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(1));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmpty_WhenRootIsEmptyUnion()
    {
        // Arrange
        var root = new UnionPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "planned empty result",
            Children = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-empty-union",
            Root = root
        };

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsHit_WhenDirectSearchSucceeds()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Bjork"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-direct-search",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists = [new ArtistDto { Id = "11111111-1111-1111-1111-111111111111", Name = "Bjork" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(result.Hits[0].Title, Is.EqualTo("Bjork"));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(1));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget ==
                    "artist?fmt=json&query=Bjork&limit=25&offset=0&dismax=true"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsFailureWarning_WhenSearchTransportFails()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Bjork"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-search-failure",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz is unavailable."]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsInvalidResponseWarning_WhenSearchBodyIsMalformed()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Bjork"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-search-malformed",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage> { Outcome = FetchOutcome.Malformed });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz returned an invalid response."]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOversizedWarning_WhenQueryExceedsRequestSizeLimit()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = new string('a', 8300)
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-oversized",
            Root = fetch
        };

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz rejected a query larger than 8 KiB before transport."]));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsHit_WhenEntityLookupSucceeds()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = "22222222-2222-2222-2222-222222222222"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-entity-lookup",
            Root = fetch
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = "22222222-2222-2222-2222-222222222222",
                        Name = "Radiohead"
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("22222222-2222-2222-2222-222222222222"));
            Assert.That(result.Hits[0].Title, Is.EqualTo("Radiohead"));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmpty_WhenEntityLookupNotFound()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = "33333333-3333-3333-3333-333333333333"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-entity-not-found",
            Root = fetch
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsHit_WhenIsrcLookupSucceeds()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.IsrcLookup,
            AnchorKind = AnchorKind.Isrc,
            CombineMode = CombineMode.And,
            Filters = [],
            DirectValue = "USRC17607839"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-isrc-lookup",
            Root = fetch
        };
        _client.Setup(client => client.GetIsrcAsync(
                "USRC17607839",
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Recordings =
                    [
                        new RecordingDto
                        {
                            Id = "44444444-4444-4444-4444-444444444444",
                            Title = "Idioteque",
                            Isrcs = ["USRC17607839"]
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("44444444-4444-4444-4444-444444444444"));
            Assert.That(result.Hits[0].Title, Is.EqualTo("Idioteque"));
        }
    }

    [Test]
    public async Task ExecuteAsync_DedupesHits_WhenUnionBranchesResolveTheSameEntity()
    {
        // Arrange
        const string mbid = "55555555-5555-5555-5555-555555555555";
        var branchOne = new FetchPlanNode
        {
            Id = 10,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = mbid
        };
        var branchTwo = branchOne with { Id = 11 };
        var root = new UnionPlanNode
        {
            Id = 12,
            Precondition = NoPrecondition,
            Contributes = "artists present in any branch",
            Children = [branchOne, branchTwo]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-union-dedupe",
            Root = root
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto { Artist = new ArtistDto { Id = mbid, Name = "Boards of Canada" } }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo(mbid));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_KeepsOnlyCommonHits_WhenIntersectingTwoBranches()
    {
        // Arrange
        const string mbidA = "66666666-6666-6666-6666-666666666666";
        const string mbidB = "77777777-7777-7777-7777-777777777777";
        var searchBranch = new FetchPlanNode
        {
            Id = 20,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "band"
        };
        var lookupBranch = new FetchPlanNode
        {
            Id = 21,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = mbidB
        };
        var root = new IntersectPlanNode
        {
            Id = 22,
            Precondition = NoPrecondition,
            Contributes = "artists present in every branch",
            Children = [searchBranch, lookupBranch]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-intersect",
            Root = root
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = mbidA, Name = "Artist A" },
                        new ArtistDto { Id = mbidB, Name = "Artist B" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto { Artist = new ArtistDto { Id = mbidB, Name = "Artist B" } }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.PrimaryMbid), Is.EqualTo([mbidB]));
    }

    [Test]
    public async Task ExecuteAsync_ResolvedArtist_WhenCompositionHasKnownMbid()
    {
        // Arrange
        var composition = new ArtistComposition
        {
            RelatedEntity = ArtistCompositionEntity.Artist,
            Resolution = ArtistCompositionResolution.KnownMbid,
            Expansion = ArtistCompositionExpansion.BrowseArtists,
            Constraint = ArtistCompositionConstraint.Include,
            FinalStage = ArtistCompositionFinalStage.RunComposedSearch,
            ExternalIdSource = Constants.ProviderType,
            RelatedMbid = "88888888-8888-8888-8888-888888888888",
            RelatedName = "Known Artist"
        };
        var root = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = composition
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolve-known-mbid",
            Root = root
        };

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Target, Is.EqualTo(QueryTarget.Artist));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("88888888-8888-8888-8888-888888888888"));
            Assert.That(result.Hits[0].Title, Is.EqualTo("Known Artist"));
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsHit_WhenLinkedArtistBrowseSucceeds()
    {
        // Arrange
        const string artistMbid = "99999999-9999-9999-9999-999999999999";
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "content linked to one artist",
            Target = QueryTarget.Album,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            DirectValue = artistMbid
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-linked-browse",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "10101010-1010-1010-1010-101010101010",
                            Title = "In Rainbows"
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("10101010-1010-1010-1010-101010101010"));
            Assert.That(result.Hits[0].Title, Is.EqualTo("In Rainbows"));
        }

        _client.Verify(
            client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget ==
                    "release-group?fmt=json&artist=" + artistMbid + "&inc=artist-credits&limit=100&offset=0"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_AppliesLocalFilter_WhenVerifiedScanRunsAgainstASearchFetch()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Wanted"
        };
        var localFilter = new QueryFilter
        {
            Kind = QueryFilterKind.Title,
            Operator = CriteriaValueOperator.ContainsString,
            Negate = false,
            StringValues = ["Alpha"],
            VerifyLocally = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [localFilter],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-verified-local-filter",
            Root = root
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "12121212-1212-1212-1212-121212121212", Name = "Alpha Team" },
                        new ArtistDto { Id = "13131313-1313-1313-1313-131313131313", Name = "Beta Team" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("12121212-1212-1212-1212-121212121212"));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReusesVerifiedScan_WhenExecutedTwiceWithTheSameFingerprint()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Wanted"
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-verified-reuse",
            Root = root
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists = [new ArtistDto { Id = "14141414-1414-1414-1414-141414141414", Name = "Reused Artist" }]
                }
            });

        // Act
        var first = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);
        var second = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Hits, Has.Count.EqualTo(1));
            Assert.That(second.Hits, Has.Count.EqualTo(1));
            Assert.That(first.Hits[0].PrimaryMbid, Is.EqualTo("14141414-1414-1414-1414-141414141414"));
            Assert.That(second.Hits[0].PrimaryMbid, Is.EqualTo("14141414-1414-1414-1414-141414141414"));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsTheClient_WhenTheCacheAlreadyHoldsTheSearchPage()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Cached"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-cache-hit",
            Root = fetch
        };
        _cache.Setup(cache => cache.GetSearchPageAsync(
                It.IsAny<SearchCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<SearchPage>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists = [new ArtistDto { Id = "15151515-1515-1515-1515-151515151515", Name = "From Cache" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(1));
        Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("15151515-1515-1515-1515-151515151515"));

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEveryRow_WhenMaxResultsExceedsTheDefaultPageSize()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Track"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 30,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-max-results",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 30,
                    Offset = 0,
                    Recordings = [.. Enumerable.Range(1, 30)
                        .Select(index => new RecordingDto
                        {
                            Id = "00000000-0000-0000-0000-0000000000" +
                                index.ToString("D2", CultureInfo.InvariantCulture),
                            Title = "Track " + index.ToString(CultureInfo.InvariantCulture),
                            Length = 100
                        })]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(30));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.SearchAsync(
                It.Is<Request>(request => request.Limit == 30 && request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsFailureWarning_WhenEntityLookupTransportFails()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-lookup-failure",
            Root = fetch
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz is unavailable."]));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_KeepsProviderTotal_WhenTheDirectPageIsNotTheFirst()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Album,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Album"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 2,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-direct-second-page",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1000,
                    Offset = 25,
                    ReleaseGroups = [.. Enumerable.Range(1, 25)
                        .Select(index => new ReleaseGroupDto
                        {
                            Id = "00000000-0000-0000-0000-0000000000" +
                                index.ToString("D2", CultureInfo.InvariantCulture),
                            Title = "Album " + index.ToString(CultureInfo.InvariantCulture)
                        })]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(25));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(2));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(
            client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 25),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReportsMore_WhenTheDirectPageDropsAnUnmappableRow()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Track"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-direct-drop",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 26,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto { Id = "invalid", Title = "Dropped", Length = 1000 },
                        new RecordingDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Kept",
                            Length = 1000
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo(["Kept"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [TestCase(1, 2, true)]
    [TestCase(2, 1, false)]
    public async Task ExecuteAsync_PagesIsrcHits_WhenTheLookupReturnsMoreThanOnePage(
        int requestedPage,
        int expectedHits,
        bool expectedHasMore)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.IsrcLookup,
            AnchorKind = AnchorKind.Isrc,
            CombineMode = CombineMode.And,
            Filters = [],
            DirectValue = "USRC17607839"
        };
        var plan = new QueryPlan
        {
            RequestedPage = requestedPage,
            MaxResults = 2,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-isrc-paging",
            Root = fetch
        };
        _client.Setup(client => client.GetIsrcAsync(
                It.IsAny<string>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 3,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto { Id = "20000000-0000-0000-0000-000000000001", Title = "First" },
                        new RecordingDto { Id = "20000000-0000-0000-0000-000000000002", Title = "Second" },
                        new RecordingDto { Id = "20000000-0000-0000-0000-000000000003", Title = "Third" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(requestedPage));
            Assert.That(result.PageInfo.HasMore, Is.EqualTo(expectedHasMore));
        }
    }

    [Test]
    public async Task ExecuteAsync_FetchesTheNextBatch_WhenTheFirstScanBatchHasTooFewSurvivors()
    {
        // Arrange
        var plan = ScanPlan;
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 200,
                    Offset = 0,
                    Artists = [.. Enumerable.Range(1, 100)
                        .Select(index => new ArtistDto
                        {
                            Id = "10000000-0000-0000-0000-000000000" +
                                index.ToString("D3", CultureInfo.InvariantCulture),
                            Name = index <= 5 ? "Keep" : "Drop"
                        })]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 100),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 200,
                    Offset = 100,
                    Artists = [.. Enumerable.Range(1, 100)
                        .Select(index => new ArtistDto
                        {
                            Id = "20000000-0000-0000-0000-000000000" +
                                index.ToString("D3", CultureInfo.InvariantCulture),
                            Name = "Keep"
                        })]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(25));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("10000000-0000-0000-0000-000000000001"));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_StopsScanning_WhenTheBatchIsShorterThanTheBatchSize()
    {
        // Arrange
        var plan = ScanPlan;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 40,
                    Offset = 0,
                    Artists = [.. Enumerable.Range(1, 40)
                        .Select(index => new ArtistDto
                        {
                            Id = "10000000-0000-0000-0000-000000000" +
                                index.ToString("D3", CultureInfo.InvariantCulture),
                            Name = index <= 25 ? "Keep" : "Drop"
                        })]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(25));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsFailureWarning_WhenTheBatchAfterAFullPageFails()
    {
        // Arrange
        var plan = ScanPlan;
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 200,
                    Offset = 0,
                    Artists = [.. Enumerable.Range(1, 100)
                        .Select(index => new ArtistDto
                        {
                            Id = "10000000-0000-0000-0000-000000000" +
                                index.ToString("D3", CultureInfo.InvariantCulture),
                            Name = index <= 25 ? "Keep" : "Drop"
                        })]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 100),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(25));
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz is unavailable."]));
        }
    }

    [Test]
    public async Task ExecuteAsync_AdvancesTheScanOffsetByRawRows_WhenTheBatchHasAnUnmappableRow()
    {
        // Arrange
        var plan = ScanPlan;
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 200,
                    Offset = 0,
                    Artists = [.. Enumerable.Range(1, 100)
                        .Select(index => new ArtistDto
                        {
                            Id = index == 1
                                ? "invalid"
                                : "10000000-0000-0000-0000-000000000" +
                                    index.ToString("D3", CultureInfo.InvariantCulture),
                            Name = "Drop"
                        })]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 100),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            { Outcome = FetchOutcome.Success, Value = new SearchPage { Count = 200, Offset = 100 } });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);

        _client.Verify(
            client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 100),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOversizedWarning_WhenTheScanRequestExceedsTheSizeLimit()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = new string('x', 9000)
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-scan-oversized",
            Root = root
        };

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz rejected a query larger than 8 KiB before transport."]));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_KeepsBrowsingByRawRows_WhenABrowsePageRowIsUnmappable()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "content linked to one artist",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            DirectValue = "99999999-9999-9999-9999-999999999999"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-browse-unmappable",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 4,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto { Id = "30000000-0000-0000-0000-000000000001", Title = "First" },
                        new RecordingDto { Id = "invalid", Title = "Dropped" }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 2),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 4,
                    Offset = 2,
                    Recordings =
                    [
                        new RecordingDto { Id = "30000000-0000-0000-0000-000000000002", Title = "Second" },
                        new RecordingDto { Id = "also-invalid", Title = "Dropped" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo(["First", "Second"]));

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestCase(1, "First", "Second", true)]
    [TestCase(2, "Third", "Fourth", false)]
    public async Task ExecuteAsync_ReturnsTheRequestedBrowsePage_WhenBrowsingSpansTwoPages(
        int requestedPage,
        string firstTitle,
        string secondTitle,
        bool expectedHasMore)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "content linked to one artist",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            DirectValue = "99999999-9999-9999-9999-999999999999"
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = requestedPage,
            MaxResults = 2,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-browse-goal",
            Root = root
        };
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 0),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 4,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto { Id = "40000000-0000-0000-0000-000000000001", Title = "First" },
                        new RecordingDto { Id = "40000000-0000-0000-0000-000000000002", Title = "Second" }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.Offset == 2),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 4,
                    Offset = 2,
                    Recordings =
                    [
                        new RecordingDto { Id = "40000000-0000-0000-0000-000000000003", Title = "Third" },
                        new RecordingDto { Id = "40000000-0000-0000-0000-000000000004", Title = "Fourth" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo([firstTitle, secondTitle]));
            Assert.That(result.PageInfo.HasMore, Is.EqualTo(expectedHasMore));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_BrowsesEveryResolvedArtist_WhenTwoResolversContributeIdentities()
    {
        // Arrange
        var firstResolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.KnownMbid,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedMbid = "50000000-0000-0000-0000-000000000001"
            }
        };
        var secondResolver = firstResolver with
        {
            Id = 2,
            Composition = firstResolver.Composition with { RelatedMbid = "50000000-0000-0000-0000-000000000002" }
        };
        var fetch = new FetchPlanNode
        {
            Id = 3,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [firstResolver, secondResolver]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-browse-two-artists",
            Root = fetch
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Recordings = [new RecordingDto { Id = "60000000-0000-0000-0000-000000000001", Title = "Shared" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo(["Shared"]));

        using (Assert.EnterMultipleScope())
        {
            _client.Verify(
                client => client.SearchAsync(
                    It.Is<Request>(request => request.RelativeTarget.Contains(
                        "artist=50000000-0000-0000-0000-000000000001",
                        StringComparison.Ordinal)),
                    It.IsAny<Settings>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _client.Verify(
                client => client.SearchAsync(
                    It.Is<Request>(request => request.RelativeTarget.Contains(
                        "artist=50000000-0000-0000-0000-000000000002",
                        StringComparison.Ordinal)),
                    It.IsAny<Settings>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_StopsBeforeTheBrowse_WhenArtistResolutionFails()
    {
        // Arrange
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.ExactNameSearch,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedName = "Miles Davis"
            }
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-failure",
            Root = fetch
        };
        _client.Setup(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz is unavailable."]));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsLaterBranches_WhenAnEarlierIntersectionBranchIsEmpty()
    {
        // Arrange
        var firstBranch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Lookup,
            AnchorKind = AnchorKind.Mbid,
            CombineMode = CombineMode.And,
            Filters = [],
            EntityKind = EntityKind.Artist,
            DirectValue = "70000000-0000-0000-0000-000000000001"
        };
        var secondBranch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Never fetched"
        };
        var root = new IntersectPlanNode
        {
            Id = 3,
            Precondition = NoPrecondition,
            Contributes = "rows in every branch",
            Children = [firstBranch, secondBranch]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-intersect-pruned",
            Root = root
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DrainsPendingCandidatesAndKeepsWarnings_WhenTheScanCarriesDeferredResiduals()
    {
        // Arrange
        var genreResidual = new ResidualCriterion
        {
            Kind = ResidualKind.Genre,
            Need = ResidualNeed.EntityGenres,
            Alternatives =
            [
                new ResidualAlternative
                {
                    Predicates =
                    [
                        new ResidualPredicate
                        {
                            Field = ResidualField.GenreName,
                            Operator = CriteriaValueOperator.Equals,
                            Values = ["rock"]
                        }
                    ]
                }
            ]
        };
        var coverResidual = new ResidualCriterion
        {
            Kind = ResidualKind.ImagePresence,
            Need = ResidualNeed.CoverArt,
            Alternatives =
            [
                new ResidualAlternative
                {
                    Predicates =
                    [
                        new ResidualPredicate
                        {
                            Field = ResidualField.ImagePresence,
                            Operator = CriteriaValueOperator.IsNotNull
                        }
                    ]
                }
            ]
        };
        var plan = ScanPlan with
        {
            Root = ScanRoot with { Filters = [], Residuals = [genreResidual, coverResidual] }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 4,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "80000000-0000-0000-0000-000000000001", Name = "Keep" },
                        new ArtistDto { Id = "80000000-0000-0000-0000-000000000002", Name = "Reject" },
                        new ArtistDto { Id = "80000000-0000-0000-0000-000000000003", Name = "Keep" },
                        new ArtistDto { Id = "80000000-0000-0000-0000-000000000004", Name = "Reject" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityCacheKey key, Settings _, CancellationToken _) => new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = key.Mbid,
                        Genres =
                        [
                            new GenreDto
                            {
                                Name = key.Mbid.EndsWith("1", StringComparison.Ordinal) ||
                                    key.Mbid.EndsWith("3", StringComparison.Ordinal) ? "rock" : "jazz",
                                Count = 1
                            }
                        ]
                    }
                }
            });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(2));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("80000000-0000-0000-0000-000000000001"));
            Assert.That(result.Hits[1].PrimaryMbid, Is.EqualTo("80000000-0000-0000-0000-000000000003"));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify image-presence criteria because cover-art lookup failed; " +
                        "affected candidates were retained."
                ]));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [TestCase("Miles Davis", 1)]
    [TestCase("Miles Davis Quintet", 0)]
    public async Task ExecuteAsync_BrowsesOnlyExactResolutionMatches_WhenTheCompositionResolvesByName(
        string candidateName,
        int expectedBrowseCount)
    {
        // Arrange
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.ExactNameSearch,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedName = "Miles Davis"
            }
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-name",
            Root = fetch
        };
        _client.Setup(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistResolutionResponse
                {
                    Count = 1,
                    ReturnedCount = 1,
                    Candidates =
                    [
                        new ArtistResolutionCandidate
                        {
                            Mbid = "90000000-0000-0000-0000-000000000001",
                            Name = candidateName
                        }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Recordings = [new RecordingDto { Id = "90000000-0000-0000-0000-000000000009", Title = "So What" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(expectedBrowseCount));

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedBrowseCount));
    }

    [TestCase("Person", 1)]
    [TestCase("Group", 0)]
    public async Task ExecuteAsync_BrowsesOnlyMatchingArtistTypes_WhenTheCompositionLooksUpATypedMbid(
        string artistType,
        int expectedBrowseCount)
    {
        // Arrange
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.TypedMbidLookup,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedMbid = "a0000000-0000-0000-0000-000000000001",
                RelatedArtistType = "Person"
            }
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-typed-mbid",
            Root = fetch
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = "a0000000-0000-0000-0000-000000000001",
                        Name = "Miles Davis",
                        Type = artistType
                    }
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Recordings = [new RecordingDto { Id = "a0000000-0000-0000-0000-000000000009", Title = "So What" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(expectedBrowseCount));

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedBrowseCount));
    }

    [Test]
    public async Task ExecuteAsync_ReadsEveryResolutionPage_WhenTheResponseIsPaged()
    {
        // Arrange
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.ExactNameSearch,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedName = "Miles Davis"
            }
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-paged",
            Root = resolver
        };
        _client.SetupSequence(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistResolutionResponse
                {
                    Count = 2,
                    ReturnedCount = 1,
                    Candidates =
                    [
                        new ArtistResolutionCandidate
                        {
                            Mbid = "b0000000-0000-0000-0000-000000000001",
                            Name = "Miles Davis"
                        }
                    ]
                }
            })
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistResolutionResponse
                {
                    Count = 2,
                    ReturnedCount = 1,
                    Candidates =
                    [
                        new ArtistResolutionCandidate
                        {
                            Mbid = "b0000000-0000-0000-0000-000000000002",
                            Name = "Miles Davis"
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Hits.Select(hit => hit.PrimaryMbid),
            Is.EqualTo(
            [
                "b0000000-0000-0000-0000-000000000001",
                "b0000000-0000-0000-0000-000000000002"
            ]));

        _client.Verify(
            client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_CarriesSeedLabelsAndLanguages_WhenTheScanUsesTheReleaseIndex()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Album,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.LabelRelationship,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    LabelValues =
                    [
                        new LabelRelationshipValue
                        {
                            LabelMbid = "44444444-4444-4444-8444-444444444444",
                            LabelName = "Blue Note"
                        }
                    ],
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                }
            ],
            UsesReleaseIndexComposition = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-release-index",
            Root = root
        };
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith("release?", StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Releases =
                    [
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000001",
                            Title = "Blue Train",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "c0000000-0000-0000-0000-00000000000a",
                                Title = "Blue Train"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" },
                            LabelInfo =
                            [
                                new LabelInfoDto
                                {
                                    Label = new LabelDto
                                    {
                                        Id = "44444444-4444-4444-8444-444444444444",
                                        Name = "Blue Note"
                                    }
                                }
                            ]
                        }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith(
                    "release-group?",
                    StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "c0000000-0000-0000-0000-00000000000a",
                            Title = "Blue Train"
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Languages, Is.EqualTo(["eng"]));
            Assert.That(
                result.Hits[0].LabelReferences?.Select(reference => reference.Name),
                Is.EqualTo(["Blue Note"]));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReadsEveryBrowsePage_WhenTheCompositionExpandsRelatedArtists()
    {
        // Arrange
        var composition = new ArtistComposition
        {
            RelatedEntity = ArtistCompositionEntity.ReleaseGroup,
            Resolution = ArtistCompositionResolution.KnownMbid,
            Expansion = ArtistCompositionExpansion.BrowseArtists,
            Constraint = ArtistCompositionConstraint.Include,
            FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
            ExternalIdSource = Constants.ProviderType,
            RelatedMbid = "d0000000-0000-0000-0000-000000000001"
        };
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = composition
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "expanded artist candidates",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver],
            BrowseComposition = composition
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-expansion-paged",
            Root = fetch
        };
        _client.SetupSequence(client => client.BrowseArtistsAsync(
                It.IsAny<ArtistBrowseCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ArtistBrowseResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistBrowseResponseDto
                {
                    ArtistCount = 2,
                    Artists = [new ArtistDto { Id = "d0000000-0000-0000-0000-00000000000a", Name = "First" }]
                }
            })
            .ReturnsAsync(new FetchResult<ArtistBrowseResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistBrowseResponseDto
                {
                    ArtistCount = 2,
                    Artists = [new ArtistDto { Id = "d0000000-0000-0000-0000-00000000000b", Name = "Second" }]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo(["First", "Second"]));

        _client.Verify(
            client => client.BrowseArtistsAsync(
                It.IsAny<ArtistBrowseCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_KeepsOnlyBandMembers_WhenTheCompositionExpandsBandMembership()
    {
        // Arrange
        var composition = new ArtistComposition
        {
            RelatedEntity = ArtistCompositionEntity.Artist,
            Resolution = ArtistCompositionResolution.KnownMbid,
            Expansion = ArtistCompositionExpansion.BandMembers,
            Constraint = ArtistCompositionConstraint.Include,
            FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
            ExternalIdSource = Constants.ProviderType,
            RelatedMbid = "e0000000-0000-0000-0000-000000000001"
        };
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = composition
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "expanded artist candidates",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver],
            BrowseComposition = composition
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-band-members",
            Root = fetch
        };
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = "e0000000-0000-0000-0000-000000000001",
                        Name = "The Band",
                        Type = "Group",
                        Relations =
                        [
                            new RelationDto
                            {
                                Type = Constants.BandMembershipRelationshipKind,
                                TargetType = Constants.ArtistRelationTarget,
                                Direction = "backward",
                                Artist = new ArtistDto
                                {
                                    Id = "e0000000-0000-0000-0000-00000000000a",
                                    Name = "Member"
                                }
                            },
                            new RelationDto
                            {
                                Type = Constants.PerformerRelationshipKind,
                                TargetType = Constants.ArtistRelationTarget,
                                Direction = "backward",
                                Artist = new ArtistDto
                                {
                                    Id = "e0000000-0000-0000-0000-00000000000b",
                                    Name = "Performer"
                                }
                            },
                            new RelationDto
                            {
                                Type = Constants.BandMembershipRelationshipKind,
                                TargetType = Constants.ArtistRelationTarget,
                                Direction = "forward",
                                Artist = new ArtistDto
                                {
                                    Id = "e0000000-0000-0000-0000-00000000000c",
                                    Name = "Other Band"
                                }
                            }
                        ]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Title), Is.EqualTo(["Member"]));
    }

    [Test]
    public async Task ExecuteAsync_ReturnsTheRequestedPage_WhenTheScanPageNumberIsAbsurd()
    {
        // Arrange
        var plan = ScanPlan with { RequestedPage = int.MaxValue };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            { Outcome = FetchOutcome.Success, Value = new SearchPage { Count = 0, Offset = 0 } });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(int.MaxValue));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_StopsPaging_WhenTheCallerCancelsDuringArtistResolution()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = ArtistCompositionResolution.ExactNameSearch,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedName = "Miles Davis"
            }
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-cancelled",
            Root = resolver
        };
        _client.Setup(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cancellation.Cancel();
                return new FetchResult<ArtistResolutionResponse>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new ArtistResolutionResponse
                    {
                        Count = 2,
                        ReturnedCount = 1,
                        Candidates =
                        [
                            new ArtistResolutionCandidate
                            {
                                Mbid = "c0000000-0000-0000-0000-000000000001",
                                Name = "Miles Davis"
                            }
                        ]
                    }
                };
            });

        // Act
        // Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _subject.ExecuteAsync(plan, cancellation.Token).ConfigureAwait(false));

        _client.Verify(
            client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReportsTheRetainedWarningAgain_WhenTheCachedScanIsReused()
    {
        // Arrange
        var plan = ScanPlan with
        {
            Fingerprint = "fp-residual-warning-reuse",
            Root = ScanRoot with
            {
                Filters = [],
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.EntityGenres,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists = [new ArtistDto { Id = "90000000-0000-0000-0000-000000000001", Name = "Retained" }]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Unavailable });

        // Act
        var first = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);
        var second = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify genre criteria because entity hydration failed; " +
                        "affected candidates were retained."
                ]));
            Assert.That(
                second.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify genre criteria because entity hydration failed; " +
                        "affected candidates were retained."
                ]));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ResumesAtTheNextSurvivor_WhenTheResidualScanServesASecondPage()
    {
        // Arrange
        var plan = ScanPlan with
        {
            MaxResults = 2,
            Fingerprint = "fp-residual-paging",
            Root = ScanRoot with
            {
                Filters = [],
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.EntityGenres,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 8,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000001", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000002", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000003", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000004", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000005", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000006", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000007", Name = "Seed" },
                        new ArtistDto { Id = "a1000000-0000-0000-0000-000000000008", Name = "Seed" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityCacheKey key, Settings _, CancellationToken _) => new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = key.Mbid,
                        Genres =
                        [
                            new GenreDto
                            {
                                Name = key.Mbid.EndsWith("2", StringComparison.Ordinal) ||
                                    key.Mbid.EndsWith("4", StringComparison.Ordinal) ||
                                    key.Mbid.EndsWith("6", StringComparison.Ordinal) ||
                                    key.Mbid.EndsWith("8", StringComparison.Ordinal) ? "rock" : "jazz",
                                Count = 1
                            }
                        ]
                    }
                }
            });

        // Act
        var first = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);
        var second = await _subject.ExecuteAsync(plan with { RequestedPage = 2 }, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Hits.Select(hit => hit.PrimaryMbid),
                Is.EqualTo(
                [
                    "a1000000-0000-0000-0000-000000000002",
                    "a1000000-0000-0000-0000-000000000004"
                ]));
            Assert.That(
                second.Hits.Select(hit => hit.PrimaryMbid),
                Is.EqualTo(
                [
                    "a1000000-0000-0000-0000-000000000006",
                    "a1000000-0000-0000-0000-000000000008"
                ]));
        }

        _client.Verify(
            client => client.SearchAsync(It.IsAny<Request>(), It.IsAny<Settings>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("rock", 1)]
    [TestCase("jazz", 0)]
    public async Task ExecuteAsync_Result_WhenTheSearchHitAlreadyCarriesGenres(string genre, int expectedHits)
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-hit-genres",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.Hit,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Low",
                            Genres = [new GenreDto { Name = genre, Count = 1 }]
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RetainsTheCandidateWithAWarning_WhenTheSearchHitOmitsGenres()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-hit-genres-missing",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.Hit,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify genre criteria because search-hit data was omitted; " +
                        "affected candidates were retained."
                ]));
        }
    }

    [TestCase("rock", 1)]
    [TestCase("jazz", 0)]
    public async Task ExecuteAsync_Result_WhenGenresAreHydratedFromTheEntity(string genre, int expectedHits)
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-entity-genres",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.EntityGenres,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Genres = [new GenreDto { Name = genre, Count = 1 }]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Capabilities == LookupCapability.Genres),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RetainsTheCandidateWithAWarning_WhenEntityHydrationFails()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-entity-hydration-failure",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.EntityGenres,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify genre criteria because entity hydration failed; " +
                        "affected candidates were retained."
                ]));
        }
    }

    [TestCase("eng", 1)]
    [TestCase("deu", 0)]
    public async Task ExecuteAsync_Result_WhenTheRepresentativeReleaseCarriesTheLanguage(
        string language,
        int expectedHits)
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-representative-language",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Language,
                        Need = ResidualNeed.RepresentativeReleaseLanguage,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.Language,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["eng"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                TextRepresentation = new TextRepresentationDto { Language = language }
                            }
                        ]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Capabilities == LookupCapability.Genres),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReportsTheHydratedLanguage_WhenTheCandidateIsAccepted()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-hydrated-language",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Language,
                        Need = ResidualNeed.RepresentativeReleaseLanguage,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.Language,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["eng"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                TextRepresentation = new TextRepresentationDto { Language = "eng" }
                            }
                        ]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits[0].RepresentativeLanguage, Is.EqualTo("eng"));
    }

    [TestCase(600000, 1)]
    [TestCase(120000, 0)]
    public async Task ExecuteAsync_Result_WhenTheRepresentativeReleaseCarriesTheDuration(
        int durationMilliseconds,
        int expectedHits)
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-representative-duration",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Duration,
                        Need = ResidualNeed.RepresentativeReleaseDuration,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.Duration,
                                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                        DurationMilliseconds = [300000]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                Media = [new MediumDto { Tracks = [new TrackDto { Length = durationMilliseconds }] }]
                            }
                        ]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsTheCoverLookup_WhenAnEarlierResidualRejectsTheCandidate()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-cover-skipped",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.Genre,
                        Need = ResidualNeed.EntityGenres,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["rock"]
                                    }
                                ]
                            }
                        ]
                    },
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.ImagePresence,
                        Need = ResidualNeed.CoverArt,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.ImagePresence,
                                        Operator = CriteriaValueOperator.IsNotNull
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Low",
                            Genres = [new GenreDto { Name = "jazz", Count = 1 }]
                        }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Genres = [new GenreDto { Name = "jazz", Count = 1 }]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);

        _coverClient.Verify(
            client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheCandidate_WhenTheRequiredCoverExists()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-cover-present",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.ImagePresence,
                        Need = ResidualNeed.CoverArt,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.ImagePresence,
                                        Operator = CriteriaValueOperator.IsNotNull
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri>
            {
                Outcome = FetchOutcome.Success,
                Value = new Uri("https://coverartarchive.org/release-group/1/front-500.jpg")
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_DropsTheCandidate_WhenTheRequiredCoverIsMissing()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-cover-absent",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.ImagePresence,
                        Need = ResidualNeed.CoverArt,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.ImagePresence,
                                        Operator = CriteriaValueOperator.IsNotNull
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.NotFound });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetainsTheCandidateWithAWarning_WhenTheCoverLookupFails()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-cover-failure",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.ImagePresence,
                        Need = ResidualNeed.CoverArt,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.ImagePresence,
                                        Operator = CriteriaValueOperator.IsNotNull
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.Unavailable });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz could not verify image-presence criteria because cover-art lookup failed; " +
                        "affected candidates were retained."
                ]));
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesTheRepresentativeReleaseLabels_WhenTheHitOmitsThem()
    {
        // Arrange
        var plan = AlbumScanPlan with
        {
            Fingerprint = "fp-representative-labels",
            Root = AlbumScanRoot with
            {
                Residuals =
                [
                    new ResidualCriterion
                    {
                        Kind = ResidualKind.LabelRelationship,
                        Need = ResidualNeed.RepresentativeReleaseLabels,
                        Alternatives =
                        [
                            new ResidualAlternative
                            {
                                Predicates =
                                [
                                    new ResidualPredicate
                                    {
                                        Field = ResidualField.LabelSubject,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Blue Note"]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                LabelInfo =
                                [
                                    new LabelInfoDto
                                    {
                                        Label = new LabelDto
                                        {
                                            Id = "11111111-1111-4111-8111-111111111111",
                                            Name = "Blue Note"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Capabilities == LookupCapability.Releases),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_EscapesLuceneSyntax_WhenTheSeedUsesAnOrdinaryTerm()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Artist,
            Filters = [],
            Contributes = "ordinary",
            OrdinaryTerm = "AC/DC (live)+?"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 2,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-ordinary",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.EqualTo("AC\\/DC \\(live\\)\\+\\?"));
            Assert.That(captured.RelativeTarget, Does.Contain("dismax=true"));
            Assert.That(captured.RelativeTarget, Does.Contain("limit=25&offset=25"));
        }
    }

    [Test]
    public async Task ExecuteAsync_SearchesTitleAndAliases_WhenTheSeedCarriesAnOriginalTitleFilter()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Album,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.AlternateText,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "alias",
                    AlternateRemoteField = "releasegroupaccent",
                    StringValues = ["Abbey Road"],
                    RemoteApplied = true,
                    IncludePrimaryTitle = true
                }
            ],
            Contributes = "original-title"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-original-title",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo("(alias:\"Abbey Road\" OR releasegroupaccent:\"Abbey Road\")"));
    }

    [Test]
    public async Task ExecuteAsync_RequestsIdentifierPresence_WhenTheSeedCarriesAnExternalIdSourceFilter()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Album,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "releasegroupaccent",
                    StringValues = ["Nevermind"],
                    RemoteApplied = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Operator = CriteriaValueOperator.IsNotNull,
                    Negate = false,
                    IdentityValues =
                    [
                        new IdentityValue
                        {
                            Source = Constants.ProviderType,
                            Value = string.Empty,
                            RemoteField = "rgid"
                        }
                    ],
                    RemoteApplied = true
                }
            ],
            Contributes = "identifier-presence"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-identifier-presence",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo("releasegroupaccent:\"Nevermind\" AND rgid:[* TO *]"));
    }

    [Test]
    public async Task ExecuteAsync_GroupsMembershipDateAndNegation_WhenTheSeedCarriesAdvancedFilters()
    {
        // Arrange
        TemporalValue.TryParse("1990", out var year);
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Album,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.In,
                    Negate = false,
                    RemoteField = "releasegroupaccent",
                    StringValues = ["Blue", "Green"],
                    RemoteApplied = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "firstreleasedate",
                    TemporalValues = [year!],
                    RemoteApplied = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Genre,
                    Operator = CriteriaValueOperator.In,
                    Negate = true,
                    RemoteField = "tag",
                    StringValues = ["bootleg"],
                    RemoteApplied = true
                }
            ],
            Contributes = "advanced"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 100,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-advanced",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Does.Contain("(releasegroupaccent:\"Blue\" OR releasegroupaccent:\"Green\")"));
            Assert.That(query, Does.Contain("firstreleasedate:[1990-01-01 TO 1990-12-31]"));
            Assert.That(query, Does.Contain("NOT (tag:\"bootleg\")"));
            Assert.That(captured.RelativeTarget, Does.Contain("dismax=false"));
        }
    }

    [TestCase(CriteriaValueOperator.Equals, false, "firstreleasedate:[1975-11-21 TO 1975-11-21]")]
    [TestCase(CriteriaValueOperator.In, false, "firstreleasedate:[1975-11-21 TO 1975-11-21]")]
    [TestCase(CriteriaValueOperator.GreaterThan, false, "firstreleasedate:[1975-11-22 TO *]")]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, false, "firstreleasedate:[1975-11-21 TO *]")]
    [TestCase(CriteriaValueOperator.LessThan, false, "firstreleasedate:[* TO 1975-11-21]")]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, false, "firstreleasedate:[* TO 1975-11-21]")]
    [TestCase(CriteriaValueOperator.IsNull, false, "NOT firstreleasedate:[* TO *]")]
    [TestCase(CriteriaValueOperator.IsNotNull, false, "firstreleasedate:[* TO *]")]
    [TestCase(CriteriaValueOperator.Equals, true, "NOT (firstreleasedate:[1975-11-21 TO 1975-11-21])")]
    public async Task ExecuteAsync_Result_WhenTheSeedFiltersOnTrackPublishDate(
        CriteriaValueOperator valueOperator,
        bool negate,
        string expected)
    {
        // Arrange
        TemporalValue.TryParse("1975-11-21", out var date);
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Track,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    Operator = valueOperator,
                    Negate = negate,
                    RemoteField = "firstreleasedate",
                    TemporalValues = [date!],
                    RemoteApplied = true
                }
            ],
            Contributes = "track-date"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-track-date",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo(expected));
    }

    [TestCase(CriteriaValueOperator.Equals, 355106, "dur:355106")]
    [TestCase(CriteriaValueOperator.In, 355106, "dur:355106")]
    [TestCase(CriteriaValueOperator.GreaterThan, 355106, "dur:[355107 TO *]")]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, 355106, "dur:[355106 TO *]")]
    [TestCase(CriteriaValueOperator.LessThan, 355106, "dur:[* TO 355105]")]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, 355106, "dur:[* TO 355106]")]
    [TestCase(CriteriaValueOperator.IsNull, 355106, "NOT dur:[* TO *]")]
    [TestCase(CriteriaValueOperator.IsNotNull, 355106, "dur:[* TO *]")]
    [TestCase(CriteriaValueOperator.GreaterThan, 0, "dur:[1 TO *]")]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, 0, "dur:[0 TO *]")]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, 0, "dur:[* TO 0]")]
    public async Task ExecuteAsync_Result_WhenTheSeedFiltersOnTrackDuration(
        CriteriaValueOperator valueOperator,
        int milliseconds,
        string expected)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Track,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Duration,
                    Operator = valueOperator,
                    Negate = false,
                    RemoteField = "dur",
                    DurationMilliseconds = [milliseconds],
                    RemoteApplied = true
                }
            ],
            Contributes = "track-duration"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-track-duration",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo(expected));
    }

    [TestCase(
        CriteriaValueOperator.ContainsString,
        "will rock",
        "(recordingaccent:\"will rock\" OR (recordingaccent:*will* AND recordingaccent:*rock*))")]
    [TestCase(CriteriaValueOperator.StartsWithString, "Smells Like", "recordingaccent:Smells*")]
    [TestCase(CriteriaValueOperator.EndsWithString, "Teen Spirit", "recordingaccent:*Spirit")]
    public async Task ExecuteAsync_Result_WhenTheSeedFiltersOnTrackTitleText(
        CriteriaValueOperator valueOperator,
        string value,
        string expected)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Track,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = valueOperator,
                    Negate = false,
                    RemoteField = "recordingaccent",
                    StringValues = [value],
                    RemoteApplied = true
                }
            ],
            Contributes = "track-title"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 100,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-track-title",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteAsync_UsesDocumentedFields_WhenTheSeedFiltersOnIdentityAndArtistRelationship()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Track,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Operator = CriteriaValueOperator.In,
                    Negate = false,
                    IdentityValues =
                    [
                        new IdentityValue { Source = "isrc", Value = "USRC17607839", RemoteField = "isrc" },
                        new IdentityValue
                        {
                            Source = "musicbrainz",
                            Value = "550e8400-e29b-41d4-a716-446655440000",
                            RemoteField = "tid"
                        }
                    ],
                    RemoteApplied = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.ArtistRelationship,
                    Operator = CriteriaValueOperator.In,
                    Negate = false,
                    RelationshipValues =
                    [
                        new ArtistRelationshipValue
                        {
                            ArtistMbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                            ArtistName = "Queen"
                        }
                    ],
                    RemoteApplied = true
                }
            ],
            Contributes = "fields"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 100,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-fields",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Does.Contain("isrc:\"USRC17607839\""));
            Assert.That(query, Does.Contain("tid:\"550e8400\\-e29b\\-41d4\\-a716\\-446655440000\""));
            Assert.That(
                query,
                Does.Contain("(arid:\"0383dadf\\-2a4e\\-4d10\\-a46a\\-e9e041da8eb3\" AND artistname:\"Queen\")"));
        }
    }

    [TestCase(CriteriaValueOperator.IsNotNull, "(end:[* TO *] OR ended:true)")]
    [TestCase(CriteriaValueOperator.IsNull, "NOT (end:[* TO *] OR ended:true)")]
    public async Task ExecuteAsync_Result_WhenTheSeedFiltersOnDissolvedExistence(
        CriteriaValueOperator valueOperator,
        string expected)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Artist,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Dissolved,
                    Operator = valueOperator,
                    Negate = false,
                    RemoteField = "end",
                    RemoteApplied = true
                }
            ],
            Contributes = "dissolved"
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-dissolved",
            Root = fetch
        };
        Request? captured = null;
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        Assert.That(query, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteAsync_SeedsTheReleaseIndex_WhenTheScanCarriesLabelAndLanguageFilters()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Album,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "releasegroupaccent",
                    StringValues = ["Blue Train"],
                    RemoteApplied = true,
                    VerifyLocally = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.LabelRelationship,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    LabelValues =
                    [
                        new LabelRelationshipValue
                        {
                            LabelMbid = "44444444-4444-4444-8444-444444444444",
                            LabelName = "Blue Note"
                        }
                    ],
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "lang",
                    StringValues = ["eng"],
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                }
            ],
            Contributes = "label",
            UsesReleaseIndexComposition = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-release-index-seed",
            Root = root
        };
        var requests = new List<Request>();
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var seed = requests.Single(request => request.RelativeTarget.StartsWith("release?", StringComparison.Ordinal));
        var seedQuery = Uri.UnescapeDataString(
            seed.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seed.ReleaseIndex, Is.True);
            Assert.That(seedQuery, Does.Contain("laid:\"44444444\\-4444\\-4444\\-8444\\-444444444444\""));
            Assert.That(seedQuery, Does.Contain("label:\"Blue Note\""));
            Assert.That(seedQuery, Does.Contain("lang:\"eng\""));
            Assert.That(seedQuery, Does.Contain("releaseaccent:\"Blue Train\""));
            Assert.That(seedQuery, Does.Not.Contain("releasegroupaccent"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ConstrainsReleaseGroups_WhenTheReleaseIndexSeedBatchResolves()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Target = QueryTarget.Album,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "releasegroupaccent",
                    StringValues = ["Blue Train"],
                    RemoteApplied = true,
                    VerifyLocally = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.LabelRelationship,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    LabelValues = [new LabelRelationshipValue { LabelName = "Blue Note" }],
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "lang",
                    StringValues = ["eng"],
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                }
            ],
            Contributes = "label",
            UsesReleaseIndexComposition = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = []
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-release-index-composed",
            Root = root
        };
        var requests = new List<Request>();
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith("release?", StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    Releases =
                    [
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000001",
                            Title = "Blue Train",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                Title = "Blue Train"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" }
                        },
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000002",
                            Title = "Blue Train",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "22222222-2222-4222-8222-222222222222",
                                Title = "Blue Train"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" }
                        }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith(
                    "release-group?",
                    StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var composed = requests.Single(request => request.RelativeTarget.StartsWith(
            "release-group?",
            StringComparison.Ordinal));
        var composedQuery = Uri.UnescapeDataString(
            composed.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composed.ReleaseIndex, Is.False);
            Assert.That(
                composedQuery,
                Does.Contain(
                    "(rgid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"" +
                    " OR rgid:\"22222222\\-2222\\-4222\\-8222\\-222222222222\")"));
            Assert.That(composedQuery, Does.Contain("releasegroupaccent:\"Blue Train\""));
            Assert.That(composedQuery, Does.Not.Contain("label:"));
            Assert.That(composedQuery, Does.Not.Contain("lang:"));
            Assert.That(composed.Limit, Is.EqualTo(2));
            Assert.That(composed.Fingerprint, Is.Not.EqualTo("fp-release-index-composed"));
        }
    }

    [TestCase((int)ArtistCompositionEntity.Artist, "artist", "artistaccent:\"AC\\/DC\"")]
    [TestCase((int)ArtistCompositionEntity.ReleaseGroup, "release-group", "releasegroupaccent:\"AC\\/DC\"")]
    [TestCase((int)ArtistCompositionEntity.Release, "release", "releaseaccent:\"AC\\/DC\"")]
    [TestCase((int)ArtistCompositionEntity.Recording, "recording", "recordingaccent:\"AC\\/DC\"")]
    [TestCase((int)ArtistCompositionEntity.Work, "work", "workaccent:\"AC\\/DC\"")]
    public async Task ExecuteAsync_Result_WhenTheCompositionResolvesARelatedNameByEntity(
        int entityValue,
        string endpoint,
        string expectedQuery)
    {
        // Arrange
        var entity = (ArtistCompositionEntity)entityValue;
        var resolver = new ResolvePlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = entity,
                Resolution = ArtistCompositionResolution.ExactNameSearch,
                Expansion = entity == ArtistCompositionEntity.Artist
                    ? ArtistCompositionExpansion.BandMembers
                    : ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
                ExternalIdSource = Constants.ProviderType,
                RelatedName = "AC/DC"
            }
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-by-name",
            Root = fetch
        };
        ArtistResolutionRequest? captured = null;
        _client.Setup(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((ArtistResolutionRequest request, Settings _, CancellationToken _) => captured = request)
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistResolutionResponse { Count = 0, ReturnedCount = 0, Candidates = [] }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            captured!.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured.RelativeTarget, Does.StartWith(endpoint + "?"));
            Assert.That(captured.RelativeTarget, Does.Contain("limit=100"));
            Assert.That(query, Is.EqualTo(expectedQuery));
        }
    }

    [TestCase(
        (int)ArtistCompositionEntity.Artist,
        "artist",
        "arid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"")]
    [TestCase(
        (int)ArtistCompositionEntity.ReleaseGroup,
        "release-group",
        "rgid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"")]
    [TestCase(
        (int)ArtistCompositionEntity.Release,
        "release",
        "reid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"")]
    [TestCase(
        (int)ArtistCompositionEntity.Recording,
        "recording",
        "rid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"")]
    [TestCase(
        (int)ArtistCompositionEntity.Work,
        "work",
        "wid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\"")]
    public async Task ExecuteAsync_Result_WhenTheCompositionResolvesARelatedMbidByEntity(
        int entityValue,
        string endpoint,
        string expectedQuery)
    {
        // Arrange
        var entity = (ArtistCompositionEntity)entityValue;
        var composition = new ArtistComposition
        {
            RelatedEntity = entity,
            Resolution = ArtistCompositionResolution.ExactNameSearch,
            Expansion = entity == ArtistCompositionEntity.Artist
                ? ArtistCompositionExpansion.BandMembers
                : ArtistCompositionExpansion.BrowseArtists,
            Constraint = ArtistCompositionConstraint.Include,
            FinalStage = ArtistCompositionFinalStage.ReturnExpandedCandidates,
            ExternalIdSource = Constants.ProviderType,
            RelatedMbid = "11111111-1111-4111-8111-111111111111",
            RelatedName = "Duplicate Name"
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "content linked to resolved artists",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ResolvedArtist,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers =
            [
                new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = NoPrecondition,
                    Contributes = "resolved artist ids",
                    Composition = composition
                }
            ]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-resolution-by-mbid",
            Root = fetch
        };
        var otherFetch = fetch with
        {
            Resolvers =
            [
                new ResolvePlanNode
                {
                    Id = 1,
                    Precondition = NoPrecondition,
                    Contributes = "resolved artist ids",
                    Composition = composition with { RelatedMbid = "22222222-2222-4222-8222-222222222222" }
                }
            ]
        };
        var requests = new List<ArtistResolutionRequest>();
        _client.Setup(client => client.ResolveArtistCompositionAsync(
                It.IsAny<ArtistResolutionRequest>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((ArtistResolutionRequest request, Settings _, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(new FetchResult<ArtistResolutionResponse>
            {
                Outcome = FetchOutcome.Success,
                Value = new ArtistResolutionResponse { Count = 0, ReturnedCount = 0, Candidates = [] }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);
        await _subject.ExecuteAsync(plan with { Root = otherFetch }, default).ConfigureAwait(false);

        // Assert
        var query = Uri.UnescapeDataString(
            requests[0].RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests[0].RelativeTarget, Does.StartWith(endpoint + "?"));
            Assert.That(query, Is.EqualTo(expectedQuery));
            Assert.That(requests[1].Fingerprint, Is.Not.EqualTo(requests[0].Fingerprint));
        }
    }

    [Test]
    public async Task ExecuteAsync_ComposesIncludedAndExcludedArtists_WhenTheScanIsAnchoredAndHasExclusions()
    {
        // Arrange
        var anchor = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "anchor artist rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Included"
        };
        var exclusion = new FetchPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "excluded artist rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Excluded"
        };
        var fetch = new FetchPlanNode
        {
            Id = 3,
            Precondition = NoPrecondition,
            Contributes = "artist-composition",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    RemoteField = "artistaccent",
                    StringValues = ["Queen"],
                    RemoteApplied = true,
                    VerifyLocally = true
                }
            ],
            AnchorSource = anchor
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [],
            Residuals = [],
            Exclusions = [exclusion]
        };
        var plan = new QueryPlan
        {
            RequestedPage = 1,
            MaxResults = 25,
            Settings = new Settings(),
            Warnings = [],
            Fingerprint = "fp-artist-composition",
            Root = root
        };
        var requests = new List<Request>();
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.Contains("Included")),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "11111111-1111-4111-8111-111111111111", Name = "Queen" }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.Contains("Excluded")),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "22222222-2222-4222-8222-222222222222", Name = "Not Queen" }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.Contains("arid")),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .Callback((Request request, Settings _, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage { Count = 0, Offset = 0 }
            });

        // Act
        await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        var composed = requests[0];
        var query = Uri.UnescapeDataString(
            composed.RelativeTarget.Split(["query="], StringSplitOptions.None)[1].Split('&')[0]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composed.RelativeTarget, Does.StartWith("artist?"));
            Assert.That(query, Does.Contain("arid:\"11111111\\-1111\\-4111\\-8111\\-111111111111\""));
            Assert.That(query, Does.Contain("NOT (arid:\"22222222\\-2222\\-4222\\-8222\\-222222222222\")"));
            Assert.That(query, Does.Contain("artistaccent:\"Queen\""));
            Assert.That(composed.Fingerprint, Does.Contain("artist-composition"));
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheFirstValidRow_WhenTheSeedPageRepeatsAndMalformsIdentifiers()
    {
        // Arrange
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 3,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto { Id = "invalid", Name = "Invalid" },
                        new ArtistDto { Id = "550e8400-e29b-41d4-a716-446655440000", Name = "First" },
                        new ArtistDto { Id = "550e8400-e29b-41d4-a716-446655440000", Name = "Duplicate" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(
            AlbumScanPlan with { Fingerprint = "fp-identity", Root = ScanFetch },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Title, Is.EqualTo("First"));
        }
    }

    [TestCase(CriteriaValueOperator.IsNotNull, true, 1)]
    [TestCase(CriteriaValueOperator.IsNotNull, false, 0)]
    [TestCase(CriteriaValueOperator.IsNull, true, 0)]
    [TestCase(CriteriaValueOperator.IsNull, false, 1)]
    public async Task ExecuteAsync_Result_WhenTheScanVerifiesDissolvedExistenceLocally(
        CriteriaValueOperator valueOperator,
        bool ended,
        int expectedHits)
    {
        // Arrange
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = ScanFetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Dissolved,
                    Operator = valueOperator,
                    Negate = false,
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = ScanPlan with { Fingerprint = "fp-local-dissolved", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto
                        {
                            Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                            Name = "Queen",
                            LifeSpan = new LifeSpanDto { Ended = ended }
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
    }

    [TestCase(CriteriaValueOperator.Equals, "Björk", "Björk", 1)]
    [TestCase(CriteriaValueOperator.Equals, "Bjork", "Björk", 0)]
    [TestCase(CriteriaValueOperator.ContainsString, "will rock", "We Will Rock You", 1)]
    [TestCase(CriteriaValueOperator.ContainsWord, "ill", "We Will Rock You", 0)]
    [TestCase(CriteriaValueOperator.StartsWithString, "we will", "We Will Rock You", 1)]
    [TestCase(CriteriaValueOperator.EndsWithString, "you", "We Will Rock You", 1)]
    public async Task ExecuteAsync_Result_WhenTheScanVerifiesTrackTitleTextLocally(
        CriteriaValueOperator valueOperator,
        string value,
        string title,
        int expectedHits)
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Wanted"
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = valueOperator,
                    Negate = false,
                    StringValues = [value],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = ScanPlan with { Fingerprint = "fp-local-track-title", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = title }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheAlbumRow_WhenTheScanVerifiesAPartialPublishDateLocally()
    {
        // Arrange
        TemporalValue.TryParse("1998", out var requested);
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = AlbumScanFetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    TemporalValues = [requested!],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = AlbumScanPlan with { Fingerprint = "fp-local-publish-date", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Homogenic",
                            FirstReleaseDate = "1998-06-15"
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheCreditedRow_WhenTheScanVerifiesAnArtistRelationshipLocally()
    {
        // Arrange
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = AlbumScanFetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ArtistRelationship,
                    Operator = CriteriaValueOperator.In,
                    Negate = false,
                    RelationshipValues =
                    [
                        new ArtistRelationshipValue
                        {
                            ArtistMbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                            ArtistName = "Queen"
                        }
                    ],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = AlbumScanPlan with { Fingerprint = "fp-local-artist-relationship", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Under Pressure",
                            ArtistCredit =
                            [
                                new ArtistCreditDto
                                {
                                    Name = "Queen + David Bowie",
                                    Artist = new ArtistDto
                                    {
                                        Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                                        Name = "Queen"
                                    }
                                }
                            ]
                        },
                        new ReleaseGroupDto
                        {
                            Id = "11111111-1111-4111-8111-111111111111",
                            Title = "Impostor",
                            ArtistCredit =
                            [
                                new ArtistCreditDto
                                {
                                    Name = "Queen",
                                    Artist = new ArtistDto
                                    {
                                        Id = "22222222-2222-4222-8222-222222222222",
                                        Name = "Not Queen"
                                    }
                                }
                            ]
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("550e8400-e29b-41d4-a716-446655440000"));
        }
    }

    [Test]
    public async Task ExecuteAsync_DropsTheTrackRow_WhenTheScanVerifiesAnOrderedDurationWithSeveralValues()
    {
        // Arrange
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Track,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.UnfieldedText,
            CombineMode = CombineMode.And,
            Filters = [],
            OrdinaryTerm = "Wanted"
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Duration,
                    Operator = CriteriaValueOperator.GreaterThan,
                    Negate = false,
                    DurationMilliseconds = [240000, 120000],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = ScanPlan with { Fingerprint = "fp-local-duration", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Recordings =
                    [
                        new RecordingDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Three Minutes",
                            Length = 180000
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [TestCase(false, "550e8400-e29b-41d4-a716-446655440000", "22222222-2222-4222-8222-222222222222")]
    [TestCase(true, "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222")]
    public async Task ExecuteAsync_Result_WhenTheScanVerifiesAlternateTextLocally(
        bool negate,
        string firstExpectedMbid,
        string secondExpectedMbid)
    {
        // Arrange
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = ScanFetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.AlternateText,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = negate,
                    StringValues = ["Ziggy Stardust"],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = ScanPlan with { Fingerprint = "fp-local-alternate-text", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 3,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Name = "David Bowie",
                            Aliases = [new AliasDto { Name = "Ziggy Stardust" }]
                        },
                        new ArtistDto
                        {
                            Id = "11111111-1111-4111-8111-111111111111",
                            Name = "David Bowie",
                            Aliases = [new AliasDto { Name = "Thin White Duke" }]
                        },
                        new ArtistDto { Id = "22222222-2222-4222-8222-222222222222", Name = "David Bowie" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Hits.Select(hit => hit.PrimaryMbid),
            Is.EqualTo([firstExpectedMbid, secondExpectedMbid]));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheArtistRow_WhenTheScanVerifiesBirthAndDeathDatesLocally()
    {
        // Arrange
        TemporalValue.TryParse("1947-01-08", out var birthDate);
        TemporalValue.TryParse("2016-01-10", out var deathDate);
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = ScanFetch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.BirthDate,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    TemporalValues = [birthDate!],
                    VerifyLocally = true
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.DeathDate,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    TemporalValues = [deathDate!],
                    VerifyLocally = true
                }
            ],
            Residuals = []
        };
        var plan = ScanPlan with { Fingerprint = "fp-local-life-span", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.IsAny<Request>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 1,
                    Offset = 0,
                    Artists =
                    [
                        new ArtistDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Name = "David Bowie",
                            LifeSpan = new LifeSpanDto { Begin = "1947-01-08", End = "2016-01-10", Ended = true }
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheLabelledRow_WhenTheScanVerifiesALabelMbidAgainstReleaseIndexData()
    {
        // Arrange
        var labelFilter = new QueryFilter
        {
            Kind = QueryFilterKind.LabelRelationship,
            Operator = CriteriaValueOperator.In,
            Negate = false,
            LabelValues =
            [
                new LabelRelationshipValue
                {
                    LabelMbid = "44444444-4444-4444-8444-444444444444",
                    LabelName = "Blue Note"
                }
            ],
            RemoteApplied = true,
            ReleaseIndexApplied = true,
            VerifyLocally = true
        };
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Album,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Filters = [labelFilter],
            UsesReleaseIndexComposition = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [labelFilter],
            Residuals = []
        };
        var plan = AlbumScanPlan with { Fingerprint = "fp-local-label", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith("release?", StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    Releases =
                    [
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000001",
                            Title = "Blue Train",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "550e8400-e29b-41d4-a716-446655440000",
                                Title = "Blue Train"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" },
                            LabelInfo =
                            [
                                new LabelInfoDto
                                {
                                    Label = new LabelDto
                                    {
                                        Id = "44444444-4444-4444-8444-444444444444",
                                        Name = "Blue Note"
                                    }
                                }
                            ]
                        },
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000002",
                            Title = "Blue Trane",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                Title = "Blue Trane"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" },
                            LabelInfo =
                            [
                                new LabelInfoDto
                                {
                                    Label = new LabelDto
                                    {
                                        Id = "99999999-9999-4999-8999-999999999999",
                                        Name = "Blue Note"
                                    }
                                }
                            ]
                        }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith(
                    "release-group?",
                    StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto
                        {
                            Id = "550e8400-e29b-41d4-a716-446655440000",
                            Title = "Blue Train"
                        },
                        new ReleaseGroupDto
                        {
                            Id = "11111111-1111-4111-8111-111111111111",
                            Title = "Blue Trane"
                        }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].PrimaryMbid, Is.EqualTo("550e8400-e29b-41d4-a716-446655440000"));
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheEnglishRow_WhenTheScanVerifiesALanguageAgainstReleaseIndexData()
    {
        // Arrange
        var languageFilter = new QueryFilter
        {
            Kind = QueryFilterKind.Language,
            Operator = CriteriaValueOperator.Equals,
            Negate = false,
            RemoteField = "lang",
            StringValues = ["eng"],
            RemoteApplied = true,
            ReleaseIndexApplied = true,
            VerifyLocally = true
        };
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Precondition = NoPrecondition,
            Contributes = "candidate rows",
            Target = QueryTarget.Album,
            AccessMode = AccessMode.Search,
            AnchorKind = AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Filters = [languageFilter],
            UsesReleaseIndexComposition = true
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Precondition = NoPrecondition,
            Contributes = "post-residual survivor rows",
            Child = fetch,
            Filters = [languageFilter],
            Residuals = []
        };
        var plan = AlbumScanPlan with { Fingerprint = "fp-local-language", Root = root };
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith("release?", StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    Releases =
                    [
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000001",
                            Title = "Low",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "550e8400-e29b-41d4-a716-446655440000",
                                Title = "Low"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "eng" }
                        },
                        new ReleaseDto
                        {
                            Id = "c0000000-0000-0000-0000-000000000002",
                            Title = "Tief",
                            ReleaseGroup = new ReleaseGroupDto
                            {
                                Id = "11111111-1111-4111-8111-111111111111",
                                Title = "Tief"
                            },
                            TextRepresentation = new TextRepresentationDto { Language = "deu" }
                        }
                    ]
                }
            });
        _client.Setup(client => client.SearchAsync(
                It.Is<Request>(request => request.RelativeTarget.StartsWith(
                    "release-group?",
                    StringComparison.Ordinal)),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<SearchPage>
            {
                Outcome = FetchOutcome.Success,
                Value = new SearchPage
                {
                    Count = 2,
                    Offset = 0,
                    ReleaseGroups =
                    [
                        new ReleaseGroupDto { Id = "550e8400-e29b-41d4-a716-446655440000", Title = "Low" },
                        new ReleaseGroupDto { Id = "11111111-1111-4111-8111-111111111111", Title = "Tief" }
                    ]
                }
            });

        // Act
        var result = await _subject.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Hits.Select(hit => hit.PrimaryMbid),
            Is.EqualTo(["550e8400-e29b-41d4-a716-446655440000"]));
    }
}
