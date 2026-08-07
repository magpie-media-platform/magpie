using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualFilter
{
    public required ResidualFilterKind Kind { get; init; }

    public required string Source { get; init; }

    public required ResidualHydration Hydration { get; init; }

    public ResidualTextField? TextField { get; init; }

    public ResidualStringCriterion? Criterion { get; init; }

    public IReadOnlyList<string> LanguageCodes { get; init; } = [];

    public IReadOnlyList<string> LanguageQids { get; init; } = [];

    public IReadOnlyList<string> LanguageNames { get; init; } = [];

    public ResidualRelationshipGroup? RelationshipGroup { get; init; }

    public ResidualCollectionGroup? CollectionGroup { get; init; }
}
