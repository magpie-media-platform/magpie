using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class ContentMetadataSearcherTests
{
    [Test]
    public async Task SearchAsync_ReturnsMappedHits_WhenSearchTermMatchesSupportedLevel()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var searchEngine = new Mock<ISearchEngine>();
        searchEngine.Setup(engine => engine.SearchAsync(
            It.Is<QueryRequest>(request =>
                request.EntityKind == EntityKind.Movie && request.OrdinaryTerm == "Matrix"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new SearchHit { Qid = "Q83495", TypeQid = "Q11424", Label = "The Matrix" }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var subject = new ContentMetadataSearcher(
            settingsResolver.Object,
            searchEngine.Object,
            cache: null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var level = new ContentHierarchyLevelDefinition
        {
            Slug = Constants.MovieLevel,
            DisplayName = "Movie",
            CustomMetadataFields = []
        };

        // Act
        var result = (await subject.SearchAsync(level, "Matrix", "en", cancellationToken: default)
            .ConfigureAwait(false)).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Single().Title, Is.EqualTo("The Matrix"));
            Assert.That(result.Single().ProviderType, Is.EqualTo(Constants.ProviderType));
        }
    }

    [Test]
    public async Task HydrateAsync_FillsLeanFields_WhenSeasonCandidateResolves()
    {
        // Arrange
        var seasonEntity = new EntityDto
        {
            Id = "Q100",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Season 1" }
            },
            Descriptions = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "First season of A Show" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse(
                                    "{\"id\":\"" + Constants.SeasonQid + "\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                [Constants.PartOfSeriesPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q200\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                [Constants.SeriesStartPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse(
                                    "{\"time\":\"+2011-04-17T00:00:00Z\",\"precision\":11,\"timezone\":0," +
                                    "\"before\":0,\"after\":0," +
                                    "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
                                    .RootElement.Clone()
                            }
                        }
                    }
                ],
                [Constants.ImagePropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"Season Poster.jpg\"").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntityAsync(
            "Q100",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(seasonEntity);
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>());
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var subject = new ContentMetadataSearcher(
            settingsResolver.Object,
            new Mock<ISearchEngine>().Object,
            cache.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var candidate = new MetadataSearchCandidate
        {
            ProviderType = "wikidata",
            ExternalIdSource = "wikidata",
            ExternalId = "Q100",
            ContentType = "tv",
            Level = "season",
            Title = "Season 1"
        };

        // Act
        var result = await subject.HydrateAsync(
            candidate,
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Title, Is.EqualTo("Season 1"));
            Assert.That(result.Overview, Is.EqualTo("First season of A Show"));
            Assert.That(result.ReleaseDate, Is.EqualTo(new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                result.ThumbnailUrl?.AbsoluteUri,
                Is.EqualTo(
                    "https://commons.wikimedia.org/wiki/Special:Redirect/file/Season%20Poster.jpg"));
            Assert.That(result.Level, Is.EqualTo("season"));
            Assert.That(result.ContentType, Is.EqualTo("tv"));
            Assert.That(result.ExternalId, Is.EqualTo("Q100"));
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_SearchesShow_WhenTvLevelIsAbsent()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var searchEngine = new Mock<ISearchEngine>();
        searchEngine.Setup(engine => engine.SearchAsync(
            It.Is<QueryRequest>(request => request.EntityKind == EntityKind.Show),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new SearchHit { Qid = "Q1", TypeQid = "Q5398426", Label = "A Show" }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var subject = new ContentMetadataSearcher(
            settingsResolver.Object,
            searchEngine.Object,
            cache: null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "A Show" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Single().Level, Is.EqualTo("show"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_SearchesShow_WhenShowLevelIsExplicit()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var searchEngine = new Mock<ISearchEngine>();
        searchEngine.Setup(engine => engine.SearchAsync(
            It.Is<QueryRequest>(request => request.EntityKind == EntityKind.Show),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new SearchHit { Qid = "Q2", TypeQid = "Q5398426", Label = "Another Show" }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
            });
        var subject = new ContentMetadataSearcher(
            settingsResolver.Object,
            searchEngine.Object,
            cache: null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "show" }]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Another Show" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Single().Level, Is.EqualTo("show"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase("season")]
    [TestCase("episode")]
    public async Task AdvancedSearchAsync_ReturnsEmptyWithoutRequest_WhenUnsupportedTvLevelIsExplicit(string level)
    {
        // Arrange
        var subject = new ContentMetadataSearcher(
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = level }]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_SearchesShowWithWarning_WhenShowAndSeasonLevelsAreIncluded()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var searchEngine = new Mock<ISearchEngine>();
        searchEngine.Setup(engine => engine.SearchAsync(
            It.Is<QueryRequest>(request => request.EntityKind == EntityKind.Show),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                Hits = [new SearchHit { Qid = "Q3", TypeQid = "Q5398426", Label = "Mixed Scope Show" }],
                PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false },
                Warnings = ["execution warning"]
            });
        var subject = new ContentMetadataSearcher(
            settingsResolver.Object,
            searchEngine.Object,
            cache: null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["show", "season"]
                    }
                ]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Title = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "Scope"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches.Single().Level, Is.EqualTo("show"));
            Assert.That(result.Warnings, Has.Count.EqualTo(2));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.Some.Contains("searches for shows only"));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Does.Contain("execution warning"));
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsEmptyWithoutRequest_WhenLevelExcludesShow()
    {
        // Arrange
        var subject = new ContentMetadataSearcher(
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Value = "show"
                    }
                ]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsEmptyWithoutRequest_WhenContentTypeCriteriaConflicts()
    {
        // Arrange
        var subject = new ContentMetadataSearcher(
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "movie" }]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task AdvancedSearchAsync_ReturnsEmptyWithoutWarning_WhenScopeCannotBeEvaluated()
    {
        // Arrange
        var subject = new ContentMetadataSearcher(
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<ContentMetadataSearcher>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.GreaterThan,
                        Value = "show"
                    }
                ]
            }
        };

        // Act
        var result = await subject.AdvancedSearchAsync("tv", query, 25, "en", default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }
}
