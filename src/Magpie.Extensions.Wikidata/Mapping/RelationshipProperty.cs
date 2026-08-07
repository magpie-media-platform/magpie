using System.Collections.Generic;

using Magpie.Extensions.Wikidata.QueryEngine;

namespace Magpie.Extensions.Wikidata.Mapping;

internal sealed record RelationshipProperty
{
    public required string PropertyId { get; init; }

    public required RelationshipGroup Group { get; init; }

    public required EntityKind SubjectKind { get; init; }

    public required string RelationshipKind { get; init; }

    public required int Cap { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public string? FixedQualifier { get; init; }

    public bool UsesCharacterQualifier { get; init; }
}
