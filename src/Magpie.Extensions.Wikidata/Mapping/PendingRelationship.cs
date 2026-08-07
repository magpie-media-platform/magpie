using Magpie.Extensions.Wikidata.QueryEngine;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record PendingRelationship
{
    public required string PropertyId { get; init; }

    public required string SubjectQid { get; init; }

    public required EntityKind SubjectKind { get; init; }

    public required string RelationshipKind { get; init; }

    public string? Qualifier { get; init; }

    public string? CharacterQid { get; init; }
}
