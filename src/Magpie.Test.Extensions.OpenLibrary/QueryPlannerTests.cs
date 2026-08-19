using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class QueryPlannerTests
{
    private static IEnumerable<TestCaseData> HierarchyCriteriaCases
    {
        get
        {
            return
            [
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And = [new PropertyCriterion<Guid> { Operator = CriteriaValueOperator.IsNull }]
                        }
                    },
                    "Ready",
                    "parentId:constant-absent").SetName("parent null matches"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And = [new PropertyCriterion<Guid> {
                                Value = Guid.Parse("11111111-1111-1111-1111-111111111111") }]
                        }
                    },
                    "Empty",
                    "parentId:constant-absent").SetName("parent value contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        RootFolderId = new CriteriaSet<PropertyCriterion<Guid>>
                        {
                            And = [new PropertyCriterion<Guid> { Operator = CriteriaValueOperator.IsNotNull }]
                        }
                    },
                    "Empty",
                    "rootFolderId:constant-absent").SetName("root folder contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        ContentItemId = Guid.Parse("22222222-2222-2222-2222-222222222222")
                    },
                    "Empty",
                    "contentItemId:constant-absent").SetName("content item id contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        Slug = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "dune" }]
                        }
                    },
                    "Empty",
                    "slug:constant-absent").SetName("slug contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        Monitoring = new CriteriaSet<PropertyCriterion<bool>>
                        {
                            And = [new PropertyCriterion<bool> { Value = true }]
                        }
                    },
                    "Empty",
                    "monitoring:constant-absent").SetName("monitoring contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        EffectiveMonitoring = false
                    },
                    "Empty",
                    "effectiveMonitoring:constant-absent").SetName("effective monitoring contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        EffectiveRootFolderId = Guid.Parse("33333333-3333-3333-3333-333333333333")
                    },
                    "Empty",
                    "effectiveRootFolderId:constant-absent").SetName("effective root folder contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        Fulfillment = new CriteriaSet<PropertyCriterion<FulfillmentStatus>>
                        {
                            And = [new PropertyCriterion<FulfillmentStatus> { Value = FulfillmentStatus.Missing }]
                        }
                    },
                    "Empty",
                    "fulfillment:constant-absent").SetName("fulfillment contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        ActiveAcquisitionStatus = new CriteriaSet<PropertyCriterion<DownloadStatus>>
                        {
                            And = [new PropertyCriterion<DownloadStatus> { Value = DownloadStatus.Downloading }]
                        }
                    },
                    "Empty",
                    "activeAcquisitionStatus:constant-absent").SetName("acquisition state contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        QualityStatus = new CriteriaSet<PropertyCriterion<QualityStatus>>
                        {
                            And = [new PropertyCriterion<QualityStatus> { Value = QualityStatus.Met }]
                        }
                    },
                    "Empty",
                    "qualityStatus:constant-absent").SetName("quality state contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        Path = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "books/dune.epub" }]
                        }
                    },
                    "Empty",
                    "path:constant-absent").SetName("path contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        Tags = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "favorite" }]
                        }
                    },
                    "Empty",
                    "tags:constant-absent").SetName("library tags contradict"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        RootLevelOnly = true,
                        LeafOnly = true
                    },
                    "Ready",
                    "rootLevelOnly:constant-true").SetName("flat root and leaf match"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        RootLevelOnly = false
                    },
                    "Empty",
                    "rootLevelOnly:constant-true").SetName("nonroot contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        LeafOnly = false
                    },
                    "Empty",
                    "leafOnly:constant-true").SetName("nonleaf contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        PendingMetadataRefresh = true
                    },
                    "Empty",
                    "pendingMetadataRefresh:constant-absent").SetName("refresh state contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        HasMetadataExternalIds = true
                    },
                    "Ready",
                    "hasMetadataExternalIds:constant-true").SetName("work identity matches"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        HasMetadataExternalIds = false
                    },
                    "Empty",
                    "hasMetadataExternalIds:constant-true").SetName("missing work identity contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        HasSearchableTitle = true
                    },
                    "Ready",
                    "hasSearchableTitle:constant-true").SetName("searchable title matches"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        HasSearchableTitle = false
                    },
                    "Empty",
                    "hasSearchableTitle:constant-true").SetName("missing searchable title contradicts"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        ExcludeIds = [Guid.Parse("44444444-4444-4444-4444-444444444444")]
                    },
                    "Ready",
                    "excludeIds:constant-absent-content-item-id").SetName("local exclusions cannot match"),
                new TestCaseData(
                    new ContentHierarchyItemCriteria
                    {
                        Name = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        AncestorId = Guid.Parse("55555555-5555-5555-5555-555555555555")
                    },
                    "Empty",
                    "ancestorId:constant-root").SetName("ancestor contradicts")
            ];
        }
    }

    [TestCaseSource(nameof(HierarchyCriteriaCases))]
    public void Plan_State_WhenHierarchyFieldIsDispositioned(
        ContentHierarchyItemCriteria criteria,
        string expectedState,
        string expectedDisposition)
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = criteria,
            MaxResults = 20
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.State.ToString(), Is.EqualTo(expectedState));
            Assert.That(plan.CriterionDispositions, Does.Contain(expectedDisposition));
        }
    }

    [Test]
    public void Plan_FetchNode_WhenOrdinarySearchIsRequested()
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.OrdinarySearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Value = "  The Hobbit  ",
            LanguagePreference = "en-US",
            Page = 3
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(plan.Root.SourceTerms.Single().Field, Is.EqualTo("title"));
            Assert.That(plan.Root.SourceTerms.Single().Values, Is.EqualTo(["The Hobbit"]));
            Assert.That(plan.Root.LanguagePreference, Is.EqualTo("en"));
            Assert.That(plan.Page, Is.EqualTo(3));
            Assert.That(plan.MaxResults, Is.EqualTo(20));
            Assert.That(plan.Advanced, Is.False);
        }
    }

    [TestCase("other", "book")]
    [TestCase("books", "author")]
    public void Plan_EmptyNode_WhenFlatHierarchyDoesNotMatch(string contentType, string level)
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = new ContentHierarchyItemCriteria
            {
                ContentType = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = contentType }]
                },
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = level }]
                },
                Name = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = "Dune" }]
                }
            },
            MaxResults = 37
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(plan.Root.State, Is.EqualTo(PlanNodeState.Empty));
            Assert.That(plan.Root.Reason, Does.Contain("content hierarchy criteria"));
            Assert.That(plan.MaxResults, Is.EqualTo(37));
        }
    }

    [TestCase(CriteriaValueOperator.Equals, "books", false, "Ready")]
    [TestCase(CriteriaValueOperator.Equals, "movies", false, "Empty")]
    [TestCase(CriteriaValueOperator.NotEquals, "movies", false, "Ready")]
    [TestCase(CriteriaValueOperator.NotEquals, "books", false, "Empty")]
    [TestCase(CriteriaValueOperator.In, "books", false, "Ready")]
    [TestCase(CriteriaValueOperator.ContainsString, "ook", false, "Ready")]
    [TestCase(CriteriaValueOperator.StartsWithString, "bo", false, "Ready")]
    [TestCase(CriteriaValueOperator.EndsWithString, "ks", false, "Ready")]
    [TestCase(CriteriaValueOperator.IsNull, "", false, "Empty")]
    [TestCase(CriteriaValueOperator.IsNotNull, "", false, "Ready")]
    [TestCase(CriteriaValueOperator.Equals, "books", true, "Empty")]
    public void Plan_State_WhenContentTypeConstantOperatorIsApplied(
        CriteriaValueOperator operation,
        string value,
        bool negate,
        string expected)
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = new ContentHierarchyItemCriteria
            {
                ContentType = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Operator = operation, Value = value, Negate = negate }]
                },
                Name = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = "Dune" }]
                }
            },
            MaxResults = 20
        });

        // Assert
        Assert.That(plan.Root.State.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void Plan_EmptyNode_WhenLevelAndCriteriaContradictEachOther()
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = new ContentHierarchyItemCriteria
            {
                Level = new CriteriaSet<PropertyCriterion<string>>
                {
                    And =
                    [
                        new PropertyCriterion<string> { Value = "book" },
                        new PropertyCriterion<string> { Value = "author" }
                    ]
                },
                Name = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = "Dune" }]
                }
            },
            MaxResults = 20
        });

        // Assert
        Assert.That(plan.Root.State, Is.EqualTo(PlanNodeState.Empty));
    }

    [TestCase(0)]
    [TestCase(-4)]
    public void Plan_EmptyNode_WhenMaximumResultsIsNotPositive(int maximumResults)
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = new ContentHierarchyItemCriteria
            {
                Name = new CriteriaSet<PropertyCriterion<string>>
                {
                    And = [new PropertyCriterion<string> { Value = "Dune" }]
                }
            },
            MaxResults = maximumResults
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.State, Is.EqualTo(PlanNodeState.Empty));
            Assert.That(plan.MaxResults, Is.EqualTo(maximumResults));
        }
    }

    [Test]
    public void Plan_FilterOverFetch_WhenFullCriteriaComposeNameLanguageAndDate()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            ContentType = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "books" }]
            },
            Level = new CriteriaSet<PropertyCriterion<string>>
            {
                And = [new PropertyCriterion<string> { Value = "book" }]
            },
            Name = new CriteriaSet<PropertyCriterion<string>>
            {
                And =
                [
                    new PropertyCriterion<string>
                    {
                        Operator = CriteriaValueOperator.ContainsString,
                        Value = "Dune"
                    }
                ]
            },
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
                    {
                        Language = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "en" }]
                        },
                        PublishDate = new CriteriaSet<PropertyCriterion<TemporalValue>>
                        {
                            And =
                            [
                                new PropertyCriterion<TemporalValue>
                                {
                                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                    Value = new TemporalValue
                                    {
                                        Value = new DateTime(1965, 1, 1),
                                        Precision = TemporalPrecision.Year
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query,
            MaxResults = 41
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(fetch.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(
                fetch.SourceTerms.Select(term => term.Field),
                Is.EquivalentTo(["title", "language", "first_publish_year"]));
            Assert.That(plan.Root.Filters.Select(filter => filter.Field), Does.Contain(QueryField.Title));
            Assert.That(plan.MaxResults, Is.EqualTo(41));
            Assert.That(plan.Advanced, Is.True);
        }
    }

    [Test]
    public void Plan_OneFetch_WhenAuthorNameAndSubjectAreRequested()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And = [new PropertyCriterion<string> { Value = "science fiction" }]
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
                                        Kind = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "author" }]
                                        },
                                        Type = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "person" }]
                                        },
                                        Subject = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And =
                                            [
                                                new PropertyCriterion<string>
                                                {
                                                    Operator = CriteriaValueOperator.ContainsString,
                                                    Value = "Le Guin"
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
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(fetch.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(fetch.SourceTerms.Select(term => term.Field), Is.EquivalentTo(["author", "subject_key"]));
            Assert.That(plan.Root.Children, Has.Count.EqualTo(1));
            Assert.That(plan.Root.Filters.Select(filter => filter.Field), Does.Contain(QueryField.AuthorName));
        }
    }

    [Test]
    public void Plan_AuthorAndPersonFacetAlternatives_WhenRelationshipTypeIsPerson()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And =
                                            [
                                                new PropertyCriterion<string>
                                                {
                                                    Operator = CriteriaValueOperator.ContainsString,
                                                    Value = "Le Guin"
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
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var alternatives = plan.Root.FilterGroups.Single().Alternatives;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alternatives, Has.Count.EqualTo(2));
            Assert.That(
                alternatives.Single(alternative => alternative.AlignAuthors).Filters.Single().Field,
                Is.EqualTo(QueryField.AuthorName));
            Assert.That(
                alternatives.Single(alternative => alternative.AlignSubjectFacet == QueryField.Person)
                    .Filters.Single().Field,
                Is.EqualTo(QueryField.Person));
        }
    }

    [TestCase("person", "Person")]
    [TestCase("place", "Place")]
    [TestCase("time", "Time")]
    public void Plan_AlignedFacetFilter_WhenRelationshipHasSeveralSubjectPredicates(string kind, string expectedField)
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And = [new PropertyCriterion<string> { Value = kind }]
                                        },
                                        Subject = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And =
                                            [
                                                new PropertyCriterion<string>
                                                {
                                                    Operator = CriteriaValueOperator.ContainsString,
                                                    Value = "first"
                                                },
                                                new PropertyCriterion<string>
                                                {
                                                    Operator = CriteriaValueOperator.EndsWithString,
                                                    Value = "second"
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
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query,
            MaxResults = 20
        });

        // Assert
        Assert.That(
            plan.Root.FilterGroups.Single().Alternatives.Single().AlignSubjectFacet?.ToString(),
            Is.EqualTo(expectedField));
    }

    [Test]
    public void Plan_AuthorKeyFetch_WhenNativeAuthorIdentityIsRequested()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And = [new PropertyCriterion<string> { Value = "author" }]
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
                                                                new PropertyCriterion<string> { Value = "openlibrary" }
                                                            ]
                                                        },
                                                        Value = new CriteriaSet<PropertyCriterion<string>>
                                                        {
                                                            And = [new PropertyCriterion<string> { Value = "OL23919A" }]
                                                        }
                                                    }
                                                ]
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
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(fetch.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(fetch.SourceTerms.Single().Field, Is.EqualTo("author_key"));
            Assert.That(fetch.SourceTerms.Single().Values, Is.EqualTo(["OL23919A"]));
        }
    }

    [Test]
    public void Plan_IdentifierOrGroup_WhenSeveralIdentifierTypesAreAlternatives()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And = [new PropertyCriterion<string> { Value = "isbn" }]
                                        },
                                        Value = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "978-0-14-311159-7" }]
                                        }
                                    },
                                    new ExternalIdCriteria
                                    {
                                        Source = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "oclc" }]
                                        },
                                        Value = new CriteriaSet<PropertyCriterion<string>>
                                        {
                                            And = [new PropertyCriterion<string> { Value = "12345" }]
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
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(fetch.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(fetch.SourceGroups.Single().Alternatives, Has.Count.EqualTo(2));
            Assert.That(
                fetch.SourceGroups.Single()
                    .Alternatives.SelectMany(alternative => alternative.Terms)
                    .Select(term => term.Field),
                Is.EquivalentTo(["isbn", "oclc"]));
        }
    }

    [TestCase(CriteriaValueOperator.StartsWithString, "Trailing")]
    [TestCase(CriteriaValueOperator.EndsWithString, "Leading")]
    public void Plan_WildcardFetchAndExactFilter_WhenTitleIsBounded(CriteriaValueOperator operation, string expected)
    {
        // Arrange
        var subject = new QueryPlanner();
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
                            And = [new PropertyCriterion<string> { Operator = operation, Value = "Harry" }]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fetch.SourceTerms.Single().WildcardPlacement.ToString(), Is.EqualTo(expected));
            Assert.That(plan.Root.Filters.Single().Operator, Is.EqualTo(operation));
        }
    }

    [Test]
    public void Plan_RequiredFields_WhenOriginalTitleAndPagesNeedVerification()
    {
        // Arrange
        var subject = new QueryPlanner();
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
                            And = [new PropertyCriterion<string> { Value = "Dune" }]
                        },
                        OriginalTitle = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "Dune World" }]
                        },
                        CustomMetadata = new CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>>
                        {
                            And =
                            [
                                new DictionaryCriteria<PropertyCriterion<string>>
                                {
                                    Items = new Dictionary<string, PropertyCriterion<string>>
                                    {
                                        ["pages"] = new PropertyCriterion<string> { Value = "412" }
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        Assert.That(plan.Root.Children.Single().RequiredFields, Is.EqualTo(["number_of_pages_median"]));
    }

    [Test]
    public void Plan_AllIdentifierValues_WhenGroupContainsMoreThanTen()
    {
        // Arrange
        var subject = new QueryPlanner();
        var query = new ContentHierarchyItemCriteria
        {
            Metadata = new CriteriaSet<MetadataCriteria>
            {
                And =
                [
                    new MetadataCriteria
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
                                            And = [new PropertyCriterion<string> { Value = "oclc" }]
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
                                                        "01",
                                                        "02",
                                                        "03",
                                                        "04",
                                                        "05",
                                                        "06",
                                                        "07",
                                                        "08",
                                                        "09",
                                                        "10",
                                                        "11",
                                                        "12"
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
            }
        };

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        var fetch = plan.Root.Children.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(fetch.Kind, Is.EqualTo(PlanNodeKind.Fetch));
            Assert.That(fetch.SourceTerms.Single().Values, Has.Count.EqualTo(12));
        }
    }

    [Test]
    public void Plan_EmptyNode_WhenOnlyNegativeCriterionExists()
    {
        // Arrange
        var subject = new QueryPlanner();
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
                                    Operator = CriteriaValueOperator.NotEquals,
                                    Value = "Dune"
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = query
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Root.State, Is.EqualTo(PlanNodeState.Empty));
            Assert.That(
                plan.Warnings,
                Does.Contain("Open Library requires at least one positive search condition."));
        }
    }

    [Test]
    public void Plan_PredicatesAndDisposition_WhenMetadataTitleCriterionIsRequested()
    {
        // Arrange
        var subject = new QueryPlanner();

        // Act
        var plan = subject.Plan(new QueryRequest
        {
            Intent = QueryIntent.AdvancedSearch,
            Hierarchy = new BookHierarchy
            {
                ContentTypeSlug = "books",
                BookLevelSlug = "book"
            },
            Criteria = new ContentHierarchyItemCriteria
            {
                Metadata = new CriteriaSet<MetadataCriteria>
                {
                    And =
                    [
                        new MetadataCriteria
                        {
                            Title = new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [new PropertyCriterion<string> { Value = "Dune" }]
                            }
                        }
                    ]
                }
            },
            MaxResults = 10
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.CriterionDispositions, Does.Contain("metadata:provider-plan"));
            Assert.That(plan.Root.State, Is.EqualTo(PlanNodeState.Ready));
            Assert.That(plan.Root.Kind, Is.EqualTo(PlanNodeKind.Filter));
            Assert.That(plan.Root.Predicates, Is.EqualTo(["Title:Equals"]));
            Assert.That(plan.Root.Children.Single().SourceFields, Is.EqualTo(["title"]));
        }
    }
}
