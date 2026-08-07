using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record ExternalIdGroup
{
    public IReadOnlyList<string> Qids { get; init; } = [];

    public IReadOnlyList<PropertyValue> ExternalIds { get; init; } = [];
}
