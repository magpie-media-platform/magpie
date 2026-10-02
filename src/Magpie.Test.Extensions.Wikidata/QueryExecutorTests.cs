using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

using SubjectFilter = Magpie.Extensions.Wikidata.QueryEngine.SubjectFilter;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class QueryExecutorTests
{
    private static readonly IEnumerable<TestCaseData> CollectionKnownMatchDominatesMissingSupportCases =
    [
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.MpaaRatingPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                    }
                                }
                            }
                        ]
                    }
                },
                ["Q11"] = new EntityDto
                {
                    Id = "Q11",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                    }
                }
            },
            ResidualCollectionKind.ContentRating,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.ContentRatingBody,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["MPAA"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.MpaaRatingPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                }
                            }
                        ],
                        [Constants.BbfcRatingPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                    }
                                }
                            }
                        ]
                    }
                },
                ["Q10"] = new EntityDto
                {
                    Id = "Q10",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                    }
                }
            },
            ResidualCollectionKind.ContentRating,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.ContentRatingRating,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["PG"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.MpaaRatingPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                    }
                                }
                            }
                        ]
                    }
                },
                ["Q10"] = new EntityDto
                {
                    Id = "Q10",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                    }
                }
            },
            ResidualCollectionKind.ContentRating,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.ContentRatingRating,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["PG"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.AwardReceivedPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                    }
                                }
                            }
                        ]
                    }
                },
                ["Q10"] = new EntityDto
                {
                    Id = "Q10",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                    }
                }
            },
            ResidualCollectionKind.Award,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.AwardName,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Best Picture"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.AwardReceivedPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.ConferredByPropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                            }
                                        }
                                    ]
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.ConferredByPropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new { id = "Q21" })
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                },
                ["Q10"] = new EntityDto
                {
                    Id = "Q10",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "First Award" }
                    }
                },
                ["Q11"] = new EntityDto
                {
                    Id = "Q11",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Second Award" }
                    }
                },
                ["Q20"] = new EntityDto
                {
                    Id = "Q20",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                    }
                }
            },
            ResidualCollectionKind.Award,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.AwardBody,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Academy"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.AwardReceivedPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.ConferredByPropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                            }
                                        }
                                    ],
                                    [Constants.PointInTimePropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new
                                                    {
                                                        time = "+2023-00-00T00:00:00Z",
                                                        timezone = 0,
                                                        before = 0,
                                                        after = 0,
                                                        precision = 9,
                                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                    })
                                            }
                                        }
                                    ]
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.ConferredByPropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new { id = "Q21" })
                                            }
                                        }
                                    ],
                                    [Constants.PointInTimePropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new
                                                    {
                                                        time = "+2024-00-00T00:00:00Z",
                                                        timezone = 0,
                                                        before = 0,
                                                        after = 0,
                                                        precision = 9,
                                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                    })
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                },
                ["Q10"] = new EntityDto
                {
                    Id = "Q10",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                    }
                },
                ["Q20"] = new EntityDto
                {
                    Id = "Q20",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                    }
                }
            },
            ResidualCollectionKind.Award,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.AwardBody,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Academy"]
                    }
                ]
            }),
        new TestCaseData(
            new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
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
                                        Value = JsonSerializer.SerializeToElement(
                                            new { id = Constants.MovieQid })
                                    }
                                }
                            }
                        ],
                        [Constants.AwardReceivedPropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.PointInTimePropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new
                                                    {
                                                        time = "+2023-00-00T00:00:00Z",
                                                        timezone = 0,
                                                        before = 0,
                                                        after = 0,
                                                        precision = 9,
                                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                    })
                                            }
                                        }
                                    ]
                                }
                            },
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                    }
                                },
                                Qualifiers = new Dictionary<string, List<SnakDto>>
                                {
                                    [Constants.ConferredByPropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                            }
                                        }
                                    ],
                                    [Constants.PointInTimePropertyId] =
                                    [
                                        new SnakDto
                                        {
                                            SnakType = "value",
                                            DataValue = new DataValueDto
                                            {
                                                Value = JsonSerializer.SerializeToElement(new
                                                    {
                                                        time = "+2024-00-00T00:00:00Z",
                                                        timezone = 0,
                                                        before = 0,
                                                        after = 0,
                                                        precision = 9,
                                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                    })
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                },
                ["Q11"] = new EntityDto
                {
                    Id = "Q11",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                    }
                },
                ["Q20"] = new EntityDto
                {
                    Id = "Q20",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                    }
                }
            },
            ResidualCollectionKind.Award,
            new ResidualCollectionAlternative
            {
                Predicates =
                [
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.AwardName,
                        Operator = CriteriaValueOperator.In,
                        StringValues = [],
                        EntityQids = ["Q11"]
                    },
                    new ResidualCollectionPredicate
                    {
                        Field = ResidualCollectionField.AwardBody,
                        Operator = CriteriaValueOperator.Equals,
                        StringValues = ["Academy"]
                    }
                ]
            }),
    ];

    [TestCase(1, 0, 0, false, TestName = "Structured empty first page returns nothing")]
    [TestCase(2, 5, 5, false, TestName = "Structured partial page reports no more")]
    [TestCase(3, 0, 0, false, TestName = "Structured empty later page returns nothing")]
    [TestCase(2, 21, 20, true, TestName = "Structured overflow caps page and flags more")]
    public async Task SearchAsync_CapsPageAndReportsHasMore_WhenStructuredPaging(
        int page,
        int rowCount,
        int expectedHits,
        bool expectedHasMore)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, rowCount).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = page
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.PageInfo.HasMore, Is.EqualTo(expectedHasMore));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SearchAsync_ReturnsFailureWarning_WhenPagingBudgetIsExhausted(bool relationshipSeed)
    {
        // Arrange
        var rows = Enumerable.Range(1, 51).Select(index => new Dictionary<string, SparqlBindingDto>
        {
            ["item"] = new() { Value = "Q" + index }
        }).ToArray();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        var subject = new QueryExecutor(
            Mock.Of<IWikidataClient>(), cache.Object, NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Page = 1,
            Warnings = [],
            UsesLocalPaging = true,
            Filters = relationshipSeed
                ? [new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives = [new SubjectFilter { PropertyIds = ["P161"], Qids = ["Q42"] }]
                }]
                : [],
            ResidualFilters = relationshipSeed
                ? [new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships.Some[0]",
                    Hydration = ResidualHydration.None
                }]
                : []
        };
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default)
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.EqualTo(["Wikidata search failed; no Wikidata results were returned."]));
        }

        cache.Verify(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()), Times.Exactly(20));
    }

    [Test]
    public async Task SearchAsync_MapsRowsToHits_WhenStructuredWindow()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Type = "uri", Value = "http://www.wikidata.org/entity/Q83495" },
                ["type"] = new SparqlBindingDto { Type = "uri", Value = "http://www.wikidata.org/entity/Q11424" },
                ["itemLabel"] = new SparqlBindingDto { Type = "literal", Value = "The Matrix" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits.Single().Qid, Is.EqualTo("Q83495"));
            Assert.That(result.Hits.Single().TypeQid, Is.EqualTo("Q11424"));
            Assert.That(result.Hits.Single().Label, Is.EqualTo("The Matrix"));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task SearchAsync_UsesCallerBoundaryAndLookahead_WhenMaximumResults()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 5).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        SparqlRequest? sparqlRequest = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest request,
                RequestContext _,
                CancellationToken _) => sparqlRequest = request)
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? _) => new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            MaximumResults = 2
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(2));
            Assert.That(result.PageInfo.HasMore, Is.True);
            Assert.That(sparqlRequest?.QueryText, Does.Contain("LIMIT 3"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsTheOffsetPage_WhenLocalSecondPage()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 5).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 2,
            MaximumResults = 2,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q3", "Q4"]));
            Assert.That(result.PageInfo.HasMore, Is.True);
        }
    }

    [TestCase(1, 49, false, 0, 20, true, TestName = "Title first page below window")]
    [TestCase(1, 50, false, 0, 20, true, TestName = "Title first page at window")]
    [TestCase(1, 50, true, 0, 20, true, TestName = "Title first page uses lookahead")]
    [TestCase(3, 50, false, 0, 10, false, TestName = "Title last page at window")]
    [TestCase(3, 50, true, 1, 11, false, TestName = "Title later page continues window")]
    public async Task SearchAsync_ContinuesUntilLogicalBoundary_WhenTitlePaging(
        int page,
        int rowCount,
        bool exceedsWindow,
        int nextRowCount,
        int expectedHits,
        bool expectedHasMore)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> sentinelRows = exceedsWindow
            ?
            [
                new()
                {
                    ["item"] = new SparqlBindingDto { Value = "Q9999" },
                    ["itemLabel"] = new SparqlBindingDto { Value = "Sentinel" },
                    ["titleWindowExceeded"] = new SparqlBindingDto { Value = "1" }
                }
            ]
            : [];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, rowCount).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }),
            .. sentinelRows
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> nextRows =
        [
            .. Enumerable.Range(rowCount + 1, nextRowCount).Select(index =>
                new Dictionary<string, SparqlBindingDto>
                {
                    ["item"] = new SparqlBindingDto { Value = "Q" + index },
                    ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                    ["type"] = new SparqlBindingDto { Value = "Q11424" }
                })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(nextRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = page,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
            Assert.That(result.PageInfo.HasMore, Is.EqualTo(expectedHasMore));
            Assert.That(result.Warnings.Select(warning => warning), Has.None.Contains("more than 50"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsCandidateFromNextWindow_WhenTitleWindowExceeded()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 50).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }),
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q9999" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Sentinel" },
                ["titleWindowExceeded"] = new SparqlBindingDto { Value = "1" }
            }
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> nextRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q51" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 51" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(nextRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 3,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(11));
            Assert.That(result.Hits, Has.Some.Property(nameof(SearchHit.Qid)).EqualTo("Q51"));
            Assert.That(result.Warnings.Select(warning => warning), Has.None.Contains("more than 50"));
        }
    }

    [Test]
    public async Task SearchAsync_ReuseCandidateWindow_WhenOrdinaryTitlePages()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 30).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var firstPage = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };
        var secondPage = firstPage with { Page = 2 };

        // Act
        var first = await subject.SearchAsync(firstPage, context, default).ConfigureAwait(false);
        var second = await subject.SearchAsync(secondPage, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Hits, Has.Count.EqualTo(20));
            Assert.That(first.PageInfo.HasMore, Is.True);
            Assert.That(second.Hits, Has.Count.EqualTo(10));
            Assert.That(second.PageInfo.HasMore, Is.False);
            Assert.That(first.Warnings, Is.Empty);
            Assert.That(second.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsEmptyWithoutSearchingWdqs_WhenUnresolvedNameSelector()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync([]);
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Names = ["Nobody With This Name"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
        cache.Verify(
            value => value.GetSearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Never);
    }

    [Test]
    public async Task SearchAsync_ResolvesEveryNameWithoutFailing_WhenManyNameSelectors()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(["Q900"]);
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Names = [.. Enumerable.Range(1, 25).Select(index => "Actor " + index)]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Is.Empty);
        }

        cache.Verify(
            value => value.GetEntitySearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Exactly(25));
    }

    [Test]
    public async Task SearchAsync_PreservesPlanWarnings_WhenNameSelectorIsUnresolved()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync([]);
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Names = ["Nobody With This Name"]
                        }
                    ]
                }
            ],
            CriterionDispositions =
            [
                new CriterionDisposition
                {
                    Source = "CustomMetadata.And[0]",
                    Disposition = CriterionDispositionKind.ServerFilter,
                    Reason = "Planned as a server-side filter."
                }
            ],
            Warnings =
            [
                "planner warning"
            ],
            Page = 2
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.RequestedPage, Is.EqualTo(2));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Is.EqualTo(["planner warning"]));
        }
    }

    [Test]
    public async Task SearchAsync_AppliesPreciseCandidateProjection_WhenPlannedCriteriaReturnAnUnprojectedHit()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> projectionRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["publishDate"] = new SparqlBindingDto
                {
                    Value = "1999-03-31T00:00:00Z|11|http://www.wikidata.org/entity/Q1985727|0"
                },
                ["durationSeconds"] = new SparqlBindingDto { Value = "8160" },
                ["genres"] = new SparqlBindingDto { Value = "science fictionaction film" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(projectionRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q1"] }]
                }
            ],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Single().PublishDate?.Precision, Is.EqualTo(TemporalPrecision.Day));
            Assert.That(
                result.Hits.Single().PublishDate?.Value,
                Is.EqualTo(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.Hits.Single().Duration, Is.EqualTo(TimeSpan.FromMinutes(136)));
            Assert.That(result.Hits.Single().Genres, Is.EqualTo(["science fiction", "action film"]));
        }
    }

    [Test]
    public async Task SearchAsync_BindsResolvedCandidatesIntoQuery_WhenTitleStartsWith()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q83495" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        SparqlRequest? sparqlRequest = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest request,
                RequestContext _,
                CancellationToken _) => sparqlRequest = request)
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(["Q83495"]);
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? _) => new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.StartsWithString,
                    Values = ["The Mat"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("wd:Q83495"));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("The Mat"));
        }
    }

    [Test]
    public async Task SearchAsync_BindsEveryResolvedCandidateIntoQuery_WhenTitleStartsWithAlternatives()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q999" },
                ["itemLabel"] = new SparqlBindingDto { Value = "El Matador" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        SparqlRequest? sparqlRequest = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest request,
                RequestContext _,
                CancellationToken _) => sparqlRequest = request)
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(["Q111"])
            .ReturnsAsync(["Q999"]);
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? _) => new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.StartsWithString,
                    Values = ["The Mat", "El Mat"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q999"]));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("wd:Q111"));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("wd:Q999"));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("The Mat"));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("El Mat"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase("Keanu Reeves")]
    [TestCase("Keanu Reeves", "", " ")]
    public async Task SearchAsync_BindsResolvedQids_WhenNameSelectorContainsBlankValues(params string[] names)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        SparqlRequest? sparqlRequest = null;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .Callback((
                SparqlRequest request,
                RequestContext _,
                CancellationToken _) => sparqlRequest = request)
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitySearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<string>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(["Q43416", "Q42"]);
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? _) => new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Names = names
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("Q43416"));
            Assert.That(sparqlRequest?.QueryText, Does.Contain("Q42"));
            Assert.That(sparqlRequest?.QueryText, Does.Not.Contain("Keanu Reeves"));
        }
    }

    [Test]
    public async Task SearchAsync_RetainsEligibleCandidate_WhenCrossBranchLookaheadSurvivesFiltering()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["titleWindowExceeded"] = new SparqlBindingDto { Value = "1" }
            }
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> nextRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(nextRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Qid, Is.EqualTo("Q1"));
            Assert.That(result.Warnings.Select(warning => warning), Has.None.Contains("more than 50"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsRateLimitWarningWithoutHits_WhenTerminalFailure()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["titleWindowExceeded"] = new SparqlBindingDto { Value = "1" }
            }
        ];
        var rateLimited = new HttpRequestException("Wikidata request remained rate limited.");
        rateLimited.Data["wikidata.rate-limited"] = true;
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ThrowsAsync(rateLimited);
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("rate limited"));
        }
    }

    [Test]
    public async Task SearchAsync_HitResidualFiltersCandidates_WhenHydrationIsNotRequired()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 2).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Movie 1$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Qid, Is.EqualTo("Q1"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsNoHits_WhenCandidateFilteringRemovesAllHits()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "OriginalTitle",
                    Hydration = ResidualHydration.Entity,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["movie"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_EvaluatorWarningsAreMerged_WhenResidualIsUnverifiable()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["["]
                    }
                }
            ],
            Warnings =
            [
                "planner warning"
            ],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Has.Count.EqualTo(2));
            Assert.That(result.Warnings.Select(warning => warning), Does.Contain("planner warning"));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.Some.Contains("could not fully verify"));
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("Title"));
        }
    }

    [Test]
    public async Task SearchAsync_ContinuesRelationshipWindows_WhenEarlierWindowsHaveNoSurvivors()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> firstSeedRows =
        [
            .. Enumerable.Range(1, 51).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> firstWindowRows =
        [
            .. Enumerable.Range(1, 50).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> survivorRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q51" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 51" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(firstSeedRows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(firstWindowRows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(survivorRows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(survivorRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Qids = ["Q100"]
                        }
                    ]
                }
            ],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["Movie 51"]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships.Some[0]",
                    Hydration = ResidualHydration.None
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q51"]));
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.None.Contains("50-candidate window"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsCandidatesFromLaterCandidateWindow_WhenSeedCandidateWindowIsFull()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> seedRows =
        [
            new() { ["item"] = new SparqlBindingDto { Value = "Q1" } },
            new() { ["item"] = new SparqlBindingDto { Value = "Q2" } }
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> firstCandidateRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q9999" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Sentinel" },
                ["titleWindowExceeded"] = new SparqlBindingDto { Value = "1" }
            }
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> secondCandidateRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 2" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(seedRows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(firstCandidateRows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(secondCandidateRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Qids = ["Q100"]
                        }
                    ]
                }
            ],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships.Some[0]",
                    Hydration = ResidualHydration.None
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "movie"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1", "Q2"]));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }

        cache.Verify(
            value => value.GetSearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task SearchAsync_ReturnsRateLimitWarningWithoutHits_WhenRelationshipSeed()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> seedRows =
        [
            new() { ["item"] = new SparqlBindingDto { Value = "Q1" } },
            new() { ["item"] = new SparqlBindingDto { Value = "Q1" } },
            new(),
            new() { ["item"] = new SparqlBindingDto { Value = "P31" } },
            new() { ["item"] = new SparqlBindingDto { Value = "not-a-qid" } }
        ];
        var rateLimited = new HttpRequestException("Wikidata request remained rate limited.");
        rateLimited.Data["wikidata.rate-limited"] = true;
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(seedRows, SearchCacheProvenance.Remote))
            .ThrowsAsync(rateLimited);
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.CastMemberPropertyId],
                            Qids = ["Q100"]
                        }
                    ]
                }
            ],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships.Some[0]",
                    Hydration = ResidualHydration.None
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("rate limited"));
        }
    }

    [TestCase(1, TestName = "Structured residual continues on first page")]
    [TestCase(3, TestName = "Structured residual continues on later page")]
    public async Task SearchAsync_ContinuesResidualWindows_WhenSourceHasMoreResults(int page)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            .. Enumerable.Range(1, 51).Select(index => new Dictionary<string, SparqlBindingDto>
            {
                ["item"] = new SparqlBindingDto { Value = "Q" + index },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie " + index },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            })
        ];
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> nextRows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q51" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 51" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.SetupSequence(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote))
            .ReturnsAsync(new SearchCacheResult(nextRows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = "Title",
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^No Match$"]
                    }
                }
            ],
            Warnings = [],
            Page = page,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo.HasMore, Is.False);
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.None.Contains("50-candidate window"));
        }
    }

    [Test]
    public async Task SearchAsync_UsesOneRequestPerStage_WhenResidualNeedsSupport()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        using var languageValue = JsonDocument.Parse("{\"id\":\"Q100\"}");
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q1" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
                    Claims = new Dictionary<string, List<StatementDto>>
                    {
                        [Constants.OriginalLanguagePropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = languageValue.RootElement.Clone()
                                    }
                                }
                            }
                        ]
                    }
                }
            });
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q100" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q100"] = new EntityDto
                {
                    Id = "Q100",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "English" }
                    }
                }
            });
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(1));
        cache.Verify(
            value => value.GetSearchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Once);
        cache.Verify(
            value => value.GetEntitiesAsync(
                It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q1" })),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Once);
        cache.Verify(
            value => value.GetEntitiesAsync(
                It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q100" })),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<SearchCacheProvenance>?>()),
            Times.Once);
    }

    [Test]
    public async Task SearchAsync_PassCacheProvenanceObservers_WhenHydrationRequests()
    {
        // Arrange
        Action<SearchCacheProvenance>? candidateObserver = null;
        Action<SearchCacheProvenance>? supportObserver = null;
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie 1" },
                ["type"] = new SparqlBindingDto { Value = "Q11424" }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        using var languageValue = JsonDocument.Parse("{\"id\":\"Q100\"}");
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q1" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Callback((
                IReadOnlyCollection<string> _,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? observer) => candidateObserver = observer)
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q1"] = new EntityDto
                {
                    Id = "Q1",
                    Claims = new Dictionary<string, List<StatementDto>>
                    {
                        [Constants.OriginalLanguagePropertyId] =
                        [
                            new StatementDto
                            {
                                Rank = "normal",
                                Mainsnak = new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = languageValue.RootElement.Clone()
                                    }
                                }
                            }
                        ]
                    }
                }
            });
        cache.Setup(value => value.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.SequenceEqual(new[] { "Q100" })),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Callback((
                IReadOnlyCollection<string> _,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? observer) => supportObserver = observer)
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q100"] = new EntityDto
                {
                    Id = "Q100",
                    Labels = new Dictionary<string, MonolingualTextDto>
                    {
                        ["en"] = new MonolingualTextDto { Language = "en", Value = "English" }
                    }
                }
            });
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(candidateObserver, Is.Not.Null);
            Assert.That(supportObserver, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetHierarchyAsync_NormalizesHierarchyImageFilename_WhenCommonsFilePathIri()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["child"] = new SparqlBindingDto { Value = "http://www.wikidata.org/entity/Q300" },
                ["childLabel"] = new SparqlBindingDto { Value = "Pilot" },
                ["ordinal"] = new SparqlBindingDto { Value = "1" },
                ["imageFilename"] = new SparqlBindingDto
                {
                    Value = "http://commons.wikimedia.org/wiki/Special:FilePath/Pilot%20Still.jpg"
                }
            }
        ];
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetHierarchyAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<HierarchyResult>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<HierarchyResult>> factory,
                CancellationToken token) => factory(token));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);

        // Act
        var result = await subject.GetHierarchyAsync(
            EntityKind.Episode,
            "Q100",
            "Q200",
            context,
            default).ConfigureAwait(false);
        var children = result.Rows.ToChildDefinitions(1, DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Rows.Single().ImageFilename, Is.EqualTo("Pilot Still.jpg"));
            Assert.That(
                children.Single().InitialContribution?.Images.Single().Url.AbsoluteUri,
                Is.EqualTo("https://commons.wikimedia.org/wiki/Special:Redirect/file/Pilot%20Still.jpg"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsWarningWithoutHits_WhenRateLimitedWindow()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var rateLimited = new HttpRequestException("Wikidata request remained rate limited.");
        rateLimited.Data["wikidata.rate-limited"] = true;
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(rateLimited);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote);
            });
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("rate limited"));
            Assert.That(result.PageInfo.HasMore, Is.False);
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsTimeoutWarning_WhenProviderCancellation()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? _) => new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("timed out"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsFailureWarning_WhenHttpFailure()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ThrowsAsync(new HttpRequestException("Wikidata returned 502."));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings.Select(warning => warning), Has.Some.Contains("search failed"));
        }
    }

    [Test]
    public async Task SearchAsync_SkipsTheClientCall_WhenCachedFill()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> _,
                CancellationToken _,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Cache);
                return Task.FromResult(new SearchCacheResult([], SearchCacheProvenance.Cache));
            });
        var client = new Mock<IWikidataClient>(MockBehavior.Strict);
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = false
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(client.Invocations, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_CallsTheClientOnce_WhenRemoteFill()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var client = new Mock<IWikidataClient>();
        client.Setup(value => value.ExecuteSparqlAsync(
            It.IsAny<SparqlRequest>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns(async (
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> factory,
                CancellationToken token,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.Remote);
                return new SearchCacheResult(
                    await factory(token).ConfigureAwait(false),
                    SearchCacheProvenance.Remote);
            });
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = false
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(client.Invocations, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_SkipsTheClientCall_WhenInFlightCacheEntry()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>> _,
                CancellationToken _,
                Action<SearchCacheProvenance>? provenance) =>
            {
                provenance?.Invoke(SearchCacheProvenance.InFlight);
                return Task.FromResult(new SearchCacheResult([], SearchCacheProvenance.InFlight));
            });
        var client = new Mock<IWikidataClient>(MockBehavior.Strict);
        IQueryExecutor subject = new QueryExecutor(
            client.Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = false
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(client.Invocations, Is.Empty);
        }
    }

    [TestCase(QueryStrategy.Empty, true)]
    [TestCase(QueryStrategy.Unsupported, false)]
    public async Task SearchAsync_ReportsSupportWithoutTouchingClientOrCache_WhenNonExecutablePlan(
        QueryStrategy strategy,
        bool supported)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            new Mock<IQueryCoordinator>(MockBehavior.Strict).Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = strategy,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Supported, Is.EqualTo(supported));
    }

    [Test]
    public async Task SearchAsync_ReturnsUnsupportedPageInfo_WhenUnsupportedStrategy()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            new Mock<IQueryCoordinator>(MockBehavior.Strict).Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Person,
            Strategy = QueryStrategy.Unsupported,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Supported, Is.False);
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.PageInfo, Is.EqualTo(ProviderSearchPageInfo.Unsupported));
        }
    }

    [Test]
    public async Task SearchAsync_OmitsQueryAndResultValues_WhenRedactedPlanSerializer()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            new Mock<IQueryCoordinator>(MockBehavior.Strict).Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Empty,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Values = ["unique-secret"],
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            Names = ["private-name"],
                            Qids = ["Q999"]
                        }
                    ]
                }
            ],
            CriterionDispositions =
            [
                new CriterionDisposition
                {
                    Source = "Title[0]",
                    Disposition = CriterionDispositionKind.Refused,
                    Reason = "secret-reason"
                }
            ],
            Warnings =
            [
                "secret-warning"
            ],
            Page = 1,
            OrdinaryTerm = "ordinary-secret",
            TitleCandidateQids = ["Q998"]
        };
        var prepared = plan.PrepareDiagnostics();

        // Act
        var result = await subject.SearchAsync(prepared, context, default).ConfigureAwait(false);
        var planJson = prepared.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Fingerprint, Has.Length.EqualTo(64));
            Assert.That(planJson, Does.Contain("\"serverFilterCount\":1"));
            Assert.That(planJson, Does.Contain("\"criterionDispositionCount\":1"));
            Assert.That(planJson, Does.Not.Contain("unique-secret"));
            Assert.That(planJson, Does.Not.Contain("private-name"));
            Assert.That(planJson, Does.Not.Contain("Q999"));
            Assert.That(planJson, Does.Not.Contain("Q998"));
            Assert.That(planJson, Does.Not.Contain("secret-reason"));
            Assert.That(planJson, Does.Not.Contain("secret-warning"));
            Assert.That(planJson, Does.Not.Contain("ordinary-secret"));
        }
    }

    [Test]
    public async Task SearchAsync_PropagatesCancellation_WhenCallerCancels()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ThrowsAsync(new OperationCanceledException());
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            ProjectCandidateDetails = false
        }.PrepareDiagnostics();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        // Act
        var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
            await subject.SearchAsync(plan, context, cancellation.Token).ConfigureAwait(false));

        // Assert
        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task SearchAsync_ReturnsFirstAcceptedHit_WhenDuplicateQidAndUnacceptedType()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Office" },
                ["type"] = new SparqlBindingDto { Value = Constants.TelevisionSeriesQid }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Office duplicate" },
                ["type"] = new SparqlBindingDto { Value = Constants.TelevisionSeriesQid }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q3" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Office Space" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Qid, Is.EqualTo("Q1"));
            Assert.That(result.Hits[0].Label, Is.EqualTo("The Office"));
        }
    }

    [Test]
    public async Task SearchAsync_RejectsHit_WhenQidFallbackLabel()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q12345" },
                ["itemLabel"] = new SparqlBindingDto { Value = "Q12345" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q83495" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 1,
            OrdinaryTerm = "matrix"
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Has.Count.EqualTo(1));
            Assert.That(result.Hits[0].Qid, Is.EqualTo("Q83495"));
        }
    }

    [TestCase(new[] { "matrix" }, 1, TestName = "Title filter keeps the matching hit")]
    [TestCase(new[] { "gladiator" }, 0, TestName = "Title filter rejects the unmatched hit")]
    [TestCase(new string[0], 0, TestName = "Title filter without values drops the unverifiable hit")]
    public async Task SearchAsync_KeepsHitOnlyWhenTitleMatches_WhenTitleFilter(string[] values, int expectedHits)
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q83495" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.ContainsWord,
                    Values = values
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(expectedHits));
    }

    [Test]
    public async Task SearchAsync_KeepsHit_WhenNonTitleFilter()
    {
        // Arrange
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q83495" },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q1"] }]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = await subject.SearchAsync(plan, context, default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SearchAsync_ReturnsMatchingAlias_WhenRequestedAliasLanguageOutsideFallback()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Film" }
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            },
            Aliases = new Dictionary<string, List<MonolingualTextDto>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = [new MonolingualTextDto { Language = "en", Value = "Shared title" }],
                ["fr"] = [new MonolingualTextDto { Language = "fr", Value = "Shared title" }]
            }
        };
        var contexts = new List<RequestContext>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext context,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                contexts.Add(context);
                var languages = context.FallbackLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var aliases = entity.Aliases!
                    .Where(pair => context.FetchAllLanguages || languages.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                IReadOnlyDictionary<string, EntityDto> resolved =
                    qids.Contains("Q1", StringComparer.OrdinalIgnoreCase)
                        ? new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Q1"] = entity with { Aliases = aliases }
                        }
                        : new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>().Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = "AlternateTexts",
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.AlternateText,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AlternateTextLanguage,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["fr"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AlternateTextText,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Shared title"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var requestContext = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.SearchAsync(plan, requestContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(item => item.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(contexts, Has.Count.EqualTo(1));
            Assert.That(contexts[0].FetchAllLanguages, Is.False);
            Assert.That(contexts[0].FallbackLanguages, Is.EqualTo(["en", "fr"]));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsMatchFromAllLanguages_WhenAliasTextWithoutLanguageConstraint()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Film" }
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            },
            Aliases = new Dictionary<string, List<MonolingualTextDto>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ja"] = [new MonolingualTextDto { Language = "ja", Value = "Matorikkusu" }]
            }
        };
        var contexts = new List<RequestContext>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext context,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                contexts.Add(context);
                var languages = context.FallbackLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var aliases = entity.Aliases!
                    .Where(pair => context.FetchAllLanguages || languages.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                IReadOnlyDictionary<string, EntityDto> resolved =
                    qids.Contains("Q1", StringComparer.OrdinalIgnoreCase)
                        ? new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Q1"] = entity with { Aliases = aliases }
                        }
                        : new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>().Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = "AlternateTexts",
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.AlternateText,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AlternateTextText,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Matorikkusu"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var requestContext = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };

        // Act
        var result = await subject.SearchAsync(plan, requestContext, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(item => item.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(contexts, Has.Count.EqualTo(1));
            Assert.That(contexts[0].FetchAllLanguages, Is.True);
        }
    }

    [Test]
    public async Task SearchAsync_RequestsEveryDistinctQid_WhenSupportBeyondCandidateWindow()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Multilingual film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        .. Enumerable.Range(101, 51).Select(index => new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q" + index })
                                }
                            }
                        }),
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q101" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Titre francais$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0], Is.EqualTo(["Q1"]));
            Assert.That(requests[1], Has.Count.EqualTo(51));
            Assert.That(requests[1], Does.Contain("Q151"));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsMatchesWithoutHydration_WhenTitleRegex()
    {
        // Arrange
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = new Dictionary<string, EntityDto>();
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Arrival" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.Title.ToString(),
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^the.*ix$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Is.Empty);
        }
    }

    [TestCase(Constants.EndedProductionStatus, "Q1")]
    [TestCase("Continuing", "Q2")]
    public async Task SearchAsync_ReturnsMatchingState_WhenProductionStatus(string value, string expectedQid)
    {
        // Arrange
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = new Dictionary<string, EntityDto>();
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Ended show" },
                ["endDate"] = new SparqlBindingDto
                {
                    Value = "2020-01-01T00:00:00Z|11|http://www.wikidata.org/entity/Q1985727|0"
                }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Continuing show" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.ProductionStatus,
                    Source = "ProductionStatus",
                    Hydration = ResidualHydration.None,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = [value]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo([expectedQid]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_HydratesMainEntitiesOnly_WhenOriginalTitleRegex()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Amelie" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Arrival" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "Le Fabuleux Destin d'Amelie Poulain", language = "en" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "Arrival", language = "en" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["Amelie.*Poulain$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0], Is.EqualTo(["Q1", "Q2"]));
        }
    }

    [Test]
    public async Task SearchAsync_UsesHydratedOriginalLanguage_WhenOriginalTitleRegex()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "French film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q150" })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "English title", language = "en" })
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "Titre francais", language = "fr" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q150"] = new EntityDto
            {
                Id = "Q150",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "French" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P218"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("fr")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Titre francais$"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1], Is.EqualTo(["Q150"]));
        }
    }

    [Test]
    public async Task SearchAsync_MatchesQidCodeAndNameWithConditionalSupportHydration_WhenLanguageResidual()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "English" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "French" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q3" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Japanese" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q1860" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q150" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q3"] = new EntityDto
            {
                Id = "Q3",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q188" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q1860"] = new EntityDto
            {
                Id = "Q1860",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "English" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P218"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("en")
                                }
                            }
                        }
                    ]
                }
            },
            ["Q150"] = new EntityDto
            {
                Id = "Q150",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "French" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P218"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("fr")
                                }
                            }
                        }
                    ]
                }
            },
            ["Q188"] = new EntityDto
            {
                Id = "Q188",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Japanese" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P218"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("ja")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.In,
                        Negate = false,
                        Values = ["Q1860", "fr", "Japanese"]
                    },
                    LanguageQids = ["Q1860"],
                    LanguageCodes = ["fr"],
                    LanguageNames = ["Japanese"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1", "Q2", "Q3"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0], Is.EqualTo(["Q1", "Q2", "Q3"]));
            Assert.That(requests[1], Is.EqualTo(["Q150", "Q188"]));
        }
    }

    [Test]
    public async Task SearchAsync_RetainsUnverifiableCandidate_WhenMissingLanguageClaim()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "Original", language = "en" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("Language"));
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsNonmatchingSubjects_WhenNegatedFuzzyRelationshipSubject()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Smith film" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Jones film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Alicia Jones" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = true,
                                            Values = ["smith"]
                                        },
                                        Names = ["smith"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q2"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task SearchAsync_ReturnsCandidatesWithoutMatchingRelationship_WhenOuterNegatedRelationship()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Smith film" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Jones film" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q3" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "No cast" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q3"] = new EntityDto
            {
                Id = "Q3",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "No cast", language = "en" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Alicia Jones" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["smith"]
                                        },
                                        Names = ["smith"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q2", "Q3"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task SearchAsync_RejectsAliasMatch_WhenOuterNegatedStudioAlias()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Alias match" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "No match" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P272"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P272"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Acme Holdings" }
                },
                Aliases = new Dictionary<string, List<MonolingualTextDto>>
                {
                    ["en"] = [new MonolingualTextDto { Language = "en", Value = "Dream Factory" }]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Practical Pictures" }
                },
                Aliases = new Dictionary<string, List<MonolingualTextDto>>
                {
                    ["en"] = [new MonolingualTextDto { Language = "en", Value = "Practical Films" }]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P272"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["Dream"]
                                        },
                                        Names = ["Dream"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q2"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_UsesFirstAvailableSubjectLabel_WhenOuterNegatedRelationship()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P272"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["es"] = new MonolingualTextDto { Language = "es", Value = "Fabrica de Suenos" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P272"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["Suenos"]
                                        },
                                        Names = ["Suenos"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "de",
                FallbackLanguages = ["de", "en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_RequireOneRelationshipToMatchSubjectAndQualifier_WhenRelationshipFacets()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Split facets" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Same relationship" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P4633"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement("Villain")
                                        }
                                    }
                                ]
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P4633"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement("Hero")
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q12" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P4633"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement("Hero")
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Alicia Jones" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q12"] = new EntityDto
            {
                Id = "Q12",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Robin Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["smith"]
                                        },
                                        Names = ["smith"]
                                    }
                                ],
                                Qualifiers =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Negate = false,
                                            Values = ["Hero"]
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q2"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1], Is.EquivalentTo(["Q10", "Q11", "Q12"]));
        }
    }

    [Test]
    public async Task SearchAsync_UsesRequestLanguageWithoutSubjectLabel_WhenCharacterQualifier()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P453"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["de"] = new MonolingualTextDto { Language = "de", Value = "Held" },
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Hero" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Negate = false,
                                            Values = ["Q10"]
                                        },
                                        Qids = ["Q10"]
                                    }
                                ],
                                Qualifiers =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Negate = false,
                                            Values = ["Held"]
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "de",
                FallbackLanguages = ["de", "en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Is.EqualTo(["Q20"]));
        }
    }

    [TestCase(CriteriaValueOperator.IsNull, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, true)]
    public async Task SearchAsync_SettlesWithoutSupportHydration_WhenCharacterQualifierPresence(
        CriteriaValueOperator valueOperator,
        bool expectedMatch)
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P453"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Qualifiers =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = valueOperator,
                                            Negate = false,
                                            Values = []
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Any(), Is.EqualTo(expectedMatch));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_MatchOneSubject_WhenRelationshipSubjectAndCriteria()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "First" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Second" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q3" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Third" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q3"] = new EntityDto
            {
                Id = "Q3",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q12" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "John Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q12"] = new EntityDto
            {
                Id = "Q12",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Jones" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["Smith"]
                                        },
                                        Names = ["Smith"]
                                    },
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = true,
                                            Values = ["John"]
                                        },
                                        Names = ["John"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q2"]));
    }

    [Test]
    public async Task SearchAsync_MatchesExactSubjectClaim_WhenRelationshipExternalIdGroup()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "First" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Second" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Smith" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ],
                    [Constants.ImdbPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("nm0000001")
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Alicia Jones" }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ],
                    [Constants.ImdbPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("nm0000002")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                ExternalIdGroups =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        ExternalIds =
                                        [
                                            new PropertyValue
                                            {
                                                PropertyId = Constants.ImdbPropertyId,
                                                Value = "nm0000001"
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase("Q10")]
    [TestCase("q10")]
    public async Task SearchAsync_MatchesWithoutSubjectHydration_WhenRelationshipFacetQidVariesInCase(string facetQid)
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "First" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Second" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q2"] = new EntityDto
            {
                Id = "Q2",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                ExternalIdGroups =
                                [
                                    new ResidualRelationshipFacet { Qids = [facetQid] }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests.Single(), Is.EqualTo(["Q1", "Q2"]));
        }
    }

    [Test]
    public async Task SearchAsync_RemainsEvaluable_WhenRelationshipBeyondDisplayCap()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1000" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = Enumerable.Range(1, 31).ToDictionary(
            value => "Q" + value,
            value => new EntityDto
            {
                Id = "Q" + value,
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto
                    {
                        Language = "en",
                        Value = value == 31 ? "Target Person" : "Other " + value
                    }
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.HumanQid })
                                }
                            }
                        }
                    ]
                }
            });
        entities["Q1000"] = new EntityDto
        {
            Id = "Q1000",
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ],
                ["P161"] =
                [
                    .. Enumerable.Range(1, 31).Select(value => new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q" + value })
                            }
                        }
                    })
                ]
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.ContainsString,
                                            Negate = false,
                                            Values = ["Target"]
                                        },
                                        Names = ["Target"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1000"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Has.Count.EqualTo(31));
        }
    }

    [Test]
    public async Task SearchAsync_MatchesSameStatement_WhenSecondQualifierValue()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    ["P161"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                ["P4633"] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement("Hero")
                                        }
                                    },
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement("Villain")
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = "Relationships",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new ResidualRelationshipGroup
                    {
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = ["P161"],
                                Qualifiers =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = new ResidualStringCriterion
                                        {
                                            Operator = CriteriaValueOperator.Equals,
                                            Negate = false,
                                            Values = ["Villain"]
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
    }

    [Test]
    public async Task SearchAsync_DoNotMatchAcrossItems_WhenContentRatingPredicates()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ],
                    [Constants.BbfcRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "15" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["MPAA"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["15"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task SearchAsync_MatchOrAndApplyOuterNegationLast_WhenContentRatingAlternatives()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ],
                    [Constants.BbfcRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "15" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["MPAA"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["R"]
                                    }
                                ]
                            },
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["BBFC"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["15"]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["MPAA"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["R"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
    }

    [Test]
    public async Task SearchAsync_MatchHydratedAwardAndQualifierBody_WhenAwardPredicates()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.AwardReceivedPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                [Constants.ConferredByPropertyId] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                        }
                                    }
                                ],
                                [Constants.PointInTimePropertyId] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new
                                                {
                                                    time = "+2024-00-00T00:00:00Z",
                                                    timezone = 0,
                                                    before = 0,
                                                    after = 0,
                                                    precision = 9,
                                                    calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                })
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Best Picture"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Academy"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardYear,
                                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                        IntegerValues = [2020]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Is.EquivalentTo(["Q10", "Q20"]));
        }
    }

    [Test]
    public async Task SearchAsync_RetainsCandidateWithWarning_WhenAwardBodyNeedsThirdBatch()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var award = new EntityDto
        {
            Id = "Q10",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
            }
        } with
        {
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.ConferredByPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                            }
                        }
                    }
                ]
            }
        };
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.AwardReceivedPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = award,
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Academy"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("Award"));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1], Is.EqualTo(["Q10"]));
        }
    }

    [Test]
    public async Task SearchAsync_RetainsCandidateWithWarning_WhenMissingRatingSupport()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["PG"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1], Is.EquivalentTo(["Q10", "Q11"]));
        }
    }

    [Test]
    public async Task SearchAsync_UsesKnownYearAndQidFacets_WhenMissingAwardSupport()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.AwardReceivedPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                [Constants.PointInTimePropertyId] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new
                                                {
                                                    time = "+2020-00-00T00:00:00Z",
                                                    timezone = 0,
                                                    before = 0,
                                                    after = 0,
                                                    precision = 9,
                                                    calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                                })
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            }
        };
        var yearMismatchPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardYear,
                                        Operator = CriteriaValueOperator.Equals,
                                        IntegerValues = [2024]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var qidMismatchPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q11"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var possibleQidPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q10"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var possibleNamePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Best Picture"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var yearMismatchResult = await subject.SearchAsync(yearMismatchPlan, context, default).ConfigureAwait(false);
        var qidMismatchResult = await subject.SearchAsync(qidMismatchPlan, context, default).ConfigureAwait(false);
        var possibleQidResult = await subject.SearchAsync(possibleQidPlan, context, default).ConfigureAwait(false);
        var possibleNameResult = await subject.SearchAsync(possibleNamePlan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(yearMismatchResult.Hits, Is.Empty);
            Assert.That(yearMismatchResult.Warnings, Is.Empty);
            Assert.That(qidMismatchResult.Hits, Is.Empty);
            Assert.That(qidMismatchResult.Warnings, Is.Empty);
            Assert.That(possibleQidResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(possibleQidResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(possibleNameResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(possibleNameResult.Warnings, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_UsesKnownBodyAndQidFacets_WhenMissingRatingSupport()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var bodyMismatchPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["BBFC"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var qidMismatchPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q11"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var possibleQidPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q10"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var bodyMismatchResult = await subject.SearchAsync(bodyMismatchPlan, context, default).ConfigureAwait(false);
        var qidMismatchResult = await subject.SearchAsync(qidMismatchPlan, context, default).ConfigureAwait(false);
        var possibleQidResult = await subject.SearchAsync(possibleQidPlan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodyMismatchResult.Hits, Is.Empty);
            Assert.That(bodyMismatchResult.Warnings, Is.Empty);
            Assert.That(qidMismatchResult.Hits, Is.Empty);
            Assert.That(qidMismatchResult.Warnings, Is.Empty);
            Assert.That(possibleQidResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(possibleQidResult.Warnings, Has.Count.EqualTo(1));
        }
    }

    [TestCaseSource(nameof(CollectionKnownMatchDominatesMissingSupportCases))]
    public async Task SearchAsync_DominatesMissingSupportForSomeAndNone_WhenCollectionKnownMatch(
        Dictionary<string, EntityDto> entities,
        ResidualCollectionKind kind,
        ResidualCollectionAlternative alternative)
    {
        // Arrange
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var somePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = kind.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = kind,
                        OuterNegate = false,
                        Alternatives = [alternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var nonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = kind.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = kind,
                        OuterNegate = true,
                        Alternatives = [alternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var someResult = await subject.SearchAsync(somePlan, context, default).ConfigureAwait(false);
        var noneResult = await subject.SearchAsync(nonePlan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(someResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(someResult.Warnings, Is.Empty);
            Assert.That(noneResult.Hits, Is.Empty);
            Assert.That(noneResult.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_IsUnverifiableWithMissingPredecessor_WhenContentRatingQid()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            }
        };
        var possibleQid = new ResidualCollectionAlternative
        {
            Predicates =
            [
                new ResidualCollectionPredicate
                {
                    Field = ResidualCollectionField.ContentRatingRating,
                    Operator = CriteriaValueOperator.In,
                    StringValues = [],
                    EntityQids = ["Q11"]
                }
            ]
        };
        var somePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives = [possibleQid]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var nonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = true,
                        Alternatives = [possibleQid]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var mismatchPlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q99"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var someResult = await subject.SearchAsync(somePlan, context, default).ConfigureAwait(false);
        var noneResult = await subject.SearchAsync(nonePlan, context, default).ConfigureAwait(false);
        var mismatchResult = await subject.SearchAsync(mismatchPlan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(someResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(someResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(noneResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(noneResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(mismatchResult.Hits, Is.Empty);
            Assert.That(mismatchResult.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotRequestIrrelevantSystems_WhenContentRatingBody()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ],
                    [Constants.BbfcRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingBody,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["MPAA"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["PG"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Is.EqualTo(["Q10"]));
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotRequestIrrelevantConferrer_WhenAwardName()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.AwardReceivedPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            },
                            Qualifiers = new Dictionary<string, List<SnakDto>>
                            {
                                [Constants.ConferredByPropertyId] =
                                [
                                    new SnakDto
                                    {
                                        SnakType = "value",
                                        DataValue = new DataValueDto
                                        {
                                            Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Best Picture"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Is.EqualTo(["Q10"]));
        }
    }

    [Test]
    public async Task SearchAsync_WeakensOnlyQidAndBodyAwardSeeds_WhenTheSameYearPredecessorIsMissing()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var movie = new EntityDto
        {
            Id = "Q1",
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ],
                [Constants.AwardReceivedPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                            }
                        }
                    }
                ]
            }
        };
        movie.Claims![Constants.AwardReceivedPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                    }
                },
                Qualifiers = new Dictionary<string, List<SnakDto>>
                {
                    [Constants.PointInTimePropertyId] =
                    [
                        new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new
                                    {
                                        time = "+2024-00-00T00:00:00Z",
                                        timezone = 0,
                                        before = 0,
                                        after = 0,
                                        precision = 9,
                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                    })
                            }
                        }
                    ]
                }
            },
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                    }
                },
                Qualifiers = new Dictionary<string, List<SnakDto>>
                {
                    [Constants.ConferredByPropertyId] =
                    [
                        new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q20" })
                            }
                        }
                    ],
                    [Constants.PointInTimePropertyId] =
                    [
                        new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new
                                    {
                                        time = "+2024-00-00T00:00:00Z",
                                        timezone = 0,
                                        before = 0,
                                        after = 0,
                                        precision = 9,
                                        calendarmodel = "http://www.wikidata.org/entity/Q1985727"
                                    })
                            }
                        }
                    ]
                }
            }
        ];
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = movie,
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy" }
                }
            }
        };
        var qidAlternative = new ResidualCollectionAlternative
        {
            Predicates =
            [
                new ResidualCollectionPredicate
                {
                    Field = ResidualCollectionField.AwardName,
                    Operator = CriteriaValueOperator.In,
                    StringValues = [],
                    EntityQids = ["Q11"]
                }
            ]
        };
        var bodyAlternative = new ResidualCollectionAlternative
        {
            Predicates =
            [
                new ResidualCollectionPredicate
                {
                    Field = ResidualCollectionField.AwardBody,
                    Operator = CriteriaValueOperator.Equals,
                    StringValues = ["Academy"]
                }
            ]
        };
        var nameYearAlternative = new ResidualCollectionAlternative
        {
            Predicates =
            [
                new ResidualCollectionPredicate
                {
                    Field = ResidualCollectionField.AwardName,
                    Operator = CriteriaValueOperator.Equals,
                    StringValues = ["Best Picture"]
                },
                new ResidualCollectionPredicate
                {
                    Field = ResidualCollectionField.AwardYear,
                    Operator = CriteriaValueOperator.Equals,
                    IntegerValues = [2024]
                }
            ]
        };
        var qidSomePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives = [qidAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var qidNonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = true,
                        Alternatives = [qidAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var bodySomePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives = [bodyAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var bodyNonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = true,
                        Alternatives = [bodyAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var nameYearSomePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives = [nameYearAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var nameYearNonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = true,
                        Alternatives = [nameYearAlternative]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var context = new RequestContext
        {
            Language = "en",
            FallbackLanguages = ["en"],
            UserAgent = "Magpie/test"
        };
        var qidSomeResult = await subject.SearchAsync(qidSomePlan, context, default).ConfigureAwait(false);
        var qidNoneResult = await subject.SearchAsync(qidNonePlan, context, default).ConfigureAwait(false);
        var bodySomeResult = await subject.SearchAsync(bodySomePlan, context, default).ConfigureAwait(false);
        var bodyNoneResult = await subject.SearchAsync(bodyNonePlan, context, default).ConfigureAwait(false);
        var nameYearSomeResult = await subject.SearchAsync(nameYearSomePlan, context, default).ConfigureAwait(false);
        var nameYearNoneResult = await subject.SearchAsync(nameYearNonePlan, context, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(qidSomeResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(qidSomeResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(qidNoneResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(qidNoneResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(bodySomeResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(bodySomeResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(bodyNoneResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(bodyNoneResult.Warnings, Has.Count.EqualTo(1));
            Assert.That(nameYearSomeResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(nameYearSomeResult.Warnings, Is.Empty);
            Assert.That(nameYearNoneResult.Hits, Is.Empty);
            Assert.That(nameYearNoneResult.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_IgnoresStatementsAfterMappingCap_WhenAwardName()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var awardQids = Enumerable.Range(1, 31).Select(value => "Q" + (1000 + value)).ToArray();
        entities = awardQids.ToDictionary(
            qid => qid,
            qid => new EntityDto
            {
                Id = qid,
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto
                    {
                        Language = "en",
                        Value = qid == awardQids[30] ? "Target Award" : "Other " + qid
                    }
                }
            },
            StringComparer.OrdinalIgnoreCase);
        entities["Q1"] = new EntityDto
        {
            Id = "Q1",
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ],
                [Constants.AwardReceivedPropertyId] =
                [
                    .. awardQids.Select(awardQid => new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = awardQid })
                            }
                        }
                    })
                ]
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Target Award"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests[1], Has.Count.EqualTo(30));
            Assert.That(requests[1], Does.Not.Contain(awardQids[30]));
        }
    }

    [Test]
    public async Task SearchAsync_UsesHitLabelsWithoutHydration_WhenExcludedGenreName()
    {
        // Arrange
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = new Dictionary<string, EntityDto>();
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Science Film" },
                ["genres"] = new SparqlBindingDto { Value = "Science fiction film" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q2" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Horror Film" },
                ["genres"] = new SparqlBindingDto { Value = "Horror" }
            },
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q3" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Genreless Film" },
                ["genres"] = new SparqlBindingDto { Value = string.Empty }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Genre.ToString(),
                    Hydration = ResidualHydration.None,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Genre,
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.GenreName,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Horror"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1", "Q3"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_MatchMappedCollections_WhenImageReferenceAndAliasPredicates()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var movie = new EntityDto
        {
            Id = "Q1",
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ],
                [Constants.OriginalTitlePropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(
                                    new { text = "Film", language = "en" })
                            }
                        }
                    }
                ]
            }
        } with
        {
            Aliases = new Dictionary<string, List<MonolingualTextDto>>
            {
                ["en"] = [new MonolingualTextDto { Language = "en", Value = "Alternate Film" }]
            }
        };
        movie.Claims![Constants.PosterPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("Poster.jpg")
                    }
                }
            }
        ];
        movie.Claims[Constants.OfficialWebsitePropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("https://example.com/film")
                    }
                }
            }
        ];
        entities = new Dictionary<string, EntityDto> { ["Q1"] = movie };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["poster"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageSource,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = [Constants.ProviderType]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageWidth,
                                        Operator = CriteriaValueOperator.IsNull,
                                        IntegerValues = []
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Reference.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Reference,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ReferenceKind,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["official"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ReferenceUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["https://example.com/film"]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.AlternateText.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.AlternateText,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AlternateTextText,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["Alternate Film"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_MatchFinalArtworkView_WhenShowPosterBackdropAndLogo()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.TelevisionSeriesQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Show" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.TelevisionSeriesQid })
                                }
                            }
                        }
                    ],
                    [Constants.PosterPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("Show Poster.jpg")
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
                                    Value = JsonSerializer.SerializeToElement("Show Backdrop.jpg")
                                }
                            }
                        }
                    ],
                    [Constants.LogoPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("Show Logo.svg")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["poster"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues =
                                        [
                                            "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
                                            Uri.EscapeDataString("Show Poster.jpg")
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["backdrop"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues =
                                        [
                                            "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
                                            Uri.EscapeDataString("Show Backdrop.jpg")
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["logo"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues =
                                        [
                                            "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
                                            Uri.EscapeDataString("Show Logo.svg")
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_MatchesFinalArtworkView_WhenMovieLogo()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.LogoPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("Movie Logo.svg")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = ["logo"]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues =
                                        [
                                            "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
                                            Uri.EscapeDataString("Movie Logo.svg")
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
    }

    [TestCase(true, "backdrop", "poster")]
    [TestCase(false, "poster", "backdrop")]
    public async Task SearchAsync_DependsOnP3383Presence_WhenMovieP18Type(
        bool hasPoster,
        string expectedType,
        string prohibitedType)
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var claims = new Dictionary<string, List<StatementDto>>
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
                            Value = JsonSerializer.SerializeToElement(
                                new { id = Constants.MovieQid })
                        }
                    }
                }
            ]
        };
        if (hasPoster)
        {
            claims[Constants.PosterPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement("Movie Poster.jpg")
                        }
                    }
                }
            ];
        }

        claims[Constants.ImagePropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("Movie Image.jpg")
                    }
                }
            }
        ];
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto { Id = "Q1", Claims = claims }
        };
        var imageUrl = "https://commons.wikimedia.org/wiki/Special:Redirect/file/" +
            Uri.EscapeDataString("Movie Image.jpg");
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = [expectedType]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = [imageUrl]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageType,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = [prohibitedType]
                                    },
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ImageUrl,
                                        Operator = CriteriaValueOperator.Equals,
                                        StringValues = [imageUrl]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
    }

    [Test]
    public async Task SearchAsync_DoNotCreateResidualItems_WhenInvalidArtworkFilenames()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.TelevisionSeriesQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Show" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.TelevisionSeriesQid })
                                }
                            }
                        }
                    ],
                    [Constants.PosterPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("Bad\nPoster.jpg")
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
                                    Value = JsonSerializer.SerializeToElement("Bad\tBackdrop.jpg")
                                }
                            }
                        }
                    ],
                    [Constants.LogoPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement("Bad\rLogo.svg")
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Image.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Image,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates = []
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_MatchesSecondItemAndNoneExcludesIt_WhenReferenceOrdinal()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var movie = new EntityDto
        {
            Id = "Q1",
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
                                Value = JsonSerializer.SerializeToElement(
                                    new { id = Constants.MovieQid })
                            }
                        }
                    }
                ],
                [Constants.OriginalTitlePropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(
                                    new { text = "Film", language = "en" })
                            }
                        }
                    }
                ]
            }
        };
        movie.Claims![Constants.OfficialWebsitePropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("https://example.com/first")
                    }
                }
            },
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("https://example.com/second")
                    }
                }
            }
        ];
        entities = new Dictionary<string, EntityDto> { ["Q1"] = movie };
        var somePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Reference.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Reference,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ReferenceOrdinal,
                                        Operator = CriteriaValueOperator.Equals,
                                        IntegerValues = [1]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };
        var nonePlan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Reference.ToString(),
                    Hydration = ResidualHydration.Entity,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Reference,
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ReferenceOrdinal,
                                        Operator = CriteriaValueOperator.Equals,
                                        IntegerValues = [1]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var someResult = await subject.SearchAsync(
            somePlan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);
        var noneResult = await subject.SearchAsync(
            nonePlan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(someResult.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(noneResult.Hits, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_DoesNotMatchMappedCollection_WhenSecondRatingQidInSystem()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        },
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "R" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = [],
                                        EntityQids = ["Q11"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_MatchesEitherFacet_WhenContentRatingMixedQidAndName()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = ["R"],
                                        EntityQids = ["Q10"]
                                    }
                                ]
                            }
                        ]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = ["PG"],
                                        EntityQids = ["Q999"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_AppliesNegationAfterOr_WhenContentRatingMixedQidAndName()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.MpaaRatingPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.ContentRating.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.ContentRating,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.ContentRatingRating,
                                        Operator = CriteriaValueOperator.In,
                                        Negate = true,
                                        EntityQids = ["Q10"],
                                        StringValues = ["R"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Hits, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_UsesRequestedLanguage_WhenAwardMixedQidAndName()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Film" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.AwardReceivedPropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                                }
                            }
                        }
                    ]
                }
            },
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" },
                    ["fr"] = new MonolingualTextDto { Language = "fr", Value = "Meilleur film" }
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Collection,
                    Source = ResidualCollectionKind.Award.ToString(),
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    CollectionGroup = new ResidualCollectionGroup
                    {
                        Kind = ResidualCollectionKind.Award,
                        OuterNegate = false,
                        Alternatives =
                        [
                            new ResidualCollectionAlternative
                            {
                                Predicates =
                                [
                                    new ResidualCollectionPredicate
                                    {
                                        Field = ResidualCollectionField.AwardName,
                                        Operator = CriteriaValueOperator.In,
                                        StringValues = ["Best Picture"],
                                        EntityQids = ["Q999"]
                                    }
                                ]
                            }
                        ]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "fr",
                FallbackLanguages = ["fr", "en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_RetainsSupersetWithOneWarning_WhenSupportHydrationFailure()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        int? failedRequestNumber = null;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                if (failedRequestNumber == requests.Count)
                {
                    return Task.FromException<IReadOnlyDictionary<string, EntityDto>>(
                        new HttpRequestException("Wikidata is unavailable."));
                }

                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalLanguagePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(new { id = "Q1860" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        failedRequestNumber = 2;
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = "Language",
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["English"]
                    },
                    LanguageNames = ["English"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("Language"));
            Assert.That(
                result.Warnings[0],
                Does.Contain("unverified candidates were retained"));
            Assert.That(requests, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task SearchAsync_RetainsSupersetAndSkipsSupport_WhenMainHydrationFailure()
    {
        // Arrange
        var requests = new List<IReadOnlyList<string>>();
        int? failedRequestNumber = null;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                if (failedRequestNumber == requests.Count)
                {
                    return Task.FromException<IReadOnlyDictionary<string, EntityDto>>(
                        new HttpRequestException("Wikidata is unavailable."));
                }

                IReadOnlyDictionary<string, EntityDto> resolved = new Dictionary<string, EntityDto>();
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        failedRequestNumber = 1;
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["matrix"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits.Select(hit => hit.Qid), Is.EqualTo(["Q1"]));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(
                result.Warnings[0],
                Does.Contain(nameof(ResidualTextField.OriginalTitle)));
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void SearchAsync_Propagates_WhenCallerCancellation()
    {
        // Arrange
        var requests = new List<IReadOnlyList<string>>();
        var hydrationCancels = false;
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken cancellationToken,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                if (hydrationCancels)
                {
                    return Task.FromException<IReadOnlyDictionary<string, EntityDto>>(
                        new OperationCanceledException(cancellationToken));
                }

                IReadOnlyDictionary<string, EntityDto> resolved = new Dictionary<string, EntityDto>();
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Movie" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        hydrationCancels = true;
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = [".*"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
            await subject.SearchAsync(
                plan,
                new RequestContext
                {
                    Language = "en",
                    FallbackLanguages = ["en"],
                    UserAgent = "Magpie/test"
                },
                cancellation.Token)
                .ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task SearchAsync_SkipsMainAndSupportHydration_WhenHitStageRejectsAll()
    {
        // Arrange
        var entities = new Dictionary<string, EntityDto>();
        var requests = new List<IReadOnlyList<string>>();
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .Returns((
                IReadOnlyCollection<string> qids,
                RequestContext _,
                CancellationToken _,
                Action<SearchCacheProvenance>? _) =>
            {
                requests.Add([.. qids]);
                IReadOnlyDictionary<string, EntityDto> resolved = qids
                    .Where(entities.ContainsKey)
                    .ToDictionary(qid => qid, qid => entities[qid], StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(resolved);
            });
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "The Matrix" }
            }
        ];
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        entities = new Dictionary<string, EntityDto>
        {
            ["Q1"] = new EntityDto
            {
                Id = "Q1",
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
                                    Value = JsonSerializer.SerializeToElement(
                                        new { id = Constants.MovieQid })
                                }
                            }
                        }
                    ],
                    [Constants.OriginalTitlePropertyId] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonSerializer.SerializeToElement(
                                        new { text = "The Matrix", language = "en" })
                                }
                            }
                        }
                    ]
                }
            }
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.Title.ToString(),
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["^Dune$"]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.OriginalTitle.ToString(),
                    Hydration = ResidualHydration.Entity,
                    TextField = ResidualTextField.OriginalTitle,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = [".*"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(requests, Is.Empty);
        }
    }

    [Test]
    public async Task SearchAsync_RemovesUnverifiableWarning_WhenLaterRejection()
    {
        // Arrange
        IReadOnlyList<Dictionary<string, SparqlBindingDto>> rows =
        [
            new()
            {
                ["item"] = new SparqlBindingDto { Value = "Q1" },
                ["type"] = new SparqlBindingDto { Value = Constants.MovieQid },
                ["itemLabel"] = new SparqlBindingDto { Value = "Ended show" },
                ["endDate"] = new SparqlBindingDto
                {
                    Value = "2020-01-01T00:00:00Z|11|http://www.wikidata.org/entity/Q1985727|0"
                }
            }
        ];
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetSearchAsync(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<Dictionary<string, SparqlBindingDto>>>>>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Action<SearchCacheProvenance>?>()))
            .ReturnsAsync(new SearchCacheResult(rows, SearchCacheProvenance.Remote));
        IQueryExecutor subject = new QueryExecutor(
            new Mock<IWikidataClient>(MockBehavior.Strict).Object,
            cache.Object,
            NullLogger<QueryExecutor>.Instance);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            ResidualFilters =
            [
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.Text,
                    Source = ResidualTextField.Title.ToString(),
                    Hydration = ResidualHydration.None,
                    TextField = ResidualTextField.Title,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = false,
                        Values = ["["]
                    }
                },
                new ResidualFilter
                {
                    Kind = ResidualFilterKind.ProductionStatus,
                    Source = "ProductionStatus",
                    Hydration = ResidualHydration.None,
                    Criterion = new ResidualStringCriterion
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Negate = false,
                        Values = ["Continuing"]
                    }
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = await subject.SearchAsync(
            plan,
            new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }
}
