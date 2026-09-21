using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class QueryPlannerTests
{
    [Test]
    public void Plan_ApproximateBoundedStrategy_WhenTitleMovie()
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
                        Values = ["Matrix"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 2);
        var disposition = result.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(result.Page, Is.EqualTo(2));
            Assert.That(result.Filters, Has.Count.EqualTo(1));
            Assert.That(result.UsesLocalPaging, Is.True);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("Title.And[0]"));
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.ServerFilter));
            Assert.That(disposition.Reason, Is.Not.Empty);
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
                        Value = "The Matrix",
                        Values = [null!, "Matrix Reloaded"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        var filter = plan.Filters.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.Values, Is.EqualTo(["The Matrix", "Matrix Reloaded"]));
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
        }
    }

    [Test]
    public void Plan_ExactStructuredStrategy_WhenShowDateAndLanguage()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["en"] }]
            },
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                        Values =
                        [
                            new TemporalValue
                            {
                                Value = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                                Precision = TemporalPrecision.Year
                            }
                        ]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Filters, Has.Count.EqualTo(2));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesLocalPaging_WhenContainsWord()
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
                        Operator = CriteriaValueOperator.ContainsWord,
                        Values = ["office"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);

        // Assert
        Assert.That(result.UsesLocalPaging, Is.True);
    }

    [Test]
    public void Plan_ReturnsEmptyAndDropsTheTerm_WhenOrdinaryTermContainsOnlyQuotes()
    {
        // Arrange
        var criteria = new MetadataCriteria();
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(
            EntityKind.Movie,
            criteria,
            1,
            " \"\"\" ");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.OrdinaryTerm, Is.Null);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenNegativeOnly()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Negate = true, Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithNoBoundWarnings_WhenRegexIsTheOnlyCriterion()
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
                        Values = ["^The"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = result.ResidualFilters.Single();
        var disposition = result.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.UsesLocalPaging, Is.True);
            Assert.That(residual.Kind, Is.EqualTo(ResidualFilterKind.Text));
            Assert.That(residual.Source, Is.EqualTo(nameof(MetadataCriteria.Title)));
            Assert.That(residual.Hydration, Is.EqualTo(ResidualHydration.None));
            Assert.That(residual.TextField, Is.EqualTo(ResidualTextField.Title));
            Assert.That(residual.Criterion!.Operator, Is.EqualTo(CriteriaValueOperator.Regex));
            Assert.That(residual.Criterion.Values, Is.EqualTo(["^The"]));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("Title.And[0]"));
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Residual));
            Assert.That(disposition.Reason, Does.Contain("no selective server bound"));
        }
    }

    [Test]
    public void Plan_CompilesTitleRegexResidual_WhenPositiveTitleBoundExists()
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
                        Values = ["Matrix"]
                    },
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Negate = true,
                        Values = ["^The"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.TextField, Is.EqualTo(ResidualTextField.Title));
            Assert.That(residual.Hydration, Is.EqualTo(ResidualHydration.None));
            Assert.That(residual.Criterion!.Negate, Is.True);
            Assert.That(plan.UsesLocalPaging, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(
        nameof(MetadataCriteria.OriginalTitle),
        nameof(ResidualTextField.OriginalTitle),
        "Entity, Support")]
    [TestCase(
        nameof(MetadataCriteria.Overview),
        nameof(ResidualTextField.Overview),
        nameof(ResidualHydration.None))]
    public void Plan_CompilesEntityOrHitTextRegexResidual_WhenTitleBoundExists(
        string fieldName,
        string expectedField,
        string expectedHydration)
    {
        // Arrange
        var criterion = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.Regex,
            Values = ["matrix$"]
        };
        var set = new CriteriaSet<PropertyCriterion<string>> { And = [criterion] };
        var title = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
        };
        var criteria = fieldName == nameof(MetadataCriteria.OriginalTitle)
            ? new MetadataCriteria { Title = title, OriginalTitle = set }
            : new MetadataCriteria { Title = title, Overview = set };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(residual.Source, Is.EqualTo(fieldName));
            Assert.That(residual.TextField.ToString(), Is.EqualTo(expectedField));
            Assert.That(residual.Hydration.ToString(), Is.EqualTo(expectedHydration));
            Assert.That(residual.Criterion!.Values, Is.EqualTo(["matrix$"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DoesNotEnterQueryPlan_WhenUnknownPropertyId()
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
                                And = [new PropertyCriterion<string> { Values = ["P999999"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["unsafe"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = result.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.ResidualFilters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_UsesSupportedExternalIdAlternative_WhenSiblingAlternativeIsUnsupported()
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
                                And = [new PropertyCriterion<string> { Values = ["P999999"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["unsafe"] }]
                            }
                        },
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.ExternalId));
            Assert.That(filter.Alternatives.Single().ExternalIds.Single().Value, Is.EqualTo("tt0133093"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsSupportedExternalIdSource_WhenSiblingSourceIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [[
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["imdb", "tvdb"]
                                }
                            ]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var externalIds = plan.Filters.Single().Alternatives.Single().ExternalIds;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(externalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(externalIds.Single().Value, Is.EqualTo("tt0133093"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExternalIdExclusionSourceIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                None =
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
                                    Values = ["imdb", "tvdb"]
                                }
                            ]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions[1].Disposition,
                Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_IsUnsupported_WhenSeasonSearch()
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Season, new MetadataCriteria(), 1);
        var disposition = result.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Unsupported));
            Assert.That(disposition.Source, Is.EqualTo(nameof(QueryPlan.EntityKind)));
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
            Assert.That(disposition.Reason, Does.Contain("entity kind"));
        }
    }

    [TestCase(CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.In)]
    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    public void Plan_AreMapped_WhenSupportedTitleOperators(CriteriaValueOperator valueOperator)
    {
        // Arrange
        var values = valueOperator == CriteriaValueOperator.In
            ? new[] { "Matrix", "Reloaded" }
            : ["Matrix"];
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = valueOperator, Values = values }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        Assert.That(result.Filters.Single().Operator, Is.EqualTo(valueOperator));
    }

    [TestCase(CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.In)]
    [TestCase(CriteriaValueOperator.GreaterThan)]
    [TestCase(CriteriaValueOperator.GreaterThanOrEqual)]
    [TestCase(CriteriaValueOperator.LessThan)]
    [TestCase(CriteriaValueOperator.LessThanOrEqual)]
    [TestCase(CriteriaValueOperator.IsNull)]
    [TestCase(CriteriaValueOperator.IsNotNull)]
    public void Plan_AreExact_WhenSupportedDateOperators(CriteriaValueOperator valueOperator)
    {
        // Arrange
        var temporalValues = valueOperator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull
            ? Array.Empty<TemporalValue>()
            :
            [
                new TemporalValue
                {
                    Value = new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Day
                }
            ];
        var criteria = new MetadataCriteria
        {
            PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = valueOperator, Values = temporalValues }]
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
                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var dateFilter = result.Filters.Single(filter => filter.Kind == QueryFilterKind.PublishDate);

        // Assert
        Assert.That(dateFilter.Operator, Is.EqualTo(valueOperator));
    }

    [TestCase(CriteriaValueOperator.NotEquals, CriteriaValueOperator.Equals)]
    [TestCase(CriteriaValueOperator.NotIn, CriteriaValueOperator.In)]
    [TestCase(CriteriaValueOperator.NotContainsString, CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString, CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString, CriteriaValueOperator.EndsWithString)]
    public void Plan_PreserveOneNegation_WhenNormalizedNegativeTitleOperators(
        CriteriaValueOperator input,
        CriteriaValueOperator expected)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = input, Values = ["Matrix"] }]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q83495"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var titleFilter = result.Filters.Single(filter => filter.Kind == QueryFilterKind.Title);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(titleFilter.Operator, Is.EqualTo(expected));
            Assert.That(titleFilter.Negate, Is.True);
        }
    }

    [Test]
    public void Plan_MapsToTheDirectorProperty_WhenRelationshipKindAndTypeIntersect()
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
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["person"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q123"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var relationship = result.Filters.Single();

        // Assert
        Assert.That(relationship.Alternatives.Single().PropertyIds, Is.EqualTo(["P57"]));
    }

    [TestCase("producer", Constants.ProducerPropertyId)]
    [TestCase("cinematographer", Constants.CinematographerPropertyId)]
    [TestCase("editor", Constants.FilmEditorPropertyId)]
    [TestCase("composer", Constants.ComposerPropertyId)]
    [TestCase("executive producer", Constants.ExecutiveProducerPropertyId)]
    [TestCase("production designer", Constants.ProductionDesignerPropertyId)]
    [TestCase("costume designer", Constants.CostumeDesignerPropertyId)]
    [TestCase("narrator", Constants.NarratorPropertyId)]
    [TestCase("presenter", Constants.PresenterPropertyId)]
    [TestCase("network", Constants.OriginalBroadcasterPropertyId)]
    [TestCase("distributor", Constants.DistributorPropertyId)]
    public void Plan_MapsSingleProperty_WhenRelationshipRoleIsKnown(string role, string propertyId)
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = role
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
                                        Value = "Q123"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Filters.Single().Alternatives.Single().PropertyIds, Is.EqualTo([propertyId]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapsAggregateProperties_WhenRelationshipRoleIsAggregate()
    {
        // Arrange
        var personCriteria = new MetadataCriteria
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "person"
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
                                        Value = "Q123"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var crewCriteria = new MetadataCriteria
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "crew"
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
                                        Value = "Q123"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var studioCriteria = new MetadataCriteria
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "studio"
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
                                        Value = "Q123"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var person = subject.Plan(EntityKind.Movie, personCriteria, 1);
        var crew = subject.Plan(EntityKind.Movie, crewCriteria, 1);
        var studio = subject.Plan(EntityKind.Movie, studioCriteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(person.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(crew.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(studio.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(
                person.Filters.Single().Alternatives.Single().PropertyIds,
                Is.EqualTo(
                [
                    "P161",
                    "P725",
                    "P57",
                    "P58",
                    "P162",
                    "P344",
                    "P1040",
                    "P86",
                    "P1431",
                    "P2554",
                    "P2515",
                    "P2438",
                    "P371"
                ]));
            Assert.That(
                crew.Filters.Single().Alternatives.Single().PropertyIds,
                Is.EqualTo(
                [
                    "P57",
                    "P58",
                    "P162",
                    "P344",
                    "P1040",
                    "P86",
                    "P1431",
                    "P2554",
                    "P2515",
                    "P2438",
                    "P371"
                ]));
            Assert.That(
                studio.Filters.Single().Alternatives.Single().PropertyIds,
                Is.EqualTo(["P272", "P449", "P750"]));
            Assert.That(person.Warnings, Is.Empty);
            Assert.That(crew.Warnings, Is.Empty);
            Assert.That(studio.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    public void Plan_MapsApproximateRelationshipName_WhenStructuredSubjectOperatorIsPositiveFuzzy(
        CriteriaValueOperator valueOperator)
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
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = valueOperator,
                                        Values = ["Q42"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();
        var alternative = filter.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Relationship));
            Assert.That(alternative.Qids, Is.Empty);
            Assert.That(alternative.Names, Is.EqualTo(["Q42"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString)]
    public void Plan_MapsApproximateRelationshipName_WhenCustomSubjectOperatorIsPositiveFuzzy(
        CriteriaValueOperator valueOperator)
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
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = valueOperator,
                                Values = ["Q42"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();
        var alternative = filter.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Relationship));
            Assert.That(alternative.Qids, Is.Empty);
            Assert.That(alternative.Names, Is.EqualTo(["Q42"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesResidualWithoutServerBound_WhenRelationshipNoneUsesFuzzySubject()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None =
                [
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = ["Laurence Fishburne"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = result.ResidualFilters.Single();
        var relationship = residual.RelationshipGroup!;
        var subjectFilter = relationship.Alternatives.Single().Subjects.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.UsesLocalPaging, Is.True);
            Assert.That(residual.Source, Is.EqualTo(nameof(MetadataCriteria.Relationships)));
            Assert.That(
                residual.Hydration,
                Is.EqualTo(
                    ResidualHydration.Entity | ResidualHydration.Support));
            Assert.That(relationship.OuterNegate, Is.True);
            Assert.That(subjectFilter.Criterion!.Operator, Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(subjectFilter.Criterion.Negate, Is.False);
            Assert.That(subjectFilter.Names, Is.EqualTo(["Laurence Fishburne"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString, true, CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString, false, CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.ContainsWord, true, CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.StartsWithString, true, CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString, false, CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString, true, CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString, false, CriteriaValueOperator.EndsWithString)]
    public void Plan_CompilesResidualWithCoarseBound_WhenStructuredFuzzySubjectIsNegated(
        CriteriaValueOperator valueOperator,
        bool negate,
        CriteriaValueOperator expectedOperator)
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
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = valueOperator,
                                        Negate = negate,
                                        Values = ["Laurence Fishburne"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = result.Filters.Single();
        var residual = result.ResidualFilters.Single();
        var subjectFilter = residual.RelationshipGroup!.Alternatives.Single().Subjects.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(coarse.Kind, Is.EqualTo(QueryFilterKind.Relationship));
            Assert.That(coarse.Alternatives.Single().PropertyIds, Is.Not.Empty);
            Assert.That(coarse.Alternatives.Single().Names, Is.Empty);
            Assert.That(subjectFilter.Criterion!.Operator, Is.EqualTo(expectedOperator));
            Assert.That(subjectFilter.Criterion.Negate, Is.True);
            Assert.That(subjectFilter.Names, Is.EqualTo(["Laurence Fishburne"]));
            Assert.That(result.UsesLocalPaging, Is.True);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.ContainsString, true, CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.NotContainsString, false, CriteriaValueOperator.ContainsString)]
    [TestCase(CriteriaValueOperator.ContainsWord, true, CriteriaValueOperator.ContainsWord)]
    [TestCase(CriteriaValueOperator.StartsWithString, true, CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.NotStartsWithString, false, CriteriaValueOperator.StartsWithString)]
    [TestCase(CriteriaValueOperator.EndsWithString, true, CriteriaValueOperator.EndsWithString)]
    [TestCase(CriteriaValueOperator.NotEndsWithString, false, CriteriaValueOperator.EndsWithString)]
    public void Plan_CompilesResidual_WhenBoundedCustomFuzzySubjectIsNegated(
        CriteriaValueOperator valueOperator,
        bool negate,
        CriteriaValueOperator expectedOperator)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = valueOperator,
                                Negate = negate,
                                Values = ["Laurence Fishburne"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var bound = result.Filters.Single();
        var residual = result.ResidualFilters.Single();
        var subjectFilter = residual.RelationshipGroup!.Alternatives.Single().Subjects.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(bound.Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.Source, Is.EqualTo(RelationshipFieldKeys.Subject));
            Assert.That(residual.RelationshipGroup.OuterNegate, Is.True);
            Assert.That(subjectFilter.Criterion!.Operator, Is.EqualTo(expectedOperator));
            Assert.That(subjectFilter.Criterion.Negate, Is.False);
            Assert.That(subjectFilter.Names, Is.EqualTo(["Laurence Fishburne"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithNoBoundWarnings_WhenCustomFuzzyExclusionIsResidualOnly()
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
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Negate = true,
                                Values = ["Laurence Fishburne"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.ResidualFilters.Single().RelationshipGroup!.OuterNegate, Is.True);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ConstrainsFuzzyExclusion_WhenRelationshipKindIsPaired()
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
                            [RelationshipFieldKeys.Kind] = new PropertyCriterion<string>
                            {
                                Values = ["director"]
                            },
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Negate = true,
                                Values = ["Smith"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single().RelationshipGroup!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(plan.Filters.Single().Alternatives.Single().PropertyIds, Is.EqualTo(["P57"]));
            Assert.That(residual.OuterNegate, Is.True);
            Assert.That(residual.Alternatives.Single().PropertyIds, Is.EqualTo(["P57"]));
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenRelatedMetadataContainerId()
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
                            RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
                            {
                                And = [new PropertyCriterion<Guid> { Values = [Guid.NewGuid()] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.ResidualFilters, Is.Empty);
            Assert.That(
                result.CriterionDispositions.Single().Disposition,
                Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PreservesServerFacets_WhenQualifierNeedsResidualEvaluation()
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
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q123"] }]
                            },
                            Qualifier = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["Neo"]
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
                                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["nm0000206"] }]
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
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = plan.Filters.Single().Alternatives.Single();
        var residual = plan.ResidualFilters.Single();
        var alternative = residual.RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(coarse.PropertyIds, Is.EqualTo(["P161", "P725"]));
            Assert.That(coarse.Qids, Is.EqualTo(["Q123"]));
            Assert.That(coarse.Names, Is.Empty);
            Assert.That(coarse.ExternalIdGroups.Single().ExternalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(alternative.Subjects.Single().Qids, Is.EqualTo(["Q123"]));
            Assert.That(
                alternative.Qualifiers.Single().Criterion!.Operator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(alternative.Qualifiers.Single().Criterion!.Values, Is.EqualTo(["Neo"]));
            Assert.That(
                alternative.ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
            Assert.That(
                residual.Hydration,
                Is.EqualTo(
                    ResidualHydration.Entity | ResidualHydration.Support));
            Assert.That(plan.UsesLocalPaging, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CombinesSubjectAndCriteria_WhenRelationshipNeedsResidualEvaluation()
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
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["Smith"]
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Negate = true,
                                        Values = ["John"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Subjects, Has.Count.EqualTo(2));
            Assert.That(alternative.Subjects[0].Criterion!.Negate, Is.False);
            Assert.That(alternative.Subjects[1].Criterion!.Negate, Is.True);
            Assert.That(plan.Filters.Single().Alternatives.Single().Names, Is.EqualTo(["Smith"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PrefersExactSubjectAfterFuzzySubject_WhenRelationshipResidualCoarseBound()
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
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Values = ["Smith"]
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Values = ["Q42"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = plan.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coarse.Qids, Is.EqualTo(["Q42"]));
            Assert.That(coarse.Names, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_OmitsMixedSubjectSelectorWhenExternalIdBounds_WhenRelationshipResidualCoarseBound()
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
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = ["Q42", "Alice"]
                                    }
                                ]
                            },
                            Qualifier = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Hero"] }]
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
                                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["nm0000206"] }]
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
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = plan.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coarse.Qids, Is.Empty);
            Assert.That(coarse.Names, Is.Empty);
            Assert.That(coarse.ExternalIdGroups.Single().ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithNoBoundWarnings_WhenNegatedQualifierGroupIsResidualOnly()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                None =
                [
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["director"] }]
                        },
                        Qualifier = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Neo"] }]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.ResidualFilters.Single().RelationshipGroup!.OuterNegate, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemainConjunctive_WhenRelationshipExternalIdGroups()
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
                                And = [new PropertyCriterion<string> { Values = ["cast"] }]
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
                                                And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["nm0000206"] }]
                                            }
                                        }
                                    ],
                                    [
                                        new ExternalIdCriteria
                                        {
                                            Source = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["tmdb"] }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Values = ["6384"] }]
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
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = result.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.ExternalIdGroups, Has.Count.EqualTo(2));
            Assert.That(alternative.ExternalIdGroups[0].ExternalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(
                alternative.ExternalIdGroups[1].ExternalIds.Select(value => value.PropertyId),
                Is.EqualTo(["P4985", "P11806"]));
        }
    }

    [Test]
    public void Plan_SkipsUnsupportedSource_WhenCustomRelationshipSubjectAndExternalId()
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
                            [RelationshipFieldKeys.Kind] = new PropertyCriterion<string>
                            {
                                Values = ["cast"]
                            },
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb", "unsupported"]
                            },
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Values = ["Alice"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = result.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Filters, Has.Count.EqualTo(1));
            Assert.That(alternative.PropertyIds, Is.EqualTo(["P161", "P725"]));
            Assert.That(alternative.Names, Is.EqualTo(["Alice"]));
            Assert.That(alternative.ExternalIdGroups.Single().ExternalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(alternative.ExternalIdGroups.Single().ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
        }
    }

    [Test]
    public void Plan_KeepFacetsOnSameRelationship_WhenNegatedCustomRelationshipSubjectAndExternalId()
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
                            [RelationshipFieldKeys.Kind] = new PropertyCriterion<string>
                            {
                                Values = ["cast"]
                            },
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            },
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Negate = true,
                                Values = ["Alice"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = result.Filters.Single().Alternatives.Single();
        var residual = result.ResidualFilters.Single().RelationshipGroup!;
        var residualAlternative = residual.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Filters, Has.Count.EqualTo(1));
            Assert.That(coarse.Names, Is.Empty);
            Assert.That(coarse.ExternalIdGroups.Single().ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
            Assert.That(residual.OuterNegate, Is.True);
            Assert.That(residualAlternative.Subjects.Single().Names, Is.EqualTo(["Alice"]));
            Assert.That(
                residualAlternative.ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
        }
    }

    [Test]
    public void Plan_PreservesPositiveExternalIdBound_WhenNegatedExactCustomRelationshipSubject()
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
                            [RelationshipFieldKeys.Kind] = new PropertyCriterion<string>
                            {
                                Values = ["cast"]
                            },
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            },
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Negate = true,
                                Values = ["Q42"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = result.Filters.Single().Alternatives.Single();
        var residual = result.ResidualFilters.Single().RelationshipGroup!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(coarse.ExternalIdGroups.Single().ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
            Assert.That(residual.OuterNegate, Is.True);
            Assert.That(residual.Alternatives.Single().Subjects.Single().Qids, Is.EqualTo(["Q42"]));
            Assert.That(
                residual.Alternatives.Single().ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenCustomRelationshipSubjectHasTooManyNames()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            },
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Values = ["one", "two", "three", "four", "five", "six"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                result.Filters.Single().Alternatives.Single().ExternalIdGroups.Single().ExternalIds.Single()
                    .Value,
                Is.EqualTo("nm0000206"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KeepsQidWhenNamesExceedLimit_WhenCustomRelationshipSubject()
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
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.In,
                                Values = ["Q42", "one", "two", "three", "four", "five", "six"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = result.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q42"]));
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenRelationshipHasTooManySubjectNames()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [[
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["a", "b", "c", "d", "e", "f"]
                                }
                            ]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.ExternalId));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().ExternalIds.Single().Value,
                Is.EqualTo("tt0133093"));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.CriterionDispositions[1].Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_KeepsQidWhenNamesExceedLimit_WhenRelationshipSubject()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["Q42", "one", "two", "three", "four", "five", "six"]
                                }
                            ]
                        },
                        Qualifier = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Hero"] }]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();
        var residual = plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q42"]));
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(residual.Subjects.Single().Qids, Is.EqualTo(["Q42"]));
            Assert.That(residual.Subjects.Single().Names, Is.Empty);
            Assert.That(residual.Qualifiers.Single().Criterion!.Values, Is.EqualTo(["Hero"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenRelationshipSubjectOperatorIsUnsupported()
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
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^Tom"]
                                }
                            ]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(plan.CriterionDispositions[1].Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_UsesTitleBound_WhenNegatedCustomRoleIsAbsent()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["relationship.subject.chef"] = new PropertyCriterion<string>
                            {
                                Negate = true,
                                Values = ["X"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions[1];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Omitted));
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenNegatedCustomCriterionCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.Regex,
                                Negate = true,
                                Values = ["^X"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions[1];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenCustomRoleIsAbsent()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            },
                            ["relationship.subject.chef"] = new PropertyCriterion<string>
                            {
                                Values = ["X"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions.Single(value =>
            value.Source == "CustomMetadata.And[0].relationship.subject.chef");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
        }
    }

    [TestCase("relationship.subject", CriteriaValueOperator.Regex, "^Tom")]
    public void Plan_ReturnsEmpty_WhenCustomRelationshipSubjectIsUnsupported(
        string key,
        CriteriaValueOperator valueOperator,
        string value)
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            },
                            [key] = new PropertyCriterion<string>
                            {
                                Operator = valueOperator,
                                Values = [value]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                result.Filters.Single().Alternatives.Single().ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenContentRatingOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [[
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [[
                    new ContentRatingCriteria
                    {
                        Body = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Value = "MPAA"
                                }
                            ]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.ExternalId));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().ExternalIds.Single().Value,
                Is.EqualTo("tt0133093"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenUnsupportedSubjectIsInAnotherDictionary()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            }
                        }
                    },
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            [RelationshipFieldKeys.Subject] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.Regex,
                                Values = ["^Tom"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().ExternalIdGroups.Single().ExternalIds.Single().Value,
                Is.EqualTo("nm0000206"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenDurationOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [[
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.ContainsString,
                        Values = [TimeSpan.FromMinutes(90)]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.ExternalId));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().ExternalIds.Single().Value,
                Is.EqualTo("tt0133093"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapToOneExactTypedFilter_WhenCustomRelationshipExternalIdKeys()
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
                            [RelationshipFieldKeys.Kind] = new PropertyCriterion<string>
                            {
                                Values = ["cast"]
                            },
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();
        var alternative = filter.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Relationship));
            Assert.That(alternative.PropertyIds, Is.EqualTo(["P161", "P725"]));
            Assert.That(alternative.ExternalIdGroups.Single().ExternalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(alternative.ExternalIdGroups.Single().ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
        }
    }

    [Test]
    public void Plan_DoNotPairAcrossGroups_WhenCustomRelationshipExternalIdKeys()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            }
                        }
                    },
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        Assert.That(result.Filters, Is.Empty);
    }

    [Test]
    public void Plan_MergesValues_WhenCustomKeysDifferOnlyByCase()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalId.ToUpperInvariant()] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000151"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var externalIds = result.Filters.Single().Alternatives.Single().ExternalIdGroups.Single().ExternalIds;

        // Assert
        Assert.That(
            externalIds.Select(externalId => externalId.Value),
            Is.EqualTo(["nm0000206", "nm0000151"]));
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenCaseVariantCustomKeyOperatorsDiffer()
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
                            [RelationshipFieldKeys.ExternalId] = new PropertyCriterion<string>
                            {
                                Values = ["nm0000206"]
                            },
                            [RelationshipFieldKeys.ExternalId.ToUpperInvariant()] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Values = ["nm0000151"]
                            },
                            [RelationshipFieldKeys.ExternalIdSource] = new PropertyCriterion<string>
                            {
                                Values = ["imdb"]
                            }
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(criteria.CustomMetadata.And.Single().Items, Has.Count.EqualTo(3));
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesSupportedRelationshipAlternative_WhenSiblingHasNoWikidataData()
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
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q123"] }]
                            },
                            Qualifier = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Neo"] }]
                            }
                        },
                        new RelationshipCriteria
                        {
                            RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
                            {
                                And = [new PropertyCriterion<Guid> { Values = [Guid.NewGuid()] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Filters.Single().Alternatives, Has.Count.EqualTo(1));
            Assert.That(result.ResidualFilters.Single().RelationshipGroup!.Alternatives, Has.Count.EqualTo(1));
            Assert.That(
                result.CriterionDispositions.Single().Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilterAndResidual));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesResidualRelationshipAlternative_WhenSiblingOperatorIsUnsupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["director"] }]
                        },
                        Qualifier = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Neo"] }]
                        }
                    },
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["cast"] }]
                        },
                        Qualifier = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.GreaterThan,
                                Values = ["Neo"]
                            }]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = plan.Filters.Single().Alternatives.Single();
        var residual = plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(coarse.PropertyIds, Is.EqualTo(["P57"]));
            Assert.That(residual.Qualifiers.Single().Criterion!.Values, Is.EqualTo(["Neo"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Plan_ReturnsEmpty_WhenRelationshipQualifierOperatorIsUnsupported(bool excluded)
    {
        // Arrange
        var relationship = new RelationshipCriteria
        {
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["director"] }]
            },
            Qualifier = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string>
                {
                    Operator = CriteriaValueOperator.GreaterThan,
                    Values = ["Neo"]
                }]
            }
        };
        var criteria = new MetadataCriteria
        {
            Relationships = excluded
                ? new CollectionCriteria<RelationshipCriteria> { None = [relationship] }
                : new CollectionCriteria<RelationshipCriteria> { Some = [[relationship]] }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesWholeRelationshipOrGroup_WhenOneAlternativeNeedsQualifierResidual()
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
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q123"] }]
                            }
                        },
                        new RelationshipCriteria
                        {
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["director"] }]
                            },
                            Qualifier = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Neo"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var coarse = plan.Filters.Single();
        var residual = plan.ResidualFilters.Single().RelationshipGroup!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coarse.Alternatives, Has.Count.EqualTo(2));
            Assert.That(coarse.Alternatives[0].Qids, Is.EqualTo(["Q123"]));
            Assert.That(coarse.Alternatives[1].PropertyIds, Is.EqualTo(["P57"]));
            Assert.That(residual.Alternatives, Has.Count.EqualTo(2));
            Assert.That(residual.Alternatives[0].Subjects.Single().Qids, Is.EqualTo(["Q123"]));
            Assert.That(
                residual.Alternatives[1].Qualifiers.Single().Criterion!.Values,
                Is.EqualTo(["Neo"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_IsApproximate_WhenUnresolvedGenreName()
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
                                And = [new PropertyCriterion<string> { Values = ["not-a-real-genre"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Plan_KeepsDistinctCanonicalIndexes_WhenCriteriaRepeatOnOneField()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Values = ["Matrix"] },
                    new PropertyCriterion<string> { Values = ["Reloaded"] }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Filters, Has.Count.EqualTo(2));
            Assert.That(
                result.CriterionDispositions.Select(value => value.Source),
                Is.EqualTo(["Title.And[0]", "Title.And[1]"]));
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmptyWithoutWarnings_WhenProviderTypeExcludesWikidata()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Values = ["wikidata"] },
                    new PropertyCriterion<string> { Values = ["tmdb"] }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = result.CriterionDispositions.Single(value =>
            value.Source == "ProviderType.And[1]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(
                result.CriterionDispositions.Select(value => value.Disposition),
                Is.EqualTo(
                [
                    CriterionDispositionKind.Omitted,
                    CriterionDispositionKind.ProvablyUnsatisfiable,
                    CriterionDispositionKind.Omitted
                ]));
            Assert.That(disposition.Source, Is.EqualTo("ProviderType.And[1]"));
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
            Assert.That(disposition.Reason, Does.Contain("provider type"));
        }
    }

    [Test]
    public void Plan_RecordsEveryCriterionDisposition_WhenProviderTypeExcludesWikidata()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Values = ["tmdb"] },
                    new PropertyCriterion<string> { Values = [Constants.ProviderType] }
                ]
            },
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = [Constants.ProviderType] }]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                result.CriterionDispositions.Select(value => (value.Source, value.Disposition)),
                Is.EqualTo(
                [
                    ("ProviderType.And[0]", CriterionDispositionKind.ProvablyUnsatisfiable),
                    ("ProviderType.And[1]", CriterionDispositionKind.Omitted),
                    ("Source.And[0]", CriterionDispositionKind.Omitted),
                    ("Title.And[0]", CriterionDispositionKind.Omitted)
                ]));
        }
    }

    [Test]
    public void Plan_ReturnsKnownEmptyWithoutWarnings_WhenNegatedProviderTypeTargetsWikidata()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Values = ["Wikidata"]
                    }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = result.CriterionDispositions.Single(value =>
            value.Source == "ProviderType.And[0]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("ProviderType.And[0]"));
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
            Assert.That(disposition.Reason, Does.Contain("provider type"));
        }
    }

    [Test]
    public void Plan_ProceedsUnchanged_WhenProviderTypeAcceptsWikidata()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["tmdb", "wikidata"]
                    }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(result.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                result.Warnings.Select(warning => warning),
                Has.None.Contains("ProviderType"));
        }
    }

    [TestCase(CriteriaValueOperator.Equals, "tmdb:movie:603")]
    [TestCase(CriteriaValueOperator.ContainsString, "tmdb")]
    [TestCase(CriteriaValueOperator.StartsWithString, "imdb")]
    public void Plan_ReturnsKnownEmptyWithoutWarnings_WhenSourceExcludesWikidata(
        CriteriaValueOperator valueOperator,
        string value)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Values = ["wikidata"] },
                    new PropertyCriterion<string> { Operator = valueOperator, Values = [value] }
                ]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = result.CriterionDispositions.Single(value => value.Source == "Source.And[1]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(
                result.CriterionDispositions.Select(candidate => candidate.Disposition),
                Is.EqualTo(
                [
                    CriterionDispositionKind.Omitted,
                    CriterionDispositionKind.ProvablyUnsatisfiable,
                    CriterionDispositionKind.Omitted
                ]));
            Assert.That(disposition.Source, Is.EqualTo("Source.And[1]"));
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
            Assert.That(disposition.Reason, Does.Contain("source key"));
        }
    }

    [TestCase(CriteriaValueOperator.Equals, "wikidata")]
    [TestCase(CriteriaValueOperator.Equals, "wikidata:movie:Q83495")]
    [TestCase(CriteriaValueOperator.StartsWithString, "wikidata")]
    [TestCase(CriteriaValueOperator.ContainsString, "wikidata")]
    public void Plan_ProceedsUnchanged_WhenSourceAcceptsWikidata(CriteriaValueOperator valueOperator, string value)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = valueOperator, Values = [value] }]
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(result.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(result.Warnings.Select(warning => warning), Has.None.Contains("Source"));
        }
    }

    [Test]
    public void Plan_MapsOriginalTitleFilter_WhenOperatorIsSupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.OriginalTitle));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapsOverviewFilter_WhenOperatorIsSupported()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Overview = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.ContainsString,
                        Values = ["heist"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Overview));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapsDurationFilter_WhenOperatorIsRange()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.LessThanOrEqual,
                        Values = [TimeSpan.FromHours(2)]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = result.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Duration));
            Assert.That(filter.DurationValues, Is.EqualTo([TimeSpan.FromHours(2)]));
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.Established), Constants.SeriesStartPropertyId)]
    [TestCase(nameof(MetadataCriteria.Dissolved), Constants.SeriesEndPropertyId)]
    public void Plan_MapsShowTemporalFieldFilter_WhenOperatorIsSupported(
        string kindName,
        string expectedPropertyId)
    {
        // Arrange
        var kind = Enum.Parse<QueryFilterKind>(kindName);
        var criterion = new CriteriaSet<PropertyCriterion<TemporalValue>>
        {
            And =
            [
                new PropertyCriterion<TemporalValue>
                {
                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                    Values =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]
                }
            ]
        };
        var criteria = kind switch
        {
            QueryFilterKind.Established => new MetadataCriteria { Established = criterion },
            _ => new MetadataCriteria { Dissolved = criterion }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);
        var filter = result.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Kind, Is.EqualTo(kind));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(filter.PropertyId, Is.EqualTo(expectedPropertyId));
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapsProductionStatusFilter_WhenValueIsEnded()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Ended"] }]
            },
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [
                    [
                        new ExternalIdCriteria
                        {
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q23572"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);
        var filter = result.Filters.Single(candidate => candidate.Kind == QueryFilterKind.ProductionStatus);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Values, Is.EqualTo(["Ended"]));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.PropertyId, Is.EqualTo("P582"));
            Assert.That(result.ResidualFilters, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithNoBoundWarnings_WhenProductionStatusResidualIsUnbounded()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["Returning Series"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);
        var residual = result.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(result.Filters, Is.Empty);
            Assert.That(residual.Kind, Is.EqualTo(ResidualFilterKind.ProductionStatus));
            Assert.That(residual.Source, Is.EqualTo(nameof(MetadataCriteria.ProductionStatus)));
            Assert.That(residual.Hydration, Is.EqualTo(ResidualHydration.None));
            Assert.That(residual.Criterion!.Values, Is.EqualTo(["Returning Series"]));
            Assert.That(result.UsesLocalPaging, Is.True);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesHitResidualWithoutRemoteGenreFilter_WhenExcludedGenreName()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = "Horror"
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();
        var group = residual.CollectionGroup!;
        var predicate = group.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Genre));
            Assert.That(residual.Hydration, Is.EqualTo(ResidualHydration.None));
            Assert.That(group.Kind, Is.EqualTo(ResidualCollectionKind.Genre));
            Assert.That(group.OuterNegate, Is.True);
            Assert.That(predicate.Field, Is.EqualTo(ResidualCollectionField.GenreName));
            Assert.That(predicate.StringValues, Is.EqualTo(["Horror"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_StaysServerSide_WhenExcludedGenreQid()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = "Q130232"
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(value => value.Kind == QueryFilterKind.Genre);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Negate, Is.True);
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q130232"]));
            Assert.That(filter.Alternatives.Single().Names, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_SplitQidAndNameChecks_WhenExcludedMixedGenreValues()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["Q130232", "Horror"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var group = plan.ResidualFilters.Single().CollectionGroup!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(value =>
                    value.Kind == QueryFilterKind.Genre));
            Assert.That(group.Kind, Is.EqualTo(ResidualCollectionKind.Genre));
            Assert.That(group.OuterNegate, Is.True);
            Assert.That(
                group.Alternatives.Single().Predicates.Single().StringValues,
                Is.EqualTo(["Horror"]));
            Assert.That(
                group.Alternatives.Single().Predicates.Single().EntityQids,
                Is.EqualTo(["Q130232"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesWholeProductionStatusResidual_WhenPositiveValuesAreMixed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["Ended", "Canceled"]
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
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Q23572"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);
        var residual = result.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(
                result.Filters.Any(filter => filter.Kind == QueryFilterKind.ProductionStatus),
                Is.False);
            Assert.That(residual.Criterion!.Operator, Is.EqualTo(CriteriaValueOperator.In));
            Assert.That(residual.Criterion.Negate, Is.False);
            Assert.That(residual.Criterion.Values, Is.EqualTo(["Ended", "Canceled"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesWholeProductionStatusResidual_WhenNegativeValuesAreMixed()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotIn,
                        Values = ["Ended", "Canceled"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var result = subject.Plan(EntityKind.Show, criteria, 1);
        var residual = result.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(
                result.Filters.Any(filter => filter.Kind == QueryFilterKind.ProductionStatus),
                Is.False);
            Assert.That(residual.Criterion!.Operator, Is.EqualTo(CriteriaValueOperator.In));
            Assert.That(residual.Criterion.Negate, Is.True);
            Assert.That(residual.Criterion.Values, Is.EqualTo(["Ended", "Canceled"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [TestCase("Movie", "Established")]
    [TestCase("Movie", "Dissolved")]
    [TestCase("Movie", "BirthDate")]
    [TestCase("Movie", "DeathDate")]
    [TestCase("Show", "BirthDate")]
    [TestCase("Show", "DeathDate")]
    public void Plan_ReturnsEmpty_WhenPositiveTemporalFieldDoesNotApply(
        string entityKindName,
        string filterKindName)
    {
        // Arrange
        var entityKind = Enum.Parse<EntityKind>(entityKindName);
        var filterKind = Enum.Parse<QueryFilterKind>(filterKindName);
        var temporalSet = new CriteriaSet<PropertyCriterion<TemporalValue>>
        {
            And =
            [
                new PropertyCriterion<TemporalValue>
                {
                    Operator = CriteriaValueOperator.Equals,
                    Negate = false,
                    Values =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]
                }
            ]
        };
        var criteria = filterKind switch
        {
            QueryFilterKind.Established => new MetadataCriteria { Established = temporalSet },
            QueryFilterKind.Dissolved => new MetadataCriteria { Dissolved = temporalSet },
            QueryFilterKind.BirthDate => new MetadataCriteria { BirthDate = temporalSet },
            QueryFilterKind.DeathDate => new MetadataCriteria { DeathDate = temporalSet },
            _ => throw new ArgumentOutOfRangeException(nameof(filterKind))
        };
        criteria = criteria with
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(entityKind, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.UsesLocalPaging, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.NotEquals, false)]
    [TestCase(CriteriaValueOperator.Equals, true)]
    [TestCase(CriteriaValueOperator.IsNull, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, true)]
    public void Plan_OmitsTemporalField_WhenInapplicableCriterionIsVacuous(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        // Arrange
        var temporalValues = valueOperator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull
            ? Array.Empty<TemporalValue>()
            :
            [
                new TemporalValue
                {
                    Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                }
            ];
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Established = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = valueOperator,
                        Negate = negate,
                        Values = temporalValues
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.NotEquals)]
    [TestCase(CriteriaValueOperator.IsNull)]
    public void Plan_ReturnsEmpty_WhenInapplicableTemporalCriterionIsDoubleNegated(
        CriteriaValueOperator valueOperator)
    {
        // Arrange
        var temporalValues = valueOperator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull
            ? Array.Empty<TemporalValue>()
            :
            [
                new TemporalValue
                {
                    Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                }
            ];
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Established = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = valueOperator,
                        Negate = true,
                        Values = temporalValues
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.Equals, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, false)]
    [TestCase(CriteriaValueOperator.NotEquals, true)]
    [TestCase(CriteriaValueOperator.IsNull, true)]
    public void Plan_ReturnsEmpty_WhenPositiveProductionStatusDoesNotApply(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = valueOperator,
                        Negate = negate,
                        Values = [Constants.EndedProductionStatus]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.NotEquals, false)]
    [TestCase(CriteriaValueOperator.Equals, true)]
    [TestCase(CriteriaValueOperator.IsNull, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, true)]
    public void Plan_OmitsProductionStatus_WhenInapplicableCriterionIsVacuous(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ProductionStatus = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = valueOperator,
                        Negate = negate,
                        Values = [Constants.EndedProductionStatus]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_NormalizesLanguageCodesAndQids_WhenAllValuesAreExact()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["EN", "eng", "https://www.wikidata.org/entity/q1860"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Language));
            Assert.That(filter.Values, Is.EqualTo(["en", "eng"]));
            Assert.That(filter.LanguageQids, Is.EqualTo(["Q1860"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.IsNull, false, CriteriaValueOperator.IsNull)]
    [TestCase(CriteriaValueOperator.IsNotNull, false, CriteriaValueOperator.IsNotNull)]
    [TestCase(CriteriaValueOperator.IsNull, true, CriteriaValueOperator.IsNotNull)]
    [TestCase(CriteriaValueOperator.IsNotNull, true, CriteriaValueOperator.IsNull)]
    public void Plan_FoldsNegateIntoTheOperator_WhenLanguagePresence(
        CriteriaValueOperator valueOperator,
        bool negate,
        CriteriaValueOperator expectedOperator)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = valueOperator,
                        Negate = negate
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
                                And = [new PropertyCriterion<string> { Value = "wikidata" }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Q83495" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        var languageFilter = plan.Filters.Single(filter => filter.Kind == QueryFilterKind.Language);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(languageFilter.Operator, Is.EqualTo(expectedOperator));
            Assert.That(languageFilter.Negate, Is.False);
        }
    }

    [TestCase("en-US")]
    [TestCase("English")]
    [TestCase("abcd")]
    [TestCase("\u00e9\u00e9")]
    public void Plan_CompilesLanguageNameResidual_WhenValueIsNotAnExactAsciiCodeOrQid(string value)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = [value]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.Kind, Is.EqualTo(ResidualFilterKind.Language));
            Assert.That(residual.Source, Is.EqualTo(nameof(MetadataCriteria.Language)));
            Assert.That(
                residual.Hydration,
                Is.EqualTo(
                    ResidualHydration.Entity | ResidualHydration.Support));
            Assert.That(residual.LanguageCodes, Is.Empty);
            Assert.That(residual.LanguageQids, Is.Empty);
            Assert.That(residual.LanguageNames, Is.EqualTo([value]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesLanguageNameResidual_WhenQidDigitsAreNotAscii()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["Q\u0661\u0668\u0666\u0660"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.LanguageQids, Is.Empty);
            Assert.That(residual.LanguageNames, Is.EqualTo(["Q\u0661\u0668\u0666\u0660"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_CompilesWholeLanguageCriterion_WhenValuesMixCodeQidAndName()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["en", "Q1860", "English"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(residual.Criterion!.Values, Is.EqualTo(["en", "Q1860", "English"]));
            Assert.That(residual.LanguageCodes, Is.EqualTo(["en"]));
            Assert.That(residual.LanguageQids, Is.EqualTo(["Q1860"]));
            Assert.That(residual.LanguageNames, Is.EqualTo(["English"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithNoBoundWarnings_WhenLanguageNameIsResidualOnly()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>(),
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.In,
                        Values = ["English"]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.ResidualFilters, Has.Count.EqualTo(1));
            Assert.That(plan.UsesLocalPaging, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsZeroDurationAlternative_WhenEqualityHasPositiveValue()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>(),
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.In,
                        Negate = false,
                        Values = [TimeSpan.Zero, TimeSpan.FromMinutes(90)]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Duration));
            Assert.That(filter.DurationValues, Is.EqualTo([TimeSpan.FromMinutes(90)]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.Equals, false)]
    [TestCase(CriteriaValueOperator.In, false)]
    [TestCase(CriteriaValueOperator.NotEquals, true)]
    [TestCase(CriteriaValueOperator.NotIn, true)]
    public void Plan_ReturnsEmpty_WhenPositiveDurationEqualityIsZero(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>(),
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = valueOperator,
                        Negate = negate,
                        Values = [TimeSpan.Zero]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("Duration.And[0]"));
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
            Assert.That(disposition.Reason, Is.Not.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.NotEquals, false)]
    [TestCase(CriteriaValueOperator.Equals, true)]
    [TestCase(CriteriaValueOperator.NotIn, false)]
    [TestCase(CriteriaValueOperator.In, true)]
    public void Plan_OmitsDurationEquality_WhenOnlyZeroAlternativeIsNegated(
        CriteriaValueOperator valueOperator,
        bool negate)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = valueOperator,
                        Negate = negate,
                        Values = [TimeSpan.Zero]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions.Single(value => value.Source == "Duration.And[0]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Omitted));
            Assert.That(disposition.Reason, Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_DropsZeroDurationAlternative_WhenMixedEqualityIsNegated()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And =
                [
                    new PropertyCriterion<TimeSpan>
                    {
                        Operator = CriteriaValueOperator.NotIn,
                        Negate = false,
                        Values = [TimeSpan.Zero, TimeSpan.FromMinutes(90)]
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Duration);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.In));
            Assert.That(filter.Negate, Is.True);
            Assert.That(filter.DurationValues, Is.EqualTo([TimeSpan.FromMinutes(90)]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesRemoteAlternativeAndResidual_WhenContentRatingBodyAndNameRating()
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "MPAA"
                                    }
                                ]
                            },
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "R"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();
        var alternative = filter.Alternatives.Single();
        var residual = plan.ResidualFilters.Single();
        var disposition = plan.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.ContentRating));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(alternative.PropertyIds, Is.EqualTo(["P1657"]));
            Assert.That(alternative.Names, Is.EqualTo(["R"]));
            Assert.That(residual.CollectionGroup!.Kind, Is.EqualTo(ResidualCollectionKind.ContentRating));
            Assert.That(
                residual.CollectionGroup.Alternatives.Single().Predicates,
                Has.Some.Matches<
                    ResidualCollectionPredicate>(predicate =>
                        predicate.Field == ResidualCollectionField.ContentRatingRating));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("ContentRatings.Some[0]"));
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilterAndResidual));
        }
    }

    [Test]
    public void Plan_UsesRemoteAlternativeAndIdentityResidual_WhenContentRatingMixedQidAndName()
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
                                        Operator = CriteriaValueOperator.In,
                                        Values = ["Q123", "R"]
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();
        var predicate = plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q123"]));
            Assert.That(alternative.Names, Is.EqualTo(["R"]));
            Assert.That(predicate.Field, Is.EqualTo(ResidualCollectionField.ContentRatingRating));
            Assert.That(predicate.EntityQids, Is.EqualTo(["Q123"]));
            Assert.That(predicate.StringValues, Is.EqualTo(["R"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UseSafeCoarseAndResidualConjunction_WhenContentRatingQidAndLabelExactCriteria()
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
                                    new PropertyCriterion<string> { Value = "Q123" },
                                    new PropertyCriterion<string> { Value = "R" }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();
        var predicates = plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q123"]));
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(predicates, Has.Count.EqualTo(2));
            Assert.That(predicates[0].EntityQids, Is.EqualTo(["Q123"]));
            Assert.That(predicates[0].StringValues, Is.Empty);
            Assert.That(predicates[1].EntityQids, Is.Empty);
            Assert.That(predicates[1].StringValues, Is.EqualTo(["R"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProduceKnownEmpty_WhenContentRatingDisjointQidExactCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
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
                                    new PropertyCriterion<string> { Value = "Q123" },
                                    new PropertyCriterion<string> { Value = "Q456" }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.ContentRating));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_IsNotASelectiveBound_WhenBareContentRatingPresence()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some = [[new ContentRatingCriteria()]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                disposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilter));
            Assert.That(disposition.Reason, Does.Contain("server-side filter"));
        }
    }

    [Test]
    public void Plan_UsesExactPropertyFilterWithoutResidual_WhenContentRatingBodyOnly()
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "MPAA"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.ContentRating));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P1657"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.UsesLocalPaging, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProducesKnownEmptyWithoutWarning_WhenUnsupportedContentRatingBodyAndImpossibleRating()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Value = "MPAA"
                                    }
                                ]
                            },
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.ContentRating));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.Awards))]
    [TestCase(nameof(MetadataCriteria.Images))]
    [TestCase(nameof(MetadataCriteria.References))]
    public void Plan_IsNotASelectiveBound_WhenBareCollectionPresence(string fieldName)
    {
        // Arrange
        var criteria = fieldName switch
        {
            nameof(MetadataCriteria.Awards) => new MetadataCriteria
            {
                Awards = new CollectionCriteria<AwardCriteria> { Some = [[new AwardCriteria()]] }
            },
            nameof(MetadataCriteria.Images) => new MetadataCriteria
            {
                Images = new CollectionCriteria<ImageCriteria> { Some = [[new ImageCriteria()]] }
            },
            _ => new MetadataCriteria
            {
                References = new CollectionCriteria<ReferenceMetadataCriteria>
                {
                    Some = [[new ReferenceMetadataCriteria()]]
                }
            }
        };
        var filterKind = fieldName switch
        {
            nameof(MetadataCriteria.Awards) => QueryFilterKind.Award,
            nameof(MetadataCriteria.Images) => QueryFilterKind.Image,
            _ => QueryFilterKind.Reference
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(value => value.Kind == filterKind);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PromotesContentRatingGroup_WhenOneAlternativeNeedsResidualEvaluation()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "R"
                                    }
                                ]
                            }
                        },
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Value = "PG"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.Exactly(1).Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.ContentRating));
            Assert.That(residual.Kind, Is.EqualTo(ResidualFilterKind.Collection));
            Assert.That(residual.CollectionGroup!.Kind, Is.EqualTo(ResidualCollectionKind.ContentRating));
            Assert.That(residual.CollectionGroup.Alternatives, Has.Count.EqualTo(2));
            Assert.That(
                residual.CollectionGroup.Alternatives[0].Predicates.Single().StringValues,
                Is.EqualTo(["R"]));
            Assert.That(
                residual.CollectionGroup.Alternatives[1].Predicates.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DoesNotApplyCoarseRemoteNegation_WhenContentRatingNoneResidual()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                None =
                {
                    new ContentRatingCriteria
                    {
                        Rating = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.StartsWithString,
                                    Value = "PG"
                                }
                            ]
                        }
                    }
                }
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.ContentRating));
            Assert.That(residual.CollectionGroup!.OuterNegate, Is.True);
            Assert.That(residual.CollectionGroup.Alternatives, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Plan_UsesNameBoundAndYearResidual_WhenAwardNameAndYear()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "Q19020"
                                    }
                                ]
                            },
                            Status = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = Constants.WonAwardStatus
                                    }
                                ]
                            },
                            Year = new CriteriaSet<PropertyCriterion<int>>
                            {
                                And =
                                [
                                    new PropertyCriterion<int>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                        Value = 2000
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();
        var predicate = plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates.Single(value =>
            value.Field == ResidualCollectionField.AwardYear);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Award));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P166"]));
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q19020"]));
            Assert.That(predicate.Field, Is.EqualTo(ResidualCollectionField.AwardYear));
            Assert.That(predicate.IntegerValues, Is.EqualTo([2000]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesSafePropertiesAndResidualForConditionalImageType_WhenMoviePosterType()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "poster"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Image));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EquivalentTo(
                [
                    "P3383",
                    "P18"
                ]));
            Assert.That(
                plan.ResidualFilters.Single().CollectionGroup!.Kind,
                Is.EqualTo(ResidualCollectionKind.Image));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesExactPropertyWithoutResidual_WhenImageLogoType()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "logo"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Image));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P154"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.UsesLocalPaging, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(EntityKind.Movie), "still")]
    [TestCase(nameof(EntityKind.Movie), "profile")]
    [TestCase(nameof(EntityKind.Show), "still")]
    [TestCase(nameof(EntityKind.Show), "profile")]
    public void Plan_ProducesKnownEmpty_WhenUnmaterializedImageType(string entityKindName, string imageType)
    {
        // Arrange
        var entityKind = entityKindName == nameof(EntityKind.Movie)
            ? EntityKind.Movie
            : EntityKind.Show;
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = imageType
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(entityKind, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Image));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProducesKnownEmptyWithoutWarning_WhenUnsupportedImageTypeAndImpossibleWidth()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Value = "poster"
                                    }
                                ]
                            },
                            Width = new CriteriaSet<PropertyCriterion<int>>
                            {
                                And = [new PropertyCriterion<int> { Operator = CriteriaValueOperator.IsNotNull }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Image));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.ContentRatings))]
    [TestCase(nameof(MetadataCriteria.Images))]
    public void Plan_ReturnsEmpty_WhenCollectionCriterionIsUnsupported(string fieldName)
    {
        // Arrange
        var criteria = fieldName == nameof(MetadataCriteria.ContentRatings)
            ? new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                ContentRatings = new CollectionCriteria<ContentRatingCriteria>
                {
                    Some =
                    [
                        [
                            new ContentRatingCriteria
                            {
                                Body = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.GreaterThan,
                                            Value = "MPAA"
                                        }
                                    ]
                                }
                            }
                        ]
                    ]
                }
            }
            : new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                Images = new CollectionCriteria<ImageCriteria>
                {
                    Some =
                    [
                        [
                            new ImageCriteria
                            {
                                Type = new CriteriaSet<PropertyCriterion<string>>
                                {
                                    And =
                                    [
                                        new PropertyCriterion<string>
                                        {
                                            Operator = CriteriaValueOperator.GreaterThan,
                                            Value = "poster"
                                        }
                                    ]
                                }
                            }
                        ]
                    ]
                }
            };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesSupportedCollectionAlternative_WhenSiblingAlternativeIsUnsupported()
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Value = "MPAA"
                                    }
                                ]
                            }
                        },
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Q123" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.ContentRating));
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q123"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_FoldsToKnownEmpty_WhenImageWidthIsNotNull()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Width = new CriteriaSet<PropertyCriterion<int>>
                            {
                                And =
                                [
                                    new PropertyCriterion<int>
                                    {
                                        Operator = CriteriaValueOperator.IsNotNull
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesExactServerFilterAndRemotePaging_WhenReferenceUrlEquality()
    {
        // Arrange
        var website = new Uri("https://example.test/matrix");
        var criteria = new MetadataCriteria
        {
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And = [new PropertyCriterion<Uri> { Value = website }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 3);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Reference));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P856"]));
            Assert.That(filter.Alternatives.Single().UriSchemes, Is.Empty);
            Assert.That(filter.Alternatives.Single().UriValues, Is.EqualTo([website.AbsoluteUri]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.UsesLocalPaging, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesAllExactServerValuesWithoutResidual_WhenReferenceUrlIn()
    {
        // Arrange
        var websites = new[]
        {
            new Uri("https://example.test/first"),
            new Uri("http://example.test/second")
        };
        var criteria = new MetadataCriteria
        {
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri>
                                    {
                                        Operator = CriteriaValueOperator.In,
                                        Values = websites
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                alternative.UriValues,
                Is.EqualTo(
                [
                    "https://example.test/first",
                    "http://example.test/second"
                ]));
            Assert.That(alternative.UriSchemes, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.UsesLocalPaging, Is.False);
        }
    }

    [Test]
    public void Plan_ProduceKnownEmpty_WhenDistinctReferenceUrlExactCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri>
                                    {
                                        Value = new Uri("https://example.test/first")
                                    },
                                    new PropertyCriterion<Uri>
                                    {
                                        Value = new Uri("https://example.test/second")
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UseSingleServerValue_WhenReferenceUrlExactCriteriaDifferingOnlyByCase()
    {
        // Arrange
        var first = new Uri("https://example.test/Matrix");
        var criteria = new MetadataCriteria
        {
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri> { Value = first },
                                    new PropertyCriterion<Uri> { Value = new Uri("https://example.test/matrix") }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(
                plan.Filters.Single().Alternatives.Single().UriValues,
                Is.EqualTo([first.AbsoluteUri]));
            Assert.That(plan.ResidualFilters, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProducesKnownEmpty_WhenFtpReferenceUrlExactCriterion()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And = [new PropertyCriterion<Uri> { Value = new Uri("ftp://example.test/file") }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Reference));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesServerFilterWithoutResidual_WhenBareReferencePresence()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some = [[new ReferenceMetadataCriteria()]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Reference);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P856"]));
            Assert.That(
                filter.Alternatives.Single().UriSchemes,
                Is.EqualTo([Uri.UriSchemeHttp, Uri.UriSchemeHttps]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UseServerFilterWithoutResidual_WhenReferenceConstantPresenceCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNotNull }]
                            },
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And = [new PropertyCriterion<Uri> { Operator = CriteriaValueOperator.IsNotNull }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Reference);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P856"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesExactServerFilterWithoutResidual_WhenAwardQidName()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "Q19020"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Award));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q19020"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.UsesLocalPaging, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UseCanonicalServerFilter_WhenAwardQidUrlAndBareQidExactCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Value = "https://www.wikidata.org/wiki/Q19020"
                                    },
                                    new PropertyCriterion<string> { Value = "Q19020" }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q19020"]));
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProduceKnownEmpty_WhenAwardDisjointNameExactCriteria()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string> { Value = "Academy Award" },
                                    new PropertyCriterion<string> { Value = "Golden Globe" }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Award));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesNegatedServerFilterWithoutResidual_WhenBareReferenceNone()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                None = [new ReferenceMetadataCriteria()]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Reference);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Negate, Is.True);
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesSelectiveRemoteAlternativeAndResidual_WhenAwardNameOnly()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "Academy Award"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Award));
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.Equals));
            Assert.That(filter.Alternatives.Single().Names, Is.EqualTo(["Academy Award"]));
            Assert.That(residual.CollectionGroup!.Kind, Is.EqualTo(ResidualCollectionKind.Award));
            Assert.That(
                residual.CollectionGroup.Alternatives.Single().Predicates.Single().Field,
                Is.EqualTo(ResidualCollectionField.AwardName));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesPresenceFilterWithoutResidual_WhenAwardWonStatus()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Status = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = Constants.WonAwardStatus
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Award);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P166"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DiscardsImpossibleUnsupportedAlternative_WhenCollectionOr()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Value = "academy"
                                    }
                                ]
                            },
                            Category = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "ceremony"
                                    }
                                ]
                            }
                        },
                        new AwardCriteria
                        {
                            Name = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "Q19020"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Award));
            Assert.That(filter.Alternatives, Has.Count.EqualTo(1));
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q19020"]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ProducesKnownEmpty_WhenImpossibleUnsupportedCollectionAlternative()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [
                        new AwardCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.GreaterThan,
                                        Value = "academy"
                                    }
                                ]
                            },
                            Category = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "ceremony"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Award));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.Awards), CriteriaValueOperator.IsNull)]
    [TestCase(nameof(MetadataCriteria.Awards), CriteriaValueOperator.IsNotNull)]
    [TestCase(nameof(MetadataCriteria.AlternateTexts), CriteriaValueOperator.IsNull)]
    [TestCase(nameof(MetadataCriteria.AlternateTexts), CriteriaValueOperator.IsNotNull)]
    public void Plan_FoldsNullCheck_WhenAlwaysNonNullCollectionField(
        string fieldName,
        CriteriaValueOperator valueOperator)
    {
        // Arrange
        var nullCheck = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Operator = valueOperator }]
        };
        var criteria = fieldName == nameof(MetadataCriteria.Awards)
            ? new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                Awards = new CollectionCriteria<AwardCriteria>
                {
                    Some = [[new AwardCriteria { Body = nullCheck }]]
                }
            }
            : new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                AlternateTexts = new CollectionCriteria<AlternateTextCriteria>
                {
                    Some = [[new AlternateTextCriteria { Language = nullCheck }]]
                }
            };
        var collectionKind = fieldName == nameof(MetadataCriteria.Awards)
            ? ResidualCollectionKind.Award
            : ResidualCollectionKind.AlternateText;
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        if (valueOperator == CriteriaValueOperator.IsNull)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
                Assert.That(plan.ResidualFilters, Is.Empty);
                Assert.That(plan.Warnings, Is.Empty);
            }

            return;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            if (collectionKind == ResidualCollectionKind.Award)
            {
                Assert.That(
                    plan.Filters,
                    Has.Some.Matches<QueryFilter>(filter =>
                        filter.Kind == QueryFilterKind.Award));
                Assert.That(plan.ResidualFilters, Is.Empty);
            }
            else
            {
                Assert.That(plan.ResidualFilters.Single().CollectionGroup!.Kind, Is.EqualTo(collectionKind));
                Assert.That(
                    plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates,
                    Is.Empty);
            }

            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.IsNull)]
    [TestCase(CriteriaValueOperator.IsNotNull)]
    public void Plan_FoldsNonNullInvariant_WhenReferenceOrdinalNullCheck(CriteriaValueOperator valueOperator)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Ordinal = new CriteriaSet<PropertyCriterion<int>>
                            {
                                And = [new PropertyCriterion<int> { Operator = valueOperator }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        if (valueOperator == CriteriaValueOperator.IsNull)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
                Assert.That(
                    plan.Filters,
                    Has.None.Matches<QueryFilter>(filter =>
                        filter.Kind == QueryFilterKind.Reference));
                Assert.That(plan.ResidualFilters, Is.Empty);
                Assert.That(plan.Warnings, Is.Empty);
            }

            return;
        }

        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Reference);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().UriSchemes,
                Is.EqualTo([Uri.UriSchemeHttp, Uri.UriSchemeHttps]));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(CriteriaValueOperator.IsNull, false)]
    [TestCase(CriteriaValueOperator.IsNotNull, true)]
    public void Plan_PreservesCollectionNegation_WhenReferenceOrdinalNullCheckNone(
        CriteriaValueOperator valueOperator,
        bool expectsNegatedFilter)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                None =
                [
                    new ReferenceMetadataCriteria
                    {
                        Ordinal = new CriteriaSet<PropertyCriterion<int>>
                        {
                            And = [new PropertyCriterion<int> { Operator = valueOperator }]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters.Any(filter => filter.Kind == QueryFilterKind.Reference),
                Is.EqualTo(expectsNegatedFilter));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }

        if (expectsNegatedFilter)
        {
            Assert.That(
                plan.Filters.Single(filter => filter.Kind == QueryFilterKind.Reference).Negate,
                Is.True);
        }
    }

    [TestCase(CriteriaValueOperator.Equals, 1)]
    [TestCase(CriteriaValueOperator.GreaterThan, 0)]
    public void Plan_UsesPresenceCoarseFilterAndIntegerResidual_WhenReferenceOrdinal(
        CriteriaValueOperator valueOperator,
        int value)
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Ordinal = new CriteriaSet<PropertyCriterion<int>>
                            {
                                And =
                                [
                                    new PropertyCriterion<int>
                                    {
                                        Operator = valueOperator,
                                        Value = value
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == QueryFilterKind.Reference);
        var predicate = plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().PropertyIds,
                Is.EqualTo(["P856"]));
            Assert.That(
                filter.Alternatives.Single().UriSchemes,
                Is.EqualTo([Uri.UriSchemeHttp, Uri.UriSchemeHttps]));
            Assert.That(predicate.Field, Is.EqualTo(ResidualCollectionField.ReferenceOrdinal));
            Assert.That(predicate.Operator, Is.EqualTo(valueOperator));
            Assert.That(predicate.IntegerValues, Is.EqualTo([value]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DoesNotApplyRemoteCollectionFilter_WhenReferenceOrdinalNone()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                None =
                [
                    new ReferenceMetadataCriteria
                    {
                        Ordinal = new CriteriaSet<PropertyCriterion<int>>
                        {
                            And = [new PropertyCriterion<int> { Value = 1 }]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind == QueryFilterKind.Reference));
            Assert.That(residual.CollectionGroup!.OuterNegate, Is.True);
            Assert.That(
                residual.CollectionGroup.Alternatives.Single().Predicates.Single().Field,
                Is.EqualTo(ResidualCollectionField.ReferenceOrdinal));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesPresenceCoarseFilterAndResidual_WhenReferenceUrlPrefix()
    {
        // Arrange
        var website = new Uri("https://example.test/");
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            References = new CollectionCriteria<ReferenceMetadataCriteria>
            {
                Some =
                [
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri>
                                    {
                                        Operator = CriteriaValueOperator.StartsWithString,
                                        Value = website
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(value => value.Kind == QueryFilterKind.Reference);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(
                filter.Alternatives.Single().UriSchemes,
                Is.EqualTo([Uri.UriSchemeHttp, Uri.UriSchemeHttps]));
            Assert.That(residual.CollectionGroup!.Kind, Is.EqualTo(ResidualCollectionKind.Reference));
            Assert.That(
                residual.CollectionGroup.Alternatives.Single().Predicates.Single().Operator,
                Is.EqualTo(CriteriaValueOperator.StartsWithString));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.Awards))]
    [TestCase(nameof(MetadataCriteria.Images))]
    [TestCase(nameof(MetadataCriteria.References))]
    public void Plan_DoesNotApplyRemoteCollectionFilter_WhenResidualCollectionNone(string fieldName)
    {
        // Arrange
        var criteria = fieldName switch
        {
            nameof(MetadataCriteria.Awards) => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                Awards = new CollectionCriteria<AwardCriteria>
                {
                    None =
                    [
                        new AwardCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Value = "academy"
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            nameof(MetadataCriteria.Images) => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                Images = new CollectionCriteria<ImageCriteria>
                {
                    None =
                    [
                        new ImageCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Value = "commons"
                                    }
                                ]
                            }
                        }
                    ]
                }
            },
            _ => new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                References = new CollectionCriteria<ReferenceMetadataCriteria>
                {
                    None =
                    [
                        new ReferenceMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<Uri>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Uri>
                                    {
                                        Operator = CriteriaValueOperator.StartsWithString,
                                        Value = new Uri("https://example.test/")
                                    }
                                ]
                            }
                        }
                    ]
                }
            }
        };
        var filterKind = fieldName switch
        {
            nameof(MetadataCriteria.Awards) => QueryFilterKind.Award,
            nameof(MetadataCriteria.Images) => QueryFilterKind.Image,
            _ => QueryFilterKind.Reference
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Filters.Any(filter => filter.Kind == filterKind), Is.False);
            Assert.That(residual.CollectionGroup!.OuterNegate, Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [TestCase(nameof(MetadataCriteria.ContentRatings))]
    [TestCase(nameof(MetadataCriteria.Awards))]
    public void Plan_UsesNegatedServerFilter_WhenBareMaterializedCollectionNone(string fieldName)
    {
        // Arrange
        var criteria = fieldName == nameof(MetadataCriteria.ContentRatings)
            ? new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                ContentRatings = new CollectionCriteria<ContentRatingCriteria>
                {
                    None = [new ContentRatingCriteria()]
                }
            }
            : new MetadataCriteria
            {
                Title = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
                },
                Awards = new CollectionCriteria<AwardCriteria>
                {
                    None = [new AwardCriteria()]
                }
            };
        var filterKind = fieldName == nameof(MetadataCriteria.ContentRatings)
            ? QueryFilterKind.ContentRating
            : QueryFilterKind.Award;
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(candidate => candidate.Kind == filterKind);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.Negate, Is.True);
            Assert.That(filter.Operator, Is.EqualTo(CriteriaValueOperator.IsNotNull));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PreserveAndBetweenGroupsAndOrWithinGroup_WhenContentRatingGroups()
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "R"
                                    }
                                ]
                            }
                        },
                        new ContentRatingCriteria
                        {
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "PG-13"
                                    }
                                ]
                            }
                        }
                    ],
                    [
                        new ContentRatingCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "MPAA"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filters = plan.Filters.Where(filter => filter.Kind == QueryFilterKind.ContentRating).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filters, Has.Length.EqualTo(2));
            Assert.That(filters[0].Alternatives, Has.Count.EqualTo(2));
            Assert.That(filters[1].Alternatives, Has.Count.EqualTo(1));
            Assert.That(plan.ResidualFilters, Has.Count.EqualTo(1));
            Assert.That(plan.ResidualFilters[0].CollectionGroup!.Alternatives, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_IsIgnored_WhenEmptyContentRatingSomeGroup()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria> { Some = [[]] }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesOnlyAliasResidual_WhenAlternateText()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                        Value = "Matrix"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.None.Matches<QueryFilter>(filter =>
                    filter.Kind is QueryFilterKind.ContentRating
                        or QueryFilterKind.Award
                        or QueryFilterKind.Image
                        or QueryFilterKind.Reference));
            Assert.That(residual.CollectionGroup!.Kind, Is.EqualTo(ResidualCollectionKind.AlternateText));
            Assert.That(
                residual.CollectionGroup.Alternatives.Single().Predicates.Single().Field,
                Is.EqualTo(ResidualCollectionField.AlternateTextText));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesEntityResidualEvaluation_WhenPreviewCriterion()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Previews = new CollectionCriteria<PreviewMetadataCriteria>
            {
                Some =
                [
                    [
                        new PreviewMetadataCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "https://www.youtube.com/watch?v=_XrKhqC5rQQ"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions.Single(value => value.Source == "Previews.Some[0]");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(
                plan.ResidualFilters.Single().CollectionGroup!.Kind,
                Is.EqualTo(ResidualCollectionKind.Preview));
            Assert.That(
                plan.ResidualFilters.Single().CollectionGroup!.Alternatives.Single().Predicates.Single().Field,
                Is.EqualTo(ResidualCollectionField.PreviewUrl));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.Residual));
            Assert.That(disposition.Reason, Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenPositiveTagCriterionIsPresent()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions,
                Has.Some.Matches<CriterionDisposition>(value =>
                    value.Source == "Tags.Some[0]" &&
                    value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable));
        }
    }

    [Test]
    public void Plan_IgnoresNegativeTagCriterion_WhenTitleProvidesBound()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
            },
            Tags = new CollectionCriteria<TagCriteria>
            {
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
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions,
                Has.Some.Matches<CriterionDisposition>(value =>
                    value.Source == "Tags.None[0]" &&
                    value.Disposition == CriterionDispositionKind.Omitted));
        }
    }

    [Test]
    public void Plan_EmptyWithoutTagWarning_WhenTagCriteriaAreTheOnlyCriteria()
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
                                And = [new PropertyCriterion<string> { Values = ["favorite"] }]
                            }
                        }
                    ]
                ],
                None = [new TagCriteria()]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters, Is.Empty);
            Assert.That(plan.ResidualFilters, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_UsesIdentitySeed_WhenExactExternalIdAndStartsWithTitle()
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
                        Value = "The Mat"
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
                                And = [new PropertyCriterion<string> { Value = "wikidata" }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Q83495" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(
                plan.Filters,
                Has.Some.Matches<QueryFilter>(value =>
                    value.Kind == QueryFilterKind.ExternalId));
            Assert.That(
                plan.Filters,
                Has.Some.Matches<QueryFilter>(value =>
                    value.Kind == QueryFilterKind.Title));
        }
    }

    [Test]
    public void Plan_CompileOneAlignedResidualAlternative_WhenGenreNamePredicates()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "The Matrix" }]
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
                                        Operator = CriteriaValueOperator.StartsWithString,
                                        Value = "science"
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.EndsWithString,
                                        Value = "film"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var residual = plan.ResidualFilters.Single().CollectionGroup!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters,
                Has.Some.Matches<QueryFilter>(value =>
                    value.Kind == QueryFilterKind.Genre));
            Assert.That(residual.Kind, Is.EqualTo(ResidualCollectionKind.Genre));
            Assert.That(residual.Alternatives, Has.Count.EqualTo(1));
            Assert.That(residual.Alternatives.Single().Predicates, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_UsesCoarseResolutionAndResidualVerification_WhenExactRelationshipName()
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
                                And = [new PropertyCriterion<string> { Value = "person" }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Keanu Reeves" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters.Single().Alternatives.Single().Names,
                Is.EqualTo(["Keanu Reeves"]));
            Assert.That(
                plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single().Subjects.Single()
                    .Criterion!.Values,
                Is.EqualTo(["Keanu Reeves"]));
        }
    }

    [Test]
    public void Plan_MapsEverySourceAsAnAlternativeSelector_WhenExternalIdSourceIn()
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
                                        Operator = CriteriaValueOperator.In,
                                        Values = ["imdb", "tmdb"]
                                    }
                                ]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "tt0133093" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var ids = plan.Filters.Single().Alternatives.Single().ExternalIds;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ids.Select(value => value.PropertyId),
                Is.EquivalentTo(
                [
                    "P345",
                    "P4947"
                ]));
            Assert.That(ids.Select(value => value.Value), Is.All.EqualTo("tt0133093"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsApproximateRelationshipNameBound_WhenExactIdentityBound()
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
                                And = [new PropertyCriterion<string> { Value = "wikidata" }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Q83495" }]
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
                                And = [new PropertyCriterion<string> { Value = "cast" }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Value = "Reeves"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var relationshipDisposition = plan.CriterionDispositions.Single(value =>
            value.Source.StartsWith(nameof(MetadataCriteria.Relationships), StringComparison.Ordinal));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(plan.Filters, Has.Count.EqualTo(1));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.ExternalId));
            Assert.That(plan.ResidualFilters.Single().Kind, Is.EqualTo(ResidualFilterKind.Relationship));
            Assert.That(relationshipDisposition.Disposition, Is.EqualTo(CriterionDispositionKind.Residual));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_DropsOnlyApproximateRelationshipNameBound_WhenExactIdentityBound()
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
                                And = [new PropertyCriterion<string> { Value = "wikidata" }]
                            },
                            Value = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Q83495" }]
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
                                And = [new PropertyCriterion<string> { Value = "cast" }]
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.ContainsString,
                                        Value = "Reeves"
                                    }
                                ]
                            }
                        }
                    ]
                ]
            },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [
                        new ContentRatingCriteria
                        {
                            Body = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "MPAA" }]
                            },
                            Rating = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "R" }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var relationshipDisposition = plan.CriterionDispositions.Single(value =>
            value.Source.StartsWith(nameof(MetadataCriteria.Relationships), StringComparison.Ordinal));
        var contentRatingDisposition = plan.CriterionDispositions.Single(value =>
            value.Source.StartsWith(nameof(MetadataCriteria.ContentRatings), StringComparison.Ordinal));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Filters.Select(filter => filter.Kind),
                Is.EqualTo(
                [
                    QueryFilterKind.ExternalId,
                    QueryFilterKind.ContentRating
                ]));
            Assert.That(
                plan.ResidualFilters.Select(filter => filter.Kind),
                Is.EqualTo(
                [
                    ResidualFilterKind.Relationship,
                    ResidualFilterKind.Collection
                ]));
            Assert.That(relationshipDisposition.Disposition, Is.EqualTo(CriterionDispositionKind.Residual));
            Assert.That(
                contentRatingDisposition.Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilterAndResidual));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PreservesQidAndPropertyAlternatives_WhenRelationshipExternalIdGroup()
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
                                And = [new PropertyCriterion<string> { Value = "cast" }]
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
                                                And = [new PropertyCriterion<string> { Value = "wikidata" }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Value = "Q43416" }]
                                            }
                                        },
                                        new ExternalIdCriteria
                                        {
                                            Source = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Value = "imdb" }]
                                            },
                                            Value = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And = [new PropertyCriterion<string> { Value = "nm0000206" }]
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
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var group = plan.Filters.Single().Alternatives.Single().ExternalIdGroups.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.Qids, Is.EqualTo(["Q43416"]));
            Assert.That(group.ExternalIds.Single().PropertyId, Is.EqualTo("P345"));
            Assert.That(group.ExternalIds.Single().Value, Is.EqualTo("nm0000206"));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_SkipUnsupportedSources_WhenRelationshipExternalIdSourceAndValueSets()
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
                                And = [new PropertyCriterion<string> { Value = "cast" }]
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
                                                        Operator = CriteriaValueOperator.In,
                                                        Values = ["wikidata", "imdb", "unsupported"]
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
                                                        Values = ["Q43416", "nm0000206"]
                                                    }
                                                ]
                                            }
                                        }
                                    ]
                                ]
                            },
                            Qualifier = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Values = ["Hero"] }]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var group = plan.Filters.Single().Alternatives.Single().ExternalIdGroups.Single();
        var residual = plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.Qids, Is.EqualTo(["Q43416"]));
            Assert.That(
                group.ExternalIds.Select(value => value.PropertyId),
                Is.All.EqualTo("P345"));
            Assert.That(
                group.ExternalIds.Select(value => value.Value),
                Is.EquivalentTo(["Q43416", "nm0000206"]));
            Assert.That(residual.ExternalIdGroups.Single().Qids, Is.EqualTo(["Q43416"]));
            Assert.That(
                residual.ExternalIdGroups.Single().ExternalIds.Select(value => value.Value),
                Is.EquivalentTo(["Q43416", "nm0000206"]));
            Assert.That(residual.Qualifiers.Single().Criterion!.Values, Is.EqualTo(["Hero"]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsEmptyWithoutWarning_WhenWikidataHasNoDataForTheRelationshipCriterion()
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
                        Values = ["Matrix"]
                    }
                ]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
                        {
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Q42"]
                                    }
                                ]
                            },
                            RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
                            {
                                And =
                                [
                                    new PropertyCriterion<Guid>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = new Guid("8a1a2fb2-06a1-4d1b-9c5e-1f2b3c4d5e6f")
                                    }
                                ]
                            }
                        }
                    ]
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var disposition = plan.CriterionDispositions[1];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(disposition.Source, Is.EqualTo("Relationships.Some[0]"));
            Assert.That(disposition.Disposition, Is.EqualTo(CriterionDispositionKind.ProvablyUnsatisfiable));
        }
    }

    [Test]
    public void Plan_UsesTitleBound_WhenExcludedRelationshipsHaveNoWikidataData()
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
                None =
                [
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["chef"] }]
                        }
                    },
                    new RelationshipCriteria
                    {
                        RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And =
                            [
                                new PropertyCriterion<Guid>
                                {
                                    Value = new Guid("8a1a2fb2-06a1-4d1b-9c5e-1f2b3c4d5e6f")
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var relationshipDispositions = plan.CriterionDispositions
            .Where(value => value.Source.StartsWith("Relationships", StringComparison.Ordinal))
            .ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(relationshipDispositions, Has.Length.EqualTo(2));
            Assert.That(
                relationshipDispositions.Select(value => value.Disposition),
                Is.All.EqualTo(CriterionDispositionKind.Omitted));
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenExcludedRelationshipSubjectOperatorIsUnsupported()
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
                None =
                [
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^Keanu"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions[1].Disposition,
                Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }

    [Test]
    public void Plan_ReturnsEmpty_WhenRelationshipKindOperatorCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            ExternalIds = new CollectionCriteria<ExternalIdCriteria>
            {
                Some =
                [[
                    new ExternalIdCriteria
                    {
                        Source = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["imdb"] }]
                        },
                        Value = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["tt0133093"] }]
                        }
                    }
                ]]
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Values = ["act"]
                                }
                            ]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Q40096"] }]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var externalId = plan.Filters.Single().Alternatives.Single().ExternalIds.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(externalId.PropertyId, Is.EqualTo("P345"));
            Assert.That(externalId.Value, Is.EqualTo("tt0133093"));
            Assert.That(plan.CriterionDispositions[1].Disposition, Is.EqualTo(CriterionDispositionKind.Refused));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_MapsKnownRole_WhenPositiveRelationshipKindListIsPartlyMappable()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["director", "actor"]
                                }
                            ]
                        },
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Q40096"] }]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(filter.Kind, Is.EqualTo(QueryFilterKind.Relationship));
            Assert.That(filter.Negate, Is.False);
            Assert.That(filter.Alternatives.Single().PropertyIds, Is.EqualTo(["P57"]));
            Assert.That(filter.Alternatives.Single().Qids, Is.EqualTo(["Q40096"]));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions.Single().Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilter));
        }
    }

    [Test]
    public void Plan_MapsKnownRole_WhenExcludedRelationshipKindListIsPartlyMappable()
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
                None =
                [
                    new RelationshipCriteria
                    {
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.In,
                                    Values = ["director", "actor"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var filter = plan.Filters.Single(value => value.Kind == QueryFilterKind.Relationship);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.TitleSearch));
            Assert.That(filter.Negate, Is.True);
            Assert.That(filter.Alternatives.Single().PropertyIds, Is.EqualTo(["P57"]));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions[1].Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilter));
        }
    }

    [Test]
    public void Plan_UsesSupportedRelationshipAlternative_WhenSiblingSubjectCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Q123"] }]
                        }
                    },
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^Tom"]
                                }
                            ]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Qids, Is.EqualTo(["Q123"]));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions.Single().Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilter));
        }
    }

    [Test]
    public void Plan_KeepsResidualForSupportedNameAlternative_WhenSiblingSubjectCannotCompile()
    {
        // Arrange
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [[
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Values = ["Tom Hanks"] }]
                        }
                    },
                    new RelationshipCriteria
                    {
                        Subject = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Regex,
                                    Values = ["^Tom"]
                                }
                            ]
                        }
                    }
                ]]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);
        var alternative = plan.Filters.Single().Alternatives.Single();
        var residual = plan.ResidualFilters.Single().RelationshipGroup!.Alternatives.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Structured));
            Assert.That(alternative.Names, Is.EqualTo(["Tom Hanks"]));
            Assert.That(residual.Subjects.Single().Names, Is.EqualTo(["Tom Hanks"]));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions.Single().Disposition,
                Is.EqualTo(CriterionDispositionKind.ServerFilterAndResidual));
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
                And = [new PropertyCriterion<string> { Values = ["The Matrix"] }]
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
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Values = ["^Act"]
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(EntityKind.Movie, criteria, 1);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Strategy, Is.EqualTo(QueryStrategy.Empty));
            Assert.That(plan.Filters.Single().Kind, Is.EqualTo(QueryFilterKind.Title));
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.CriterionDispositions[1].Disposition,
                Is.EqualTo(CriterionDispositionKind.Refused));
        }
    }
}
