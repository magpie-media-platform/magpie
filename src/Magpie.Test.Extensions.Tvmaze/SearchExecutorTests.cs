using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
internal sealed class SearchExecutorTests
{
    private static readonly ShowDto GameOfThrones = new()
    {
        Id = 82,
        Url = "https://www.tvmaze.com/shows/82/game-of-thrones",
        Name = "Game of Thrones",
        Language = "English",
        Genres = ["Drama", "Adventure"],
        Status = "Ended",
        Runtime = 60,
        Premiered = "2011-04-17",
        Ended = "2019-05-19",
        OfficialSite = "http://www.hbo.com/game-of-thrones",
        Summary = "<p>Seven noble families fight.</p>",
        Externals = new ExternalsDto { Imdb = "tt0944947", TheTvdb = 121361 }
    };

    private static readonly ShowDto GameOn = new()
    {
        Id = 83,
        Url = "https://www.tvmaze.com/shows/83/game-on",
        Name = "Game On",
        Language = "Japanese",
        Genres = ["Comedy"],
        Status = "Running",
        AverageRuntime = 30,
        Premiered = "2015-07-01",
        Summary = "<p>A cheerful gaming club.</p>"
    };

    private static readonly AkaDto[] RussianAka = [new AkaDto { Name = "Игра престолов" }];

    private static readonly SearchSeed BothShows = new SearchSeed.IdLookup(
    [
        new SearchSeed.Lookup(Constants.ProviderType, "82", 82),
        new SearchSeed.Lookup(Constants.ProviderType, "83", 83)
    ]);

    private Mock<IClient> _client = null!;

