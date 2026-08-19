using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzSearchPlannerTests
{
    [Test]
    public void Plan_UnfieldedLucene_WhenSearchIsOrdinary()
    {
        // Arrange
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria(),
            RequestedPage = 2,
            Settings = new Settings(),
            IsOrdinary = true,
            OrdinaryTerm = "Björk"
        });

        // Assert
        var fetch = (FetchPlanNode)plan.Root!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AccessMode, Is.EqualTo(AccessMode.Search));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.UnfieldedText));
            Assert.That(fetch.OrdinaryTerm, Is.EqualTo("Björk"));
            Assert.That(plan.RequestedPage, Is.EqualTo(2));
        }
    }

    [Test]
    public void Plan_AllValuesAndIndex_WhenTitleUsesMembership()
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
                        Operator = CriteriaValueOperator.In,
                        Values = ["Kind of Blue", "Blue Train"]
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filterNode = (FilterPlanNode)plan.Root!;
        var fetch = (FetchPlanNode)filterNode.Child;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AccessMode, Is.EqualTo(AccessMode.Search));
            Assert.That(fetch.Filters, Has.Count.EqualTo(1));
            Assert.That(fetch.Filters[0].StringValues, Is.EqualTo(["Kind of Blue", "Blue Train"]));
            Assert.That(fetch.Filters[0].RemoteField, Is.EqualTo("releasegroupaccent"));
            Assert.That(filterNode.Filters, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Plan_UsesBothRawValueShapes_WhenNegativeOperatorIsDoubleNegated()
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
                        Value = "Kind of Blue",
                        Values = [null!, "Blue Train"]
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.StringValues, Is.EqualTo(["Kind of Blue", "Blue Train"]));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
        }
    }

    [TestCase((int)QueryTarget.Artist, Constants.ProviderType, (int)EntityKind.Artist)]
    [TestCase((int)QueryTarget.Album, Constants.ProviderType, (int)EntityKind.ReleaseGroup)]
    [TestCase((int)QueryTarget.Track, Constants.ProviderType, (int)EntityKind.Recording)]
    public void Plan_DirectLookup_WhenPrimaryMbidIsSpecified(
        int targetValue,
        string source,
        int expectedEntityValue)
    {
        // Arrange
        var target = (QueryTarget)targetValue;
        var expectedEntity = (EntityKind)expectedEntityValue;
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
                                And = [new PropertyCriterion<string> { Values = [source] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["550e8400-e29b-41d4-a716-446655440000"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = target,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var fetch = plan.Root!.Flatten().OfType<FetchPlanNode>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AccessMode, Is.EqualTo(AccessMode.Lookup));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.Mbid));
            Assert.That(fetch.EntityKind, Is.EqualTo(expectedEntity));
        }
    }

    [Test]
    public void Plan_TrackOverview_UsesSeededLocalVerification()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Smells Like Teen Spirit"] }]
            },
            Overview = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["live"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filterNode = (FilterPlanNode)plan.Root!;
        var overview = filterNode.Filters.Single(filter => filter.Kind == QueryFilterKind.Overview);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(overview.RemoteField, Is.Null);
            Assert.That(overview.RemoteApplied, Is.False);
            Assert.That(overview.VerifyLocally, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompatibleAlternatives_WhenIdentifierSourceUsesMembership()
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
                                And = [new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = [Constants.ProviderType, "foreign"]
                                }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["550e8400-e29b-41d4-a716-446655440000"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Lookup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().EntityKind,
                Is.EqualTo(EntityKind.ReleaseGroup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().DirectValue,
                Is.EqualTo("550e8400-e29b-41d4-a716-446655440000"));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.One.Contains("dropped invalid identifier alternatives"));
        }
    }

    [Test]
    public void Plan_DirectLookup_WhenIsrcIsSpecified()
    {
        // Arrange
        var isrcCriteria = new MetadataCriteria
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
                                And = [new PropertyCriterion<string> { Values = [Constants.IsrcSource] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["USRC17607839"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var isrcPlan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = isrcCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var fetch = isrcPlan.Root!.Flatten().OfType<FetchPlanNode>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AccessMode, Is.EqualTo(AccessMode.IsrcLookup));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.Isrc));
            Assert.That(fetch.DirectValue, Is.EqualTo("USRC17607839"));
        }
    }

    [TestCase(
        (int)QueryTarget.Artist,
        Constants.ProviderType,
        (int)AccessMode.Lookup,
        (int)ResidualNeed.EntityGenres)]
    [TestCase(
        (int)QueryTarget.Album,
        Constants.ProviderType,
        (int)AccessMode.Lookup,
        (int)ResidualNeed.EntityGenres)]
    [TestCase(
        (int)QueryTarget.Track,
        Constants.ProviderType,
        (int)AccessMode.Lookup,
        (int)ResidualNeed.EntityGenres)]
    [TestCase(
        (int)QueryTarget.Track,
        Constants.IsrcSource,
        (int)AccessMode.IsrcLookup,
        (int)ResidualNeed.EntityGenres)]
    public void Plan_Result_WhenDirectLookupHasGenreResidual(
        int targetValue,
        string source,
        int accessModeValue,
        int needValue)
    {
        // Arrange
        var target = (QueryTarget)targetValue;
        var accessMode = (AccessMode)accessModeValue;
        var need = (ResidualNeed)needValue;
        var identity = source == Constants.IsrcSource
            ? "USRC17607839"
            : "550e8400-e29b-41d4-a716-446655440000";
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
                                And = [new PropertyCriterion<string> { Values = [source] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [identity] }]
                            }
                        }
                    ]
                ]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["ambient"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = target,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(accessMode));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Need,
                Is.EqualTo(need));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenTitleContainsStringUsesWildcardSupersetSeed()
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
                        Operator = CriteriaValueOperator.ContainsString,
                        Values = ["will rock"]
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().RemoteField,
                Is.EqualTo("recordingaccent"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().RemoteApplied,
                Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().VerifyLocally,
                Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenTitleContainsStringHasGenreSeed()
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
                        Operator = CriteriaValueOperator.ContainsString,
                        Values = ["will rock"]
                    }
                ]
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
                                And = [new PropertyCriterion<string> { Values = ["rock"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var title = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
            .Single(filter => filter.Kind == QueryFilterKind.Title);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters, Has.Count.EqualTo(2));
            Assert.That(title.RemoteApplied, Is.True);
            Assert.That(title.VerifyLocally, Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(
                plan.Warnings.Single(),
                Does.Contain("treats genre searches as tag searches"));
        }
    }

    [Test]
    public void Plan_ExactAndLocallyVerifiedFilters_WhenPartialDateUsesEqualityAndOrdering()
    {
        // Arrange
        TemporalValue.TryParse("1998", out var year);
        TemporalValue.TryParse("2000-06", out var month);
        var criteria = new MetadataCriteria
        {
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue> { Values = [year!] },
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.GreaterThan,
                        Values = [month!]
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters[0].VerifyLocally,
                Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters[1].VerifyLocally,
                Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
        }
    }

    [Test]
    public void Plan_TagApproximation_WhenGenreIsSpecified()
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
                                And = [new PropertyCriterion<string> { Values = ["jazz"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().RemoteField,
                Is.EqualTo("tag"));
            Assert.That(
                plan.Warnings.Single(),
                Does.Contain("treats genre searches as tag searches"));
        }
    }

    [Test]
    public void Plan_ArtistNameWithLocalVerification_WhenArtistRelationshipIsSpecified()
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
                                And = [new PropertyCriterion<string> { Values = ["artist"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single()
                    .RelationshipValues
                    .Single()
                    .ArtistName,
                Is.EqualTo("Miles Davis"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().VerifyLocally,
                Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
        }
    }

    [Test]
    public void Plan_IntersectedValue_WhenExternalIdCriteriaRepeat()
    {
        // Arrange
        const string first = "11111111-1111-4111-8111-111111111111";
        const string shared = "22222222-2222-4222-8222-222222222222";
        const string last = "33333333-3333-4333-8333-333333333333";
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values =
                                        [
                                            Constants.ProviderType,
                                            Constants.IsniSource
                                        ]
                                    },
                                    new PropertyCriterion<string>
                                    {
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
                                        Operator = CriteriaValueOperator.In,
                                        Values = [first, shared]
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = [shared, last]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            var filterNode = (FilterPlanNode)plan.Root!;
            Assert.That(
                filterNode.Filters.Single().IdentityValues
                    .Select(value => value.Value),
                Is.EqualTo([shared]));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Lookup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().EntityKind,
                Is.EqualTo(EntityKind.Artist));
        }
    }

    [Test]
    public void Plan_EmptyQuery_WhenExternalIdValuesAreDisjoint()
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
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
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["22222222-2222-4222-8222-222222222222"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_IntersectedSubjectAndIdentifier_WhenRelationshipCriteriaAreConjoined()
    {
        // Arrange
        const string shared = "22222222-2222-4222-8222-222222222222";
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
                                And = [new PropertyCriterion<string> { Values = ["artist"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = ["Miles Davis", "John Coltrane"]
                                    },
                                    new PropertyCriterion<string> { Values = ["Miles Davis"] }
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
                                                        Operator = CriteriaValueOperator.In,
                                                        Values =
                                                        [
                                                            "11111111-1111-4111-8111-111111111111",
                                                            shared
                                                        ]
                                                    }
                                                ]
                                            }
                                        }
                                    ],
                                    [
                                        new ExternalIdCriteria
                                        {
                                            Source = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And =
                                                [
                                                    new PropertyCriterion<string>
                                                    {
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
                                                        Operator = CriteriaValueOperator.In,
                                                        Values =
                                                        [
                                                            shared,
                                                            "33333333-3333-4333-8333-333333333333"
                                                        ]
                                                    }
                                                ]
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
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var relationship = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single()
            .RelationshipValues
            .Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationship.ArtistName, Is.EqualTo("Miles Davis"));
            Assert.That(relationship.ArtistMbid, Is.EqualTo(shared));
        }
    }

    [Test]
    public void Plan_LocalVerification_WhenTemporalEqualityIsNegated()
    {
        // Arrange
        TemporalValue.TryParse("1998", out var year);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Album"] }]
            },
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Values = [year!], Negate = true }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var temporal = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
            .Single(filter => filter.Kind == QueryFilterKind.PublishDate);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(temporal.RemoteApplied, Is.True);
            Assert.That(temporal.VerifyLocally, Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
        }
    }

    [Test]
    public void Plan_LocalVerification_WhenDirectLookupHasAdditionalCriteria()
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
                        Operator = CriteriaValueOperator.ContainsString,
                        Values = ["uee"]
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
                                        Values = ["0383dadf-2a4e-4d10-a46a-e9e041da8eb3"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Lookup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().EntityKind,
                Is.EqualTo(EntityKind.Artist));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters,
                Has.All.Property(nameof(QueryFilter.VerifyLocally)).True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters,
                Has.All.Property(nameof(QueryFilter.RemoteApplied)).False);
        }
    }

    [Test]
    public void Plan_Result_WhenOriginalTitleUsesTitleAndAliasCompilation()
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
                        Operator = CriteriaValueOperator.Regex,
                        Values = [".*blue.*"]
                    }
                ]
            },
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Original"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.AlternateText));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().RemoteField,
                Is.EqualTo("alias"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().AlternateRemoteField,
                Is.EqualTo("releasegroupaccent"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().IncludePrimaryTitle,
                Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.AlternateText));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().IncludePrimaryTitle,
                Is.True);
            Assert.That(plan.Warnings.Single(), Does.Contain("text field or operator"));
        }
    }

    [Test]
    public void Plan_UsesJustifyingIdentity_WhenEarlierGroupHasMultipleValues()
    {
        // Arrange
        const string first = "11111111-1111-4111-8111-111111111111";
        const string selected = "22222222-2222-4222-8222-222222222222";
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
                                And = [new PropertyCriterion<string>
                                {
                                    Values = [Constants.ProviderType]
                                }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = [first, selected]
                                }]
                            }
                        }
                    ],
                    [
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string>
                                {
                                    Values = [Constants.ProviderType]
                                }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [selected] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Lookup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().EntityKind,
                Is.EqualTo(EntityKind.Artist));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().DirectValue,
                Is.EqualTo(selected));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters,
                Has.All.Property(nameof(QueryFilter.VerifyLocally)).True);
        }
    }

    [Test]
    public void Plan_ReturnsApproximateApplication_WhenTemporalInContainsInstant()
    {
        // Arrange
        TemporalValue.TryParse("1990", out var year);
        TemporalValue.TryParse("1990-06-01T12:00:00Z", out var instant);
        var criteria = new MetadataCriteria
        {
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue>
                {
                    Operator = CriteriaValueOperator.In,
                    Values = [year!, instant!]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().TemporalValues,
                Is.EqualTo([year]));
            Assert.That(plan.Warnings.Single(), Does.Contain("instant-precision"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
        }
    }

    [Test]
    public void Plan_RequiresLocalVerification_WhenOrderedDurationHasMultipleValues()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And = [new PropertyCriterion<TimeSpan>
                {
                    Operator = CriteriaValueOperator.GreaterThan,
                    Values = [TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(2)]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().RemoteApplied,
                Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().VerifyLocally,
                Is.True);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
        }
    }

    [TestCase(CriteriaValueOperator.LessThan, 0)]
    [TestCase(CriteriaValueOperator.LessThan, -1)]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, -1)]
    public void Plan_ReturnsKnownEmpty_WhenTheOrderedDurationBoundaryCannotMatch(
        CriteriaValueOperator valueOperator,
        int milliseconds)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And = [new PropertyCriterion<TimeSpan>
                {
                    Operator = valueOperator,
                    Values = [TimeSpan.FromMilliseconds(milliseconds)]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty),
                Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.GreaterThan, 0, CriteriaValueOperator.GreaterThan)]
    [TestCase(CriteriaValueOperator.GreaterThan, -1, CriteriaValueOperator.GreaterThanOrEqual)]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, 0, CriteriaValueOperator.GreaterThanOrEqual)]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual, -1, CriteriaValueOperator.GreaterThanOrEqual)]
    [TestCase(CriteriaValueOperator.LessThanOrEqual, 0, CriteriaValueOperator.LessThanOrEqual)]
    public void Plan_KeepsTheOrderedDurationBoundary_WhenTheComparisonIsSatisfiable(
        CriteriaValueOperator valueOperator,
        int milliseconds,
        CriteriaValueOperator expectedOperator)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And = [new PropertyCriterion<TimeSpan>
                {
                    Operator = valueOperator,
                    Values = [TimeSpan.FromMilliseconds(milliseconds)]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty),
                Is.False);
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Duration));
            Assert.That(filter.Operator, Is.EqualTo(expectedOperator));
            Assert.That(filter.DurationMilliseconds, Is.EqualTo([0]));
            Assert.That(filter.RemoteField, Is.EqualTo("dur"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesLabelRelationshipFilter_WhenAlbumCriterionReferencesMusicLabel()
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
                                And = [new PropertyCriterion<string> { Values = ["music-label"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Blue Note"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.LabelRelationship));
            Assert.That(filter.LabelValues.Single().LabelName, Is.EqualTo("Blue Note"));
            Assert.That(filter.RemoteApplied, Is.True);
            Assert.That(filter.VerifyLocally, Is.True);
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.LabelRelationship));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.RepresentativeReleaseLabels));
            Assert.That(residual.Negate, Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesLabelMbid_WhenRelationshipExternalIdUsesLabelSource()
    {
        // Arrange
        const string labelMbid = "44444444-4444-4444-8444-444444444444";
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
                                And = [new PropertyCriterion<string> { Values = ["music-label"] }]
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
                                                        Values = [Constants.ProviderType]
                                                    }
                                                ]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = [labelMbid] }]
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
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.LabelRelationship));
            Assert.That(filter.LabelValues.Single().LabelMbid, Is.EqualTo(labelMbid));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesLabelRelationshipFilter_WhenPublicTypeFacetIdentifiesMusicLabel()
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
                                And = [new PropertyCriterion<string> { Values = ["music-label"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["DGC Records"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.LabelRelationship));
            Assert.That(filter.LabelValues.Single().LabelName, Is.EqualTo("DGC Records"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenTitleAndLabelComposeThroughReleaseIndex()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.LabelSubjectKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["DGC Records"] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var labelFilter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single(filter =>
            filter.Kind == QueryFilterKind.LabelRelationship);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(labelFilter.RemoteApplied, Is.True);
            Assert.That(labelFilter.ReleaseIndexApplied, Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().UsesReleaseIndexComposition,
                Is.True);
        }
    }

    [Test]
    public void Plan_Result_WhenExternalIdSourcePresenceUsesRemoteExistenceClause()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
            .Single(value => value.Kind == QueryFilterKind.ExternalId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(filter.IdentityValues.Single().RemoteField, Is.EqualTo("rgid"));
            Assert.That(filter.IdentityValues.Single().Value, Is.Empty);
            Assert.That(filter.RemoteApplied, Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenExternalIdFuzzyValueUsesHydratedResidual()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = ["1b022e01"]
                                }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.ExternalId));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.EntityExternalIds));
            Assert.That(
                residual.Alternatives.Single().Predicates.Select(predicate => predicate.Field),
                Is.EqualTo(
                [
                    ResidualField.ExternalIdSource,
                    ResidualField.ExternalIdValue
                ]));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
        }
    }

    [TestCase(CriteriaValueOperator.StartsWithString, "Smells Like")]
    [TestCase(CriteriaValueOperator.EndsWithString, "Teen Spirit")]
    public void Plan_Result_WhenTitleBoundaryOperatorUsesWildcardSupersetSeed(
        CriteriaValueOperator valueOperator,
        string value)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = valueOperator, Values = [value] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var seedFilter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(seedFilter.VerifyLocally, Is.True);
            Assert.That(seedFilter.RemoteApplied, Is.True);
            Assert.That(seedFilter.RemoteField, Is.EqualTo("recordingaccent"));
            Assert.That(seedFilter.Operator, Is.EqualTo(valueOperator));
            Assert.That(seedFilter.StringValues, Is.EqualTo([value]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenArtistLanguageMatchesAbsentScalar()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nirvana"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenTrackLanguageCannotMatchAbsentScalar()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Smells Like Teen Spirit"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["eng"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumDurationUsesRepresentativeReleaseResidual()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And = [new PropertyCriterion<TimeSpan> { Operator = CriteriaValueOperator.IsNull }]
            },
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.NotEquals, Values = ["x"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.Duration));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Need,
                Is.EqualTo(ResidualNeed.RepresentativeReleaseDuration));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumDurationWithTitleSeedUsesRepresentativeReleaseResidual()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.GreaterThan,
                        Values = [TimeSpan.FromMinutes(40)]
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.Duration));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Alternatives.Single()
                    .Predicates
                    .Single()
                    .DurationMilliseconds,
                Is.EqualTo([2400000L]));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().UsesReleaseIndexComposition,
                Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase("duration")]
    [TestCase("production-status")]
    [TestCase("publish-date")]
    public void Plan_Result_WhenTargetDoesNotExposeRequiredScalar(string criterionName)
    {
        // Arrange
        TemporalValue.TryParse("2020", out var publishDate);
        var criteria = criterionName switch
        {
            "duration" => new MetadataCriteria
            {
                Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
                {
                    And = [new PropertyCriterion<TimeSpan> { Values = [TimeSpan.FromMinutes(30)] }]
                }
            },
            "production-status" => new MetadataCriteria
            {
                ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["released"] }]
                }
            },
            "publish-date" => new MetadataCriteria
            {
                PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
                {
                    And = [new PropertyCriterion<TemporalValue> { Values = [publishDate!] }]
                }
            },
            _ => throw new InvalidOperationException("criterion")
        };
        var target = criterionName is "publish-date" or "duration"
            ? QueryTarget.Artist
            : QueryTarget.Album;
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = target,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmpty_WhenForeignSubjectKindCriterionIsPositive()
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
                                And = [new PropertyCriterion<string> { Values = ["Ghibli"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_IgnoresForeignSubjectKindExclusion_WhenPairedWithTitleSeed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None =
                [
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["studio"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Ghibli"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmpty_WhenTrackCriterionReferencesMusicLabel()
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
                                And = [new PropertyCriterion<string> { Values = ["music-label"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Blue Note"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenLabelExclusionUsesRepresentativeReleaseResidual()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None =
                [
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["music-label"] }]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Blue Note"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.LabelRelationship));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Need,
                Is.EqualTo(ResidualNeed.RepresentativeReleaseLabels));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Negate,
                Is.True);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
        }
    }

    [Test]
    public void Plan_ReturnsStableApproximateDisposition_WhenInvalidIdentifierOrderChanges()
    {
        // Arrange
        const string valid = "550e8400-e29b-41d4-a716-446655440000";
        var invalidFirstCriteria = new MetadataCriteria
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = ["not-a-mbid", valid]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var invalidLastCriteria = new MetadataCriteria
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = [valid, "not-a-mbid"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var invalidFirst = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = invalidFirstCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var invalidLast = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = invalidLastCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var invalidFirstFetch = invalidFirst.Root!.Flatten().OfType<FetchPlanNode>().Single();
        var invalidLastFetch = invalidLast.Root!.Flatten().OfType<FetchPlanNode>().Single();
        var invalidFirstFilter = (FilterPlanNode)invalidFirst.Root!;
        var invalidLastFilter = (FilterPlanNode)invalidLast.Root!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(invalidFirstFetch.DirectValue, Is.EqualTo(valid));
            Assert.That(invalidLastFetch.DirectValue, Is.EqualTo(valid));
            Assert.That(
                invalidFirst.Warnings.Select(warning => warning),
                Is.EqualTo(
                    ["musicbrainz dropped invalid identifier alternatives, so matching candidates may be omitted."]));
            Assert.That(
                invalidLast.Warnings.Select(warning => warning),
                Is.EqualTo(
                    ["musicbrainz dropped invalid identifier alternatives, so matching candidates may be omitted."]));
            Assert.That(
                invalidFirstFilter.Filters.Single().IdentityValues,
                Is.EqualTo(
                    [
                        new IdentityValue
                        {
                            Source = "musicbrainz",
                            Value = "550e8400-e29b-41d4-a716-446655440000",
                            RemoteField = "arid"
                        }
                    ]));
            Assert.That(
                invalidLastFilter.Filters.Single().IdentityValues,
                Is.EqualTo(
                    [
                        new IdentityValue
                        {
                            Source = "musicbrainz",
                            Value = "550e8400-e29b-41d4-a716-446655440000",
                            RemoteField = "arid"
                        }
                    ]));
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmpty_WhenPositiveTagCriterionAccompaniesTitle()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Tags = new CollectionCriteria<TagCriteria>
            {
                Some =
                [
                    [
                        new TagCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["favorite"] }]
                            }
                        }
                    ]
                ],
                None =
                [
                    new TagCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = ["skip"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
        }
    }

    [Test]
    public void Plan_Result_WhenOnlyTagExclusionIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Tags = new CollectionCriteria<TagCriteria>
            {
                None = [new TagCriteria()]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
        }
    }

    [TestCase(CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.In)]
    [TestCase(CriteriaValueOperator.ContainsWord)]
    public void Plan_Result_WhenAlternateTextUsesRemoteOperator(CriteriaValueOperator valueOperator)
    {
        // Arrange
        var values = valueOperator == CriteriaValueOperator.In ? new[] { "Bowie", "Ziggy" } : ["Bowie"];
        var criteria = new MetadataCriteria
        {
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Operator = valueOperator, Values = values }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.AlternateText));
            Assert.That(filter.RemoteField, Is.EqualTo("alias"));
            Assert.That(filter.RemoteApplied, Is.True);
            Assert.That(filter.VerifyLocally, Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.AlternateText));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Need,
                Is.EqualTo(ResidualNeed.EntityAliases));
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlternateTextSubstringHasTitleSeed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["David Bowie"] }]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["Bowie"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters,
                Has.None.Property(nameof(QueryFilter.Kind))
                    .EqualTo(QueryFilterKind.AlternateText));
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.AlternateText));
            Assert.That(
                residual.Alternatives.Single().Predicates.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlternateTextNoneHasTitleSeed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["David Bowie"] }]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                None =
                [
                    new AlternateTextCriteria
                    {
                        Text = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Ziggy Stardust"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters,
                Has.None.Property(nameof(QueryFilter.Kind))
                    .EqualTo(QueryFilterKind.AlternateText));
            Assert.That(residual.Negate, Is.True);
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.EntityAliases));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlternateTextKindOrLanguageNeedsHydration()
    {
        // Arrange
        var kindCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["David Bowie"] }]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Artist name"] }]
                            },
                            Language = new CriteriaSet<PropertyCriterion<string>>(),
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Bowie"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var languageCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["David Bowie"] }]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                Some =
                [
                    [
                        new AlternateTextCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>(),
                            Language = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["eng"] }]
                            },
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Bowie"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var kindPlan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = kindCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var languagePlan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = languageCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ((FilterPlanNode)kindPlan.Root!).Residuals.Single().Alternatives.Single().Predicates,
                Has.Some
                    .Property(nameof(ResidualPredicate.Field))
                    .EqualTo(ResidualField.AlternateTextKind));
            Assert.That(
                ((FilterPlanNode)languagePlan.Root!).Residuals.Single().Alternatives.Single().Predicates,
                Has
                    .Some
                    .Property(nameof(ResidualPredicate.Field))
                    .EqualTo(ResidualField.AlternateTextLanguage));
            Assert.That(kindPlan.Warnings, Is.Empty);
            Assert.That(languagePlan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenArtistBirthAndDeathDatesAreRequested()
    {
        // Arrange
        TemporalValue.TryParse("1947-01-08", out var birthDate);
        TemporalValue.TryParse("2016-01-10", out var deathDate);
        var criteria = new MetadataCriteria
        {
            BirthDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Values = [birthDate!] }]
            },
            DeathDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Values = [deathDate!] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
                    .Single(filter => filter.Kind == QueryFilterKind.BirthDate)
                    .RemoteField,
                Is.EqualTo("begin"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
                    .Single(filter => filter.Kind == QueryFilterKind.DeathDate)
                    .RemoteField,
                Is.EqualTo("end"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenTrackPublishDateIsRequested()
    {
        // Arrange
        TemporalValue.TryParse("1975-10-31", out var publishDate);
        var criteria = new MetadataCriteria
        {
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Values = [publishDate!] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.PublishDate));
            Assert.That(filter.RemoteField, Is.EqualTo("firstreleasedate"));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumLanguageUsesIso6393Code()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["ENG"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Language));
            Assert.That(filter.RemoteField, Is.EqualTo("lang"));
            Assert.That(filter.StringValues, Is.EqualTo(["eng"]));
            Assert.That(filter.RemoteApplied, Is.True);
            Assert.That(filter.ReleaseIndexApplied, Is.True);
            Assert.That(filter.VerifyLocally, Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().UsesReleaseIndexComposition,
                Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumLanguageUsesNameInsteadOfCode()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Low"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["English"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var fetch = plan.Root!.Flatten().OfType<FetchPlanNode>().Single();
        var filterNode = (FilterPlanNode)plan.Root!;
        var languageFilter = filterNode.Filters.Single(filter => filter.Kind == QueryFilterKind.Language);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(languageFilter.StringValues, Is.EqualTo(["eng"]));
            Assert.That(languageFilter.RemoteApplied, Is.False);
            Assert.That(languageFilter.ReleaseIndexApplied, Is.False);
            Assert.That(fetch.UsesReleaseIndexComposition, Is.False);
            Assert.That(fetch.Target, Is.EqualTo(QueryTarget.Album));
            Assert.That(fetch.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(fetch.Filters.Single().RemoteField, Is.EqualTo("releasegroupaccent"));
            Assert.That(fetch.Filters.Single().StringValues, Is.EqualTo(["Low"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsUnknownLanguageAlternative_WhenAlbumLanguageHasKnownAlternative()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.In,
                    Values = ["English", "not-a-language"]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().StringValues,
                Is.EqualTo(["eng"]));
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(plan.Warnings.Single(), Does.Contain("unrecognized language alternatives"));
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmpty_WhenAlbumLanguageIsUnknown()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["not-a-language"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
        }
    }

    [TestCase((int)CriteriaValueOperator.NotEquals, "English", (int)CriteriaValueOperator.Equals, true, "eng")]
    [TestCase((int)CriteriaValueOperator.NotIn, "English", (int)CriteriaValueOperator.In, true, "eng")]
    [TestCase((int)CriteriaValueOperator.ContainsString, "ng", (int)CriteriaValueOperator.ContainsString, false, "ng")]
    [TestCase(
        (int)CriteriaValueOperator.NotContainsString,
        "ng",
        (int)CriteriaValueOperator.ContainsString,
        true,
        "ng")]
    [TestCase(
        (int)CriteriaValueOperator.StartsWithString,
        "en",
        (int)CriteriaValueOperator.StartsWithString,
        false,
        "eng")]
    [TestCase(
        (int)CriteriaValueOperator.NotStartsWithString,
        "en",
        (int)CriteriaValueOperator.StartsWithString,
        true,
        "eng")]
    [TestCase((int)CriteriaValueOperator.EndsWithString, "ng", (int)CriteriaValueOperator.EndsWithString, false, "ng")]
    [TestCase(
        (int)CriteriaValueOperator.NotEndsWithString,
        "ng",
        (int)CriteriaValueOperator.EndsWithString,
        true,
        "ng")]
    public void Plan_AlbumLanguageOperator_UsesSeededRepresentativeReleaseResidual(
        int operationValue,
        string value,
        int expectedOperationValue,
        bool expectedNegate,
        string expectedValue)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = (CriteriaValueOperator)operationValue,
                    Values = [value]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();
        var predicate = residual.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters, Has.Count.EqualTo(1));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.Language));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.RepresentativeReleaseLanguage));
            Assert.That(predicate.Field, Is.EqualTo(ResidualField.Language));
            Assert.That(predicate.Operator, Is.EqualTo((CriteriaValueOperator)expectedOperationValue));
            Assert.That(predicate.Negate, Is.EqualTo(expectedNegate));
            Assert.That(predicate.Values, Is.EqualTo([expectedValue]));
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase((int)CriteriaValueOperator.IsNull)]
    [TestCase((int)CriteriaValueOperator.IsNotNull)]
    public void Plan_AlbumLanguageNullOperator_UsesSeededRepresentativeReleaseResidual(int operationValue)
    {
        // Arrange
        var operation = (CriteriaValueOperator)operationValue;
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = operation }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();
        var predicate = residual.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters, Has.Count.EqualTo(1));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.RepresentativeReleaseLanguage));
            Assert.That(predicate.Field, Is.EqualTo(ResidualField.Language));
            Assert.That(predicate.Operator, Is.EqualTo(operation));
            Assert.That(predicate.Negate, Is.False);
            Assert.That(predicate.Values, Is.Empty);
            Assert.That(plan.Root is FilterPlanNode, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenLanguageAndLabelMustMatchOnePhysicalRelease()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["eng"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["music-label"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Blue Note"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().UsesReleaseIndexComposition,
                Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
                    .Count(filter => filter.ReleaseIndexApplied),
                Is.EqualTo(2));
            Assert.That(plan.Warnings.Single(), Does.Contain("one physical release"));
        }
    }

    [TestCase("awards")]
    [TestCase("previews")]
    [TestCase("content-ratings")]
    [TestCase("custom-metadata")]
    public void Plan_Result_WhenProviderAbsentDataIsRequired(string criterionName)
    {
        // Arrange
        var criteria = criterionName switch
        {
            "awards" => new MetadataCriteria
            {
                Awards = new CollectionCriteria<AwardCriteria> { Some = [[new AwardCriteria()]] }
            },
            "previews" => new MetadataCriteria
            {
                Previews = new CollectionCriteria<PreviewMetadataCriteria>
                {
                    Some = [[new PreviewMetadataCriteria()]]
                }
            },
            "content-ratings" => new MetadataCriteria
            {
                ContentRatings = new CollectionCriteria<ContentRatingCriteria>
                {
                    Some = [[new ContentRatingCriteria()]]
                }
            },
            "custom-metadata" => new MetadataCriteria
            {
                CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
                {
                    And =
                    [
                        new DictionaryCriteria<PropertyCriterion<string>>
                        {
                            Items = new Dictionary<string, PropertyCriterion<string>>
                            {
                                ["catalog"] = new() { Values = ["blue-note"] }
                            }
                        }
                    ]
                }
            },
            _ => throw new InvalidOperationException("criterion")
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenKnownEmptyRetainsAllUnsupportedCriterionWarnings()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.GreaterThan,
                    Values = ["Kind of Blue"]
                }]
            },
            Awards = new CollectionCriteria<AwardCriteria> { Some = [[new AwardCriteria()]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Is.EqualTo(["musicbrainz does not support this text field or operator."]));
        }
    }

    [Test]
    public void Plan_Result_WhenSeededQueryRelationshipCannotMatchArtistTarget()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.Refused), Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenProviderAbsentDataConditionMatchesAbsence()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria> { None = [new AwardCriteria()] },
            Previews = new CollectionCriteria<PreviewMetadataCriteria>
            {
                None = [new PreviewMetadataCriteria()]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                None = [new ContentRatingCriteria()]
            },
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["catalog"] = new()
                            {
                                Operator = CriteriaValueOperator.NotEquals,
                                Values = ["blue-note"]
                            },
                            ["missing"] = new() { Operator = CriteriaValueOperator.IsNull }
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase("awards")]
    [TestCase("previews")]
    [TestCase("content-ratings")]
    public void Plan_Result_WhenProviderAbsentCollectionCannotMatchNestedNullCondition(string criterionName)
    {
        // Arrange
        var criteria = criterionName switch
        {
            "awards" => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
                },
                Awards = new CollectionCriteria<AwardCriteria>
                {
                    Some = [[new AwardCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
                        }
                    }]]
                }
            },
            "previews" => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
                },
                Previews = new CollectionCriteria<PreviewMetadataCriteria>
                {
                    Some = [[new PreviewMetadataCriteria
                    {
                        Url = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
                        }
                    }]]
                }
            },
            "content-ratings" => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
                },
                ContentRatings = new CollectionCriteria<ContentRatingCriteria>
                {
                    Some = [[new ContentRatingCriteria
                    {
                        Rating = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
                        }
                    }]]
                }
            },
            _ => throw new InvalidOperationException("criterion")
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenCollectionSomeGroupsAreEmpty()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Awards = new CollectionCriteria<AwardCriteria> { Some = [[]] },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria> { Some = [[]] },
            References = new CollectionCriteria<ReferenceMetadataCriteria> { Some = [[]] },
            Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[]] },
            Images = new CollectionCriteria<ImageCriteria> { Some = [[]] },
            Previews = new CollectionCriteria<PreviewMetadataCriteria> { Some = [[]] },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria> { Some = [[]] },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria> { Some = [[]] },
            Genres = new CollectionCriteria<GenreCriteria> { Some = [[]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.IsNotNull, false)]
    [TestCase(CriteriaValueOperator.IsNull, true)]
    public void Plan_Result_WhenCustomMetadataPresenceOperatorIsApplied(
        CriteriaValueOperator valueOperator,
        bool negate)
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
                            ["catalog"] = new() { Operator = valueOperator, Negate = negate }
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenGenreContainsWithTitleSeed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
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
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["jazz"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.Genre));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.Hit));
            Assert.That(
                residual.Alternatives.Single().Predicates.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("broader tag data"));
        }
    }

    [Test]
    public void Plan_Result_WhenReferenceCriteriaAreExpressible()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["official homepage"] }]
                            },
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri>
                                    {
                                        Operator = CriteriaValueOperator.StartsWithString,
                                        Values = [new Uri("https://example.com/artist/")]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.Reference));
            Assert.That(residual.Need, Is.EqualTo(ResidualNeed.EntityUrlRelations));
            Assert.That(
                residual.Alternatives.Single().Predicates.Select(predicate => predicate.Field),
                Is.EqualTo(
                [
                    ResidualField.ReferenceKind,
                    ResidualField.ReferenceUrl
                ]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenReferenceCriteriaCannotBeEvaluated()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some = [[new ReferenceMetadataCriteria
                {
                    Ordinal = new CriteriaSet<PropertyCriterion<int>>
                    {
                        And = [new PropertyCriterion<int> { Values = [1] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters.Single().Kind,
                Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Is.EqualTo(
                [
                    "musicbrainz reference ordinal, display-name, and language criteria cannot be evaluated."
                ]));
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumImagePresenceIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Images = new CollectionCriteria<ImageCriteria> { Some = [[new ImageCriteria()]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Kind,
                Is.EqualTo(ResidualKind.ImagePresence));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Need,
                Is.EqualTo(ResidualNeed.CoverArt));
            Assert.That(
                plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Alternatives.Single()
                    .Predicates
                    .Single()
                    .Operator,
                Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(false, CriteriaValueOperator.IsNull, true)]
    [TestCase(false, CriteriaValueOperator.IsNotNull, false)]
    [TestCase(true, CriteriaValueOperator.IsNull, false)]
    [TestCase(true, CriteriaValueOperator.IsNotNull, true)]
    public void Plan_Result_WhenAlbumImageNullOperatorIsRequested(
        bool useMimeType,
        CriteriaValueOperator valueOperator,
        bool knownEmpty)
    {
        // Arrange
        var image = useMimeType
            ? new ImageCriteria
            {
                MimeType = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Operator = valueOperator }]
                }
            }
            : new ImageCriteria
            {
                Url = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Operator = valueOperator }]
                }
            };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some = [[image]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty),
                Is.EqualTo(knownEmpty));
            if (knownEmpty)
            {
                Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            }
            else
            {
                var predicate = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single()
                    .Alternatives
                    .Single()
                    .Predicates
                    .Single();
                Assert.That(predicate.Field, Is.EqualTo(ResidualField.ImagePresence));
                Assert.That(predicate.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            }

            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase("Type", "cover", false)]
    [TestCase("Type", "poster", true)]
    [TestCase("Source", "musicbrainz", false)]
    [TestCase("Source", "foreign", true)]
    public void Plan_Result_WhenAlbumImageKnownStringFacetIsRequested(
        string field,
        string value,
        bool knownEmpty)
    {
        // Arrange
        var image = field == "Type"
            ? new ImageCriteria
            {
                Type = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = [value] }]
                }
            }
            : new ImageCriteria
            {
                Source = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = [value] }]
                }
            };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Images = new CollectionCriteria<ImageCriteria> { Some = [[image]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty),
                Is.EqualTo(knownEmpty));
            if (knownEmpty)
            {
                Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            }
            else
            {
                Assert.That(
                    plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals,
                    Has.Count.EqualTo(1));
            }

            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumImageUrlIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["https://archive.org/front.jpg"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var predicates = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single().Alternatives
            .Single()
            .Predicates;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                predicates.Select(predicate => predicate.Field),
                Is.EqualTo(
                [
                    ResidualField.ImagePresence,
                    ResidualField.ImageUrl
                ]));
            Assert.That(predicates[1].Values, Is.EqualTo(["https://archive.org/front.jpg"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumImageNoneCannotMatchConceptualCover()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                None =
                [
                    new ImageCriteria
                    {
                        MimeType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.IsNotNull
                            }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenArtistImagePresenceIsRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Images = new CollectionCriteria<ImageCriteria> { Some = [[new ImageCriteria()]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenOnlyResidualCriteriaAreRequested()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["jazz"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(
                plan.TraceProvenance.Criteria.Single().FinalDisposition &
                QueryTraceProvenance.Disposition.Residual,
                Is.EqualTo(QueryTraceProvenance.Disposition.Residual));
            Assert.That(plan.Warnings.Single(), Does.Contain("positive"));
        }
    }

    [Test]
    public void Plan_Result_WhenResidualSemanticsDiffer()
    {
        // Arrange
        var jazzCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["jazz"] }]
                        }
                    }
                ]
            }
        };
        var bluesCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["blues"] }]
                        }
                    }
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var jazz = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = jazzCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var blues = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = bluesCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        Assert.That(jazz.Fingerprint, Is.Not.EqualTo(blues.Fingerprint));
    }

    [TestCase(Constants.ProviderType, (int)ArtistCompositionEntity.ReleaseGroup)]
    [TestCase(Constants.ProviderType, (int)ArtistCompositionEntity.Recording)]
    [TestCase(Constants.ProviderType, (int)ArtistCompositionEntity.Work)]
    public void Plan_Result_WhenArtistRelationshipUsesDirectRelatedMbid(
        string source,
        int expectedEntityValue)
    {
        // Arrange
        var expectedEntity = (ArtistCompositionEntity)expectedEntityValue;
        var targetKind = expectedEntity switch
        {
            ArtistCompositionEntity.ReleaseGroup or ArtistCompositionEntity.Release =>
                Constants.AlbumLevelSlug,
            ArtistCompositionEntity.Recording => Constants.TrackLevelSlug,
            _ => Constants.WorkSubjectKind
        };
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
                                    new PropertyCriterion<string>
                                    {
                                        Values = [Constants.ArtistRelationshipKind]
                                    }
                                ]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [targetKind] }]
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
                                                And = [new PropertyCriterion<string> { Values = [source] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And =
                                                [
                                                    new PropertyCriterion<string>
                                                    {
                                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                                    }
                                                ]
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
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Browse));
            Assert.That(composition.RelatedEntity, Is.EqualTo(expectedEntity));
            Assert.That(composition.Resolution, Is.EqualTo(ArtistCompositionResolution.KnownMbid));
            Assert.That(
                composition.FinalStage,
                Is.EqualTo(ArtistCompositionFinalStage.ReturnExpandedCandidates));
        }
    }

    [Test]
    public void Plan_Result_WhenDirectArtistExpansionCanSettleResiduals()
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
                                    new PropertyCriterion<string>
                                    {
                                        Values = [Constants.ArtistRelationshipKind]
                                    }
                                ]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                                    }
                                                ]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                ]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                None =
                [
                    new AlternateTextCriteria
                    {
                        Text = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Forbidden"] }]
                        }
                    }
                ]
            },
            Genres = new CollectionCriteria<GenreCriteria>
            {
                None =
                [
                    new GenreCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["jazz"] }]
                        }
                    }
                ]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["official homepage"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                composition.FinalStage,
                Is.EqualTo(ArtistCompositionFinalStage.ReturnExpandedCandidates));
            Assert.That(composition.Expansion, Is.EqualTo(ArtistCompositionExpansion.BrowseArtists));
            Assert.That(
                composition.ExpansionCapabilities,
                Is.EqualTo(
                    LookupCapability.Aliases |
                    LookupCapability.Genres |
                    LookupCapability.UrlRelations));
        }
    }

    [Test]
    public void Plan_Result_WhenArtistExpansionWillNotReturnBrowseCandidates()
    {
        // Arrange
        var browseCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Artist"] }]
            },
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
                                    new PropertyCriterion<string>
                                    {
                                        Values = [Constants.ArtistRelationshipKind]
                                    }
                                ]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                                    }
                                                ]
                                            }
                                        }
                                    ]
                                ]
                            }
                        }
                    ]
                ]
            },
            AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
            {
                None =
                [
                    new AlternateTextCriteria
                    {
                        Text = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Forbidden"] }]
                        }
                    }
                ]
            }
        };
        var bandCriteria = browseCriteria with
        {
            Title = new CriteriaSet<PropertyCriterion<string>>(),
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
                                    new PropertyCriterion<string>
                                    {
                                        Values = [Constants.BandMembershipRelationshipKind]
                                    }
                                ]
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                                        Values = ["22222222-2222-4222-8222-222222222222"]
                                                    }
                                                ]
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

        var planner = new QueryPlanner();

        // Act
        var composedPlan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = browseCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var bandPlan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = bandCriteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var composed = composedPlan.Root!.Flatten().OfType<ResolvePlanNode>().Single().Composition;
        var band = bandPlan.Root!.Flatten().OfType<ResolvePlanNode>().Single().Composition;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composed.FinalStage, Is.EqualTo(ArtistCompositionFinalStage.RunComposedSearch));
            Assert.That(composed.ExpansionCapabilities, Is.EqualTo(LookupCapability.None));
            Assert.That(
                band.FinalStage,
                Is.EqualTo(ArtistCompositionFinalStage.ReturnExpandedCandidates));
            Assert.That(band.Expansion, Is.EqualTo(ArtistCompositionExpansion.BandMembers));
            Assert.That(band.ExpansionCapabilities, Is.EqualTo(LookupCapability.None));
        }
    }

    [Test]
    public void Plan_Result_WhenArtistRelationshipFacetsUseLegacySwappedForm()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        Assert.That(
            plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList().Single()
                .RelatedEntity,
            Is.EqualTo(ArtistCompositionEntity.ReleaseGroup));
    }

    [Test]
    public void Plan_Result_WhenNameOnlyAlbumRelationshipUsesReleaseGroupResolution()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composition.RelatedEntity, Is.EqualTo(ArtistCompositionEntity.ReleaseGroup));
            Assert.That(composition.Resolution, Is.EqualTo(ArtistCompositionResolution.ExactNameSearch));
            Assert.That(composition.RelatedName, Is.EqualTo("Kind of Blue"));
        }
    }

    [Test]
    public void Plan_Result_WhenMultipleArtistRelationshipsCompose()
    {
        // Arrange
        var first = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var second = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.TrackLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["22222222-2222-4222-8222-222222222222"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[first], [second]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList(),
                Has.Count.EqualTo(2));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenArtistRelationshipAlternativesUseUnion()
    {
        // Arrange
        var first = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var second = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.TrackLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["22222222-2222-4222-8222-222222222222"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[first, second]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var union = filter?.Child as UnionPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(union, Is.Not.Null);
            Assert.That(union!.Children, Has.Count.EqualTo(2));
            Assert.That(union.Children, Has.All.TypeOf<FetchPlanNode>());
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenArtistRelationshipAlternativeIsEmptyKeepsViableAlternative()
    {
        // Arrange
        var viable = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var empty = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["unsupported relationship"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[empty, viable]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(
                plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList(),
                Has.Count.EqualTo(1));
            Assert.That(plan.Root, Is.TypeOf<FilterPlanNode>());
        }
    }

    [Test]
    public void Plan_Result_WhenReverseRelationshipCombinesWithTitleUsesAnchoredSearch()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                },
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var search = filter?.Child as FetchPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(search, Is.Not.Null);
            Assert.That(search!.AccessMode, Is.EqualTo(AccessMode.Search));
            Assert.That(search.AnchorKind, Is.EqualTo(AnchorKind.ReverseRelationship));
            Assert.That(search.AnchorSource, Is.TypeOf<FetchPlanNode>());
            Assert.That(plan.Root!.Flatten().Count(node => node is FetchPlanNode), Is.EqualTo(2));
        }
    }

    [Test]
    public void Plan_Result_WhenBandMembershipTargetsPersonSubjectKind()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.BandMembershipRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.PersonSubjectKind] }]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Member"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composition.RelatedEntity, Is.EqualTo(ArtistCompositionEntity.Artist));
            Assert.That(composition.RelatedArtistType, Is.EqualTo(Constants.PersonArtistType));
        }
    }

    [Test]
    public void Plan_Result_WhenBandMembershipUsesExactCanonicalFacets()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.BandMembershipRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composition.RelatedEntity, Is.EqualTo(ArtistCompositionEntity.Artist));
            Assert.That(composition.Expansion, Is.EqualTo(ArtistCompositionExpansion.BandMembers));
        }
    }

    [Test]
    public void Plan_Result_WhenArtistTargetFacetOmitsBandMembershipRole()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.True);
            Assert.That(
                plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList(),
                Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenNegativeBandMembershipHasIndependentSeed()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.BandMembershipRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Queen"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria> { None = [relationship] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composition.Constraint, Is.EqualTo(ArtistCompositionConstraint.Exclude));
            Assert.That(composition.FinalStage, Is.EqualTo(ArtistCompositionFinalStage.RunComposedSearch));
        }
    }

    [TestCase(CriteriaValueOperator.NotEquals, false, true, CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.Equals, true, true, CriteriaValueOperator.Equals)]
    public void Plan_Result_WhenOriginalTitleUsesTitleOrAliasNegation(
        CriteriaValueOperator valueOperator,
        bool negate,
        bool expectedCollectionNegate,
        CriteriaValueOperator expectedPredicateOperator)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Nevermind"] }]
            },
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = valueOperator,
                    Negate = negate,
                    Values = ["ニルヴァーナ"]
                }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var residual = plan.Root!.Flatten().OfType<FilterPlanNode>().Single().Residuals.Single();
        var predicate = residual.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residual.Kind, Is.EqualTo(ResidualKind.AlternateText));
            Assert.That(residual.Negate, Is.EqualTo(expectedCollectionNegate));
            Assert.That(residual.IncludePrimaryTitle, Is.True);
            Assert.That(predicate.Operator, Is.EqualTo(expectedPredicateOperator));
            Assert.That(predicate.Negate, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.IsNull, false, true)]
    [TestCase(CriteriaValueOperator.IsNotNull, false, false)]
    [TestCase(CriteriaValueOperator.IsNull, true, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, true, true)]
    public void Plan_Result_WhenOriginalTitlePresenceUsesMandatoryTitleInvariant(
        CriteriaValueOperator valueOperator,
        bool negate,
        bool expectedKnownEmpty)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = valueOperator, Negate = negate }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty),
                Is.EqualTo(expectedKnownEmpty));
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Result_WhenPositiveBandMembershipHasIndependentSeedStillFailsClosed()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.BandMembershipRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Member"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        Assert.That(
            plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList(),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void Plan_Result_WhenNegativeBandMembershipHasNoIndependentSeed()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.BandMembershipRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { None = [relationship] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root!).Children, Is.Empty);
            Assert.That(
                plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition).ToList(),
                Is.Empty);
            Assert.That(plan.Warnings.Select(warning => warning), Has.Some.Contains("positive"));
        }
    }

    [Test]
    public void Plan_Result_WhenArtistRelationshipCombinesNameAndMbid()
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.AlbumLevelSlug] }]
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            },
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = new MetadataCriteria
            {
                Relationships = new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
            },
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var composition = plan.Root!.Flatten().OfType<ResolvePlanNode>().Select(node => node.Composition)
            .ToList()
            .Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(composition.Resolution, Is.EqualTo(ArtistCompositionResolution.ExactNameSearch));
            Assert.That(composition.RelatedMbid, Is.EqualTo("11111111-1111-4111-8111-111111111111"));
            Assert.That(composition.RelatedName, Is.EqualTo("Kind of Blue"));
        }
    }

    [Test]
    public void Plan_Result_WhenPureAlbumArtistRelationshipUsesResolvedBrowse()
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
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var fetch = filter?.Child as FetchPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Browse));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.ResolvedArtist));
            Assert.That(fetch.Resolvers, Has.Count.EqualTo(1));
            Assert.That(fetch.Resolvers.Single().RelationshipFilterIndex, Is.EqualTo(0));
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumArtistRelationshipComposesWithTitleUsesOneSearchFetch()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Kind of Blue"] }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var fetch = filter?.Child as FetchPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Search));
            Assert.That(fetch.Resolvers, Has.Count.EqualTo(1));
            Assert.That(fetch.Filters, Has.Count.EqualTo(2));
            Assert.That(plan.Root!.Flatten().Count(node => node is FetchPlanNode), Is.EqualTo(1));
        }
    }

    [Test]
    public void Plan_Result_WhenAlbumContainsTitleAndTypedArtistNameComposeThroughResolveAndSearch()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.ContainsString,
                    Values = ["Kind of Blue"]
                }]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some = [[new RelationshipCriteria
                {
                    Kind = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.PersonSubjectKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.ContainsString,
                            Values = ["Miles Davis"]
                        }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var fetch = filter?.Child as FetchPlanNode;
        var resolver = fetch?.Resolvers.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Search));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.ResolvedArtist));
            Assert.That(fetch.Filters, Has.Count.EqualTo(2));
            Assert.That(resolver, Is.Not.Null);
            Assert.That(resolver!.Composition.RelatedName, Is.EqualTo("Miles Davis"));
            Assert.That(
                resolver.Composition.RelatedNameOperator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(resolver.Composition.RelatedArtistType, Is.EqualTo(Constants.PersonArtistType));
        }
    }

    [Test]
    public void Plan_Result_WhenPureTrackTypedArtistNameUsesResolveAndLinkedBrowse()
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
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.PersonSubjectKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string>
                        {
                            Operator = CriteriaValueOperator.ContainsString,
                            Values = ["Miles Davis"]
                        }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var filter = plan.Root as FilterPlanNode;
        var fetch = filter?.Child as FetchPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Browse));
            Assert.That(fetch.AnchorKind, Is.EqualTo(AnchorKind.ResolvedArtist));
            Assert.That(fetch.Resolvers, Has.Count.EqualTo(1));
            Assert.That(
                fetch.Resolvers.Single().Composition.RelatedNameOperator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
        }
    }

    [Test]
    public void Plan_Result_WhenArtistAndPersonFacetsUseSwappedRepresentation()
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
                        And = [new PropertyCriterion<string> { Values = [Constants.PersonSubjectKind] }]
                    },
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Subject = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = ["Miles Davis"] }]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var fetch = (plan.Root as FilterPlanNode)?.Child as FetchPlanNode;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.TraceProvenance.Includes(QueryTraceProvenance.Disposition.PlannedEmpty), Is.False);
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Browse));
            Assert.That(
                fetch.Resolvers.Single().Composition.RelatedArtistType,
                Is.EqualTo(Constants.PersonArtistType));
        }
    }

    [Test]
    public void Plan_Result_WhenTypedArtistMbidRequiresLookupBeforeTrackBrowse()
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
                        And = [new PropertyCriterion<string> { Values = [Constants.ArtistRelationshipKind] }]
                    },
                    Type = new CriteriaSet<PropertyCriterion<string>>
                    {
                        And = [new PropertyCriterion<string> { Values = [Constants.PersonSubjectKind] }]
                    },
                    ExternalIds = new CollectionCriteria<ExternalIdCriteria>
                    {
                        Some = [[new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["11111111-1111-4111-8111-111111111111"]
                                    }
                                ]
                            }
                        }]]
                    }
                }]]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Track,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        var fetch = (plan.Root as FilterPlanNode)?.Child as FetchPlanNode;
        var resolver = fetch?.Resolvers.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch, Is.Not.Null);
            Assert.That(fetch!.AccessMode, Is.EqualTo(AccessMode.Browse));
            Assert.That(fetch.DirectValue, Is.Null);
            Assert.That(resolver, Is.Not.Null);
            Assert.That(
                resolver!.Composition.Resolution,
                Is.EqualTo(ArtistCompositionResolution.TypedMbidLookup));
            Assert.That(
                resolver.Composition.RelatedMbid,
                Is.EqualTo("11111111-1111-4111-8111-111111111111"));
            Assert.That(resolver.Composition.RelatedArtistType, Is.EqualTo(Constants.PersonArtistType));
        }
    }

    [TestCase(Constants.IsniSource, "isni")]
    [TestCase(Constants.IpiSource, "ipi")]
    public void Plan_Result_WhenArtistAuthorityIdentitySourceUsesFieldedLuceneSearch(string source, string remoteField)
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
                                And = [new PropertyCriterion<string> { Values = [source] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["0000000123486830"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Artist,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });
        var filter = plan.Root!.Flatten().OfType<FetchPlanNode>().Single().Filters
            .Single(value => value.Kind == QueryFilterKind.ExternalId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Search));
            Assert.That(filter.IdentityValues.Single().RemoteField, Is.EqualTo(remoteField));
            Assert.That(filter.RemoteApplied, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesExactIdentity_WhenTwoExternalIdGroupsBothAllowTheSameMbid()
    {
        // Arrange
        const string mbid = "11111111-1111-4111-8111-111111111111";
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
                                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = [mbid] }]
                            }
                        }
                    ],
                    [new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string>
                            { Operator = CriteriaValueOperator.ContainsString, Values = ["musicbrainz"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string>
                            { Operator = CriteriaValueOperator.ContainsString, Values = ["11111111"] }]
                        }
                    }]
                ]
            }
        };
        var planner = new QueryPlanner();

        // Act
        var plan = planner.Plan(new QueryRequest
        {
            Target = QueryTarget.Album,
            Criteria = criteria,
            RequestedPage = 1,
            Settings = new Settings()
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().AccessMode,
                Is.EqualTo(AccessMode.Lookup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().EntityKind,
                Is.EqualTo(EntityKind.ReleaseGroup));
            Assert.That(
                plan.Root!.Flatten().OfType<FetchPlanNode>().Single().DirectValue,
                Is.EqualTo(mbid));
        }
    }
}
