using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualRelationshipFacet
{
    public ResidualStringCriterion? Criterion { get; init; }

    public IReadOnlyList<string> Qids { get; init; } = [];

    public IReadOnlyList<string> Names { get; init; } = [];

    public IReadOnlyList<PropertyValue> ExternalIds { get; init; } = [];
}
