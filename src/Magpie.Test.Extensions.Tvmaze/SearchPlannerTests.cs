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
public class SearchPlannerTests
{
    private SearchPlanner _planner = null!;

    [SetUp]
    public void SetUp()
    {
        _planner = new SearchPlanner(NullLogger<SearchPlanner>.Instance);
    }

    [Test]
    public void Plan_TitleSearch_WhenProviderTypeAcceptsTvmaze()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenProviderTypeExcludesTvmaze()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [TestCase("book-publisher", true)]
    [TestCase("network", false)]
    [TestCase("cast", false)]
    public void Plan_KnownEmpty_WhenRequiredRelationshipTypeCannotComeFromTvmaze(
        string relationshipType,
        bool expectedKnownEmpty)
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
                                        Values = [relationshipType]
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
                                        Values = ["Penguin"]
                                    }
                                ]
                            }
                        }
                    ],
                ],
            },
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes == "known-empty show candidates", Is.EqualTo(expectedKnownEmpty));
    }

    [Test]
    public void Plan_KnownEmpty_WhenProviderTypeNegationExcludesTvmaze()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_TitleSearch_WhenProviderTypeNegationTargetsAnotherProvider()
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
                        Values = ["tmdb"],
                    },
                ],
            },
            Title = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Values = ["lost"] }],
            },
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_HandlesProviderTypeNullChecksAsSelfIdentity_WhenTheProviderTypeCriterionIsANullCheck()
    {
        // Arrange
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

        var isNullQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [isNull] }
        };

        var isNotNullQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [isNotNull] }
        };

        // Act
        var nullPlan = _planner.Plan(isNullQuery, maxResults: 25);
        var notNullPlan = _planner.Plan(isNotNullQuery, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullPlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(notNullPlan.Root, Is.InstanceOf<FilterPlanNode>());
            Assert.That(
                notNullPlan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(notNullPlan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceCannotMatchAnyShowSourceKey()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_SourceFilter_WhenSourceTargetsOneShowSourceKey()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.HitFilters.Any(hitFilter => hitFilter.Kind == LocalFilterKind.Source), Is.True);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenSourceNegationExcludesEveryShowSourceKey()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_ApplyEmptyCollectionSemantics_WhenAbsentTags()
    {
        // Arrange
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

        var positiveQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [positive] }
        };

        var excludedQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [excluded] }
        };

        // Act
        var positivePlan = _planner.Plan(positiveQuery, maxResults: 25);
        var excludedPlan = _planner.Plan(excludedQuery, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)excludedPlan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(excludedPlan.Root, Is.InstanceOf<FilterPlanNode>());
            Assert.That(
                excludedPlan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(excludedPlan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_EvaluatesPersonDatesAsAbsentConcepts_WhenBirthOrDeathDateCriteriaArePresent()
    {
        // Arrange
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

        var positiveQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [positive] }
        };

        var negatedQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [negated] }
        };

        var isNullQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [isNull] }
        };

        var isNotNullQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [isNotNull] }
        };

        // Act
        var positivePlan = _planner.Plan(positiveQuery, maxResults: 25);
        var negatedPlan = _planner.Plan(negatedQuery, maxResults: 25);
        var isNullPlan = _planner.Plan(isNullQuery, maxResults: 25);
        var isNotNullPlan = _planner.Plan(isNotNullQuery, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(negatedPlan.Root, Is.InstanceOf<FilterPlanNode>());
            Assert.That(
                negatedPlan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(negatedPlan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(isNullPlan.Root, Is.InstanceOf<FilterPlanNode>());
            Assert.That(
                isNullPlan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(isNullPlan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(isNotNullPlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentCollectionsAreRequired()
    {
        // Arrange
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

        var awardsQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [awards] }
        };

        var contentRatingsQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [contentRatings] }
        };

        var previewsQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [previews] }
        };

        // Act
        var awardsPlan = _planner.Plan(awardsQuery, maxResults: 25);
        var contentRatingsPlan = _planner.Plan(contentRatingsQuery, maxResults: 25);
        var previewsPlan = _planner.Plan(previewsQuery, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(awardsPlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(contentRatingsPlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(previewsPlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
        }
    }

    [Test]
    public void Plan_TitleSearch_WhenAbsentCollectionsAreExcluded()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
        }
    }

    [Test]
    public void Plan_EvaluatesCustomMetadataAsAbsentConcepts_WhenCustomMetadataCriteriaArePresent()
    {
        // Arrange
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

        var positiveQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [positive] }
        };

        var negatedQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [negated] }
        };

        // Act
        var positivePlan = _planner.Plan(positiveQuery, maxResults: 25);
        var negatedPlan = _planner.Plan(negatedQuery, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(positivePlan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(negatedPlan.Root, Is.InstanceOf<FilterPlanNode>());
            Assert.That(
                negatedPlan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(negatedPlan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_CompileAsResiduals_WhenRelationshipAndImageCriteria()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
            Assert.That(filter.RelationshipResiduals, Has.Count.EqualTo(1));
            Assert.That(filter.ImageResiduals, Has.Count.EqualTo(1));
            Assert.That(filter.ImageResiduals.Single().Negate, Is.True);
            Assert.That(
                filter.ImageResiduals.Single().Alternatives.Single().Kind,
                Is.EqualTo(ImageCriteriaResidual.EvaluationKind.Presence));
            Assert.That(plan.Dispositions.Count(disposition =>
                disposition.Source == "Relationships.Some[1]" &&
                disposition.Disposition == "pass-through-residual"), Is.EqualTo(1));
        }
    }

    [Test]
    public void Plan_SplitPresenceAndGalleryResiduals_WhenImageCriteria()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.ImageResiduals, Has.Count.EqualTo(3));
            Assert.That(
                filter.ImageResiduals.Select(residual => residual.Alternatives.Single().Kind),
                Is.EqualTo(
                    [
                        ImageCriteriaResidual.EvaluationKind.Presence,
                        ImageCriteriaResidual.EvaluationKind.Gallery,
                        ImageCriteriaResidual.EvaluationKind.Presence,
                    ]));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenRequiredImageFieldMustBeNull()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_KnownEmpty_WhenImageTypeConstraintsContradict()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_ImageResidual_WhenNegationLeavesMappedTypes()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.ImageResiduals, Has.Count.EqualTo(1));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_ImageResidual_WhenPositiveGroupHasPossibleAlternative()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.ImageResiduals.Single().Alternatives, Has.Count.EqualTo(1));
            Assert.That(filter.ImageResiduals.Single().Alternatives.Single().Criteria, Is.SameAs(possible));
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.ImageResiduals, Is.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.ImageResiduals, Has.Count.EqualTo(2));
            Assert.That(
                filter.ImageResiduals[0].Alternatives.Select(alternative => alternative.Kind),
                Is.EqualTo(
                    [ImageCriteriaResidual.EvaluationKind.Gallery, ImageCriteriaResidual.EvaluationKind.Gallery]));
            Assert.That(filter.ImageResiduals[1].Negate, Is.True);
            Assert.That(
                filter.ImageResiduals[1].Alternatives.Single().Kind,
                Is.EqualTo(ImageCriteriaResidual.EvaluationKind.Gallery));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_PreservesResidual_WhenTitleUsesRegex()
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
                        Values = ["Game.*Thrones"],
                    },
                ],
            },
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Browse));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Title" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
            Assert.That(plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("unbounded-refusal"));
        }
    }

    [Test]
    public void Plan_CompilesEveryTitleCriterionAsLocalFilter_WhenTwoTitleEqualityCriteriaAreAnded()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((SearchSeed.FuzzyShow)((FetchPlanNode)filter.Child).Seed).Terms, Is.EqualTo(["first"]));
            Assert.That(filter.HitFilters.Count(hitFilter => hitFilter.Kind == LocalFilterKind.Title), Is.EqualTo(2));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenExternalIdSourcesArePinnedToForeignProviders()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_IdLookup_WhenExternalIdAndTitleCanSeed()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var fetch = (FetchPlanNode)filter.Child;
        var seed = (SearchSeed.IdLookup)fetch.Seed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.ExternalId));
            Assert.That(seed.Lookups, Has.Count.EqualTo(1));
            Assert.That(seed.Lookups[0].Source, Is.EqualTo("imdb"));
            Assert.That(seed.Lookups[0].Value, Is.EqualTo("tt0411008"));
            Assert.That(seed.Lookups[0].ShowId, Is.Null);
            Assert.That(
                filter.HitFilters.Select(hitFilter => hitFilter.Kind),
                Is.EqualTo([LocalFilterKind.Title, LocalFilterKind.ExternalId]));
        }
    }

    [Test]
    public void Plan_IdLookup_WhenTvrageExternalIdCanSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var lookup = ((SearchSeed.IdLookup)((FetchPlanNode)filter.Child).Seed).Lookups.Single();
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<UnionPlanNode>());
        var lookups = ((UnionPlanNode)filter.Child).Children
            .Cast<FetchPlanNode>()
            .Select(child => ((SearchSeed.IdLookup)child.Seed).Lookups.Single())
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookups, Has.Length.EqualTo(2));
            Assert.That(lookups[0].Source, Is.EqualTo("imdb"));
            Assert.That(lookups[0].Value, Is.EqualTo("tt0411008"));
            Assert.That(lookups[0].ShowId, Is.Null);
            Assert.That(lookups[1].Source, Is.EqualTo("tvmaze"));
            Assert.That(lookups[1].Value, Is.EqualTo("82"));
            Assert.That(lookups[1].ShowId, Is.EqualTo(82));
            Assert.That(
                filter.HitFilters.Select(hitFilter => hitFilter.Kind),
                Is.EqualTo([LocalFilterKind.Title, LocalFilterKind.ExternalId]));
        }
    }

    [Test]
    public void Plan_DirectIdLookup_WhenSourceCanSeed()
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
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["tvmaze:show:82"],
                    },
                ],
            },
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var lookup = ((SearchSeed.IdLookup)((FetchPlanNode)filter.Child).Seed).Lookups.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookup.Source, Is.EqualTo("tvmaze"));
            Assert.That(lookup.Value, Is.EqualTo("82"));
            Assert.That(lookup.ShowId, Is.EqualTo(82));
            Assert.That(filter.HitFilters.Single().Kind, Is.EqualTo(LocalFilterKind.Source));
        }
    }

    [Test]
    public void Plan_PersonSeed_WhenTitleAkaAndPersonCanSeed()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Person));
            Assert.That(filter.HitFilters.Single().Kind, Is.EqualTo(LocalFilterKind.Title));
            Assert.That(filter.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.OriginalTitle));
            Assert.That(filter.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_PersonSeed_WhenOriginalTitleAndPersonCanSeed()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Person));
            Assert.That(filter.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.OriginalTitle));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
            Assert.That(filter.RelationshipResiduals, Is.Empty);
        }
    }

    [Test]
    public void Plan_AkaSeed_WhenAlternateTextCanSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var seed = (SearchSeed.FuzzyShow)((FetchPlanNode)filter.Child).Seed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seed.Terms, Is.EqualTo(["Perdidos"]));
            Assert.That(seed.IsAka, Is.True);
            Assert.That(filter.AkaFilters.Single().Kind, Is.EqualTo(LocalFilterKind.AlternateText));
        }
    }

    [Test]
    public void Plan_AkaSeed_WhenAlternateTextGroupHasMultipleAlternatives()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<UnionPlanNode>());
        var seeds = ((UnionPlanNode)filter.Child).Children
            .Cast<FetchPlanNode>()
            .Select(child => (SearchSeed.FuzzyShow)child.Seed)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeds.Select(seed => seed.Terms.Single()), Is.EqualTo(["Perdidos", "Desaparecidos"]));
            Assert.That(seeds.Select(seed => seed.IsAka), Is.All.True);
            Assert.That(filter.AkaFilters.Single().AlternateTextAlternatives, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Plan_PersonChain_WhenDirectorRelationshipCanSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var fetch = (FetchPlanNode)filter.Child;
        var alternative = ((SearchSeed.PersonChain)fetch.Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.Person));
            Assert.That(alternative.Names, Is.EqualTo(["Mimi Leder"]));
            Assert.That(alternative.IncludeCast, Is.False);
            Assert.That(alternative.IncludeCrew, Is.True);
            Assert.That(alternative.CrewKinds, Is.EqualTo(["director"]));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_SettledPersonChain_WhenTvmazePersonIdCanSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var fetch = (FetchPlanNode)filter.Child;
        var alternative = ((SearchSeed.PersonChain)fetch.Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Names, Is.Empty);
            Assert.That(alternative.PersonIds, Is.EqualTo([14072L]));
            Assert.That(alternative.IncludeCast, Is.True);
            Assert.That(filter.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_Browse_WhenPersonIdAlternativeIncludesUnsupportedSource()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Browse));
    }

    [Test]
    public void Plan_CheapestPersonChain_WhenLaterRelationshipGroupCostsLess()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<FetchPlanNode>());
        var alternative = ((SearchSeed.PersonChain)((FetchPlanNode)filter.Child).Seed).Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.Names, Is.EqualTo(["Solo Person"]));
            Assert.That(alternative.IncludeCast, Is.True);
            Assert.That(alternative.IncludeCrew, Is.False);
            Assert.That(filter.RelationshipResiduals, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Plan_Browse_WhenNoCriterionCanSeed()
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
                        Operator = CriteriaValueOperator.Equals,
                        Values = ["English"],
                    },
                ],
            },
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Browse));
            Assert.That(plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("unbounded-refusal"));
        }
    }

    [Test]
    public void Plan_RecordsCriterionDispositions_WhenEveryPlanNode()
    {
        // Arrange
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
        var browseCriteria = new MetadataCriteria
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

        var seededQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [seededCriteria] }
        };

        var knownEmptyQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [knownEmptyCriteria] }
        };

        var browseQuery = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [browseCriteria] }
        };

        // Act
        var seeded = _planner.Plan(seededQuery, maxResults: 25);
        var knownEmpty = _planner.Plan(knownEmptyQuery, maxResults: 25);
        var browse = _planner.Plan(browseQuery, maxResults: 25);

        // Assert
        var browseFilter = (FilterPlanNode)browse.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                seeded.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(
                seeded.Dispositions.Any(disposition =>
                    disposition.Source == "Title" && disposition.Disposition == "hit-filter"),
                Is.True);
            Assert.That(knownEmpty.Root.Contributes, Is.EqualTo("known-empty show candidates"));
            Assert.That(
                knownEmpty.Dispositions.Any(disposition =>
                    disposition.Source == MetadataField.Awards.ToString() && disposition.Disposition == "known-empty"),
                Is.True);
            Assert.That(
                knownEmpty.Dispositions.Any(disposition =>
                    disposition.Source == "Title" && disposition.Disposition == "not-executed-known-empty"),
                Is.True);
            Assert.That(
                browse.Dispositions.Any(disposition =>
                    disposition.Source == "Language" &&
                    disposition.Disposition == "hit-filter"),
                Is.True);
            Assert.That(
                browseFilter.HitFilters.Select(hitFilter => hitFilter.Kind),
                Is.EqualTo([LocalFilterKind.Language]));
        }
    }

    [Test]
    public void Plan_PersonChainWithResidual_WhenPersonRelationshipHasQualifier()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Person));
            Assert.That(filter.RelationshipResiduals, Has.Count.EqualTo(1));
            Assert.That(plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("unbounded-refusal"));
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenAbsentCollectionAndIdentifierCanSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root.Contributes, Is.EqualTo("known-empty show candidates"));
    }

    [Test]
    public void Plan_PreservesIdentifierAlternatives_WhenMultipleIdentifiersAreProvided()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<UnionPlanNode>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((UnionPlanNode)filter.Child).Children, Has.Count.EqualTo(11));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_NetworkResidual_WhenTitleAndNetworkRelationshipArePresent()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var alternative = filter.RelationshipResiduals.Single().Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(alternative.MatchesNetwork, Is.True);
            Assert.That(alternative.MatchesPerson, Is.False);
            Assert.That(alternative.PassThrough, Is.False);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_HybridResidual_WhenTitleAndBareSubjectArePresent()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var alternative = filter.RelationshipResiduals.Single().Alternatives.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternative.MatchesNetwork, Is.True);
            Assert.That(alternative.MatchesPerson, Is.True);
            Assert.That(((FetchPlanNode)filter.Child).Seed, Is.InstanceOf<SearchSeed.FuzzyShow>());
        }
    }

    [Test]
    public void Plan_SkipsEmptyRelationshipGroup_WhenTitleProvidesSeed()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_PassesThroughPositiveGroup_WhenOneAlternativeTargetsUnknownRelationshipData()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var alternatives = filter.RelationshipResiduals.Single().Alternatives;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(alternatives, Has.Count.EqualTo(2));
            Assert.That(alternatives.Count(alternative => alternative.PassThrough), Is.EqualTo(1));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Relationships.Some[1]" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_Browse_WhenBareSubjectIsTheOnlyPossibleSeed()
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Browse));
    }

    [Test]
    public void Plan_SettlesPersonSeed_WhenOnlyContainerIdRemainsForCore()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((FetchPlanNode)filter.Child).AnchorKind, Is.EqualTo(SearchAnchorKind.Person));
            Assert.That(filter.RelationshipResiduals, Is.Empty);
        }
    }

    [Test]
    public void Plan_LeavesNegatedContainerRelationshipForCore_WhenProviderFacetsAreAlsoPresent()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.RelationshipResiduals, Is.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_NegatedPersonResidual_WhenTitleAndRelationshipExclusionArePresent()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var residual = filter.RelationshipResiduals.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(residual.Negate, Is.True);
            Assert.That(residual.Alternatives.Single().MatchesPerson, Is.True);
            Assert.That(residual.Alternatives.Single().MatchesNetwork, Is.False);
        }
    }

    [Test]
    public void Plan_PreservesSafeSuperset_WhenRelationshipsCannotBeEvaluated()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.RelationshipResiduals.Single().Alternatives.Single().PassThrough, Is.True);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Relationships.Some[1]" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Relationships.None[1]" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_PreservesSafeSuperset_WhenRelationshipExternalIdExclusionUsesUnknownSource()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.RelationshipResiduals.Single().Alternatives.Single().PassThrough, Is.True);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Relationships.Some[1]" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Relationships.None[1]" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_WarnsWithoutDurationFilter_WhenDurationHasNoUsableValue()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("browse-anchor"));
            Assert.That(filter.HitFilters.Select(hitFilter => hitFilter.Kind), Is.EqualTo([LocalFilterKind.Title]));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Source == "Duration" && disposition.Disposition == "pass-through-residual"),
                Is.Not.Empty);
        }
    }

    [Test]
    public void Plan_TitleSearch_WhenNegativeOperatorIsDoubleNegated()
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
                        Operator = CriteriaValueOperator.NotStartsWithString,
                        Negate = true,
                        Value = "Game of Thrones",
                        Values = [null!, "Game"]
                    }
                ]
            }
        };

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Root, Is.InstanceOf<FilterPlanNode>());
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(filter.Child, Is.InstanceOf<UnionPlanNode>());
        var seeds = ((UnionPlanNode)filter.Child).Children
            .Cast<FetchPlanNode>()
            .Select(child => (SearchSeed.FuzzyShow)child.Seed)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeds.Select(seed => seed.Terms.Single()), Is.EqualTo(["Game of Thrones", "Game"]));
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
        }
    }

    [Test]
    public void Plan_ReportsOnlyTheSearchTermUnitAsApplied_WhenTitleCriteriaRepeat()
    {
        // Arrange
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

        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria> { And = [criteria] }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        Assert.That(((SearchSeed.FuzzyShow)((FetchPlanNode)filter.Child).Seed).Terms, Is.EqualTo(["first"]));
    }

    [Test]
    public void Plan_BrowseAnchorAndNormalizedMaximum_WhenNoPositiveAnchor()
    {
        // Arrange
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
        var plan = _planner.Plan(query, maxResults: 0);

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
            Assert.That(plan.Dispositions.Select(disposition => disposition.Disposition),
                Does.Not.Contain("unbounded-refusal"));
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
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var fetch = (FetchPlanNode)filter.Child;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "ContentType" &&
                    Equals(disposition.OperatorsAndValues, criterion) &&
                    disposition.Disposition == "safe-superset"),
                Is.True);
        }
    }

    [Test]
    public void Plan_KeepsTitleAnchorWithoutWarning_WhenTitleSeedIsUsable()
    {
        // Arrange
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
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
    }

    [Test]
    public void Plan_SafeSuperset_WhenLevelValueListIsEmpty()
    {
        // Arrange
        var criterion = new PropertyCriterion<string> { Operator = CriteriaValueOperator.In, Values = [] };
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>> { And = [criterion] },
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
        var plan = _planner.Plan(query, maxResults: 25);

        // Assert
        var filter = (FilterPlanNode)plan.Root;
        var fetch = (FetchPlanNode)filter.Child;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.AnchorKind, Is.EqualTo(SearchAnchorKind.Title));
            Assert.That(
                plan.Dispositions.Any(disposition =>
                    disposition.Source == "Level" &&
                    Equals(disposition.OperatorsAndValues, criterion) &&
                    disposition.Disposition == "safe-superset"),
                Is.True);
        }
    }

    [Test]
    public void Plan_KnownEmpty_WhenLevelExcludesShow()
    {
        // Arrange
        var query = new ContentHierarchyItemCriteria
        {
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Operator = CriteriaValueOperator.Equals, Value = "episode" }]
            }
        };

        // Act
        var plan = _planner.Plan(query, maxResults: 25);

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
        var plan = _planner.Plan(query, maxResults: 25);

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
            Assert.That(plan.Dispositions.Where(disposition =>
                disposition.Disposition == "pass-through-residual"), Is.Empty);
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
        var plan = _planner.Plan(query, maxResults: 10);

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
            Assert.That(root.GetProperty("hitFilterCount").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("relationshipResidualCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("imageResidualCount").GetInt32(), Is.EqualTo(1));
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
