using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
internal sealed class SearchPlannerTests
{
    [Test]
    public void Plan_TitleSearch_WhenProviderTypeAcceptsTvmaze()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["TVMAZE"] }],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaf.Warnings, Is.Empty);
            Assert.That(leaf.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenProviderTypeExcludesTvmaze()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["tmdb"] }],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_KnownEmpty_WhenProviderTypeNegationExcludesTvmaze()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.NotEquals, Values = ["tvmaze"] },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_TitleSearch_WhenProviderTypeNegationTargetsAnotherProvider()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Values = ["tmdb"],
                    },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaf.Warnings, Is.Empty);
            Assert.That(leaf.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_HandlesProviderTypeNullChecks_AsSelfIdentity()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var title = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
        };
        var isNull = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }],
            },
            Title = title,
        };
        var isNotNull = new MetadataCriteria
        {
            ProviderType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNotNull }],
            },
            Title = title,
        };

        // Act
        var nullPlan = planner.Compile(isNull);
        var notNullPlan = planner.Compile(isNotNull);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullPlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(notNullPlan, Is.InstanceOf<SearchPlanNode.Seeded>());
            Assert.That(((SearchPlanNode.Seeded)notNullPlan).Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceCannotMatchAnyShowSourceKey()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["openlibrary:work:OL1W"],
                    },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_SourceFilter_WhenSourceTargetsOneShowSourceKey()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["tvmaze:show:82"],
                    },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaf.Warnings, Is.Empty);
            Assert.That(leaf.HitFilters.Any(filter => filter.Kind == LocalFilterKind.Source), Is.True);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceNegationExcludesEveryShowSourceKey()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotStartsWithString,
                        Values = ["tvmaze:"],
                    },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_AbsentTags_ApplyEmptyCollectionSemantics()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var positive = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["watched"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };
        var excluded = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["dropped"],
                                },
                            ],
                        },
                    },
                ],
            },
        };

        // Act
        var positivePlan = planner.Compile(positive);
        var excludedPlan = planner.Compile(excluded);

        // Assert
        var leaf = (SearchPlanNode.Seeded)excludedPlan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(excludedPlan, Is.InstanceOf<SearchPlanNode.Seeded>());
            Assert.That(leaf.Warnings, Is.Empty);
            Assert.That(leaf.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_EvaluatesPersonDates_AsAbsentConcepts()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var title = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
        };
        var birthDateValue = new TemporalValue
        {
            Value = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Precision = TemporalPrecision.Year,
        };
        var positive = new MetadataCriteria
        {
            Title = title,
            BirthDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = [birthDateValue],
                    },
                ],
            },
        };
        var negated = new MetadataCriteria
        {
            Title = title,
            BirthDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And =
                [
                    new PropertyCriterion<TemporalValue>
                    {
                        Operator = CriteriaValueOperator.NotEquals,
                        Values = [birthDateValue],
                    },
                ],
            },
        };
        var isNull = new MetadataCriteria
        {
            Title = title,
            DeathDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = CriteriaValueOperator.IsNull }],
            },
        };
        var isNotNull = new MetadataCriteria
        {
            Title = title,
            DeathDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
            {
                And = [new PropertyCriterion<TemporalValue> { Operator = CriteriaValueOperator.IsNotNull }],
            },
        };

        // Act
        var positivePlan = planner.Compile(positive);
        var negatedPlan = planner.Compile(negated);
        var isNullPlan = planner.Compile(isNull);
        var isNotNullPlan = planner.Compile(isNotNull);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(negatedPlan, Is.InstanceOf<SearchPlanNode.Seeded>());
            Assert.That(((SearchPlanNode.Seeded)negatedPlan).Warnings, Is.Empty);
            Assert.That(isNullPlan, Is.InstanceOf<SearchPlanNode.Seeded>());
            Assert.That(((SearchPlanNode.Seeded)isNullPlan).Warnings, Is.Empty);
            Assert.That(isNotNullPlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentCollectionsAreRequired()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var title = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
        };
        var awards = new MetadataCriteria
        {
            Title = title,
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [new AwardCriteria()],
                ],
            },
        };
        var contentRatings = new MetadataCriteria
        {
            Title = title,
            ContentRatings = new CollectionCriteria<ContentRatingCriteria>
            {
                Some =
                [
                    [new ContentRatingCriteria()],
                ],
            },
        };
        var previews = new MetadataCriteria
        {
            Title = title,
            Previews = new CollectionCriteria<PreviewMetadataCriteria>
            {
                Some =
                [
                    [new PreviewMetadataCriteria()],
                ],
            },
        };

        // Act
        var awardsPlan = planner.Compile(awards);
        var contentRatingsPlan = planner.Compile(contentRatings);
        var previewsPlan = planner.Compile(previews);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(awardsPlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(contentRatingsPlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(previewsPlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
        }
    }

    [Test]
    public void Plan_TitleSearch_WhenAbsentCollectionsAreExcluded()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
            Awards = new CollectionCriteria<AwardCriteria> { None = [new AwardCriteria()] },
            ContentRatings = new CollectionCriteria<ContentRatingCriteria> { None = [new ContentRatingCriteria()] },
            Previews = new CollectionCriteria<PreviewMetadataCriteria> { None = [new PreviewMetadataCriteria()] },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaf.Warnings, Is.Empty);
            Assert.That(leaf.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_EvaluatesCustomMetadata_AsAbsentConcepts()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var title = new CriteriaSet<PropertyCriterion<string>>
        {
            And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
        };
        var positive = new MetadataCriteria
        {
            Title = title,
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["showType"] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.Equals,
                                Values = ["Scripted"],
                            },
                        },
                    },
                ],
            },
        };
        var negated = new MetadataCriteria
        {
            Title = title,
            CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
            {
                And =
                [
                    new DictionaryCriteria<PropertyCriterion<string>>
                    {
                        Items = new Dictionary<string, PropertyCriterion<string>>
                        {
                            ["showType"] = new PropertyCriterion<string>
                            {
                                Operator = CriteriaValueOperator.NotEquals,
                                Values = ["Scripted"],
                            },
                        },
                    },
                ],
            },
        };

        // Act
        var positivePlan = planner.Compile(positive);
        var negatedPlan = planner.Compile(negated);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
            Assert.That(negatedPlan, Is.InstanceOf<SearchPlanNode.Seeded>());
            Assert.That(((SearchPlanNode.Seeded)negatedPlan).Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_RelationshipAndImageCriteria_CompileAsResiduals()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [new RelationshipCriteria()],
                ],
            },
            Images = new CollectionCriteria<ImageCriteria> { None = [new ImageCriteria()] },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(leaf.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
            Assert.That(leaf.RelationshipResiduals, Has.Count.EqualTo(1));
            Assert.That(leaf.ImageResiduals, Has.Count.EqualTo(1));
            Assert.That(leaf.ImageResiduals.Single().Negate, Is.True);
            Assert.That(
                leaf.ImageResiduals.Single().Alternatives.Single().Kind,
                Is.EqualTo(ImageCriteriaResidual.EvaluationKind.Presence));
            Assert.That(leaf.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ImageCriteria_SplitPresenceAndGalleryResiduals()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [new ImageCriteria()],
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
                                        Values = ["poster"],
                                    },
                                ],
                            },
                        },
                    ],
                    [
                        new ImageCriteria
                        {
                            Url = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNotNull }],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ImageResiduals, Has.Count.EqualTo(3));
            Assert.That(
                plan.ImageResiduals.Select(residual => residual.Alternatives.Single().Kind),
                Is.EqualTo(
                    [
                        ImageCriteriaResidual.EvaluationKind.Presence,
                        ImageCriteriaResidual.EvaluationKind.Gallery,
                        ImageCriteriaResidual.EvaluationKind.Presence,
                    ]));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenRequiredImageFieldMustBeNull()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.IsNull }],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [TestCase(CriteriaValueOperator.Equals, false, "still")]
    [TestCase(CriteriaValueOperator.NotIn, false, "poster", "banner", "background")]
    [TestCase(CriteriaValueOperator.NotEquals, true, "still")]
    public void Plan_KnownEmpty_WhenImageTypeCannotMatchMappedDomain(
        CriteriaValueOperator op,
        bool negate,
        params string[] values)
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Operator = op,
                                        Negate = negate,
                                        Values = values,
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.KnownEmpty)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Images.Some[1].Alternative[1]" &&
                    disposition.Disposition == "vacuous-omission"),
                Is.True);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Images.Some[1]" && disposition.Disposition == "known-empty"),
                Is.True);
        }
    }

    [TestCase(CriteriaValueOperator.Equals, "tmdb")]
    [TestCase(CriteriaValueOperator.NotEquals, "tvmaze")]
    public void Plan_KnownEmpty_WhenImageSourceCannotMatchMappedDomain(CriteriaValueOperator op, string value)
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Operator = op, Values = [value] }],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_KnownEmpty_WhenImageTypeConstraintsContradict()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Values = ["poster"],
                                    },
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["banner"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_ImageResidual_WhenNegationLeavesMappedTypes()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Negate = true,
                                        Values = ["still"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ImageResiduals, Has.Count.EqualTo(1));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ImageResidual_WhenPositiveGroupHasPossibleAlternative()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var impossible = new ImageCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["still"] }],
            },
        };
        var possible = new ImageCriteria
        {
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["poster"] }],
            },
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [impossible, possible],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ImageResiduals.Single().Alternatives, Has.Count.EqualTo(1));
            Assert.That(plan.ImageResiduals.Single().Alternatives.Single().Criteria, Is.SameAs(possible));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Images.Some[1].Alternative[1]" &&
                    disposition.Disposition == "vacuous-omission"),
                Is.True);
        }
    }

    [Test]
    public void Plan_OmitsImageExclusion_WhenAlternativeCannotMatchMappedDomain()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                None =
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
                                    Values = ["still"],
                                },
                            ],
                        },
                    },
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ImageResiduals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Images.None[1]" &&
                    disposition.Disposition == "vacuous-omission" &&
                    disposition.Negate),
                Is.True);
        }
    }

    [Test]
    public void Plan_GalleryResiduals_WhenImageMimeTypeOrLanguageIsConstrained()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Images = new CollectionCriteria<ImageCriteria>
            {
                Some =
                [
                    [
                        new ImageCriteria
                        {
                            MimeType = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["image/jpeg"],
                                    },
                                ],
                            },
                        },
                        new ImageCriteria
                        {
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["poster"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
                None =
                [
                    new ImageCriteria
                    {
                        Language = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["en"],
                                },
                            ],
                        },
                    },
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.ImageResiduals, Has.Count.EqualTo(2));
            Assert.That(
                plan.ImageResiduals[0].Alternatives.Select(alternative => alternative.Kind),
                Is.EqualTo(
                    [ImageCriteriaResidual.EvaluationKind.Gallery, ImageCriteriaResidual.EvaluationKind.Gallery]));
            Assert.That(plan.ImageResiduals[1].Negate, Is.True);
            Assert.That(
                plan.ImageResiduals[1].Alternatives.Single().Kind,
                Is.EqualTo(ImageCriteriaResidual.EvaluationKind.Gallery));
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReturnsUnboundedPlanWithWarning_WhenTitleUsesRegex()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Regex,
                        Values = ["Game.*Thrones"],
                    },
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Unbounded>());
        var node = (SearchPlanNode.Unbounded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(node.Warnings.Select(warning => warning), Has.Some.Contains("the title filter"));
            Assert.That(node.Warnings.Select(warning => warning), Has.Some.Contains("no search was run"));
        }
    }

    [Test]
    public void Plan_CompilesEveryTitleCriterion_AsLocalFilter()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["first"] },
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["second"] },
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((SearchSeed.FuzzyShow)leaf.Seed).Terms, Is.EqualTo(["first"]));
            Assert.That(leaf.HitFilters.Count(filter => filter.Kind == LocalFilterKind.Title), Is.EqualTo(2));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenExternalIdSourcesArePinnedToForeignProviders()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
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
                                        Values = ["tmdb"],
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
                                        Values = ["1399"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_IdLookup_WhenExternalIdAndTitleCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
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
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.IdLookup>());
        var seed = (SearchSeed.IdLookup)plan.Seed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seed.Lookups, Has.Count.EqualTo(1));
            Assert.That(seed.Lookups[0].Source, Is.EqualTo("imdb"));
            Assert.That(seed.Lookups[0].Value, Is.EqualTo("tt0411008"));
            Assert.That(seed.Lookups[0].ShowId, Is.Null);
            Assert.That(
                plan.HitFilters.Select(filter => filter.Kind),
                Is.EqualTo([LocalFilterKind.Title, LocalFilterKind.ExternalId]));
        }
    }

    [Test]
    public void Plan_IdLookup_WhenTvrageExternalIdCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [Constants.TvRageSource],
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
                                        Values = ["18164"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var lookup = ((SearchSeed.IdLookup)plan.Seed).Lookups.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookup.Source, Is.EqualTo("tvrage"));
            Assert.That(lookup.Value, Is.EqualTo("18164"));
            Assert.That(lookup.ShowId, Is.Null);
        }
    }

    [Test]
    public void Plan_DirectIdLookup_WhenExternalIdGroupContainsTvmazeAlternative()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
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
                        new ExternalIdCriteria
                        {
                            Source = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = [Constants.ProviderType],
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
                                        Values = ["82"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.IdLookup>());
        var lookups = ((SearchSeed.IdLookup)plan.Seed).Lookups;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookups, Has.Count.EqualTo(2));
            Assert.That(lookups[0].Source, Is.EqualTo("imdb"));
            Assert.That(lookups[0].Value, Is.EqualTo("tt0411008"));
            Assert.That(lookups[0].ShowId, Is.Null);
            Assert.That(lookups[1].Source, Is.EqualTo("tvmaze"));
            Assert.That(lookups[1].Value, Is.EqualTo("82"));
            Assert.That(lookups[1].ShowId, Is.EqualTo(82));
            Assert.That(
                plan.HitFilters.Select(filter => filter.Kind),
                Is.EqualTo([LocalFilterKind.Title, LocalFilterKind.ExternalId]));
        }
    }

    [Test]
    public void Plan_DirectIdLookup_WhenSourceCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["tvmaze:show:82"],
                    },
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.IdLookup>());
        var lookup = ((SearchSeed.IdLookup)plan.Seed).Lookups.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookup.Source, Is.EqualTo("tvmaze"));
            Assert.That(lookup.Value, Is.EqualTo("82"));
            Assert.That(lookup.ShowId, Is.EqualTo(82));
            Assert.That(plan.HitFilters.Single().Kind, Is.EqualTo(LocalFilterKind.Source));
        }
    }

    [Test]
    public void Plan_PersonSeed_WhenTitleAkaAndPersonCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Perdidos"] },
                ],
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
                                        Values = ["Matthew Fox"],
                                    },
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["cast"],
                                    },
                                ],
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["person"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.PersonChain>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.HitFilters.Single().Kind, Is.EqualTo(LocalFilterKind.Title));
            Assert.That(plan.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.OriginalTitle));
            Assert.That(plan.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Warnings, Is.EqualTo([SearchWarnings.PersonSearchApproximate]));
        }
    }

    [Test]
    public void Plan_PersonSeed_WhenOriginalTitleAndPersonCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Perdidos"] },
                ],
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["cast"],
                                    },
                                ],
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["person"],
                                    },
                                ],
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Matthew Fox"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.PersonChain>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.OriginalTitle));
            Assert.That(plan.Warnings, Is.EqualTo([SearchWarnings.PersonSearchApproximate]));
            Assert.That(plan.RelationshipResiduals, Is.Empty);
        }
    }

    [Test]
    public void Plan_AkaSeed_WhenAlternateTextCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Perdidos"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var seed = (SearchSeed.FuzzyShow)plan.Seed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seed.Terms, Is.EqualTo(["Perdidos"]));
            Assert.That(seed.IsAka, Is.True);
            Assert.That(plan.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.AlternateText));
        }
    }

    [Test]
    public void Plan_AkaSeed_WhenAlternateTextGroupHasMultipleAlternatives()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Perdidos"],
                                    },
                                ],
                            },
                        },
                        new AlternateTextCriteria
                        {
                            Text = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Desaparecidos"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var seed = (SearchSeed.FuzzyShow)plan.Seed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seed.Terms, Is.EqualTo(["Perdidos", "Desaparecidos"]));
            Assert.That(seed.IsAka, Is.True);
            Assert.That(plan.AkaFilters.Single().AlternateTextAlternatives, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_PersonChain_WhenDirectorRelationshipCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["director"],
                                    },
                                ],
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["person"],
                                    },
                                ],
                            },
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Mimi Leder"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.PersonChain>());
        var alternative = ((SearchSeed.PersonChain)plan.Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Names, Is.EqualTo(["Mimi Leder"]));
            Assert.That(alternative.IncludeCast, Is.False);
            Assert.That(alternative.IncludeCrew, Is.True);
            Assert.That(alternative.CrewKinds, Is.EqualTo(["director"]));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("person-name search is fuzzy"));
            Assert.That(plan.Warnings.Select(warning => warning), Has.None.Contains("at most"));
        }
    }

    [Test]
    public void Plan_SettledPersonChain_WhenTvmazePersonIdCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
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
                                                        Operator = CriteriaValueOperator.Equals,
                                                        Values = [Constants.ProviderType],
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
                                                        Values = ["14072"],
                                                    },
                                                ],
                                            },
                                        },
                                    ],
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["cast"],
                                    },
                                ],
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["person"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var alternative = ((SearchSeed.PersonChain)plan.Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(alternative.PersonIds, Is.EqualTo([14072L]));
            Assert.That(alternative.IncludeCast, Is.True);
            Assert.That(plan.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_Unbounded_WhenPersonIdAlternativeIncludesUnsupportedSource()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [
                        new RelationshipCriteria
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
                                                        Operator = CriteriaValueOperator.Equals,
                                                        Values = [Constants.ProviderType],
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
                                                        Values = ["14072"],
                                                    },
                                                ],
                                            },
                                        },
                                        new ExternalIdCriteria
                                        {
                                            Source = new CriteriaSet<PropertyCriterion<string>>
                                            {
                                                And =
                                                [
                                                    new PropertyCriterion<string>
                                                    {
                                                        Operator = CriteriaValueOperator.Equals,
                                                        Values = ["imdb"],
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
                                                        Values = ["nm0227759"],
                                                    },
                                                ],
                                            },
                                        },
                                    ],
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["cast"],
                                    },
                                ],
                            },
                            Type = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["person"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Unbounded>());
    }

    [Test]
    public void Plan_CheapestPersonChain_WhenLaterRelationshipGroupCostsLess()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var expensive = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Expensive"] },
                ],
            },
        } with
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["First Person", "Second Person"],
                    },
                ],
            },
        };
        var cheap = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Solo Person"],
                    },
                ],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }],
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }],
            },
        };
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [expensive],
                    [cheap],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var alternative = ((SearchSeed.PersonChain)plan.Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Names, Is.EqualTo(["Solo Person"]));
            Assert.That(alternative.IncludeCast, Is.True);
            Assert.That(alternative.IncludeCrew, Is.False);
            Assert.That(plan.RelationshipResiduals, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Plan_Unbounded_WhenNoCriterionCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["English"],
                    },
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan, Is.InstanceOf<SearchPlanNode.Unbounded>());
            Assert.That(
                ((SearchPlanNode.Unbounded)plan).Warnings.Select(warning => warning),
                Has.Some.Contains("requires a bounded identifier"));
        }
    }

    [Test]
    public void Plan_RecordsCriterionDispositions_OnEveryPlanNode()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var seededCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
        };
        var knownEmptyCriteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [new AwardCriteria()],
                ],
            },
        };
        var unboundedCriteria = new MetadataCriteria
        {
            Language = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["English"],
                    },
                ],
            },
        };

        // Act
        var seeded = (SearchPlanNode.Seeded)planner.Compile(seededCriteria);
        var knownEmpty = (SearchPlanNode.KnownEmpty)planner.Compile(knownEmptyCriteria);
        var unbounded = (SearchPlanNode.Unbounded)planner.Compile(unboundedCriteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeded.SeedReason, Is.Not.Empty);
            Assert.That(
                seeded.Dispositions.Any(disposition =>
                    disposition.Source == "Title" && disposition.Disposition == "hit-filter"),
                Is.True);
            Assert.That(
                knownEmpty.Dispositions.Any(disposition =>
                    disposition.Source == MetadataField.Awards.ToString() && disposition.Disposition == "known-empty"),
                Is.True);
            Assert.That(
                knownEmpty.Dispositions.Any(disposition =>
                    disposition.Source == "Title" && disposition.Disposition == "not-executed-known-empty"),
                Is.True);
            Assert.That(
                unbounded.Dispositions.Any(disposition =>
                    disposition.Source == "Language" &&
                    disposition.Disposition == "hit-filter-not-executed-unbounded"),
                Is.True);
            Assert.That(
                unbounded.Dispositions.Any(disposition => disposition.Disposition == "unbounded-refusal"),
                Is.True);
        }
    }

    [Test]
    public void Plan_PersonChainWithWarning_WhenPersonRelationshipHasQualifier()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var relationship = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"],
                    },
                ],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }],
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }],
            },
        } with
        {
            Qualifier = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Jack Shephard"],
                    },
                ],
            },
        };
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [relationship],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var seeded = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeded.Seed, Is.InstanceOf<SearchSeed.PersonChain>());
            Assert.That(seeded.RelationshipResiduals, Has.Count.EqualTo(1));
            Assert.That(seeded.Warnings.Select(warning => warning), Has.None.Contains("no search was run"));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentCollectionAndIdentifierCanSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Source = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["tvmaze:show:82"],
                    },
                ],
            },
            Awards = new CollectionCriteria<AwardCriteria>
            {
                Some =
                [
                    [new AwardCriteria()],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.KnownEmpty>());
    }

    [Test]
    public void Plan_PreservesIdentifierAlternatives_WhenMultipleIdentifiersAreProvided()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var ids = Enumerable.Range(1, 11).Select(value => $"tt{value:0000000}").ToArray();
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
                                        Values = ids,
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((SearchSeed.IdLookup)plan.Seed).Lookups, Has.Count.EqualTo(11));
            Assert.That(plan.Warnings.Select(warning => warning), Has.None.Contains("lookup"));
        }
    }

    [Test]
    public void Plan_NetworkResidual_WhenTitleAndNetworkRelationshipArePresent()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Values = ["HBO"],
                                    },
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["network"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var alternative = plan.RelationshipResiduals.Single().Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.MatchesNetwork, Is.True);
            Assert.That(alternative.MatchesPerson, Is.False);
            Assert.That(alternative.PassThrough, Is.False);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_HybridResidual_WhenTitleAndBareSubjectArePresent()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Values = ["HBO"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var alternative = plan.RelationshipResiduals.Single().Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.MatchesNetwork, Is.True);
            Assert.That(alternative.MatchesPerson, Is.True);
            Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.FuzzyShow>());
        }
    }

    [Test]
    public void Plan_SkipsEmptyRelationshipGroup_WhenTitleProvidesSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_PassesThroughPositiveGroup_WhenOneAlternativeTargetsUnknownRelationshipData()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                        Values = ["HBO"],
                                    },
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["network"],
                                    },
                                ],
                            },
                        },
                        new RelationshipCriteria
                        {
                            Subject = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["Acme"],
                                    },
                                ],
                            },
                            Kind = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And =
                                [
                                    new PropertyCriterion<string>
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["publisher"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var alternatives = plan.RelationshipResiduals.Single().Alternatives;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternatives, Has.Count.EqualTo(2));
            Assert.That(alternatives.Count(alternative => alternative.PassThrough), Is.EqualTo(1));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.One.Contains("positive relationship group 1"));
        }
    }

    [Test]
    public void Plan_Unbounded_WhenBareSubjectIsTheOnlyPossibleSeed()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                                        Operator = CriteriaValueOperator.Equals,
                                        Values = ["HBO"],
                                    },
                                ],
                            },
                        },
                    ],
                ],
            },
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Unbounded>());
    }

    [Test]
    public void Plan_SettlesPersonSeed_WhenOnlyContainerIdRemainsForCore()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var relationship = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"],
                    },
                ],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }],
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }],
            },
        } with
        {
            RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
            {
                And = [new PropertyCriterion<Guid> { Value = Guid.NewGuid() }],
            },
        };
        var criteria = new MetadataCriteria
        {
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [relationship],
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Seed, Is.InstanceOf<SearchSeed.PersonChain>());
            Assert.That(plan.RelationshipResiduals, Is.Empty);
        }
    }

    [Test]
    public void Plan_LeavesNegatedContainerRelationshipForCore_WhenProviderFacetsAreAlsoPresent()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var relationship = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"],
                    },
                ],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }],
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }],
            },
        } with
        {
            RelatedMetadataContainerId = new CriteriaSet<PropertyCriterion<Guid>>
            {
                And = [new PropertyCriterion<Guid> { Value = Guid.NewGuid() }],
            },
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Relationships = new CollectionCriteria<RelationshipCriteria> { None = [relationship] },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_NegatedPersonResidual_WhenTitleAndRelationshipExclusionArePresent()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["Matthew Fox"],
                                },
                            ],
                        },
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["cast"],
                                },
                            ],
                        },
                        Type = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["person"],
                                },
                            ],
                        },
                    },
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        var residual = plan.RelationshipResiduals.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(residual.Negate, Is.True);
            Assert.That(residual.Alternatives.Single().MatchesPerson, Is.True);
            Assert.That(residual.Alternatives.Single().MatchesNetwork, Is.False);
        }
    }

    [Test]
    public void Plan_PreservesSafeSupersetAndWarnings_WhenRelationshipsCannotBeEvaluated()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var unsupportedPositive = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["HBO"] }],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["network"],
                    },
                ],
            },
        } with
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Regex, Value = "H.*" }],
            },
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [unsupportedPositive],
                ],
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["Unknown"],
                                },
                            ],
                        },
                        Kind = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["publisher"],
                                },
                            ],
                        },
                    },
                ],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.RelationshipResiduals.Single().Alternatives.Single().PassThrough, Is.True);
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("positive relationship group 1"));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("relationship exclusion 1"));
        }
    }

    [Test]
    public void Plan_PreservesSafeSuperset_WhenRelationshipExternalIdExclusionUsesUnknownSource()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var relationship = new RelationshipCriteria
        {
            Subject = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["Matthew Fox"],
                    },
                ],
            },
            Kind = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["cast"] }],
            },
            Type = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["person"] }],
            },
        } with
        {
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
                                    Operator = CriteriaValueOperator.Equals,
                                    Values = ["wikidata"],
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
                                    Values = ["Q123"],
                                },
                            ],
                        },
                    },
                ],
            },
        };
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Relationships = new CollectionCriteria<RelationshipCriteria>
            {
                Some =
                [
                    [relationship],
                ],
                None = [relationship],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.RelationshipResiduals.Single().Alternatives.Single().PassThrough, Is.True);
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("positive relationship group 1"));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Has.Some.Contains("relationship exclusion 1"));
        }
    }

    [Test]
    public void Plan_WarnsWithoutDurationFilter_WhenDurationHasNoUsableValue()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["Lost"] }],
            },
            Duration = new CriteriaSet<PropertyCriterion<TimeSpan>>
            {
                And = [new PropertyCriterion<TimeSpan> { Operator = CriteriaValueOperator.Equals }],
            },
        };

        // Act
        var plan = (SearchPlanNode.Seeded)planner.Compile(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.HitFilters.Select(filter => filter.Kind), Is.EqualTo([LocalFilterKind.Title]));
            Assert.That(
                plan.Warnings,
                Is.EqualTo(["the duration filter".EmptyCriterionWarning()]));
        }
    }

    [Test]
    public void Plan_TitleSearch_WhenNegativeOperatorIsDoubleNegated()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.NotStartsWithString,
                        Negate = true,
                        Value = "Game of Thrones",
                        Values = [null!, "Game"]
                    }
                ]
            }
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((SearchSeed.FuzzyShow)leaf.Seed).Terms, Is.EqualTo(["Game of Thrones", "Game"]));
            Assert.That(leaf.Warnings, Is.Empty);
        }
    }

    [Test]
    public void Plan_ReportsOnlyTheSearchTermUnitAsApplied_WhenTitleCriteriaRepeat()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criteria = new MetadataCriteria
        {
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["first"] },
                    new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["second"] }
                ]
            }
        };

        // Act
        var plan = planner.Compile(criteria);

        // Assert
        Assert.That(plan, Is.InstanceOf<SearchPlanNode.Seeded>());
        var leaf = (SearchPlanNode.Seeded)plan;
        Assert.That(((SearchSeed.FuzzyShow)leaf.Seed).Terms, Is.EqualTo(["first"]));
    }

    [Test]
    public void Plan_BrowseAnchorAndNormalizedMaximum_WhenNoPositiveAnchor()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "tv" }]
            },
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "show" }]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.NotContainsString,
                                    Value = "international"
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
                                            And =
                                            [
                                                new PropertyCriterion<string>
                                                {
                                                    Operator = CriteriaValueOperator.Equals,
                                                    Value = "Drama"
                                                }
                                            ]
                                        }
                                    }
                                ]
                            ]
                        },
                        Relationships = new CollectionCriteria<RelationshipCriteria>
                        {
                            None =
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
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "John Smith"
                                            }
                                        ]
                                    }
                                }
                            ]
                        },
                        Images = new CollectionCriteria<ImageCriteria>
                        {
                            Some = [[new ImageCriteria()]]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 0);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var fetch = (FetchPlanNode)filter.Child;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Query, Is.SameAs(query));
            Assert.That(plan.MaximumResults, Is.EqualTo(1));
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.Browse));
            Assert.That(plan.Warnings, Does.Not.Contain(SearchWarnings.Unbounded));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == LocalFilterKind.Genre.ToString() &&
                    disposition.Disposition == "hit-filter"),
                Is.True);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == LocalFilterKind.OriginalTitle.ToString() &&
                    disposition.Disposition == "aka-verification"),
                Is.True);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "relationship" &&
                    disposition.Disposition == "credits-residual"),
                Is.True);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "image" &&
                    disposition.Disposition == "hit-or-images-residual"),
                Is.True);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Disposition.Contains("not-executed-unbounded", StringComparison.Ordinal)),
                Is.False);
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Anchor" &&
                    Equals(disposition.OperatorsAndValues, "shows?page") &&
                    disposition.Disposition == "browse-anchor"),
                Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Plan_SafeSuperset_WhenContentTypeOperatorIsUnsupported(bool negate)
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var criterion = new PropertyCriterion<string>
        {
            Operator = CriteriaValueOperator.Regex,
            Value = "^tv$",
            Negate = negate
        };
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>> { And = [criterion] },
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
                                    Value = "lost"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var fetch = (FetchPlanNode)filter.Child;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(
                plan.Warnings.Select(warning => warning),
                Does.Contain(
                    "tvmaze cannot evaluate the requested operator for the content type filter; that criterion was" +
                    " not applied."));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "ContentType" &&
                    Equals(disposition.OperatorsAndValues, criterion) &&
                    disposition.Disposition == "safe-superset"),
                Is.True);
        }
    }

    [Test]
    public void Plan_TitleAnchor_WarnsThatUnpagedRankedSearchMayOmitMatches()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "lost"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Warnings, Does.Contain(SearchWarnings.TitleSearchApproximate));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "seed:title" &&
                    disposition.Disposition == "warning" &&
                    Equals(disposition.OperatorsAndValues, SearchWarnings.TitleSearchApproximate)),
                Is.True);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenLevelExcludesShow()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "episode" }]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root, Is.InstanceOf<UnionPlanNode>());
            Assert.That(((UnionPlanNode)plan.Root).Children, Is.Empty);
        }
    }

    [Test]
    public void Plan_IntersectsPersonGroupsFirst_WhenTitleIsAlsoPresent()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "office"
                                }
                            ]
                        },
                        OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "La Oficina"
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
                                                    Operator = CriteriaValueOperator.ContainsString,
                                                    Value = "Steve Carell"
                                                }
                                            ]
                                        }
                                    }
                                ],
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
                                                    Operator = CriteriaValueOperator.ContainsString,
                                                    Value = "Rainn Wilson"
                                                }
                                            ]
                                        }
                                    }
                                ]
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<IntersectPlanNode>());
        var intersect = (IntersectPlanNode)filter.Child;
        var personUnions = intersect.Children.Cast<UnionPlanNode>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(intersect.Children, Has.Count.EqualTo(2));
            Assert.That(intersect.Children, Has.All.InstanceOf<UnionPlanNode>());
            Assert.That(
                personUnions.SelectMany(union => union.Children).Cast<FetchPlanNode>()
                    .Select(child => child.AnchorKind),
                Is.All.EqualTo(SearchAnchorKind.Person));
            Assert.That(
                personUnions.SelectMany(union => union.Children).Cast<FetchPlanNode>()
                    .Select(child => child.Resolve),
                Is.All.Not.Null);
            Assert.That(personUnions, Has.All.Property(nameof(UnionPlanNode.Children)).Count.EqualTo(2));
            Assert.That(filter.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Warnings, Does.Contain(SearchWarnings.PersonSearchApproximate));
            Assert.That(plan.Warnings, Does.Not.Contain(SearchWarnings.TitleSearchApproximate));
            Assert.That(plan.Warnings, Does.Not.Contain(SearchWarnings.AkaSearchApproximate));
            Assert.That(
                plan.Dispositions.Count(disposition =>
                    disposition.Source.StartsWith("Relationships.Some[", StringComparison.Ordinal) &&
                    disposition.Disposition == "seed-settled"),
                Is.EqualTo(2));
        }
    }

    [Test]
    public void Plan_OmitsNestedLocalRelationshipAndImageCriteriaPayloads_WhenTheResultIsSerialized()
    {
        // Arrange
        var planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
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
                            And =
                            [
                                new PropertyCriterion<string>
                                {
                                    Operator = CriteriaValueOperator.ContainsString,
                                    Value = "trace anchor"
                                }
                            ]
                        },
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
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "secret-local-genre"
                                            }
                                        ]
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
                                    Type = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "network"
                                            }
                                        ]
                                    },
                                    Subject = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "secret-relationship-subject"
                                            }
                                        ]
                                    }
                                }
                            ]]
                        },
                        Images = new CollectionCriteria<ImageCriteria>
                        {
                            Some =
                            [[
                                new ImageCriteria
                                {
                                    Type = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "banner"
                                            }
                                        ]
                                    },
                                    Url = new CriteriaSet<PropertyCriterion<string>>
                                    {
                                        And =
                                        [
                                            new PropertyCriterion<string>
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "https://secret-image-url.example/poster.jpg"
                                            }
                                        ]
                                    }
                                }
                            ]]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = planner.Plan(query, maxResults: 10);

        // Assert
        var json = JsonSerializer.Serialize(
            plan,
            new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.GetProperty("root");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("hitFilterCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("relationshipResidualCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("imageResidualCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(json, Does.Not.Contain("secret-local-genre"));
            Assert.That(json, Does.Not.Contain("secret-relationship-subject"));
            Assert.That(json, Does.Not.Contain("secret-image-url"));
            Assert.That(json, Does.Not.Contain("hitFilters"));
            Assert.That(json, Does.Not.Contain("relationshipResiduals"));
            Assert.That(json, Does.Not.Contain("imageResiduals"));
            Assert.That(json, Does.Not.Contain("operatorsAndValues"));
        }
    }
}
