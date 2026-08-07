using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.Wikidata.QueryEngine;

namespace Magpie.Extensions.Wikidata.Mapping;

internal static class RelationshipMappingExtensions
{
    private static readonly IReadOnlyList<RelationshipProperty> PropertyCatalog =
    [
        new RelationshipProperty
        {
            PropertyId = Constants.CastMemberPropertyId,
            Group = RelationshipGroup.Cast,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "cast",
            Cap = 30,
            Roles = ["person", "cast", "performer"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.VoiceActorPropertyId,
            Group = RelationshipGroup.Cast,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "cast",
            Cap = 30,
            Roles = ["person", "cast", "performer"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.DirectorPropertyId,
            Group = RelationshipGroup.Director,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "director",
            Cap = 10,
            Roles = ["person", "crew", "director"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ScreenwriterPropertyId,
            Group = RelationshipGroup.Writer,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "writer",
            Cap = 10,
            Roles = ["person", "crew", "writer"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ProducerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "producer"],
            FixedQualifier = "Producer"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.CinematographerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "cinematographer"],
            FixedQualifier = "Director of Photography"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.FilmEditorPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "editor"],
            FixedQualifier = "Editor"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ComposerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "composer"],
            FixedQualifier = "Composer"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ExecutiveProducerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "executive producer"],
            FixedQualifier = "Executive Producer"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ProductionDesignerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "production designer"],
            FixedQualifier = "Production Designer"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.CostumeDesignerPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "costume designer"],
            FixedQualifier = "Costume Designer"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.NarratorPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "narrator"],
            FixedQualifier = "Narrator"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.PresenterPropertyId,
            Group = RelationshipGroup.Crew,
            SubjectKind = EntityKind.Person,
            RelationshipKind = "crew",
            Cap = 20,
            Roles = ["person", "crew", "presenter"],
            FixedQualifier = "Presenter"
        },
        new RelationshipProperty
        {
            PropertyId = Constants.ProductionCompanyPropertyId,
            Group = RelationshipGroup.Studio,
            SubjectKind = EntityKind.Studio,
            RelationshipKind = "studio",
            Cap = 12,
            Roles = ["studio"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.OriginalBroadcasterPropertyId,
            Group = RelationshipGroup.Network,
            SubjectKind = EntityKind.Studio,
            RelationshipKind = "network",
            Cap = 12,
            Roles = ["studio", "network"],
            UsesCharacterQualifier = true
        },
        new RelationshipProperty
        {
            PropertyId = Constants.DistributorPropertyId,
            Group = RelationshipGroup.Distributor,
            SubjectKind = EntityKind.Studio,
            RelationshipKind = "distributor",
            Cap = 12,
            Roles = ["studio", "distributor"],
            UsesCharacterQualifier = true
        }
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RoleProperties =
        BuildRoleProperties();

    public static bool TryGetRelationshipPropertyIds(
        this string role,
        out IReadOnlyList<string> propertyIds)
    {
        if (string.IsNullOrWhiteSpace(role) ||
            !RoleProperties.TryGetValue(role.Trim(), out var mapped))
        {
            propertyIds = [];
            return false;
        }

        propertyIds = mapped;
        return true;
    }

    public static IReadOnlyList<PendingRelationship> ToPendingRelationships(this EntityDto entity)
    {
        return BuildPending(entity, applyCaps: true, expandQualifierValues: false);
    }

    public static IReadOnlyList<PendingRelationship> ToEvaluationRelationships(this EntityDto entity)
    {
        return BuildPending(entity, applyCaps: false, expandQualifierValues: true);
    }

    private static IReadOnlyList<PendingRelationship> BuildPending(
        EntityDto entity,
        bool applyCaps,
        bool expandQualifierValues)
    {
        var result = new List<PendingRelationship>();
        var groupCounts = new Dictionary<RelationshipGroup, int>();
        foreach (var property in PropertyCatalog)
        {
            groupCounts.TryGetValue(property.Group, out var groupCount);
            foreach (var statement in entity.TruthyStatements(property.PropertyId))
            {
                if (applyCaps && groupCount >= property.Cap)
                {
                    break;
                }

                var qid = EntityId(statement.Mainsnak);
                if (qid is null)
                {
                    continue;
                }

                IReadOnlyList<string> qualifiers = property.FixedQualifier is null
                    ? []
                    : [property.FixedQualifier];
                IReadOnlyList<string> characterQids = [];
                if (property.UsesCharacterQualifier)
                {
                    qualifiers = QualifierStrings(statement, Constants.CharacterRoleNamePropertyId);
                    if (qualifiers.Count == 0)
                    {
                        characterQids = QualifierEntityIds(statement, Constants.CharacterRolePropertyId);
                    }
                }

                void Add(string? qualifier, string? characterQid)
                {
                    result.Add(new PendingRelationship
                    {
                        PropertyId = property.PropertyId,
                        SubjectQid = qid,
                        SubjectKind = property.SubjectKind,
                        RelationshipKind = property.RelationshipKind,
                        Qualifier = qualifier,
                        CharacterQid = characterQid
                    });
                }

                if (!expandQualifierValues)
                {
                    Add(qualifiers.FirstOrDefault(), characterQids.FirstOrDefault());
                }
                else if (qualifiers.Count > 0)
                {
                    foreach (var qualifier in qualifiers)
                    {
                        Add(qualifier, characterQid: null);
                    }
                }
                else if (characterQids.Count > 0)
                {
                    foreach (var characterQid in characterQids)
                    {
                        Add(qualifier: null, characterQid);
                    }
                }
                else
                {
                    Add(qualifier: null, characterQid: null);
                }

                groupCount++;
                groupCounts[property.Group] = groupCount;
            }
        }

        return result;
    }

    public static IReadOnlyList<string> ToSupportQids(
        this IEnumerable<PendingRelationship> relationships)
    {
        var pending = relationships.ToArray();
        var characterQids =
            from relationship in pending
            where relationship.CharacterQid is not null
            select relationship.CharacterQid!;
        return
        [
            .. pending.Select(relationship => relationship.SubjectQid)
                .Concat(characterQids)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static IReadOnlyList<StatementDto> TruthyStatements(this EntityDto entity, string propertyId)
    {
        if (entity.Claims is null || !entity.Claims.TryGetValue(propertyId, out var statements))
        {
            return [];
        }

        var usable = statements.Where(statement => statement.Rank != "deprecated" &&
            statement.Mainsnak?.DataValue is not null &&
            statement.Mainsnak.SnakType is null or "value").ToArray();
        return usable.Any(statement => statement.Rank == "preferred")
            ? usable.Where(statement => statement.Rank == "preferred").ToArray()
            : [.. usable.Where(statement => statement.Rank is null or "normal")];
    }

    public static IReadOnlyList<string> StringClaimValues(this EntityDto entity, string propertyId)
    {
        return
        [
            .. from statement in entity.TruthyStatements(propertyId)
                let value = StringValue(statement.Mainsnak)
                where value is not null
                select value!
        ];
    }

    public static IReadOnlyList<string> EntityClaimQids(this EntityDto entity, string propertyId)
    {
        return
        [
            .. from statement in entity.TruthyStatements(propertyId)
                let value = EntityId(statement.Mainsnak)
                where value is not null
                select value!
        ];
    }

    public static string? ResolveLabel(this EntityDto entity, string language) => entity.Labels.Resolve(language);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildRoleProperties()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var roles = PropertyCatalog.SelectMany(property => property.Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            result[role] =
            [
                .. PropertyCatalog.Where(property => property.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Select(property => property.PropertyId)
            ];
        }

        return result;
    }

    private static IReadOnlyList<string> QualifierStrings(StatementDto statement, string propertyId)
    {
        if (statement.Qualifiers is null || !statement.Qualifiers.TryGetValue(propertyId, out var values))
        {
            return [];
        }

        var strings =
            from value in values
            let text = StringValue(value)
            where text is not null
            select text!;
        return [.. strings.Distinct(StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> QualifierEntityIds(StatementDto statement, string propertyId)
    {
        if (statement.Qualifiers is null || !statement.Qualifiers.TryGetValue(propertyId, out var values))
        {
            return [];
        }

        var qids =
            from value in values
            let qid = EntityId(value)
            where qid is not null
            select qid!;
        return [.. qids.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string? StringValue(SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        return value is { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;
    }

    private static string? EntityId(SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        if (value is not { ValueKind: JsonValueKind.Object } element ||
            !element.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()))
        {
            return null;
        }

        var qid = id.GetString()!.Trim();
        if (qid.Length <= 1 ||
            qid[0] is not ('Q' or 'q') ||
            qid[1] == '0' ||
            qid.Skip(1).Any(value => value is < '0' or > '9'))
        {
            return null;
        }

        return qid[0] == 'q' ? "Q" + qid.Substring(1) : qid;
    }
}
