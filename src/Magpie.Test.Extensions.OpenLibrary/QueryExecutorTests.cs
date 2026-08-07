using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class QueryExecutorTests
{
    private Mock<IOpenLibraryClient> _client = null!;
    private Mock<IQueryCache> _cache = null!;
    private IQueryExecutor _subject = null!;
    private IQueryExecutor _requestSubject = null!;
    private RequestContext _context = null!;
    private ApiRequest _request = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IOpenLibraryClient>(MockBehavior.Strict);
        _cache = new Mock<IQueryCache>(MockBehavior.Strict);
        _context = new RequestContext { UserAgent = "test" };
        _request = null!;
        var settingsResolver = new Mock<ISettingsResolver>(MockBehavior.Strict);
        settingsResolver
            .Setup(value => value.GetRequestContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_context);
        _cache
            .Setup(value => value.GetSourcePageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Func<CancellationToken, Task<SourcePage>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, bool _, Func<CancellationToken, Task<SourcePage>> factory, CancellationToken token) =>
                factory(token));
        _cache
            .Setup(value => value.GetResolutionAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<WorkResolution>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<WorkResolution>> factory, CancellationToken token) =>
                factory(token));
        _cache
            .Setup(value => value.GetAuthorAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<AuthorLookup>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<AuthorLookup>> factory, CancellationToken token) =>
                factory(token));
        _subject = new QueryExecutor(
            _client.Object,
            _cache.Object,
            settingsResolver.Object,
            NullLogger<QueryExecutor>.Instance);
        var requestClient = new Mock<IOpenLibraryClient>(MockBehavior.Strict);
        requestClient
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                _context,
                It.IsAny<CancellationToken>()))
            .Callback<ApiRequest, RequestContext, CancellationToken>((request, _, _) => _request = request)
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 0,
                NumFoundExact = true,
                Start = 0,
                Docs = []
            });
        _requestSubject = new QueryExecutor(
            requestClient.Object,
            _cache.Object,
            settingsResolver.Object,
            NullLogger<QueryExecutor>.Instance);
    }

    [Test]
    public async Task ExecuteSearchAsync_Result_WhenResidualSurvivorsExceedMaximum()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound": 5,
                  "numFoundExact": true,
                  "start": 0,
                  "docs": [
                    {"key":"/works/OL1W","title":"Drop One"},
                    {"key":"/works/OL2W","title":"Keep One"},
                    {"key":"/works/OL3W","title":"Drop Two"},
                    {"key":"/works/OL4W","title":"Keep Two"},
                    {"key":"/works/OL5W","title":"Keep Three"}
                  ]
                }
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Title,
                        Operator = CriteriaValueOperator.StartsWithString,
                        Values = ["Keep"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                        PagingMode = FetchPagingMode.SurvivorScan
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 2
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL2W", "OL4W"]));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(1));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteSearchAsync_Result_WhenResidualSurvivorsRequireAnotherSourceBatch()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request => request.Parameters.Any(parameter =>
                    parameter.Key == "offset" && parameter.Value == "0")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound": 103,
                  "numFoundExact": true,
                  "start": 0,
                  "docs": [
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},
                    {"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"},{"key":"/works/OL0W","title":"Drop"}
                  ]
                }
                """));
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request => request.Parameters.Any(parameter =>
                    parameter.Key == "offset" && parameter.Value == "100")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound": 103,
                  "numFoundExact": true,
                  "start": 100,
                  "docs": [
                    {"key":"/works/OL1W","title":"Keep One"},
                    {"key":"/works/OL2W","title":"Keep Two"},
                    {"key":"/works/OL3W","title":"Keep Three"}
                  ]
                }
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Title,
                        Operator = CriteriaValueOperator.StartsWithString,
                        Values = ["Keep"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                        PagingMode = FetchPagingMode.SurvivorScan
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 2
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W", "OL2W"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteSearchAsync_Result_WhenAuthorDateResidualIsVerifiedByHydration()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound":1,
                  "numFoundExact":true,
                  "start":0,
                  "docs":[
                    {"key":"/works/OL1W","title":"Book","author_key":["OL1A"],"author_name":["Author"]}
                  ]
                }
                """));
        _client
            .Setup(value => value.GetJsonAsync<AuthorDto>(
                It.IsAny<ApiRequest>(),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<AuthorDto>(
                """
                {"key":"/authors/OL1A","name":"Author","birth_date":"January 1, 1950"}
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Residuals =
                [
                    new AuthorDateResidual
                    {
                        Field = AuthorDateField.BirthDate,
                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                        TemporalValues =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(1950, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Day
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                        PagingMode = FetchPagingMode.SurvivorScan
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 10
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            value => value.GetJsonAsync<AuthorDto>(It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteSearchAsync_Result_WhenOrdinaryPageRequiresLogicalSkip()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request =>
                    request.Parameters.Any(parameter => parameter.Key == "limit" && parameter.Value == "20") &&
                    request.Parameters.Any(parameter => parameter.Key == "offset" && parameter.Value == "20")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound": 22,
                  "numFoundExact": true,
                  "start": 20,
                  "docs": [
                    {"key":"/works/OL21W","title":"Book 21"},
                    {"key":"/works/OL22W","title":"Book 22"}
                  ]
                }
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Page = 2
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL21W", "OL22W"]));
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(2));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_Result_WhenAdvancedDirectSearchProbesOneExtraRow()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request =>
                    request.Parameters.Any(parameter => parameter.Key == "limit" && parameter.Value == "8") &&
                    request.Parameters.Any(parameter => parameter.Key == "offset" && parameter.Value == "0")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {"numFound":1,"numFoundExact":true,"start":0,"docs":[{"key":"/works/OL1W","title":"Book"}]}
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                PagingMode = FetchPagingMode.Direct
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 7
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_EmptyWithoutRequest_WhenOrdinaryPageOffsetExceedsApiRange()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                PagingMode = FetchPagingMode.Direct
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Page = int.MaxValue
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(int.MaxValue));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(
                result.Warnings,
                Does.Contain("Open Library cannot represent the requested page offset. No search was run."));
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteSearchAsync_ResultWithoutHugeRequest_WhenMaximumResultsIsIntMaxValue()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request =>
                    request.Parameters.Any(parameter => parameter.Key == "limit" && parameter.Value == "100") &&
                    request.Parameters.Any(parameter => parameter.Key == "offset" && parameter.Value == "0")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {"numFound":1,"numFoundExact":true,"start":0,"docs":[{"key":"/works/OL1W","title":"Book"}]}
                """));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                PagingMode = FetchPagingMode.Direct
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = int.MaxValue
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [TestCase(0)]
    [TestCase(-2)]
    public async Task ExecuteSearchAsync_EmptyWithoutRequest_WhenMaximumResultsIsNotPositive(int maximumResults)
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = maximumResults
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task ResolveWorkAsync_Result_WhenIsbnSearchReturnsWorkCandidate()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request => request.Parameters.Any(parameter =>
                    parameter.Key == "limit" && parameter.Value == "5")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {
                  "numFound": 1,
                  "numFoundExact": true,
                  "start": 0,
                  "docs": [
                    {
                      "key": "/works/OL1W",
                      "title": "Book",
                      "isbn": ["9780143111597"]
                    }
                  ]
                }
                """));
        Task<WorkResolution>? cachedResolution = null;
        _cache
            .Setup(value => value.GetResolutionAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<WorkResolution>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<WorkResolution>> factory, CancellationToken token) =>
                cachedResolution ??= factory(token));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "isbn", Values = ["9780143111597"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            ResolutionSource = Constants.IsbnSource,
            ResolutionValue = "9780143111597"
        };

        // Act
        var result = await _subject.ResolveWorkAsync(plan, default).ConfigureAwait(false);
        var cached = await _subject.ResolveWorkAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Kind, Is.EqualTo(WorkResolutionKind.Resolved));
            Assert.That(result.Hit?.WorkOlid, Is.EqualTo("OL1W"));
            Assert.That(cached.Kind, Is.EqualTo(WorkResolutionKind.Resolved));
            Assert.That(cached.Hit?.WorkOlid, Is.EqualTo("OL1W"));
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteSearchAsync_ExactCache_WhenOrdinaryPageIsRequestedTwice()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.Is<ApiRequest>(request =>
                    request.Parameters.Any(parameter => parameter.Key == "limit" && parameter.Value == "20") &&
                    request.Parameters.Any(parameter => parameter.Key == "offset" && parameter.Value == "0")),
                _context,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<SearchResponseDto>(
                """
                {"numFound":1,"numFoundExact":true,"start":0,"docs":[{"key":"/works/OL1W","title":"Book"}]}
                """));
        Task<SourcePage>? cachedPage = null;
        _cache
            .Setup(value => value.GetSourcePageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Func<CancellationToken, Task<SourcePage>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, bool _, Func<CancellationToken, Task<SourcePage>> factory, CancellationToken token) =>
                cachedPage ??= factory(token));
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }],
                PagingMode = FetchPagingMode.Direct
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            }
        };

        // Act
        var first = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);
        var second = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
            Assert.That(second.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
        }

        _client.Verify(
            value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(), _context, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteSearchAsync_Throws_WhenCancellationIsRequested()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Book"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            }
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var task = _subject.ExecuteSearchAsync(plan, cancellation.Token);

        // Assert
        Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenTitleEqualityNormalizesUnicodeAndCase()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "ＣＡＦÉ",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Title,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["café"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["cafe"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [TestCase(CriteriaValueOperator.ContainsString, "obb", true)]
    [TestCase(CriteriaValueOperator.ContainsWord, "hob", false)]
    [TestCase(CriteriaValueOperator.ContainsWord, "hobbit", true)]
    [TestCase(CriteriaValueOperator.StartsWithString, "the ho", true)]
    [TestCase(CriteriaValueOperator.EndsWithString, "bbit", true)]
    public async Task ExecuteSearchAsync_AppliesExactLocalSemantics_WhenTitleOperatorVaries(
        CriteriaValueOperator op,
        string text,
        bool expectedMatch)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "The Hobbit",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["J. R. R. Tolkien"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Title,
                        Operator = op,
                        Values = [text]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["hobbit"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, expectedMatch ? Is.Not.Empty : Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_DropsHit_WhenStrictYearBoundaryIsNotExceeded()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "The Hobbit",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        FirstPublishYear = 1937
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.PublishDate,
                        Operator = CriteriaValueOperator.GreaterThan,
                        TemporalValues =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(1937, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["hobbit"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenLanguageMatchesAnyEditionAggregateCode()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        Languages = ["spa", "eng"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Language,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["eng"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [TestCase(CriteriaValueOperator.Equals, 2020, 10, 1, TemporalPrecision.Day, true)]
    [TestCase(CriteriaValueOperator.Equals, 2020, 10, 2, TemporalPrecision.Day, false)]
    [TestCase(CriteriaValueOperator.In, 2020, 10, 1, TemporalPrecision.Day, true)]
    [TestCase(CriteriaValueOperator.GreaterThan, 2020, 7, 1, TemporalPrecision.Day, true)]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, 2020, 10, 1, TemporalPrecision.Day, true)]
    [TestCase(CriteriaValueOperator.LessThan, 2020, 11, 1, TemporalPrecision.Month, true)]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, 2020, 9, 1, TemporalPrecision.Month, false)]
    public async Task ExecuteSearchAsync_UsesRefinedDayCandidate_WhenPublishDateOperatorVaries(
        CriteriaValueOperator operation,
        int year,
        int month,
        int day,
        TemporalPrecision precision,
        bool expectedMatch)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        FirstPublishYear = 2020,
                        PublishDates = ["October 1, 2020"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.PublishDate,
                        Operator = operation,
                        TemporalValues =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
                                Precision = precision
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, expectedMatch ? Is.Not.Empty : Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenPublishDateEqualityUsesRefinedMonthCandidate()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        FirstPublishYear = 2020,
                        PublishDates = ["October 2020"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.PublishDate,
                        Operator = CriteriaValueOperator.Equals,
                        TemporalValues =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(2020, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Month
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenPublishDateEqualityFallsBackToFirstPublishYear()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        FirstPublishYear = 1937,
                        PublishDates = ["not a date", "January 1, 1940"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.PublishDate,
                        Operator = CriteriaValueOperator.Equals,
                        TemporalValues =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(1937, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [TestCase(CriteriaValueOperator.IsNotNull, true)]
    [TestCase(CriteriaValueOperator.IsNull, false)]
    public async Task ExecuteSearchAsync_RecognizesAnyNonblankAggregateCode_WhenLanguagePresenceIsTested(
        CriteriaValueOperator op,
        bool expectedMatch)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        Languages = ["", "spa", "eng"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters = [new QueryFilter { Field = QueryField.Language, Operator = op }],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, expectedMatch ? Is.Not.Empty : Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenGenreEqualityNormalizesReturnedSubject()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        Subjects = ["Science / Fiction"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Genre,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["science fiction"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenAlignedGenreCriteriaMatchTheSameSubject()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        Subjects = ["Fantasy", "Science fiction"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                FilterGroups =
                [
                    new FilterGroup
                    {
                        Alternatives =
                        [
                            new FilterAlternative
                            {
                                AlignGenres = true,
                                Filters =
                                [
                                    new QueryFilter
                                    {
                                        Field = QueryField.Genre,
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["fantasy"]
                                    },
                                    new QueryFilter
                                    {
                                        Field = QueryField.Genre,
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["science"],
                                        Negate = true
                                    }
                                ]
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [TestCase((int)QueryField.Person, "Alice", "person_bob")]
    [TestCase((int)QueryField.Place, "Paris", "london")]
    [TestCase((int)QueryField.Time, "Victorian", "modern_era")]
    public async Task ExecuteSearchAsync_DropsHit_WhenAlignedFacetCriteriaSpanDifferentFacets(
        int facet,
        string textValue,
        string keyValue)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"],
                        Persons = ["Alice", "Bob"],
                        PersonKeys = ["person_alice", "person_bob"],
                        Places = ["Paris", "London"],
                        PlaceKeys = ["paris", "london"],
                        Times = ["Victorian", "Modern"],
                        TimeKeys = ["victorian_era", "modern_era"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                FilterGroups =
                [
                    new FilterGroup
                    {
                        Alternatives =
                        [
                            new FilterAlternative
                            {
                                AlignSubjectFacet = (QueryField)facet,
                                Filters =
                                [
                                    new QueryFilter
                                    {
                                        Field = (QueryField)facet,
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = [textValue]
                                    },
                                    new QueryFilter
                                    {
                                        Field = (QueryField)facet,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [keyValue]
                                    }
                                ]
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [TestCase("Canonical title", true)]
    [TestCase("Translated title", false)]
    public async Task ExecuteSearchAsync_UsesCanonicalWorkTitleOnly_WhenOriginalTitleIsFiltered(
        string title,
        bool expectedMatch)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Canonical title",
                        AlternativeTitles = ["Translated title"],
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.OriginalTitle,
                        Operator = CriteriaValueOperator.Equals,
                        Values = [title]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["title"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, expectedMatch ? Is.Not.Empty : Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHit_WhenAuthorFiltersMatchDifferentAlignedPairs()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Good Omens",
                        AuthorKeys = ["OL1A", "OL2A"],
                        AuthorNames = ["Terry Pratchett", "Neil Gaiman"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.AuthorOlid,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["OL2A"]
                    },
                    new QueryFilter
                    {
                        Field = QueryField.AuthorName,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Neil Gaiman"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["omens"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task ExecuteSearchAsync_AppliesOrInsideSomeOrNoneGroup_WhenRelationshipAlternativesVary(
        bool negate,
        bool expectedMatch)
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Alice"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                FilterGroups =
                [
                    new FilterGroup
                    {
                        Negate = negate,
                        Alternatives =
                        [
                            new FilterAlternative
                            {
                                AlignAuthors = true,
                                Filters =
                                [
                                    new QueryFilter
                                    {
                                        Field = QueryField.AuthorName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Bob"]
                                    }
                                ]
                            },
                            new FilterAlternative
                            {
                                AlignAuthors = true,
                                Filters =
                                [
                                    new QueryFilter
                                    {
                                        Field = QueryField.AuthorName,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Alice"]
                                    },
                                    new QueryFilter
                                    {
                                        Field = QueryField.AuthorOlid,
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["OL1A"]
                                    }
                                ]
                            }
                        ]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, expectedMatch ? Is.Not.Empty : Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_KeepsHitAndWarns_WhenOptionalFieldIsMissing()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Book",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Author"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Language,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["eng"]
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["book"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.WorkOlid), Is.EqualTo(["OL1W"]));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Does.Contain("Open Library retained rows with missing optional fields and degraded certainty."));
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_DropsHit_WhenNegationInvertsVerifiedMatch()
    {
        // Arrange
        _client
            .Setup(value => value.GetJsonAsync<SearchResponseDto>(
                It.IsAny<ApiRequest>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                NumFound = 1,
                NumFoundExact = true,
                Docs =
                [
                    new SearchDocumentDto
                    {
                        Key = "/works/OL1W",
                        Title = "Dune",
                        AuthorKeys = ["OL1A"],
                        AuthorNames = ["Frank Herbert"]
                    }
                ]
            });
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Filters =
                [
                    new QueryFilter
                    {
                        Field = QueryField.Title,
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Dune"],
                        Negate = true
                    }
                ],
                Children =
                [
                    new PlanNode
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [new SourceTerm { Field = "title", Values = ["dune"] }]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 20
        };

        // Act
        var result = await _subject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestQueryAndFields_WhenFetchContainsBooleanGroups()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceGroups =
                [
                    new SourceGroup
                    {
                        Alternatives =
                        [
                            new SourceAlternative
                            {
                                Terms =
                                [
                                    new SourceTerm { Field = "author", Values = ["Ursula Le Guin"] },
                                    new SourceTerm { Field = "language", Values = ["eng"] }
                                ]
                            },
                            new SourceAlternative
                            {
                                Terms = [new SourceTerm { Field = "isbn", Values = ["9780143111597"] }]
                            }
                        ]
                    },
                    new SourceGroup
                    {
                        Alternatives =
                        [
                            new SourceAlternative
                            {
                                Terms = [new SourceTerm { Field = "subject", Values = ["science fiction"] }]
                            }
                        ]
                    }
                ],
                RequiredFields = ["alternative_title"],
                LanguagePreference = "en"
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _request.Parameters.Single(parameter => parameter.Key == "q").Value,
                Is.EqualTo(
                    "((author:\"Ursula Le Guin\" AND language:\"eng\") OR isbn:\"9780143111597\") " +
                    "AND subject:\"science fiction\""));
            Assert.That(
                _request.Parameters.Single(parameter => parameter.Key == "fields").Value,
                Does.EndWith(",publisher,alternative_title"));
            Assert.That(_request.Parameters.Single(parameter => parameter.Key == "lang").Value, Is.EqualTo("en"));
            Assert.That(_request.Parameters.Single(parameter => parameter.Key == "limit").Value, Is.EqualTo("100"));
            Assert.That(_request.Parameters.Single(parameter => parameter.Key == "offset").Value, Is.EqualTo("0"));
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestQuery_WhenTermIsNegated()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms =
                [
                    new SourceTerm { Field = "title", Values = ["Dune"] },
                    new SourceTerm { Field = "language", Values = ["fre"], Negate = true }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            _request.Parameters.Single(parameter => parameter.Key == "q").Value,
            Is.EqualTo("title:\"Dune\" AND NOT language:\"fre\""));
    }

    [TestCase((int)WildcardPlacement.Trailing, "harry", "title:harry*")]
    [TestCase((int)WildcardPlacement.Leading, "potter", "title:*potter")]
    [TestCase((int)WildcardPlacement.Trailing, "har*ry", "title:har\\*ry*")]
    [TestCase((int)WildcardPlacement.Leading, "pot?ter", "title:*pot\\?ter")]
    [TestCase((int)WildcardPlacement.Trailing, "harry potter", "title:\"harry potter\"")]
    [TestCase((int)WildcardPlacement.Leading, "harry potter", "title:\"harry potter\"")]
    public async Task ExecuteSearchAsync_RequestQuery_WhenWildcardSeedIsPlanned(
        int placement,
        string value,
        string expected)
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms =
                [
                    new SourceTerm
                    {
                        Field = "title",
                        Values = [value],
                        WildcardPlacement = (WildcardPlacement)placement
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(_request.Parameters.Single(parameter => parameter.Key == "q").Value, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestQuery_WhenWildcardHasSeveralValues()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms =
                [
                    new SourceTerm
                    {
                        Field = "title",
                        Values = ["hobbit", "harry potter", "dune"],
                        WildcardPlacement = WildcardPlacement.Trailing
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(
            _request.Parameters.Single(parameter => parameter.Key == "q").Value,
            Is.EqualTo("title:(dune* OR \"harry potter\" OR hobbit*)"));
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestSort_WhenLanguageIsOnlySeed()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "language", Values = ["eng"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_request.Parameters.Single(parameter => parameter.Key == "sort").Value, Is.EqualTo("editions"));
            Assert.That(_request.Parameters.Single(parameter => parameter.Key == "limit").Value, Is.EqualTo("100"));
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestFields_WhenNoResidualNeedsDetailProjection()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms = [new SourceTerm { Field = "title", Values = ["Dune"] }]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        var fields = _request.Parameters.Single(parameter => parameter.Key == "fields").Value.Split(',');
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields, Does.Contain("publisher"));
            Assert.That(fields, Does.Not.Contain("alternative_title"));
            Assert.That(fields, Does.Not.Contain("number_of_pages_median"));
            Assert.That(fields, Does.Not.Contain("subtitle"));
        }
    }

    [Test]
    public async Task ExecuteSearchAsync_RequestQuery_WhenMoreThanTenValuesArePresent()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Root = new PlanNode
            {
                Kind = PlanNodeKind.Fetch,
                SourceTerms =
                [
                    new SourceTerm
                    {
                        Field = "publisher",
                        Values =
                        [
                            "Publisher 01",
                            "Publisher 02",
                            "Publisher 03",
                            "Publisher 04",
                            "Publisher 05",
                            "Publisher 06",
                            "Publisher 07",
                            "Publisher 08",
                            "Publisher 09",
                            "Publisher 10",
                            "Publisher 11",
                            "Publisher 12"
                        ]
                    }
                ]
            },
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Advanced = true,
            MaxResults = 99
        };

        // Act
        await _requestSubject.ExecuteSearchAsync(plan, default).ConfigureAwait(false);

        // Assert
        var query = _request.Parameters.Single(parameter => parameter.Key == "q").Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Does.Contain("\"Publisher 01\""));
            Assert.That(query, Does.Contain("\"Publisher 10\""));
            Assert.That(query, Does.Contain("\"Publisher 12\""));
        }
    }
}
