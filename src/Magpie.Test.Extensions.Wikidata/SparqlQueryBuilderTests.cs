using System;
using System.Linq;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using NUnit.Framework;

using SubjectFilter = Magpie.Extensions.Wikidata.QueryEngine.SubjectFilter;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class SparqlQueryBuilderTests
{
    [Test]
    public void BuildSearch_ContainsBothBoundsAndDirectTypeGate_WhenMovieTitle()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.ContainsString,
                    Values = ["Matrix"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "the matrix"
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;
        var labelService = result.Substring(result.LastIndexOf("SERVICE wikibase:label", StringComparison.Ordinal));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Split("mwapi:srlimit \"51\"", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(result.Split("wikibase:limit 51 .", StringSplitOptions.None), Has.Length.EqualTo(2));
            Assert.That(result, Does.Contain("VALUES ?typeValue { wd:Q11424 wd:Q24862 wd:Q506240 }"));
            Assert.That(result, Does.Contain("?item wdt:P31 ?typeValue."));
            Assert.That(
                result,
                Does.Contain(
                    "mwapi:srsearch \"inlabel:\\\"the matrix\\\" " +
                    "haswbstatement:P31=Q11424|P31=Q24862|P31=Q506240\";"));
            Assert.That(result, Does.Contain("GROUP BY ?item"));
            Assert.That(result, Does.Contain("BIND(1 AS ?titleCandidateEligible)"));
            Assert.That(
                result,
                Does.Contain("FILTER(?ordinalValue >= 50 || BOUND(?titleCandidateEligible))"));
            Assert.That(
                result,
                Does.Contain("(MAX(?titleWindowExceededValue) AS ?titleWindowExceeded)"));
            Assert.That(
                result,
                Does.Contain(
                    "BIND(IF(?ordinalValue >= 50, \"overflow\", STR(?item)) AS ?titleResultGroup)"));
            Assert.That(
                result,
                Does.Contain("BIND(IF(?ordinalValue >= 50, 1, 0) AS ?titleWindowExceededValue)"));
            Assert.That(result, Does.Contain("GROUP BY ?titleResultGroup"));
            Assert.That(result, Does.Contain("BIND(?candidateItem AS ?item)"));
            Assert.That(result, Does.Contain("LIMIT 51"));
            Assert.That(result, Does.Not.Contain("P279*"));
            Assert.That(result, Does.Not.Contain(" OR "));
            Assert.That(result, Does.Not.Contain("(inlabel:"));
            Assert.That(result, Does.Not.Contain("FILTER(?ordinalValue < 50)"));
            Assert.That(labelService, Does.Not.Contain("?genreItem"));
        }
    }

    [Test]
    public void BuildSearch_UsesSeparateCirrusServiceBranches_WhenTitleIn()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.In,
                    Values = ["The Matrix", "Matrix Reloaded"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;
        var serviceCount = result.Split("SERVICE wikibase:mwapi", StringSplitOptions.None).Length - 1;
        var searchLimitCount = result.Split("mwapi:srlimit \"51\"", StringSplitOptions.None).Length - 1;
        var serviceLimitCount = result.Split("wikibase:limit 51 .", StringSplitOptions.None).Length - 1;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(serviceCount, Is.EqualTo(2));
            Assert.That(searchLimitCount, Is.EqualTo(2));
            Assert.That(serviceLimitCount, Is.EqualTo(2));
            Assert.That(result, Does.Contain("} UNION {"));
            Assert.That(
                result,
                Does.Contain(
                    "mwapi:srsearch \"inlabel:\\\"The Matrix\\\" " +
                    "haswbstatement:P31=Q11424|P31=Q24862|P31=Q506240\";"));
            Assert.That(
                result,
                Does.Contain(
                    "mwapi:srsearch \"inlabel:\\\"Matrix Reloaded\\\" " +
                    "haswbstatement:P31=Q11424|P31=Q24862|P31=Q506240\";"));
            Assert.That(result, Does.Contain("(MIN(?ordinalValue) AS ?searchOrdinalValue)"));
            Assert.That(
                result,
                Does.Contain("(MAX(?titleWindowExceededValue) AS ?titleWindowExceeded)"));
            Assert.That(
                result,
                Does.Contain("ORDER BY ASC(MIN(?ordinalValue)) ASC(?titleResultGroup)"));
            Assert.That(result, Does.Contain("LIMIT 102"));
            Assert.That(
                result,
                Does.Contain("FILTER(LCASE(STR(?titleFilter0)) IN (\"the matrix\", \"matrix reloaded\"))"));
            Assert.That(result, Does.Not.Contain(" OR "));
        }
    }

    [Test]
    public void BuildSearch_UsesAllAlternatives_WhenTitleEqualsWithExtraValues()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Values = ["The Matrix", "Matrix Reloaded"]
                }
            ],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;
        var serviceCount = result.Split("SERVICE wikibase:mwapi", StringSplitOptions.None).Length - 1;
        var escapedTheMatrix = "inlabel:" + "\\\"" + "The Matrix" + "\\\"";
        var escapedReloaded = "inlabel:" + "\\\"" + "Matrix Reloaded" + "\\\"";

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(serviceCount, Is.EqualTo(2));
            Assert.That(result, Does.Contain(escapedTheMatrix));
            Assert.That(result, Does.Contain(escapedReloaded));
            Assert.That(
                result,
                Does.Contain("FILTER(LCASE(STR(?titleFilter0)) IN (\"the matrix\", \"matrix reloaded\"))"));
        }
    }

    [Test]
    public void BuildSearch_ContinuesEachCirrusSource_WhenTitleWindowOffset()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters = [],
            Warnings = [],
            Page = 1,
            UsesLocalPaging = true,
            OrdinaryTerm = "matrix"
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en", 50).QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("mwapi:srlimit \"51\""));
            Assert.That(result, Does.Contain("mwapi:sroffset \"50\""));
            Assert.That(result, Does.Contain("OFFSET 0"));
        }
    }

    [Test]
    public void BuildSearch_UsesTypeGatedCirrusSeeds_WhenTitleStartsWith()
    {
        // Arrange
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
        var result = SparqlQueryBuilder.BuildSearch(plan, "fr-CA").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Split("wikibase:api \"Search\";", StringSplitOptions.None), Has.Length.EqualTo(3));
            Assert.That(
                result,
                Does.Contain(
                    "mwapi:srsearch \"inlabel:\\\"The Mat\\\" " +
                    "haswbstatement:P31=Q11424|P31=Q24862|P31=Q506240\";"));
            Assert.That(
                result,
                Does.Contain(
                    "mwapi:srsearch \"inlabel:\\\"El Mat\\\" " +
                    "haswbstatement:P31=Q11424|P31=Q24862|P31=Q506240\";"));
            Assert.That(result.Split("mwapi:srlimit \"51\"", StringSplitOptions.None), Has.Length.EqualTo(3));
            Assert.That(result.Split("wikibase:limit 51 .", StringSplitOptions.None), Has.Length.EqualTo(3));
            Assert.That(result, Does.Contain("?item wikibase:apiOutputItem mwapi:title."));
            Assert.That(result, Does.Contain("?ordinalValue wikibase:apiOrdinal true."));
            Assert.That(result, Does.Contain("} UNION {"));
            Assert.That(result, Does.Contain("LIMIT 102"));
            Assert.That(
                result,
                Does.Contain(
                    "FILTER((STRSTARTS(LCASE(STR(?titleFilter0)), \"the mat\") || " +
                    "STRSTARTS(LCASE(STR(?titleFilter0)), \"el mat\")))"));
        }
    }

    [Test]
    public void BuildSearch_UsesShowTypesAndStartDate_WhenShowStructured()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            ExternalIds =
                            [
                                new PropertyValue
                                {
                                    PropertyId = Constants.TmdbShowPropertyId,
                                    Value = "1399"
                                }
                            ]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 2
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "fr-CA").QueryText;
        var projection = SparqlQueryBuilder.BuildCandidateProjection(EntityKind.Show, ["Q1"], "fr-CA").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("wd:Q5398426 wd:Q581714"));
            Assert.That(result, Does.Contain("wdt:P4983 \"1399\""));
            Assert.That(projection, Does.Contain("?item p:P580 ?dateStatementProjection."));
            Assert.That(
                projection,
                Does.Contain(
                    "BIND(CONCAT(STR(?dateValueProjection), \"|\", STR(?datePrecisionProjection), \"|\", " +
                    "STR(?dateCalendarProjection), \"|\", STR(?dateTimezoneProjection)) " +
                    "AS ?publishDateProjection)"));
            Assert.That(projection, Does.Contain("?item wdt:P3383 ?posterFilenameValue."));
            Assert.That(projection, Does.Contain("?item wdt:P18 ?imageFilenameValue."));
            Assert.That(projection, Does.Not.Contain("?item wdt:P18 ?posterFilenameValue."));
            Assert.That(result, Does.Contain("OFFSET 20"));
            Assert.That(projection, Does.Contain("FILTER(LANG(?genreLabelValue) IN (\"fr\", \"en\"))"));
        }
    }

    [Test]
    public void BuildSearch_UsesBoundedLocalCandidateWindow_WhenStructuredResidual()
    {
        // Arrange
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
                        Values = ["matrix"]
                    }
                }
            ],
            Warnings = [],
            Page = 3,
            UsesLocalPaging = true
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en", 50).QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("LIMIT 51"));
            Assert.That(result, Does.Contain("OFFSET 50"));
            Assert.That(result, Does.Not.Contain("OFFSET 40"));
        }
    }

    [Test]
    public void BuildExternalIdentity_SelectsDistinctAndEscapesLiteral_WhenValue()
    {
        // Arrange

        // Act
        var result = SparqlQueryBuilder.BuildExternalIdentity(
            EntityKind.Movie,
            Constants.ImdbPropertyId,
            "tt\"12\\3").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("SELECT DISTINCT ?item"));
            Assert.That(result, Does.Contain("LIMIT 2"));
            Assert.That(result, Does.Contain("\"tt\\\"12\\\\3\""));
        }
    }

    [Test]
    public void BuildRelationshipSeed_UsesLightweightBoundedQuery_WhenPositiveSubjectQids()
    {
        // Arrange
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.Relationship,
            Alternatives =
            [
                new SubjectFilter
                {
                    PropertyIds = [Constants.CastMemberPropertyId],
                    Qids = ["Q43416"]
                }
            ]
        };

        // Act
        var result = SparqlQueryBuilder
            .BuildRelationshipSeed(EntityKind.Movie, filter, 50).QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.StartWith("PREFIX bd:"));
            Assert.That(result, Does.Contain("SELECT DISTINCT ?item"));
            Assert.That(result, Does.Contain("VALUES ?relationshipProperty0_0 { wdt:P161 }"));
            Assert.That(result, Does.Contain("VALUES ?subject0_0 { wd:Q43416 }"));
            Assert.That(result, Does.Contain("VALUES ?typeValue { wd:Q11424 wd:Q24862 wd:Q506240 }"));
            Assert.That(result, Does.Contain("LIMIT 51"));
            Assert.That(result, Does.Contain("OFFSET 50"));
            Assert.That(result, Does.Not.Contain("wikibase:label"));
            Assert.That(result, Does.Not.Contain("GROUP_CONCAT"));
        }
    }

    [Test]
    public void BuildExternalIdentity_Throws_WhenPropertyIdDigitsAreNotAscii()
    {
        // Arrange

        // Act
        var action = () => SparqlQueryBuilder.BuildExternalIdentity(
            EntityKind.Movie,
            "P\u0663\u0664\u0665",
            "tt0133093");

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [Test]
    public void BuildHierarchy_UsesBestRankAndAggregatesBeforeSentinel_WhenEpisode()
    {
        // Arrange

        // Act
        var result = SparqlQueryBuilder.BuildHierarchy(
            EntityKind.Episode,
            "Q100",
            "Q200",
            "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("a wikibase:BestRank; ps:P4908 wd:Q100"));
            Assert.That(result, Does.Contain("wdt:P179 wd:Q200"));
            Assert.That(result, Does.Contain("GROUP BY ?child"));
            Assert.That(result, Does.Contain("HAVING(COUNT(DISTINCT ?ordinalValue) = 1)"));
            Assert.That(result, Does.Contain("LIMIT 2501"));
            Assert.That(result, Does.Contain("?child p:P577 ?dateStatementProjection."));
            Assert.That(result, Does.Contain("psv:P577 ?dateNodeProjection"));
            Assert.That(result, Does.Contain("wikibase:timePrecision ?datePrecisionProjection"));
        }
    }

    [Test]
    public void BuildCoordinateIdentity_ConstrainsSeasonStatementAndShowSeries_WhenEpisode()
    {
        // Arrange

        // Act
        var result = SparqlQueryBuilder.BuildCoordinateIdentity(EntityKind.Episode, "Q100", 2, "Q200").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("wdt:P179 wd:Q200"));
            Assert.That(result, Does.Contain("a wikibase:BestRank; ps:P4908 wd:Q100"));
            Assert.That(result, Does.Contain("pq:P1545 \"2\""));
            Assert.That(result, Does.Contain("LIMIT 2"));
        }
    }

    [TestCase("Q0x")]
    [TestCase("Q\u0661\u0668\u0666\u0660")]
    public void BuildCoordinateIdentity_Throws_WhenQidIsInvalid(string qid)
    {
        // Arrange

        // Act
        var action = () => SparqlQueryBuilder.BuildCoordinateIdentity(EntityKind.Season, qid, 1, null);

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [Test]
    public void BuildSearch_RenderAsAnd_WhenRelationshipExternalIdGroups()
    {
        // Arrange
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
                            PropertyIds = ["P161", "P725"],
                            ExternalIdGroups =
                            [
                                new ExternalIdGroup
                                {
                                    ExternalIds =
                                    [
                                        new PropertyValue { PropertyId = "P345", Value = "nm0000206" }
                                    ]
                                },
                                new ExternalIdGroup
                                {
                                    ExternalIds =
                                    [
                                        new PropertyValue { PropertyId = "P4985", Value = "6384" }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("VALUES ?relationshipProperty0_0 { wdt:P161 wdt:P725 }"));
            Assert.That(result, Does.Contain("            ?subject0_0 wdt:P345 \"nm0000206\"."));
            Assert.That(result, Does.Contain("            ?subject0_0 wdt:P4985 \"6384\"."));
            Assert.That(
                result,
                Does.Not.Contain("?subject0_0 wdt:P345 \"nm0000206\".\n          } UNION {"));
        }
    }

    [Test]
    public void BuildSearch_UsesBoundedInlabelService_WhenRelationshipSubjectContainsName()
    {
        // Arrange
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
                            Names = ["Laurence Fishburne"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;
        var serviceCount = result.Split("SERVICE wikibase:mwapi", StringSplitOptions.None).Length - 1;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(serviceCount, Is.EqualTo(1));
            Assert.That(result, Does.Contain("mwapi:srsearch \"inlabel:\\\"Laurence Fishburne\\\"\";"));
            Assert.That(result, Does.Contain("mwapi:srlimit \"5\";"));
            Assert.That(result, Does.Contain("wikibase:limit 5 ."));
            Assert.That(result, Does.Contain("?subject0_0 wikibase:apiOutputItem mwapi:title."));
        }
    }

    [Test]
    public void BuildSearch_UsesSharedPropertyCatalogAndNamedItemSelectors_WhenContentRating()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ContentRating,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            Qids = ["Q1860"],
                            Names = ["PG-13"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result,
                Does.Contain(
                    "VALUES ?collectionProperty0_0 { wdt:P1657 wdt:P2629 wdt:P1981 wdt:P2758 " +
                    "wdt:P2756 wdt:P3428 wdt:P3818 }"));
            Assert.That(result, Does.Contain("?item ?collectionProperty0_0 ?value0_0."));
            Assert.That(result, Does.Contain("VALUES ?value0_0 { wd:Q1860 }"));
            Assert.That(result, Does.Contain("mwapi:srsearch \"inlabel:\\\"PG-13\\\"\";"));
            Assert.That(result, Does.Contain("?value0_0 wikibase:apiOutputItem mwapi:title."));
            Assert.That(result, Does.Not.Contain("?value0_0 rdfs:label ?name0_0."));
        }
    }

    [Test]
    public void BuildSearch_UseDirectPropertyAlternatives_WhenAwardAndNegatedImage()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Award,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.AwardReceivedPropertyId],
                            Names = ["Academy Award"]
                        }
                    ]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Image,
                    Negate = true,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.PosterPropertyId]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("VALUES ?collectionProperty0_0 { wdt:P166 }"));
            Assert.That(result, Does.Contain("mwapi:srsearch \"inlabel:\\\"Academy Award\\\"\";"));
            Assert.That(result, Does.Contain("?value0_0 wikibase:apiOutputItem mwapi:title."));
            Assert.That(result, Does.Not.Contain("?value0_0 rdfs:label ?name0_0."));
            Assert.That(result, Does.Contain("FILTER NOT EXISTS {"));
            Assert.That(result, Does.Contain("VALUES ?collectionProperty1_0 { wdt:P3383 }"));
            Assert.That(result, Does.Contain("?item ?collectionProperty1_0 ?value1_0."));
        }
    }

    [Test]
    public void BuildSearch_UseMwapiAndDirectValueSelector_WhenGenreNameAndQid()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Genre,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            Qids = ["Q130232"],
                            Names = ["sci-fi"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item wdt:P136 ?value0_0."));
            Assert.That(result, Does.Contain("VALUES ?value0_0 { wd:Q130232 }"));
            Assert.That(result, Does.Contain("mwapi:srsearch \"inlabel:\\\"sci-fi\\\"\";"));
            Assert.That(result, Does.Contain("?value0_0 wikibase:apiOutputItem mwapi:title."));
            Assert.That(result, Does.Not.Contain("?value0_0 rdfs:label ?name0_0."));
        }
    }

    [Test]
    public void BuildSearch_UsesCaseInsensitiveSchemeSelector_WhenReference()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Reference,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.OfficialWebsitePropertyId],
                            UriSchemes = ["HTTP", "https"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("VALUES ?collectionProperty0_0 { wdt:P856 }"));
            Assert.That(result, Does.Contain("?item ?collectionProperty0_0 ?value0_0."));
            Assert.That(
                result,
                Does.Contain(
                    "?item ?collectionProperty0_0 ?value0_0." + Environment.NewLine + "          FILTER("));
            Assert.That(
                result,
                Does.Contain(
                    "FILTER(STRSTARTS(LCASE(STR(?value0_0)), \"http:\") || " +
                    "STRSTARTS(LCASE(STR(?value0_0)), \"https:\"))"));
        }
    }

    [Test]
    public void BuildSearch_FiltersValuesBeforeRemotePaging_WhenReferenceExactUrls()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Reference,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = [Constants.OfficialWebsitePropertyId],
                            UriValues =
                            [
                                "https://example.test/First",
                                "http://example.test/second"
                            ]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 3
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result,
                Does.Contain(
                    "FILTER(LCASE(STR(?value0_0)) IN (\"https://example.test/first\", " +
                    "\"http://example.test/second\"))"));
            Assert.That(
                result,
                Does.Contain(
                    "?item ?collectionProperty0_0 ?value0_0." + Environment.NewLine + "          FILTER("));
            Assert.That(result, Does.Contain("LIMIT 21"));
            Assert.That(result, Does.Contain("OFFSET 40"));
            Assert.That(result, Does.Not.Contain("STRSTARTS(LCASE(STR(?value0_0))"));
        }
    }

    [Test]
    public void BuildSearch_Throws_WhenCollectionPropertyIdIsInvalid()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Image,
                    Alternatives = [new SubjectFilter { PropertyIds = ["P0"] }]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var action = () => SparqlQueryBuilder.BuildSearch(plan, "en");

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [Test]
    public void BuildSearch_UsesPeriodOverlapAcrossWikibasePrecisions_WhenDayEquality()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    PropertyId = Constants.PublicationDatePropertyId,
                    Operator = CriteriaValueOperator.Equals,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Day
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item p:P577 ?dateStatement0."));
            Assert.That(result, Does.Not.Contain("?datePrecision0 >= 11"));
            Assert.That(
                result,
                Does.Contain(
                    "?dateValue0 < \"1999-04-01T00:00:00.0000000Z\"^^xsd:dateTime"));
            Assert.That(result, Does.Contain("&& !((?datePrecision0 = 9"));
            Assert.That(result, Does.Contain("?dateCalendar0 = wd:Q1985727"));
        }
    }

    [Test]
    public void BuildSearch_BindsSortVariableOnce_WhenTwoPositiveDateCriteria()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    PropertyId = Constants.PublicationDatePropertyId,
                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Day
                        }
                    ]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.PublishDate,
                    PropertyId = Constants.PublicationDatePropertyId,
                    Operator = CriteriaValueOperator.LessThan,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Day
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item p:P577 ?dateStatement0."));
            Assert.That(result, Does.Contain("?item p:P577 ?dateStatement1."));
            Assert.That(result.Split("AS ?sortDateValue").Length - 1, Is.EqualTo(1));
            Assert.That(
                result,
                Does.Contain(
                    "BIND(COALESCE(?dateValue0, ?dateValue1) AS ?sortDateValue)"));
        }
    }

    [Test]
    public void BuildSearch_InvertsTheSameDayPeriodOverlap_WhenDayEqualityIsNegated()
    {
        // Arrange
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.PublishDate,
            PropertyId = Constants.PublicationDatePropertyId,
            Operator = CriteriaValueOperator.Equals,
            Negate = true,
            TemporalValues =
            [
                new TemporalValue
                {
                    Value = new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Day
                }
            ]
        };
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            Qids = ["Q83495"]
                        }
                    ]
                },
                filter
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("FILTER NOT EXISTS {"));
            Assert.That(
                result,
                Does.Contain(
                    "?dateValue1 < \"1999-04-01T00:00:00.0000000Z\"^^xsd:dateTime"));
            Assert.That(result, Does.Contain("&& !((?datePrecision1 = 9"));
            Assert.That(result, Does.Not.Contain("?datePrecision1 >= 11"));
        }
    }

    [Test]
    public void BuildSearch_JoinWithoutFilterExists_WhenPositiveStructuredFilters()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Values = ["en", "eng"],
                    LanguageQids = ["Q1860"]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Genre,
                    Alternatives = [new SubjectFilter { Qids = ["Q130232"] }]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            ExternalIds =
                            [
                                new PropertyValue
                                {
                                    PropertyId = Constants.ImdbPropertyId,
                                    Value = "tt0133093"
                                }
                            ]
                        }
                    ]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Relationship,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            PropertyIds = ["P57"],
                            Qids = ["Q123"]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("VALUES ?languageItem0 { wd:Q1860 }"));
            Assert.That(result, Does.Contain("?languageCodeItem0 wdt:P279* ?languageCodeAncestor0."));
            Assert.That(result, Does.Contain("?languageCodeAncestor0 wdt:P218 ?languageCode0."));
            Assert.That(result, Does.Contain("IN (\"en\", \"eng\")"));
            Assert.That(result, Does.Contain("?item wdt:P136 ?value1_0."));
            Assert.That(result, Does.Contain("?item wdt:P345 \"tt0133093\"."));
            Assert.That(result, Does.Contain("?item ?relationshipProperty3_0 ?subject3_0."));
            Assert.That(result, Does.Not.Contain("FILTER EXISTS"));
        }
    }

    [TestCase(EntityKind.Movie, CriteriaValueOperator.IsNull, "FILTER NOT EXISTS")]
    [TestCase(EntityKind.Movie, CriteriaValueOperator.IsNotNull, "FILTER EXISTS")]
    [TestCase(EntityKind.Show, CriteriaValueOperator.IsNull, "FILTER NOT EXISTS")]
    [TestCase(EntityKind.Show, CriteriaValueOperator.IsNotNull, "FILTER EXISTS")]
    public void BuildSearch_UsesProjectableLanguageCode_WhenLanguagePresence(
        EntityKind entityKind,
        CriteriaValueOperator valueOperator,
        string expectedFilter)
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = entityKind,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Operator = valueOperator
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q83495"] }]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain(expectedFilter + " { ?item wdt:P364 ?languageItem0."));
            Assert.That(result, Does.Contain("?languageItem0 wdt:P279* ?languageCodeAncestor0."));
            Assert.That(result, Does.Contain("?languageCodeAncestor0 wdt:P218 ?languageCode0."));
            Assert.That(result, Does.Not.Contain("?item wdt:P364 ?languageExists."));
        }
    }

    [Test]
    public void BuildSearch_Throws_WhenLanguageQidDigitsAreNotAscii()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    LanguageQids = ["Q\u0661\u0668\u0666\u0660"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var action = () => SparqlQueryBuilder.BuildSearch(plan, "en");

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [Test]
    public void BuildSearch_KeepFilterNotExists_WhenNegatedCompositeFilters()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q83495"] }]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Genre,
                    Negate = true,
                    Alternatives = [new SubjectFilter { Qids = ["Q130232"] }]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Negate = true,
                    Alternatives =
                    [
                        new SubjectFilter
                        {
                            ExternalIds =
                            [
                                new PropertyValue
                                {
                                    PropertyId = Constants.ImdbPropertyId,
                                    Value = "tt0133093"
                                }
                            ]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Split("FILTER NOT EXISTS").Length - 1, Is.EqualTo(2));
            Assert.That(result, Does.Contain("?item wdt:P136 ?value1_0."));
            Assert.That(result, Does.Contain("?item wdt:P345 \"tt0133093\"."));
        }
    }

    [Test]
    public void BuildSearch_DoesNotRequireOuterLabel_WhenNegatedTitle()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q83495"] }]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = true,
                    Values = ["The Matrix"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;
        var titleLine = result.Split('\n').Single(line => line.Contains("?item rdfs:label ?titleFilter1."))
            .TrimEnd('\r');

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("FILTER NOT EXISTS {"));
            Assert.That(titleLine, Is.EqualTo("        ?item rdfs:label ?titleFilter1."));
        }
    }

    [Test]
    public void BuildSearch_ConstrainsMonolingualTitle_WhenOriginalTitleEquals()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.OriginalTitle,
                    Operator = CriteriaValueOperator.Equals,
                    Values = ["The Matrix"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item wdt:P1476 ?originalTitle0."));
            Assert.That(result, Does.Contain("FILTER(LCASE(STR(?originalTitle0)) = \"the matrix\")"));
        }
    }

    [Test]
    public void BuildSearch_ConstrainsTaggedDescription_WhenOverviewContains()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Overview,
                    Operator = CriteriaValueOperator.ContainsString,
                    Values = ["heist"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "fr-CA").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item schema:description ?overview0."));
            Assert.That(result, Does.Contain("FILTER(LCASE(LANG(?overview0)) IN (\"fr\", \"en\"))"));
            Assert.That(result, Does.Contain("FILTER(CONTAINS(LCASE(STR(?overview0)), \"heist\"))"));
        }
    }

    [Test]
    public void BuildSearch_UsesStrStarts_WhenOverviewStartsWith()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Overview,
                    Operator = CriteriaValueOperator.StartsWithString,
                    Values = ["A heist"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        Assert.That(result, Does.Contain("FILTER(STRSTARTS(LCASE(STR(?overview0)), \"a heist\"))"));
    }

    [Test]
    public void BuildSearch_ComparesAmountInStatementUnits_WhenDurationBound()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Duration,
                    Operator = CriteriaValueOperator.LessThanOrEqual,
                    DurationValues = [TimeSpan.FromHours(2)]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item p:P2047 ?durationStatement0."));
            Assert.That(result, Does.Contain("?durationStatement0 a wikibase:BestRank; psv:P2047 ?durationNode0."));
            Assert.That(
                result,
                Does.Contain(
                    "?durationNode0 wikibase:quantityAmount ?durationAmount0; wikibase:quantityUnit ?durationUnit0."));
            Assert.That(result, Does.Contain("(?durationUnit0 = wd:Q11574 && ?durationAmount0 <= 7200)"));
            Assert.That(result, Does.Contain("(?durationUnit0 = wd:Q7727 && ?durationAmount0 <= 120)"));
            Assert.That(result, Does.Contain("(?durationUnit0 = wd:Q25235 && ?durationAmount0 <= 2)"));
        }
    }

    [Test]
    public void BuildSearch_WrapsPatternInNotExists_WhenNegatedDurationEquality()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ExternalId,
                    Alternatives = [new SubjectFilter { Qids = ["Q83495"] }]
                },
                new QueryFilter
                {
                    Kind = QueryFilterKind.Duration,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = true,
                    DurationValues = [TimeSpan.FromMinutes(136)]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("FILTER NOT EXISTS {"));
            Assert.That(result, Does.Contain("(?durationUnit1 = wd:Q7727 && ?durationAmount1 = 136)"));
        }
    }

    [TestCase(false, "FILTER EXISTS { ?item wdt:P582 ?productionStatus0. }")]
    [TestCase(true, "FILTER NOT EXISTS { ?item wdt:P582 ?productionStatus0. }")]
    public void BuildSearch_TogglesSeriesEndExistence_WhenProductionStatusEnded(bool negate, string expected)
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.ProductionStatus,
                    PropertyId = Constants.SeriesEndPropertyId,
                    Operator = CriteriaValueOperator.Equals,
                    Negate = negate,
                    Values = ["Ended"]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain(expected));
            Assert.That(result, Does.Not.Contain("wdt:P576 ?productionStatus0"));
        }
    }

    [TestCase(
        "Established",
        Constants.InceptionPropertyId,
        Constants.SeriesStartPropertyId)]
    [TestCase(
        "Dissolved",
        Constants.DissolutionPropertyId,
        Constants.SeriesEndPropertyId)]
    public void BuildSearch_UsesPlannedProperty_WhenStudioTemporalField(
        string kindName,
        string propertyId,
        string forbiddenPropertyId)
    {
        // Arrange
        var kind = Enum.Parse<QueryFilterKind>(kindName);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Studio,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = kind,
                    PropertyId = propertyId,
                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var query = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Does.Contain("?item p:" + propertyId + " ?dateStatement0."));
            Assert.That(query, Does.Contain("psv:" + propertyId + " ?dateNode0"));
            Assert.That(query, Does.Not.Contain("?item p:" + forbiddenPropertyId + " ?dateStatement0."));
        }
    }

    [TestCase(
        "Established",
        Constants.SeriesStartPropertyId,
        Constants.InceptionPropertyId)]
    [TestCase(
        "Dissolved",
        Constants.SeriesEndPropertyId,
        Constants.DissolutionPropertyId)]
    public void BuildSearch_UsesPlannedProperty_WhenShowTemporalFieldFilter(
        string kindName,
        string propertyId,
        string forbiddenPropertyId)
    {
        // Arrange
        var kind = Enum.Parse<QueryFilterKind>(kindName);
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = kind,
                    PropertyId = propertyId,
                    Operator = CriteriaValueOperator.GreaterThanOrEqual,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("?item p:" + propertyId + " ?dateStatement0."));
            Assert.That(result, Does.Contain("psv:" + propertyId + " ?dateNode0"));
            Assert.That(result, Does.Not.Contain("?item p:" + forbiddenPropertyId + " ?dateStatement0."));
            Assert.That(result, Does.Not.Contain("psv:" + forbiddenPropertyId + " ?dateNode0"));
            Assert.That(result, Does.Not.Contain("p:P571"));
            Assert.That(result, Does.Not.Contain("p:P576"));
            Assert.That(
                result,
                Does.Contain(
                    "?dateValue0 >= \"1980-01-01T00:00:00.0000000Z\"^^xsd:dateTime"));
        }
    }

    [Test]
    public void BuildSearch_Throws_WhenDateFilterLacksPlannedProperty()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Established,
                    TemporalValues =
                    [
                        new TemporalValue
                        {
                            Value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                            Precision = TemporalPrecision.Year
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var action = () => SparqlQueryBuilder.BuildSearch(plan, "en");

        // Assert
        Assert.That(action, Throws.InvalidOperationException);
    }

    [Test]
    public void BuildCandidateProjection_ProjectsCandidateFieldsWithPreciseDates_WhenMovieKind()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder
            .BuildCandidateProjection(plan.EntityKind, ["Q83495"], "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result,
                Does.Contain(
                    "(GROUP_CONCAT(DISTINCT ?genreLabelValue; SEPARATOR=\"\\u001F\") AS ?genres)"));
            Assert.That(result, Does.Contain("?item p:P577 ?dateStatementProjection."));
            Assert.That(result, Does.Contain("?dateNodeProjection wikibase:timeValue ?dateValueProjection;"));
            Assert.That(result, Does.Contain("?durationStatementProjection"));
            Assert.That(result, Does.Contain("?item wdt:P136 ?genreItem."));
            Assert.That(result, Does.Contain("?genreItem rdfs:label ?genreLabelValue."));
            Assert.That(result, Does.Contain("FILTER(LANG(?genreLabelValue) IN (\"en\"))"));
            Assert.That(result, Does.Not.Contain("?endDateProjection"));
        }
    }

    [Test]
    public void BuildCandidateProjection_ProjectsSeriesEndDate_WhenShowKind()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Show,
            Strategy = QueryStrategy.Structured,
            Filters = [],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder
            .BuildCandidateProjection(plan.EntityKind, ["Q1"], "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("(MIN(?endDateProjection) AS ?endDate)"));
            Assert.That(result, Does.Contain("(MAX(?hasEndDateValue) AS ?hasEndDate)"));
            Assert.That(result, Does.Contain("?item p:P582 ?dateStatementEndProjection."));
            Assert.That(result, Does.Contain("?item wdt:P582 ?endDateExists."));
            Assert.That(result, Does.Contain("AS ?endDateProjection)"));
            Assert.That(result, Does.Contain("AS ?publishDateProjection)"));
        }
    }

    [Test]
    public void BuildSearch_StripsOnlyCirrusQuotes_WhenTitleWithEmbeddedQuotes()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.TitleSearch,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = CriteriaValueOperator.ContainsString,
                    Values = ["The \"Matrix\""]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("inlabel:\\\"The Matrix\\\""));
            Assert.That(result, Does.Not.Contain("The \\\\\\\"Matrix"));
            Assert.That(result, Does.Contain("\"the \\\"matrix\\\"\""));
        }
    }

    [Test]
    public void BuildCandidateProjection_ProjectsDuration_WhenDurationFilterRequiresIt()
    {
        // Arrange
        var plan = new QueryPlan
        {
            EntityKind = EntityKind.Movie,
            Strategy = QueryStrategy.Structured,
            Filters =
            [
                new QueryFilter
                {
                    Kind = QueryFilterKind.Duration,
                    Operator = CriteriaValueOperator.GreaterThan,
                    DurationValues = [TimeSpan.FromHours(2)]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder
            .BuildCandidateProjection(plan.EntityKind, ["Q83495"], "en").QueryText;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("(MIN(?durationSecondsValue) AS ?durationSeconds)"));
            Assert.That(result, Does.Contain("?item p:P2047 ?durationStatementProjection."));
            Assert.That(result, Does.Contain("AS ?durationSecondsValue)"));
        }
    }

    [Test]
    public void BuildSearch_RendersQidAndPropertyAsOr_WhenRelationshipExternalIdGroup()
    {
        // Arrange
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
                            PropertyIds = ["P161"],
                            ExternalIdGroups =
                            [
                                new ExternalIdGroup
                                {
                                    Qids = ["Q43416"],
                                    ExternalIds =
                                    [
                                        new PropertyValue
                                        {
                                            PropertyId = "P345",
                                            Value = "nm0000206"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            Warnings = [],
            Page = 1
        };

        // Act
        var result = SparqlQueryBuilder.BuildSearch(plan, "en", 50).QueryText;

        // Assert
        Assert.That(
            result,
            Does.Contain(
                "            VALUES ?subject0_0 { wd:Q43416 }\n" +
                "          } UNION {\n" +
                "            ?subject0_0 wdt:P345 \"nm0000206\"."));
    }
}