    private ISearchExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IClient>();
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GameOfThrones);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            83, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GameOn);
        _executor = new SearchExecutor(_client.Object, NullLogger<SearchExecutor>.Instance);
    }

    private static IEnumerable<TestCaseData> HitFilterCases()
    {
        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Title,
                Operator = CriteriaValueOperator.ContainsString,
                StringValues = ["thrones"]
            },
            new[] { "82" }).SetName("ExecuteAsync_KeepsOnlyTheTitleHolder_WhenTitleFilterContainsText");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Genre,
                GenreAlternatives =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["drama"]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "82" }).SetName("ExecuteAsync_KeepsOnlyTheGenreMember_WhenGenreFilterIsPositive");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Genre,
                Negate = true,
                GenreAlternatives =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["Drama"]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "83" }).SetName("ExecuteAsync_RemovesTheGenreMember_WhenGenreFilterIsNegated");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Language,
                Operator = CriteriaValueOperator.Equals,
                StringValues = ["English"]
            },
            new[] { "82" }).SetName("ExecuteAsync_KeepsOnlyTheMatchingLanguage_WhenLanguageFilterIsEquality");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Duration,
                Operator = CriteriaValueOperator.LessThanOrEqual,
                DurationValues = [TimeSpan.FromMinutes(30)]
            },
            new[] { "83" }).SetName("ExecuteAsync_UsesAverageRuntimeFallback_WhenDurationFilterIsBounded");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.PublishDate,
                Operator = CriteriaValueOperator.GreaterThanOrEqual,
                TemporalValues =
                [
                    new TemporalValue
                    {
                        Value = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        Precision = TemporalPrecision.Year
                    }
                ]
            },
            new[] { "83" }).SetName("ExecuteAsync_ReadsThePremiereDate_WhenPublishDateFilterIsBounded");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Dissolved,
                Operator = CriteriaValueOperator.IsNotNull
            },
            new[] { "82" }).SetName("ExecuteAsync_ReadsTheEndedDate_WhenDissolvedFilterRequiresPresence");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Established,
                Operator = CriteriaValueOperator.Equals,
                TemporalValues =
                [
                    new TemporalValue
                    {
                        Value = new DateTime(2011, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        Precision = TemporalPrecision.Year
                    }
                ]
            },
            new[] { "82" }).SetName("ExecuteAsync_ReadsThePremiereDate_WhenEstablishedFilterIsAYear");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.ProductionStatus,
                Operator = CriteriaValueOperator.Equals,
                StringValues = ["Running"]
            },
            new[] { "83" }).SetName("ExecuteAsync_KeepsOnlyTheMatchingStatus_WhenProductionStatusFilterIsEquality");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Overview,
                Operator = CriteriaValueOperator.ContainsString,
                StringValues = ["noble families"]
            },
            new[] { "82" }).SetName("ExecuteAsync_ReadsTheStrippedSummary_WhenOverviewFilterContainsText");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.ExternalId,
                ExternalIdAlternatives =
                [
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["imdb"]
                                }
                            ]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["tt0944947"]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "82" }).SetName("ExecuteAsync_KeepsOnlyTheIdentifiedShow_WhenExternalIdFilterIsPositive");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.ExternalId,
                Negate = true,
                ExternalIdAlternatives =
                [
                    new ExternalIdCriteria
                    {
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["tt0944947"]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "83" }).SetName("ExecuteAsync_RemovesTheIdentifiedShow_WhenExternalIdFilterIsNegated");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Source,
                Operator = CriteriaValueOperator.Equals,
                StringValues = ["tvmaze:show:83"]
            },
            new[] { "83" }).SetName("ExecuteAsync_KeepsOnlyTheAddressedShow_WhenSourceFilterIsEquality");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.Reference,
                ReferenceAlternatives =
                [
                    new ReferenceMetadataCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["official"]
                                }
                            ]
                        },
                        Url = new CriteriaSet<PropertyCriterion<Uri>>
                        {
                            And =
                            [
                                new PropertyCriterion<Uri>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = [new Uri("http://www.hbo.com")]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "82" }).SetName("ExecuteAsync_ReadsTheOfficialSite_WhenReferenceFilterIsAUrl");
    }

    private static IEnumerable<TestCaseData> AkaFilterCases()
    {
        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.OriginalTitle,
                Operator = CriteriaValueOperator.Equals,
                StringValues = ["Игра престолов"]
            },
            new[] { "82" }).SetName("ExecuteAsync_ReadsAkaNames_WhenOriginalTitleFilterIsEquality");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.AlternateText,
                AlternateTextAlternatives =
                [
                    new AlternateTextCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["Title"]
                                }
                            ]
                        },
                        Language = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
                        },
                        Text = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = ["престолов"]
                                }
                            ]
                        }
                    }
                ]
            },
            new[] { "82" }).SetName("ExecuteAsync_AlignsKindLanguageAndTextPerAka_WhenAlternateTextFilterIsPositive");

        yield return new TestCaseData(
            new LocalFilter
            {
                Kind = LocalFilterKind.AlternateText,
                AlternateTextAlternatives =
                [
                    new AlternateTextCriteria
                    {
                        Language = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["RU"]
                                }
                            ]
                        }
                    }
                ]
            },
            Array.Empty<string>()).SetName("ExecuteAsync_ReturnsEmpty_WhenAlternateTextLanguageUsesCountryCode");
    }

    [TestCaseSource(nameof(HitFilterCases))]
    public async Task ExecuteAsync_KeepsOnlyMatchingShows_WhenHitFilterIsApplied(
        LocalFilter filter,
        string[] expected)
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.ExternalId,
                    Seed = BothShows
                },
                HitFilters = [filter],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(AkaFilterCases))]
    public async Task ExecuteAsync_KeepsOnlyMatchingShows_WhenAkaFilterIsApplied(
        LocalFilter filter,
        string[] expected)
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.ExternalId,
                    Seed = BothShows
                },
                HitFilters = [],
                AkaFilters = [filter],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameOfThrones with { Embedded = new ShowEmbeddedDto { Akas = RussianAka } });
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                83,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameOn with { Embedded = new ShowEmbeddedDto { Akas = [] } });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteAsync_SkipsCreditFetch_WhenNetworkNameAndIdMatch()
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Lost",
            Network = new NetworkDto { Id = 8, Name = "HBO" },
            WebChannel = new NetworkDto { Id = 9, Name = "Max" }
        };
        var networkCriteria = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["network"]
                    }
                ]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["HBO"]
                    }
                ]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [Constants.ProviderType]
                                    }
                                ]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["8"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var webChannelCriteria = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["network"]
                    }
                ]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Max"]
                    }
                ]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [Constants.ProviderType]
                                    }
                                ]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["9"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [new RelationshipCriteriaResidual.Alternative(
                            networkCriteria,
                            MatchesNetwork: true,
                            MatchesPerson: false,
                            PassThrough: false)]),
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [new RelationshipCriteriaResidual.Alternative(
                            webChannelCriteria,
                            MatchesNetwork: true,
                            MatchesPerson: false,
                            PassThrough: false)])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(show);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_MatchesVoiceQualifier_WhenCastAndPersonIdMatch()
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Lost",
            Embedded = new ShowEmbeddedDto
            {
                Cast =
                [
                    new CastEntryDto
                    {
                        Person = new PersonDto { Id = 101, Name = "Matthew Fox" },
                        Character = new CharacterDto { Name = "Jack Shephard" },
                        Voice = true
                    }
                ],
                Crew = []
            }
        };
        var criteria = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"]
                    }
                ]
            },
            Qualifier = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Jack Shephard (voice)"]
                    }
                ]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [Constants.ProviderType]
                                    }
                                ]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["101"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var unmarkedCriteria = criteria with
        {
            Qualifier = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Jack Shephard"]
                    }
                ]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [new RelationshipCriteriaResidual.Alternative(
                            criteria,
                            MatchesNetwork: false,
                            MatchesPerson: true,
                            PassThrough: false)])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        var unmarkedPlan = plan with
        {
            Root = ((FilterPlanNode)plan.Root) with
            {
                RelationshipResiduals =
                [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [new RelationshipCriteriaResidual.Alternative(
                            unmarkedCriteria,
                            MatchesNetwork: false,
                            MatchesPerson: true,
                            PassThrough: false)])
                ]
            }
        };
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(show);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(show);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);
        var unmarkedResult = await _executor.ExecuteAsync(unmarkedPlan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(unmarkedResult.Matches, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_RejectsCandidate_WhenNegatedCrewRelationshipMatches()
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Lost",
            Embedded = new ShowEmbeddedDto
            {
                Crew =
                [
                    new CrewEntryDto
                    {
                        Type = "Screenplay",
                        Person = new PersonDto { Id = 201, Name = "Damon Lindelof" }
                    }
                ],
                Cast = []
            }
        };
        var criteria = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["writer"] }]
            },
            Qualifier = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Screenplay"]
                    }
                ]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Damon Lindelof"]
                    }
                ]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: null,
                        Negate: true,
                        [new RelationshipCriteriaResidual.Alternative(
                            criteria,
                            MatchesNetwork: false,
                            MatchesPerson: true,
                            PassThrough: false)])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(show);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(show);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_MatchesCandidate_WhenTargetCreditAppearsBeyondMappingCap()
    {
        // Arrange
        var cast = Enumerable.Range(1, 31)
            .Select(index => new CastEntryDto
            {
                Person = new PersonDto
                {
                    Id = index,
                    Name = index == 31 ? "Target Person" : $"Person {index}"
                }
            })
            .ToArray();
        var show = new ShowDto
        {
            Id = 82,
            Name = "Lost",
            Embedded = new ShowEmbeddedDto { Cast = cast, Crew = [] }
        };
        var criteria = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Target Person"]
                    }
                ]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [new RelationshipCriteriaResidual.Alternative(
                            criteria,
                            MatchesNetwork: false,
                            MatchesPerson: true,
                            PassThrough: false)])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(show);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(show);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
    }

    [Test]
    public async Task ExecuteAsync_SettlesBarePresenceAndExclusion_WhenPrimaryImageIsUsable()
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Lost",
            Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/lost.jpg" }
        };
        var presencePlan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [new ImageCriteriaResidual.Alternative(
                            new ImageCriteria(),
                            ImageCriteriaResidual.EvaluationKind.Presence)])
                ]
            },
            Dispositions = []
        };
        var exclusionPlan = presencePlan with
        {
            Root = ((FilterPlanNode)presencePlan.Root) with
            {
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: true,
                        [new ImageCriteriaResidual.Alternative(
                            new ImageCriteria(),
                            ImageCriteriaResidual.EvaluationKind.Presence)])
                ]
            }
        };
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(show);

        // Act
        var presenceResult = await _executor.ExecuteAsync(presencePlan, default).ConfigureAwait(false);
        var exclusionResult = await _executor.ExecuteAsync(exclusionPlan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(presenceResult.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(exclusionResult.Matches, Is.Empty);
        }

        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_MatchesNormalizedBannerFields_WhenPrimaryImageIsMissing()
    {
        // Arrange
        var criteria = new ImageCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["banner"] }]
            },
            MimeType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["image/jpeg"]
                    }
                ]
            },
            Url = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["https://static.tvmaze.com/banner.jpg"]
                    }
                ]
            },
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = [Constants.ProviderType]
                    }
                ]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
            },
            Width = new CriteriaSet<PropertyCriterion<int>>
            {
                And =
                [
                    new PropertyCriterion<int>
                    {
                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                        Value = 1200
                    }
                ]
            },
            Height = new CriteriaSet<PropertyCriterion<int>>
            {
                And =
                [
                    new PropertyCriterion<int>
                    {
                        Operator = CriteriaValueOperator.LessThan,
                        Value = 700
                    }
                ]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [new ImageCriteriaResidual.Alternative(
                            criteria,
                            ImageCriteriaResidual.EvaluationKind.Gallery)])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(82, Array.Empty<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto { Id = 82, Name = "Lost" });
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images =
                    [
                        new ImageDto
                        {
                            Type = "banner",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/banner.jpg",
                                    Width = 1200,
                                    Height = 675
                                }
                            }
                        }
                    ]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_KeepsImageCriteriaSeparate_WhenDifferentImagesMatch()
    {
        // Arrange
        var criteria = new ImageCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["poster"] }]
            },
            Width = new CriteriaSet<PropertyCriterion<int>>
            {
                And = [new PropertyCriterion<int> { Operator = CriteriaValueOperator.GreaterThan, Value = 1000 }]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [new ImageCriteriaResidual.Alternative(
                            criteria,
                            ImageCriteriaResidual.EvaluationKind.Gallery)])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(82, Array.Empty<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto { Id = 82, Name = "Lost" });
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images =
                    [
                        new ImageDto
                        {
                            Type = "poster",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/poster.jpg",
                                    Width = 680,
                                    Height = 1000
                                }
                            }
                        },
                        new ImageDto
                        {
                            Type = "banner",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/banner.jpg",
                                    Width = 1200,
                                    Height = 675
                                }
                            }
                        }
                    ]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_PagesBrowseUntilExtraSurvivor_WhenCandidatesNeedVerification()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _client.Setup(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 1, Name = "discard" },
            new ShowDto { Id = 2, Name = "keep two" }
        ]);
        _client.Setup(client => client.GetShowsPageAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 3, Name = "keep three" },
            new ShowDto { Id = 4, Name = "keep four" }
        ]);
        var browse = new FetchPlanNode
        {
            Id = 1,
            Contributes = "browse",
            AnchorKind = SearchAnchorKind.Browse,
            Seed = new SearchSeed.Browse()
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Contributes = "verified",
            Child = browse,
            HitFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.Title,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["keep"]
                }
            ],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 2,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["2", "3"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetShowsPageAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetShowsPageAsync(3, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DropsSamePageDuplicates_WhenBrowsing()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 1, Name = "first" },
            new ShowDto { Id = 1, Name = "duplicate" },
            new ShowDto { Id = 2, Name = "second" }
        ]);
        _client.Setup(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 3, Name = "third" }
        ]);
        var browse = new FetchPlanNode
        {
            Id = 1,
            Contributes = "browse",
            AnchorKind = SearchAnchorKind.Browse,
            Seed = new SearchSeed.Browse()
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 2,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "verified",
                Child = browse,
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1", "2"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetShowsPageAsync(2, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_StopsAtTheSurvivorThreshold_WhenBrowsing()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 1, Name = "first" },
            new ShowDto { Id = 2, Name = "second" },
            new ShowDto { Id = 3, Name = "unexamined third" },
            new ShowDto { Id = 4, Name = "unexamined fourth" }
        ]);
        var browse = new FetchPlanNode
        {
            Id = 1,
            Contributes = "browse",
            AnchorKind = SearchAnchorKind.Browse,
            Seed = new SearchSeed.Browse()
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 1,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "verified",
                Child = browse,
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCoalesceCandidates_WhenBrowseVerificationIsUnavailable()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new ShowDto { Id = 1, Name = "Browse candidate" }
        ]);
        _client.Setup(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShowDto>?)null);
        _client.Setup(client => client.SearchShowsAsync("fallback", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 2, Name = "Fallback candidate" } }
        ]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            1,
            It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShowDto?)null);
        var coalesce = new CoalescePlanNode
        {
            Id = 3,
            Contributes = "fallback anchors",
            Children =
            [
                new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "browse",
                    AnchorKind = SearchAnchorKind.Browse,
                    Seed = new SearchSeed.Browse()
                },
                new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "fallback search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["fallback"], IsAka: false)
                }
            ]
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Contributes = "verified",
            Child = coalesce,
            HitFilters = [],
            AkaFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.OriginalTitle,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["Alias"]
                }
            ],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.One.Contains("candidate was omitted"));
        }

        _client.Verify(client => client.SearchShowsAsync("fallback", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCoalesceCandidates_WhenTitleVerificationIsUnavailable()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("primary", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "Primary candidate" } }
        ]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            1,
            It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShowDto?)null);
        var coalesce = new CoalescePlanNode
        {
            Id = 3,
            Contributes = "fallback anchors",
            Children =
            [
                new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "primary search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["primary"], IsAka: false)
                },
                new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "fallback search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["fallback"], IsAka: false)
                }
            ]
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Contributes = "verified",
            Child = coalesce,
            HitFilters = [],
            AkaFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.OriginalTitle,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["Alias"]
                }
            ],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.One.Contains("candidate was omitted"));
        }

        _client.Verify(client => client.SearchShowsAsync("fallback", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_CoalescesCandidates_WhenChildIsExplicitlySkipped()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("fallback", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 2, Name = "Fallback candidate" } }
        ]);
        var skippedSeed = new SearchSeed.PersonChain([]);
        var coalesce = new CoalescePlanNode
        {
            Id = 3,
            Contributes = "fallback anchors",
            Children =
            [
                new ResolvePlanNode
                {
                    Id = 1,
                    Contributes = "not applicable",
                    Request = skippedSeed
                },
                new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "fallback search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["fallback"], IsAka: false)
                }
            ]
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Contributes = "verified",
            Child = coalesce,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["2"]));
        _client.Verify(client => client.SearchShowsAsync("fallback", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_PreservesPrimaryOrder_WhenPersonGroupsAreIntersected()
    {
        // Arrange
        _client.Setup(client => client.SearchPeopleAsync("First Person", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PersonSearchHitDto { Person = new PersonDto { Id = 11, Name = "First Person" } }
        ]);
        _client.Setup(client => client.SearchPeopleAsync("Second Person", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PersonSearchHitDto { Person = new PersonDto { Id = 12, Name = "Second Person" } }
        ]);
        _client.Setup(client => client.GetPersonCastCreditsAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CastCreditDto
            {
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 30, Name = "Thirty" } }
            },
            new CastCreditDto
            {
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 10, Name = "Ten" } }
            },
            new CastCreditDto
            {
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 20, Name = "Twenty" } }
            }
        ]);
        _client.Setup(client => client.GetPersonCrewCreditsAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CrewCreditDto
            {
                Type = "Writer",
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 20, Name = "Twenty" } }
            },
            new CrewCreditDto
            {
                Type = "Writer",
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 30, Name = "Thirty" } }
            }
        ]);
        var castSeed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["First Person"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "First Person" }],
                [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var crewSeed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Second Person"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "Second Person" }],
                [],
                IncludeCast: false,
                IncludeCrew: true,
                CrewKinds: [RelationshipKinds.Writer])
        ]);
        var castResolve = new ResolvePlanNode { Id = 1, Contributes = "first identity", Request = castSeed };
        var crewResolve = new ResolvePlanNode { Id = 3, Contributes = "second identity", Request = crewSeed };
        var intersect = new IntersectPlanNode
        {
            Id = 5,
            Contributes = "shared shows",
            Children =
            [
                new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "cast shows",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = castSeed,
                    Resolve = castResolve
                },
                new FetchPlanNode
                {
                    Id = 4,
                    Contributes = "writer shows",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = crewSeed,
                    Resolve = crewResolve
                }
            ]
        };
        var root = new FilterPlanNode
        {
            Id = 6,
            Contributes = "verified",
            Child = intersect,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["30", "20"]));
        _client.Verify(client => client.GetPersonCastCreditsAsync(11, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetPersonCrewCreditsAsync(11, It.IsAny<CancellationToken>()), Times.Never);
        _client.Verify(client => client.GetPersonCrewCreditsAsync(12, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetPersonCastCreditsAsync(12, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_UnionsCastAndCrewShows_WhenPersonKindIsGeneric()
    {
        // Arrange
        _client.Setup(client => client.SearchPeopleAsync("Both Roles", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PersonSearchHitDto { Person = new PersonDto { Id = 13, Name = "Both Roles" } }
        ]);
        _client.Setup(client => client.GetPersonCastCreditsAsync(13, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CastCreditDto
            {
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 1, Name = "Cast Show" } }
            }
        ]);
        _client.Setup(client => client.GetPersonCrewCreditsAsync(13, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("crew failed"));
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Both Roles"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "Both Roles" }],
                [],
                IncludeCast: true,
                IncludeCrew: true,
                CrewKinds: [])
        ]);
        var resolve = new ResolvePlanNode { Id = 1, Contributes = "identity", Request = seed };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Contributes = "credited shows",
            AnchorKind = SearchAnchorKind.Person,
            Seed = seed,
            Resolve = resolve
        };
        var root = new FilterPlanNode
        {
            Id = 3,
            Contributes = "verified",
            Child = fetch,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.One.Contains("results may be incomplete"));
        }
    }

    [Test]
    public async Task ExecuteAsync_StopsDemandDrivenUnion_WhenExtraSurvivorIsFound()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("first", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "discard" } },
            new SearchHitDto { Show = new ShowDto { Id = 2, Name = "keep two" } },
            new SearchHitDto { Show = new ShowDto { Id = 3, Name = "keep three" } }
        ]);
        var first = new FetchPlanNode
        {
            Id = 1,
            Contributes = "first search",
            AnchorKind = SearchAnchorKind.Title,
            Seed = new SearchSeed.FuzzyShow(["first"], IsAka: false)
        };
        var second = new FetchPlanNode
        {
            Id = 2,
            Contributes = "second search",
            AnchorKind = SearchAnchorKind.Title,
            Seed = new SearchSeed.FuzzyShow(["second"], IsAka: false)
        };
        var union = new UnionPlanNode
        {
            Id = 3,
            Contributes = "title alternatives",
            Children = [first, second]
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Contributes = "verified",
            Child = union,
            HitFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.Title,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["keep"]
                }
            ],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 1,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["2"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }

        _client.Verify(client => client.SearchShowsAsync("first", It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.SearchShowsAsync("second", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesUnionChild_WhenNovelShowsAreStillRequired()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("first", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "One" } }
        ]);
        _client.Setup(client => client.SearchShowsAsync("second", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "One duplicate" } },
            new SearchHitDto { Show = new ShowDto { Id = 2, Name = "Two" } },
            new SearchHitDto { Show = new ShowDto { Id = 3, Name = "Three" } }
        ]);
        var union = new UnionPlanNode
        {
            Id = 3,
            Contributes = "title alternatives",
            Children =
            [
                new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "first search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["first"], IsAka: false)
                },
                new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "second search",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["second"], IsAka: false)
                }
            ]
        };
        var root = new FilterPlanNode
        {
            Id = 4,
            Contributes = "verified",
            Child = union,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 2,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1", "2"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [Test]
    public async Task ExecuteAsync_RetainsSuccessfulIdentity_WhenAnotherAmbiguousIdentityFails()
    {
        // Arrange
        _client.Setup(client => client.SearchPeopleAsync("Shared Name", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PersonSearchHitDto { Person = new PersonDto { Id = 11, Name = "Shared Name" } },
            new PersonSearchHitDto { Person = new PersonDto { Id = 12, Name = "Shared Name" } }
        ]);
        _client.Setup(client => client.GetPersonCastCreditsAsync(11, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("first identity failed"));
        _client.Setup(client => client.GetPersonCastCreditsAsync(12, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new CastCreditDto
            {
                Embedded = new CreditEmbeddedDto { Show = new ShowDto { Id = 82, Name = "Success" } }
            }
        ]);
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Shared Name"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "Shared Name" }],
                [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var resolve = new ResolvePlanNode { Id = 1, Contributes = "identities", Request = seed };
        var root = new FilterPlanNode
        {
            Id = 3,
            Contributes = "verified",
            Child = new FetchPlanNode
            {
                Id = 2,
                Contributes = "cast shows",
                AnchorKind = SearchAnchorKind.Person,
                Seed = seed,
                Resolve = resolve
            },
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.One.Contains("results may be incomplete"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReusesPlanNodeResult_WhenPlanNodeRepeats()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("once", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "One" } }
        ]);
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Contributes = "shared search",
            AnchorKind = SearchAnchorKind.Title,
            Seed = new SearchSeed.FuzzyShow(["once"], IsAka: false)
        };
        var union = new UnionPlanNode
        {
            Id = 2,
            Contributes = "reused branch",
            Children = [fetch, fetch]
        };
        var root = new FilterPlanNode
        {
            Id = 3,
            Contributes = "verified",
            Child = union,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
        _client.Verify(client => client.SearchShowsAsync("once", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenTheOnlyFetchFails()
    {
        // Arrange
        _client.Setup(client => client.GetShowWithEmbedsAsync(82, Array.Empty<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("corrupt payload"));
        var root = new FilterPlanNode
        {
            Id = 2,
            Contributes = "verified",
            Child = new FetchPlanNode
            {
                Id = 1,
                Contributes = "native id",
                AnchorKind = SearchAnchorKind.NativeId,
                Seed = new SearchSeed.IdLookup(
                    [new SearchSeed.Lookup(Constants.ProviderType, "82", ShowId: 82)])
            },
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var execute = () => _executor.ExecuteAsync(plan, default);

        // Assert
        Assert.That(execute, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ExecuteAsync_UsesCombinedDetailRequest_WhenMultipleResidualEmbedsAreRequired()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("show", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "Show" } }
        ]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            1,
            It.Is<IReadOnlyList<string>>(embeds => embeds.SequenceEqual(
                new[] { "akas", "cast", "crew", "images" })),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 1,
                Name = "Show",
                Embedded = new ShowEmbeddedDto
                {
                    Akas = [new AkaDto { Name = "Alias" }],
                    Cast = [new CastEntryDto { Person = new PersonDto { Id = 7, Name = "Actor" } }],
                    Crew = [],
                    Images =
                    [
                        new ImageDto
                        {
                            Id = 9,
                            Type = "banner",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/banner.jpg",
                                    Width = 1200,
                                    Height = 675
                                }
                            }
                        }
                    ]
                }
            });
        var relationship = new RelationshipCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "cast" }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "Actor" }]
            }
        };
        var imageCriteria = new ImageCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "banner" }]
            }
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Contributes = "verified",
            Child = new FetchPlanNode
            {
                Id = 1,
                Contributes = "search",
                AnchorKind = SearchAnchorKind.Title,
                Seed = new SearchSeed.FuzzyShow(["show"], IsAka: false)
            },
            HitFilters = [],
            AkaFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.OriginalTitle,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["Alias"]
                }
            ],
            RelationshipResiduals =
            [
                new RelationshipCriteriaResidual(
                    SourceGroupIndex: 0,
                    Negate: false,
                    [new(relationship,MatchesNetwork: false,MatchesPerson: true,PassThrough: false)])
            ],
            ImageResiduals = [new(Negate: false, [new(imageCriteria, ImageCriteriaResidual.EvaluationKind.Gallery)])]
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                1,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(), new[] { "cast", "crew" }, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_OmitsCandidate_WhenCreditVerificationIsUnavailable()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("show", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "Show" } }
        ]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            1,
            It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShowDto?)null);
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Contributes = "search",
            AnchorKind = SearchAnchorKind.Title,
            Seed = new SearchSeed.FuzzyShow(["show"], IsAka: false)
        };
        var relationship = new RelationshipCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "cast" }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "Actor" }]
            }
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Contributes = "verified",
            Child = fetch,
            HitFilters = [],
            AkaFilters = [],
            RelationshipResiduals =
            [
                new RelationshipCriteriaResidual(
                    SourceGroupIndex: 0,
                    Negate: false,
                    [new(relationship,MatchesNetwork: false,MatchesPerson: true,PassThrough: false)])
            ],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.One.Contains("candidate was omitted"));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(), new[] { "cast", "crew" }, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_Throws_WhenAkaVerificationFailsForEveryCandidate()
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("show", It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new SearchHitDto { Show = new ShowDto { Id = 1, Name = "Show" } }
        ]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
            1,
            It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("failed"));
        var fetch = new FetchPlanNode
        {
            Id = 1,
            Contributes = "search",
            AnchorKind = SearchAnchorKind.Title,
            Seed = new SearchSeed.FuzzyShow(["show"], IsAka: false)
        };
        var root = new FilterPlanNode
        {
            Id = 2,
            Contributes = "verified",
            Child = fetch,
            HitFilters = [],
            AkaFilters =
            [
                new LocalFilter
                {
                    Kind = LocalFilterKind.OriginalTitle,
                    Operator = CriteriaValueOperator.ContainsString,
                    StringValues = ["Alias"]
                }
            ],
            RelationshipResiduals = [],
            ImageResiduals = []
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = root,
            Dispositions = []
        };

        // Act
        var execute = () => _executor.ExecuteAsync(plan, default);

        // Assert
        Assert.That(execute, Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOneMatch_WhenThePlannedTitleSearchRepeatsAndOmitsShows()
    {
        // Arrange
        _client
            .Setup(client => client.SearchShowsAsync("Trace Needle", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto
                    {
                        Id = 1,
                        Name = "Trace Needle",
                        Summary = "secret-result-payload"
                    }
                },
                new SearchHitDto
                {
                    Show = new ShowDto
                    {
                        Id = 1,
                        Name = "Trace Needle",
                        Summary = "duplicate-result-payload"
                    }
                },
                new SearchHitDto
                {
                    Show = null
                }
            ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 1,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Trace Needle"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.ContainsString,
                        StringValues = ["Trace Needle"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["1"]));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(
            client => client.SearchShowsAsync("Trace Needle", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ResolvesEachPersonIdentityOnce_WhenThePersonSearchRepeatsHits()
    {
        // Arrange
        _client
            .Setup(client => client.SearchPeopleAsync("Alex", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PersonSearchHitDto { Person = new PersonDto { Id = 10, Name = "Alex" } },
                new PersonSearchHitDto { Person = new PersonDto { Id = 10, Name = "Alex" } },
                new PersonSearchHitDto { Person = new PersonDto { Id = 11, Name = "Taylor" } },
                new PersonSearchHitDto { Person = null }
            ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new CastCreditDto
                {
                    Embedded = new CreditEmbeddedDto
                    {
                        Show = new ShowDto { Id = 82, Name = "Alex Show" }
                    }
                }
            ]);
        var subjectCriterion = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.ContainsString,
            Value = "Alex"
        };
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Alex"],
                [subjectCriterion],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Contributes = "resolved identities",
            Request = seed
        };
        var fetch = new FetchPlanNode
        {
            Id = 2,
            Contributes = "person credits",
            AnchorKind = SearchAnchorKind.Person,
            Seed = seed,
            Resolve = resolve
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = 10,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "verified shows",
                Child = fetch,
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetPersonCastCreditsAsync(10, It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.GetPersonCastCreditsAsync(11, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsVerifiedShow_WhenExternalIdSeedsLookup()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.ExternalId,
                    Seed = new SearchSeed.IdLookup(
                    [
                        new SearchSeed.Lookup(Constants.ImdbSource, "tt0411008", null)
                    ])
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.ExternalId,
                        ExternalIdAlternatives =
                        [
                            new ExternalIdCriteria
                            {
                                Source = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Values = [Constants.ImdbSource],
                                        },
                                    ],
                                },
                                Value = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Values = ["tt0411008"],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client =>
                client.LookupShowAsync(Constants.ImdbSource, "tt0411008", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ShowDto
                {
                    Id = 82,
                    Name = "Lost",
                    Externals = new ExternalsDto { Imdb = "tt0411008" },
                });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.LookupShowAsync(
                Constants.ImdbSource,
                "tt0411008",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.SearchShowsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmpty_WhenLookupDoesNotVerifyExternalId()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.ExternalId,
                    Seed = new SearchSeed.IdLookup(
                    [
                        new SearchSeed.Lookup(Constants.ImdbSource, "tt0411008", null)
                    ])
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.ExternalId,
                        ExternalIdAlternatives =
                        [
                            new ExternalIdCriteria
                            {
                                Source = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Values = [Constants.ImdbSource],
                                        },
                                    ],
                                },
                                Value = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Values = ["tt0411008"],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client =>
                client.LookupShowAsync(Constants.ImdbSource, "tt0411008", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ShowDto
                {
                    Id = 82,
                    Name = "Lost",
                    Externals = new ExternalsDto { Imdb = "tt9999999" },
                });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_UsesDirectFetch_WhenSourceIdSeedsLookup()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.NativeId,
                    Seed = new SearchSeed.IdLookup([new SearchSeed.Lookup(Constants.ProviderType, "82", 82)])
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Source,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["tvmaze:show:82"],
                    },
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(82, Array.Empty<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto { Id = 82, Name = "Lost" });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(client => client.GetShowWithEmbedsAsync(
            82, Array.Empty<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(
            client => client.LookupShowAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_VerifiesAkas_WhenOriginalTitleSeedsFuzzySearch()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Perdidos"], IsAka: true)
                },
                HitFilters = [],
                AkaFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.OriginalTitle,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Perdidos"],
                    },
                ],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Perdidos", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new SearchHitDto
                        {
                            Show = new ShowDto { Id = 82, Name = "Lost" },
                        },
                ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Akas = [new AkaDto { Name = "Perdidos" }]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsAkas_WhenHitFiltersRemoveEveryCandidate()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"],
                    },
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Language,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Klingon"],
                    },
                ],
                AkaFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.OriginalTitle,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Perdidos"],
                    },
                ],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new SearchHitDto
                        {
                            Show = new ShowDto
                            {
                                Id = 82,
                                Name = "Lost",
                                Language = "English",
                            },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsCredits_WhenPersonSearchIsEmpty()
    {
        // Arrange
        var subject = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.Equals,
            Values = ["Matthew Fox"]
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Contributes = "resolved person identities",
            Request = new SearchSeed.PersonChain(
            [
                new SearchSeed.PersonAlternative(
                    ["Matthew Fox"],
                    [subject],
                    PersonIds: [],
                    IncludeCast: true,
                    IncludeCrew: true,
                    CrewKinds: [])
            ])
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 5,
                Contributes = "locally verified show candidates",
                Child = new UnionPlanNode
                {
                    Id = 4,
                    Contributes = "shows credited to resolved people",
                    Children =
                    [
                        new FetchPlanNode
                        {
                            Id = 2,
                            Contributes = "shows with cast credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Matthew Fox"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: true,
                                    IncludeCrew: false,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        },
                        new FetchPlanNode
                        {
                            Id = 3,
                            Contributes = "shows with crew credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Matthew Fox"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: false,
                                    IncludeCrew: true,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        }
                    ]
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Matthew Fox", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
        _client.Verify(
            client => client.GetPersonCastCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            client => client.GetPersonCrewCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_DeduplicatesShows_WhenPersonHasCastAndCrewCredits()
    {
        // Arrange
        var subject = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.Equals,
            Values = ["Matthew Fox"]
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Contributes = "resolved person identities",
            Request = new SearchSeed.PersonChain(
            [
                new SearchSeed.PersonAlternative(
                    ["Matthew Fox"],
                    [subject],
                    PersonIds: [],
                    IncludeCast: true,
                    IncludeCrew: true,
                    CrewKinds: [])
            ])
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 5,
                Contributes = "locally verified show candidates",
                Child = new UnionPlanNode
                {
                    Id = 4,
                    Contributes = "shows credited to resolved people",
                    Children =
                    [
                        new FetchPlanNode
                        {
                            Id = 2,
                            Contributes = "shows with cast credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Matthew Fox"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: true,
                                    IncludeCrew: false,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        },
                        new FetchPlanNode
                        {
                            Id = 3,
                            Contributes = "shows with crew credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Matthew Fox"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: false,
                                    IncludeCrew: true,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        }
                    ]
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Matthew Fox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 101, Name = "Matthew Fox" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CastCreditDto
                        {
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 82, Name = "Lost" },
                            },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCrewCreditsAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CrewCreditDto
                        {
                            Type = "Producer",
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 82, Name = "Lost" },
                            },
                        },
                        new CrewCreditDto
                        {
                            Type = "Producer",
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 83, Name = "Haunted" },
                            },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82", "83"]));
            Assert.That(
                result.Matches[0].SubjectReferences,
                Has.Some.Matches<ProviderMetadataSearchSubjectReference>(reference =>
                    reference.DisplayName == "Matthew Fox" && reference.SubjectKind == "person"));
        }

        _client.Verify(client => client.GetPersonCastCreditsAsync(101, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(client => client.GetPersonCrewCreditsAsync(101, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsDirectorShows_WhenRelationshipRestrictsCrewKind()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Mimi Leder"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Mimi Leder"] }],
                PersonIds: [],
                IncludeCast: false,
                IncludeCrew: true,
                CrewKinds: ["director"])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with crew credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Mimi Leder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 201, Name = "Mimi Leder" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCrewCreditsAsync(201, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CrewCreditDto
                        {
                            Type = "Director",
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 82, Name = "The Leftovers" },
                            },
                        },
                        new CrewCreditDto
                        {
                            Type = "Producer",
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 83, Name = "Shameless" },
                            },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetPersonCastCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ExaminesEveryPerson_WhenPersonSearchReturnsSixMatches()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Alex Smith"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Alex Smith"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        var hits = Enumerable
            .Range(1, 6)
            .Select(id => new PersonSearchHitDto
            {
                Person = new PersonDto { Id = id, Name = "Alex Smith" },
            })
            .ToArray();
        _client
            .Setup(client => client.SearchPeopleAsync("Alex Smith", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (long personId, CancellationToken _) =>

                    [
                        new CastCreditDto
                        {
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = (int)personId, Name = "Show" },
                            },
                        },
                    ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Has.Count.EqualTo(6));
        _client.Verify(client => client.GetPersonCastCreditsAsync(6, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_QueriesEveryName_WhenPersonSeedContainsSixNames()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Person 1"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 1"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: []),
            new SearchSeed.PersonAlternative(
                ["Person 2"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 2"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: []),
            new SearchSeed.PersonAlternative(
                ["Person 3"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 3"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: []),
            new SearchSeed.PersonAlternative(
                ["Person 4"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 4"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: []),
            new SearchSeed.PersonAlternative(
                ["Person 5"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 5"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: []),
            new SearchSeed.PersonAlternative(
                ["Person 6"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Person 6"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
        _client.Verify(
            client => client.SearchPeopleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6));
        _client.Verify(client => client.SearchPeopleAsync("Person 6", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsShows_WhenPersonContainsSeedsSearch()
    {
        // Arrange
        var subject = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.ContainsString,
            Values = ["Bryan Cranston"]
        };
        var resolve = new ResolvePlanNode
        {
            Id = 1,
            Contributes = "resolved person identities",
            Request = new SearchSeed.PersonChain(
            [
                new SearchSeed.PersonAlternative(
                    ["Bryan Cranston"],
                    [subject],
                    PersonIds: [],
                    IncludeCast: true,
                    IncludeCrew: true,
                    CrewKinds: [])
            ])
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 5,
                Contributes = "locally verified show candidates",
                Child = new UnionPlanNode
                {
                    Id = 4,
                    Contributes = "shows credited to resolved people",
                    Children =
                    [
                        new FetchPlanNode
                        {
                            Id = 2,
                            Contributes = "shows with cast credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Bryan Cranston"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: true,
                                    IncludeCrew: false,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        },
                        new FetchPlanNode
                        {
                            Id = 3,
                            Contributes = "shows with crew credits for resolved people",
                            AnchorKind = SearchAnchorKind.Person,
                            Seed = new SearchSeed.PersonChain(
                            [
                                new SearchSeed.PersonAlternative(
                                    ["Bryan Cranston"],
                                    [subject],
                                    PersonIds: [],
                                    IncludeCast: false,
                                    IncludeCrew: true,
                                    CrewKinds: [])
                            ]),
                            Resolve = resolve
                        }
                    ]
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Bryan Cranston", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 307, Name = "Bryan Cranston" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(307, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CastCreditDto
                        {
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 169, Name = "Breaking Bad" },
                            },
                        },
                ]);
        _client.Setup(client => client.GetPersonCrewCreditsAsync(307, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.Title), Is.EqualTo(["Breaking Bad"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsLaterNameShow_WhenEarlierNameHasManyMatches()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["John Smith", "Peter Dinklage"],
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["John Smith", "Peter Dinklage"]
                    }
                ],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("John Smith", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [.. Enumerable
                    .Range(1, 5)
                    .Select(id => new PersonSearchHitDto
                    {
                        Person = new PersonDto { Id = id, Name = "John Smith" },
                    })]);
        _client
            .Setup(client => client.SearchPeopleAsync("Peter Dinklage", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 14072, Name = "Peter Dinklage" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (long personId, CancellationToken _) =>
                    personId == 1
                        ? Enumerable
                            .Range(1, 10)
                            .Select(id => new CastCreditDto
                            {
                                Embedded = new CreditEmbeddedDto
                                {
                                    Show = new ShowDto { Id = id, Name = "John Show" },
                                },
                            })
                            .ToArray()
                    : personId == 14072
                        ?
                        [
                            new CastCreditDto
                            {
                                Embedded = new CreditEmbeddedDto
                                {
                                    Show = new ShowDto { Id = 82, Name = "Game of Thrones" },
                                },
                            },
                        ]
                    : []);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Has.Member("82"));
        _client.Verify(
            client => client.SearchPeopleAsync("Peter Dinklage", It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(client => client.GetPersonCastCreditsAsync(14072, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_UsesDirectPersonFetch_WhenRelationshipHasTvmazePersonId()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                Names: [],
                SubjectCriteria: [],
                PersonIds: [14072],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.GetPersonByIdAsync(14072, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonDto { Id = 14072, Name = "Peter Dinklage" });
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(14072, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CastCreditDto
                        {
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 82, Name = "Game of Thrones" },
                            },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(client => client.GetPersonByIdAsync(14072, It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(
            client => client.SearchPeopleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEveryShow_WhenCastCreditsContainElevenShows()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Actor"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Actor"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 101, Name = "Actor" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [.. Enumerable
                    .Range(1, 11)
                    .Select(id => new CastCreditDto
                    {
                        Embedded = new CreditEmbeddedDto
                        {
                            Show = new ShowDto { Id = id, Name = "Show" },
                        },
                    })]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Has.Count.EqualTo(11));
        _client.Verify(
            client => client.GetPersonCrewCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEveryShow_WhenCrewCreditsContainElevenShows()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Crew Member"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Crew Member"] }],
                PersonIds: [],
                IncludeCast: false,
                IncludeCrew: true,
                CrewKinds: ["crew"])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with crew credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Crew Member", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 201, Name = "Crew Member" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCrewCreditsAsync(201, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [.. Enumerable
                    .Range(1, 11)
                    .Select(id => new CrewCreditDto
                    {
                        Type = "Producer",
                        Embedded = new CreditEmbeddedDto
                        {
                            Show = new ShowDto { Id = id, Name = "Show" },
                        },
                    })]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Has.Count.EqualTo(11));
        _client.Verify(
            client => client.GetPersonCastCreditsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEveryShow_WhenTheFuzzySearchSetIsFixed()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Show"], IsAka: false)
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Show"],
                    },
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        var hits = Enumerable
            .Range(1, 11)
            .Select(id => new SearchHitDto
            {
                Show = new ShowDto { Id = id, Name = "Show" },
            })
            .ToArray();
        _client.Setup(client => client.SearchShowsAsync("Show", It.IsAny<CancellationToken>())).ReturnsAsync(hits);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Has.Count.EqualTo(11));
    }

    [Test]
    public async Task ExecuteAsync_ReturnsLaterTitleAlternative_WhenFirstSearchIsEmpty()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "show candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Missing", "Game of Thrones"], IsAka: false)
                },
                HitFilters = [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Missing", "Game of Thrones"],
                    },
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client.Setup(client => client.SearchShowsAsync("Missing", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _client
            .Setup(client => client.SearchShowsAsync("Game of Thrones", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new SearchHitDto
                        {
                            Show = new ShowDto { Id = 82, Name = "Game of Thrones" },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(client => client.SearchShowsAsync("Missing", It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(
            client => client.SearchShowsAsync("Game of Thrones", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsCreditFetch_WhenNetworkResidualMatchesSearchHit()
    {
        // Arrange
        var network = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["HBO"] }]
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["network"] }]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals =
                [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: 0,
                        Negate: false,
                        [
                            new RelationshipCriteriaResidual.Alternative(
                                network,
                                MatchesNetwork: true,
                                MatchesPerson: false,
                                PassThrough: false)
                        ])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto
                    {
                        Id = 82,
                        Name = "Lost",
                        Network = new NetworkDto { Id = 8, Name = "HBO" }
                    }
                }
            ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_SkipsShowCreditFetch_WhenPersonSeedSettlesRelationshipGroup()
    {
        // Arrange
        var seed = new SearchSeed.PersonChain(
        [
            new SearchSeed.PersonAlternative(
                ["Matthew Fox"],
                [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Matthew Fox"] }],
                PersonIds: [],
                IncludeCast: true,
                IncludeCrew: false,
                CrewKinds: [])
        ]);
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 3,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 2,
                    Contributes = "shows with cast credits for resolved people",
                    AnchorKind = SearchAnchorKind.Person,
                    Seed = seed,
                    Resolve = new ResolvePlanNode
                    {
                        Id = 1,
                        Contributes = "resolved person identities",
                        Request = seed
                    }
                },
                HitFilters = [],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchPeopleAsync("Matthew Fox", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new PersonSearchHitDto
                        {
                            Person = new PersonDto { Id = 101, Name = "Matthew Fox" },
                        },
                ]);
        _client
            .Setup(client => client.GetPersonCastCreditsAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [
                        new CastCreditDto
                        {
                            Embedded = new CreditEmbeddedDto
                            {
                                Show = new ShowDto { Id = 82, Name = "Lost" },
                            },
                        },
                ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RejectsMatchingCandidate_WhenPersonRelationshipIsExcluded()
    {
        // Arrange
        var excluded = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"]
                    }
                ]
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }]
            }
        };
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals =
                [
                    new RelationshipCriteriaResidual(
                        SourceGroupIndex: null,
                        Negate: true,
                        [
                            new RelationshipCriteriaResidual.Alternative(
                                excluded,
                                MatchesNetwork: false,
                                MatchesPerson: true,
                                PassThrough: false)
                        ])
                ],
                ImageResiduals = []
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto { Id = 82, Name = "Lost" },
                },
            ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ShowDto
                {
                    Id = 82,
                    Name = "Lost",
                    Embedded = new ShowEmbeddedDto
                    {
                        Cast =
                        [
                            new CastEntryDto
                            {
                                Person = new PersonDto { Id = 101, Name = "Matthew Fox" },
                                Character = new CharacterDto { Name = "Jack Shephard" },
                            },
                        ],
                        Crew = [],
                    },
                });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches, Is.Empty);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsImageFetch_WhenPrimarySettlesBarePresence()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria(),
                                ImageCriteriaResidual.EvaluationKind.Presence)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto
                    {
                        Id = 82,
                        Name = "Lost",
                        Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/lost.jpg" }
                    }
                }
            ]);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_FetchesTheNullPrimaryGalleryOnlyForSurvivors_WhenHitFilteringRemovesACandidate()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria(),
                                ImageCriteriaResidual.EvaluationKind.Presence)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto { Id = 82, Name = "Lost" },
                },
                new SearchHitDto
                {
                    Show = new ShowDto { Id = 83, Name = "Other" },
                },
            ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images = [
                        new ImageDto
                        {
                            Type = "banner",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/banner.jpg",
                                    Width = 1200,
                                    Height = 675,
                                },
                            },
                        },
                    ]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                83,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_FetchesGalleryOnce_WhenMultipleImageResidualsArePresent()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria
                                {
                                    Type = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Values = ["poster"]
                                            }
                                        ]
                                    }
                                },
                                ImageCriteriaResidual.EvaluationKind.Gallery)
                        ]),
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria
                                {
                                    Width = new CriteriaSet<PropertyCriterion<int>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<int>
                                            {
                                                Operator = CriteriaValueOperator.GreaterThan,
                                                Value = 500
                                            }
                                        ]
                                    }
                                },
                                ImageCriteriaResidual.EvaluationKind.Gallery)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto
                    {
                        Id = 82,
                        Name = "Lost",
                        Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/primary.jpg" }
                    }
                }
            ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images = [
                        new ImageDto
                        {
                            Type = "poster",
                            Resolutions = new ImageResolutionsDto
                            {
                                Original = new ImageResolutionDto
                                {
                                    Url = "https://static.tvmaze.com/poster.jpg",
                                    Width = 680,
                                    Height = 1000,
                                },
                            },
                        },
                    ]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_FetchesGalleryToResolveBareImageExclusion_WhenPrimaryIsNull()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: true,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria(),
                                ImageCriteriaResidual.EvaluationKind.Presence)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [new SearchHitDto
                    {
                        Show = new ShowDto { Id = 82, Name = "Lost" },
                    },
                    new SearchHitDto
                    {
                        Show = new ShowDto { Id = 83, Name = "Lost" },
                    },
                ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images = [new ImageDto
                            {
                                Type = "banner",
                                Resolutions = new ImageResolutionsDto
                                {
                                    Original = new ImageResolutionDto
                                    {
                                        Url = "https://static.tvmaze.com/banner.jpg",
                                        Width = 1200,
                                        Height = 675,
                                    },
                                },
                            },
                        ]
                }
            });
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                83,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 83,
                Name = "Other",
                Embedded = new ShowEmbeddedDto { Images = [] }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["83"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                83,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_VerifiesImageFields_WhenGalleryHasMimeTypeAndNullLanguage()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria
                                {
                                    MimeType = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Values = ["image/jpeg"]
                                            }
                                        ]
                                    }
                                },
                                ImageCriteriaResidual.EvaluationKind.Gallery)
                        ]),
                    new ImageCriteriaResidual(
                        Negate: true,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria
                                {
                                    Language = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Values = ["en"]
                                            }
                                        ]
                                    }
                                },
                                ImageCriteriaResidual.EvaluationKind.Gallery)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SearchHitDto
                {
                    Show = new ShowDto { Id = 82, Name = "Lost" },
                },
            ]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images = [new ImageDto
                            {
                                Type = "poster",
                                Resolutions = new ImageResolutionsDto
                                {
                                    Original = new ImageResolutionDto
                                    {
                                        Url = "https://static.tvmaze.com/poster.jpg",
                                        Width = 680,
                                        Height = 1000,
                                    },
                                },
                            },
                        ]
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_OmitsCandidateWithWarning_WhenImageGalleryIsUnavailable()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new FilterPlanNode
            {
                Id = 2,
                Contributes = "locally verified show candidates",
                Child = new FetchPlanNode
                {
                    Id = 1,
                    Contributes = "unpaged show-search candidates",
                    AnchorKind = SearchAnchorKind.Title,
                    Seed = new SearchSeed.FuzzyShow(["Lost"], IsAka: false)
                },
                HitFilters =
                [
                    new LocalFilter
                    {
                        Kind = LocalFilterKind.Title,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Lost"]
                    }
                ],
                AkaFilters = [],
                RelationshipResiduals = [],
                ImageResiduals =
                [
                    new ImageCriteriaResidual(
                        Negate: false,
                        [
                            new ImageCriteriaResidual.Alternative(
                                new ImageCriteria
                                {
                                    Type = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Values = ["banner"]
                                            }
                                        ]
                                    }
                                },
                                ImageCriteriaResidual.EvaluationKind.Gallery)
                        ])
                ]
            },
            Dispositions = []
        };
        _client
            .Setup(client => client.SearchShowsAsync("Lost", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new() { Show = new() { Id = 82, Name = "Lost" }, },]);
        _client
            .Setup(client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto
            {
                Id = 82,
                Name = "Lost",
                Embedded = new ShowEmbeddedDto
                {
                    Images = null
                }
            });

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Has.One.EqualTo(SearchWarnings.ImageVerificationUnavailable));
        }

        _client.Verify(
            client => client.GetShowWithEmbedsAsync(
                82,
                It.Is<IReadOnlyList<string>>(embeds => embeds.Count > 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_NoWarnings_WhenPlanDiagnosticIsKnownEmpty()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new UnionPlanNode { Id = 1, Contributes = "known-empty show candidates", Children = [] },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyWithoutQuerying_WhenPlanIsKnownEmpty()
    {
        // Arrange
        var plan = new QueryPlan
        {
            Query = new ContentHierarchyItemCriteria(),
            Metadata = new MetadataCriteria(),
            MaximumResults = int.MaxValue,
            Root = new UnionPlanNode { Id = 1, Contributes = "known-empty show candidates", Children = [] },
            Dispositions = []
        };

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }

        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ExecuteAsync_StopsAtPageBudget_WhenBrowseHasNoSurvivors()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShowDto { Id = 1, Name = "Show", Language = "English" }]);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Language = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Japanese" }]
                        }
                    }
                ]
            }
        };
        var plan = new SearchPlanner(NullLogger<SearchPlanner>.Instance).Plan(query, 25);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        _client.Verify(client => client.GetShowsPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
        _client.Verify(client => client.GetShowsPageAsync(4, It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_PreservesCandidatesWithoutWarnings_WhenMetadataOperatorIsUnsupported(bool negate)
    {
        // Arrange
        _client.Setup(client => client.SearchShowsAsync("Game of Thrones", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SearchHitDto { Show = GameOfThrones }]);
        var criterion = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.Regex,
            Value = "English",
            Negate = negate
        };
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
                            And = [new PropertyCriterion<string> { Value = "Game of Thrones" }]
                        },
                        Language = new CriteriaSet<PropertyCriterion<string>> { And = [criterion] }
                    }
                ]
            }
        };
        var plan = new SearchPlanner(NullLogger<SearchPlanner>.Instance).Plan(query, 25);

        // Act
        var result = await _executor.ExecuteAsync(plan, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches.Select(match => match.ExternalId), Is.EqualTo(["82"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(plan.Query, Is.SameAs(query));
            Assert.That(plan.Dispositions.Any(disposition =>
                ReferenceEquals(disposition.OperatorsAndValues, criterion) &&
                disposition.Disposition == "pass-through-residual"), Is.True);
        }
    }

    [Test]
    public async Task ContinueSearchAsync_PreservesFilteredLookahead_WhenBrowsePagesContinueAndReplay()
    {
        // Arrange
        var pages = new List<int>();
        _client.Setup(value => value.GetShowsPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((int page, CancellationToken _) => pages.Add(page))
            .ReturnsAsync((int page, CancellationToken _) => page switch
            {
                0 => [new ShowDto { Id = 1, Name = "discard" }, new ShowDto { Id = 2, Name = "keep two" }],
                1 => [new ShowDto { Id = 3, Name = "keep three" }, new ShowDto { Id = 4, Name = "keep four" }],
                _ => null
            });
        var planner = new Mock<ISearchPlanner>();
        planner.Setup(value => value.Plan(It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<int>()))
            .Returns((ContentHierarchyItemCriteria query, int maximum) => new QueryPlan
            {
                Query = query,
                Metadata = new MetadataCriteria(),
                MaximumResults = maximum,
                Dispositions = [],
                Root = new FilterPlanNode
                {
                    Id = 2,
                    Contributes = "verified",
                    Child = new FetchPlanNode
                    {
                        Id = 1,
                        Contributes = "browse",
                        AnchorKind = SearchAnchorKind.Browse,
                        Seed = new SearchSeed.Browse()
                    },
                    HitFilters = [new LocalFilter
                    {
                        Kind = LocalFilterKind.Title, Operator = CriteriaValueOperator.ContainsString,
                        StringValues = ["keep"]
                    }],
                    AkaFilters = [],
                    RelationshipResiduals = [],
                    ImageResiduals = []
                }
            });
        var searcher = new ContentMetadataSearcher(_client.Object, planner.Object, _executor,
            NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("tv", new(), 2, "", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("tv", new(), 2, "", first.Continuation).ConfigureAwait(false);
        var retry = await searcher.ContinueSearchAsync("tv", new(), 2, "", first.Continuation).ConfigureAwait(false);
        var independent = await searcher.ContinueSearchAsync("tv", new(), 2, "", null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["2", "3"]));
            Assert.That(second.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["4"]));
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(retry.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["4"]));
            Assert.That(independent.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["2", "3"]));
            Assert.That(pages, Is.EqualTo([0, 1, 1, 2, 1, 2, 0, 1]));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContinueSearchAsync_ReplaysLastSourcePage_WhenFilteredScanBudgetEnds(bool emptyPages)
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(It.IsIn(0, 1, 2, 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyPages ? [] : [new ShowDto { Id = 1, Name = "No image" }]);
        _client.Setup(client => client.GetShowWithEmbedsAsync(
                1, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShowDto { Id = 1, Name = "No image", Embedded = new ShowEmbeddedDto { Images = [] } });
        _client.SetupSequence(client => client.GetShowsPageAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyPages ? [] : [new ShowDto { Id = 1, Name = "No image" }])
            .ReturnsAsync([new ShowDto
            {
                Id = 4,
                Name = "New on last page",
                Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/new.jpg" }
            }]);
        _client.Setup(client => client.GetShowsPageAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShowDto
            {
                Id = 5,
                Name = "Matched",
                Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/matched.jpg" }
            }]);
        _client.Setup(client => client.GetShowsPageAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShowDto>?)null);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And = [new MetadataCriteria { Images = new CollectionCriteria<ImageCriteria> { Some = [[new()]] } }]
            }
        };
        var searcher = new ContentMetadataSearcher(_client.Object,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance), _executor,
            NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("tv", query, 2, "", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("tv", query, 2, "", first.Continuation).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches, Is.Empty);
            Assert.That(first.Page.Warnings, Is.Empty);
            Assert.That(first.Page.PageInfo.HasMore, Is.True);
            Assert.That(second.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["4", "5"]));
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(_client.Invocations.Where(value => value.Method.Name == "GetShowsPageAsync")
                .Select(value => value.Arguments[0]), Is.EqualTo([0, 1, 2, 3, 3, 4, 5]));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ContinueSearchAsync_RetriesUnverifiedBrowseCandidate_WhenLaterPageReachesExhaustion(
        bool requestFailed)
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShowDto { Id = 1, Name = "First" }]);
        _client.Setup(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShowDto
            {
                Id = 2,
                Name = "Second",
                Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/second.jpg" }
            }]);
        _client.Setup(client => client.GetShowsPageAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShowDto>?)null);
        var recoveredShow = new ShowDto
        {
            Id = 1,
            Name = "First",
            Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/first.jpg" },
            Embedded = new ShowEmbeddedDto { Images = [] }
        };
        _client.SetupSequence(client => client.GetShowWithEmbedsAsync(
                1, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(requestFailed
                ? Task.FromException<ShowDto?>(new HttpRequestException("Temporary detail failure"))
                : Task.FromResult<ShowDto?>(null))
            .ReturnsAsync(recoveredShow)
            .ReturnsAsync(recoveredShow);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And = [new MetadataCriteria { Images = new CollectionCriteria<ImageCriteria> { Some = [[new()]] } }]
            }
        };
        var searcher = new ContentMetadataSearcher(_client.Object,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance), _executor,
            NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("tv", query, 2, "", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("tv", query, 2, "", first.Continuation).ConfigureAwait(false);
        var independent = await searcher.ContinueSearchAsync("tv", query, 2, "", null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["2"]));
            Assert.That(first.Page.Warnings, Is.Not.Empty);
            Assert.That(first.Page.PageInfo.HasMore, Is.True);
            Assert.That(second.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["1"]));
            Assert.That(second.Page.Warnings, Is.Empty);
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(independent.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["1", "2"]));
            Assert.That(_client.Invocations.Where(value => value.Method.Name == "GetShowsPageAsync")
                .Select(value => value.Arguments[0]), Is.EqualTo([0, 1, 2, 0, 1, 2, 0, 1, 2]));
        }
    }

    [Test]
    public async Task ContinueSearchAsync_PreservesRetryAndLookahead_WhenUnverifiedPagePrecedesFullPage()
    {
        // Arrange
        _client.Setup(client => client.GetShowsPageAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShowDto { Id = 1, Name = "First" }]);
        _client.Setup(client => client.GetShowsPageAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShowDto { Id = 2, Name = "Second", Image = new() { Original = "https://static.tvmaze.com/2.jpg" } },
                new ShowDto { Id = 3, Name = "Third", Image = new() { Original = "https://static.tvmaze.com/3.jpg" } },
                new ShowDto { Id = 4, Name = "Fourth", Image = new() { Original = "https://static.tvmaze.com/4.jpg" } }
            ]);
        _client.Setup(client => client.GetShowsPageAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ShowDto>?)null);
        var recoveredShow = new ShowDto
        {
            Id = 1,
            Name = "First",
            Image = new ImageReferenceDto { Original = "https://static.tvmaze.com/first.jpg" },
            Embedded = new ShowEmbeddedDto { Images = [] }
        };
        _client.SetupSequence(client => client.GetShowWithEmbedsAsync(
                1, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShowDto?)null)
            .ReturnsAsync(recoveredShow)
            .ReturnsAsync(recoveredShow);
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And = [new MetadataCriteria { Images = new CollectionCriteria<ImageCriteria> { Some = [[new()]] } }]
            }
        };
        var searcher = new ContentMetadataSearcher(_client.Object,
            new SearchPlanner(NullLogger<SearchPlanner>.Instance), _executor,
            NullLogger<ContentMetadataSearcher>.Instance);

        // Act
        var first = await searcher.ContinueSearchAsync("tv", query, 2, "", null).ConfigureAwait(false);
        var second = await searcher.ContinueSearchAsync("tv", query, 2, "", first.Continuation).ConfigureAwait(false);
        var retry = await searcher.ContinueSearchAsync("tv", query, 2, "", first.Continuation).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["2", "3"]));
            Assert.That(first.Page.Warnings, Is.Not.Empty);
            Assert.That(first.Page.PageInfo.HasMore, Is.True);
            Assert.That(second.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["1", "4"]));
            Assert.That(second.Page.Warnings, Is.Empty);
            Assert.That(second.Page.PageInfo.HasMore, Is.False);
            Assert.That(retry.Page.Matches.Select(value => value.ExternalId), Is.EqualTo(["1", "4"]));
            Assert.That(_client.Invocations.Where(value => value.Method.Name == "GetShowsPageAsync")
                .Select(value => value.Arguments[0]), Is.EqualTo([0, 1, 0, 1, 2, 0, 1, 2]));
        }
    }
}
