using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ResidualRelationshipAlternative
{
    public required IReadOnlyList<string> PropertyIds { get; init; }

    public IReadOnlyList<ResidualRelationshipFacet> Subjects { get; init; } = [];

    public IReadOnlyList<ResidualRelationshipFacet> Qualifiers { get; init; } = [];

    public IReadOnlyList<ResidualRelationshipFacet> ExternalIdGroups { get; init; } = [];
}
