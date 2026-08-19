using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb.QueryEngine;

[TestFixture]
public class QueryPlannerTests
{
    private IQueryPlanner _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subject = new QueryPlanner();
    }

    [Test]
    public void Plan_KeepsTitleAndPublishDateFilters_WhenTitleAndExactYearAreRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            },
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).TextFilters, Has.Count.EqualTo(1));
            Assert.That(((FilterPlanNode)plan.Root).PublishDateFilters, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Plan_PositiveTitleFilter_WhenNegativeOperatorIsDoubleNegated()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Negate = true,
                        Value = "Iron Man",
                        Values = [null!, "Iron Man 2"]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = ((FilterPlanNode)plan.Root).TextFilters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.Values, Is.EqualTo(["Iron Man", "Iron Man 2"]));
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
        }
    }

    [Test]
    public void Plan_KeepsBothGenreAlternatives_WhenTitleGenreAndLanguageAreCombined()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> {
                    Operator = CriteriaValueOperator.ContainsWord,
                    Values = ["iron"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["en-US"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some =
                [
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
                                        Values = ["Action"]
                                    }
                                ]
                            }
                        },
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
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(
                ((FilterPlanNode)plan.Root).GenreFilters.Single().Alternatives.Select(alternative =>
                    alternative.Single().Values.Single()),
                Is.EqualTo(["Action", "Drama"]));
        }
    }

    [Test]
    public void Plan_KeepsTwoSubjectBindingsWithDistinctRoles_WhenStudioAndCastAreRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["studio"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Marvel Studios"] }]
                            }
                        }
                    ],
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Robert Downey Jr."] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).SubjectBindings, Has.Count.EqualTo(2));
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings[0].Filter.Alternatives.Single().Roles.Single(),
                Is.EqualTo(SubjectRole.Company));
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings[1].Filter.Alternatives.Single().Roles.Single(),
                Is.EqualTo(SubjectRole.Cast));
        }
    }

    [Test]
    public void SerializePlan_ReportsResidualCriteria_WhenGenresAndRelationshipsNeedResolution()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some =
                [
                    [
                        new GenreCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Action"] }]
                            }
                        },
                        new GenreCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Unresolvable genre"] }]
                            }
                        }
                    ]
                ]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["studio"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Marvel Studios"] }]
                            }
                        }
                    ],
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["studio"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["DC Studios"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var resolvedIdExpansion = (UnionPlanNode)filter.Child;
        var fetch = (FetchPlanNode)resolvedIdExpansion.Children.Single();
        var traceJson = plan.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                filter.ResidualPredicates,
                Does.Contain("genre[0]").And.Contain("relationship[0]").And.Contain("relationship[1]"));
            Assert.That(
                fetch.PushedFilters,
                Has.None.StartsWith("genre[").And.None.StartsWith("relationship["));
            Assert.That(traceJson.Split("\"genre[0]\"", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(traceJson.Split("\"relationship[0]\"", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(traceJson.Split("\"relationship[1]\"", StringSplitOptions.None), Has.Length.EqualTo(2));
        }
    }

    [Test]
    public void Plan_PushesTvCreditsWithoutName_WhenPersonIsGivenByExternalId()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
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
                                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["17419"] }]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var fetch = (FetchPlanNode)coalesce.Children[0];
        var traceJson = plan.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.Alternatives.Single()
                    .IdentityClauses.Single().Single().Name,
                Is.Null);
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.Alternatives.Single()
                    .IdentityClauses.Single().Single().ExternalId,
                Is.EqualTo("17419"));
            Assert.That(fetch.PushedFilters, Does.Contain("relationship[0] /person/{id}/tv_credits"));
            Assert.That(filter.ResidualPredicates, Does.Not.Contain("relationship[0]"));
            Assert.That(traceJson, Does.Contain("relationship[0] /person/{id}/tv_credits"));
            Assert.That(traceJson, Does.Not.Contain("\"relationship[0]\""));
        }
    }

    [TestCase("person", "tmdb", "6384", "with_people")]
    [TestCase("studio", "tmdb", "420", "with_companies")]
    public void SerializePlan_ReportsPushedMovieSubjectId_WhenSingleDirectIdIsPresent(
        string relationshipType,
        string externalIdSource,
        string externalId,
        string parameter)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [relationshipType] }]
                    },
                    ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                    {
                        Some = [[new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [externalIdSource] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [externalId] }]
                            }
                        }]]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var primary = filter.Child is CoalescePlanNode coalesce
            ? coalesce.Children[0]
            : filter.Child;
        var resolvedIdExpansion = (UnionPlanNode)primary;
        var fetch = (FetchPlanNode)resolvedIdExpansion.Children.Single();
        var traceJson = plan.SerializePlan();

        // Assert
        var pushedDisposition = "relationship[0] " + parameter;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(fetch.PushedFilters, Does.Contain(pushedDisposition));
            Assert.That(filter.ResidualPredicates, Does.Not.Contain("relationship[0]"));
            Assert.That(traceJson, Does.Contain(pushedDisposition));
            Assert.That(traceJson, Does.Not.Contain("\"relationship[0]\""));
        }
    }

    [TestCase("studio", "tmdb", "420", "with_companies")]
    [TestCase("network", "tmdb", "213", "with_networks")]
    public void SerializePlan_ReportsPushedTvOrganizationId_WhenSingleDirectIdIsPresent(
        string relationshipType,
        string externalIdSource,
        string externalId,
        string parameter)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [relationshipType] }]
                    },
                    ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                    {
                        Some = [[new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [externalIdSource] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [externalId] }]
                            }
                        }]]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var resolvedIdExpansion = (UnionPlanNode)filter.Child;
        var fetch = (FetchPlanNode)resolvedIdExpansion.Children.Single();
        var traceJson = plan.SerializePlan();

        // Assert
        var pushedDisposition = "relationship[0] " + parameter;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(fetch.PushedFilters, Does.Contain(pushedDisposition));
            Assert.That(filter.ResidualPredicates, Does.Not.Contain("relationship[0]"));
            Assert.That(traceJson, Does.Contain(pushedDisposition));
            Assert.That(traceJson, Does.Not.Contain("\"relationship[0]\""));
        }
    }

    [Test]
    public void SerializePlan_ReportsResidualTvNetworkIds_WhenMultipleDirectIdsArePresent()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [new RelationshipCriteria
                    {
                        Type = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["network"] }]
                        },
                        ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                        {
                            Some = [[new ExternalIdCriteria
                            {
                                Source = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                },
                                Value = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And = [new PropertyCriterion<string> { Values = ["1"] }]
                                }
                            }]]
                        }
                    }                                                            ],
                    [new RelationshipCriteria
                    {
                        Type = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["network"] }]
                        },
                        ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                        {
                            Some = [[new ExternalIdCriteria
                            {
                                Source = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                },
                                Value = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And = [new PropertyCriterion<string> { Values = ["2"] }]
                                }
                            }]]
                        }
                    }                                                            ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var resolvedIdExpansion = (UnionPlanNode)filter.Child;
        var fetch = (FetchPlanNode)resolvedIdExpansion.Children.Single();
        var traceJson = plan.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.PushedFilters, Has.None.Contains("with_networks"));
            Assert.That(
                filter.ResidualPredicates,
                Does.Contain("relationship[0]").And.Contain("relationship[1]"));
            Assert.That(traceJson.Split("\"relationship[0]\"", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(traceJson.Split("\"relationship[1]\"", StringSplitOptions.None), Has.Length.EqualTo(2));
        }
    }

    [TestCase("tv", "relationship[0] /person/{id}/tv_credits")]
    [TestCase("movie", "relationship[0] with_people")]
    public void SerializePlan_RetainsDirectRelationshipResidual_WhenFallbackCanIgnoreSubjects(
        string routeName,
        string pushedDisposition)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Source = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                    },
                                    Value = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["6384"] }]
                                    }
                                }]]
                            }
                        }
                    ],
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["studio"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Acme"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = routeName == "movie" ? ContentRoute.Movie : ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var primary = coalesce.Children[0];
        var fetch = primary is FetchPlanNode directFetch
            ? directFetch
            : (FetchPlanNode)((UnionPlanNode)primary).Children.Single();
        var fallback = (FetchPlanNode)coalesce.Children[1];
        var traceJson = plan.SerializePlan();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.PushedFilters, Does.Contain(pushedDisposition));
            Assert.That(fallback.IgnoreSubjectFilters, Is.True);
            Assert.That(
                filter.ResidualPredicates,
                Does.Contain("relationship[0]").And.Contain("relationship[1]"));
            Assert.That(traceJson, Does.Contain(pushedDisposition));
            Assert.That(traceJson, Does.Contain("\"relationship[0]\""));
        }
    }

    [Test]
    public void Plan_KeepsDirectorAndWriterRoles_WhenBothAreRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["director"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Greta Gerwig"] }]
                            }
                        }
                    ],
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["writer"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Noah Baumbach"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(
            ((FilterPlanNode)plan.Root).SubjectBindings.Select(binding =>
                binding.Filter.Alternatives.Single().Roles.Single()),
            Is.EqualTo([SubjectRole.Director, SubjectRole.Writer]));
    }

    [Test]
    public void Plan_ProducesDeterministicIntersect_WhenTwoTvPeopleAreRequired()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Source = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                    },
                                    Value = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["1"] }]
                                    }
                                }]]
                            }
                        }
                    ],
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Source = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                    },
                                    Value = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["2"] }]
                                    }
                                }]]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var intersect = (IntersectPlanNode)coalesce.Children[0];
        var fetches = intersect.Children.Cast<FetchPlanNode>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetches, Has.Count.EqualTo(2));
            Assert.That(
                fetches.Select(fetch => fetch.Route),
                Is.EqualTo([ContentRoute.TvShow, ContentRoute.TvShow]));
            Assert.That(
                fetches.Select(fetch => fetch.AnchorKind),
                Is.All.EqualTo(QueryAnchorKind.PersonCredits));
            Assert.That(
                fetches.Select(fetch => fetch.SubjectBindings.Single().Key),
                Is.EqualTo(["relationship:0", "relationship:1"]));
        }
    }

    [Test]
    public void Plan_ProducesCreditDiscoverUnion_WhenTvPersonOrCompanyIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Source = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                    },
                                    Value = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["1"] }]
                                    }
                                }]]
                            }
                        },
                        new RelationshipCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["studio"] }]
                            },
                            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                            {
                                Some = [[new ExternalIdCriteria
                                {
                                    Source = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                    },
                                    Value = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And = [new PropertyCriterion<string> { Values = ["2"] }]
                                    }
                                }]]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var union = (UnionPlanNode)coalesce.Children[0];
        var traceJson = plan.SerializePlan();
        var fetches = union.Children.Select(child => child is FetchPlanNode fetch
            ? fetch
            : (FetchPlanNode)((UnionPlanNode)child).Children.Single())
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                fetches.Select(fetch => fetch.AnchorKind),
                Is.EqualTo(
                [
                    QueryAnchorKind.PersonCredits,
                    QueryAnchorKind.Discover
                ]));
            Assert.That(fetches[0].SubjectRoles, Is.EqualTo([SubjectRole.Person]));
            Assert.That(fetches[1].SubjectRoles, Is.EqualTo([SubjectRole.Company]));
            Assert.That(traceJson, Does.Contain("\"expandsResolvedSubjectIds\": true"));
        }
    }

    [Test]
    public void Plan_ProducesPersonCompanyNetworkUnion_WhenTvRelationshipIsUntyped()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Acme"] }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var union = (UnionPlanNode)coalesce.Children[0];
        var fetches = union.Children.Select(child => child is FetchPlanNode fetch
            ? fetch
            : (FetchPlanNode)((UnionPlanNode)child).Children.Single())
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                fetches.Select(fetch => fetch.AnchorKind),
                Is.EqualTo(
                [
                    QueryAnchorKind.PersonCredits,
                    QueryAnchorKind.Discover,
                    QueryAnchorKind.Discover
                ]));
            Assert.That(
                fetches.Select(fetch => fetch.SubjectRoles.Single()),
                Is.EqualTo(
                [
                    SubjectRole.Person,
                    SubjectRole.Company,
                    SubjectRole.Network
                ]));
        }
    }

    [Test]
    public void Plan_ProducesPersonCompanyUnion_WhenMovieRelationshipIsUntyped()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Acme"] }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var coalesce = (CoalescePlanNode)filter.Child;
        var union = (UnionPlanNode)coalesce.Children[0];
        var expansionNodes = union.Children.Cast<UnionPlanNode>().ToList();
        var fetches = expansionNodes.Select(node => (FetchPlanNode)node.Children.Single()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(expansionNodes, Has.All.Property(nameof(UnionPlanNode.ExpandsResolvedSubjectIds)).True);
            Assert.That(fetches.Select(fetch => fetch.AnchorKind), Is.All.EqualTo(QueryAnchorKind.Discover));
            Assert.That(
                fetches.Select(fetch => fetch.SubjectRoleSelections.Single().Roles.Single()),
                Is.EqualTo([SubjectRole.Person, SubjectRole.Company]));
            Assert.That(
                fetches.Select(fetch => fetch.DiscoverParameters.Single().Name),
                Is.EqualTo(["with_people", "with_companies"]));
            Assert.That(
                fetches,
                Has.All.Matches<FetchPlanNode>(fetch =>
                    fetch.DiscoverParameters.Single().Combination == DiscoverCombination.Or));
        }
    }

    [Test]
    public void Plan_RetainsEveryAlternativeWithoutWarning_WhenSixExactContentIdsAreGiven()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["1"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["2"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["3"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["4"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["5"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["6"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FilterPlanNode)plan.Root).ContentIdFilters.Single().Alternatives, Has.Count.EqualTo(6));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesDiscovery_WhenTitleIsCombinedWithWholeMinuteDuration()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Heat"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                        Values = [TimeSpan.FromMinutes(120)]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
    }

    [Test]
    public void Plan_ReturnsDataQualityWarning_WhenTvDurationFilterIsApplied()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Bear"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                        Values = [TimeSpan.FromMinutes(30)]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FilterPlanNode)plan.Root).DurationFilters, Has.Count.EqualTo(1));
            Assert.That(plan.Warnings, Is.EqualTo(["TMDb series runtime data can be incomplete."]));
        }
    }

    [Test]
    public void Plan_KeepsBothTextFiltersWithoutWarning_WhenOneFieldCarriesTwoCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.StartsWithString,
                        Values = ["The"]
                    },
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.EndsWithString,
                        Values = ["Matrix"]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FilterPlanNode)plan.Root).TextFilters, Has.Count.EqualTo(2));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsConjunctiveIdentityClauses_WhenRelationshipCriteriaAreNested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string> { Values = ["cast"] },
                                    new PropertyCriterion<string> { Values = ["performer"] }
                                ]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string> { Values = ["Alice"] },
                                    new PropertyCriterion<string> { Values = ["Bob"] }
                                ]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var alternative = ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Roles, Is.EqualTo([SubjectRole.Cast]));
            Assert.That(alternative.IdentityClauses, Has.Count.EqualTo(2));
            Assert.That(alternative.IdentityClauses[0].Single().Name, Is.EqualTo("Alice"));
            Assert.That(alternative.IdentityClauses[1].Single().Name, Is.EqualTo("Bob"));
        }
    }

    [Test]
    public void Plan_UsesFilterRoot_WhenOverviewIsCombinedWithGenre()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Overview = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> {
                    Operator = CriteriaValueOperator.ContainsWord,
                    Values = ["dream"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some =
                [
                    [
                        new GenreCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Drama"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
    }

    [Test]
    public void Plan_UsesFilterRoot_WhenTitleIsCombinedWithSeveralMoviePersonGroups()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Barbie"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["person"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Greta Gerwig"] }]
                        }
                    }                                                            ],
                    [new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["person"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Margot Robbie"] }]
                        }
                    }                                                            ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenExcludedContentIdCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Iron Man"] }]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                None =
                [
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^tt"]
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenExcludedContentIdSourceIsPartlyUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Iron Man"] }]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                None = [new ExternalIdCriteria
                {
                    Source = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.In,
                            Values = ["tmdb", "thetvdb"]
                        }]
                    },
                    Value = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["550"] }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmptyWithoutWarning_WhenImdbContentIdIsInvalid()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some = [[new ExternalIdCriteria
                {
                    Source = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                    },
                    Value = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["tt123/evil?api_key=stolen"] }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesSpecificRole_WhenKindIsSubsumedByType()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["director"] }]
                    },
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["person"] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Greta Gerwig"] }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.Alternatives.Single().Roles,
                Is.EqualTo([SubjectRole.Director]));
        }
    }

    [Test]
    public void Plan_RequiresLocalVerificationWithoutWarning_WhenRelationshipQualifierIsAppliedLocally()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["cast"] }]
                    },
                    Qualifier = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Tony Stark"] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Robert Downey Jr."] }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.RequiresLocalVerification,
                Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsNoWarning_WhenBroadSubjectSeedIsUsable()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["cast"] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.ContainsString,
                            Values = ["Tom Hanks"]
                        }]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings.Single().Filter.RequiresLocalVerification,
                Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_InfersValidDomains_WhenRelationshipTypeIsOmitted()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Keanu Reeves"] }]
                    }
                }]]
            }
        };

        // Act
        var movie = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var tv = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)movie.Root).SubjectBindings.Single().Filter.Alternatives.Single().Roles,
                Is.EqualTo([SubjectRole.Person, SubjectRole.Company]));
            Assert.That(
                ((FilterPlanNode)tv.Root).SubjectBindings.Single().Filter.Alternatives.Single().Roles,
                Is.EqualTo(
                [
                    SubjectRole.Person,
                    SubjectRole.Company,
                    SubjectRole.Network
                ]));
            Assert.That(movie.Warnings, Is.Empty);
            Assert.That(tv.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PreservesCriteria_WhenNullChecksAndShortDurationsAreUsed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = CriteriaValueOperator.IsNotNull }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan> { Operator = CriteriaValueOperator.IsNull },
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.GreaterThan,
                        Value = new TimeSpan(2, 18, 59)
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var fetch = (FetchPlanNode)filter.Child;
        using (Assert.EnterMultipleScope())
        {
            var evaluation = (FilterPlanNode)plan.Root;
            Assert.That(evaluation.PublishDateFilters.Single().Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(evaluation.PublishDateFilters.Single().Values, Is.Empty);
            Assert.That(evaluation.DurationFilters[0].Operator, Is.EqualTo(CriteriaValueOperator.IsNull));
            Assert.That(evaluation.DurationFilters[0].Values, Is.Empty);
            Assert.That(evaluation.DurationFilters[1].Values.Single(), Is.EqualTo(new TimeSpan(2, 18, 59)));
            Assert.That(fetch.PushedFilters, Does.Contain("duration[1] runtime"));
            Assert.That(fetch.PushedFilters, Does.Not.Contain("duration[0] runtime"));
            Assert.That(filter.ResidualPredicates, Does.Contain("duration[0]"));
            Assert.That(filter.ResidualPredicates, Does.Not.Contain("duration[1]"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsSupportedGenreAlternativeWithoutWarning_WhenSiblingIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some =
                [[
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Values = ["Action"]
                                }
                            ]
                        }
                    },
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Comedy"] }]
                        }
                    }
                ]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var filter = ((FilterPlanNode)plan.Root).GenreFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Alternatives.Single().Single().Values, Is.EqualTo(["Comedy"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedGenreOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Thing"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^Hor"]
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_IgnoresExcludedGenreCriterion_WhenCriterionIsEmpty()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Thing"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None = [new GenreCriteria()]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).GenreFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PreservesSameGenrePredicateConjunctions_WhenGroupHasAlternatives()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some = [[
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.StartsWithString,
                                    Values = ["Act"]
                                },
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.EndsWithString,
                                    Values = ["ion"]
                                }
                            ]
                        }
                    },
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Drama"] }]
                        }
                    }
                ]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FilterPlanNode)plan.Root).GenreFilters.Single().Alternatives, Has.Count.EqualTo(2));
            Assert.That(((FilterPlanNode)plan.Root).GenreFilters.Single().Alternatives[0], Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_CombinesTheGenreParameterWithOr_WhenOneGenreGroupOffersAlternatives()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some = [[
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Action"] }]
                        }
                    },
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Drama"] }]
                        }
                    }
                ]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var parameter = ((FetchPlanNode)((FilterPlanNode)plan.Root).Child).DiscoverParameters
            .Single(candidate => candidate.Name == "with_genres");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameter.Combination, Is.EqualTo(DiscoverCombination.Or));
            Assert.That(parameter.GenreFilters.Single().Alternatives, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_CombinesTheGenreParameterWithAnd_WhenSeveralGenreGroupsAreRequired()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                Some =
                [
                    [
                        new GenreCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Action"] }]
                            }
                        }
                    ],
                    [
                        new GenreCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Drama"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var parameter = ((FetchPlanNode)((FilterPlanNode)plan.Root).Child).DiscoverParameters
            .Single(candidate => candidate.Name == "with_genres");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameter.Combination, Is.EqualTo(DiscoverCombination.And));
            Assert.That(parameter.GenreFilters, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_SupportsOverview_WhenMoviePersonPushdownIsSelective()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Overview = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["biography"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["cast"] }]
                    },
                    ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                    {
                        Some = [[new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["31"] }]
                            }
                        }]]
                    }
                }]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
    }

    [Test]
    public void Plan_AppliesTmdbExclusionLocally_WhenSeedIsNotContentIds()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                None = [new ExternalIdCriteria
                {
                    Source = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                    },
                    Value = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["550"] }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
    }

    [Test]
    public void Plan_RetainsImdbExclusion_WhenDetailVerificationIsRequired()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                None = [new ExternalIdCriteria
                {
                    Source = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                    },
                    Value = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["tt0137523"] }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FilterPlanNode)plan.Root).ContentIdFilters.Single().Excluded, Is.True);
            Assert.That(
                ((FilterPlanNode)plan.Root).ContentIdFilters.Single().Alternatives.Single().Source,
                Is.EqualTo("imdb"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesDetailVerificationWithoutWarning_WhenProductionStatusIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Released"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)plan.Root).TextFilters.Single().Field,
                Is.EqualTo(TextField.ProductionStatus));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_SupportsNullCheck_WhenListHitCarriesField()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)plan.Root).TextFilters.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.IsNull));
        }
    }

    [Test]
    public void Plan_UsesDistinctFingerprint_WhenValuesContainStructuralSeparators()
    {
        // Arrange
        var first = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.In, Values = ["a,b", "c"] }]
            }
        };
        var second = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.In, Values = ["a", "b,c"] }]
            }
        };

        // Act
        var firstPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = first,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var secondPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = second,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(firstPlan.Fingerprint, Is.Not.EqualTo(secondPlan.Fingerprint));
    }

    [Test]
    public void Plan_UsesDistinctFingerprint_WhenRelationshipCriteriaDiffer()
    {
        // Arrange
        var first = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["director"] }]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Christopher Nolan"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var second = first with
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["director"] }]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["organization"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Christopher Nolan"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var firstPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = first,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var secondPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = second,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(firstPlan.Fingerprint, Is.Not.EqualTo(secondPlan.Fingerprint));
    }

    [Test]
    public void Plan_KnownEmpty_WhenProviderTypeExcludesTmdb()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["tvdb"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsCriterionWithoutWarning_WhenProviderTypeAcceptsTmdb()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.In, Values = ["TMDB", "tvdb"] }]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = ((FilterPlanNode)plan.Root).TextFilters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).TextFilters, Has.Count.EqualTo(1));
            Assert.That(filter.Field, Is.EqualTo(TextField.Title));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.Values, Is.EqualTo(["Iron Man"]));
            Assert.That(((FilterPlanNode)plan.Root).ResidualPredicates, Is.EqualTo(["text[0]:Title"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenNegatedProviderTypeExcludesTmdb()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.NotEquals, Values = ["tmdb"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceTargetsAnotherRoute()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["tmdb:tv:42"] }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsCriterionWithoutWarning_WhenSourceMatchesRouteKey()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.StartsWithString,
                        Values = ["tmdb:movie:"]
                    }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceNegatesProviderSlug()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.NotEquals, Values = ["tmdb"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentPropertyIsRequestedPositively()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Established = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_VacuousPass_WhenAbsentPropertyIsNegatedOrNullChecked()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            BirthDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = CriteriaValueOperator.IsNull }]
            },
            DeathDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Values =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(1960, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentPropertyIsNotNullChecked()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Dissolved = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = CriteriaValueOperator.IsNotNull }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
    }

    [Test]
    public void Plan_KnownEmpty_WhenAwardsAreRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some = [[new AwardCriteria()]]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_VacuousPass_WhenAwardsAreExcluded()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                None = [new AwardCriteria()]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PositiveTagCriterion_IsKnownEmpty()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Tags = new CollectionCriteria<TagCriteria>
            {
                Some =
                [
                    [
                        new TagCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["watchlist"] }]
                            }
                        }
                    ]
                ],
                None = [new TagCriteria()]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ExcludedTagCriterion_IsVacuouslySatisfied()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Tags = new CollectionCriteria<TagCriteria> { None = [new TagCriteria()] },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Iron Man"] }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenCustomMetadataKeyIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["isbn"] = new() { Operator = CriteriaValueOperator.Equals, Values = ["12345"] }
                        }
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_AddsElementFilter_WhenContentRatingIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["US"] }]
                            },
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["PG-13"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var filter = ((FilterPlanNode)plan.Root).ElementFilters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(filter.Family, Is.EqualTo(ElementFamily.ContentRating));
            Assert.That(filter.Excluded, Is.False);
            Assert.That(filter.Alternatives.Single(), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenPreviewRequiresMimeType()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Previews = new CollectionCriteria<PreviewMetadataCriteria>
            {
                Some =
                [
                    [
                        new PreviewMetadataCriteria
                        {
                            MimeType = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNotNull }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsKindPredicate_WhenAlternateTextKindRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["title"] }]
                            },
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["Eisen"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        var alternative = ((FilterPlanNode)plan.Root).ElementFilters.Single().Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative, Has.Count.EqualTo(2));
            Assert.That(alternative.Select(predicate => predicate.Field), Does.Contain(ElementField.TextKind));
            Assert.That(alternative.Select(predicate => predicate.Field), Does.Contain(ElementField.TextValue));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAlternateTextLanguageRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Language = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["DE"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenExcludedContentRatingOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Iron Man"] }]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                None =
                [
                    new ContentRatingCriteria
                    {
                        Rating = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Values = ["PG"]
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenContentRatingOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Values = ["PG"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesDistinctFingerprint_WhenElementFiltersDiffer()
    {
        // Arrange
        var first = new MetadataCriteria
        {
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["PG-13"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var second = new MetadataCriteria
        {
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["R"] }]
                            }
                        }
                    ]
                ]
            }
        };

        // Act
        var firstPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = first,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });
        var secondPlan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = second,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        Assert.That(firstPlan.Fingerprint, Is.Not.EqualTo(secondPlan.Fingerprint));
    }

    [Test]
    public void Plan_MapsNetworkRoleIntoSubjectFilter_WhenRouteIsTv()
    {
        // Arrange
        var request = new QueryRequest
        {
            Route = ContentRoute.TvShow,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria>
                {
                    Some = [[new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["network"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["HBO"] }]
                        }
                    }]]
                }
            },
            Page = 1,
            DefaultLanguagePreference = "en-US"
        };

        // Act
        var plan = _subject.Plan(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).SubjectBindings, Has.Count.EqualTo(1));
            Assert.That(
                ((FilterPlanNode)plan.Root).SubjectBindings[0].Filter.Alternatives[0].Roles,
                Is.EqualTo([SubjectRole.Network]));
        }
    }

    [Test]
    public void Plan_IsKnownEmptyWithoutWarning_WhenPositiveNetworkCriterionTargetsMovieRoute()
    {
        // Arrange
        var request = new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria>
                {
                    Some = [[new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["network"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["HBO"] }]
                        }
                    }]]
                }
            },
            Page = 1,
            DefaultLanguagePreference = "en-US"
        };

        // Act
        var plan = _subject.Plan(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsExcludedNetworkCriterionVacuously_WhenRouteIsMovie()
    {
        // Arrange
        var request = new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria>
                {
                    None = [new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["network"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["HBO"] }]
                        }
                    }]
                }
            },
            Page = 1,
            DefaultLanguagePreference = "en-US"
        };

        // Act
        var plan = _subject.Plan(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).SubjectBindings, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsTitle_WhenExcludedRelationshipRoleIsAbsent()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None = [new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["chef"] }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
            Assert.That(((FilterPlanNode)plan.Root).TextFilters.Single().Values, Is.EqualTo(["The Matrix"]));
            Assert.That(((FilterPlanNode)plan.Root).SubjectBindings, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedRelationshipRoleOperatorCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None = [new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.Regex,
                            Values = ["^chef"]
                        }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedRelationshipSubjectOperatorCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None = [new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["director"] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.Regex,
                            Values = ["^Tom"]
                        }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedRelationshipQualifierOperatorCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None = [new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["director"] }]
                    },
                    Qualifier = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.Regex,
                            Values = ["^lead"]
                        }]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedRelationshipExternalIdOperatorCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None = [new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["director"] }]
                    },
                    ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                    {
                        Some = [[new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^nm"]
                                }]
                            }
                        }]]
                    }
                }]
            }
        };

        // Act
        var plan = _subject.Plan(new QueryRequest
        {
            Route = ContentRoute.Movie,
            Criteria = criteria,
            Page = 1,
            DefaultLanguagePreference = "en-US"
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }
}
