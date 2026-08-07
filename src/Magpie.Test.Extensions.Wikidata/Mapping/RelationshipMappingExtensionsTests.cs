using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata.Mapping;

[TestFixture]
internal sealed class RelationshipMappingExtensionsTests
{
    [Test]
    public void TryGetRelationshipPropertyIds_ReturnsCurrentCatalog_WhenRoleIsKnown()
    {
        // Act
        var found = " performer ".TryGetRelationshipPropertyIds(out var performer);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(performer, Is.EqualTo(["P161", "P725"]));
        }
    }

    [Test]
    public void ToPendingRelationships_ReturnsCurrentRelationships_WhenClaimsAreTruthy()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "claims": {
                "P161": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q1" } } },
                    "qualifiers": { "P4633": [{ "datavalue": { "value": " Lead " } }] }
                  }
                ],
                "P725": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q2" } } },
                    "qualifiers": { "P453": [{ "datavalue": { "value": { "id": "Q20" } } }] }
                  }
                ],
                "P57": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q3" } } } }],
                "P58": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q4" } } } }],
                "P162": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q5" } } } }],
                "P344": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q6" } } } }],
                "P1040": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q7" } } } }],
                "P86": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q8" } } } }],
                "P1431": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q9" } } } }],
                "P2554": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q10" } } } }],
                "P2515": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q11" } } } }],
                "P2438": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q12" } } } }],
                "P371": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q13" } } } }],
                "P272": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q14" } } } }],
                "P449": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q15" } } } }],
                "P750": [{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q16" } } } }]
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var result = entity.ToPendingRelationships();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Select(relationship => relationship.PropertyId),
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
                    "P371",
                    "P272",
                    "P449",
                    "P750"
                ]));
            Assert.That(
                result.Select(relationship => relationship.SubjectQid),
                Is.EqualTo(
                [
                    "Q1",
                    "Q2",
                    "Q3",
                    "Q4",
                    "Q5",
                    "Q6",
                    "Q7",
                    "Q8",
                    "Q9",
                    "Q10",
                    "Q11",
                    "Q12",
                    "Q13",
                    "Q14",
                    "Q15",
                    "Q16"
                ]));
            Assert.That(
                result.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(
                [
                    "cast",
                    "cast",
                    "director",
                    "writer",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "crew",
                    "studio",
                    "network",
                    "distributor"
                ]));
            Assert.That(
                result.Select(relationship => relationship.SubjectKind),
                Is.EqualTo(
                [
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Person,
                    EntityKind.Studio,
                    EntityKind.Studio,
                    EntityKind.Studio
                ]));
            Assert.That(
                result.Select(relationship => relationship.Qualifier),
                Is.EqualTo(
                [
                    "Lead",
                    null,
                    null,
                    null,
                    "Producer",
                    "Director of Photography",
                    "Editor",
                    "Composer",
                    "Executive Producer",
                    "Production Designer",
                    "Costume Designer",
                    "Narrator",
                    "Presenter",
                    null,
                    null,
                    null
                ]));
            Assert.That(
                result.Select(relationship => relationship.CharacterQid),
                Is.EqualTo(
                [
                    null, "Q20", null, null, null, null, null, null, null, null, null, null, null, null, null, null
                ]));
        }
    }

    [Test]
    public void ToPendingRelationships_PreservesStatementQualifiers_WhenNonCrewRelationshipsContainThem()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "claims": {
                "P57": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q1" } } },
                    "qualifiers": { "P4633": [{ "datavalue": { "value": "Director credit" } }] }
                  }
                ],
                "P58": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q2" } } },
                    "qualifiers": { "P453": [{ "datavalue": { "value": { "id": "Q20" } } }] }
                  }
                ],
                "P272": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q3" } } },
                    "qualifiers": { "P4633": [{ "datavalue": { "value": "Studio credit" } }] }
                  }
                ],
                "P449": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q4" } } },
                    "qualifiers": { "P4633": [{ "datavalue": { "value": "Network credit" } }] }
                  }
                ],
                "P750": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q5" } } },
                    "qualifiers": { "P453": [{ "datavalue": { "value": { "id": "Q21" } } }] }
                  }
                ]
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var result = entity.ToPendingRelationships();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Select(relationship => relationship.Qualifier),
                Is.EqualTo(
                [
                    "Director credit", null, "Studio credit", "Network credit", null
                ]));
            Assert.That(
                result.Select(relationship => relationship.CharacterQid),
                Is.EqualTo(
                [
                    null, "Q20", null, null, "Q21"
                ]));
        }
    }

    [Test]
    public void ToPendingRelationships_ReturnsPreferredAsciiQids_WhenRanksAndIdsVary()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "claims": {
                "P161": [
                  { "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q1" } } } },
                  { "rank": "preferred", "mainsnak": { "datavalue": { "value": { "id": "q2" } } } },
                  { "rank": "preferred", "mainsnak": { "datavalue": { "value": { "id": "Q0" } } } },
                  { "rank": "preferred", "mainsnak": { "datavalue": { "value": { "id": "Q\u0661" } } } },
                  { "rank": "deprecated", "mainsnak": { "datavalue": { "value": { "id": "Q3" } } } },
                  {
                    "rank": "preferred",
                    "mainsnak": { "snaktype": "somevalue", "datavalue": { "value": { "id": "Q4" } } }
                  }
                ]
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var pending = entity.ToPendingRelationships();
        var claimQids = entity.EntityClaimQids("P161");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pending.Select(relationship => relationship.SubjectQid), Is.EqualTo(["Q2"]));
            Assert.That(claimQids, Is.EqualTo(["Q2"]));
            Assert.That(entity.TruthyStatements("P161"), Has.Count.EqualTo(3));
        }
    }

    [Test]
    public void ToPendingRelationships_EnforcesSharedCaps_WhenGroupsExceedLimits()
    {
        // Arrange
        var statement = JsonSerializer.Deserialize<StatementDto>(
            """{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q1" } } } }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var entity = new EntityDto
        {
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P161"] = [.. Enumerable.Repeat(statement, 31)],
                ["P725"] = [.. Enumerable.Repeat(statement, 1)],
                ["P57"] = [.. Enumerable.Repeat(statement, 11)],
                ["P58"] = [.. Enumerable.Repeat(statement, 11)],
                ["P162"] = [.. Enumerable.Repeat(statement, 10)],
                ["P344"] = [.. Enumerable.Repeat(statement, 3)],
                ["P1040"] = [.. Enumerable.Repeat(statement, 2)],
                ["P86"] = [.. Enumerable.Repeat(statement, 1)],
                ["P1431"] = [.. Enumerable.Repeat(statement, 2)],
                ["P2554"] = [.. Enumerable.Repeat(statement, 3)],
                ["P2515"] = [.. Enumerable.Repeat(statement, 1)],
                ["P2438"] = [.. Enumerable.Repeat(statement, 1)],
                ["P371"] = [.. Enumerable.Repeat(statement, 1)],
                ["P272"] = [.. Enumerable.Repeat(statement, 13)],
                ["P449"] = [.. Enumerable.Repeat(statement, 13)],
                ["P750"] = [.. Enumerable.Repeat(statement, 13)]
            }
        };

        // Act
        var result = entity.ToPendingRelationships();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count(relationship => relationship.PropertyId == "P161"), Is.EqualTo(30));
            Assert.That(
                result,
                Has.None.Matches<PendingRelationship>(relationship =>
                    relationship.PropertyId == "P725"));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P57"), Is.EqualTo(10));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P58"), Is.EqualTo(10));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P162"), Is.EqualTo(10));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P344"), Is.EqualTo(3));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P1040"), Is.EqualTo(2));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P86"), Is.EqualTo(1));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P1431"), Is.EqualTo(2));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P2554"), Is.EqualTo(2));
            Assert.That(
                result,
                Has.None.Matches<PendingRelationship>(relationship =>
                    relationship.PropertyId is "P2515" or "P2438" or "P371"));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P272"), Is.EqualTo(12));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P449"), Is.EqualTo(12));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P750"), Is.EqualTo(12));
        }
    }

    [Test]
    public void ToEvaluationRelationships_ReturnsAllRelationships_WhenDisplayCapsWouldTrimResults()
    {
        // Arrange
        var statement = JsonSerializer.Deserialize<StatementDto>(
            """{ "rank": "normal", "mainsnak": { "datavalue": { "value": { "id": "Q1" } } } }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var entity = new EntityDto
        {
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P161"] = [.. Enumerable.Repeat(statement, 31)],
                ["P725"] = [.. Enumerable.Repeat(statement, 1)]
            }
        };

        // Act
        var result = entity.ToEvaluationRelationships();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count(relationship => relationship.PropertyId == "P161"), Is.EqualTo(31));
            Assert.That(result.Count(relationship => relationship.PropertyId == "P725"), Is.EqualTo(1));
        }
    }

    [Test]
    public void ToEvaluationRelationships_ExpandsEveryQualifier_WhenStatementHasMultipleValues()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "claims": {
                "P161": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q1" } } },
                    "qualifiers": {
                      "P4633": [
                        { "datavalue": { "value": "Hero" } },
                        { "datavalue": { "value": "Villain" } }
                      ]
                    }
                  }
                ],
                "P725": [
                  {
                    "rank": "normal",
                    "mainsnak": { "datavalue": { "value": { "id": "Q2" } } },
                    "qualifiers": {
                      "P453": [
                        { "datavalue": { "value": { "id": "Q10" } } },
                        { "datavalue": { "value": { "id": "Q11" } } }
                      ]
                    }
                  }
                ]
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var display = entity.ToPendingRelationships();
        var evaluation = entity.ToEvaluationRelationships();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(display.Select(value => value.Qualifier), Is.EqualTo(["Hero", null]));
            Assert.That(display.Select(value => value.CharacterQid), Is.EqualTo([null, "Q10"]));
            Assert.That(
                evaluation.Select(value => value.Qualifier),
                Is.EqualTo(["Hero", "Villain", null, null]));
            Assert.That(
                evaluation.Select(value => value.CharacterQid),
                Is.EqualTo([null, null, "Q10", "Q11"]));
        }
    }

    [Test]
    public void ToSupportQids_ReturnsDistinctSubjectsBeforeCharacters_WhenRelationshipsOverlap()
    {
        // Arrange
        var relationships = new[]
        {
            new PendingRelationship
            {
                PropertyId = "P161",
                SubjectQid = "Q1",
                SubjectKind = EntityKind.Person,
                RelationshipKind = "cast",
                CharacterQid = "Q3"
            },
            new PendingRelationship
            {
                PropertyId = "P725",
                SubjectQid = "Q2",
                SubjectKind = EntityKind.Person,
                RelationshipKind = "cast",
                CharacterQid = "Q1"
            },
            new PendingRelationship
            {
                PropertyId = "P161",
                SubjectQid = "q1",
                SubjectKind = EntityKind.Person,
                RelationshipKind = "cast",
                CharacterQid = "Q4"
            }
        };

        // Act
        var result = relationships.ToSupportQids();

        // Assert
        Assert.That(result, Is.EqualTo(["Q1", "Q2", "Q3", "Q4"]));
    }

    [Test]
    public void ResolveLabel_ReturnsPreferredThenFallback_WhenLabelsExist()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "labels": {
                "de": { "language": "de", "value": " " },
                "fr": { "language": "fr", "value": " Francais " },
                "en": { "language": "en", "value": " English " }
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var preferred = entity.ResolveLabel("fr");
        var fallback = entity.ResolveLabel("de");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preferred, Is.EqualTo("Francais"));
            Assert.That(fallback, Is.EqualTo("English"));
        }
    }

    [Test]
    public void StringClaimValues_ReturnsExactTruthyStrings_WhenClaimsContainOtherValues()
    {
        // Arrange
        var entity = JsonSerializer.Deserialize<EntityDto>(
            """
            {
              "claims": {
                "P345": [
                  { "rank": "normal", "mainsnak": { "datavalue": { "value": "old" } } },
                  { "rank": "preferred", "mainsnak": { "datavalue": { "value": " selected " } } },
                  { "rank": "preferred", "mainsnak": { "snaktype": "somevalue", "datavalue": { "value": "ignored" } } }
                ],
                "P4947": [
                  { "rank": "preferred", "mainsnak": { "datavalue": { "value": "other" } } }
                ]
              }
            }
            """,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Act
        var result = entity.StringClaimValues("P345");

        // Assert
        Assert.That(result, Is.EqualTo(["selected"]));
    }
}
